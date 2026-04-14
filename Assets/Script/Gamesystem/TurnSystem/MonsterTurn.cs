using UnityEngine;

/// <summary>
/// 魔物ターン: Enemy ターン終了後、Player ターン開始前に実行される。
/// MonsterAI による全魔物の自動行動 + MonsterSystem の補充判定を行う。
/// </summary>
public class MonsterTurn : TurnState
{
    public MonsterTurn(TurnGenerator turn) : base(turn) { }

    public override void Entry()
    {
        Debug.Log("[MonsterTurn] 魔物ターン開始");

        // 魔物の補充判定（10ターンごと）
        if (Systems.MonsterSystem != null)
            Systems.MonsterSystem.ProcessTurn(Context.Turn);

        // 魔物AI実行
        if (Systems.MonsterAI != null)
            Systems.MonsterAI.ExecuteAllActions();

        // 視界再計算
        RefreshVision();

        Debug.Log("[MonsterTurn] 魔物ターン終了");

        // プレイヤーターンへ
        Turn.ChangeState(new PlayerStart(Turn));
    }

    public override void Update() { }
    public override void Exit() { }
}
