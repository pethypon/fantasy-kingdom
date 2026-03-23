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
//  最適化:
//    ・Alpha-Beta枝刈りで無駄なノードを探索しない
//    ・候補行動をQuickScoreで事前ソートし、良い手から探索
//    ・各深さで候補数を制限 (深さ1: 全候補, 深さ2-3: 上位N手)
//    ・時間制限チェック付き
//
//  盤面シミュレーション:
//    ・SimBoardState上で実際に行動を適用し、結果を評価
//    ・建築・召喚・戦闘・視野拡張すべてシミュレーション対象
// =====================================================================
public class AIMinimaxEngine
{
    // ---- 設定 ----
    readonly int _maxDepth;
    readonly int _candidateLimit;         // 各深さの候補数上限
    readonly int _greedyActionsPerTurn;   // 1ターンのgreedy行動回数上限

    // ---- 統計 ----
    int _nodesEvaluated;
    int _pruned;
    float _elapsedMs;

    // ---- 時間制限 (ms) ----
    // ユーザーは「1時間まで」と言ったが、Unity上で長すぎるとフリーズするため
    // 実用的な範囲で設定。十分な精度を保ちつつ数秒で完了する。
    const float DefaultTimeBudgetMs = 30000f; // 30秒 (正確性重視)
    float _timeBudgetMs;
    Stopwatch _stopwatch;

    public AIMinimaxEngine(int maxDepth = 3, int candidateLimit = 12,
        int greedyActionsPerTurn = 8, float timeBudgetMs = DefaultTimeBudgetMs)
    {
        _maxDepth = Mathf.Clamp(maxDepth, 1, 3);
        _candidateLimit = Mathf.Max(3, candidateLimit);
        _greedyActionsPerTurn = Mathf.Clamp(greedyActionsPerTurn, 3, 15);
        _timeBudgetMs = timeBudgetMs;
    }

    // ================================================================
    //  メイン探索: 最善の行動列スコアを返す
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

        float alpha = float.MinValue;
        float beta = float.MaxValue;

        // 初期盤面の基準スコア
        float baseScore = SimBoardEvaluator.Evaluate(initialBoard);

        // 候補をQuickScoreで事前ソート (良い手から探索 → alpha-beta効率UP)
        var scoredCandidates = new List<(AIAction action, float quickScore)>();
        foreach (var c in candidates)
        {
            var simAction = ConvertToSimAction(c, initialBoard);
            float qs = simAction != null ? SimActionGenerator.QuickScore(simAction, initialBoard) : 0f;
            scoredCandidates.Add((c, qs));
        }
        scoredCandidates.Sort((a, b) => b.quickScore.CompareTo(a.quickScore));

        foreach (var (candidate, _) in scoredCandidates)
        {
            // 時間チェック
            if (_stopwatch.ElapsedMilliseconds > _timeBudgetMs)
            {
                Debug.Log($"[AIMinimaxEngine] 時間切れ ({_stopwatch.ElapsedMilliseconds}ms) " +
                    $"— 残り{candidates.Count - result.Count}候補は未評価");
                break;
            }

            var simAction = ConvertToSimAction(candidate, initialBoard);
            if (simAction == null)
            {
                result[candidate] = 0f;
                continue;
            }

            // 盤面をクローンして最初の行動を適用
            var boardAfterAction = initialBoard.Clone();
            if (!boardAfterAction.ApplyAction(simAction))
            {
                result[candidate] = 0f;
                continue;
            }

            // 残りのAIターンをgreedyに実行
            SimulateGreedyTurn(boardAfterAction, Team.Enemy);

            // 深さ2以降の探索
            float score;
            if (_maxDepth >= 2)
            {
                // 深さ2: Playerの最善応答 (Min)
                score = MinSearch(boardAfterAction, 2, alpha, beta);
            }
            else
            {
                score = SimBoardEvaluator.Evaluate(boardAfterAction);
                _nodesEvaluated++;
            }

            // 基準スコアとの差分を先読みスコアとして返す
            float lookaheadDelta = score - baseScore;
            result[candidate] = lookaheadDelta;

            // Alpha更新
            if (score > alpha) alpha = score;
        }

        _stopwatch.Stop();
        _elapsedMs = _stopwatch.ElapsedMilliseconds;

        Debug.Log($"[AIMinimaxEngine] 探索完了: 深さ{_maxDepth} " +
            $"評価{_nodesEvaluated}ノード 枝刈り{_pruned}回 " +
            $"{_elapsedMs:F0}ms 基準値={baseScore:F1}");

