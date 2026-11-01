using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  AICombatEvaluator — 戦闘行動の基本評価（攻撃・移動・スキル・撤退・援護・包囲・防衛）
//  AIActionEvaluator から分離。
// =====================================================================
static class AICombatEvaluator
{
    internal static float CalcAttackBaseScore(AIAction action, AIBoardState board)
    {
        if (action.TargetUnit == null) return 0f;
        if (AIActionEvaluator.IsIneffectiveProtectedAttack(action, board)) return -10000f;

        float score = 30f;

        int expectedDmg = AIActionEvaluator.EstimateEffectiveSpecialDamage(action.Unit, action.TargetUnit, board, out var actualVictim);
        // Rank legal attacks by actual HP progress per paid AP as well as
        // objective importance. Previously 2- and 35-damage chips had the same
        // base score against a healthy crystal. No combat/AP rule is changed.
        score += AttackEfficiency(expectedDmg, action.APCost);
        if (actualVictim == action.TargetUnit && BarrierSystem.UsesBarrier(actualVictim))
        {
            int raw = AIActionEvaluator.EstimateObservedNormalPacket(action.Unit, actualVictim,board,
                AIActionEvaluator.EstimateSpecialAwareAttack(action.Unit, actualVictim, board));
            int barrierLoss = Mathf.Min(actualVictim.Barrier, SpecialSkillRuntime.PreviewDamage(actualVictim, raw));
            score += 15f * barrierLoss / Mathf.Max(1f, actualVictim.MaxBarrier);
        }
        if (actualVictim != null && expectedDmg >= actualVictim.HP)
            score += 40f;

        if (action.TargetUnit.MaxHP > 0)
        {
            float hpRatio = (float)action.TargetUnit.HP / action.TargetUnit.MaxHP;
            score += (1f - hpRatio) * 15f;
        }

        if (MatchObjectiveRules.IsDecisive(action.TargetUnit))
        {
            // Both decisive identities win the same match. Prefer the smaller
            // observed workload instead of an unconditional crystal-over-king
            // premium. This is a bounded ranking heuristic, not a turn forecast:
            // future fatigue, healing and reinforcement still need simulation.
            int impact = AIActionEvaluator.EstimateEffectiveSpecialImpact(action.Unit, action.TargetUnit, board, out var recipient);
            long durability = recipient != null ? (long)recipient.HP + recipient.Barrier : action.TargetUnit.HP;
            score += 50 + VictoryPressure(durability, impact, action.APCost);
        }
        if (board.ReconThreatLevel >= 31 && action.TargetUnit.kind == Kind.Scout)
            score += board.ReconThreatLevel >= 51 ? 20f : 12f;

        if (action.TargetUnit.HasDamageShield)
            score -= 30f;
        if (expectedDmg == 0 && action.TargetUnit.Special?.damageNullUnused == true)
            score -= Mathf.Min(25, AIActionEvaluator.EstimateSpecialAwareAttack(action.Unit, action.TargetUnit, board));

        return score;
    }

    internal static float AttackEfficiency(int hpDamage, int ap)
        => Mathf.Clamp(hpDamage / (float)Mathf.Max(1, ap), 0, 10) * 2;

    internal static float VictoryPressure(long durability, int impact, int ap)
        => impact <= 0 ? 0 : (float)(30d / (1 + System.Math.Max(0, durability)
            * (double)Mathf.Max(1, ap) / impact / Mathf.Max(1, APSystem.MaximumAP)));

