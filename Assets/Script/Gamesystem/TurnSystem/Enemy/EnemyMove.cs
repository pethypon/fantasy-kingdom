using UnityEngine;

public class EnemyMove : StateCore
{
    private readonly GameContext _ctx;

    public EnemyMove(GameContext ctx)
    {
        _ctx = ctx;
    }

    public void Entry()
    {
        _ctx.RefreshVision();

        // ========================================
        //  AI指揮官による全体指揮実行
        // ========================================
        if (_ctx.TurnGen.aiCommander != null)
        {
            _ctx.TurnGen.aiCommander.ExecuteTurn();
        }
        else
        {
            Debug.LogWarning("[EnemyMove] AICommander未初期化 — AI行動スキップ");
        }

        // 視界再計算（AI行動後 — 同一フレームでもキャッシュを無効化して強制更新）
        _ctx.VisionGen.InvalidateCache();
        _ctx.RefreshVision();

        // Enemy の Special Ability + 資源獲得＋建築物攻撃
        _ctx.ProcessTurnEnd(Team.Enemy);

        // タイマー停止
        if (_ctx.TurnGen.timerSystem != null)
            _ctx.TurnGen.timerSystem.StopTurn();

        // プレイヤーターンへ
        _ctx.TurnGen.ChangeState(new PlayerStart(_ctx));

        Debug.Log("[EnemyMove] 敵ターン終了");
    }

    public void Update() { }

    public void Exit()
    {
        _ctx.RefreshVision();
    }
}
