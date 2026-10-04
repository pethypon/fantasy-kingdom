using System.Collections.Generic;
using UnityEngine;

/// <summary>Reusable, bounded best-first routing. Every edge is validated by the shared movement rules.</summary>
public sealed class ThirdFactionPathPlanner
{
    public delegate bool StepProvider(Vector3 from, Vector2Int offset, out Vector3 to, out float cost);
    struct Node
    {
        public Vector3 Position, FirstStep;
        public float Cost, Score;
    }
    readonly List<Node> nodes = new List<Node>(256);
    readonly List<int> open = new List<int>(256);
    readonly Dictionary<Vector3Int, int> visited = new Dictionary<Vector3Int, int>(256);
    readonly System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();

    public bool TryNextStep(Kind kind, Vector3 origin, Vector3 goal, StepProvider step,
        int maximumExpansions, float budgetMs, out Vector3 destination)
    {
        return TryNextStep(kind, null, origin, goal, step, maximumExpansions, budgetMs, out destination);
    }

    public bool TryNextStep(Status actor, Vector3 origin, Vector3 goal, StepProvider step,
        int maximumExpansions, float budgetMs, out Vector3 destination)
        => TryNextStep(actor.kind, BoardActionProfile.For(actor), origin, goal, step, maximumExpansions, budgetMs, out destination);

    bool TryNextStep(Kind kind, BoardActionProfile profile, Vector3 origin, Vector3 goal, StepProvider step,
        int maximumExpansions, float budgetMs, out Vector3 destination)
    {
        destination = origin;
        nodes.Clear(); open.Clear(); visited.Clear(); watch.Restart();
        int limit = Mathf.Clamp(maximumExpansions, 8, 256);
        nodes.Add(new Node { Position = origin }); open.Add(0); visited.Add(GridHelper.ToGridXZ(origin), 0);
        float originalDistance = GridHelper.ChebyshevDistance(origin, goal), bestDistance = originalDistance;
        int best = -1;
        var offsets = MovePatterns.Offsets(kind, profile);
        int facings = MovePatterns.IsDirectionIndependent(kind, profile) ? 1 : 2;
        for (int expanded = 0; expanded < limit && open.Count > 0; expanded++)
        {
            if (watch.Elapsed.TotalMilliseconds >= budgetMs) break;
            int next = 0;
            for (int i = 1; i < open.Count; i++) if (nodes[open[i]].Score < nodes[open[next]].Score) next = i;
            int index = open[next]; open[next] = open[open.Count - 1]; open.RemoveAt(open.Count - 1);
            var node = nodes[index];
            float distance = GridHelper.ChebyshevDistance(node.Position, goal);
            if (distance < bestDistance) { bestDistance = distance; best = index; }
            if (best >= 0 && distance <= 1) break;
            for (int facing = 0; facing < facings; facing++) foreach (var offset in offsets)
            {
                if (watch.Elapsed.TotalMilliseconds >= budgetMs || nodes.Count >= limit * 4) break;
                var direction = new Vector2Int(offset.x, offset.y * (facing == 0 ? 1 : -1));
                if (!step(node.Position, direction, out var cell, out float edgeCost)
                    || float.IsNaN(edgeCost) || float.IsInfinity(edgeCost) || edgeCost <= 0) continue;
                var key = GridHelper.ToGridXZ(cell);
                float cost = node.Cost + edgeCost;
                if (visited.TryGetValue(key, out int old) && cost >= nodes[old].Cost) continue;
                var candidate = new Node { Position = cell, FirstStep = index == 0 ? cell : node.FirstStep,
                    Cost = cost, Score = cost + GridHelper.ChebyshevDistance(cell, goal) };
                if (visited.TryGetValue(key, out old))
                { nodes[old] = candidate; if (!open.Contains(old)) open.Add(old); }
                else { visited.Add(key, nodes.Count); open.Add(nodes.Count); nodes.Add(candidate); }
            }
        }
        watch.Stop();
        if (best < 0) return false;
        destination = nodes[best].FirstStep;
        return destination != origin;
    }
}
