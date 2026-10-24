using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Faction-local territory observations and a persistent, legally observed approach goal.</summary>
public sealed partial class EnemyTerritoryIntel
{
    public const int RegionSize = 16, MaximumRegions = 4096, MaximumVisits = 128;
    const int CandidateLimit = 8;
    readonly Dictionary<Vector2Int, EnemyTerritoryIntelRegion> regions = new Dictionary<Vector2Int, EnemyTerritoryIntelRegion>();
    readonly Dictionary<Vector3Int, Vector3Int> terrain = new Dictionary<Vector3Int, Vector3Int>();
    readonly HashSet<Vector3Int> terrainBlocked = new HashSet<Vector3Int>();
    readonly HashSet<Vector3Int> previousVision = new HashSet<Vector3Int>();
    readonly HashSet<Vector3Int> occupied = new HashSet<Vector3Int>();
    readonly Dictionary<Vector3Int, int> visits = new Dictionary<Vector3Int, int>();
    readonly HashSet<Vector3Int> completedApproaches = new HashSet<Vector3Int>();
    readonly List<Vector3Int> removalScratch = new List<Vector3Int>();
    readonly List<Vector3Int> candidates = new List<Vector3Int>(CandidateLimit);
    readonly List<Status> leadActors = new List<Status>(4);
    readonly List<Status> mobileActors = new List<Status>(16);
    Team actorTeam;
    int width, depth, observedCellCount, terrainRevision, observationTurn = -1, preparedGeneration = -1;
    int failedMoves, pausedTurn = -1;
    int leadActorCursor;
    bool importedExplored, hasSelectionCursor;
    Vector3Int selectionCursor;
    MapCreate sourceMap;
    AIBoardState preparedBoard;
    AIBoardState observationBoard;
    int observationGeneration = -1;

    public bool HasObservedTerritory => FirstObservedTurn >= 0;
    public bool HasUsableTerritory => observedCellCount > 0;
    public int ObservedCellCount => observedCellCount;
    public int RegionCount => regions.Count;
    public int CompletedApproachCount => completedApproaches.Count;
    public bool HasWaypoint { get; private set; }
    public Vector3Int Waypoint { get; private set; }
    public bool Suspended { get; private set; }
    public string Reason { get; private set; } = "territory_unobserved";
    public int FirstObservedTurn { get; private set; } = -1;
    public int LastConfirmedTurn { get; private set; } = -1;
    public int LastProgressTurn { get; private set; } = -1;
    public int DiscoveryEvents => HasObservedTerritory ? 1 : 0;
    public int LastObservationQueries { get; private set; }
    public long TotalObservationQueries { get; private set; }
    public int RouteBuilds => routes?.TotalRouteBuilds ?? 0;
    public int RouteNodes => routes?.NodesExpanded ?? 0;

    static Vector3Int XZ(Vector3Int cell) => new Vector3Int(cell.x, 0, cell.z);
    bool Inside(Vector3Int cell) => cell.x >= 0 && cell.z >= 0 && cell.x < width && cell.z < depth;
    static Vector2Int RegionKey(Vector3Int cell) => new Vector2Int(cell.x / RegionSize, cell.z / RegionSize);
    static int BitIndex(Vector3Int cell) => (cell.z % RegionSize) * RegionSize + cell.x % RegionSize;
    bool Contains(Vector3Int cell)
        => regions.TryGetValue(RegionKey(cell), out var region) && (region.Cells[BitIndex(cell) >> 5] & (1 << (BitIndex(cell) & 31))) != 0;

    public void Observe(AIBoardState board)
    {
        if (board == null) return;
        var map = board.MapCreate != null ? board.MapCreate : board.ReconMap;
        if (map == null || map.maxX <= 0 || map.maxZ <= 0) return;
        if (width != 0 && (actorTeam != board.ActorTeam || width != map.maxX || depth != map.maxZ)) Reset(board.ActorTeam);
        actorTeam = board.ActorTeam; width = map.maxX; depth = map.maxZ;
        if (sourceMap != map)
        {
            sourceMap = map; terrain.Clear(); terrainBlocked.Clear(); previousVision.Clear(); importedExplored = false;
            terrainRevision++; preparedBoard = null; ClearRoutes();
        }
        if (!importedExplored)
        {
            // Geography was already explored; this imports no historical territory affiliation.
            foreach (var cell in board.OwnExploredCells) ImportTerrain(board, XZ(cell));
            importedExplored = true;
        }
        bool changed = previousVision.Count != board.OwnVisionCells.Count;
        if (!changed) foreach (var cell in board.OwnVisionCells) if (!previousVision.Contains(XZ(cell))) { changed = true; break; }
        if (!changed && observationTurn == board.TurnCount && observationBoard == board && observationGeneration == board.Generation) return;
        observationBoard = board; observationGeneration = board.Generation;
        observationTurn = board.TurnCount; previousVision.Clear(); LastObservationQueries = 0;
        var territory = board.TerritorySystem != null ? board.TerritorySystem : map.GetComponent<TerritorySystem>();
        foreach (var supplied in board.OwnVisionCells)
        {
            var cell = XZ(supplied);
            if (!Inside(cell) || !previousVision.Add(cell)) continue;
            ImportTerrain(board, cell);
            if (territory == null) continue;
            // The only opponent-affiliation query: one currently visible cell at a time.
            bool opponent = territory.IsInTerritory(cell, board.OpponentTeam);
            LastObservationQueries++; TotalObservationQueries++;
            RecordCell(cell, opponent, board.TurnCount);
        }
    }

