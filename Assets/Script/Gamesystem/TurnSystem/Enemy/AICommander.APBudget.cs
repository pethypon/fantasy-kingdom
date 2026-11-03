using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  AICommander.APBudget — AP予約・建築需要算出・予約ペナルティ
// =====================================================================
public partial class AICommander
{
    int mainCrystalStrikeRound = -1;
    int mainBaseReconMoveRound = -1;

    AIAction ReservedMainBaseReconMove(IReadOnlyList<(AIAction action, float score)> valid)
    {
        if (_board == null || mainBaseReconMoveRound == _board.TurnCount || _board.PlayerCrystalVisible
            || _board.Governor?.Mode == StrategicMode.EmergencyDefense || _board.Governor?.Mode == StrategicMode.EconomicRecovery
            || !_board.TerritoryIntel.HasUsableTerritory && !_board.GetLastKnownPlayerCrystal().Valid
                && !_board.TerritoryIntel.HasSearchHypothesis) return null;
        AIAction best = null; float value = float.NegativeInfinity;
        foreach (var entry in valid)
        {
            var action = entry.action;
            if (action.Unit == null || action.Unit.kind != Kind.Scout || action.ActionType != AIActionType.Move
                || !_board.Exploration.IsObjectiveProgress(action.Unit, action.TargetPos, _board)) continue;
            float danger = _board.Governor?.IncomingDamage(_board, action.Unit, action.TargetPos, null)
                ?? _board.EstimateCounterDamageAt(action.TargetPos, action.Unit);
            if (danger >= action.Unit.HP * Mathf.Clamp(_board.Exploration.Settings.MaximumFrontierDangerFraction, .01f, 1f)) continue;
            if (entry.score > value) { best = action; value = entry.score; }
        }
        return best;
    }

    // Select one effective siege strike before unrelated spending in the SAME surviving
    // strategic tier. Emergency defense and necessary supply recovery still win first.
    AIAction ReservedMainCrystalStrike(IReadOnlyList<(AIAction action, float score)> valid)
    {
        if (_board == null || mainCrystalStrikeRound == _board.TurnCount || _board.Operations == null
            || _board.Governor?.Mode == StrategicMode.EmergencyDefense
            || _board.Governor?.Mode == StrategicMode.EconomicRecovery) return null;
        AIAction best = null; float bestScore = float.NegativeInfinity;
        foreach (var entry in valid)
        {
            var action = entry.action;
            if (!_board.Operations.IsMainCrystalCampaignAttack(action, _board)) continue;
            int impact = AIActionEvaluator.EstimateEffectiveSpecialImpact(action.Unit, action.TargetUnit, _board, out var recipient);
            if (impact <= 0 || recipient != action.TargetUnit) continue;
            int damage = AIActionEvaluator.EstimateEffectiveSpecialDamage(action.Unit, action.TargetUnit, _board, out _);
            float incoming = _board.Governor?.IncomingDamage(_board, action.Unit, action.Unit.transform.position, null)
                ?? _board.EstimateCounterDamageAt(action.Unit.transform.position, action.Unit);
            if (damage < action.TargetUnit.HP && incoming >= action.Unit.HP) continue;
            if (entry.score > bestScore) { best = action; bestScore = entry.score; }
        }
        return best;
    }

    void RecordCampaignAction(AIAction action)
    {
        if (action?.ActionType == AIActionType.Attack && action.TargetUnit != null
            && action.TargetUnit.kind == Kind.Crystal && action.TargetUnit.team == _board.OpponentTeam)
            mainCrystalStrikeRound = _board.TurnCount;
        if (action?.ActionType == AIActionType.Move && action.Unit?.kind == Kind.Scout)
            mainBaseReconMoveRound = _board.TurnCount;
    }

    // ================================================================
    //  AP予約計算: 建築/召喚用にAPを確保する
    //  移動でAPを使い果たして建築/召喚できなくなるのを防ぐ
    // ================================================================
    int CalcReservedAP()
    {
        if (_board == null || _governor.Mode == StrategicMode.EmergencyDefense) return 0;
        // An offensive objective must not suppress an affordable, necessary supply recovery.
        // No AP is reserved for an impossible project; the army can still scout or fight.
        return _governor.GetRecoveryReserveAP(_board);
    }

    // ================================================================
    //  AP予約ペナルティ: 移動系が予約APを食い込む場合に減点
    // ================================================================
    void ApplyAPReservationPenalty(List<AIAction> actions, int reservedAP)
    {
        if (reservedAP <= 0) return;

        // 経済が未成熟かどうかで重みを変える
        bool econWeak = !EconomyHelper.IsEconomySufficient(_board);

        foreach (var action in actions)
        {
            // 建築・召喚・サブクリスタルは予約対象なのでペナルティなし
            if (action.ActionType == AIActionType.Build
                || action.ActionType == AIActionType.Upgrade
                || action.ActionType == AIActionType.Summon
                || action.ActionType == AIActionType.SubCrystal)
                continue;

            int apAfterAction = _board.EnemyAP - action.APCost;

            // 攻撃は高価値なので軽いペナルティのみ
            if (action.ActionType == AIActionType.Attack
                || action.ActionType == AIActionType.SkillUse)
            {
                if (apAfterAction < reservedAP)
                    action.Score -= 8f;
                continue;
            }

            // 移動系: AP予約を食い込む場合は強く減点
            if (apAfterAction < reservedAP)
            {
                // 経済未成熟時は非常に強いペナルティ（建築を移動より優先させる）
                float penalty = econWeak ? 60f : 20f;
                action.Score -= penalty;
            }


        }
    }
}
