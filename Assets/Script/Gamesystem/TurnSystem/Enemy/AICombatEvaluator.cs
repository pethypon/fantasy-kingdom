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

        float score = 30f;

        int expectedDmg = AIEvalHelpers.EstimateDamage(action.Unit, action.TargetUnit);
        if (expectedDmg >= action.TargetUnit.HP)
            score += 40f;

        if (action.TargetUnit.MaxHP > 0)
        {
            float hpRatio = (float)action.TargetUnit.HP / action.TargetUnit.MaxHP;
            score += (1f - hpRatio) * 15f;
        }

        if (action.TargetUnit.kind == Kind.Crystal)
            score += 50f;
        if (action.TargetUnit.kind == Kind.King)
            score += 35f;

        if (action.TargetUnit.ShieldTurns > 0)
            score -= 30f;

        return score;
    }

    internal static float CalcMoveBaseScore(AIAction action, AIBoardState board)
    {
        float score = 10f;

        Vector3 unitPos = action.Unit.transform.position;
        Vector3 dest = action.TargetPos;

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

        // 偵察ボーナス: 未探索エリアへの移動を高評価
        int newVisionCells = board.EstimateNewVisionCells(dest);

        if (action.Unit.kind == Kind.Scout)
        {
            if (newVisionCells > 0)
                score += Mathf.Min(newVisionCells * 3f, 30f);
            float explorationRatio = board.GetExplorationRatio();
            if (explorationRatio < 0.5f)
                score += (1f - explorationRatio) * 15f;
        }
        else if (board.AlivePlayerUnits.Count == 0)
        {
            if (newVisionCells > 0)
                score += Mathf.Min(newVisionCells * 2f, 20f);

            float explorationRatio = board.GetExplorationRatio();
            if (explorationRatio < 0.6f)
                score += (1f - explorationRatio) * 10f;

            float allyDist = board.GetNearestAllyDist(dest, action.Unit);
            if (allyDist > 6f)
                score -= 8f;
            else if (allyDist >= 2f && allyDist <= 4f)
                score += 5f;
        }

        return score;
    }

    internal static float CalcSkillBaseScore(AIAction action, AIBoardState board)
    {
        if (action.Skill == null) return 0f;
        float score = 20f;

        var skill = action.Skill;

        // 攻撃スキル
        if (skill.Multiplier > 0 && action.TargetUnit != null)
        {
            int expectedDmg = SkillSystem.CalcSkillDamage(action.Unit, action.TargetUnit, skill);
            if (expectedDmg >= action.TargetUnit.HP)
                score += 45f;

            if (action.TargetUnit.kind == Kind.Crystal)
                score += 40f;
            if (action.TargetUnit.kind == Kind.King)
                score += 30f;

            if (action.TargetUnit.ShieldTurns > 0)
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
        if (skill.GrantBuff != BuffType.None)
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
        if (skill.InflictDebuff != StatusEffectType.None)
        {
            score += 8f;
            if (skill.InflictDebuff == StatusEffectType.Stun)
                score += 12f;
            if (skill.InflictDebuff == StatusEffectType.Freeze)
                score += 10f;
        }

        score -= (skill.APCost - 4) * 1.5f;

        return score;
    }

    internal static float CalcRetreatBaseScore(AIAction action, AIBoardState board)
    {
        float score = 8f;
        if (action.Unit == null) return score;

        float hpRatio = action.Unit.MaxHP > 0 ? (float)action.Unit.HP / action.Unit.MaxHP : 1f;
        score += (1f - hpRatio) * 25f;
        if (hpRatio < 0.2f) score += 15f;

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

        if (action.TargetUnit.kind == Kind.King) score += 10f;

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
        if (action.TargetUnit.kind == Kind.King) score += 15f;

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