    void RecordCell(Vector3Int cell, bool opponent, int turn)
    {
        var key = RegionKey(cell);
        if (!regions.TryGetValue(key, out var region))
        {
            if (!opponent || regions.Count >= MaximumRegions) return; // Never FIFO-evict an existing known direction.
            region = new EnemyTerritoryIntelRegion { OriginX = key.x * RegionSize, OriginZ = key.y * RegionSize,
                FirstObservedTurn = turn, LastConfirmedTurn = turn };
            regions.Add(key, region);
        }
        int bit = BitIndex(cell), word = bit >> 5, mask = 1 << (bit & 31);
        bool had = (region.Cells[word] & mask) != 0;
        if (opponent)
        {
            if (!had) { region.Cells[word] |= mask; observedCellCount++; }
            if (FirstObservedTurn < 0) FirstObservedTurn = turn;
            region.LastConfirmedTurn = LastConfirmedTurn = turn;
        }
        else if (had)
        {
            region.Cells[word] &= ~mask; observedCellCount--;
            if (HasWaypoint && cell == XZ(Waypoint)) { HasWaypoint = false; Reason = "territory_reobserved_removed"; }
            // Only fresh ownership evidence can make a formerly completed approach
            // cease to belong to this remembered territory direction.
            for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
            {
                var approach = cell + new Vector3Int(dx, 0, dz);
                if (Inside(approach) && completedApproaches.Contains(approach) && !IsObservedApproach(approach))
                { completedApproaches.Remove(approach); visits.Remove(approach); }
            }
        }
    }

    void ImportTerrain(AIBoardState board, Vector3Int cell)
    {
        if (!Inside(cell) || !board.IsTerrainKnown(cell) || sourceMap == null) return;
        bool hasHeight = sourceMap.TryGetHeight(cell.x, cell.z, out float y);
        var position = new Vector3Int(cell.x, hasHeight ? Mathf.RoundToInt(y) : 0, cell.z);
        bool blocked = !hasHeight || sourceMap.IsRiver(cell.x, cell.z) || sourceMap.IsHighMountain(cell.x, cell.z)
            || board.KnownEnvironmentDefinition(cell)?.blocksMovement == true;
        bool different = !terrain.TryGetValue(cell, out var previous) || previous != position || terrainBlocked.Contains(cell) != blocked;
        terrain[cell] = position;
        if (blocked) terrainBlocked.Add(cell); else terrainBlocked.Remove(cell);
        if (different) terrainRevision++;
    }

    public void Update(AIBoardState board, bool suspended, string reason)
    {
        Observe(board);
        if (board == null || sourceMap == null) return;
        PrepareBoard(board);
        if (suspended && !Suspended) pausedTurn = board.TurnCount;
        if (!suspended && Suspended && pausedTurn >= 0 && LastProgressTurn >= 0)
            LastProgressTurn += Mathf.Max(0, board.TurnCount - pausedTurn);
        Suspended = suspended;
        if (Suspended) { Reason = string.IsNullOrEmpty(reason) ? "territory_suspended" : reason; return; }
        pausedTurn = -1;
        if (!HasObservedTerritory) { HasWaypoint = false; Reason = "territory_unobserved"; return; }
        if (!HasUsableTerritory) { HasWaypoint = false; Reason = "territory_reobserved_removed"; return; }
        bool valid = HasWaypoint && !completedApproaches.Contains(XZ(Waypoint)) && IsObservedApproach(XZ(Waypoint)) && Passable(XZ(Waypoint));
        if (valid && occupied.Contains(XZ(Waypoint))) valid = false;
        int stall = Mathf.Clamp(ExplorationAISettings.Active.StallTurns, 1, 64);
        if (valid && LastProgressTurn >= 0 && board.TurnCount - LastProgressTurn >= stall)
        { Cooldown(XZ(Waypoint), board.TurnCount); valid = false; }
        if (!valid)
        {
            HasWaypoint = false;
            SelectWaypoint(board);
        }
        else Reason = "territory_advance_ready";
    }

