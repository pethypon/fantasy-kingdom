using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  AICommander.Selection — 行動選択・振動防止・履歴掃除・CD管理
// =====================================================================
public partial class AICommander
{
    // ================================================================
    //  死亡ユニットの位置履歴を掃除
    // ================================================================
    void CleanupDeadUnitHistory()
    {
        var deadUnits = new List<Status>();
        foreach (var kvp in _unitPositionHistory)
        {
            if (kvp.Key == null || !kvp.Key.gameObject.activeInHierarchy || kvp.Key.HP <= 0)
                deadUnits.Add(kvp.Key);
        }
        foreach (var unit in deadUnits)
            _unitPositionHistory.Remove(unit);
    }

    // ================================================================
    //  行動選択
    // ================================================================
    AIAction SelectBestAction(List<AIAction> actions, HashSet<string> failedActions,
        HashSet<string> failedActionTypes = null)
    {
        // 有効な候補をスコア順に収集
        var validActions = new List<(AIAction action, float score)>();

        foreach (var action in actions)
        {
            if (action.ActionType == AIActionType.Wait) continue;
            if (action.APCost > _board.EnemyAP) continue;

            string failKey = $"{action.ActionType}_{action.Facility}_{action.SummonKind}_{action.TargetPos}";
            if (failedActions.Contains(failKey)) continue;

            if (failedActionTypes != null)
            {
                string typeKey = $"{action.ActionType}_{action.Facility}_{action.SummonKind}";
                if (failedActionTypes.Contains(typeKey)) continue;
            }

            float score = action.Score;
            if (action.Unit != null && _actedUnits.Contains(action.Unit))
                score *= 0.5f;

            validActions.Add((action, score));
        }

        if (validActions.Count == 0) return null;

        // ミス率: 一定確率で最善手以外を選択する（チュートリアル〜ノーマル帯）
        float mistakeRate = _threatLevel.MistakeRate;
        if (mistakeRate > 0f && validActions.Count > 1 && _rng != null && _rng.NextFloat() < mistakeRate)
        {
            // 上位25-75%の範囲からランダムに選択（完全ランダムではなくそこそこの手を選ぶ）
            validActions.Sort((a, b) => b.score.CompareTo(a.score));
            int minIdx = Mathf.Max(1, validActions.Count / 4);
            int maxIdx = Mathf.Min(validActions.Count - 1, validActions.Count * 3 / 4);
            int idx = _rng.Range(minIdx, maxIdx + 1);
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

        return best;
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

    // ================================================================
    //  スキルクールダウン管理
    // ================================================================
    void TickSkillCooldowns()
    {
        foreach (var unit in _board.AliveEnemyUnits)
        {
            if (unit == null || !unit.gameObject.activeInHierarchy) continue;
            if (unit.SkillCooldown > 0)
                unit.SkillCooldown--;
        }
    }
}
