using UnityEngine;

public class EnemyStart : TurnState
{
    public EnemyStart(TurnGenerator turn) : base(turn) { }

    public override void Entry()
    {
        Debug.Log("[EnemyStart] 敵ターン開始");

        // プレイヤーの残留MovePoint/AttackPointをクリア
        Systems.MoveGenerator.MoveReset();
        Systems.AttackGenerator.AtkpDestroy();

        // 選択状態もクリア
        Context.SelectUnit = null;

        if (Systems.UnitPanelUI != null)
            Systems.UnitPanelUI.Hide();

        if (Systems.InputHintUI != null)
            Systems.InputHintUI.SetHints(InputHintUI.Hints.EnemyTurn);

        // 敵ターンバナー表示
        EnemyTurnBannerUI.Show();

        // クリスタルシールドのターン経過（敵陣営）
        BattleSystem.TickCrystalShields(Systems.CrystalSystem.Enemycrystal);

        // 状態異常ティック
        StatusEffectSystem.TickAllUnits(Systems.UnitSetting.EnemyUnit);

        // Special Ability: ターン開始時処理
        SpecialAbilitySystem.OnTurnStart(Systems.UnitSetting.EnemyUnit);

        Systems.APSystem.ResetAP(Team.Enemy);
        Systems.APSystem.ResetFatigue(Systems.UnitSetting.EnemyUnit);

        // サブクリスタル返却待ちタイマー処理
        if (Systems.SubCrystalSystem != null)
            Systems.SubCrystalSystem.TickPendingReturns(Team.Enemy);

        // タイマー開始（敵ターン）
        if (Systems.TimerSystem != null)
            Systems.TimerSystem.StartTurn(Team.Enemy);

        Turn.ChangeState(new EnemyMove(Turn));
    }

    public override void Update() { }
    public override void Exit() { }
}
