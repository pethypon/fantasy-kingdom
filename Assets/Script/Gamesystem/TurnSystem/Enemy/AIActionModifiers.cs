using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  AIActionModifiers — 候補行動リストへの事後補正
//
//  AIActionEvaluator.EvaluateAll() から呼ばれる。
//  基本スコア算出後の「反撃ペナルティ」「撤退ボーナス」「BOSS条件」
//  「段階的軍拡」など、リスト全体を横断する調整を担当する。
// =====================================================================
public static class AIActionModifiers
{
    // ================================================================
    //  次ターン反撃圏ペナルティ
    // ================================================================
    public static void ApplyCounterDangerPenalty(List<AIAction> actions, AIBoardState board, AIPersonality personality)
    {
        foreach (var action in actions)
        {
            if (action.Unit == null) continue;
            if (action.ActionType != AIActionType.Move
                && action.ActionType != AIActionType.Surround
                && action.ActionType != AIActionType.Support
                && action.ActionType != AIActionType.Attack
                && action.ActionType != AIActionType.SkillUse) continue;

            Vector3 posAfter;
            if (action.ActionType == AIActionType.Move
                || action.ActionType == AIActionType.Surround
                || action.ActionType == AIActionType.Support)
            {
                posAfter = action.TargetPos;
            }
            else
            {
                posAfter = action.Unit.transform.position;
            }

            int counterDmg = board.EstimateCounterDamageAt(posAfter, action.Unit);
            if (counterDmg <= 0) continue;

            float hpRatio = action.Unit.MaxHP > 0 ? (float)action.Unit.HP / action.Unit.MaxHP : 1f;
            bool wouldDie = counterDmg >= action.Unit.HP;

            float importanceMult = 1f;
            if (action.Unit.IsBoss) importanceMult = 2.0f;
            else if (action.Unit.kind == Kind.King) importanceMult = 2.5f;
            else if (action.Unit.kind == Kind.Priest) importanceMult = 1.8f;
            else if (personality.HasBoss && (action.Unit.kind == Kind.Guardian || action.Unit.kind == Kind.Knight))
            {
                float bossDist = Vector3.Distance(posAfter, personality.BossUnit.transform.position);
                if (bossDist < 3f) importanceMult = 1.5f;
            }

            int alliesNear = board.CountAlliesNear(posAfter, action.Unit, 3f);
            float isolationMult = alliesNear == 0 ? 1.5f : alliesNear == 1 ? 1.2f : 1f;

            float retreatSafety = EvalRetreatPathSafety(posAfter, action.Unit, board);
            if (retreatSafety < -5f) isolationMult *= 1.3f;

            float penalty;
            if (wouldDie)
            {
                penalty = 35f * importanceMult * isolationMult;
                if (action.ActionType == AIActionType.Attack && action.TargetUnit != null)
                {
                    int myDmg = AIEvalHelpers.EstimateDamage(action.Unit, action.TargetUnit);
                    if (myDmg >= action.TargetUnit.HP)
                        penalty *= 0.3f;
                }
            }
            else
            {
                float dmgRatio = (float)counterDmg / Mathf.Max(1, action.Unit.HP);
                penalty = dmgRatio * 20f * importanceMult * isolationMult;
                if (hpRatio < 0.4f) penalty += 10f * importanceMult;
            }

            action.Score -= penalty;
        }
    }

    // ================================================================
    //  撤退→再編チェーンボーナス
    // ================================================================
    public static void ApplyRetreatRegroupBonus(List<AIAction> actions, AIPersonality p, AIBoardState board)
    {
        float chainMultiplier = 1f;
        if (p.ShouldApplyMajorBonus && p.Major == MajorPersonality.Intellect)
            chainMultiplier = 1.5f;

        foreach (var action in actions)
        {
            if (action.ActionType != AIActionType.Retreat && action.ActionType != AIActionType.DefenseRepos)
                continue;
            if (action.Unit == null) continue;

            float bonus = 0f;

            if (board.HasHealerInRange(action.TargetPos, 4f))
                bonus += 12f;

            if (board.HasDefensiveStructureNear(action.TargetPos, 3f))
                bonus += 8f;

            int alliesNear = board.CountAlliesNear(action.TargetPos, action.Unit, 3f);
            if (alliesNear >= 2)
                bonus += 10f;
            else if (alliesNear >= 1)
                bonus += 5f;

            float nearestPlayerDist = AIEvalHelpers.GetNearestPlayerDist(action.TargetPos, board);
            if (nearestPlayerDist >= 2f && nearestPlayerDist <= 4f)
                bonus += 6f;

            int counterDmg = board.EstimateCounterDamageAt(action.TargetPos, action.Unit);
            if (counterDmg == 0)
                bonus += 8f;
            else if (counterDmg < action.Unit.HP * 0.2f)
                bonus += 4f;

            float retreatPathSafety = EvalRetreatPathSafety(action.TargetPos, action.Unit, board);
            bonus += retreatPathSafety;

            action.Score += bonus * chainMultiplier;
        }
    }

