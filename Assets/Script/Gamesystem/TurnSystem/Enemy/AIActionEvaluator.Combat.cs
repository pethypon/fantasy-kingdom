// =====================================================================
//  AIActionEvaluator.Combat — 戦闘系スコアのディスパッチ
// =====================================================================
public static partial class AIActionEvaluator
{
    /// <summary>Pressure on an observed unarmed facility competes with planned
    /// recruitment and deployment. A nonlethal chip is not an urgent battle by
    /// itself; lethal hits, armed defenses and victory targets keep their tier.</summary>
    public static bool IsNonFinishingPassiveStructureAttack(AIAction action, AIBoardState board)
    {
        var target = action?.TargetUnit;
        if (board == null || action?.ActionType != AIActionType.Attack || action.Unit == null
            || target == null || !target.IsAlive || target.team == board.ActorTeam
            || !board.IsSpecialActorObserved(target) || target.ATK > 0
            || target.kind == Kind.Crystal || MatchObjectiveRules.IsCommander(target)
            || target.type != Type.Building && target.type != Type.Wall && target.kind != Kind.SubCrystal)
            return false;
        return !CanFinishSpecialTarget(action.Unit, target, board);
    }

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
