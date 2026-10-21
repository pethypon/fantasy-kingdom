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
        foreach (var unit in Units) unit.SimulationRound = TurnCount;
        // Snapshot support before any isolation death removes another friendly observer.
        var outgoingTeam = nextTeam == Team.Enemy ? Team.Player : Team.Enemy;
        ProcessAssassinIsolation(outgoingTeam, TurnCount);
        // Recovery sees the actual HP loss, before the incoming faction's DoT.
        for (int i = 0; i < Units.Count; i++)
            if (Units[i].Team == outgoingTeam)
            {
                if (!BarrierCombatSettings.Active.recoveryAtOwnerTurnStart) ProcessBarrierRecovery(Units[i], TurnCount);
                ProcessNaturalRecovery(Units[i], TurnCount);
            }
        ResetAP(nextTeam);

        for (int i = 0; i < Units.Count; i++)
        {
            var u = Units[i];
            if (!u.IsAlive || u.Team != nextTeam) continue;

            u.Fatigue = 0;
            if (u.VisionZeroTurns > 0) u.VisionZeroTurns--;
            if (u.ScoutVisionPenaltyTurns > 0) u.ScoutVisionPenaltyTurns--;
            ApplyDoT(u);
            TickEffects(u);
            TickCooldowns(u);
            if (BarrierCombatSettings.Active.recoveryAtOwnerTurnStart) ProcessBarrierRecovery(u, TurnCount);
        }

        RemoveDeadFromOccupied();
    }

    [System.ThreadStatic] static System.Collections.Generic.List<SimUnit> isolatedAssassins;
    void ProcessAssassinIsolation(Team team, int round)
    {
        if (isolatedAssassins == null) isolatedAssassins = new System.Collections.Generic.List<SimUnit>();
        isolatedAssassins.Clear();
        foreach (var actor in Units)
        {
            if (!actor.IsAlive || actor.Team != team || actor.Type != Type.Unit || !actor.AssassinPassive
                || actor.AssassinLastEndRound == round) continue;
            actor.AssassinLastEndRound = round;
            if (!HasOtherAllyVision(actor)) isolatedAssassins.Add(actor);
        }
        foreach (var actor in isolatedAssassins)
            if (actor.MaxHP > 0) actor.ApplyHpDirectDamage(Mathf.Max(1, (int)System.Math.Floor(
                actor.MaxHP * (double)SpecialSkillDefinition.AssassinIsolationFraction)));
        isolatedAssassins.Clear();
    }

    private void ResetAP(Team team)
    {
        if (team == Team.Enemy)
            EnemyAP = Mathf.Clamp(EnemyAPReset, 0, APSystem.MaximumAP);
        else
            PlayerAP = Mathf.Clamp(PlayerAPReset, 0, APSystem.MaximumAP);
    }

    private static void ApplyDoT(SimUnit u)
    {
        for (int j = 0; j < u.Effects.Count; j++)
        {
            var eff = u.Effects[j];
            if (eff.Debuff == StatusEffectType.Poison)
                u.ApplyDamage(GameConstants.PoisonDamagePerTurn);
        }
    }

    static void ProcessNaturalRecovery(SimUnit unit, int round)
    {
        if (!unit.IsAlive || unit.Type != Type.Unit || unit.MaxHP <= 0 || unit.NaturalRecoveryLastRound == round) return;
        unit.NaturalRecoveryLastRound = round;
        unit.NaturalRecoveryHasBaseline = true;
        if (unit.NaturalRecoveryHpLost)
        {
            unit.NaturalRecoveryHpLost = false;
            unit.NaturalRecoverySafeTurns = 0;
            return;
        }
        unit.NaturalRecoverySafeTurns = Mathf.Min(NaturalRecoverySystem.SafeTurnThreshold,
            Mathf.Clamp(unit.NaturalRecoverySafeTurns, 0, NaturalRecoverySystem.SafeTurnThreshold) + 1);
        if (unit.NaturalRecoverySafeTurns >= NaturalRecoverySystem.SafeTurnThreshold)
            unit.ApplyHeal(NaturalRecoverySystem.HealAmount(unit.MaxHP));
    }

    static void ProcessBarrierRecovery(SimUnit unit, int round)
    {
        if (!unit.IsAlive || !unit.UsesBarrier || unit.BarrierLastRound == round) return;
        unit.BarrierLastRound = round;
        if (!unit.BarrierHasOwnTurnBaseline)
        {
            unit.BarrierHasOwnTurnBaseline = true;
            if (BarrierCombatSettings.Active.recoveryAtOwnerTurnStart) return;
        }
        int amount = BarrierSystem.AdvanceRecovery(ref unit.BarrierSafeTurns,ref unit.BarrierDamaged,
            ref unit.BarrierHasRecovered,unit.HP,unit.MaxHP,unit.MaxBarrier);
        if (amount > 0) unit.Barrier = (int)System.Math.Min(unit.MaxBarrier,(long)unit.Barrier+amount);
    }

    private static void TickEffects(SimUnit u)
    {
        u.CapturedVisionCells = null;
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
