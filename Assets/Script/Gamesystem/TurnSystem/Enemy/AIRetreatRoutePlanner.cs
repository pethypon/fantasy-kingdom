using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>A bounded observed-state proposal. Only FirstHop may become a current legal action.</summary>
public sealed class AIRetreatRoutePlan
{
    public bool Found { get; internal set; }
    public Vector3 FirstHop { get; internal set; }
    public Vector3 Destination { get; internal set; }
    public readonly List<Vector3> Steps = new List<Vector3>(6);
    public int OwnTurns { get; internal set; }
    public int ExpandedNodes { get; internal set; }
    public int FirstHopOptions { get; internal set; }
    public int CurrentTurnAP { get; internal set; }
    public int NextTurnAP { get; internal set; }
    public float ObservedHpLoss { get; internal set; }
    public float DestinationRisk { get; internal set; }
    public const string Scope = "Known formal steps; current AP and one projected owner-turn AP reset. Observed enemies may change; execution revalidates.";
}

// The governor owns both this proposal and reservations. This is not another operation commander.
internal abstract class AIRetreatRouteContext
{
    internal Vector3 Origin, RetainedRefuge;
    internal int HP, CurrentAP, NextAP, Fatigue, ExtraFatigue;
    internal bool HasMoved;
    internal float CurrentRisk, RefugeRiskLimit;
    internal AIObservedDamageBudget InitialProtection;
    internal abstract void CollectSteps(Vector3 from, List<Vector3> result);
    internal abstract int MoveCost(Vector3 from, Vector3 to, int fatigue, bool hasMoved, bool freeAvailable);
    internal abstract bool ReservedFirstHop(Vector3 destination);
    internal abstract float EntryLoss(Vector3 destination, ref AIObservedDamageBudget protection);
    internal abstract float Exposure(Vector3 destination, ref AIObservedDamageBudget protection);
    internal abstract bool CanRecoverAt(Vector3 position, float observedHpRisk);
    internal virtual bool Stop => AITurnBudget.Expired;
}

internal static class AIRetreatRoutePlanner
{
    struct Node
    {
        internal Vector3 Position;
        internal int Parent, Depth, Turn, AP, Fatigue, CurrentSpent, NextSpent;
        internal bool HasMoved;
        internal AIObservedDamageBudget Protection;
        internal float Loss;
    }

    internal static AIRetreatRoutePlan Find(AIRetreatRouteContext context, int maximumDepth, int maximumNodes, int futureOwnTurns)
    {
        var result = new AIRetreatRoutePlan();
        int depth = Mathf.Clamp(maximumDepth, 1, 6), limit = Mathf.Clamp(maximumNodes, 1, 256);
        int future = Mathf.Clamp(futureOwnTurns, 0, 1);
        if (context == null || context.HP <= 0 || context.CurrentAP < 0) return result;
        var nodes = new List<Node>(limit) { new Node { Position = context.Origin, Parent = -1,
            AP = context.CurrentAP, Fatigue = context.Fatigue, HasMoved = context.HasMoved, Protection = context.InitialProtection } };
        var steps = new List<Vector3>(64);
        var firstHops = new HashSet<Vector3Int>();
        int best = -1; float bestLoss = float.PositiveInfinity, bestRisk = float.PositiveInfinity;
        for (int index = 0; index < nodes.Count && !context.Stop; index++)
        {
            var node = nodes[index]; result.ExpandedNodes++;
            if (node.Depth > 0)
            {
                var endProtection = node.Protection;
                float risk = context.Exposure(node.Position, ref endProtection);
                float loss = node.Loss + risk;
                if (loss < context.HP && risk <= context.RefugeRiskLimit + .001f
                    && (risk + .001f < context.CurrentRisk || risk <= .001f)
                    && context.CanRecoverAt(node.Position, risk))
                {
                    firstHops.Add(GridHelper.ToGridXZ(FirstHop(nodes, index)));
                    bool retained = GridHelper.ToGridXZ(node.Position) == GridHelper.ToGridXZ(context.RetainedRefuge);
                    bool oldRetained = best >= 0 && GridHelper.ToGridXZ(nodes[best].Position) == GridHelper.ToGridXZ(context.RetainedRefuge);
                    if (best < 0 || loss < bestLoss - .001f || Mathf.Abs(loss - bestLoss) <= .001f
                        && (risk < bestRisk - .001f || Mathf.Abs(risk - bestRisk) <= .001f
                            && (node.Turn < nodes[best].Turn || node.Turn == nodes[best].Turn
                                && (retained && !oldRetained || retained == oldRetained
                                    && node.CurrentSpent + node.NextSpent < nodes[best].CurrentSpent + nodes[best].NextSpent))))
                    { best = index; bestLoss = loss; bestRisk = risk; }
                }
            }
            if (node.Depth >= depth || nodes.Count >= limit) continue;
            // One real owner boundary may reset fatigue/AP. Exposure is paid once at the
            // waiting position with the same finite barrier/one-use-protection budget.
            for (int boundary = 0; boundary <= (node.Depth > 0 && node.Turn < future ? 1 : 0); boundary++)
            {
                var source = node;
                if (boundary != 0)
                {
                    source.Loss += context.Exposure(source.Position, ref source.Protection);
                    if (source.Loss >= context.HP) continue;
                    source.Turn++; source.AP = context.NextAP; source.Fatigue = 0; source.HasMoved = false;
                }
                steps.Clear(); context.CollectSteps(source.Position, steps);
                foreach (var destination in steps)
                {
                    if (nodes.Count >= limit || context.Stop) break;
                    if (node.Depth == 0 && context.ReservedFirstHop(destination) || InPath(nodes, index, destination)) continue;
                    int cost = context.MoveCost(source.Position, destination, source.Fatigue, source.HasMoved, node.Depth == 0);
                    if (cost < 0 || cost > source.AP) continue;
                    var child = source; child.Position = destination; child.Parent = index; child.Depth++;
                    child.AP -= cost;
                    child.Fatigue = (int)Math.Min(int.MaxValue, (long)child.Fatigue + 1 + Math.Max(0, context.ExtraFatigue));
                    child.HasMoved = true;
                    if (child.Turn == 0) child.CurrentSpent += cost; else child.NextSpent += cost;
                    child.Loss += context.EntryLoss(destination, ref child.Protection);
                    // The existing action owner commits one action at a time. A first hop
                    // must survive on its own if another order spends the remaining AP.
                    if (child.Depth == 1)
                    {
                        var firstHopProtection = child.Protection;
                        if (child.Loss + context.Exposure(destination, ref firstHopProtection) >= context.HP) continue;
                    }
                    if (child.Loss < context.HP) nodes.Add(child);
                }
            }
        }
        result.FirstHopOptions = firstHops.Count;
        if (best < 0) return result;
        var chosen = nodes[best]; result.Found = true; result.FirstHop = FirstHop(nodes, best); result.Destination = chosen.Position;
        result.OwnTurns = chosen.Turn + 1; result.CurrentTurnAP = chosen.CurrentSpent; result.NextTurnAP = chosen.NextSpent;
        result.ObservedHpLoss = bestLoss; result.DestinationRisk = bestRisk;
        for (int cursor = best; nodes[cursor].Parent >= 0; cursor = nodes[cursor].Parent) result.Steps.Add(nodes[cursor].Position);
        result.Steps.Reverse(); return result;
    }
    static Vector3 FirstHop(List<Node> nodes, int index)
    { while (nodes[index].Parent > 0) index = nodes[index].Parent; return nodes[index].Position; }
    static bool InPath(List<Node> nodes, int index, Vector3 destination)
    {
        var cell = GridHelper.ToGridXZ(destination);
        for (int cursor = index; cursor >= 0; cursor = nodes[cursor].Parent)
            if (GridHelper.ToGridXZ(nodes[cursor].Position) == cell) return true;
        return false;
    }
}

