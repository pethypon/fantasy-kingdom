using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class EnemyTerritoryIntel
{
    AIBoardState reconBoard;
    MapCreate reconMap;
    int reconGeneration = -1, reconCellCount = -1, reconConfirmedTurn = -1;
    bool reconTargetAvailable;
    Vector3Int reconTarget;
    const int MaximumReconVisits = 128;
    readonly Dictionary<Vector3Int, int> reconVisits = new Dictionary<Vector3Int, int>();
    bool hasReconExpedition;
    Vector3Int reconExpedition;
    int reconSelectedTurn = -1;
    readonly int[] knownSearchSectors = new int[16];
    bool reconExpeditionHypothesis;
    public bool HasSearchHypothesis { get; private set; }
    public Vector3Int SearchHypothesisCenter { get; private set; }
    public float SearchHypothesisConfidence { get; private set; }
    public string ReconSource { get; private set; } = "none";

    void ClearReconTarget()
    {
        reconBoard = null; reconMap = null; reconGeneration = reconCellCount = reconConfirmedTurn = -1;
        reconTargetAvailable = false; reconTarget = default;
        hasReconExpedition = false; reconExpedition = default; reconSelectedTurn = -1; reconVisits.Clear();
        Array.Clear(knownSearchSectors, 0, knownSearchSectors.Length); reconExpeditionHypothesis = false;
        HasSearchHypothesis = false; SearchHypothesisCenter = default; SearchHypothesisConfidence = 0; ReconSource = "none";
    }

    /// <summary>A reconnaissance direction from actual territory evidence, never a proved route or attack target.</summary>
    public bool TryGetReconTarget(AIBoardState board, out Vector3Int target)
    {
        target = default;
        if (board == null) return false;
        Observe(board); PrepareBoard(board);
        if (board.ActorTeam != actorTeam || sourceMap == null
            || !ExplorationAISettings.Active.FocusObservedEnemyTerritory) return false;
        if (reconBoard == board && reconMap == sourceMap && reconGeneration == board.Generation
            && reconCellCount == observedCellCount && reconConfirmedTurn == LastConfirmedTurn)
        { target = reconTarget; return reconTargetAvailable; }
        reconBoard = board; reconMap = sourceMap; reconGeneration = board.Generation;
        reconCellCount = observedCellCount; reconConfirmedTurn = LastConfirmedTurn;
        reconTargetAvailable = false;
        HasSearchHypothesis = false; SearchHypothesisConfidence = 0;
        var remembered = board.GetLastKnownPlayerCrystal();
        if (remembered.Valid && remembered.Turn <= board.TurnCount)
        {
            // A confirmed old coordinate is an investigation direction, never a live attack target.
            reconTarget = remembered.Position; reconTargetAvailable = true;
            ReconSource = "observed_main_base_memory";
            target = reconTarget; return true;
        }
        bool hypothesis = !HasUsableTerritory;
        Vector3Int hypothesisCenter = default;
        if (hypothesis && !TryCoverageHypothesis(board, out hypothesisCenter)) { ReconSource = "none"; return false; }
        ReconSource = hypothesis ? "coverage_hypothesis" : "observed_enemy_territory";
        if (hasReconExpedition && reconExpeditionHypothesis != hypothesis) hasReconExpedition = false;
        if (hasReconExpedition)
        {
            bool visited = ReconUnknownNeighbours(reconExpedition) == 0;
            foreach (var actor in board.AliveEnemyUnits)
                if (actor != null && actor.IsAlive && actor.type == Type.Unit && !MatchObjectiveRules.IsCommander(actor)
                    && (actor.Special == null || actor.Special.visionZeroTurns <= 0) && Distance(XZ(actor.GridPosition), reconExpedition) <= 1
                    && board.IsVisibleToEnemy(reconExpedition)) { visited = true; break; }
            if (visited || board.TurnCount - reconSelectedTurn >= Mathf.Max(4, ExplorationAISettings.Active.StallTurns * 3))
            {
                if (reconVisits.Count >= MaximumReconVisits && !reconVisits.ContainsKey(reconExpedition))
                {
                    Vector3Int oldest = default; int until = int.MaxValue;
                    foreach (var pair in reconVisits) if (pair.Value < until) { until = pair.Value; oldest = pair.Key; }
                    reconVisits.Remove(oldest);
                }
                reconVisits[reconExpedition] = board.TurnCount + Mathf.Max(1, ExplorationAISettings.Active.LoopCooldownTurns);
                hasReconExpedition = false;
            }
            else if (Passable(reconExpedition) && !occupied.Contains(reconExpedition))
            { reconTarget = terrain[reconExpedition]; reconTargetAvailable = true; target = reconTarget; return true; }
            else hasReconExpedition = false;
        }
        EnemyTerritoryIntelRegion lead = null;
        foreach (var region in regions.Values)
            if (lead == null || region.LastConfirmedTurn > lead.LastConfirmedTurn
                || region.LastConfirmedTurn == lead.LastConfirmedTurn && region.FirstObservedTurn < lead.FirstObservedTurn) lead = region;
        long sumX = 0, sumZ = 0; int count = 0;
        if (lead != null) for (int word = 0; word < lead.Cells.Length; word++)
            for (int bit = 0; bit < 32; bit++)
                if (((uint)lead.Cells[word] & (1u << bit)) != 0)
                { int index = word * 32 + bit; sumX += lead.OriginX + index % RegionSize; sumZ += lead.OriginZ + index / RegionSize; count++; }
        if (count == 0 && !hypothesis) return false;
        var anchor = hypothesis ? hypothesisCenter : new Vector3Int((int)(sumX / count), 0, (int)(sumZ / count));
        Vector3 inward = ((Vector3)(anchor - GridHelper.ToGridXZ(board.EnemyCrystalPos))).normalized;
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
            if (reconVisits.TryGetValue(cell, out int retry) && retry > board.TurnCount) continue;
            int unknown = ReconUnknownNeighbours(cell);
            if (unknown == 0) continue;
            float score = Distance(cell, anchor) * Mathf.Clamp(policy.MainBaseFocusDistanceWeight,0,20)
                + CandidateDistance(cell, board) * Mathf.Clamp(policy.MainBaseFocusTravelWeight,0,20)
                - unknown * Mathf.Clamp(policy.UnknownGainWeight, 0, 20)
                - Vector3.Dot((Vector3)(cell - anchor), inward) * Mathf.Clamp(policy.MainBaseFocusDistanceWeight,0,20) * .75f
                + RouteRiskAt(cell) * 2;
            if (float.IsNaN(score) || score > best || score == best && reconTargetAvailable && CompareCell(cell, XZ(reconTarget)) >= 0) continue;
            best = score; reconTarget = pair.Value; reconTargetAvailable = true;
        }
        // No unknown frontier is not a reason to revisit the same already surveyed border.
        if (reconTargetAvailable)
        { hasReconExpedition = true; reconExpedition = XZ(reconTarget); reconSelectedTurn = board.TurnCount; reconExpeditionHypothesis = hypothesis; }
        target = reconTarget;
        return reconTargetAvailable;
    }

    int ReconUnknownNeighbours(Vector3Int cell)
    {
        int unknown = 0;
        for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
        {
            var adjacent = cell + new Vector3Int(dx, 0, dz);
            if ((dx != 0 || dz != 0) && Inside(adjacent) && !terrain.ContainsKey(adjacent)) unknown++;
        }
        return unknown;
    }

    int SearchSector(Vector3Int cell) => Mathf.Min(3, cell.z * 4 / depth) * 4 + Mathf.Min(3, cell.x * 4 / width);
    bool TryCoverageHypothesis(AIBoardState board, out Vector3Int center)
    {
        center = default;
        if (terrain.Count < 32 || board.TurnCount < 4) return false;
        float best = -1;
        for (int z = 0; z < 4; z++) for (int x = 0; x < 4; x++)
        {
            int left = x * width / 4, right = (x + 1) * width / 4, bottom = z * depth / 4, top = (z + 1) * depth / 4;
            int area = (right - left) * (top - bottom); if (area <= 0) continue;
            int unknown = Mathf.Max(0, area - knownSearchSectors[z * 4 + x]); if (unknown == 0) continue;
            var candidate = new Vector3Int((left + right - 1) / 2, 0, (bottom + top - 1) / 2);
            float distance = Distance(candidate, GridHelper.ToGridXZ(board.EnemyCrystalPos)) / (float)Mathf.Max(width, depth);
            float score = unknown / (float)area * (.5f + distance);
            if (score <= best) continue;
            best = score; center = candidate;
        }
        if (best < 0) return false;
        HasSearchHypothesis = true; SearchHypothesisCenter = center;
        SearchHypothesisConfidence = Mathf.Min(.55f, .2f + .35f * terrain.Count / ((float)width * depth));
        return true;
    }
}
