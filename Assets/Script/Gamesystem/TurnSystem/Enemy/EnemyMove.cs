using UnityEngine;

public class EnemyMove : TurnState
{
    public EnemyMove(TurnGenerator turn) : base(turn) { }
    private System.Collections.IEnumerator actions;
    private bool finished;
    private bool timeExpired;
    private bool budgetSynchronized, menuPaused;
    private readonly System.Diagnostics.Stopwatch elapsed = new System.Diagnostics.Stopwatch();
    private double deadlineMs;
    private long previousBudgetSequence;

    public override void Entry()
    {
        finished = false;
        timeExpired = false;
        budgetSynchronized = menuPaused = false;
        // Initialization is bounded too. The first core step commits the observed
        // category before its first yield; then this becomes that exact decision's deadline.
        deadlineMs = AITurnBudget.ClampCompletionDeadline(
            AIThinkingBudgetConfig.MaximumMilliseconds + AIThinkingBudgetConfig.MaximumCompletionGraceMilliseconds,
            Systems.TimerSystem != null ? Systems.TimerSystem.AvailableAIThinkingMilliseconds(Team.Enemy) : double.PositiveInfinity);
        previousBudgetSequence = Systems.AICommander?.ThinkingBudgetSequence ?? 0;
        elapsed.Restart();
        if (Systems.TimerSystem != null)
        {
            Systems.TimerSystem.OnTurnTimeExpired += Expire;
            Systems.TimerSystem.OnTotalTimeExpired += EndGame;
        }
        RefreshVision();
        actions = Systems.AICommander?.ExecuteTurnSteps();
    }

    public override void Update()
    {
        if (finished || Turn.IsGameOver || Turn.CurrentState != this) return;
        if (menuPaused) { menuPaused = false; elapsed.Start(); AITurnBudget.ResumeWaitLimit(); }
        if (timeExpired || elapsed.Elapsed.TotalMilliseconds >= deadlineMs)
        {
            if (!timeExpired) Debug.LogWarning("[EnemyMove] AI completion deadline reached; ending the enemy turn.");
            FinishTurn();
            return;
        }
        bool more = false;
        try { AITurnBudget.Resume(); more = actions != null && actions.MoveNext(); }
        catch (System.Exception e) { Debug.LogException(e); }
        finally { AITurnBudget.Pause(); }
        if (!budgetSynchronized && Systems.AICommander != null
            && Systems.AICommander.TryGetPreparedThinkingBudget(previousBudgetSequence, Team.Enemy, out var decision))
        {
            deadlineMs = AITurnBudget.ClampCompletionDeadline(decision.CompletionDeadlineMilliseconds);
            budgetSynchronized = true;
        }
        if (!more && !Turn.IsGameOver && Turn.CurrentState == this) FinishTurn();
    }

    private void Expire() => timeExpired = true;
    private void EndGame(GameResult result) => Turn.ChangeState(new GameEndState(Turn, result));

    public void PauseForMenu()
    {
        if (finished || actions == null || menuPaused) return;
        menuPaused = true; elapsed.Stop(); AITurnBudget.SuspendWaitLimit();
    }

    private void DisposeActions()
    {
        var pending = actions;
        actions = null; // Reentrant state changes must not dispose the same iterator twice.
        (pending as System.IDisposable)?.Dispose();
    }

    private void FinishTurn()
    {
        if (finished || Turn.CurrentState != this) return;
        finished = true;
        elapsed.Stop();
        Systems.TimerSystem?.StopTurn();
        try
        {
            try { DisposeActions(); }
            catch (System.Exception e) { Debug.LogException(e); }
            RefreshVision();
            if (Systems.UnitSetting != null)
                SpecialAbilitySystem.OnTurnEnd(Systems.UnitSetting.EnemyUnit);
            if (!Turn.IsGameOver) Systems.EconomySystem?.ProcessTurn(Team.Enemy);
            if (!Turn.IsGameOver) Systems.BuildingAttackSystem?.ProcessAttacks(Team.Enemy);
            if (!Turn.IsGameOver) SpecialSkillRuntime.OnOwnerTurnEnd(Systems, Team.Enemy, Context.Turn);
        }
        catch (System.Exception e) { Debug.LogException(e); }
        finally
        {
            Systems.AICommander?.EndReflectionTurn();
            Systems.AICommander?.EndExplorationTurn();
            // Never leave an inert, finished EnemyMove installed after an end-of-turn error.
            if (!Turn.IsGameOver && Turn.CurrentState == this)
                Turn.ChangeState(new IndependentFactionState(Turn));
        }
    }

    public override void Exit()
    {
        elapsed.Stop();
        if (menuPaused) { menuPaused = false; AITurnBudget.ResumeWaitLimit(); }
        if (Systems.TimerSystem != null)
        {
            Systems.TimerSystem.OnTurnTimeExpired -= Expire;
            Systems.TimerSystem.OnTotalTimeExpired -= EndGame;
        }
        try { DisposeActions(); }
        finally { RefreshVision(); }
    }
}
