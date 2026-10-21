using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Durable per-life recovery state. Turn stamps prevent duplicate end callbacks and reload healing.</summary>
[Serializable]
public sealed class BarrierRecoveryState
{
    public int SafeTurns;
    public int LastDamagedRound = -1;
    public bool HasRecovered, HasOwnTurnBaseline;
    public bool DamageSinceOwnTurn;
    public int LastProcessedRound = -1;
}

/// <summary>Finite barrier rules shared by live combat, previews and search. Perfect shields remain separate.</summary>
public static class BarrierSystem
{
    public const int GrowthPerLevel = 2, SafeTurnThreshold = 3;
    static readonly List<Status> actors = new List<Status>(64);
    public static bool UsesBarrier(Status actor) => actor != null && !actor.IsPrimaryCommander
        && UsesBarrier(actor.kind, actor.type, actor.isWildBoss);
    public static bool UsesBarrier(Kind kind, Type type, bool wildBoss = false)
        => BarrierCombatSettings.Active.enabledForPlay && type == Type.Unit && !wildBoss
        && IsR1Role(kind);
    public static bool IsR1Role(Kind kind) => kind >= Kind.Knight && kind <= Kind.Bomber || kind >= Kind.WhiteMage && kind <= Kind.DragonKnight;
    public static int Initial(Kind kind) => BarrierCombatSettings.Active.Initial(kind);
    public static int Maximum(int baseValue, int level) => (int)Math.Min(int.MaxValue,
        (long)Mathf.Max(0, baseValue) + GrowthPerLevel * (long)(Mathf.Clamp(level, 1, GameConstants.MaxUnitLevel) - 1));
    public static int RegenerationAmount(int maximum)
    {
        long offset = BarrierCombatSettings.Active.recoveryRounding == BarrierRecoveryRounding.Floor ? 0
            : BarrierCombatSettings.Active.recoveryRounding == BarrierRecoveryRounding.Nearest ? 2 : 4;
        return (int)(((long)Mathf.Max(0, maximum) + offset) / 5);
    }
    public static bool CanRegenerate(int hp, int maximumHP, int safeTurns)
        => hp > 0 && maximumHP > 0 && (long)hp * 10 >= (long)maximumHP * 7 && safeTurns >= SafeTurnThreshold;
    /// <returns>Remaining barrier, with overflow assigned to hpDamage.</returns>
    public static int Absorb(int barrier, int damage, out int hpDamage)
    {
        int absorbed = Mathf.Min(Mathf.Max(0, barrier), Mathf.Max(0, damage));
        hpDamage = Mathf.Max(0, damage) - absorbed;
        return Mathf.Max(0, barrier) - absorbed;
    }
    public static int PreviewHpLoss(int hp, int barrier, int damage, bool bypass = false)
        => (int)Math.Min(Mathf.Max(0, hp), Math.Max(0L, (long)damage - (bypass ? 0 : Mathf.Max(0, barrier))));
    public static int NormalAttackDamage(Status attacker, Status target, int raw)
    {
        var recipient = SpecialSkillRuntime.ResolveDamageRecipient(target, raw, HpChangeKind.NormalDamage) ?? target;
        return NormalAttackDamage(attacker != null ? attacker.kind : Kind.None, recipient != null ? recipient.Barrier : 0, raw);
    }
    public static int NormalAttackDamage(Kind attackerKind, int targetBarrier, int raw)
    {
        // A normal attack never bypasses barrier. Optional breaker bonuses affect only the shielded portion.
        if (attackerKind != Kind.HeavyWarrior || raw <= 0 || targetBarrier <= 0) return Mathf.Max(0, raw);
        float multiplier = Mathf.Max(1, BarrierCombatSettings.Active.heavyNormalBarrierMultiplier);
        return AmplifyBarrierPortion(raw, targetBarrier, multiplier);
    }
    public static int AmplifyBarrierPortion(int raw, int barrier, float multiplier)
    {
        if (raw <= 0) return 0;
        multiplier = float.IsNaN(multiplier) || float.IsInfinity(multiplier) ? 1 : Mathf.Max(1, multiplier);
        // Extra damage cannot spill into HP: only the original unshielded portion can do so.
        int covered = Mathf.Min(raw, Mathf.Max(0, barrier));
        long boosted = (long)Math.Min(int.MaxValue, Math.Ceiling(covered * (double)multiplier));
        return (int)Math.Min(int.MaxValue, Math.Min(Mathf.Max(0, barrier), boosted) + (long)raw - covered);
    }
    public static int PreviewNormalHpDamage(Status attacker, Status target, int raw)
        => SpecialSkillRuntime.PreviewHpDamage(target, NormalAttackDamage(attacker, target, raw));
    public static BarrierRecoveryState CopyState(BarrierRecoveryState value) => value == null ? new BarrierRecoveryState()
        : new BarrierRecoveryState { SafeTurns = Mathf.Clamp(value.SafeTurns, 0, SafeTurnThreshold + 1),
            LastDamagedRound = Mathf.Max(-1, value.LastDamagedRound), HasRecovered = value.HasRecovered, HasOwnTurnBaseline = value.HasOwnTurnBaseline,
            DamageSinceOwnTurn = value.DamageSinceOwnTurn, LastProcessedRound = Mathf.Max(-1, value.LastProcessedRound) };
    public static void RecordDamage(Status actor)
    {
        if (!UsesBarrier(actor)) return;
        var state = actor.BarrierRecovery ?? (actor.BarrierRecovery = new BarrierRecoveryState());
        state.SafeTurns = 0; state.DamageSinceOwnTurn = true; state.HasRecovered = false;
        state.LastDamagedRound = actor.RuntimeSystems?.TurnGenerator?.Context.Turn ?? -1;
    }
    public static void RecordNegatedHit(Status actor)
    { if (BarrierCombatSettings.Active.fullyNegatedHitsResetRecovery) RecordDamage(actor); }
    public static void ProcessOwnerTurnStart(Team team, int round)
    {
        if (!BarrierCombatSettings.Active.recoveryAtOwnerTurnStart) return;
        ProcessOwnerBoundary(team, round, true);
    }
    public static void ProcessOwnerTurnEnd(Team team, int round)
    {
        if (BarrierCombatSettings.Active.recoveryAtOwnerTurnStart) return;
        ProcessOwnerBoundary(team, round, false);
    }
    static void ProcessOwnerBoundary(Team team, int round, bool atStart)
    {
        CombatRegistry.Collect(actors);
        try { foreach (var actor in actors) if (actor != null && actor.team == team) ProcessActorBoundary(actor, round, atStart); }
        finally { actors.Clear(); }
    }
    public static void ProcessActorEnd(Status actor, int round)
    { if (!BarrierCombatSettings.Active.recoveryAtOwnerTurnStart) ProcessActorBoundary(actor, round, false); }
    public static void ProcessActorStart(Status actor, int round)
    { if (BarrierCombatSettings.Active.recoveryAtOwnerTurnStart) ProcessActorBoundary(actor, round, true); }
    static void ProcessActorBoundary(Status actor, int round, bool atStart)
    {
        if (!UsesBarrier(actor) || !actor.IsAlive) return;
        var state = actor.BarrierRecovery ?? (actor.BarrierRecovery = new BarrierRecoveryState());
        if (state.LastProcessedRound == round) return;
        state.LastProcessedRound = round;
        if (!state.HasOwnTurnBaseline)
        {
            state.HasOwnTurnBaseline = true;
            if (atStart) return;
        }
        int amount = AdvanceRecovery(ref state.SafeTurns, ref state.DamageSinceOwnTurn, ref state.HasRecovered,
            actor.HP, actor.MaxHP, actor.MaxBarrier);
        int oldBarrier = actor.Barrier;
        actor.Barrier = (int)Math.Min(actor.MaxBarrier, (long)oldBarrier + amount);
        if (actor.Barrier > oldBarrier) FloatingDamageUI.ShowBarrierRecovery(actor, actor.Barrier - oldBarrier);
    }
    /// <summary>Pure shared state transition; the live/search callers own turn stamps and start baselines.</summary>
    public static int AdvanceRecovery(ref int safeTurns, ref bool damaged, ref bool hasRecovered, int hp, int maximumHP, int maximumBarrier)
    {
        if (damaged) { damaged = false; safeTurns = 0; hasRecovered = false; return 0; }
        safeTurns = Mathf.Min(SafeTurnThreshold + 1, safeTurns + 1);
        int first = BarrierCombatSettings.Active.recoverOnThresholdTurn ? SafeTurnThreshold : SafeTurnThreshold + 1;
        if (safeTurns < first || !CanRegenerate(hp, maximumHP, safeTurns)
            || hasRecovered && !BarrierCombatSettings.Active.repeatRecoveryEveryTurn) return 0;
        hasRecovered = true; return RegenerationAmount(maximumBarrier);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => actors.Clear();
}
