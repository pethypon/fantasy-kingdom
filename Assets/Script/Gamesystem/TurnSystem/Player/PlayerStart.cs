using UnityEngine;

public class PlayerStart : TurnState
{
    public PlayerStart(TurnGenerater turn) : base(turn) { }

    public override void Entry()
    {
        // 敵ターンバナーを非表示
        EnemyTurnBannerUI.Hide();

        // ターンカウント更新
        Context.Turn++;
        ActionLogUI.LogTurnStart(Context.Turn, Team.Player);

        // クリスタルシールドのターン経過（自陣営 — 敵側は EnemyStart で処理）
        BattleSystem.TickCrystalShields(Systems.CrystalSystem.Playercrystal);

        // 状態異常ティック（DoTダメージ + ターン経過）
        StatusEffectSystem.TickAllUnits(Systems.UnitSetting.PlayerUnit);

        // Special Ability: ターン開始時処理（フラグリセット + 浄化光輪）
        SpecialAbilitySystem.OnTurnStart(Systems.UnitSetting.PlayerUnit);

        // AP リセット（FactionState.ResetAPForTurn で Reset+Plus-Minus を計算）
        Systems.APSystem.ResetAP(Team.Player);

        // 疲労リセット
        Systems.APSystem.ResetFatigue(Systems.UnitSetting.PlayerUnit);

        // サブクリスタル返却待ちタイマー処理
        if (Systems.SubCrystalSystem != null)
            Systems.SubCrystalSystem.TickPendingReturns(Team.Player);
        else
            Debug.LogWarning("[PlayerStart] subCrystalSystem が null のため TickPendingReturns をスキップ");

        // 移動Undo履歴クリア
        if (Systems.MoveUndoSystem != null)
            Systems.MoveUndoSystem.Clear();

        // タイマー開始（プレイヤーターン）
        if (Systems.TimerSystem != null)
            Systems.TimerSystem.StartTurn(Team.Player);

        Turn.ChangeState(new PlayerMove(Turn));
    }

    public override void Update() { }
    public override void Exit() { }
}