    // ================================================================
    //  BOSS前線参加条件
    // ================================================================
    public static void ApplyBossFrontlineConditions(List<AIAction> actions, AIPersonality p, AIBoardState board)
    {
        if (!p.HasBoss) return;
        var boss = p.BossUnit;

        bool noVisibleEnemies = board.AlivePlayerUnits.Count == 0;

        foreach (var action in actions)
        {
            if (action.Unit != boss) continue;
            if (action.ActionType != AIActionType.Move && action.ActionType != AIActionType.Surround) continue;

            float approach = AIEvalHelpers.GetApproachToEnemy(action, board);
            if (approach <= 0) continue;

            if (noVisibleEnemies) continue;

            float conditionScore = 0f;
            int conditionsMet = 0;

            int escortsNear = board.CountAlliesNear(action.TargetPos, boss, 3f);
            if (escortsNear >= 2) { conditionScore += 8f; conditionsMet++; }

            float nearestAllyOnFrontline = float.MaxValue;
            foreach (var u in board.AliveEnemyUnits)
            {
                if (u == null || u == boss || !u.gameObject.activeInHierarchy) continue;
                float d = Vector3.Distance(action.TargetPos, u.transform.position);
                if (d < nearestAllyOnFrontline) nearestAllyOnFrontline = d;
            }
            if (nearestAllyOnFrontline > 4f)
            { conditionScore += 6f; conditionsMet++; }

            foreach (var pu in board.AlivePlayerUnits)
            {
                if (pu == null || !pu.gameObject.activeInHierarchy) continue;
                float dist = Vector3.Distance(action.TargetPos, pu.transform.position);
                if (dist < 2.5f)
                {
                    int dmg = AIEvalHelpers.EstimateDamage(boss, pu);
                    if (dmg >= pu.HP) { conditionScore += 12f; conditionsMet++; break; }
                }
            }

            int counterDmg = board.EstimateCounterDamageAt(action.TargetPos, boss);
            if (counterDmg < boss.HP * 0.3f) { conditionScore += 5f; conditionsMet++; }

            int influencedCount = 0;
            foreach (var u in board.AliveEnemyUnits)
            {
                if (u == null || u == boss || !u.gameObject.activeInHierarchy) continue;
                float distBefore = Vector3.Distance(u.transform.position, boss.transform.position);
                float distAfter = Vector3.Distance(u.transform.position, action.TargetPos);
                if (distAfter < distBefore && distAfter < 10f) influencedCount++;
            }
            if (influencedCount >= 2) { conditionScore += 8f; conditionsMet++; }

            if (conditionsMet < 2)
            {
                action.Score -= approach * 15f;
            }
            else
            {
                action.Score += conditionScore;
            }
        }
    }

    // ================================================================
    //  経済余裕による段階的軍拡
    // ================================================================
    public static void ApplyGradualArmyExpansion(List<AIAction> actions, AIBoardState board)
    {
        int allyCount = board.AliveEnemyUnits.Count;
        float surplus = board.GetEconomicSurplus();

        bool desperateForUnits = allyCount <= 3 && board.TurnCount > 5;
        if (surplus < 0.15f && !desperateForUnits) return;

        float expansionBonus = 0f;
        if (desperateForUnits)
            expansionBonus = 20f;
        else if (surplus > 0.7f)
            expansionBonus = 15f;
        else if (surplus > 0.5f)
            expansionBonus = 10f;
        else if (surplus > 0.3f)
            expansionBonus = 8f;
        else
            expansionBonus = 5f;

        if (allyCount >= 8) expansionBonus *= 0.3f;
        else if (allyCount >= 6) expansionBonus *= 0.6f;

        foreach (var action in actions)
        {
            if (action.ActionType != AIActionType.Summon) continue;
            action.Score += expansionBonus;

            if (action.SummonKind == Kind.Knight || action.SummonKind == Kind.Archer || action.SummonKind == Kind.Scout)
                action.Score += 5f;
        }
    }

