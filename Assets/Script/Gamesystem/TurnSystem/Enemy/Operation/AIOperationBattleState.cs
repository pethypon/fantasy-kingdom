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
    public List<AIKnownEnemyBase> KnownEnemyBases = new List<AIKnownEnemyBase>();
    // Fixed-size, battle-local discovery filter: eviction must never grant a repeated first-discovery reward.
    // Hash collisions can suppress a reward, never create information about an undiscovered entity.
    public int[] BaseDiscoveryBits = new int[64];
    public List<AIOperationPlan> ActiveOperations = new List<AIOperationPlan>();
    public long NextSquadId;
    public List<AIOperationSquad> Squads = new List<AIOperationSquad>();
    public List<AIOperationPlan> CompletedOperations = new List<AIOperationPlan>();
    public List<long> CompletedOperationIds = new List<long>();
    public List<string> ConfirmedDestroyedLifeIds = new List<string>();
    public List<string> ObservedEnemyLifeIds = new List<string>();
    public List<string> ArtifactEventIds = new List<string>();
    public AIOperationLearningProfile LearningProfile;
    // Opaque optional versioned payload: a future R4 version survives an older client's save untouched.
    public string CognitiveJson;
}
