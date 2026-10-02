using System;
using UnityEngine;

public enum AIPlanGoal { Reconnaissance, EconomicRaid, EasternFeint, Ambush, KingStrike, CrystalStrike }
public enum AIPlanStep { Assemble, Demonstrate, ObserveResponse, Flank, Strike, SupplyDelay, MainAssault, Withdraw, Complete, Aborted }

/// <summary>A serializable operation, containing observed target data and public waypoints only.</summary>
[Serializable]
public sealed class AIPlan
{
    public int Version = 1;
    public AIPlanGoal Goal;
    public Vector3 Target, DemonstrationPoint, FlankPoint, RallyPoint;
    public Kind[] RequiredUnits;
    public AIPlanStep[] Steps;
    public string SuccessCondition, AbortCondition;
    public int ExpectedTurns = 7, StartedTurn, StepStartedTurn, LastObservedTurn;
    public AIPlanStep Step = AIPlanStep.Assemble;
    public int TargetId;
    public Type TargetType;
    public Kind TargetKind;
    public FacilityKind TargetFacility;
    public bool TargetDestroyedConfirmed;
    public int InitialArmy, VisibleResponders, SuccessfulStrikes;
    public bool Active => Step != AIPlanStep.Complete && Step != AIPlanStep.Aborted;
}
