using UnityEngine;

public class PlayerStart : TurnState
{
    public PlayerStart(TurnGenerator turn) : base(turn) { }

    public override void Entry()
    {
        // 敵ターンバナーを非表示
        EnemyTurnBannerUI.Hide();

        // ターンカウント更新
        Context.Turn++;
        ActionLogUI.LogTurnStart(Context.Turn, Team.Player);

        // 共通ターン開始処理
        TurnStartHelper.ProcessTurnStart(Systems, Team.Player);

        // 占有セルを再構築（敵の最終位置・WildBoss本体/子駒を反映）。
        // これがないと敵ターン中に動いた駒や WildBoss の上にプレイヤーが
        // 移動できてしまう。
        Systems.MoveGenerator?.UnitPointCore();

        // 移動Undo履歴クリア
        if (Systems.MoveUndoSystem != null)
            Systems.MoveUndoSystem.Clear();

        Turn.ChangeState(new PlayerMove(Turn));
    }

    public override void Update() { }
    public override void Exit() { }
}
