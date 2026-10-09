using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bounded routes to an already assigned frontier. Only supplied known terrain and observed danger
/// are inspected. Failure means a route was not proved; it never means unknown terrain is impassable.
/// Search scratch buffers are reused, and candidate queries never start another search.
/// </summary>
public sealed class ExplorationKnownRoute
{
    sealed class Route
    {
        public int UnitId, TerrainRevision, DangerRevision, ProfileSignature, PolicySignature, HP, Width, Depth;
        public int BuiltTurn, LastUsedTurn, MetricRevision;
        public Vector3Int Start, Target;
        public bool Proved;
        public IReadOnlyDictionary<Vector3Int, Vector3Int> Terrain;
        public IReadOnlyCollection<Vector3Int> Blocked;
        public Func<int, Vector3Int, float> Danger;
        public readonly List<Vector2Int> Offsets = new List<Vector2Int>(64);
        public readonly List<Vector3Int> Path = new List<Vector3Int>(128);
        public readonly List<float> Costs = new List<float>(128);
        public readonly Dictionary<Vector3Int, int> PathIndex = new Dictionary<Vector3Int, int>();
        public readonly Dictionary<Vector3Int, float> CandidateDistances = new Dictionary<Vector3Int, float>();
        public readonly HashSet<Vector3Int> FallbackBlocked = new HashSet<Vector3Int>();
        public readonly Dictionary<Vector3Int, float> DangerCache = new Dictionary<Vector3Int, float>(256);
    }
    struct Node
    {
        public Vector3Int Cell;
        public float Cost, Estimate;
        public int Parent, HeapIndex;
        public bool Closed;
    }
    readonly ExplorationAISettings settings;
    readonly Dictionary<int, Route> routes = new Dictionary<int, Route>();
    readonly Dictionary<Vector3Int, int> indices = new Dictionary<Vector3Int, int>(2048);
    readonly List<Node> nodes = new List<Node>(2048);
    readonly List<int> heap = new List<int>(2048);
    readonly List<Vector3Int> rebuiltPath = new List<Vector3Int>(128);
    readonly List<float> rebuiltCosts = new List<float>(128);
    int nextMetricRevision;
    public int NodesExpanded { get; private set; }
    public int TotalRouteBuilds { get; private set; }
    public int CachedRouteCount => routes.Count;

    public ExplorationKnownRoute(ExplorationAISettings settings = null)
        => this.settings = settings != null ? settings : ExplorationAISettings.Active;

    public bool TryMeasure(ExplorationActor actor, Vector3Int goal,
        IReadOnlyDictionary<Vector3Int, Vector3Int> knownTerrain, IReadOnlyCollection<Vector3Int> blocked,
        IReadOnlyList<Vector2Int> movementOffsets, bool directional, int dirZ, int terrainRevision,
        int turn, int dangerRevision, Func<int, Vector3Int, float> estimatedDamage,
        out float remaining, out Vector3Int waypoint, int width = int.MaxValue, int depth = int.MaxValue)
    {
        remaining = 0; waypoint = actor.Cell;
        if (!settings.Enabled || knownTerrain == null || movementOffsets == null || movementOffsets.Count == 0
            || actor.HP <= 0 || width <= 0 || depth <= 0) return false;
        Vector3Int start = XZ(actor.Cell), target = XZ(goal);
        TrimCache(actor.UnitId);
        int profile = OffsetSignature(movementOffsets, directional, dirZ);
        int policy = PolicySignature();
        if (!routes.TryGetValue(actor.UnitId, out var route))
        {
            EvictIfNeeded(); route = new Route { UnitId = actor.UnitId }; routes.Add(actor.UnitId, route);
        }
        Vector3Int previousTarget = route.Target;
        bool sameKey = route.Target == target && route.TerrainRevision == terrainRevision
            && route.DangerRevision == dangerRevision && route.ProfileSignature == profile && route.HP == actor.HP
            && route.PolicySignature == policy
            && route.Width == width && route.Depth == depth && ReferenceEquals(route.Terrain, knownTerrain)
            && ReferenceEquals(route.Blocked, blocked);
        int age = Math.Max(0, turn - route.BuiltTurn);
        bool validAge = turn >= route.BuiltTurn && age < Mathf.Clamp(settings.RouteCacheTurns, 1, 64);
        if (sameKey && validAge && (route.Proved ? route.PathIndex.ContainsKey(start) : route.Start == start))
        {
            route.LastUsedTurn = turn;
            return ReadCurrent(route, start, out remaining, out waypoint);
        }
        route.Start = start; route.Target = target; route.TerrainRevision = terrainRevision;
        route.DangerRevision = dangerRevision; route.ProfileSignature = profile; route.HP = actor.HP;
        route.PolicySignature = policy;
        route.Width = width; route.Depth = depth; route.BuiltTurn = route.LastUsedTurn = turn;
        route.Terrain = knownTerrain; route.Blocked = blocked; route.Danger = estimatedDamage;
        PrepareOffsets(route, movementOffsets, directional, dirZ);
        route.FallbackBlocked.Clear();
        if (blocked != null && !(blocked is ISet<Vector3Int>))
            foreach (var cell in blocked) route.FallbackBlocked.Add(XZ(cell));
        route.CandidateDistances.Clear();
        bool proved = Build(route);
        // A legal alternate start uses the same distance function when evidence/rules are unchanged.
        // If evidence changed, only an identical remaining suffix and cost can preserve the metric.
        bool sameMetric = proved && route.Proved && (sameKey || SameSuffix(route, start));
        // Unproved -> unproved uses the same fallback distance, even if new observations triggered another attempt.
        bool previousFailure = !proved && !route.Proved && previousTarget == target;
        if (!sameMetric && !previousFailure) route.MetricRevision = ++nextMetricRevision;
        route.Proved = proved;
        route.Path.Clear(); route.Costs.Clear(); route.PathIndex.Clear();
        if (proved)
            for (int i = 0; i < rebuiltPath.Count; i++)
            {
                route.PathIndex[rebuiltPath[i]] = i;
                route.Path.Add(rebuiltPath[i]); route.Costs.Add(rebuiltCosts[i]);
            }
        return ReadCurrent(route, start, out remaining, out waypoint);
    }

