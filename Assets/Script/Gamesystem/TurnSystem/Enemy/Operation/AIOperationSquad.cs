using System;
using System.Collections.Generic;
using UnityEngine;

public enum AIOperationSquadRole { Frontline, Ranged, Healer, Scout }
public enum AIOperationSquadPhase { Assembling, Advancing, Engaged, Reorganizing, Suspended }

/// <summary>Optional battle-local values. Unity object references and tentative action reservations are never saved.</summary>
[Serializable]
public sealed class AIOperationSquad
{
    public long SquadId, OperationId;
    public int FormedTurn, LastProgressTurn, LastAssemblyTurn, GoalX, GoalZ;
    public bool HasApproachWaypoint;
    public int ApproachX, ApproachZ;
    public AIOperationSquadPhase Phase;
    public string LastReason;
    public List<AIOperationSquadMember> Members = new List<AIOperationSquadMember>();
}

[Serializable]
public sealed class AIOperationSquadMember
{
    public string ActorLifeId;
    public AIOperationSquadRole Role;
    public bool Recovering, HasSlot;
    [NonSerialized] public bool MarchSlot;
    public int SlotX, SlotZ;
}

public readonly struct AIOperationRecruitmentNeed
{
    public readonly string Role;
    public readonly int MissingCount, NeededFromTurn;
    public readonly long OperationId;
    public AIOperationRecruitmentNeed(string role, int missing, int needed, long operation)
    { Role = role; MissingCount = missing; NeededFromTurn = needed; OperationId = operation; }
}

/// <summary>A short tentative sequence. Every real step is re-generated after execution, not replayed unchecked.</summary>
[Serializable]
public sealed class AIOperationCoordinationStep
{
    public string ActorLifeId, TargetLifeId;
    public AIActionType ActionType;
    public int APCost, ExpectedHpLoss;
}

