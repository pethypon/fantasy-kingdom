using System;
using System.Collections.Generic;
using UnityEngine;

public enum AIUnitSafetyLevel { HoldPositionOrFight, WithdrawalRecommended, Emergency }
public enum AIUnitRecoveryPhase { Ready, Withdrawing, Recovering }

public readonly struct AIUnitPreservationAssessment
{
    public readonly AIUnitSafetyLevel Safety;
    public readonly AIUnitRecoveryPhase Phase;
    public readonly float IncomingCurrent, RepeatedObservedLoss, IncomingAtRefuge;
    public readonly bool HasLegalSaferMove, HealingPossible;
    public readonly int SaferMoveCount, AlliesNear;
    public readonly Vector3 Refuge;
    public readonly string Reason;
    // This is an observed-packet scenario, not knowledge of future enemy AP or decisions.
    public const string ProjectionScope = "Current observed attacks/protections repeated for at most two exchanges; future enemy choices are uncertain.";
    internal AIUnitPreservationAssessment(AIUnitSafetyLevel safety, AIUnitRecoveryPhase phase,
        float incoming, float repeated, float refugeRisk, bool escape, bool healing, int options, int allies, Vector3 refuge, string reason)
    {
        Safety = safety; Phase = phase; IncomingCurrent = incoming; RepeatedObservedLoss = repeated;
        IncomingAtRefuge = refugeRisk; HasLegalSaferMove = escape; HealingPossible = healing;
        SaferMoveCount = options; AlliesNear = allies; Refuge = refuge; Reason = reason;
    }
}

[Serializable]
public sealed class AIUnitPreservationRecord
{
    public string ActorLifeId;
    public AIUnitRecoveryPhase Phase;
    public int LastTurn;
    public Vector3 Refuge;
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
        if (source == null || source.Version != 1 || source.Owner != owner
            || source.Records == null || source.Records.Count > MaximumRecords) return null;
        var copy = new AIUnitPreservationState { Owner = owner };
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in source.Records)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.ActorLifeId) || record.ActorLifeId.Length > 128
                || record.LastTurn < 0 || !Enum.IsDefined(typeof(AIUnitRecoveryPhase), record.Phase)
                || record.Phase == AIUnitRecoveryPhase.Ready || !FinitePoint(record.Refuge)
                || !identities.Add(record.ActorLifeId)) continue;
            copy.Records.Add(record.Copy());
        }
        return copy;
    }
    static bool FinitePoint(Vector3 point) => !float.IsNaN(point.sqrMagnitude) && !float.IsInfinity(point.sqrMagnitude)
        && point.x >= 0 && point.x < 32768 && point.z >= 0 && point.z < 32768 && point.y >= -1024 && point.y <= 1024;
}
