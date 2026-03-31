using UnityEngine;

public class PlayerStart : StateCore
{
    private readonly GameContext _ctx;

    public PlayerStart(GameContext ctx)
    {
        _ctx = ctx;
    }

    public void Entry()
    {
        // 敵ターンバナーを非表示
        EnemyTurnBannerUI.Hide();

        // ターンカウント更新
        _ctx.TurnGen.Turn++;
        ActionLogUI.LogTurnStart(_ctx.TurnGen.Turn, Team.Player);

        // クリスタルシールドのターン経過（自陣営 — 敵側は EnemyStart で処理）
        BattleSystem.TickCrystalShields(_ctx.CrystalSystem.Playercrystal);

        // 状態異常ティック（DoTダメージ + ターン経過）
        StatusEffectSystem.TickAllUnits(_ctx.UnitSetting.PlayerUnit);

        // AP リセット（FactionState.ResetAPForTurn で Reset+Plus-Minus を計算）
        _ctx.TurnGen.apsystem.ResetAP(Team.Player);

        // 疲労リセット
        _ctx.TurnGen.apsystem.ResetFatigue(_ctx.UnitSetting.PlayerUnit);

        // サブクリスタル返却待ちタイマー処理
        if (_ctx.TurnGen.subCrystalSystem != null)
            _ctx.TurnGen.subCrystalSystem.TickPendingReturns(Team.Player);
        else
            Debug.LogWarning("[PlayerStart] subCrystalSystem が null のため TickPendingReturns をスキップ");

        // 移動Undo履歴クリア
        if (_ctx.TurnGen.moveUndoSystem != null)
            _ctx.TurnGen.moveUndoSystem.Clear();

        // タイマー開始（プレイヤーターン）
        if (_ctx.TurnGen.timerSystem != null)
            _ctx.TurnGen.timerSystem.StartTurn(Team.Player);

        _ctx.TurnGen.ChangeState(new PlayerMove(_ctx));
    }

    public void Update() { }
    public void Exit() { }
}
