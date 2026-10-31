using System;
using System.Collections.Generic;
using UnityEngine;

public enum AIUnitSafetyLevel { HoldPositionOrFight, WithdrawalRecommended, Emergency }
public enum AIUnitRecoveryPhase { Ready, Withdrawing, Recovering }

/// <summary>Conditional barrier forecast at unchanged HP; no unperformed healer action is credited.</summary>
public readonly struct AIUnitRecoveryForecast
{
    public readonly int Barrier, FirstRecoveryBoundary, PossibleUnpaidHpLoss;
    public readonly bool UpkeepRisk;
    internal AIUnitRecoveryForecast(int barrier, int boundary, int unpaid, bool upkeep)
    { Barrier = barrier; FirstRecoveryBoundary = boundary; PossibleUnpaidHpLoss = unpaid; UpkeepRisk = upkeep; }

    public static AIUnitRecoveryForecast Project(Status actor, int ownBoundaries, bool upkeepRisk)
    {
        if (actor == null) return default;
        var clock = BarrierSystem.CopyState(actor.BarrierRecovery);
        int barrier = actor.Barrier, first = -1, hp = actor.HP;
        bool usesBarrier = BarrierSystem.UsesBarrier(actor);
        int unpaid = upkeepRisk ? R2EconomyRules.UnpaidDamage(hp, actor.MaxHP) : 0;
        for (int i = 1; i <= Mathf.Clamp(ownBoundaries, 0, 12); i++)
        {
            // A national failure cannot identify this actor's exact payment order. This is
            // the conservative unpaid scenario, labelled as a risk rather than a certainty.
            if (upkeepRisk) hp = Mathf.Max(0, hp - R2EconomyRules.UnpaidDamage(hp, actor.MaxHP));
            if (!usesBarrier) continue;
            int amount = BarrierSystem.AdvanceRecovery(ref clock.SafeTurns, ref clock.DamageSinceOwnTurn,
                ref clock.HasRecovered, ref clock.FullHPObserved, hp, actor.MaxHP, actor.MaxBarrier);
            int after = (int)Math.Min(actor.MaxBarrier, (long)barrier + amount);
            if (after > barrier && first < 0) first = i;
            barrier = after;
        }
        return new AIUnitRecoveryForecast(barrier, first, unpaid, upkeepRisk);
    }
}

public readonly struct AIUnitPreservationAssessment
{
    public readonly AIUnitSafetyLevel Safety;
    public readonly AIUnitRecoveryPhase Phase;
    public readonly float IncomingCurrent, RepeatedObservedLoss, IncomingAtRefuge;
    public readonly bool HasLegalSaferMove, HealingPossible;
    public readonly int SaferMoveCount, AlliesNear;
    public readonly Vector3 Refuge;
    // Refuge remains the executable first hop for existing candidate generation.
    public readonly Vector3 RecoveryDestination;
    public readonly int RouteSteps, RouteOwnTurns, RouteNodes, ForecastBarrier, BarrierRecoveryBoundary;
    public readonly float RouteObservedHpLoss;
    public readonly bool UpkeepRisk;
    public readonly string Reason;
    // This is an observed-packet scenario, not knowledge of future enemy AP or decisions.
    public const string ProjectionScope = "Current observed attacks/protections repeated for at most two exchanges; future enemy choices are uncertain.";
    internal AIUnitPreservationAssessment(AIUnitSafetyLevel safety, AIUnitRecoveryPhase phase,
        float incoming, float repeated, float refugeRisk, bool escape, bool healing, int options, int allies, Vector3 refuge, string reason,
        AIRetreatRoutePlan route = null, AIUnitRecoveryForecast recovery = default)
    {
        Safety = safety; Phase = phase; IncomingCurrent = incoming; RepeatedObservedLoss = repeated;
        IncomingAtRefuge = refugeRisk; HasLegalSaferMove = escape; HealingPossible = healing;
        SaferMoveCount = options; AlliesNear = allies; Refuge = refuge; Reason = reason;
        RecoveryDestination = route != null && route.Found ? route.Destination : refuge;
        RouteSteps = route?.Steps.Count ?? 0; RouteOwnTurns = route?.OwnTurns ?? 0; RouteNodes = route?.ExpandedNodes ?? 0;
        RouteObservedHpLoss = route?.ObservedHpLoss ?? 0;
        ForecastBarrier = recovery.Barrier; BarrierRecoveryBoundary = recovery.FirstRecoveryBoundary; UpkeepRisk = recovery.UpkeepRisk;
    }
}

[Serializable]
public sealed class AIUnitPreservationRecord
{
    public string ActorLifeId;
    public AIUnitRecoveryPhase Phase;
    public int LastTurn;
    public Vector3 Refuge;
    // Additive legacy-safe fields. Routes/reservations themselves are transient and revalidated after load.
    public bool HasRecoveryBaseline;
    public int LastProgressTurn, LastHP, LastBarrier;
    public AIUnitPreservationRecord Copy() => (AIUnitPreservationRecord)MemberwiseClone();
}

[Serializable]
public sealed class AIUnitPreservationState
{
    public const int MaximumRecords = 128;
    public int Version = 1;
    public Team Owner;
    public List<AIUnitPreservationRecord> Records = new List<AIUnitPreservationRecord>();

    /// <summary>One bounded validator/copy for active governors and pending developer saves.</summary>
    public static AIUnitPreservationState CopyForOwner(AIUnitPreservationState source, Team owner)
    {
        if (source == null || source.Version != 1 || source.Owner != owner || !Enum.IsDefined(typeof(Team), owner)
            || source.Records == null || source.Records.Count > MaximumRecords) return null;
        var copy = new AIUnitPreservationState { Owner = owner };
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in source.Records)
        {
            if (!IsValidRecord(record)
                || !identities.Add(record.ActorLifeId)) continue;
            copy.Records.Add(record.Copy());
        }
        return copy;
    }
    internal static bool IsValidRecord(AIUnitPreservationRecord record)
        => record != null && !string.IsNullOrWhiteSpace(record.ActorLifeId) && record.ActorLifeId.Length <= 128
            && record.LastTurn >= 0 && Enum.IsDefined(typeof(AIUnitRecoveryPhase), record.Phase)
            && record.Phase != AIUnitRecoveryPhase.Ready && IsValidPoint(record.Refuge)
            && (!record.HasRecoveryBaseline || record.LastProgressTurn >= 0 && record.LastProgressTurn <= record.LastTurn
                && record.LastHP >= 0 && record.LastBarrier >= 0);

    internal static bool IsValidPoint(Vector3 point) => !float.IsNaN(point.sqrMagnitude) && !float.IsInfinity(point.sqrMagnitude)
        && point.x >= 0 && point.x < 32768 && point.z >= 0 && point.z < 32768 && point.y >= -1024 && point.y <= 1024;
}
