using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Battle-local observations, never cross-match learning or inferred enemy-base coordinates.</summary>
[Serializable]
public sealed class EnemyTerritoryIntelState
{
    public int Version = 1;
    public Team ActorTeam, OpponentTeam;
    public int Width, Depth;
    public int FirstObservedTurn = -1, LastConfirmedTurn = -1, LastProgressTurn = -1;
    public int LastObservationTurn = -1;
    public bool HasWaypoint, Suspended;
    public Vector3Int Waypoint;
    public string Reason;
    public int PausedTurn = -1;
    public List<EnemyTerritoryIntelRegion> Regions = new List<EnemyTerritoryIntelRegion>();
    public List<EnemyTerritoryIntelVisit> Visits = new List<EnemyTerritoryIntelVisit>();
}

[Serializable]
public sealed class EnemyTerritoryIntelRegion
{
    public int OriginX, OriginZ, FirstObservedTurn, LastConfirmedTurn;
    // Exact 16x16 observed cells. No centroid is ever promoted to a known coordinate.
    public int[] Cells = new int[8];
}

[Serializable]
public sealed class EnemyTerritoryIntelVisit
{
    public Vector3Int Cell;
    public int UntilTurn;
}
