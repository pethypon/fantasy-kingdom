using System;
using System.Collections.Generic;
using UnityEngine;

public enum ExplorationKnowledgeState { Unknown, SeenBefore, VisibleNow }
public enum ObjectiveState { Active, Suspended, Completed, Failed }

[Serializable]
public struct ExplorationCellMemory
{
    public ExplorationKnowledgeState State;
    public int LastSeenTurn;
    public int LastVisitedTurn;
    public ushort VisitCount;
    public ushort RevealCount;
}

[Serializable]
public sealed class ExplorationFrontierRegion
{
    public int RegionId;
    public Vector3Int RepresentativeCell;
    public int UnknownCellsBehind;
    public int FrontierCellCount;
    public int LastAssignedTurn = -1;
    public float EstimatedDanger;
    public bool Reachable = true;
    public bool IsIntelRevisit;
    public float Importance;
    public int OldestSeenTurn;
    public Vector3Int ImportantCell;
}

[Serializable]
public sealed class ExploreObjective
{
    public int ObjectiveId;
    public int FrontierRegionId;
    public Vector3Int TargetCell;
    public int AssignedUnitId;
    public string ActorLifeId;
    public int CreatedTurn;
    public int LastProgressTurn;
    public int NewlyRevealedSinceStart;
    public ObjectiveState State;
    public float BestDistance;
    public float LastDistance;
    public int LastSampleTurn;
    public int LastUnknownCount;
    public int FailedMoves;
    public int SuspendedTurn;
    public string LastReason;
    public bool IsIntelRevisit;
    // Version 2 saves measured Chebyshev progress; bind a Manhattan baseline once after loading.
    public bool RebaseProgressOnNextObservation;
    public bool UsesKnownRouteDistance;
    public int RouteMetricRevision;
    public Vector3Int RouteWaypoint;
}

/// <summary>A legal observation, also usable by deterministic tests without scene objects.</summary>
public sealed class ExplorationObservation
{
    public int Turn;
    public int Width;
    public int Depth;
    public Team ActorTeam = Team.Enemy;
    public int ViewVersion;
    public int CellVersion;
    public int DangerVersion;
    public Vector3Int OwnBase;
    public readonly List<ExplorationObservedCell> KnownCells = new List<ExplorationObservedCell>();
    public readonly List<Vector3Int> VisibleCells = new List<Vector3Int>();
    public readonly List<ExplorationActor> Actors = new List<ExplorationActor>();
    public readonly Dictionary<Vector3Int, float> ImportantCells = new Dictionary<Vector3Int, float>();
    // Delegates must describe only observed/cached evidence, never hidden occupants or terrain.
    public Func<int, Vector3Int, float> EstimateDanger;
    public Func<int, Vector3Int, bool> CanReach;
}

[Serializable]
public struct ExplorationObservedCell
{
    public Vector3Int Cell;
    public bool Passable;
    public int SeenTurn;
    public ExplorationObservedCell(Vector3Int cell, bool passable = true, int seenTurn = -1)
    { Cell = cell; Passable = passable; SeenTurn = seenTurn; }
}

[Serializable]
public struct ExplorationActor
{
    public int UnitId;
    public string ActorLifeId;
    public Vector3Int Cell;
    public Kind Kind;
    public int HP;
    public bool CanAct;
    public IReadOnlyList<Vector2Int> MovementOffsets;
    public bool MovementDirectionIndependent;
    public Direction Direction;
    public ExplorationActor(int unitId, Vector3Int cell, Kind kind = Kind.Scout, int hp = 100, bool canAct = true, string actorLifeId = null)
    {
        UnitId = unitId; Cell = cell; Kind = kind; HP = hp; CanAct = canAct; ActorLifeId = actorLifeId ?? unitId.ToString();
        MovementOffsets = null; MovementDirectionIndependent = MovePatterns.IsDirectionIndependent(kind, null); Direction = global::Direction.N;
    }
}

[Serializable]
public sealed class ExplorationMemoryRecord
{
    public Vector3Int Cell;
    public ExplorationCellMemory Memory;
    public bool Passable;
    public float Importance;
}

[Serializable]
public struct ExplorationHistorySample
{
    public Vector3Int Cell;
    public int Turn;
    public int NewlyRevealed;
    public float Distance;
}

[Serializable]
public sealed class ExplorationHistoryRecord
{
    public string ActorLifeId;
    public List<ExplorationHistorySample> Samples = new List<ExplorationHistorySample>();
}

[Serializable]
public struct ExplorationRegionCooldown
{
    public int RegionId;
    public int UntilTurn;
}

[Serializable]
public struct ExplorationRegionState
{
    public int RegionId;
    public int LastAssignedTurn;
}

[Serializable]
public sealed class AIExplorationState
{
    public int Version = 3;
    public int Width;
    public int Depth;
    public int Turn;
    public Team ActorTeam;
    public int NextObjectiveId = 1;
    public List<ExplorationMemoryRecord> Cells = new List<ExplorationMemoryRecord>();
    public List<ExploreObjective> Objectives = new List<ExploreObjective>();
    public List<ExplorationHistoryRecord> Histories = new List<ExplorationHistoryRecord>();
    public List<ExplorationRegionCooldown> LoopCooldowns = new List<ExplorationRegionCooldown>();
    public List<ExplorationRegionState> Regions = new List<ExplorationRegionState>();
}
