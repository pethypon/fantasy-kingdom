using UnityEngine;

public class EnemyStart : StateCore
{
    private readonly GameContext _ctx;

    public EnemyStart(GameContext ctx)
    {
        _ctx = ctx;
    }

    public void Entry()
    {
        Debug.Log("[EnemyStart] 敵ターン開始");

        // プレイヤーの残留MovePoint/AttackPointをクリア
        _ctx.ClearPointMarkers();

        // 選択状態もクリア
        _ctx.TurnGen.SelectUnit = null;

        // UnitPanelUI を非表示
        if (_ctx.TurnGen.unitPanelUI != null)
            _ctx.TurnGen.unitPanelUI.Hide();

        // InputHintUI を敵ターン表示に
        if (_ctx.TurnGen.inputHintUI != null)
            _ctx.TurnGen.inputHintUI.SetHints(InputHintUI.Hints.EnemyTurn);

        // 敵ターンバナー表示
        EnemyTurnBannerUI.Show();

        // クリスタルシールドのターン経過（敵陣営）
        BattleSystem.TickCrystalShields(_ctx.CrystalSystem.Enemycrystal);

        // 状態異常ティック（DoTダメージ + ターン経過 + シールド + スキルクールダウン）
        StatusEffectSystem.TickAllUnits(_ctx.UnitSetting.EnemyUnit);

        // Special Ability: ターン開始時処理（フラグリセット + 浄化光輪）
        SpecialAbilitySystem.OnTurnStart(_ctx.UnitSetting.EnemyUnit);

        _ctx.TurnGen.apsystem.ResetAP(Team.Enemy);
        _ctx.TurnGen.apsystem.ResetFatigue(_ctx.UnitSetting.EnemyUnit);

        // サブクリスタル返却待ちタイマー処理
        if (_ctx.TurnGen.subCrystalSystem != null)
            _ctx.TurnGen.subCrystalSystem.TickPendingReturns(Team.Enemy);

        // タイマー開始（敵ターン）
        if (_ctx.TurnGen.timerSystem != null)
            _ctx.TurnGen.timerSystem.StartTurn(Team.Enemy);

        _ctx.TurnGen.ChangeState(new EnemyMove(_ctx));
    }

    public void Update() { }
    public void Exit() { }
}
