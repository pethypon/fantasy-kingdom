using UnityEngine;

public sealed partial class EnemyTerritoryIntel
{
    AIBoardState reconBoard;
    MapCreate reconMap;
    int reconGeneration = -1, reconCellCount = -1, reconConfirmedTurn = -1;
    bool reconTargetAvailable;
    Vector3Int reconTarget;

    void ClearReconTarget()
    {
        reconBoard = null; reconMap = null; reconGeneration = reconCellCount = reconConfirmedTurn = -1;
        reconTargetAvailable = false; reconTarget = default;
    }

    /// <summary>A reconnaissance direction from actual territory evidence, never a proved route or attack target.</summary>
    public bool TryGetReconTarget(AIBoardState board, out Vector3Int target)
    {
        target = default;
        if (board == null || board.ActorTeam != actorTeam || sourceMap == null
            || !ExplorationAISettings.Active.FocusObservedEnemyTerritory
            || !HasUsableTerritory && !board.GetLastKnownPlayerCrystal().Valid) return false;
        if (reconBoard == board && reconMap == sourceMap && reconGeneration == board.Generation
            && reconCellCount == observedCellCount && reconConfirmedTurn == LastConfirmedTurn)
        { target = reconTarget; return reconTargetAvailable; }
        reconBoard = board; reconMap = sourceMap; reconGeneration = board.Generation;
        reconCellCount = observedCellCount; reconConfirmedTurn = LastConfirmedTurn;
        reconTargetAvailable = false;
        var remembered = board.GetLastKnownPlayerCrystal();
        if (remembered.Valid && remembered.Turn <= board.TurnCount)
        {
            // A confirmed old coordinate is an investigation direction, never a live attack target.
            reconTarget = remembered.Position; reconTargetAvailable = true;
            target = reconTarget; return true;
        }
        EnemyTerritoryIntelRegion lead = null;
        foreach (var region in regions.Values)
            if (lead == null || region.LastConfirmedTurn > lead.LastConfirmedTurn
                || region.LastConfirmedTurn == lead.LastConfirmedTurn && region.FirstObservedTurn < lead.FirstObservedTurn) lead = region;
        if (lead == null) return false;
        long sumX = 0, sumZ = 0; int count = 0;
        for (int word = 0; word < lead.Cells.Length; word++)
            for (int bit = 0; bit < 32; bit++)
                if (((uint)lead.Cells[word] & (1u << bit)) != 0)
                { int index = word * 32 + bit; sumX += lead.OriginX + index % RegionSize; sumZ += lead.OriginZ + index / RegionSize; count++; }
        if (count == 0) return false;
        var anchor = new Vector3Int((int)(sumX / count), 0, (int)(sumZ / count));
        float best = float.PositiveInfinity;
        var policy = ExplorationAISettings.Active;
        int work = 0, limit = Mathf.Clamp(policy.MainBaseReconWorkLimit, 64, 8192);
        // One bounded pass over already observed geography per board generation. Unknown
        // neighbors are only absence in our memory, never queries of unseen terrain/ownership.
        foreach (var pair in terrain)
        {
            if (++work > limit) break;
            var cell = pair.Key;
            if (!Passable(cell) || occupied.Contains(cell)) continue;
            int unknown = 0;
            for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
            {
                var adjacent = cell + new Vector3Int(dx, 0, dz);
                if ((dx != 0 || dz != 0) && Inside(adjacent) && !board.IsTerrainKnown(adjacent)) unknown++;
            }
            if (unknown == 0) continue;
            float score = Distance(cell, anchor) * Mathf.Clamp(policy.MainBaseFocusDistanceWeight,0,20)
                + CandidateDistance(cell, board) * Mathf.Clamp(policy.MainBaseFocusTravelWeight,0,20)
                - unknown * Mathf.Clamp(policy.UnknownGainWeight, 0, 20);
            if (float.IsNaN(score) || score > best || score == best && reconTargetAvailable && CompareCell(cell, XZ(reconTarget)) >= 0) continue;
            best = score; reconTarget = pair.Value; reconTargetAvailable = true;
        }
        // No fresh frontier near the lead: retain the observed lead as a direction so
        // the existing stale-intelligence/route planner can decide the next legal visit.
        if (!reconTargetAvailable)
            for (int word = 0; word < lead.Cells.Length && !reconTargetAvailable; word++)
                for (int bit = 0; bit < 32; bit++)
                    if (((uint)lead.Cells[word] & (1u << bit)) != 0)
                    {
                        int index = word * 32 + bit;
                        var cell = new Vector3Int(lead.OriginX + index % RegionSize, 0, lead.OriginZ + index / RegionSize);
                        if (Passable(cell)) { reconTarget = terrain[cell]; reconTargetAvailable = true; break; }
                    }
        target = reconTarget;
        return reconTargetAvailable;
    }
}