    public bool TryMeasureDestination(int unitId, Vector3Int destination, out float remaining)
    {
        remaining = 0;
        if (!routes.TryGetValue(unitId, out var route) || !route.Proved) return false;
        var cell = XZ(destination);
        if (route.PathIndex.TryGetValue(cell, out int index)) { remaining = route.Costs[index]; return true; }
        if (route.CandidateDistances.TryGetValue(cell, out float cached))
        { remaining = cached; return !float.IsPositiveInfinity(cached); }
        float best = float.PositiveInfinity;
        if (KnownPassable(route, cell) && Safe(route, cell))
            for (int i = 0; i < route.Offsets.Count; i++)
            {
                var offset = route.Offsets[i];
                var next = cell + new Vector3Int(offset.x, 0, offset.y);
                if (!route.PathIndex.TryGetValue(next, out int step) || !KnownLineClear(route, cell, next) || !Safe(route, next)) continue;
                best = Mathf.Min(best, EdgeCost(route, offset, next) + route.Costs[step]);
            }
        if (route.CandidateDistances.Count < 4096) route.CandidateDistances[cell] = best;
        remaining = best;
        return !float.IsPositiveInfinity(best);
    }
    public int GetMetricRevision(int unitId) => routes.TryGetValue(unitId, out var route) ? route.MetricRevision : 0;
    public void Clear()
    {
        routes.Clear(); indices.Clear(); nodes.Clear(); heap.Clear(); rebuiltPath.Clear(); rebuiltCosts.Clear();
        NodesExpanded = TotalRouteBuilds = nextMetricRevision = 0;
    }
    public void Remove(int unitId) => routes.Remove(unitId);

