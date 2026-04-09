using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

// =====================================================================
//  AIMinimaxEngine — 3手先探索エンジン (Minimax + Alpha-Beta 枝刈り)
//
//  探索構造:
//    深さ1 (Max): AI(Enemy)のターン — 最善手を選ぶ
//    深さ2 (Min): Player のターン — AIにとって最悪の応手を想定
//    深さ3 (Max): AI(Enemy)の再応手 — 最善の反撃を選ぶ
//
//  各深さでは1ターン分の「行動列」をシミュレーションする:
//    ・APが尽きるまで貪欲に行動を選択・実行
//    ・ただし深さ1のみ「最初の1手」を候補として分岐する
//
//  改善点:
//    ・反復深化 (Iterative Deepening) — 浅い探索で手順序を最適化
//    ・キラームーブ — 兄弟ノードで有効だった手を優先
//    ・ターン遷移シミュレーション — AP/疲労リセット、DoT、クールダウン
//    ・MinSearchの手順序修正 — Playerの最善手（高QuickScore）を先に探索
//    ・ActorTeamの正確な設定
//
//  実装は以下の partial ファイルに分離されている:
//    - AIMinimaxEngine.Zobrist.cs  Zobristハッシュ (TT 用)
//    - AIMinimaxEngine.Search.cs   Min/Max 再帰探索
//    - AIMinimaxEngine.Greedy.cs   貪欲ターンシミュ + ソート
//    - AIMinimaxEngine.Convert.cs  AIAction ↔ SimAction 変換
// =====================================================================
public partial class AIMinimaxEngine
{
    // ---- 設定 ----
    readonly int _maxDepth;
    readonly int _candidateLimit;         // 各深さの候補数上限
    readonly int _greedyActionsPerTurn;   // 1ターンのgreedy行動回数上限

    // ---- 統計 ----
    int _nodesEvaluated;
    int _pruned;
    float _elapsedMs;

    // ---- 時間制限 ----
    const float DefaultTimeBudgetMs = 5000f;
    float _timeBudgetMs;
    Stopwatch _stopwatch;

    // ---- キラームーブ (深さごとに最善だった行動を記録) ----
    SimAction[] _killerMoves;

    // ---- トランスポジションテーブル (盤面ハッシュ → 評価値キャッシュ) ----
    Dictionary<long, TTEntry> _transTable;
    const int MaxTTSize = 32768;

    // ---- 再利用バッファ（GC削減） ----
    readonly HashSet<int> _greedyActedUnits = new HashSet<int>();
    readonly List<float> _sortScoreBuffer = new List<float>();

    struct TTEntry
    {
        public float Score;
        public int Depth;
        public TTFlag Flag; // Exact, LowerBound, UpperBound
    }

    enum TTFlag { Exact, LowerBound, UpperBound }

    public AIMinimaxEngine(int maxDepth = 3, int candidateLimit = 14,
        int greedyActionsPerTurn = 10, float timeBudgetMs = DefaultTimeBudgetMs)
    {
        _maxDepth = Mathf.Clamp(maxDepth, 1, 20);
        _candidateLimit = Mathf.Max(4, candidateLimit);
        _greedyActionsPerTurn = Mathf.Clamp(greedyActionsPerTurn, 4, 15);
        _timeBudgetMs = timeBudgetMs;
        _killerMoves = new SimAction[_maxDepth + 1];
        _transTable = new Dictionary<long, TTEntry>(MaxTTSize);
    }

    // ================================================================
    //  メイン探索: 反復深化 + Alpha-Beta
    //  入力: AIの候補行動リスト (AIAction) と現在の盤面状態
    //  出力: 各AIActionに対する先読みスコア補正値
    // ================================================================
    public Dictionary<AIAction, float> Search(
        List<AIAction> candidates,
        SimBoardState initialBoard,
        AIBoardState realBoard)
    {
        var result = new Dictionary<AIAction, float>();
        _nodesEvaluated = 0;
        _pruned = 0;
        _stopwatch = Stopwatch.StartNew();

        // 初期盤面の基準スコア
        float baseScore = SimBoardEvaluator.Evaluate(initialBoard);

        // 候補をSimActionに事前変換
        var convertedCandidates = new List<(AIAction action, SimAction sim, float quickScore)>();
        foreach (var c in candidates)
        {
            var simAction = ConvertToSimAction(c, initialBoard);
            float qs = simAction != null ? SimActionGenerator.QuickScore(simAction, initialBoard) : float.MinValue;
            convertedCandidates.Add((c, simAction, qs));
        }

        // 反復深化: 深さ1から_maxDepthまで段階的に探索
        // 浅い探索の結果で手順序を最適化し、深い探索のAlpha-Beta効率を上げる
        float[] candidateScores = new float[convertedCandidates.Count];
        for (int i = 0; i < candidateScores.Length; i++)
            candidateScores[i] = convertedCandidates[i].quickScore;

        for (int iterDepth = Mathf.Min(1, _maxDepth); iterDepth <= _maxDepth; iterDepth++)
        {
            if (_stopwatch.ElapsedMilliseconds > _timeBudgetMs * 0.9f) break;

            // 前回のスコアで降順ソート（最善手を先に探索）
            var indices = new int[convertedCandidates.Count];
            for (int i = 0; i < indices.Length; i++) indices[i] = i;
            System.Array.Sort(indices, (a, b) => candidateScores[b].CompareTo(candidateScores[a]));

            float alpha = float.MinValue;
            float beta = float.MaxValue;

            for (int ii = 0; ii < indices.Length; ii++)
            {
                int idx = indices[ii];
                var (candidate, simAction, _) = convertedCandidates[idx];

                if (_stopwatch.ElapsedMilliseconds > _timeBudgetMs)
                    break;

                if (simAction == null)
                {
                    candidateScores[idx] = baseScore;
                    continue;
                }

                // 盤面をクローンして最初の行動を適用
                var boardAfterAction = initialBoard.Clone();
                if (!boardAfterAction.ApplyAction(simAction))
                {
                    SimBoardPool.ReturnBoard(boardAfterAction);
                    candidateScores[idx] = baseScore;
                    continue;
                }

                // 残りのAIターンをgreedyに実行
                SimulateGreedyTurn(boardAfterAction, Team.Enemy);

                // 深さ2以降の探索
                float score;
                if (iterDepth >= 2)
                {
                    score = MinSearch(boardAfterAction, 2, iterDepth, alpha, beta);
                }
                else
                {
                    score = SimBoardEvaluator.Evaluate(boardAfterAction);
                    _nodesEvaluated++;
                }

                SimBoardPool.ReturnBoard(boardAfterAction);
                candidateScores[idx] = score;

                if (score > alpha) alpha = score;
            }
        }

        // 結果をDictionaryに変換
        for (int i = 0; i < convertedCandidates.Count; i++)
        {
            var (candidate, _, _) = convertedCandidates[i];
            float lookaheadDelta = candidateScores[i] - baseScore;
            result[candidate] = lookaheadDelta;
        }

        _stopwatch.Stop();
        _elapsedMs = _stopwatch.ElapsedMilliseconds;

        Debug.Log($"[AIMinimaxEngine] 探索完了: 深さ{_maxDepth} " +
            $"評価{_nodesEvaluated}ノード 枝刈り{_pruned}回 TT{_transTable.Count}件 " +
            $"{_elapsedMs:F0}ms 基準値={baseScore:F1}");

        return result;
    }
}
