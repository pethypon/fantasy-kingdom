using System.Collections;
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
    public int CompletedDepth { get; private set; }
    int _nodesEvaluated;
    int _pruned;
    float _elapsedMs;

    // ---- 時間制限 ----
    const float DefaultTimeBudgetMs = 5000f;
    float _timeBudgetMs;
    Stopwatch _stopwatch;
    Stopwatch _wallClock;
    double SearchElapsedMs => System.Math.Max(_stopwatch.Elapsed.TotalMilliseconds, _wallClock.Elapsed.TotalMilliseconds);

    // ---- キラームーブ (深さごとに最善だった行動を記録) ----
    SimAction[] _killerMoves;
    readonly SimActionBuffer greedyActions = new SimActionBuffer();
    readonly Dictionary<int,SimActionBuffer> depthActions = new Dictionary<int,SimActionBuffer>();
    SimActionBuffer ActionsAt(int depth) { if(!depthActions.TryGetValue(depth,out var buffer)) depthActions[depth]=buffer=new SimActionBuffer();return buffer; }
    void StoreKiller(int depth, SimAction action) { if(_killerMoves[depth]==null) _killerMoves[depth]=new SimAction();_killerMoves[depth].CopyFrom(action); }

    // ---- トランスポジションテーブル (盤面ハッシュ → 評価値キャッシュ) ----
    Dictionary<long, TTEntry> _transTable;
    const int MaxTTSize = 32768;

    // ---- 再利用バッファ（GC削減） ----
    readonly HashSet<int> _greedyActedUnits = new HashSet<int>();
    readonly List<float> _sortScoreBuffer = new List<float>();
    readonly List<SimAction> _mergeActions = new List<SimAction>();
    readonly List<float> _mergeScores = new List<float>();

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
    float _nodeScore;
    bool _searchActive;
    public AISlicedWork BeginSearch(List<AIAction> candidates, SimBoardState initialBoard,
        AIBoardState realBoard, Dictionary<AIAction, float> result, float? timeBudgetMs = null)
    {
        if (candidates == null) throw new System.ArgumentNullException(nameof(candidates));
        if (initialBoard == null) throw new System.ArgumentNullException(nameof(initialBoard));
        if (result == null) throw new System.ArgumentNullException(nameof(result));
        if (_searchActive) throw new System.InvalidOperationException("Search already active");
        _searchActive = true;
        if (timeBudgetMs.HasValue) _timeBudgetMs = Mathf.Max(0, timeBudgetMs.Value);
        result.Clear();
        _stopwatch = new Stopwatch();
        _wallClock = Stopwatch.StartNew();
        return new AISlicedWork(OwnedSearch(candidates, initialBoard, realBoard, result), _stopwatch, () => _searchActive = false);
    }

    IEnumerator OwnedSearch(List<AIAction> candidates, SimBoardState initialBoard,
        AIBoardState realBoard, Dictionary<AIAction, float> result)
    {
        try { yield return SearchSteps(candidates, initialBoard, realBoard, result); }
        finally { _searchActive = false; }
    }

    // Compatibility for offline tools/tests. Gameplay uses BeginSearch and bounded Step calls.
    public Dictionary<AIAction, float> Search(List<AIAction> candidates,
        SimBoardState initialBoard, AIBoardState realBoard)
    {
        var result = new Dictionary<AIAction, float>(candidates.Count);
        using (var work = BeginSearch(candidates, initialBoard, realBoard, result))
            while (work.Step(double.PositiveInfinity)) { }
        return result;
    }

    IEnumerator SearchSteps(
        List<AIAction> candidates,
        SimBoardState initialBoard,
        AIBoardState realBoard, Dictionary<AIAction, float> result)
    {
        _nodesEvaluated = 0;
        CompletedDepth = 0;
        _pruned = 0;
        _transTable.Clear();
        System.Array.Clear(_killerMoves, 0, _killerMoves.Length);
        yield return null;

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

        var indices = new int[convertedCandidates.Count];
        var completedScores = new float[candidateScores.Length];
        for (int iterDepth = Mathf.Min(1, _maxDepth); iterDepth <= _maxDepth; iterDepth++)
        {
            if (SearchElapsedMs >= _timeBudgetMs * 0.9f) break;

            // 前回のスコアで降順ソート（最善手を先に探索）
            for (int i = 0; i < indices.Length; i++) indices[i] = i;
            System.Array.Sort(indices, (a, b) => candidateScores[b].CompareTo(candidateScores[a]));

            System.Array.Copy(candidateScores, completedScores, candidateScores.Length);
            bool completed = true;
            float alpha = float.MinValue;
            float beta = float.MaxValue;

            for (int ii = 0; ii < indices.Length; ii++)
            {
                int idx = indices[ii];
                var (candidate, simAction, _) = convertedCandidates[idx];

                if (SearchElapsedMs >= _timeBudgetMs)
                { completed = false; break; }

                if (simAction == null)
                {
                    candidateScores[idx] = baseScore;
                    continue;
                }

                // 盤面をクローンして最初の行動を適用
                yield return null;
                var boardAfterAction = initialBoard.Clone();
                try
                {
                if (!boardAfterAction.ApplyAction(simAction))
                {
                    candidateScores[idx] = baseScore;
                    continue;
                }

                // 残りのAIターンをgreedyに実行
                yield return SimulateGreedyTurnSteps(boardAfterAction, Team.Enemy);

                // 深さ2以降の探索
                float score;
                if (iterDepth >= 2)
                {
                    yield return MinSearchSteps(boardAfterAction, 2, iterDepth, alpha, beta);
                    score = _nodeScore;
                }
                else
                {
                    score = SimBoardEvaluator.Evaluate(boardAfterAction);
                    _nodesEvaluated++;
                }

                candidateScores[idx] = score;

                if (score > alpha) alpha = score;
                }
                finally { SimBoardPool.ReturnBoard(boardAfterAction); }
            }
            if (!completed || SearchElapsedMs >= _timeBudgetMs)
            { System.Array.Copy(completedScores, candidateScores, candidateScores.Length); break; }
            CompletedDepth = iterDepth;
        }

        // 結果をDictionaryに変換
        for (int i = 0; i < convertedCandidates.Count; i++)
        {
            var (candidate, _, _) = convertedCandidates[i];
            float lookaheadDelta = CompletedDepth == 0 ? 0f : candidateScores[i] - baseScore;
            result[candidate] = lookaheadDelta;
        }

        _stopwatch.Stop();
        _elapsedMs = _stopwatch.ElapsedMilliseconds;

        DevelopmentLog.Log($"[AIMinimaxEngine] 探索完了: 設定深さ{_maxDepth} 完了深さ{CompletedDepth} " +
            $"評価{_nodesEvaluated}ノード 枝刈り{_pruned}回 TT{_transTable.Count}件 " +
            $"{_elapsedMs:F0}ms 基準値={baseScore:F1}");

        yield break;
    }
}