    void PrepareBoard(AIBoardState board)
    {
        if (preparedBoard == board && preparedGeneration == board.Generation) return;
        preparedBoard = board; preparedGeneration = board.Generation;
        occupied.Clear(); leadActors.Clear(); mobileActors.Clear();
        foreach (var actor in board.SpecialObservedActors)
            if (actor != null && actor.IsAlive && board.IsSpecialActorObserved(actor)) occupied.Add(actor.GridPosition);
        foreach (var actor in board.AliveEnemyUnits)
            if (Eligible(actor, board) && PreferredLead(actor)) mobileActors.Add(actor);
        foreach (var actor in board.AliveEnemyUnits)
            if (Eligible(actor, board) && !PreferredLead(actor)) mobileActors.Add(actor);
        if (mobileActors.Count > 0)
        {
            leadActorCursor %= mobileActors.Count;
            for (int i = 0; i < Mathf.Min(4, mobileActors.Count); i++)
                leadActors.Add(mobileActors[(leadActorCursor + i) % mobileActors.Count]);
        }
        removalScratch.Clear();
        foreach (var pair in visits) if (pair.Value <= board.TurnCount && !completedApproaches.Contains(pair.Key)) removalScratch.Add(pair.Key);
        foreach (var cell in removalScratch) visits.Remove(cell);
        PruneRouteActors(board);
    }

    static bool Eligible(Status actor, AIBoardState board)
        => actor != null && actor.IsAlive && actor.gameObject.activeInHierarchy && actor.team == board.ActorTeam
            && actor.type == Type.Unit && !MatchObjectiveRules.IsCommander(actor)
            && !StatusEffectSystem.IsStunned(actor) && !StatusEffectSystem.IsMovementBlocked(actor)
            && !SpecialSkillRuntime.MovementBlocked(actor) && BoardActionProfile.For(actor)?.CanMove != false;
    static bool PreferredLead(Status actor)
        => actor.kind != Kind.Scout && !UnitRoster.IsDedicatedHealer(actor.kind)
            && BoardActionProfile.For(actor)?.CanAttack != false && actor.HP >= actor.MaxHP * .6f;
    bool Passable(Vector3Int cell) => terrain.ContainsKey(cell) && !terrainBlocked.Contains(cell);
    bool IsObservedApproach(Vector3Int cell)
    {
        if (Contains(cell)) return true;
        for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
            if ((dx != 0 || dz != 0) && Inside(cell + new Vector3Int(dx, 0, dz)) && Contains(cell + new Vector3Int(dx, 0, dz))) return true;
        return false;
    }

    void SelectWaypoint(AIBoardState board)
    {
        if (leadActors.Count == 0) { Reason = "territory_no_mobile_actor"; return; }
        candidates.Clear();
        foreach (var region in regions.Values)
            for (int word = 0; word < region.Cells.Length; word++)
            {
                uint bits = (uint)region.Cells[word];
                if (bits == 0) continue;
                for (int b = 0; b < 32; b++) if ((bits & (1u << b)) != 0)
                {
                    int bit = word * 32 + b;
                    var cell = new Vector3Int(region.OriginX + bit % RegionSize, 0, region.OriginZ + bit / RegionSize);
                    AddCandidate(cell, board);
                    // A blocked/occupied border still permits a known adjacent staging tile.
                    for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
                        if (dx != 0 || dz != 0) AddCandidate(cell + new Vector3Int(dx, 0, dz), board);
                }
            }
        foreach (var candidate in candidates)
        {
            foreach (var actor in leadActors)
            {
                if (!TryRoute(actor, candidate, board, out var context) || context.Remaining <= .01f) continue;
                Waypoint = terrain[candidate]; HasWaypoint = true; failedMoves = 0;
                hasSelectionCursor = false;
                LastProgressTurn = board.TurnCount; Reason = "territory_advance_ready"; return;
            }
            // A small failed batch must not pin every later turn to those same nearest cells.
            // Cooling route attempts retains all territory evidence and exposes the next known approaches.
            Cooldown(candidate, board.TurnCount);
            selectionCursor = candidate; hasSelectionCursor = true;
        }
        // Keep work bounded without permanently excluding a later movement profile.
        if (mobileActors.Count > 0) leadActorCursor = (leadActorCursor + leadActors.Count) % mobileActors.Count;
        preparedGeneration = -1;
        Reason = "territory_route_recon_required";
    }

