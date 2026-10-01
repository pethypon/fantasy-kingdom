using UnityEngine;

public class EnemyMove : TurnState
{
    public EnemyMove(TurnGenerator turn) : base(turn) { }
    private System.Collections.IEnumerator actions;
    private bool finished;
    private bool timeExpired;
    private readonly System.Diagnostics.Stopwatch elapsed = new System.Diagnostics.Stopwatch();
    private double deadlineMs;

    public override void Entry()
    {
        finished = false;
        timeExpired = false;
        double budget = Systems.AICommander?.TurnThinkingBudgetMs ?? 0;
        if (double.IsNaN(budget) || double.IsInfinity(budget)) budget = 8000;
        deadlineMs = System.Math.Max(0, budget) + 500; // Allow the final evaluated action to finish.
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
        if (!more && !Turn.IsGameOver && Turn.CurrentState == this) FinishTurn();
    }

    private void Expire() => timeExpired = true;
    private void EndGame(GameResult result) => Turn.ChangeState(new GameEndState(Turn, result));

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
        }
        catch (System.Exception e) { Debug.LogException(e); }
        finally
        {
            // Never leave an inert, finished EnemyMove installed after an end-of-turn error.
            if (!Turn.IsGameOver && Turn.CurrentState == this)
                Turn.ChangeState(new IndependentFactionState(Turn));
        }
    }

    public override void Exit()
    {
        elapsed.Stop();
        if (Systems.TimerSystem != null)
        {
            Systems.TimerSystem.OnTurnTimeExpired -= Expire;
            Systems.TimerSystem.OnTotalTimeExpired -= EndGame;
        }
        try { DisposeActions(); }
        finally { RefreshVision(); }
    }
}