    bool ReadCurrent(Route route, Vector3Int start, out float remaining, out Vector3Int waypoint)
    {
        remaining = 0; waypoint = start;
        if (!route.Proved || !route.PathIndex.TryGetValue(start, out int index)) return false;
        remaining = route.Costs[index];
        var next = route.Path[Math.Min(index + 1, route.Path.Count - 1)];
        waypoint = route.Terrain.TryGetValue(next, out var known) ? known : next;
        return true;
    }
    bool Build(Route route)
    {
        TotalRouteBuilds++; NodesExpanded = 0;
        indices.Clear(); nodes.Clear(); heap.Clear(); rebuiltPath.Clear(); rebuiltCosts.Clear(); route.DangerCache.Clear();
        if (!KnownPassable(route, route.Start) || !KnownPassable(route, route.Target)
            || route.Offsets.Count == 0 || route.Start != route.Target && !Safe(route, route.Target)) return false;
        int limit = Mathf.Clamp(settings.MaxRouteNodes, 16, 4096);
        int lengthLimit = Mathf.Clamp(settings.MaxRouteLength, 2, 4096);
        AddNode(route.Start, 0, -1, route.Target);
        while (heap.Count > 0 && NodesExpanded < limit)
        {
            int index = Pop(); var node = nodes[index];
            node.Closed = true; nodes[index] = node; NodesExpanded++;
            if (node.Cell == route.Target) return Reconstruct(index, lengthLimit);
            for (int i = 0; i < route.Offsets.Count; i++)
            {
                var offset = route.Offsets[i]; var next = node.Cell + new Vector3Int(offset.x, 0, offset.y);
                if (!KnownLineClear(route, node.Cell, next) || !Safe(route, next)) continue;
                float cost = node.Cost + EdgeCost(route, offset, next);
                if (!indices.TryGetValue(next, out int nextIndex))
                {
                    if (nodes.Count >= limit) continue;
                    AddNode(next, cost, index, route.Target);
                }
                else
                {
                    var old = nodes[nextIndex];
                    if (old.Closed || cost >= old.Cost) continue;
                    old.Cost = cost; old.Estimate = cost + Manhattan(next, route.Target); old.Parent = index;
                    nodes[nextIndex] = old; SiftUp(old.HeapIndex); SiftDown(nodes[nextIndex].HeapIndex);
                }
            }
        }
        return false;
    }
    bool Reconstruct(int goal, int lengthLimit)
    {
        int cursor = goal;
        float totalCost = nodes[goal].Cost;
        while (cursor >= 0 && rebuiltPath.Count < lengthLimit)
        {
            var node = nodes[cursor]; rebuiltPath.Add(node.Cell); rebuiltCosts.Add(totalCost - node.Cost); cursor = node.Parent;
        }
        if (cursor >= 0) { rebuiltPath.Clear(); rebuiltCosts.Clear(); return false; }
        rebuiltPath.Reverse(); rebuiltCosts.Reverse();
        return true;
    }
    bool SameSuffix(Route route, Vector3Int start)
    {
        if (!route.PathIndex.TryGetValue(start, out int offset) || route.Path.Count - offset != rebuiltPath.Count) return false;
        for (int i = 0; i < rebuiltPath.Count; i++)
            if (route.Path[offset + i] != rebuiltPath[i] || Mathf.Abs(route.Costs[offset + i] - rebuiltCosts[i]) > .0001f) return false;
        return true;
    }
    bool KnownPassable(Route route, Vector3Int cell)
    {
        if (cell.x < 0 || cell.z < 0 || cell.x >= route.Width || cell.z >= route.Depth || !route.Terrain.ContainsKey(cell)) return false;
        return route.Blocked is ISet<Vector3Int> set ? !set.Contains(cell) : !route.FallbackBlocked.Contains(cell);
    }
    // Every touched intermediate/corner cell must be proved known and passable, matching supercover movement.
    bool KnownLineClear(Route route, Vector3Int from, Vector3Int to)
    {
        if (!KnownPassable(route, from) || !KnownPassable(route, to)) return false;
        int x = from.x, z = from.z, dx = Mathf.Abs(to.x - x), dz = Mathf.Abs(to.z - z);
        int sx = Math.Sign(to.x - x), sz = Math.Sign(to.z - z), ix = 0, iz = 0;
        while (ix < dx || iz < dz)
        {
            int decision = (1 + 2 * ix) * dz - (1 + 2 * iz) * dx;
            if (decision == 0)
            {
                if (!KnownPassable(route, new Vector3Int(x + sx, 0, z)) || !KnownPassable(route, new Vector3Int(x, 0, z + sz))) return false;
                x += sx; z += sz; ix++; iz++;
            }
            else if (decision < 0) { x += sx; ix++; }
            else { z += sz; iz++; }
            if (!KnownPassable(route, new Vector3Int(x, 0, z))) return false;
        }
        return true;
    }
    bool Safe(Route route, Vector3Int cell)
    {
        if (cell == route.Start) return true; // A threatened explorer may escape its current cell.
        float limit = Mathf.Clamp(Finite(settings.MaximumFrontierDangerFraction, .8f), .01f, 1);
        return Damage(route, cell) < route.HP * limit;
    }
    float Damage(Route route, Vector3Int cell)
    {
        if (route.DangerCache.TryGetValue(cell, out float value)) return value;
        var at = route.Terrain.TryGetValue(cell, out var height) ? height : cell;
        value = route.Danger != null ? Mathf.Max(0, Finite(route.Danger(route.UnitId, at), float.MaxValue)) : 0;
        if (route.DangerCache.Count < 8192) route.DangerCache[cell] = value;
        return value;
    }
    float EdgeCost(Route route, Vector2Int offset, Vector3Int destination)
        => Mathf.Abs(offset.x) + Mathf.Abs(offset.y)
            + Damage(route, destination) / Math.Max(1, route.HP) * Mathf.Clamp(Finite(settings.RouteDangerWeight, 2), 0, 100);

