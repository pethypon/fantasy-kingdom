using UnityEngine;

// =====================================================================
//  SimBoardState.Turn — ターン遷移シミュレーション
//  ターン間で発生するイベントを再現:
//  - AP リセット, 疲労リセット
//  - DoT ダメージ (毒8, 出血6)
//  - ステータス効果ティック (ターン減算 + 除去)
//  - クリスタルシールドティック
//  - スキルクールダウン減算
// =====================================================================
public partial class SimBoardState
{
    public void SimulateTurnTransition(Team nextTeam)
    {
        TurnCount++;
        ResetAP(nextTeam);

        for (int i = 0; i < Units.Count; i++)
        {
            var u = Units[i];
            if (!u.IsAlive || u.Team != nextTeam) continue;

            u.Fatigue = 0;
            ApplyDoT(u);
            TickEffects(u);
            TickCooldowns(u);
        }

        RemoveDeadFromOccupied();
    }

    private void ResetAP(Team team)
    {
        if (team == Team.Enemy)
            EnemyAP = EnemyAPReset;
        else
            PlayerAP = PlayerAPReset;
    }

    private static void ApplyDoT(SimUnit u)
    {
        for (int j = 0; j < u.Effects.Count; j++)
        {
            var eff = u.Effects[j];
            if (eff.Debuff == StatusEffectType.Poison)
                u.HP = Mathf.Max(0, u.HP - GameConstants.PoisonDamagePerTurn);
            else if (eff.Debuff == StatusEffectType.Bleed)
                u.HP = Mathf.Max(0, u.HP - GameConstants.BleedDamagePerTurn);
        }
    }

    private static void TickEffects(SimUnit u)
    {
        for (int j = u.Effects.Count - 1; j >= 0; j--)
        {
            var eff = u.Effects[j];
            eff.RemainingTurns--;
            if (eff.RemainingTurns <= 0)
                u.Effects.RemoveAt(j);
            else
                u.Effects[j] = eff;
        }
    }

    private static void TickCooldowns(SimUnit u)
    {
        if (u.ShieldTurns > 0)
            u.ShieldTurns--;
        if (u.SkillCooldown > 0)
            u.SkillCooldown--;
    }

    private void RemoveDeadFromOccupied()
    {
        for (int i = 0; i < Units.Count; i++)
        {
            if (!Units[i].IsAlive && Units[i].Type == Type.Unit)
                _occupiedCells.Remove(Units[i].Position);
        }
    }
}
