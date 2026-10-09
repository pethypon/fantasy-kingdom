// =====================================================================
//  AIActionEvaluator.Personality — 性格補正 + 学習補正の集約
//  ML 関連は AIConfig.IsMLEnabled によりゲート。
// =====================================================================
public static partial class AIActionEvaluator
{
    // ================================================================
    //  スコア計算
    // ================================================================
    static float CalcScore(AIAction action, AIPersonality p, AIBoardState board, AILearning learning)
    {
        float baseScore     = CalcBaseScore(action, board);
        float majorBonus    = AIPersonalityScoring.CalcMajorBonus(action, p, board);
        float traitBonus    = AIPersonalityScoring.CalcTraitBonus(action, p, board);
        float situationBonus = AIPersonalityScoring.CalcSituationBonus(action, p, board);

        // 学習補正は ML モード有効時のみ適用（PureRule モードでは完全決定論化）
        float learnBonus = 0f;
        if (AIConfig.IsMLEnabled && learning != null)
        {
            learnBonus = learning.GetBonus(action, board);
        }

        board.Governor?.Evaluate(board);
        float foundationScore = board.Governor?.BasicResources.ScoreAction(action) ?? 0;
        float finalScore = baseScore + majorBonus + traitBonus + situationBonus + learnBonus + foundationScore;
        if ((action.ActionType == AIActionType.Build || action.ActionType == AIActionType.Upgrade) && AIEconomySettings.Active.enableDecisionLogs)
            DevelopmentLog.Log($"[AI経済建築] {action.Facility} base={baseScore:F1} "
                + $"personality={majorBonus + traitBonus:F1} situation={situationBonus:F1} learning={learnBonus:F1} foundation={foundationScore:F1} final={finalScore:F1}");
        return finalScore;
    }
}
