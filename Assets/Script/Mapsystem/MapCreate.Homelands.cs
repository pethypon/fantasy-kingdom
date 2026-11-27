using System.Collections.Generic;
using UnityEngine;

public partial class MapCreate
{
    public const int HomelandDepth = 3;
    public const int TacticalSize = 15;
    private HashSet<Vector2Int> openingRegion;

    public bool InHomeRows(int z, Team team)
    {
        return InHomeRows(z, team, maxZ);
    }

    public static bool InHomeRows(int z, Team team, int depth)
    {
        return team == Team.Player ? z >= 0 && z < HomelandDepth
            : team == Team.Enemy && z >= depth - HomelandDepth && z < depth;
    }

    public bool OpeningCell(Vector3 cell, Team team)
    {
        return !FixedHomelands || InHomeRows((int)cell.z, team) &&
            (openingRegion == null || openingRegion.Contains(new Vector2Int((int)cell.x, (int)cell.z)));
    }

    // Choose one contiguous landmass with room for both opening rosters.
    private bool PrepareOpeningRegion()
    {
        openingRegion = null;
        var visited = new bool[maxX, maxZ];
        var queue = new Queue<Vector2Int>();
        for (int x = 0; x < maxX; x++)
        for (int z = 0; z < maxZ; z++)
        {
            if (!TryVisit(x, z)) continue;
            var region = new HashSet<Vector2Int>();
            int player = 0, enemy = 0;
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                region.Add(cell);
                if (InHomeRows(cell.y, Team.Player)) player++;
                if (InHomeRows(cell.y, Team.Enemy)) enemy++;
                TryVisit(cell.x + 1, cell.y); TryVisit(cell.x - 1, cell.y);
                TryVisit(cell.x, cell.y + 1); TryVisit(cell.x, cell.y - 1);
            }
            if (player >= 5 && enemy >= 5 && (openingRegion == null || region.Count > openingRegion.Count)
                && HasHomePair(region, Team.Player) && HasHomePair(region, Team.Enemy))
                openingRegion = region;
        }
        return openingRegion != null;

        bool HasHomePair(HashSet<Vector2Int> region, Team team)
        {
            foreach (var cell in region)
                if (InHomeRows(cell.y, team) && (region.Contains(cell + Vector2Int.right) ||
                    InHomeRows(cell.y + 1, team) && region.Contains(cell + Vector2Int.up))) return true;
            return false;
        }

        bool TryVisit(int x, int z)
        {
            if (x < 0 || z < 0 || x >= maxX || z >= maxZ || visited[x,z]) return false;
            visited[x,z] = true;
            if (IsRiver(x,z) || IsHighMountain(x,z)) return false;
            queue.Enqueue(new Vector2Int(x,z));
            return true;
        }
    }
}