        return result;
    }

    // ================================================================
    //  Min探索 (Playerの応手): AIにとって最悪のスコアを返す
    // ================================================================
    float MinSearch(SimBoardState board, int depth, float alpha, float beta)
    {
        if (_stopwatch.ElapsedMilliseconds > _timeBudgetMs)
            return SimBoardEvaluator.Evaluate(board);

        // ゲーム終了チェック
        var eCrystal = board.GetCrystal(Team.Enemy);
        var pCrystal = board.GetCrystal(Team.Player);
        if ((eCrystal != null && !eCrystal.IsAlive) || (pCrystal != null && !pCrystal.IsAlive))
        {
            _nodesEvaluated++;
            return SimBoardEvaluator.Evaluate(board);
        }

        // Playerのターンをシミュレーション
        // Player APを概算リセット
        board.PlayerAP = 20;

        // Playerの候補行動を生成
        var actions = SimActionGenerator.GenerateAllActions(board, Team.Player);
        if (actions.Count == 0)
        {
            // Playerが行動不能 → 現状のまま
            if (depth < _maxDepth)
                return MaxSearch(board, depth + 1, alpha, beta);
            _nodesEvaluated++;
            return SimBoardEvaluator.Evaluate(board);
        }

        // QuickScoreでソート (Playerにとって良い手 = AIにとって悪い手)
        actions.Sort((a, b) =>
        {
            float sa = SimActionGenerator.QuickScore(a, board);
            float sb = SimActionGenerator.QuickScore(b, board);
            // Playerは自分に有利な手を選ぶ = AIのスコアが低くなる手
            // QuickScoreはEnemy視点なので、Playerの良い手は低スコア
            return sa.CompareTo(sb); // 昇順 (AIにとって悪い手が先)
        });

        // 上位候補に絞る
        int limit = Mathf.Min(_candidateLimit, actions.Count);

        float minScore = float.MaxValue;

        for (int i = 0; i < limit; i++)
        {
            if (_stopwatch.ElapsedMilliseconds > _timeBudgetMs) break;

            var boardCopy = board.Clone();
            boardCopy.ApplyAction(actions[i]);

            // Playerの残りターンをgreedyに実行
            SimulateGreedyTurn(boardCopy, Team.Player);

            float score;
            if (depth < _maxDepth)
            {
                // 次の深さ (Max: AIの再応手)
                score = MaxSearch(boardCopy, depth + 1, alpha, beta);
            }
            else
            {
                score = SimBoardEvaluator.Evaluate(boardCopy);
                _nodesEvaluated++;
            }

            if (score < minScore) minScore = score;

            // Beta枝刈り
            if (score <= alpha)
            {
                _pruned++;
                break;
            }
            if (score < beta) beta = score;
        }

        return minScore == float.MaxValue ? SimBoardEvaluator.Evaluate(board) : minScore;
    }

    // ================================================================
    //  Max探索 (AIの再応手): AIにとって最善のスコアを返す
    // ================================================================
    float MaxSearch(SimBoardState board, int depth, float alpha, float beta)
    {
        if (_stopwatch.ElapsedMilliseconds > _timeBudgetMs)
            return SimBoardEvaluator.Evaluate(board);

        // ゲーム終了チェック
        var eCrystal = board.GetCrystal(Team.Enemy);
        var pCrystal = board.GetCrystal(Team.Player);
        if ((eCrystal != null && !eCrystal.IsAlive) || (pCrystal != null && !pCrystal.IsAlive))
        {
            _nodesEvaluated++;
            return SimBoardEvaluator.Evaluate(board);
        }

        // AIのターンをシミュレーション
        // AP概算リセット
        board.EnemyAP = 20;

        var actions = SimActionGenerator.GenerateAllActions(board, Team.Enemy);
        if (actions.Count == 0)
        {
            _nodesEvaluated++;
            return SimBoardEvaluator.Evaluate(board);
        }

        // QuickScoreでソート (降順: AIに有利な手が先)
        actions.Sort((a, b) =>
        {
            float sa = SimActionGenerator.QuickScore(a, board);
            float sb = SimActionGenerator.QuickScore(b, board);
            return sb.CompareTo(sa);
        });

        int limit = Mathf.Min(_candidateLimit, actions.Count);

        float maxScore = float.MinValue;

        for (int i = 0; i < limit; i++)
        {
            if (_stopwatch.ElapsedMilliseconds > _timeBudgetMs) break;

            var boardCopy = board.Clone();
            boardCopy.ApplyAction(actions[i]);

            // AIの残りターンをgreedyに実行
            SimulateGreedyTurn(boardCopy, Team.Enemy);

            float score;
            if (depth < _maxDepth)
            {
                score = MinSearch(boardCopy, depth + 1, alpha, beta);
            }
            else
            {
                score = SimBoardEvaluator.Evaluate(boardCopy);
                _nodesEvaluated++;
            }

            if (score > maxScore) maxScore = score;

            // Alpha枝刈り
            if (score >= beta)
            {
                _pruned++;
                break;
            }
            if (score > alpha) alpha = score;
        }

        return maxScore == float.MinValue ? SimBoardEvaluator.Evaluate(board) : maxScore;
    }

    // ================================================================
    //  貪欲ターンシミュレーション
    //  あるチームの1ターン分をgreedy(最高スコアの行動を順次実行)で完了する
    // ================================================================
    void SimulateGreedyTurn(SimBoardState board, Team team)
    {
        var actedUnits = new HashSet<int>();

        for (int step = 0; step < _greedyActionsPerTurn; step++)
        {
            int ap = board.GetAP(team);
            if (ap <= 0) break;

            var actions = SimActionGenerator.GenerateAllActions(board, team);
            if (actions.Count == 0) break;

            // 最高スコアの行動を選択
            SimAction best = null;
            float bestScore = float.MinValue;

            for (int i = 0; i < actions.Count; i++)
            {
                var a = actions[i];
                if (a.APCost > ap) continue;

                float score = SimActionGenerator.QuickScore(a, board);

                // 既に行動した駒は割引
                if (a.UnitId >= 0 && actedUnits.Contains(a.UnitId))
                    score *= 0.5f;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = a;
                }
            }

            if (best == null) break;

            board.ApplyAction(best);
            if (best.UnitId >= 0)
                actedUnits.Add(best.UnitId);

            // ゲーム終了チェック
            var eCrystal = board.GetCrystal(Team.Enemy);
            var pCrystal = board.GetCrystal(Team.Player);
            if ((eCrystal != null && !eCrystal.IsAlive) || (pCrystal != null && !pCrystal.IsAlive))
                break;
        }
    }

    // ================================================================
    //  AIAction → SimAction 変換
    //  実際のAIAction (GameObjectベース) をSimAction (IDベース) に変換
    // ================================================================
    SimAction ConvertToSimAction(AIAction aiAction, SimBoardState board)
    {
        var sim = new SimAction();
        sim.APCost = aiAction.APCost;

        switch (aiAction.ActionType)
        {
            case AIActionType.Move:
            case AIActionType.Retreat:
            case AIActionType.Support:
            case AIActionType.Surround:
            case AIActionType.DefenseRepos:
                sim.Type = SimActionType.Move;
                sim.UnitId = FindSimUnitId(aiAction.Unit, board);
                sim.TargetPos = SimBoardState.ToCell(aiAction.TargetPos);
                if (sim.UnitId < 0) return null;
                return sim;

            case AIActionType.Attack:
                sim.Type = SimActionType.Attack;
                sim.UnitId = FindSimUnitId(aiAction.Unit, board);
                sim.TargetUnitId = FindSimUnitId(aiAction.TargetUnit, board);
                sim.TargetPos = SimBoardState.ToCell(aiAction.TargetPos);
                if (sim.UnitId < 0 || sim.TargetUnitId < 0) return null;
                return sim;

            case AIActionType.SkillUse:
                sim.Type = SimActionType.SkillUse;
                sim.UnitId = FindSimUnitId(aiAction.Unit, board);
                sim.TargetUnitId = aiAction.TargetUnit != null
                    ? FindSimUnitId(aiAction.TargetUnit, board) : -1;
                sim.TargetPos = SimBoardState.ToCell(aiAction.TargetPos);
                sim.SkillId = aiAction.Skill != null ? aiAction.Skill.Id : -1;
                if (sim.UnitId < 0) return null;
                return sim;

            case AIActionType.Build:
            case AIActionType.SubCrystal:
                sim.Type = SimActionType.Build;
                sim.Facility = aiAction.Facility;
                sim.TargetPos = SimBoardState.ToCell(aiAction.TargetPos);
                return sim;

            case AIActionType.Summon:
                sim.Type = SimActionType.Summon;
                sim.SummonKind = aiAction.SummonKind;
                sim.TargetPos = SimBoardState.ToCell(aiAction.TargetPos);
                return sim;

            case AIActionType.Wait:
                sim.Type = SimActionType.Wait;
                return sim;

            default:
                return null;
        }
    }

    // ================================================================
    //  Status (実ゲーム) → SimUnit ID のマッピング
    //  位置とチームで照合する
    // ================================================================
    static int FindSimUnitId(Status realUnit, SimBoardState board)
    {
        if (realUnit == null) return -1;

        var pos = new Vector3Int(
            Mathf.RoundToInt(realUnit.transform.position.x), 0,
            Mathf.RoundToInt(realUnit.transform.position.z));

        for (int i = 0; i < board.Units.Count; i++)
        {
            var su = board.Units[i];
            if (su.Team == realUnit.team && su.Position == pos && su.Kind == realUnit.kind)
                return su.Id;
        }

        // 位置が合わない場合はKind+Teamのみで探索
        for (int i = 0; i < board.Units.Count; i++)
        {
            var su = board.Units[i];
            if (su.Team == realUnit.team && su.Kind == realUnit.kind && su.IsAlive)
                return su.Id;
        }

        return -1;
    }
}