    void AddCandidate(Vector3Int cell, AIBoardState board)
    {
        if (!Inside(cell) || !Passable(cell) || occupied.Contains(cell) || visits.ContainsKey(cell) || candidates.Contains(cell)) return;
        int index = 0;
        while (index < candidates.Count && CompareCandidate(candidates[index], cell, board) < 0) index++;
        if (index >= CandidateLimit) return;
        candidates.Insert(index, cell);
        if (candidates.Count > CandidateLimit) candidates.RemoveAt(candidates.Count - 1);
    }
    int CompareCandidate(Vector3Int a, Vector3Int b, AIBoardState board)
    {
        if (hasSelectionCursor)
        {
            // A cyclic cursor advances independently of cooldown expiry. Dense disconnected
            // knowledge cannot repeatedly refill the nearest batch and starve a later region.
            bool afterA = CompareCell(a, selectionCursor) > 0, afterB = CompareCell(b, selectionCursor) > 0;
            if (afterA != afterB) return afterA ? -1 : 1;
            return CompareCell(a, b);
        }
        int distance = CandidateDistance(a, board).CompareTo(CandidateDistance(b, board));
        return distance != 0 ? distance : CompareCell(a, b);
    }
    float CandidateDistance(Vector3Int cell, AIBoardState board)
    {
        float best = float.PositiveInfinity;
        foreach (var actor in leadActors) best = Mathf.Min(best, Distance(actor.GridPosition, cell));
        return float.IsPositiveInfinity(best) ? Distance(GridHelper.ToGridXZ(board.EnemyCrystalPos), cell) : best;
    }
    static int CompareCell(Vector3Int a, Vector3Int b) => a.x != b.x ? a.x.CompareTo(b.x) : a.z.CompareTo(b.z);
    static float Distance(Vector3Int a, Vector3Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.z - b.z);
    void Cooldown(Vector3Int cell, int turn, bool completed = false)
    {
        if (visits.Count >= MaximumVisits && !visits.ContainsKey(cell))
        {
            Vector3Int first = default; int earliest = int.MaxValue; bool foundTemporary = false;
            foreach (var pair in visits)
            {
                bool temporary = !completedApproaches.Contains(pair.Key);
                if (foundTemporary && !temporary) continue;
                if (temporary && !foundTemporary || pair.Value < earliest)
                { first = pair.Key; earliest = pair.Value; foundTemporary = temporary; }
            }
            // Failed probes must not evict every completed approach and restart a patrol.
            if (!foundTemporary && !completed) return;
            visits.Remove(first); completedApproaches.Remove(first);
        }
        visits[cell] = turn + Mathf.Clamp(ExplorationAISettings.Active.LoopCooldownTurns, 1, 64);
        if (completed) completedApproaches.Add(cell);
    }

    public void RecordAction(AIAction action, bool succeeded, AIBoardState board)
    {
        if (board == null || action?.Unit == null || !HasWaypoint || Suspended || !Eligible(action.Unit, board)
            || action.ActionType != AIActionType.Move && action.ActionType != AIActionType.Support
                && action.ActionType != AIActionType.Surround && action.ActionType != AIActionType.Retreat
                && action.ActionType != AIActionType.DefenseRepos) return;
        if (!routeContexts.TryGetValue(action.Unit.GetInstanceID(), out var context) || context.Target != XZ(Waypoint)) return;
        if (!succeeded)
        {
            if (++failedMoves >= Mathf.Clamp(ExplorationAISettings.Active.FailedMoveLimit, 1, 16))
            { Cooldown(XZ(Waypoint), board.TurnCount); HasWaypoint = false; Reason = "territory_repeated_move_failure"; }
            return;
        }
        if (routes.TryMeasureDestination(action.Unit.GetInstanceID(), GridHelper.ToGridXZ(action.TargetPos), out float after)
            && after + .01f < context.Remaining)
        {
            LastProgressTurn = board.TurnCount; failedMoves = 0;
            if (GridHelper.ToGridXZ(action.TargetPos) == XZ(Waypoint) && action.Unit.GridPosition == XZ(Waypoint))
            { Cooldown(XZ(Waypoint), board.TurnCount, completed: true); HasWaypoint = false; Reason = "territory_waypoint_reached"; }
        }
    }
}
