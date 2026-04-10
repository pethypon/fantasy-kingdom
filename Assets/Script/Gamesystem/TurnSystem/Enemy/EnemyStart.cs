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

        // 共通ターン開始処理
        TurnStartHelper.ProcessTurnStart(Systems, Team.Enemy);

        Turn.ChangeState(new EnemyMove(Turn));
    }

    public override void Update() { }
    public override void Exit() { }
}
