using System.Collections.Generic;
using UnityEngine;

/// <summary>Called once after owner-turn damage. All HP setters share the loss reset.</summary>
public static class NaturalRecoverySystem
{
    public const int SafeTurnThreshold = 5;
    static readonly List<Status> turnActors = new List<Status>(64);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => turnActors.Clear();
    public static int HealAmount(int maximumHP) => Mathf.Max(0, maximumHP) / 10;
    // Main crystals use the legacy Unit component type in shipped prefabs.
    // They are objectives, not units eligible for the five-safe-turn recovery.
    static bool Eligible(Status actor) => actor != null && actor.IsAlive && actor.type == Type.Unit
        && actor.kind != Kind.Crystal && actor.kind != Kind.SubCrystal && actor.MaxHP > 0;

    public static NaturalRecoveryState CopyState(NaturalRecoveryState source)
        => source == null ? new NaturalRecoveryState() : new NaturalRecoveryState
        {
            ConsecutiveSafeTurns = Mathf.Clamp(source.ConsecutiveSafeTurns, 0, SafeTurnThreshold),
            LastProcessedRound = Mathf.Max(-1, source.LastProcessedRound),
            HpLostSinceLastOwnTurn = source.HpLostSinceLastOwnTurn,
            HasTurnBaseline = source.HasTurnBaseline
        };

    public static void ProcessOwnerTurn(GameSystems systems, Team team, int round)
    {
        CombatRegistry.Collect(turnActors);
        try
        {
            foreach (var actor in turnActors)
                if (actor != null && actor.team == team) ProcessActor(actor, round);
        }
        finally { turnActors.Clear(); }
    }

    public static void ProcessOwnerTurnEnd(GameSystems systems, Team team, int round)
    {
        CombatRegistry.Collect(turnActors);
        try
        {
            foreach (var actor in turnActors)
                if (actor != null && actor.team == team) ProcessActorEnd(actor, round);
        }
        finally { turnActors.Clear(); }
    }

    public static void ProcessActorEnd(Status actor, int round)
    {
        if (!Eligible(actor)) return;
        var state = actor.NaturalRecovery ?? (actor.NaturalRecovery = new NaturalRecoveryState());
        if (state.LastProcessedRound == round) return;
        state.LastProcessedRound = round; state.HasTurnBaseline = true;
        if (state.HpLostSinceLastOwnTurn)
        {
            state.HpLostSinceLastOwnTurn = false; state.ConsecutiveSafeTurns = 0;
            CombatRuleTelemetry.Record("natural_regen_reset", actor, actor); return;
        }
        bool starting = state.ConsecutiveSafeTurns == SafeTurnThreshold - 1;
        state.ConsecutiveSafeTurns = Mathf.Min(SafeTurnThreshold, state.ConsecutiveSafeTurns + 1);
        if (state.ConsecutiveSafeTurns < SafeTurnThreshold) return;
        if (starting) CombatRuleTelemetry.Record("natural_regen_start", actor, actor);
        int actual = actor.ApplyHeal(HealAmount(actor.MaxHP));
        CombatRuleTelemetry.Record("natural_regen_tick", actor, actor, candidateValue: actual);
    }

    public static void ProcessActor(Status actor, int round)
    {
        if (!Eligible(actor)) return;
        var state = actor.NaturalRecovery ?? (actor.NaturalRecovery = new NaturalRecoveryState());
        if (state.LastProcessedRound == round) return;
        state.LastProcessedRound = round;
        if (!state.HasTurnBaseline || state.HpLostSinceLastOwnTurn)
        {
            state.HasTurnBaseline = true;
            state.HpLostSinceLastOwnTurn = false;
            state.ConsecutiveSafeTurns = 0;
            return;
        }
        state.ConsecutiveSafeTurns = Mathf.Min(SafeTurnThreshold, state.ConsecutiveSafeTurns + 1);
        if (state.ConsecutiveSafeTurns >= SafeTurnThreshold) actor.ApplyHeal(HealAmount(actor.MaxHP));
    }
}
