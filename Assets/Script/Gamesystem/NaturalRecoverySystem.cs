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

    public static void ProcessActor(Status actor, int round)
    {
        if (actor == null || !actor.IsAlive || actor.type != Type.Unit || actor.MaxHP <= 0) return;
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
