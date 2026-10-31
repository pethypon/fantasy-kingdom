using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  AICommander.Selection — 行動選択・振動防止・履歴掃除・CD管理
// =====================================================================
public partial class AICommander
{
    // Owner: commander; lifetime: current selection; <= the complete legal candidate set; reused and
    // cleared on each selection/battle disposal. Never caps candidates or changes their tie-breaking order.
    readonly List<(AIAction action, float score)> selectionBuffer = new List<(AIAction action, float score)>(64);
    readonly AIRuntimeMaintenance runtimeMaintenance = new AIRuntimeMaintenance();
    readonly AITurnMovementHistory turnMovementHistory = new AITurnMovementHistory();
    // ================================================================
    //  死亡ユニットの位置履歴を掃除
    // ================================================================
    void CleanupDeadUnitHistory()
    {
        runtimeMaintenance.PrunePositionHistory(_unitPositionHistory, _actorTeam, _board?.OwnUnitParent);
    }

    // ================================================================
    //  行動選択
    // ================================================================
    AIAction SelectBestAction(List<AIAction> actions, HashSet<string> failedActions,
        HashSet<string> failedActionTypes = null)
    {
        using var measurement = AIPerformanceCounters.Measure(AIPerformanceStage.Selection);
        // 有効な候補をスコア順に収集
        var validActions = selectionBuffer;
        validActions.Clear();

        foreach (var action in actions)
        {
            if (action.ActionType == AIActionType.Wait) continue;
            if (AIActionEvaluator.IsIneffectiveProtectedAttack(action, _board)) continue;
            if (turnMovementHistory.ShouldReject(action, _board)) continue;
            if (_board.Operations?.ReversesHealthyMarch(action, _board) == true) continue;
            if (action.SpecialCommand != null && action.Score <= 0) continue;
            if (AIActionEvaluator.ShouldPreserveStationary(action, actions, _board)) continue;
            if (action.APCost > _board.EnemyAP) continue;

            if (failedActions.Count > 0 && failedActions.Contains(action.FailureKey)) continue;

            if (failedActionTypes != null && failedActionTypes.Count > 0)
            {
                string typeKey = action.FailureGroupKey;
                if (failedActionTypes.Contains(typeKey)) continue;
            }

            float score = action.Score;
            if (action.Unit != null && _actedUnits.Contains(action.Unit)
                && action.ActionType != AIActionType.Attack && action.ActionType != AIActionType.SkillUse)
                score *= 0.5f;

            validActions.Add((action, score));
        }

        if (validActions.Count == 0)
        {
            AIDecisionObserver.Emit(_board, actions, null, "selected");
            return null;
        }
        // Strategic priorities are lexicographic. Tactical scores and deliberate mistakes cannot override survival.
        int highestPriority=4;
        foreach(var entry in validActions)highestPriority=Mathf.Min(highestPriority,entry.action.StrategicPriority);
        for(int i=validActions.Count-1;i>=0;i--)if(validActions[i].action.StrategicPriority!=highestPriority)validActions.RemoveAt(i);
        // Bounded experience ranks legal actions only inside this surviving priority tier.
        for (int i = 0; i < validActions.Count; i++)
        {
            var entry = validActions[i];
            validActions[i] = (entry.action, entry.score + LearnedModifier(entry.action) + OperationActionBonus(entry.action)
                + CognitiveActionBonus(entry.action));
        }

        // Honor a currently affordable multi-actor kill inside the surviving
        // strategic tier. Otherwise an unrelated high-scoring action can spend
        // the AP reserved by the coordination estimate and the chain never starts.
        // Emergency/resupply priorities above this tier have already won; each
        // subsequent command is regenerated from its real result as usual.
        foreach (var entry in validActions)
            if (_board.Operations?.IsCoordinatedKillHead(entry.action, _board) == true)
            {
                RememberSelectionScores(entry.action, entry.score);
                AIDecisionObserver.Emit(_board, actions, entry.action, "selected");
                return entry.action;
            }

        // ミス率: 一定確率で最善手以外を選択する（チュートリアル〜ノーマル帯）
        float mistakeRate = _threatLevel.MistakeRate;
        if (mistakeRate > 0f && validActions.Count > 1 && _rng != null && _rng.NextFloat() < mistakeRate)
        {
            // 上位25-75%の範囲からランダムに選択（完全ランダムではなくそこそこの手を選ぶ）
            validActions.Sort((a, b) => b.score.CompareTo(a.score));
            int minIdx = Mathf.Max(1, validActions.Count / 4);
            int maxIdx = Mathf.Min(validActions.Count - 1, validActions.Count * 3 / 4);
            int idx = _rng.Range(minIdx, maxIdx + 1);
            RememberSelectionScores(validActions[idx].action, validActions[idx].score);
            AIDecisionObserver.Emit(_board, actions, validActions[idx].action, "selected");
            return validActions[idx].action;
        }

        // 通常: 最高スコアの行動を選択
        AIAction best = null;
        float bestScore = float.MinValue;
        foreach (var (action, score) in validActions)
        {
            if (score > bestScore)
            {
                bestScore = score;
                best = action;
            }
        }

        RememberSelectionScores(best, bestScore);
        AIDecisionObserver.Emit(_board, actions, best, "selected");
        return best;
    }

    void RememberSelectionScores(AIAction action, float score)
    {
        selectedLearnedModifier = action != null ? LearnedModifier(action) : 0;
        selectedFinalScore = score;
        selectedBaseScore = score - selectedLearnedModifier;
    }

    // ================================================================
    //  振動防止ペナルティ
    //  直近に訪れたマスへ戻る移動を大きく減点し、同じ2マスを往復するのを防ぐ
    // ================================================================
    void ApplyAntiOscillationPenalty(List<AIAction> actions)
    {
        foreach (var action in actions)
        {
            if (action.Unit == null) continue;
            if (_board.Operations?.IsPlannedMarch(action, _board) == true) continue;
            if (action.ActionType != AIActionType.Move
                && action.ActionType != AIActionType.Retreat
                && action.ActionType != AIActionType.Support
                && action.ActionType != AIActionType.Surround) continue;

            if (!_unitPositionHistory.TryGetValue(action.Unit, out var history)) continue;
            if (history.Count == 0) continue;

            var destCell = AIBoardState.ToCell(action.TargetPos);

            // 直近の位置と一致 → 大ペナルティ（往復防止）
            for (int i = history.Count - 1; i >= 0; i--)
            {
                if (history[i].x == destCell.x && history[i].z == destCell.z)
                {
                    float recency = history.Count - i; // 1=直前, 2=2ターン前...
                    float penalty = 30f / recency;     // 直前なら-30, 2ターン前なら-15
                    action.Score -= penalty;
                    break;
                }
            }
        }
    }

}
