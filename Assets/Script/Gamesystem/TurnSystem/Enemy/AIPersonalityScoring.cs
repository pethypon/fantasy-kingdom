using UnityEngine;

// =====================================================================
//  AIPersonalityScoring — 性格に基づくスコア補正
//  AIActionEvaluator から分離。
//  大きい性格補正 / 細かい性格補正 / 局面補正 を担当。
// =====================================================================
static class AIPersonalityScoring
{
    const int TurnEarlyEnd = AIConstants.TurnEarlyEnd;

    // ---- 大きい性格補正 ----
    // BOSSが生存している場合のみ適用。通常駒はBOSSからの距離に応じて影響度が減衰する。
    internal static float CalcMajorBonus(AIAction action, AIPersonality p, AIBoardState board)
    {
        if (!p.ShouldApplyMajorBonus) return 0f;

        float influence = action.Unit != null ? p.GetCommandInfluence(action.Unit) : 0.5f;

        // BOSS自身の前線参加は性格によって制御
        // ただし敵が見えない場合は前進を制限しない（展開期に引き籠り防止）
        if (action.Unit != null && action.Unit.IsBoss)
        {
            if (action.ActionType == AIActionType.Move && board.AlivePlayerUnits.Count > 0)
            {
                float approach = AIEvalHelpers.GetApproachToEnemy(action, board);
                if (approach > 0 && p.BossFrontlineRate < 0.5f)
                    return -approach * (1f - p.BossFrontlineRate) * 8f;
            }
        }

        float bonus = 0f;

        switch (p.Major)
        {
            case MajorPersonality.Combat:
                if (action.ActionType == AIActionType.Attack)
                    bonus += 15f;
                if (action.ActionType == AIActionType.SkillUse && action.Skill != null && action.Skill.Multiplier > 0)
                    bonus += 12f;
                if (action.ActionType == AIActionType.Move)
                {
                    float approach = AIEvalHelpers.GetApproachToEnemy(action, board);
                    bonus += approach * 5f;
                }
                if (action.ActionType == AIActionType.Surround)
                    bonus += 10f;
                if (action.ActionType == AIActionType.Wait)
                    bonus -= 5f;
                if (action.ActionType == AIActionType.Retreat)
                    bonus -= 8f;
                if (action.ActionType == AIActionType.Summon)
                    bonus += 8f;
                break;

            case MajorPersonality.Intellect:
                if (action.ActionType == AIActionType.Attack)
                    bonus += 5f;
                if (action.ActionType == AIActionType.SkillUse && action.Skill != null)
                {
                    if (action.Skill.FixedHeal > 0 || action.Skill.GrantBuff != BuffType.None)
                        bonus += 12f;
                    else
                        bonus += 5f;
                }
                if (action.ActionType == AIActionType.Move)
                {
                    float allyDist = AIEvalHelpers.GetNearestAllyDist(action.TargetPos, action.Unit, board);
                    if (allyDist < 4f)
                        bonus += 8f;
                    float crystalDist = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
                    if (crystalDist > 10f)
                        bonus -= 5f;
                }
                if (action.ActionType == AIActionType.Support)
                    bonus += 10f;
                if (action.ActionType == AIActionType.Build)
                    bonus += 12f;
                if (action.ActionType == AIActionType.Retreat)
                    bonus += 5f;
                break;

            case MajorPersonality.Adaptive:
                if (action.ActionType == AIActionType.Attack)
                    bonus += 8f;
                if (action.ActionType == AIActionType.SkillUse)
                    bonus += 6f;
                break;

            case MajorPersonality.Growth:
                if (action.ActionType == AIActionType.Attack)
                    bonus += 10f;
                if (action.ActionType == AIActionType.SkillUse)
                    bonus += 8f;
                if (action.ActionType == AIActionType.Build)
                    bonus += 10f;
                if (action.ActionType == AIActionType.Summon)
                    bonus += 6f;
                break;
        }

        return bonus * influence;
    }

