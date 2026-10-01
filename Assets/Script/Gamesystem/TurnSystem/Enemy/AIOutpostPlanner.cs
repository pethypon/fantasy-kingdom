using System.Collections.Generic;
using UnityEngine;

/// <summary>Observed objectives and supported expansion; cached once per board revision.</summary>
public sealed class AIOutpostPlanner
{
    const int CandidateLimit = 4;
    readonly AIBoardState board;
    readonly List<Status> buildings = new List<Status>();
    readonly List<Vector3> bases = new List<Vector3>();
    readonly List<Vector3Int> dungeons = new List<Vector3Int>();
    readonly List<Vector3Int> best = new List<Vector3Int>(CandidateLimit);
    readonly Dictionary<Vector3Int, float> scores = new Dictionary<Vector3Int, float>();
    int generation = -1;
    DungeonSystem dungeonSource;
    public int EvaluatedSites { get; private set; }
    public AIOutpostPlanner(AIBoardState board) { this.board = board; }

    void Prepare()
    {
        if (generation == board.Generation && dungeonSource == board.DungeonSystem) return;
        generation = board.Generation; dungeonSource = board.DungeonSystem;
        scores.Clear(); best.Clear(); bases.Clear(); dungeons.Clear(); EvaluatedSites = 0;
        bases.Add(board.EnemyCrystalPos);
        board.CollectOwnBuildings(buildings);
        foreach (var building in buildings)
            if (building != null && building.IsAlive && building.facilityKind == FacilityKind.SubCrystal)
                bases.Add(building.transform.position);
        if (dungeonSource != null)
        foreach (var dungeon in dungeonSource.Dungeons)
        {
            // Positions remain known after discovery. Hidden ownership/rewards are never inspected.
            if (!board.IsExploredByEnemy(dungeon.Position) && !board.IsVisibleToEnemy(dungeon.Position)) continue;
            bool owned = false;
            for (int i = 1; i < bases.Count; i++)
                if (GridHelper.ChebyshevDistance(bases[i], dungeon.Position) <= SubCrystalSystem.SubCrystalTerritoryRadius) { owned = true; break; }
            if (!owned) dungeons.Add(dungeon.Position);
        }
        bool hasContact = board.Recon.TryGetContactTarget(out var contact);
        Vector3 direction = board.GetUnexploredDirection();
        foreach (var site in board.SubCrystalPlaceable)
        {
            float score = Evaluate(site, hasContact, contact, direction);
            scores[site] = score; EvaluatedSites++;
            if (score < 40) continue;
            int index = 0;
            while (index < best.Count && (scores[best[index]] > score ||
                (scores[best[index]] == score && Before(best[index], site)))) index++;
            if (index >= CandidateLimit) continue;
            best.Insert(index, site);
            if (best.Count > CandidateLimit) best.RemoveAt(CandidateLimit);
        }
    }

    float Evaluate(Vector3Int site, bool hasContact, Vector3 contact, Vector3 direction)
    {
        if (!board.IsVisibleToEnemy(site)) return -1000;
        float supply = float.PositiveInfinity, support = float.PositiveInfinity;
        foreach (var position in bases) supply = Mathf.Min(supply, GridHelper.ChebyshevDistance(position, site));
        foreach (var unit in board.AliveEnemyUnits)
            if (unit != null && unit.IsAlive && unit.type == Type.Unit)
                support = Mathf.Min(support, GridHelper.ChebyshevDistance(unit.transform.position, site));
        // Keep an isolated foothold for later, when a unit can support it.
        if (supply < 4 || supply > 14 || support > 6) return -1000;
        float risk = 0;
        foreach (var unit in board.AlivePlayerUnits)
        {
            if (unit == null || !unit.IsAlive) continue;
            float distance = GridHelper.ChebyshevDistance(unit.transform.position, site);
            if (distance <= 2) return -1000;
            if (distance <= 5) risk += 25;
        }
        bool dungeonSite = false;
        foreach (var dungeon in dungeons)
            if (GridHelper.ChebyshevDistance(site, dungeon) <= SubCrystalSystem.SubCrystalTerritoryRadius) { dungeonSite = true; break; }
        // Ordinary expansion should not starve the opening economy.
        if (!dungeonSite && EconomyHelper.CountEconBuildings(board) < 2) return -1000;
        float score = dungeonSite ? 100 : 25;
        score += Mathf.Min(supply, 8) * 2 - support * 2 - risk;
        if (hasContact)
        {
            float homeDistance = GridHelper.ChebyshevDistance(board.EnemyCrystalPos, contact);
            score += Mathf.Clamp(homeDistance - GridHelper.ChebyshevDistance(site, contact), -5, 5) * 3;
        }
        else score += Mathf.Max(0, Vector3.Dot(((Vector3)site - board.EnemyCrystalPos).normalized, direction)) * 12;
        return score;
    }

    public IReadOnlyList<Vector3Int> Candidates { get { Prepare(); return best; } }
    public float Score(Vector3 site) { Prepare(); return scores.TryGetValue(GridHelper.ToGrid(site), out float score) ? score : -1000; }
    public string Describe(Vector3 site)
    {
        Prepare();
        foreach (var dungeon in dungeons)
            if (GridHelper.ChebyshevDistance(site, dungeon) <= SubCrystalSystem.SubCrystalTerritoryRadius)
                return "発見済みダンジョンの確保";
        return "探索先の前線・補給拠点";
    }
    public bool TryGetDungeonObjective(Vector3 from, out Vector3 target)
    {
        Prepare(); target = default; float nearest = float.PositiveInfinity;
        foreach (var dungeon in dungeons)
        {
            float distance = GridHelper.ChebyshevDistance(from, dungeon);
            if (distance >= nearest) continue;
            nearest = distance; target = dungeon;
        }
        return !float.IsPositiveInfinity(nearest);
    }
    static bool Before(Vector3Int a, Vector3Int b) => a.x < b.x || (a.x == b.x && (a.z < b.z || (a.z == b.z && a.y < b.y)));
}