    void PrepareOffsets(Route route, IReadOnlyList<Vector2Int> source, bool directional, int dirZ)
    {
        route.Offsets.Clear(); int direction = directional && dirZ < 0 ? -1 : 1;
        for (int i = 0; i < source.Count && i < 256; i++)
        {
            var value = new Vector2Int(source[i].x, source[i].y * direction);
            if (value == Vector2Int.zero || Mathf.Abs(value.x) > 64 || Mathf.Abs(value.y) > 64 || route.Offsets.Contains(value)) continue;
            route.Offsets.Add(value);
        }
        route.Offsets.Sort((a, b) => { int order = a.x.CompareTo(b.x); return order != 0 ? order : a.y.CompareTo(b.y); });
    }
    static int OffsetSignature(IReadOnlyList<Vector2Int> offsets, bool directional, int dirZ)
    {
        unchecked
        {
            int value = directional ? dirZ < 0 ? -397 : 397 : 17;
            for (int i = 0; i < offsets.Count && i < 256; i++) value = value * 31 + offsets[i].GetHashCode();
            return value;
        }
    }
    int PolicySignature()
    {
        unchecked
        {
            int value = settings.MaxRouteNodes * 397 ^ settings.MaxRouteLength;
            value = value * 31 + settings.MaximumFrontierDangerFraction.GetHashCode();
            return value * 31 + settings.RouteDangerWeight.GetHashCode();
        }
    }
    void EvictIfNeeded()
    {
        if (routes.Count < Mathf.Clamp(settings.MaxCachedRoutes, 1, 256)) return;
        int remove = 0; int oldest = int.MaxValue; bool found = false;
        foreach (var pair in routes)
            if (!found || pair.Value.LastUsedTurn < oldest || pair.Value.LastUsedTurn == oldest && pair.Key < remove)
            { found = true; remove = pair.Key; oldest = pair.Value.LastUsedTurn; }
        if (found) routes.Remove(remove);
    }
    void TrimCache(int requestedUnit)
    {
        int limit = Mathf.Clamp(settings.MaxCachedRoutes, 1, 256);
        while (routes.Count > limit)
        {
            int remove = 0, oldest = int.MaxValue; bool found = false;
            foreach (var pair in routes)
            {
                if (pair.Key == requestedUnit) continue;
                if (!found || pair.Value.LastUsedTurn < oldest || pair.Value.LastUsedTurn == oldest && pair.Key < remove)
                { found = true; remove = pair.Key; oldest = pair.Value.LastUsedTurn; }
            }
            if (!found) break;
            routes.Remove(remove);
        }
    }
    void AddNode(Vector3Int cell, float cost, int parent, Vector3Int target)
    {
        int index = nodes.Count; indices[cell] = index;
        nodes.Add(new Node { Cell = cell, Cost = cost, Estimate = cost + Manhattan(cell, target), Parent = parent, HeapIndex = heap.Count });
        heap.Add(index); SiftUp(heap.Count - 1);
    }
    int Pop()
    {
        int result = heap[0]; int last = heap[heap.Count - 1]; heap.RemoveAt(heap.Count - 1);
        if (heap.Count > 0) { heap[0] = last; SetHeapIndex(last, 0); SiftDown(0); }
        SetHeapIndex(result, -1); return result;
    }
    void SiftUp(int index)
    {
        while (index > 0)
        {
            int parent = (index - 1) / 2; if (!Before(heap[index], heap[parent])) break;
            Swap(index, parent); index = parent;
        }
    }
    void SiftDown(int index)
    {
        while (true)
        {
            int child = index * 2 + 1; if (child >= heap.Count) return;
            if (child + 1 < heap.Count && Before(heap[child + 1], heap[child])) child++;
            if (!Before(heap[child], heap[index])) return;
            Swap(index, child); index = child;
        }
    }
    bool Before(int first, int second)
    {
        var a = nodes[first]; var b = nodes[second];
        if (a.Estimate != b.Estimate) return a.Estimate < b.Estimate;
        // On a flat rectangle many shortest-path nodes share f. Prefer deeper progress rather than flooding the rectangle.
        if (a.Cost != b.Cost) return a.Cost > b.Cost;
        return a.Cell.x != b.Cell.x ? a.Cell.x < b.Cell.x : a.Cell.z < b.Cell.z;
    }
    void Swap(int first, int second)
    {
        int value = heap[first]; heap[first] = heap[second]; heap[second] = value;
        SetHeapIndex(heap[first], first); SetHeapIndex(heap[second], second);
    }
    void SetHeapIndex(int nodeIndex, int index) { var node = nodes[nodeIndex]; node.HeapIndex = index; nodes[nodeIndex] = node; }
    static Vector3Int XZ(Vector3Int cell) => new Vector3Int(cell.x, 0, cell.z);
    static float Manhattan(Vector3Int a, Vector3Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.z - b.z);
    static float Finite(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
}