    // ---- 細かい性格補正 ----
    internal static float CalcTraitBonus(AIAction action, AIPersonality p, AIBoardState board)
    {
        float bonus = 0f;

        switch (action.ActionType)
        {
            case AIActionType.Attack:
                bonus += p.ObsessionRate * 20f;
                if (action.TargetUnit != null)
                {
                    int myDmg = AIEvalHelpers.EstimateDamage(action.Unit, action.TargetUnit);
                    int counterDmg = AIEvalHelpers.EstimateDamage(action.TargetUnit, action.Unit);
                    if (counterDmg > myDmg)
                        bonus -= p.CautionRate * 25f;
                }
                break;

            case AIActionType.SkillUse:
                if (action.Skill != null)
                {
                    if (action.Skill.Multiplier > 0)
                        bonus += p.ObsessionRate * 15f;
                    if (action.Skill.FixedHeal > 0 || action.Skill.GrantBuff != BuffType.None)
                        bonus += p.CommandRate * 12f;
                    if (action.Skill.InflictDebuff != StatusEffectType.None)
                        bonus += p.TacticsRate * 15f;
                    if (action.Skill.Target == SkillTarget.Self && action.Skill.GrantBuff != BuffType.None
                        && board.AlivePlayerUnits.Count > 0)
                        bonus += p.CautionRate * 8f;
                    if (action.AreaTargets != null && action.AreaTargets.Count > 1)
                        bonus += p.TacticsRate * (action.AreaTargets.Count * 5f);
                }
                break;

            case AIActionType.Move:
                bonus += CalcTacticalMoveBonus(action, p, board);
                float distFromBase = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
                if (distFromBase > 8f)
                {
                    float defPenalty = action.Unit.kind == Kind.Scout ? 5f : 15f;
                    bonus -= p.DefenseRate * defPenalty;
                }
                float allyDist = AIEvalHelpers.GetNearestAllyDist(action.TargetPos, action.Unit, board);
                if (allyDist < 3f)
                    bonus += p.CommandRate * 12f;
                else if (allyDist > 6f)
                    bonus -= p.CommandRate * 10f;
                float dangerDist = AIEvalHelpers.GetNearestPlayerDist(action.TargetPos, board);
                if (dangerDist < 2f)
                    bonus -= p.CautionRate * 10f;
                if (action.Unit.kind == Kind.Scout)
                {
                    int newCells = board.EstimateNewVisionCells(action.TargetPos);
                    if (newCells > 0)
                        bonus += p.TacticsRate * Mathf.Min(newCells * 2f, 20f);
                }
                break;

            case AIActionType.Retreat:
                bonus += p.CautionRate * 20f;
                bonus += p.DefenseRate * 10f;
                bonus -= p.ObsessionRate * 8f;
                break;

            case AIActionType.Support:
                bonus += p.CommandRate * 18f;
                bonus += p.DefenseRate * 8f;
                break;

            case AIActionType.Surround:
                bonus += p.TacticsRate * 20f;
                bonus += p.ObsessionRate * 8f;
                break;

            case AIActionType.DefenseRepos:
                bonus += p.DefenseRate * 22f;
                bonus += p.CautionRate * 10f;
                bonus -= p.ObsessionRate * 5f;
                break;

            case AIActionType.Build:
                bonus += p.DevelopRate * 20f;
                if (FacilityData.IsWall(action.Facility) || FacilityData.IsOffensive(action.Facility))
                    bonus += p.DefenseRate * 15f;
                if (board.TurnCount <= TurnEarlyEnd && !FacilityData.IsWall(action.Facility) && !FacilityData.IsOffensive(action.Facility))
                    bonus += p.CautionRate * 12f;
                break;

            case AIActionType.Summon:
                bonus += p.CommandRate * 15f;
                bonus += p.ObsessionRate * 5f;
                break;

            case AIActionType.Wait:
                bonus += p.CautionRate * 3f;
                bonus -= p.ObsessionRate * 5f;
                break;
        }

        if (action.ActionType == AIActionType.SubCrystal)
        {
            bonus += p.DevelopRate * 25f;
        }

        return bonus;
    }

