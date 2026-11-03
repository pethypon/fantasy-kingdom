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
    readonly List<Vector3> strongholds = new List<Vector3>();
    readonly HashSet<Vector3Int> connectedSupply = new HashSet<Vector3Int>();
    readonly Queue<Vector3> supplyQueue = new Queue<Vector3>();
    static readonly Vector3[] supplySteps = { Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
    readonly List<Vector3Int> best = new List<Vector3Int>(CandidateLimit);
    readonly Dictionary<Vector3Int, float> scores = new Dictionary<Vector3Int, float>();
    int generation = -1;
    DungeonSystem dungeonSource;
    bool hasAssaultLead;
    public int EvaluatedSites { get; private set; }
    public AIOutpostPlanner(AIBoardState board) { this.board = board; }

    void Prepare()
    {
        if (generation == board.Generation && dungeonSource == board.DungeonSystem) return;
        generation = board.Generation; dungeonSource = board.DungeonSystem;
        scores.Clear(); best.Clear(); bases.Clear(); dungeons.Clear(); strongholds.Clear(); EvaluatedSites = 0;
        board.TerritoryIntel.Observe(board);
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
        var main = board.GetLastKnownPlayerCrystal();
        hasAssaultLead = main.Valid || board.TerritoryIntel.HasUsableTerritory;
        if (main.Valid) { contact = main.Position; hasContact = true; }
        else if (board.TerritoryIntel.HasUsableTerritory && board.TerritoryIntel.HasWaypoint)
        { contact = board.TerritoryIntel.Waypoint; hasContact = true; }
        foreach (var hostile in board.AlivePlayerUnits)
            if (hostile != null && hostile.IsAlive && board.IsSpecialActorObserved(hostile)
                && (hostile.kind == Kind.Boss || hostile.team == Team.Monster || hostile.team == Team.Intruder))
                strongholds.Add(hostile.transform.position);
        PrepareSupplyConnections();
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
        float rememberedRisk = board.TerritoryIntel.RouteRiskAt(site);
        if (rememberedRisk >= 24) return -1000;
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
            // An authored long-range defender can threaten a site beyond the old fixed distance check.
            if (unit.ATK > 0 && board.IsSpecialActorObserved(unit) && AttackPatterns.CanAttack(unit, unit.direction,
                site.x - unit.transform.position.x, site.z - unit.transform.position.z)) return -1000;
            float distance = GridHelper.ChebyshevDistance(unit.transform.position, site);
            if (distance <= 2) return -1000;
            if (distance <= 5) risk += 25;
        }
        bool dungeonSite = false;
        foreach (var dungeon in dungeons)
            if (GridHelper.ChebyshevDistance(site, dungeon) <= SubCrystalSystem.SubCrystalTerritoryRadius) { dungeonSite = true; break; }
        bool strongholdSite = false;
        foreach (var stronghold in strongholds)
        {
            float distance = GridHelper.ChebyshevDistance(site, stronghold);
            if (distance >= 4 && distance <= 10
                && GridHelper.ChebyshevDistance(board.EnemyCrystalPos, stronghold) - distance >= 3) { strongholdSite = true; break; }
        }
        bool assaultSite = hasAssaultLead && hasContact
            && GridHelper.ChebyshevDistance(board.EnemyCrystalPos, contact) - GridHelper.ChebyshevDistance(site, contact) >= 3;
        // The first outpost can support expansion. Keep the final scarce crystal for a
        // concrete observed objective rather than spending both on generic opening growth.
        if (board.EnemySubCrystals == 1 && !dungeonSite && !strongholdSite && !assaultSite) return -1000;
        // Ordinary expansion should not starve the opening economy.
        if (!dungeonSite && EconomyHelper.CountEconBuildings(board) < 2) return -1000;
        float score = dungeonSite ? 100 : 25;
        score += Mathf.Min(supply, 8) * 2 - support * 2 - risk - rememberedRisk * 1.5f;
        if (board.MapCreate != null)
        {
            score += connectedSupply.Contains(GridHelper.ToGridXZ(site)) ? 12 : -20;
            int exits = 0;
            foreach (var step in supplySteps) if (TryKnownSupplyStep(site, (Vector3)site + step, out _)) exits++;
            score += exits >= 2 ? 6 : -12;
        }
        if (hasContact)
        {
            float homeDistance = GridHelper.ChebyshevDistance(board.EnemyCrystalPos, contact);
            score += Mathf.Clamp(homeDistance - GridHelper.ChebyshevDistance(site, contact), -5, 5) * 3;
        }
        else score += Mathf.Max(0, Vector3.Dot(((Vector3)site - board.EnemyCrystalPos).normalized, direction)) * 12;
        foreach (var stronghold in strongholds)
        {
            float distance = GridHelper.ChebyshevDistance(site, stronghold);
            if (distance < 4 || distance > 10) continue;
            float progress = GridHelper.ChebyshevDistance(board.EnemyCrystalPos, stronghold) - distance;
            score += Mathf.Clamp(progress, 0, 6) * 2 + 10;
            break;
        }
        return score;
    }

    void PrepareSupplyConnections()
    {
        connectedSupply.Clear(); supplyQueue.Clear();
        if (board.MapCreate == null) return;
        foreach (var origin in bases)
            foreach (var step in supplySteps)
                if (TryKnownSupplyStep(origin, origin + step, out var point) && connectedSupply.Add(GridHelper.ToGridXZ(point))) supplyQueue.Enqueue(point);
        int work = 0;
        while (supplyQueue.Count > 0 && work++ < 2048)
        {
            var from = supplyQueue.Dequeue();
            foreach (var step in supplySteps)
                if (TryKnownSupplyStep(from, from + step, out var point) && connectedSupply.Add(GridHelper.ToGridXZ(point))) supplyQueue.Enqueue(point);
        }
        supplyQueue.Clear();
    }
    bool TryKnownSupplyStep(Vector3 from, Vector3 desired, out Vector3 point)
    {
        point = desired; var map = board.MapCreate;
        if (map == null || !board.IsTerrainKnown(from) || !board.IsTerrainKnown(desired)
            || !map.TryGetHeight(Mathf.RoundToInt(desired.x), Mathf.RoundToInt(desired.z), out float height)) return false;
        point.y = height;
        return board.TerritoryIntel.RouteRiskAt(point) < 24 && map.CanTraverse(from, point);
    }

    public IReadOnlyList<Vector3Int> Candidates { get { Prepare(); return best; } }
    public float Score(Vector3 site) { Prepare(); return scores.TryGetValue(GridHelper.ToGrid(site), out float score) ? score : -1000; }
    public string Describe(Vector3 site)
    {
        Prepare();
        foreach (var dungeon in dungeons)
            if (GridHelper.ChebyshevDistance(site, dungeon) <= SubCrystalSystem.SubCrystalTerritoryRadius)
                return "発見済みダンジョンの確保";
        foreach (var stronghold in strongholds)
            if (GridHelper.ChebyshevDistance(site, stronghold) >= 4 && GridHelper.ChebyshevDistance(site, stronghold) <= 10)
                return "確認した強敵への補給・再集結拠点";
        if (board.GetLastKnownPlayerCrystal().Valid) return "発見済み本拠点への攻城・増援拠点";
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
