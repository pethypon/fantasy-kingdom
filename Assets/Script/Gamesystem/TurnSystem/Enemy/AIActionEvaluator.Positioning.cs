using System.Collections.Generic;

// =====================================================================
//  AIActionEvaluator.Positioning — 局面補正（事後モディファイア集約）
// =====================================================================
public static partial class AIActionEvaluator
{
    /// <summary>
    /// 位置/局面に関する事後補正を一括適用する。
    /// 従来 EvaluateAll 直書きだった AIActionModifiers 呼び出しを集約。
    /// </summary>
    static void ApplyPositioningModifiers(
        List<AIAction> actions,
        AIPersonality personality,
        AIBoardState board)
    {
        AIActionModifiers.ApplyCounterDangerPenalty(actions, board, personality);
        AIActionModifiers.ApplyRetreatRegroupBonus(actions, personality, board);
        AIActionModifiers.ApplyBossFrontlineConditions(actions, personality, board);
        AIActionModifiers.ApplyGradualArmyExpansion(actions, board);
    }
}
