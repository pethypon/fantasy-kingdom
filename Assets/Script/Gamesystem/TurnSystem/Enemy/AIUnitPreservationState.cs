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
    public int Version = 1;
    public Team Owner;
    public List<AIUnitPreservationRecord> Records = new List<AIUnitPreservationRecord>();
}
