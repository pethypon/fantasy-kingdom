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
        AIDecisionObserver.Emit(board, actions, null, "score:Safety_counter");
        AIActionModifiers.ApplyRetreatRegroupBonus(actions, personality, board);
        AIDecisionObserver.Emit(board, actions, null, "score:Safety_regroup");
        AIActionModifiers.ApplyBossFrontlineConditions(actions, personality, board);
        AIDecisionObserver.Emit(board, actions, null, "score:Safety_commander");
        AIActionModifiers.ApplyGradualArmyExpansion(actions, board);
        AIDecisionObserver.Emit(board, actions, null, "score:EconomyFuture_expansion");
        AIActionModifiers.ApplyTerrainAwareness(actions, board);
        AIDecisionObserver.Emit(board, actions, null, "score:OperationalProgress_terrain");
        AIActionModifiers.ApplyDungeonAwareness(actions, board);
        AIDecisionObserver.Emit(board, actions, null, "score:OperationalProgress_dungeon");
    }
}
