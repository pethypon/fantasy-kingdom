using System;
using System.Collections.Generic;

/// <summary>A bounded multi-turn plan. Identity, target cells and action evidence remain within this battle.</summary>
[Serializable]
public sealed class AIOperationPlan
{
    public long OperationId;
    public long GoalStartedAfterActionId;
    public string BattleId;
    public AIOperationGoal PrimaryGoal, OriginalGoal;
    public string TargetCategory, TargetLifeId, StrategicReason, ContextKey, PatternKey;
    public bool HasTargetPosition;
    public int TargetX, TargetZ;
    public int StartTurn, ExpectedEndTurn, ActualEndTurn, MaximumEndTurn, LastProgressTurn;
    public int SuspendedOwnTurn = -1;
    public AIOperationStatus Status;
    public AIOperationAbortReason AbortReason;
    public bool StrategicAbort, IsPrimary;
    public int StrategicPriority, CurrentStep;
    public float ExpectedApBudget = 40f;
    public int TargetNewTiles = 32, TargetTerritoryGain = 4;
    public float TargetPowerReduction = .5f;
    public List<AIOperationStep> Steps = new List<AIOperationStep>();
    public List<long> ActionIds = new List<long>();
    public List<long> CreditedActionIds = new List<long>();
    public List<AIActionRecord> ActionEvidence = new List<AIActionRecord>();
    public List<string> AssignedUnitRoles = new List<string>();
    public List<string> AssignedUnitLifeIds = new List<string>();
    public List<string> OriginalAssignedUnitLifeIds = new List<string>();
    public List<string> UtilizedUnitLifeIds = new List<string>();
    public AIOperationContext StartContext, OriginalStartContext, CurrentContext, EndContext;
    public AIOperationScoreBreakdown Score, ProvisionalScore;
    public AIOperationRank Rank;
    public bool PrimaryGoalAchieved;
    public float LearningReward, LearnedValueBefore, LearnedValueAfter;
    public List<AIOperationSuccessReason> SuccessReasons = new List<AIOperationSuccessReason>();
    public List<AIOperationFailureReason> FailureReasons = new List<AIOperationFailureReason>();
    public List<AIOperationRevision> Revisions = new List<AIOperationRevision>();
}
