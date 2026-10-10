/// <summary>Stable serialized identifiers. A goal is enabled only when its game rule and observed evidence exist.</summary>
public enum AIOperationGoal
{
    None, DestroyEnemyCrystal, DestroySubCrystal, DefendOwnCrystal, DefendSubCrystal,
    EliminateEnemyForce, WeakenEnemyForce, CaptureTerritory, HoldTerritory, ObtainArtifact,
    ClearDungeon, BreakEnemyDefense, FlankEnemyForce, SurroundEnemyForce, ScoutRegion,
    LocateEnemyMainForce, EconomicRecovery, SecureResourceArea, DelayEnemy, ForceEnemyRetreat,
    CreateDiversion, ExploitWeakFront
}
public enum AIOperationStatus { Proposed, Preparing, Active, Suspended, Replanning, Aborted, Success, PartialSuccess, Failure }
public enum AIOperationStepType { Scout, Approach, Diversion, Attack, Defense, Flank, Surround, Support, Retreat, Regroup, Hold, DestroyObjective, AcquireArtifact, Exploit }
public enum AIOperationDecision { Continue, Replan, Suspend, Abort, Complete }
public enum AIOperationAbortReason
{
    None, StrategicPriorityChanged, ObjectiveDestroyedByOther, ObjectiveUnavailable,
    InsufficientForce, EconomicEmergency, CrystalEmergency, IntelligenceInvalidated, ExcessiveLoss, Timeout
}
public enum AIOperationFailureReason
{
    None, PrimaryGoalFailed, PoorPreparation, BadIntel, InsufficientForce, ExcessiveLoss,
    SlowExecution, FailedDiversion, FailedFlank, FailedSurround, EconomicOverstretch, ResourceWaste,
    ArmyUnderutilization, ObjectiveAbandoned, MissedExploitation, CrystalExposed, Overcommitment,
    RepeatedFailedPattern
}
public enum AIOperationSuccessReason
{
    None, PrimaryGoalCompleted, FastCompletion, LowLosses, GoodTrade, SuccessfulScout,
    SuccessfulDiversion, SuccessfulFlank, SuccessfulSurround, GoodArmyUtilization,
    GoodRiskControl, EconomicEfficiency, StrongPreparation, SuccessfulExploitation
}
public enum AIOperationRank { CriticalFailure, Failure, Neutral, PartialSuccess, Success, Excellent, Perfect }
