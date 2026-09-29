using UnityEngine;

public class EnemyMove : TurnState
{
    public EnemyMove(TurnGenerator turn) : base(turn) { }

    private System.Collections.IEnumerator actions;
    private bool finished;
    private bool timeExpired;
    public override void Entry()
    {
        if (Systems.TimerSystem != null) { Systems.TimerSystem.OnTurnTimeExpired += Expire; Systems.TimerSystem.OnTotalTimeExpired += EndGame; }
        RefreshVision();
        actions = Systems.AICommander?.ExecuteTurnSteps();
    }
    public override void Update()
    {
        if (finished || Turn.IsGameOver) return;
        if (timeExpired) { finished = true; FinishTurn(); return; }
        bool more = false;
        try { AITurnBudget.Resume(); more = actions != null && actions.MoveNext(); }
        catch (System.Exception e) { Debug.LogException(e); }
        finally { AITurnBudget.Pause(); }
        if (more || Turn.IsGameOver) return;
        finished = true;
        FinishTurn();
    }
    private void Expire() => timeExpired = true;
    private void EndGame(GameResult result) => Turn.ChangeState(new GameEndState(Turn, result));
    private void FinishTurn()
    {
        Systems.TimerSystem?.StopTurn();
        // 視界再計算（AI行動後）
        RefreshVision();

        // Special Ability: ターン終了時処理（応急処置、聖域反応）
        if (Systems.UnitSetting != null)
            SpecialAbilitySystem.OnTurnEnd(Systems.UnitSetting.EnemyUnit);

        // Enemy の資源獲得（ターン終了時）
        if (Systems.EconomySystem != null)
            Systems.EconomySystem.ProcessTurn(Team.Enemy);

        // Enemy の攻撃建築物による自動攻撃
        if (Systems.BuildingAttackSystem != null)
            Systems.BuildingAttackSystem.ProcessAttacks(Team.Enemy);

        // タイマー停止
        if (Systems.TimerSystem != null)
            Systems.TimerSystem.StopTurn();

        // 強敵ターンへ（スポーン済みでなければ即 PlayerStart へ）
        Turn.ChangeState(new IndependentFactionState(Turn));

        DevelopmentLog.Log("[EnemyMove] 敵ターン終了");
    }


    public override void Exit()
    {
        if (Systems.TimerSystem != null) { Systems.TimerSystem.OnTurnTimeExpired -= Expire; Systems.TimerSystem.OnTotalTimeExpired -= EndGame; }
        (actions as System.IDisposable)?.Dispose();
        actions = null;
        RefreshVision();
    }
}