public partial class AIBoardState
{
    // Local masks and indexed terrain queries only; no per-node scan of SetPos or hidden actors.
    internal void CollectKnownRetreatSteps(Status actor, Vector3 from, HashSet<Vector3Int> occupied, List<Vector3> result)
    {
        var map = _moveGen?.mapcreate;
        if (map == null || !IsTerrainKnown(from)) return;
        var offsets = MovePatterns.Offsets(actor);
        int direction = MovePatterns.IsDirectionIndependent(actor) ? 1 : MovePatterns.DirZ(actor.direction);
        for (int i = 0; i < offsets.Count; i++)
        {
            int x = Mathf.RoundToInt(from.x) + offsets[i].x, z = Mathf.RoundToInt(from.z) + offsets[i].y * direction;
            var cell = new Vector3Int(x, 0, z);
            if (!knownCells.Contains(cell) || occupied.Contains(cell) || ownedWallCells.Contains(cell)) continue;
            // Conservatively require the local jump rectangle to be known before formal
            // terrain traversal can consult it. Unknown environment is not a safe refuge.
            bool known = true;
            for (int px = Math.Min(Mathf.RoundToInt(from.x), x); px <= Math.Max(Mathf.RoundToInt(from.x), x) && known; px++)
                for (int pz = Math.Min(Mathf.RoundToInt(from.z), z); pz <= Math.Max(Mathf.RoundToInt(from.z), z); pz++)
                    if (!knownCells.Contains(new Vector3Int(px, 0, pz))) { known = false; break; }
            if (!known || !map.TryGetHeight(x, z, out float height)) continue;
            var destination = new Vector3(x, height, z);
            // Cave travel has transit/arrival scheduling; it is not an instantaneous safe
            // multi-step chain. Ordinary generator/executor may still choose that escape.
            if (map.Environment.IsCaveConnection(from, destination) || !map.IsLegalMove(actor, from, destination)) continue;
            result.Add(destination);
        }
    }
    internal int RetreatMoveQuote(Status actor, Vector3 from, Vector3 to, int fatigue, bool moved, bool freeAvailable)
        => _apSystem == null ? -1 : _apSystem.CalcMoveCostPreview(actor, from, to, fatigue, moved, freeAvailable);
    internal void CollectRetreatOccupancy(Status actor, HashSet<Vector3Int> result)
    {
        result.Clear(); result.Add(GridHelper.ToGridXZ(EnemyCrystalPos));
        if (CanUsePlayerCrystalAsTarget()) result.Add(GridHelper.ToGridXZ(PlayerCrystalPos));
        foreach (var own in AliveEnemyUnits) if (own != null && own != actor && own.IsAlive) result.Add(own.GridPosition);
        foreach (var seen in AlivePlayerUnits) if (seen != null && seen.IsAlive) result.Add(seen.GridPosition);
    }
}
