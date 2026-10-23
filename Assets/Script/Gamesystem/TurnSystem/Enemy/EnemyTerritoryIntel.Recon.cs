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
        if (board == null || board.ActorTeam != actorTeam || sourceMap == null || !HasUsableTerritory || HasWaypoint || Suspended
            || board.Governor?.Mode == StrategicMode.EmergencyDefense || board.Governor?.Mode == StrategicMode.EconomicRecovery) return false;
        if (reconBoard == board && reconMap == sourceMap && reconGeneration == board.Generation
            && reconCellCount == observedCellCount && reconConfirmedTurn == LastConfirmedTurn)
        { target = reconTarget; return reconTargetAvailable; }
        reconBoard = board; reconMap = sourceMap; reconGeneration = board.Generation;
        reconCellCount = observedCellCount; reconConfirmedTurn = LastConfirmedTurn;
        reconTargetAvailable = false;
        float best = float.PositiveInfinity;
        // Bounded by the existing compressed observation cap. This never asks the world
        // about opponent ownership, unknown terrain, or unseen actors.
        foreach (var region in regions.Values)
            for (int word = 0; word < region.Cells.Length; word++)
            {
                uint bits = (uint)region.Cells[word];
                if (bits == 0) continue;
                for (int bit = 0; bit < 32; bit++)
                    if ((bits & (1u << bit)) != 0)
                    {
                        int index = word * 32 + bit;
                        var cell = new Vector3Int(region.OriginX + index % RegionSize, 0, region.OriginZ + index / RegionSize);
                        Consider(cell);
                        for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
                            if (dx != 0 || dz != 0) Consider(cell + new Vector3Int(dx, 0, dz));
                    }
            }
        target = reconTarget;
        return reconTargetAvailable;

        void Consider(Vector3Int cell)
        {
            if (!Inside(cell) || !Passable(cell)) return;
            float score = CandidateDistance(cell, board);
            if (score > best || score == best && reconTargetAvailable && CompareCell(cell, XZ(reconTarget)) >= 0) return;
            best = score; reconTarget = terrain[cell]; reconTargetAvailable = true;
        }
    }
}