    internal static float CalcMoveBaseScore(AIAction action, AIBoardState board)
    {
        float score = 10f;

        Vector3 unitPos = action.Unit.transform.position;
        Vector3 dest = action.TargetPos;

        if (MatchObjectiveRules.IsCommander(action.Unit))
        {
            // Scouting/contact bonuses belong to the field army. A commander
            // moves to recover safety or return home, not to reveal more Fog.
            float homeProgress = Vector3.Distance(unitPos, board.EnemyCrystalPos) - Vector3.Distance(dest, board.EnemyCrystalPos);
            float before = board.Governor != null ? board.Governor.IncomingDamage(board, action.Unit, unitPos, null)
                : board.EstimateCounterDamageAt(unitPos, action.Unit);
            float after = board.Governor != null ? board.Governor.IncomingDamage(board, action.Unit, dest, null)
                : board.EstimateCounterDamageAt(dest, action.Unit);
            return homeProgress * 10 + Mathf.Clamp(before - after, -action.Unit.HP, action.Unit.HP) * .5f - 15;
        }

        // ★ 視界制限: Playerクリスタルへの接近加点は視認済みの場合のみ
        if (board.CanUsePlayerCrystalAsTarget())
        {
            float distBefore = Vector3.Distance(unitPos, board.PlayerCrystalPos);
            float distAfter = Vector3.Distance(dest, board.PlayerCrystalPos);
            float approach = distBefore - distAfter;
            score += approach * 3f;
        }
        else
        {
            // 未視認時: Last Known Position があればそちらへ向かう（信頼度減衰付き）
            var lkCrystal = board.GetLastKnownPlayerCrystal();
            if (lkCrystal.Valid)
            {
                int age = board.TurnCount - lkCrystal.Turn;
                float reliability = Mathf.Clamp01(1f - age * 0.15f);
                if (reliability > 0.1f)
                {
                    Vector3 lkPos = new Vector3(lkCrystal.Position.x, 0, lkCrystal.Position.z);
                    float distBefore = Vector3.Distance(unitPos, lkPos);
                    float distAfter = Vector3.Distance(dest, lkPos);
                    float approach = distBefore - distAfter;
                    score += approach * 1.5f * reliability;
                }
            }
            // Last Known Player位置への接近（痕跡情報）
            var lkPositions = board.GetLastKnownPlayerPositions();
            foreach (var (pos, reliability) in lkPositions)
            {
                Vector3 lkPos = new Vector3(pos.x, 0, pos.z);
                float distBefore = Vector3.Distance(unitPos, lkPos);
                float distAfter = Vector3.Distance(dest, lkPos);
                float approach = distBefore - distAfter;
                if (approach > 0)
                    score += approach * 1f * reliability;
            }

            // 未探索方向への展開ボーナス
            Vector3 unexploredDir = board.GetUnexploredDirection();
            float dotProduct = Vector3.Dot((dest - unitPos).normalized, unexploredDir);
            if (dotProduct > 0.3f)
                score += dotProduct * 5f;
        }

        // 視界内の敵に近づく加点（視認済みの敵だけ）
        float nearestPlayerDist = AIEvalHelpers.GetNearestPlayerDist(dest, board);
        if (nearestPlayerDist < 3f)
            score += 5f;

        // 次ターン攻撃可能位置を優先（交戦開始時）
        if (board.AlivePlayerUnits.Count > 0)
        {
            foreach (var pu in board.AlivePlayerUnits)
            {
                if (pu == null || !pu.gameObject.activeInHierarchy) continue;
                Vector3 puPos = pu.transform.position;
                float dx = dest.x - puPos.x;
                float dy = dest.y - puPos.y;
                float dz = dest.z - puPos.z;
                float sqrDist = dx * dx + dy * dy + dz * dz;
                if (sqrDist <= 4f)        // 2f²
                    score += 8f;
                else if (sqrDist <= 12.25f) // 3.5f²
                    score += 4f;
            }
        }

        if (dest.y > unitPos.y)
            score += 2f;

        // Reconnaissance has value in every strategy, even while other enemies are visible.
        score += board.Recon.ScoreMove(action.Unit, dest);

        // Only the observer's stored territory waypoint contributes here. Known
        // route distance permits a necessary detour without inventing enemy actors.
        if (board.Governor?.HasActiveTerritoryAdvance == true)
            score += board.Governor.TerritoryIntel.ProgressScore(action.Unit, dest, board);

        return score;
    }

