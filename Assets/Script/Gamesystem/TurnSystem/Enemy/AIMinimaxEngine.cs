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

    // ---- 時間制限 ----
    const float DefaultTimeBudgetMs = 30000f;
    float _timeBudgetMs;
    Stopwatch _stopwatch;

    // ---- キラームーブ (深さごとに最善だった行動を記録) ----
    SimAction[] _killerMoves;

    public AIMinimaxEngine(int maxDepth = 3, int candidateLimit = 14,
        int greedyActionsPerTurn = 10, float timeBudgetMs = DefaultTimeBudgetMs)
    {
        _maxDepth = Mathf.Clamp(maxDepth, 1, 4);
        _candidateLimit = Mathf.Max(4, candidateLimit);
        _greedyActionsPerTurn = Mathf.Clamp(greedyActionsPerTurn, 4, 15);
        _timeBudgetMs = timeBudgetMs;
        _killerMoves = new SimAction[_maxDepth + 1];
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
            $"評価{_nodesEvaluated}ノード 枝刈り{_pruned}回 " +
            $"{_elapsedMs:F0}ms 基準値={baseScore:F1}");

        return result;
    }

    // ================================================================
    //  Min探索 (Playerの応手): AIにとって最悪のスコアを返す
    // ================================================================
    float MinSearch(SimBoardState board, int depth, int maxDepth, float alpha, float beta)
    {
        if (_stopwatch.ElapsedMilliseconds > _timeBudgetMs)
            return SimBoardEvaluator.Evaluate(board);

        // ゲーム終了チェック
        if (board.IsTerminal())
        {
            _nodesEvaluated++;
            return SimBoardEvaluator.Evaluate(board);
        }

        // ターン遷移: Player のターン開始をシミュレーション
        board.SimulateTurnTransition(Team.Player);

        // ゲーム終了チェック（DoTで死亡した場合）
        if (board.IsTerminal())
        {
            _nodesEvaluated++;
            return SimBoardEvaluator.Evaluate(board);
        }

        // Playerの候補行動を生成
        var actions = SimActionGenerator.GenerateAllActions(board, Team.Player);
        if (actions.Count == 0)
        {
            if (depth < maxDepth)
                return MaxSearch(board, depth + 1, maxDepth, alpha, beta);
            _nodesEvaluated++;
            return SimBoardEvaluator.Evaluate(board);
        }

        // 手順序: Playerの最善手（高QuickScore = Player自身にとって有利）を先に探索
        // QuickScoreはActorTeamに基づいてチーム視点を切り替えるので、
        // Player行動の高スコアはPlayerにとって良い手 → AIにとって悪い手
        // これをMin探索で先に探索するとBeta枝刈りが最大化される
        actions.Sort((a, b) =>
        {
            float sa = SimActionGenerator.QuickScore(a, board);
            float sb = SimActionGenerator.QuickScore(b, board);
            return sb.CompareTo(sa); // 降順: Playerの良い手が先
        });

        // キラームーブを先頭に移動
        if (depth < _killerMoves.Length && _killerMoves[depth] != null)
        {
            var killer = _killerMoves[depth];
            for (int i = 1; i < actions.Count; i++)
            {
                if (actions[i].UnitId == killer.UnitId && actions[i].Type == killer.Type
                    && actions[i].TargetPos == killer.TargetPos)
                {
                    var tmp = actions[i];
                    actions[i] = actions[0];
                    actions[0] = tmp;
                    break;
                }
            }
        }

        int limit = Mathf.Min(_candidateLimit, actions.Count);
        float minScore = float.MaxValue;
        SimAction bestAction = null;

        for (int i = 0; i < limit; i++)
        {
            if (_stopwatch.ElapsedMilliseconds > _timeBudgetMs) break;

            var boardCopy = board.Clone();
            boardCopy.ApplyAction(actions[i]);

            // Playerの残りターンをgreedyに実行
            SimulateGreedyTurn(boardCopy, Team.Player);

            float score;
            if (depth < maxDepth)
            {
                score = MaxSearch(boardCopy, depth + 1, maxDepth, alpha, beta);
            }
            else
            {
                score = SimBoardEvaluator.Evaluate(boardCopy);
                _nodesEvaluated++;
            }

            if (score < minScore)
            {
                minScore = score;
                bestAction = actions[i];
            }

            // Beta枝刈り
            if (score <= alpha)
            {
                _pruned++;
                break;
            }
            if (score < beta) beta = score;
        }

        // キラームーブ記録
        if (bestAction != null && depth < _killerMoves.Length)
            _killerMoves[depth] = bestAction;

        return minScore == float.MaxValue ? SimBoardEvaluator.Evaluate(board) : minScore;
    }

    // ================================================================
    //  Max探索 (AIの再応手): AIにとって最善のスコアを返す
    // ================================================================
    float MaxSearch(SimBoardState board, int depth, int maxDepth, float alpha, float beta)
    {
        if (_stopwatch.ElapsedMilliseconds > _timeBudgetMs)
            return SimBoardEvaluator.Evaluate(board);

        // ゲーム終了チェック
        if (board.IsTerminal())
        {
            _nodesEvaluated++;
            return SimBoardEvaluator.Evaluate(board);
        }

        // ターン遷移: AI(Enemy)のターン開始をシミュレーション
        board.SimulateTurnTransition(Team.Enemy);

        if (board.IsTerminal())
        {
            _nodesEvaluated++;
            return SimBoardEvaluator.Evaluate(board);
        }

        var actions = SimActionGenerator.GenerateAllActions(board, Team.Enemy);
        if (actions.Count == 0)
        {
            _nodesEvaluated++;
            return SimBoardEvaluator.Evaluate(board);
        }

        // 手順序: AIにとって良い手（高QuickScore）が先
        actions.Sort((a, b) =>
        {
            float sa = SimActionGenerator.QuickScore(a, board);
            float sb = SimActionGenerator.QuickScore(b, board);
            return sb.CompareTo(sa);
        });

        // キラームーブ
        if (depth < _killerMoves.Length && _killerMoves[depth] != null)
        {
            var killer = _killerMoves[depth];
            for (int i = 1; i < actions.Count; i++)
            {
                if (actions[i].UnitId == killer.UnitId && actions[i].Type == killer.Type
                    && actions[i].TargetPos == killer.TargetPos)
                {
                    var tmp = actions[i];
                    actions[i] = actions[0];
                    actions[0] = tmp;
                    break;
                }
            }
        }

        int limit = Mathf.Min(_candidateLimit, actions.Count);
        float maxScore = float.MinValue;
        SimAction bestAction = null;

        for (int i = 0; i < limit; i++)
        {
            if (_stopwatch.ElapsedMilliseconds > _timeBudgetMs) break;

            var boardCopy = board.Clone();
            boardCopy.ApplyAction(actions[i]);

            SimulateGreedyTurn(boardCopy, Team.Enemy);

            float score;
            if (depth < maxDepth)
            {
                score = MinSearch(boardCopy, depth + 1, maxDepth, alpha, beta);
            }
            else
            {
                score = SimBoardEvaluator.Evaluate(boardCopy);
                _nodesEvaluated++;
            }

            if (score > maxScore)
            {
                maxScore = score;
                bestAction = actions[i];
            }

            // Alpha枝刈り
            if (score >= beta)
            {
                _pruned++;
                break;
            }
            if (score > alpha) alpha = score;
        }

        if (bestAction != null && depth < _killerMoves.Length)
            _killerMoves[depth] = bestAction;

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

            // ゲーム終了チェック
            if (board.IsTerminal()) break;

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

                // 既に行動した駒は割引（ただし確殺可能な攻撃は例外）
                if (a.UnitId >= 0 && actedUnits.Contains(a.UnitId))
                {
                    bool isKillShot = false;
                    if (a.Type == SimActionType.Attack && a.TargetUnitId >= 0)
                    {
                        var attacker = board.GetUnit(a.UnitId);
                        var target = board.GetUnit(a.TargetUnitId);
                        if (attacker != null && target != null)
                        {
                            int dmg = SimBoardState.CalcDamage(attacker, target);
                            if (dmg >= target.HP) isKillShot = true;
                        }
                    }
                    if (!isKillShot) score *= 0.4f;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = a;
                }
            }

            if (best == null || bestScore < -5f) break; // 有益な行動がなければ終了

            board.ApplyAction(best);
            if (best.UnitId >= 0)
                actedUnits.Add(best.UnitId);
        }
    }

    // ================================================================
    //  AIAction → SimAction 変換
    // ================================================================
    SimAction ConvertToSimAction(AIAction aiAction, SimBoardState board)
    {
        var sim = new SimAction();
        sim.APCost = aiAction.APCost;
        sim.ActorTeam = Team.Enemy; // AICommanderから呼ばれるので常にEnemy

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
    // ================================================================
    static int FindSimUnitId(Status realUnit, SimBoardState board)
    {
        if (realUnit == null) return -1;

        var pos = new Vector3Int(
            Mathf.RoundToInt(realUnit.transform.position.x), 0,
            Mathf.RoundToInt(realUnit.transform.position.z));

        // 位置 + チーム + Kind で完全一致
        for (int i = 0; i < board.Units.Count; i++)
        {
            var su = board.Units[i];
            if (su.Team == realUnit.team && su.Position == pos && su.Kind == realUnit.kind)
                return su.Id;
        }

        // 位置が合わない場合はKind+Teamのみで探索（移動済みの場合）
        for (int i = 0; i < board.Units.Count; i++)
        {
            var su = board.Units[i];
            if (su.Team == realUnit.team && su.Kind == realUnit.kind && su.IsAlive)
                return su.Id;
        }

        return -1;
    }
}
