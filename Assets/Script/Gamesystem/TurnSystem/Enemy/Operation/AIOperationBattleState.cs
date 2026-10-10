using System;
using System.Collections.Generic;

/// <summary>Optional save-game payload. Absent in legacy saves; distinct from generalized cross-battle knowledge.</summary>
[Serializable]
public sealed class AIOperationBattleState
{
    public int Version = 1;
    public Team Faction;
    public string BattleId;
    public long NextOperationId;
    public int OwnTurns, LastStartedOwnTurn = -1, LastEndedOwnTurn = -1, LastStartedGlobalTurn = -1;
    public bool Ended;
    public List<AIOperationPlan> ActiveOperations = new List<AIOperationPlan>();
    public List<AIOperationPlan> CompletedOperations = new List<AIOperationPlan>();
    public List<long> CompletedOperationIds = new List<long>();
    public List<string> ConfirmedDestroyedLifeIds = new List<string>();
    public List<string> ObservedEnemyLifeIds = new List<string>();
    public List<string> ArtifactEventIds = new List<string>();
    public AIOperationLearningProfile LearningProfile;
}