    internal static float CalcSkillBaseScore(AIAction action, AIBoardState board)
    {
        if (action.Skill == null) return 0f;
        if (AIActionEvaluator.IsIneffectiveProtectedAttack(action, board)) return -10000f;
        float score = 20f;

        var skill = action.Skill;
        Status buffTarget = skill.BuffToSelf ? action.Unit : action.TargetUnit ?? action.Unit;
        bool inertDefenceBuff = skill.GrantBuff == BuffType.Defensive && !DamageCalculator.UsesLegacyDefence;
        bool inertDefenceDebuff = skill.InflictDebuff == StatusEffectType.ArmorBreak && !DamageCalculator.UsesLegacyDefence;
        if (inertDefenceBuff && skill.Multiplier <= 0 && skill.FixedDamage <= 0 && skill.FixedHeal <= 0
            && string.IsNullOrEmpty(skill.SpecialEffect)
            && (skill.InflictDebuff == StatusEffectType.None || inertDefenceDebuff)) return -10000;

        // 攻撃スキル
        if (skill.Multiplier > 0 && action.TargetUnit != null
            && !AIActionEvaluator.IsObservedCompleteProtection(board, action.TargetUnit))
        {
            int expectedDmg = SpecialSkillRuntime.PreviewHpDamage(action.TargetUnit,
                SkillSystem.CalcSkillDamage(action.Unit, action.TargetUnit, skill));
            if (expectedDmg >= action.TargetUnit.HP)
                score += 45f;

            if (action.TargetUnit.kind == Kind.Crystal)
                score += 40f;
            if (MatchObjectiveRules.IsCommander(action.TargetUnit))
                score += 30f;

            if (action.TargetUnit.HasDamageShield)
                score -= 25f;

            if (action.AreaTargets != null && action.AreaTargets.Count > 1)
                score += action.AreaTargets.Count * 12f;

            score += skill.Multiplier * 10f;
        }

        // 回復スキル
        if (skill.FixedHeal > 0 && action.TargetUnit != null)
        {
            float hpRatio = action.TargetUnit.MaxHP > 0
                ? (float)action.TargetUnit.HP / action.TargetUnit.MaxHP : 1f;
            score += (1f - hpRatio) * 35f;
            if (hpRatio < 0.3f)
                score += 15f;
        }

        // バフスキル
        if (skill.GrantBuff != BuffType.None && !inertDefenceBuff)
        {
            score += 12f;
            if (skill.GrantBuff == BuffType.Haste)
                score += 8f;

            if (skill.Target == SkillTarget.Self || skill.Target == SkillTarget.SelfArea)
            {
                if (board.AlivePlayerUnits.Count == 0)
                    score -= 30f;
                else
                {
                    Vector3 selfPos = action.Unit.transform.position;
                    float nearestSqr = float.MaxValue;
                    foreach (var pu in board.AlivePlayerUnits)
                    {
                        if (pu == null || !pu.gameObject.activeInHierarchy) continue;
                        Vector3 puPos = pu.transform.position;
                        float dx = selfPos.x - puPos.x;
                        float dy = selfPos.y - puPos.y;
                        float dz = selfPos.z - puPos.z;
                        float sqrD = dx * dx + dy * dy + dz * dz;
                        if (sqrD < nearestSqr) nearestSqr = sqrD;
                    }
                    if (nearestSqr > 64f) // 8f²
                        score -= 15f;
                }
            }
        }

        // デバフ付き
        if (skill.InflictDebuff != StatusEffectType.None && !inertDefenceDebuff)
        {
            score += 8f;
            if (skill.InflictDebuff == StatusEffectType.Stun)
                score += 12f;
            if (skill.InflictDebuff == StatusEffectType.Freeze)
                score += 10f;
        }

        score -= (action.APCost - 4) * 1.5f;

        return score;
    }

    internal static float CalcRetreatBaseScore(AIAction action, AIBoardState board)
    {
        float score = 8f;
        if (action.Unit == null) return score;

        float hpRatio = action.Unit.MaxHP > 0 ? (float)action.Unit.HP / action.Unit.MaxHP : 1f;
        score += (1f - hpRatio) * 25f;
        if (hpRatio < 0.2f) score += 15f;
        if (board.Governor != null)
        {
            float before = board.Governor.IncomingDamage(board, action.Unit, action.Unit.transform.position, null);
            float after = board.Governor.IncomingDamage(board, action.Unit, action.TargetPos, null);
            score += Mathf.Clamp((before - after) / Mathf.Max(1, action.Unit.HP), -1, 1) * 25;
        }

        float dist = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
        if (dist < 5f) score += 8f;

        return score;
    }

    internal static float CalcSupportBaseScore(AIAction action, AIBoardState board)
    {
        float score = 15f;
        if (action.TargetUnit == null) return score;

        float targetHpRatio = action.TargetUnit.MaxHP > 0
            ? (float)action.TargetUnit.HP / action.TargetUnit.MaxHP : 1f;
        score += (1f - targetHpRatio) * 20f;

        if (MatchObjectiveRules.IsCommander(action.TargetUnit)) score += 10f;

        return score;
    }

    internal static float CalcSurroundBaseScore(AIAction action, AIBoardState board)
    {
        float score = 18f;
        if (action.TargetUnit == null) return score;

        float hpRatio = action.TargetUnit.MaxHP > 0
            ? (float)action.TargetUnit.HP / action.TargetUnit.MaxHP : 1f;
        score += (1f - hpRatio) * 15f;

        if (action.TargetUnit.kind == Kind.Crystal) score += 25f;
        if (MatchObjectiveRules.IsCommander(action.TargetUnit)) score += 15f;

        return score;
    }

    internal static float CalcDefenseReposBaseScore(AIAction action, AIBoardState board)
    {
        float score = 12f;
        if (action.Unit == null) return score;

        float dist = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
        if (dist < 3f) score += 15f;
        else if (dist < 5f) score += 8f;

        if (board.EnemyCrystalHP < board.EnemyCrystalMaxHP * 0.3f)
            score += 20f;
        else if (board.EnemyCrystalHP < board.EnemyCrystalMaxHP * 0.5f)
            score += 10f;

        return score;
    }
}