    // ================================================================
    //  地形ボーナス: 高台への移動を優先、低地から移動中の優位
    // ================================================================
    public static void ApplyTerrainAwareness(List<AIAction> actions, AIBoardState board)
    {
        if (board.MapCreate == null) return;
        var setPos = board.MapCreate.SetPos;
        if (setPos == null || setPos.Count == 0) return;

        foreach (var action in actions)
        {
            if (action.Unit == null) continue;
            if (action.ActionType != AIActionType.Move
                && action.ActionType != AIActionType.Surround
                && action.ActionType != AIActionType.Support
                && action.ActionType != AIActionType.Retreat) continue;

            int tx = Mathf.RoundToInt(action.TargetPos.x);
            int tz = Mathf.RoundToInt(action.TargetPos.z);
            int cx = Mathf.RoundToInt(action.Unit.transform.position.x);
            int cz = Mathf.RoundToInt(action.Unit.transform.position.z);

            if (!GridHelper.TryGetHeight(setPos, tx, tz, out float ty)) continue;
            if (!GridHelper.TryGetHeight(setPos, cx, cz, out float cy)) continue;

            int dy = Mathf.RoundToInt(ty) - Mathf.RoundToInt(cy);
            if (dy >= GameConstants.HighGroundYThreshold)
                action.Score += 6f; // 高台への移動
            else if (dy <= -GameConstants.HighGroundYThreshold)
                action.Score -= 3f; // 低地への移動
        }
    }

    // ================================================================
    //  ダンジョン占有ボーナス: 未制圧ダンジョンへの接近・占有を優遇
    // ================================================================
    public static void ApplyDungeonAwareness(List<AIAction> actions, AIBoardState board)
    {
        if (board.DungeonSystem == null) return;
        var dungeons = board.DungeonSystem.GetActiveDungeons();
        if (dungeons == null || dungeons.Count == 0) return;

        foreach (var action in actions)
        {
            if (action.Unit == null) continue;
            if (action.ActionType != AIActionType.Move
                && action.ActionType != AIActionType.Surround
                && action.ActionType != AIActionType.Support) continue;

            Vector3Int targetGrid = GridHelper.ToGridXZ(action.TargetPos);

            foreach (var d in dungeons)
            {
                int dist = GridHelper.ChebyshevDistance(targetGrid, d.Position);
                // 占有中対象ダンジョンへ到達
                if (dist == 0)
                {
                    // 敵陣が占有進行中の場合は妨害で大きな加点
                    if (d.ClaimingTeam == Team.Player && d.ClaimProgress > 0)
                        action.Score += 18f + d.ClaimProgress * 1.5f;
                    else
                        action.Score += 12f;
                }
                else if (dist <= 3)
                {
                    action.Score += (4 - dist) * 3f;
                }
            }
        }
    }

    // ================================================================
    //  ヘルパー: 退路安全性評価
    // ================================================================

    /// <summary>
    /// 退路安全性評価: 撤退先からさらに移動可能なマスのうち
    /// 敵の攻撃圏外に出られるマスがどれだけあるかを評価する。
    /// </summary>
    internal static float EvalRetreatPathSafety(Vector3 retreatPos, Status unit, AIBoardState board)
    {
        Vector3[] directions = {
            new Vector3(1, 0, 0), new Vector3(-1, 0, 0),
            new Vector3(0, 0, 1), new Vector3(0, 0, -1)
        };

        int safePaths = 0;
        int totalPaths = 0;

        foreach (var dir in directions)
        {
            Vector3 neighbor = retreatPos + dir;
            if (!board.IsValidTile(neighbor)) continue;
            totalPaths++;

            int dmgAtNeighbor = board.EstimateCounterDamageAt(neighbor, unit);
            if (dmgAtNeighbor < unit.HP * 0.3f)
                safePaths++;
        }

        if (totalPaths == 0)
            return -15f;

        float safeRatio = (float)safePaths / totalPaths;

        if (safeRatio <= 0f)
            return -12f;
        if (safeRatio < 0.5f)
            return -5f;
        if (safeRatio >= 0.75f)
            return 6f;

        return 0f;
    }
}
