using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Durable per-life recovery state. Turn stamps prevent duplicate end callbacks and reload healing.</summary>
[Serializable]
public sealed class BarrierRecoveryState
{
    // Missing in legacy saves on purpose: their safe-hit history is not full-HP history.
    public int RuleRevision;
    public bool FullHPObserved;
    public int SafeTurns;
    public int LastDamagedRound = -1;
    public bool HasRecovered, HasOwnTurnBaseline;
    public bool DamageSinceOwnTurn;
    public int LastProcessedRound = -1;
}

/// <summary>Finite barrier rules shared by live combat, previews and search. Perfect shields remain separate.</summary>
public static class BarrierSystem
{
    public const int GrowthPerLevel = 0, SafeTurnThreshold = 3, RecoveryRuleRevision = 2;
    static readonly List<Status> actors = new List<Status>(64);
    public static bool UsesBarrier(Status actor) => actor != null && !actor.IsPrimaryCommander
        && (actor.kind == Kind.Engineer ? !actor.isWildBoss && UsesBarrier(actor.GrowthData, actor.type)
            : UsesBarrier(actor.kind, actor.type, actor.isWildBoss));
    public static bool UsesBarrier(UnitData definition, Type type)
        => definition != null && (definition.kind == Kind.Engineer
            ? BarrierCombatSettings.Active.enabledForPlay && type == Type.Unit && definition.baseBarrier >= 0
            : UsesBarrier(definition.kind, type));
    public static bool UsesBarrier(Kind kind, Type type, bool wildBoss = false)
        => BarrierCombatSettings.Active.enabledForPlay && type == Type.Unit && !wildBoss
        && IsR1Role(kind);
    public static bool IsR1Role(Kind kind) => kind >= Kind.Knight && kind <= Kind.Bomber || kind >= Kind.WhiteMage && kind <= Kind.DragonKnight;
    public static int Initial(Kind kind) => BarrierCombatSettings.Active.Initial(kind);
    public static int Maximum(int baseValue, int level) => Mathf.Max(0, baseValue);
    public static int RegenerationAmount(int maximum)
    {
        long offset = BarrierCombatSettings.Active.recoveryRounding == BarrierRecoveryRounding.Floor ? 0
            : BarrierCombatSettings.Active.recoveryRounding == BarrierRecoveryRounding.Nearest ? 2 : 4;
        return (int)(((long)Mathf.Max(0, maximum) + offset) / 5);
    }
    public static bool CanRegenerate(int hp, int maximumHP, int safeTurns)
        => hp > 0 && maximumHP > 0 && hp == maximumHP && safeTurns >= SafeTurnThreshold;
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
    public static BarrierRecoveryState CreateState(int hp, int maximumHP) => new BarrierRecoveryState
        { RuleRevision = RecoveryRuleRevision, FullHPObserved = hp > 0 && hp == maximumHP };
    public static BarrierRecoveryState CopyState(BarrierRecoveryState value)
    {
        if (value == null || value.RuleRevision != RecoveryRuleRevision)
            return new BarrierRecoveryState { RuleRevision = RecoveryRuleRevision,
                LastProcessedRound = Mathf.Max(-1, value?.LastProcessedRound ?? -1) };
        return new BarrierRecoveryState { RuleRevision = RecoveryRuleRevision, FullHPObserved = value.FullHPObserved,
            SafeTurns = value.FullHPObserved ? Mathf.Clamp(value.SafeTurns, 0, SafeTurnThreshold) : 0,
            LastDamagedRound = Mathf.Max(-1, value.LastDamagedRound), HasRecovered = value.FullHPObserved && value.HasRecovered, HasOwnTurnBaseline = value.HasOwnTurnBaseline,
            DamageSinceOwnTurn = value.DamageSinceOwnTurn, LastProcessedRound = Mathf.Max(-1, value.LastProcessedRound) };
    }
    /// <summary>Call after assigning HP. Every healing/loss path shares the same full-HP clock.</summary>
    public static void RecordHpChange(Status actor, int previousHP, int nextHP)
    {
        if (!UsesBarrier(actor)) return;
        var state = actor.BarrierRecovery;
        if (state == null || state.RuleRevision != RecoveryRuleRevision)
            actor.BarrierRecovery = state = CopyState(state);
        if (nextHP <= 0 || nextHP != actor.MaxHP)
        {
            state.SafeTurns = 0; state.FullHPObserved = false; state.HasRecovered = false;
            state.DamageSinceOwnTurn = nextHP < previousHP;
            if (nextHP < previousHP) state.LastDamagedRound = actor.RuntimeSystems?.TurnGenerator?.Context.Turn ?? -1;
        }
        else if (previousHP != actor.MaxHP || !state.FullHPObserved)
        {
            state.SafeTurns = 0; state.FullHPObserved = true; state.HasRecovered = false;
            state.DamageSinceOwnTurn = false;
        }
    }
    /// <summary>Compatibility callback for absorbed hits/stripping. Barrier loss alone never resets R2 recovery.</summary>
    public static void RecordDamage(Status actor) { }
    public static void RecordNegatedHit(Status actor) { }
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
        var state = actor.BarrierRecovery;
        if (state == null || state.RuleRevision != RecoveryRuleRevision) actor.BarrierRecovery = state = CopyState(state);
        if (state.LastProcessedRound == round) return;
        state.LastProcessedRound = round;
        state.HasOwnTurnBaseline = true;
        int amount = AdvanceRecovery(ref state.SafeTurns, ref state.DamageSinceOwnTurn, ref state.HasRecovered, ref state.FullHPObserved,
            actor.HP, actor.MaxHP, actor.MaxBarrier);
        int oldBarrier = actor.Barrier;
        actor.Barrier = (int)Math.Min(actor.MaxBarrier, (long)oldBarrier + amount);
        if (actor.Barrier > oldBarrier) FloatingDamageUI.ShowBarrierRecovery(actor, actor.Barrier - oldBarrier);
    }
    /// <summary>Legacy source-compatible entry point; new callers also preserve the explicit full-HP baseline.</summary>
    public static int AdvanceRecovery(ref int safeTurns, ref bool damaged, ref bool hasRecovered, int hp, int maximumHP, int maximumBarrier)
    {
        bool fullHPObserved = !damaged;
        return AdvanceRecovery(ref safeTurns, ref damaged, ref hasRecovered, ref fullHPObserved, hp, maximumHP, maximumBarrier);
    }
    /// <summary>R2 pure live/search transition. Three owner boundaries after full HP, then 20% every boundary.</summary>
    public static int AdvanceRecovery(ref int safeTurns, ref bool damaged, ref bool hasRecovered, ref bool fullHPObserved,
        int hp, int maximumHP, int maximumBarrier)
    {
        damaged = false;
        if (hp <= 0 || maximumHP <= 0 || hp != maximumHP)
        { safeTurns = 0; fullHPObserved = false; hasRecovered = false; return 0; }
        if (!fullHPObserved)
        { safeTurns = 0; fullHPObserved = true; hasRecovered = false; return 0; }
        safeTurns = Mathf.Min(SafeTurnThreshold, Mathf.Clamp(safeTurns, 0, SafeTurnThreshold) + 1);
        if (!CanRegenerate(hp, maximumHP, safeTurns)) return 0;
        hasRecovered = true; return RegenerationAmount(maximumBarrier);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => actors.Clear();
}