    // ---- 局面補正 ----
    internal static float CalcSituationBonus(AIAction action, AIPersonality p, AIBoardState board)
    {
        float bonus = 0f;
        float advantageRatio = board.GetAdvantageRatio();

        if (p.Major == MajorPersonality.Adaptive)
        {
            if (advantageRatio > 0.2f)
            {
                if (action.ActionType == AIActionType.Attack)
                    bonus += 12f;
                if (action.ActionType == AIActionType.SkillUse && action.Skill != null && action.Skill.Multiplier > 0)
                    bonus += 10f;
                if (action.ActionType == AIActionType.Move)
                    bonus += AIEvalHelpers.GetApproachToEnemy(action, board) * 4f;
                if (action.ActionType == AIActionType.Surround)
                    bonus += 10f;
                if (action.ActionType == AIActionType.Summon)
                    bonus += 8f;
            }
            else if (advantageRatio < -0.2f)
            {
                if (action.ActionType == AIActionType.Retreat)
                    bonus += 15f;
                if (action.ActionType == AIActionType.Support)
                    bonus += 12f;
                if (action.ActionType == AIActionType.SkillUse && action.Skill != null && action.Skill.FixedHeal > 0)
                    bonus += 12f;
                if (action.ActionType == AIActionType.Build)
                    bonus += 10f;
                if (action.ActionType == AIActionType.Move)
                {
                    float retreatValue = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
                    if (retreatValue < 5f)
                        bonus += 10f;
                }
            }
        }

        // クリスタル危機時は防衛優先
        if (board.EnemyCrystalHP < board.EnemyCrystalMaxHP * 0.5f)
        {
            if (action.ActionType == AIActionType.Move && action.Unit != null)
            {
                float crystalDist = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
                if (crystalDist < 3f)
                    bonus += 20f;
            }
            if (action.ActionType == AIActionType.DefenseRepos)
                bonus += 18f;
            if (action.ActionType == AIActionType.Build && FacilityData.IsWall(action.Facility))
                bonus += 15f;
        }

        // 駒が少ない時は召喚優先
        if (board.AliveEnemyUnits.Count <= 2)
        {
            if (action.ActionType == AIActionType.Summon)
                bonus += 15f;
        }

        // 経済逼迫時：敵不在の自己バフスキルにAPを浪費しない
        if (action.ActionType == AIActionType.SkillUse && action.Skill != null)
        {
            float surplus = board.GetEconomicSurplus();
            if (surplus < 0.3f && action.Skill.Multiplier <= 0 && board.AlivePlayerUnits.Count == 0)
            {
                bonus -= 20f;
            }
            if (board.EnemyAP <= action.Skill.APCost + 2 && action.Skill.Multiplier <= 0)
            {
                bonus -= 10f;
            }
        }

        // 経済状況に応じた建築ボーナス
        if (action.ActionType == AIActionType.Build)
        {
            bool isMilitary = FacilityData.IsWall(action.Facility) || FacilityData.IsOffensive(action.Facility);
            int basicProducers = board.GetBuildingCount(FacilityKind.Well)
                               + board.GetBuildingCount(FacilityKind.LoggingCamp)
                               + board.GetBuildingCount(FacilityKind.Quarry);

            if (basicProducers == 0 && !isMilitary)
                bonus += 25f;
            if (board.TurnCount <= 4 && isMilitary)
                bonus -= 15f;
        }

        return bonus;
    }

    // ---- 戦術的移動ボーナス（側面攻撃） ----
    static float CalcTacticalMoveBonus(AIAction action, AIPersonality p, AIBoardState board)
    {
        float bonus = 0f;
        foreach (var pu in board.AlivePlayerUnits)
        {
            if (pu == null || !pu.gameObject.activeInHierarchy) continue;
            float dist = Vector3.Distance(action.TargetPos, pu.transform.position);
            if (dist < 2f)
            {
                Vector3 diff = action.TargetPos - pu.transform.position;
                bool isFlanking = Mathf.Abs(diff.x) > Mathf.Abs(diff.z);
                if (isFlanking)
                    bonus += p.TacticsRate * 10f;
            }
        }
        return bonus;
    }
}
