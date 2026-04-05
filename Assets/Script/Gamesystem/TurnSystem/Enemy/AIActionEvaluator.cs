using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  AIActionEvaluator — 行動評価コーディネーター
//  候補生成は AIActionGenerator に委譲。
//  基本評価は AICombatEvaluator / AIBuildEvaluator に委譲。
//  性格補正は AIPersonalityScoring に委譲。
//  最終評価 = 基本評価 + 大きい性格補正 + 細かい性格補正 + 局面補正 + 学習補正
// =====================================================================
public static class AIActionEvaluator
{
    // ================================================================
    //  共通ヘルパー（他の評価クラスからも参照される）
    // ================================================================

    const int TurnEarlyEnd = AIConstants.TurnEarlyEnd;
    const int TurnMidEnd   = AIConstants.TurnMidEnd;

    /// <summary>ターンに応じたフェーズ別スコアを返す</summary>
    internal static float PhaseScore(int turn, float early, float mid, float late)
    {
        if (turn <= TurnEarlyEnd) return early;
        if (turn <= TurnMidEnd)   return mid;
        return late;
    }

    /// <summary>基礎経済施設5種(Well,LoggingCamp,Quarry,Field,House)の設置済み種類数</summary>
    internal static int CalcCoreEconomyCount(AIBoardState board)
    {
        return (board.GetBuildingCount(FacilityKind.Well) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.LoggingCamp) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.Quarry) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.Field) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.House) > 0 ? 1 : 0);
    }

    /// <summary>原料生産施設5種(Well,LoggingCamp,Quarry,Field,Mine)の設置済み種類数</summary>
    internal static int CalcRawFacilityCount(AIBoardState board)
    {
        return (board.GetBuildingCount(FacilityKind.Well) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.LoggingCamp) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.Quarry) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.Field) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.Mine) > 0 ? 1 : 0);
    }

    /// <summary>加工施設4種(Smelter,Bakery,LumberMill,StoneWorks)の設置済み種類数</summary>
    internal static int CalcProcessingFacilityCount(AIBoardState board)
    {
        return (board.GetBuildingCount(FacilityKind.Smelter) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.Bakery) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.LumberMill) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.StoneWorks) > 0 ? 1 : 0);
    }

    /// <summary>資源量に応じた緊急度ボーナス(枯渇→最大, 少量→中, やや不足→小)</summary>
    internal static float ResourceEmergencyBonus(int amount, float depleted, float low, float moderate,
        int lowThreshold = 20, int moderateThreshold = 50)
    {
        if (amount <= 0)                 return depleted;
        if (amount <= lowThreshold)      return low;
        if (amount <= moderateThreshold) return moderate;
        return 0f;
    }

    /// <summary>指定施設が基礎経済5種の中でまだ建っていないものかどうか</summary>
    internal static bool IsMissingCoreFacility(FacilityKind facility, AIBoardState board)
    {
        switch (facility)
        {
            case FacilityKind.Well:        return board.GetBuildingCount(FacilityKind.Well) == 0;
            case FacilityKind.LoggingCamp: return board.GetBuildingCount(FacilityKind.LoggingCamp) == 0;
            case FacilityKind.Quarry:      return board.GetBuildingCount(FacilityKind.Quarry) == 0;
            case FacilityKind.Field:       return board.GetBuildingCount(FacilityKind.Field) == 0;
            case FacilityKind.House:       return board.GetBuildingCount(FacilityKind.House) == 0;
            case FacilityKind.LumberMill:  return board.GetBuildingCount(FacilityKind.LumberMill) == 0;
            case FacilityKind.StoneWorks:  return board.GetBuildingCount(FacilityKind.StoneWorks) == 0;
            case FacilityKind.Bakery:      return board.GetBuildingCount(FacilityKind.Bakery) == 0;
            case FacilityKind.Smelter:     return board.GetBuildingCount(FacilityKind.Smelter) == 0;
            case FacilityKind.Mine:        return board.GetBuildingCount(FacilityKind.Mine) == 0;
            default: return false;
        }
    }

    internal static bool IsProcessingFacility(FacilityKind facility)
    {
        return facility == FacilityKind.LumberMill
            || facility == FacilityKind.StoneWorks
            || facility == FacilityKind.Bakery
            || facility == FacilityKind.Smelter;
    }

    // ================================================================
    //  全候補行動を生成・評価してスコア順に返す
    // ================================================================
    public static List<AIAction> EvaluateAll(
        AIPersonality personality,
        AIBoardState board,
        AILearning learning,
        TurnStrategy strategy = TurnStrategy.Balanced)
    {
        var actions = new List<AIAction>();

        // 候補生成は AIActionGenerator に委譲
        AIActionGenerator.GenerateAllCandidates(board, actions);

        // 各候補にスコア付け
        foreach (var action in actions)
        {
            action.Score = CalcScore(action, personality, board, learning);
        }

        // ターン方針ボーナス（AIStrategyBonusに委譲）
        AIStrategyBonus.Apply(actions, strategy, board);

        // 次ターン反撃圏ペナルティ
        ApplyCounterDangerPenaltyInternal(actions, board, personality);

        // 撤退→回復チェーンボーナス
        ApplyRetreatRegroupBonusInternal(actions, personality, board);

        // BOSS前線参加条件チェック
        ApplyBossFrontlineConditionsInternal(actions, personality, board);

        // 経済余裕による段階的召喚ボーナス
        ApplyGradualArmyExpansionInternal(actions, board);

        // スコア降順
        actions.Sort((a, b) => b.Score.CompareTo(a.Score));
        return actions;
    }

    // ================================================================
    //  スコア計算
    // ================================================================
    static float CalcScore(AIAction action, AIPersonality p, AIBoardState board, AILearning learning)
    {
        float baseScore = CalcBaseScore(action, board);
        float majorBonus = AIPersonalityScoring.CalcMajorBonus(action, p, board);
        float traitBonus = AIPersonalityScoring.CalcTraitBonus(action, p, board);
        float situationBonus = AIPersonalityScoring.CalcSituationBonus(action, p, board);
        float learnBonus = learning != null ? learning.GetBonus(action, board) : 0f;

        return baseScore + majorBonus + traitBonus + situationBonus + learnBonus;
    }

    // ---- 基本評価（各専門クラスへ委譲） ----
    static float CalcBaseScore(AIAction action, AIBoardState board)
    {
        switch (action.ActionType)
        {
            case AIActionType.Attack:
                return AICombatEvaluator.CalcAttackBaseScore(action, board);
            case AIActionType.Move:
                return AICombatEvaluator.CalcMoveBaseScore(action, board);
            case AIActionType.SkillUse:
                return AICombatEvaluator.CalcSkillBaseScore(action, board);
            case AIActionType.Retreat:
                return AICombatEvaluator.CalcRetreatBaseScore(action, board);
            case AIActionType.Support:
                return AICombatEvaluator.CalcSupportBaseScore(action, board);
            case AIActionType.Surround:
                return AICombatEvaluator.CalcSurroundBaseScore(action, board);
            case AIActionType.DefenseRepos:
                return AICombatEvaluator.CalcDefenseReposBaseScore(action, board);
            case AIActionType.Build:
                return AIBuildEvaluator.CalcBuildBaseScore(action, board);
            case AIActionType.Summon:
                return AIBuildEvaluator.CalcSummonBaseScore(action, board);
            case AIActionType.SubCrystal:
                return AIBuildEvaluator.CalcSubCrystalBaseScore(action, board);
            case AIActionType.Wait:
                return 1f;
            default:
                return 5f;
        }
    }

    // ================================================================
    //  次ターン反撃圏ペナルティ
    // ================================================================
    public static void ApplyCounterDangerPenaltyInternal(List<AIAction> actions, AIBoardState board, AIPersonality personality)
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
    public static void ApplyRetreatRegroupBonusInternal(List<AIAction> actions, AIPersonality p, AIBoardState board)
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
    public static void ApplyBossFrontlineConditionsInternal(List<AIAction> actions, AIPersonality p, AIBoardState board)
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
    public static void ApplyGradualArmyExpansionInternal(List<AIAction> actions, AIBoardState board)
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
    //  ヘルパー
    // ================================================================

    /// <summary>
    /// 退路安全性評価: 撤退先からさらに移動可能なマスのうち
    /// 敵の攻撃圏外に出られるマスがどれだけあるかを評価する。
    /// </summary>
    static float EvalRetreatPathSafety(Vector3 retreatPos, Status unit, AIBoardState board)
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

    // ================================================================
    //  公開ラッパー: AICommander の建築先行フェーズから利用
    // ================================================================

    /// <summary>建築候補のみを生成して results に追加する</summary>
    public static void GenerateBuildCandidatesPublic(AIBoardState board, List<AIAction> results)
        => AIActionGenerator.GenerateBuildCandidates(board, results);

    /// <summary>サブクリスタル候補のみを生成して results に追加する</summary>
    public static void GenerateSubCrystalCandidatesPublic(AIBoardState board, List<AIAction> results)
        => AIActionGenerator.GenerateSubCrystalCandidates(board, results);

    /// <summary>建築アクション用のスコアを計算する</summary>
    public static float CalcBuildScorePublic(AIAction action, AIPersonality p, AIBoardState board, AILearning learning)
        => CalcScore(action, p, board, learning);
}
