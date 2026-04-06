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

        // 事後補正は AIActionModifiers に委譲
        AIActionModifiers.ApplyCounterDangerPenalty(actions, board, personality);
        AIActionModifiers.ApplyRetreatRegroupBonus(actions, personality, board);
        AIActionModifiers.ApplyBossFrontlineConditions(actions, personality, board);
        AIActionModifiers.ApplyGradualArmyExpansion(actions, board);

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
