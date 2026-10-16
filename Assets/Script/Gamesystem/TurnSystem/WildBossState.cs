using UnityEngine;

/// <summary>
/// 強敵（ワイルドボス）の専用ターン。
/// ターン順: プレイヤー → 異形（敵）→ [魔物] → 強敵 → プレイヤー
/// 縄張り内にプレイヤー・敵・魔物がいれば行動し、いなければスキップ。
/// Entry() で処理を完結させ、直後に PlayerStart へ遷移する。
/// </summary>
public class WildBossState : TurnState
{
    public WildBossState(TurnGenerator turn) : base(turn) { }

    public override void Entry()
    {
        try
        {
            SpecialSkillRuntime.OnOwnerTurnStart(Systems, Team.Obstacle, Context.Turn);
            StatusEffectSystem.TickAllUnits(Team.Obstacle, Systems.WildBossSystem?.BossParent);
            Systems.MapCreate?.Environment?.OnOwnerTurnStart(Team.Obstacle, Context.Turn, Systems);
            NaturalRecoverySystem.ProcessOwnerTurn(Systems, Team.Obstacle, Context.Turn);
            if (Turn.IsGameOver) return;
            Systems.WildBossSystem?.ProcessTurn();
            RefreshVision();
        }
        finally
        {
            if (!Turn.IsGameOver && Turn.CurrentState == this)
                Turn.ChangeState(new PlayerStart(Turn));
        }
    }
}
