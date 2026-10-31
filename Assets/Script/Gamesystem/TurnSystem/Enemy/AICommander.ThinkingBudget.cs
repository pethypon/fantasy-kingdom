using System;
using UnityEngine;

public partial class AICommander
{
    public AIThinkingBudgetDecision ActiveThinkingBudget { get; private set; }
    public bool HasPreparedThinkingBudget { get; private set; }
    // Attempt identity, not a game turn or save counter. Drivers must not adopt a previous attempt's prepared decision.
    public long ThinkingBudgetSequence { get; private set; }
    float configuredThinkingBudgetMs = 3000f;
    double formalThinkingWindowMs;

    // Starts before the core iterator's setup. Resolving the category changes only
    // the limit, retaining initialization CPU/wall time instead of starting a second clock.
    void BeginThinkingBudgetSelection()
    {
        HasPreparedThinkingBudget = false;
        ActiveThinkingBudget = default;
        ThinkingBudgetSequence = ThinkingBudgetSequence == long.MaxValue ? 1 : ThinkingBudgetSequence + 1;
        var timer = _turnGen?.Systems?.TimerSystem;
        formalThinkingWindowMs = timer != null ? timer.AvailableAIThinkingMilliseconds(_actorTeam) : double.PositiveInfinity;
        // Classification and initialization already share the formal side's remaining window.
        // A zero/invalid formal clock must not grant a fresh eight-second setup allowance.
        AITurnBudget.Begin(AITurnBudget.ClampCompletionDeadline(AIThinkingBudgetConfig.MaximumMilliseconds, formalThinkingWindowMs));
    }

    public bool TryGetPreparedThinkingBudget(long previousSequence, Team expectedTeam, out AIThinkingBudgetDecision decision)
    {
        decision = ActiveThinkingBudget;
        return HasPreparedThinkingBudget && ThinkingBudgetSequence != previousSequence && decision.Team == expectedTeam;
    }

    void ResolveTurnThinkingBudget(AIBoardState board)
    {
        if (HasPreparedThinkingBudget) return;
        AIThinkingBudgetFacts facts;
        try { facts = ThinkingFacts(board); }
        catch (Exception error)
        {
            // Failure of this optional classifier must not invent a threat or stop
            // the legal-action pipeline. Its ordinary fallback remains bounded.
            facts = default;
            if (DevelopmentLog.IsEnabled(AILogLevel.Verbose))
                DevelopmentLog.Log("[AI思考時間] 分類を通常判断へ戻しました: " + error.GetType().Name);
        }
        ActiveThinkingBudget = AIThinkingBudgetConfig.Active.Decide(facts, _actorTeam,
            board?.TurnCount ?? _turnCount, formalThinkingWindowMs, configuredThinkingBudgetMs);
        AITurnBudget.SetLimitWithoutRestart(ActiveThinkingBudget.Milliseconds);
        HasPreparedThinkingBudget = true;
        if (DevelopmentLog.IsEnabled(AILogLevel.Verbose))
            DevelopmentLog.Log("[AI思考時間] side=" + _actorTeam + " turn=" + ActiveThinkingBudget.Turn
                + " category=" + ActiveThinkingBudget.Category + " budgetMs=" + ActiveThinkingBudget.Milliseconds
                + " completionMs=" + ActiveThinkingBudget.CompletionDeadlineMilliseconds + " reason=" + ActiveThinkingBudget.Reason);
    }

    static AIThinkingBudgetFacts ThinkingFacts(AIBoardState board)
    {
        if (board == null) return default;
        // PlayerCrystalVisible is filled by the board's legal observation path.
        if (board.PlayerCrystalVisible || board.Governor?.Mode == StrategicMode.EmergencyDefense)
            return new AIThinkingBudgetFacts(true, false, true);
        bool combat = false;
        int exposedLethalUnits = 0;
        foreach (var actor in board.AliveEnemyUnits)
        {
            if (actor == null || !actor.IsAlive || actor.team != board.ActorTeam || actor.type != Type.Unit) continue;
            float incoming = board.Governor?.IncomingDamage(board, actor, actor.transform.position, null) ?? 0;
            if (incoming > 0) combat = true;
            if (incoming >= actor.HP) exposedLethalUnits++;
            if (StatusEffectSystem.IsStunned(actor)) continue;
            if (!combat && actor.SkillCooldown == 0 && AttackPatterns.CanUseSkills(actor)
                && !StatusEffectSystem.HasDebuff(actor, StatusEffectType.Seal)
                && SkillData.Table.TryGetValue(actor.AssignedSkillId, out var skill) && skill.APCost <= board.EnemyAP)
                foreach (var target in board.GetSkillTargets(actor, skill))
                    if (target != null && target.team != board.ActorTeam && target.IsAlive && board.IsSpecialActorObserved(target))
                    { combat = true; break; }
            if (board.CalcAttackCost(actor) > board.EnemyAP || BoardActionProfile.For(actor)?.CanAttack == false) continue;
            foreach (var target in board.GetAttackTargets(actor))
            {
                if (target == null || !target.IsAlive || !board.IsSpecialActorObserved(target)) continue;
                combat = true;
                if ((target.kind == Kind.Crystal || MatchObjectiveRules.IsCommander(target))
                    && AIActionEvaluator.CanFinishSpecialTarget(actor, target, board))
                    return new AIThinkingBudgetFacts(true, false, true);
            }
        }
        // Three observed potentially lethal exposures can destroy a standard
        // squad. This is an uncertainty-aware time allocation, not a claim that
        // the opponent will actually pay for or choose every predicted attack.
        if (exposedLethalUnits >= 3 && board.Operations?.HasCoordinatedSquad == true)
            return new AIThinkingBudgetFacts(true, true, combat);
        // Merely owning three troops must not make every peaceful turn a six-second
        // turn. Coordination gets the larger budget for an actual observed engagement.
        bool coordination = combat && board.Operations?.HasCoordinatedSquad == true;
        return new AIThinkingBudgetFacts(false, coordination, combat);
    }
}
