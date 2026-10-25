using System.Collections.Generic;
using UnityEngine;

public sealed partial class AIExplorationPlanner
{
    public const int MaximumExpeditionRecords = 256;
    readonly List<ExplorationExpeditionRecord> expeditionRecords = new List<ExplorationExpeditionRecord>(32);
    readonly Dictionary<(string actor, int region, Vector3Int cell), ExplorationExpeditionRecord> expeditionIndex
        = new Dictionary<(string, int, Vector3Int), ExplorationExpeditionRecord>();
    readonly Dictionary<int, int> incomingContactSignatures = new Dictionary<int, int>();
    public IReadOnlyList<ExplorationExpeditionRecord> Expeditions => expeditionRecords;

    static void AdvanceInformation(RegionCache region)
        => region.InformationRevision = region.InformationRevision < int.MaxValue ? region.InformationRevision + 1 : 1;
    int InformationRevision(int region) => regions.TryGetValue(region, out var cache) ? cache.InformationRevision : 0;
    void BeginLeg(ExploreObjective objective)
    {
        objective.HasLegBaseline = true; objective.LegRevealBaseline = objective.NewlyRevealedSinceStart;
        objective.LegInformationRevision = InformationRevision(objective.FrontierRegionId); objective.LegStartedTurn = turn;
    }
    int LegReveals(ExploreObjective objective) => Mathf.Max(0, objective.NewlyRevealedSinceStart - objective.LegRevealBaseline);
    bool HasLegInformation(ExploreObjective objective)
        => LegReveals(objective) >= Mathf.Max(1, settings.CompletionRevealCells)
            || objective.LegInformationRevision != InformationRevision(objective.FrontierRegionId);

    bool IsUnproductiveViewpoint(string actor, int region, Vector3Int viewpoint, bool intelRevisit)
    {
        if (intelRevisit || !expeditionIndex.TryGetValue((actor, region, XZ(viewpoint)), out var record)
            || !record.Unproductive || !regions.TryGetValue(region, out var cache)) return false;
        // More observed terrain, changed terrain/importance, or a materially changed observed
        // hostile contact supplies a legitimate reason to try again. Time alone is not information.
        return record.KnownCells == cache.Cells.Count && record.InformationRevision == cache.InformationRevision;
    }
    void RecordViewpoint(ExplorationActor actor, ExploreObjective objective, bool unproductive)
    {
        var point = XZ(actor.Cell); int region = RegionId(point);
        if (!regions.TryGetValue(region, out var cache)) return;
        var key = (actor.ActorLifeId, region, point);
        if (!expeditionIndex.TryGetValue(key, out var record))
        {
            if (expeditionRecords.Count >= MaximumExpeditionRecords)
            {
                int oldest = 0;
                for (int i = 1; i < expeditionRecords.Count; i++)
                    if (expeditionRecords[i].LastArrivalTurn < expeditionRecords[oldest].LastArrivalTurn) oldest = i;
                var old = expeditionRecords[oldest]; expeditionIndex.Remove((old.ActorLifeId, old.RegionId, XZ(old.Viewpoint)));
                expeditionRecords.RemoveAt(oldest);
            }
            record = new ExplorationExpeditionRecord { ActorLifeId = actor.ActorLifeId, RegionId = region, Viewpoint = point };
            expeditionRecords.Add(record); expeditionIndex[key] = record;
        }
        record.Visits = Mathf.Min(ushort.MaxValue, record.Visits + 1);
        record.TotalReveals = (int)System.Math.Min(int.MaxValue, (long)record.TotalReveals + LegReveals(objective));
        record.LastArrivalTurn = turn; record.KnownCells = cache.Cells.Count;
        record.InformationRevision = cache.InformationRevision; record.Unproductive = unproductive;
    }
    ExplorationFrontierRegion CandidateForActor(ExplorationFrontierRegion source, ExplorationActor actor)
    {
        if (!IsUnproductiveViewpoint(actor.ActorLifeId, source.RegionId, source.RepresentativeCell, source.IsIntelRevisit)) return source;
        if (!regions.TryGetValue(source.RegionId, out var cache)) return source;
        bool found = false; float best = float.NegativeInfinity; Vector3Int next = default;
        foreach (var cell in cache.FrontierCells)
        {
            if (cell == XZ(actor.Cell) || blocked.Contains(cell) || !terrain.TryGetValue(cell, out var target)
                || IsUnproductiveViewpoint(actor.ActorLifeId, source.RegionId, target, false)) continue;
            if (observation.CanReach != null && !observation.CanReach(actor.UnitId, target)) continue;
            float danger = observation.EstimateDanger != null ? Mathf.Max(0, observation.EstimateDanger(actor.UnitId, target)) : 0;
            if (danger / Mathf.Max(1, actor.HP) >= settings.MaximumFrontierDangerFraction) continue;
            int unknown = 0;
            foreach (var offset in neighbours) if (Inside(cell + offset) && !memory.ContainsKey(cell + offset)) unknown++;
            if (unknown == 0) continue;
            float score = unknown * settings.UnknownGainWeight - Distance(actor.Cell, target) * settings.TravelCostWeight;
            if (!found || score > best || score == best && CompareCell(cell, XZ(next)) < 0)
            { found = true; best = score; next = target; }
        }
        if (!found) return source;
        return new ExplorationFrontierRegion { RegionId = source.RegionId, RepresentativeCell = next,
            UnknownCellsBehind = source.UnknownCellsBehind, FrontierCellCount = source.FrontierCellCount,
            LastAssignedTurn = source.LastAssignedTurn, Importance = source.Importance, OldestSeenTurn = source.OldestSeenTurn,
            ImportantCell = source.ImportantCell, IsIntelRevisit = source.IsIntelRevisit, Reachable = source.Reachable };
    }
    void ObserveContacts(ExplorationObservation next)
    {
        incomingContactSignatures.Clear();
        foreach (var contact in next.ObservedContacts)
        {
            if (!Inside(XZ(contact.Cell))) continue;
            int id = RegionId(contact.Cell); incomingContactSignatures.TryGetValue(id, out int prior);
            incomingContactSignatures[id] = prior ^ contact.Signature;
        }
        // Losing Fog contact is not proof that the old position is empty. Only current
        // observed contacts change this signature; generic visibility refreshes do not.
        foreach (var pair in incomingContactSignatures)
        {
            var cache = GetRegion(pair.Key);
            if (cache.ObservedContactSignature == pair.Value) continue;
            cache.ObservedContactSignature = pair.Value; AdvanceInformation(cache);
        }
    }
    static int ObservedInformationStamp(ExplorationObservation next)
    {
        unchecked
        {
            int stamp = 17;
            foreach (var contact in next.ObservedContacts) stamp ^= contact.Signature * 397 + contact.Cell.GetHashCode();
            foreach (var important in next.ImportantCells) stamp ^= important.Key.GetHashCode() * 31 + important.Value.GetHashCode();
            return stamp;
        }
    }
}
