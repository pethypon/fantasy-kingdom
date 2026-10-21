// =====================================================================
//  AIActionEvaluator.Combat — 戦闘系スコアのディスパッチ
// =====================================================================
public static partial class AIActionEvaluator
{
    // ---- 基本評価（各専門クラスへ委譲） ----
    static float CalcBaseScore(AIAction action, AIBoardState board)
    {
        switch (action.ActionType)
        {
            case AIActionType.Attack:
                if (action.TargetUnit != null && action.TargetUnit.team == board.ActorTeam)
                    return -10000;
                return AICombatEvaluator.CalcAttackBaseScore(action, board);
            case AIActionType.Move:
                return AICombatEvaluator.CalcMoveBaseScore(action, board);
            case AIActionType.SkillUse:
                if (action.SpecialCommand != null) return CalcSpecialSkillScore(action, board);
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
            case AIActionType.Upgrade:
                return CalcUpgradeBaseScore(action, board);
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
}
