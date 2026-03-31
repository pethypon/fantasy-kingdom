using UnityEngine;

public class EnemyMove : TurnState
{
    public EnemyMove(TurnGenerater turn) : base(turn) { }

    public override void Entry()
    {
        RefreshVision();

        // AI指揮官による全体指揮実行
        if (Systems.AICommander != null)
        {
            Systems.AICommander.ExecuteTurn();
        }
        else
        {
            Debug.LogWarning("[EnemyMove] AICommander未初期化 — AI行動スキップ");
        }

        // 視界再計算（AI行動後）
        RefreshVision();

        // Special Ability: ターン終了時処理（応急処置、聖域反応）
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

        // プレイヤーターンへ
        Turn.ChangeState(new PlayerStart(Turn));

        Debug.Log("[EnemyMove] 敵ターン終了");
    }

    public override void Update() { }

    public override void Exit()
    {
        RefreshVision();
    }
}
