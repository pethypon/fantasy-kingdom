using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Information utility from observations only. Unknown occupants are never queried.
/// Forecasts are estimates, not permission to attack hidden targets.
/// Rebuilt once per board generation; candidate queries reuse cached scores.
/// </summary>
public sealed class AIReconnaissance
{
    readonly AIBoardState board;
    readonly List<Status> scouts = new List<Status>();
    readonly HashSet<int> visibleIds = new HashSet<int>();
    readonly Dictionary<Vector3Int, float> interest = new Dictionary<Vector3Int, float>();
    readonly Dictionary<(int, Vector3Int), float> scores = new Dictionary<(int, Vector3Int), float>();
    readonly HashSet<Vector3Int> forecast = new HashSet<Vector3Int>();
    readonly List<Vector3Int> frontier = new List<Vector3Int>(256);
    readonly HashSet<Vector3Int> footprint = new HashSet<Vector3Int>();
    readonly Vector3[] sectorTargets = new Vector3[8];
    readonly float[] sectorDistances = new float[8];
    readonly bool[] claimedSectors = new bool[8];
    readonly Dictionary<int, Vector3> assignments = new Dictionary<int, Vector3>();
    int generation = -1, threat = -1;
    Vector3 contactTarget;
    bool hasContact;

    public AIReconnaissance(AIBoardState board) { this.board = board; }

    void Prepare()
    {
        if (generation == board.Generation && threat == board.ReconThreatLevel) return;
        generation = board.Generation; threat = board.ReconThreatLevel;
        scores.Clear(); interest.Clear(); scouts.Clear(); visibleIds.Clear(); assignments.Clear();
        hasContact = false;
        float contactPriority = float.NegativeInfinity;
        System.Array.Clear(claimedSectors, 0, claimedSectors.Length);
        foreach (var unit in board.AliveEnemyUnits)
            if (unit != null && unit.IsAlive && unit.kind == Kind.Scout) scouts.Add(unit);
        scouts.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));
        foreach (var unit in board.AlivePlayerUnits)
            if (unit != null)
            {
                visibleIds.Add(unit.GetInstanceID());
                float priority = 100 - GridHelper.ChebyshevDistance(unit.transform.position, board.EnemyCrystalPos);
                if (priority > contactPriority) { contactPriority = priority; contactTarget = unit.transform.position; hasContact = true; }
            }

        // Recent observations guide deployment, never hidden transforms or attack permission.
        if (!hasContact && threat >= 4)
        foreach (var entry in board.ObservedHistory)
        {
            var memory = entry.Value;
            int age = Mathf.Max(0, board.TurnCount - memory.Turn);
            if (!memory.Valid || age >= (threat < 10 ? 3 : 7)) continue;
            Vector3 predicted = memory.Position;
            if (threat >= 31 && memory.Type == Type.Unit && memory.HasPrevious)
                predicted += Vector3.ClampMagnitude(memory.Position - memory.PreviousPosition, 2) * Mathf.Min(age, 2);
            if (board.ReconMap != null)
            {
                predicted.x = Mathf.Clamp(Mathf.Round(predicted.x), 0, board.ReconMap.maxX - 1);
                predicted.z = Mathf.Clamp(Mathf.Round(predicted.z), 0, board.ReconMap.maxZ - 1);
            }
            if (board.IsVisibleToEnemy(predicted)) continue; // This prediction has already been searched.
            float priority = (7 - age) * 10 - GridHelper.ChebyshevDistance(predicted, board.EnemyCrystalPos);
            if (priority > contactPriority) { contactPriority = priority; contactTarget = predicted; hasContact = true; }
        }

        if (threat >= 21)
        {
            foreach (var entry in board.ObservedHistory)
            {
                var memory = entry.Value;
                int age = Mathf.Max(0, board.TurnCount - memory.Turn);
                if (!memory.Valid || age >= 7 || visibleIds.Contains(entry.Key)) continue;
                if (memory.Kind == Kind.Crystal && board.PlayerCrystalVisible) continue;
                Predict(memory, age);
                float importance = memory.Kind == Kind.Crystal ? 65 : memory.Type != Type.Unit ? 30 : memory.Kind == Kind.Scout ? 35 : 24;
                float confidence = 1f - age / 7f;
                // Distribute one contact's value over its possible cells: uncertainty must not multiply its value.
                float value = importance * confidence / Mathf.Max(1, forecast.Count);
                foreach (var cell in forecast)
                {
                    if (board.IsVisibleToEnemy(cell)) continue;
                    float directionWeight = 1;
                    if (threat >= 31 && memory.HasPrevious)
                    {
                        Vector3 heading = memory.Position - memory.PreviousPosition;
                        directionWeight += Mathf.Max(0, Vector3.Dot(heading.normalized, ((Vector3)(cell - memory.Position)).normalized)) * .75f;
                    }
                    interest.TryGetValue(cell, out float existing);
                    interest[cell] = existing + value * directionWeight;
                }
            }
        }

        for (int i = 0; i < 8; i++) sectorDistances[i] = float.PositiveInfinity;
        if (threat < 7 || scouts.Count == 0 || board.ReconMap == null) return;
        // Map extents are public; do not inspect undiscovered terrain/occupants to choose a frontier.
        for (int x = 0; x < board.ReconMap.maxX; x++)
        for (int z = 0; z < board.ReconMap.maxZ; z++)
        {
            var cell = new Vector3(x, 0, z);
            if (board.IsExploredByEnemy(cell)) continue;
            int sector = Sector(cell - board.EnemyCrystalPos);
            float distance = (cell - board.EnemyCrystalPos).sqrMagnitude;
            if (distance >= sectorDistances[sector]) continue;
            sectorDistances[sector] = distance; sectorTargets[sector] = cell;
        }
        for (int index = 0; index < scouts.Count; index++)
        {
            int wanted = index * 8 / scouts.Count;
            for (int offset = 0; offset < 8; offset++)
            {
                int sector = (wanted + offset) % 8;
                if (claimedSectors[sector] || float.IsPositiveInfinity(sectorDistances[sector])) continue;
                claimedSectors[sector] = true;
                assignments[scouts[index].GetInstanceID()] = sectorTargets[sector];
                break;
            }
        }
    }

    void Predict(AIBoardState.LastKnownInfo memory, int age)
    {
        forecast.Clear(); frontier.Clear();
        var start = GridHelper.ToGridXZ(memory.Position);
        forecast.Add(start); frontier.Add(start);
        if (memory.Type != Type.Unit) return;
        // Bounded uncertainty envelope; multiple actions per turn mean this is not an exact reachable set.
        int steps = threat >= 51 ? Mathf.Clamp(age + 1, 1, 3) : 1;
        int first = 0;
        for (int step = 0; step < steps && forecast.Count < 256; step++)
        {
            int end = frontier.Count;
            for (int i = first; i < end && forecast.Count < 256; i++)
            foreach (var offset in MovePatterns.Offsets(memory.Kind, memory.ActionProfile))
            {
                int direction = MovePatterns.IsDirectionIndependent(memory.Kind, memory.ActionProfile) ? 1 : MovePatterns.DirZ(memory.Direction);
                var next = frontier[i] + new Vector3Int(offset.x, 0, offset.y * direction);
                if (!Inside(next) || !KnownLineClear(frontier[i], next, true)) continue;
                if (forecast.Add(next)) frontier.Add(next);
                if (forecast.Count >= 256) break;
            }
            first = end;
        }
    }

    bool Inside(Vector3Int p) => board.ReconMap != null && p.x >= 0 && p.z >= 0 && p.x < board.ReconMap.maxX && p.z < board.ReconMap.maxZ;

    // Supercover terrain test uses only observed cells. Unknown terrain remains uncertain.
    bool KnownLineClear(Vector3Int a, Vector3Int b, bool movement)
    {
        int x = a.x, z = a.z, nx = Mathf.Abs(b.x - x), nz = Mathf.Abs(b.z - z);
        int sx = System.Math.Sign(b.x - x), sz = System.Math.Sign(b.z - z), ix = 0, iz = 0;
        while (ix < nx || iz < nz)
        {
            int decision = (1 + 2 * ix) * nz - (1 + 2 * iz) * nx;
            if (decision == 0)
            {
                if (KnownBlocked(x + sx, z, movement) || KnownBlocked(x, z + sz, movement)) return false;
                x += sx; z += sz; ix++; iz++;
            }
            else if (decision < 0) { x += sx; ix++; }
            else { z += sz; iz++; }
            if ((movement || x != b.x || z != b.z) && KnownBlocked(x, z, movement)) return false;
        }
        return true;
    }

    bool KnownBlocked(int x, int z, bool movement)
    {
        if (!board.IsTerrainKnown(new Vector3(x, 0, z))) return false;
        return board.ReconMap.IsHighMountain(x, z) || (movement && board.ReconMap.IsRiver(x, z));
    }

    public float ScoreMove(Status unit, Vector3 destination)
    {
        Prepare();
        var target = GridHelper.ToGridXZ(destination);
        var key = (unit.GetInstanceID(), target);
        if (scores.TryGetValue(key, out float cached)) return cached;
        bool scout = unit.kind == Kind.Scout;
        footprint.Clear();
        bool blind = StatusEffectSystem.HasDebuff(unit, StatusEffectType.Blind) || StatusEffectSystem.HasDebuff(unit, StatusEffectType.NarrowVision);
        if (blind) footprint.Add(target + new Vector3Int(0, 0, MovePatterns.DirZ(unit.direction)));
        else foreach (var offset in VisionGenerator.BaseVisionOffsets(unit)) footprint.Add(target + GridHelper.ToGridXZ(offset));
        float information = 0, ranged = 0, warning = 0;
        int fresh = 0, overlap = 0;
        foreach (var cell in footprint)
        {
            if (!Inside(cell) || !KnownLineClear(target, cell, false)) continue;
            if (board.IsVisibleToEnemy(cell)) { overlap++; continue; }
            bool unknown = !board.IsExploredByEnemy(cell);
            if (unknown) fresh++;
            interest.TryGetValue(cell, out float expected);
            information += expected;
            if (threat >= 31)
            {
                foreach (var ally in board.AliveEnemyUnits)
                {
                    if (ally == null || ally == unit || !IsRanged(ally.kind)) continue;
                    var from = GridHelper.ToGridXZ(ally.transform.position);
                    if (!AttackPatterns.CanAttack(ally, ally.direction, cell.x - from.x, cell.z - from.z)) continue;
                    if (!MapCreate.IsArcingAttack(ally.kind, ally.facilityKind) && !KnownLineClear(from, cell, false)) continue;
                    ranged += unknown ? 1.5f : expected * .3f;
                    break;
                }
            }
            if (threat >= 51)
            {
                float distance = GridHelper.ChebyshevDistance(cell, board.EnemyCrystalPos);
                if (distance >= 3 && distance <= 6) warning += .4f;
            }
        }
        float score = Mathf.Min(fresh * (scout ? 2.5f : .7f), scout ? 40 : 12);
        score += Mathf.Min(information, 55) + Mathf.Min(ranged, 18) + Mathf.Min(warning, 8);
        if (!scout && hasContact && unit.kind != Kind.King && unit.HP >= unit.MaxHP / 2)
        {
            float progress = GridHelper.ChebyshevDistance(unit.transform.position, contactTarget) - GridHelper.ChebyshevDistance(destination, contactTarget);
            float risk = board.EstimateCounterDamageAt(destination, unit);
            if (risk < unit.HP / 2f) score += Mathf.Clamp(progress, -3, 3) * 7;
        }
        if (scout && threat >= 7 && fresh > 0)
        {
            if (TryGetAssignedTarget(unit, out var goal))
                score += Mathf.Clamp(Vector3.Distance(unit.transform.position, goal) - Vector3.Distance(destination, goal), -2, 2) * 5;
            foreach (var other in scouts)
                if (other != unit && GridHelper.ChebyshevDistance(destination, other.transform.position) <= 2) score -= 10;
            score -= overlap * .2f;
        }
        if (scout)
        {
            if (board.Outposts.TryGetDungeonObjective(unit.transform.position, out var dungeon))
                score += Mathf.Clamp(GridHelper.ChebyshevDistance(unit.transform.position, dungeon) - GridHelper.ChebyshevDistance(destination, dungeon), -3, 3) * 6;
            float risk = board.EstimateCounterDamageAt(destination, unit);
            score -= Mathf.Min(70, risk / Mathf.Max(1, unit.HP) * 65);
            if (board.GetNearestAllyDist(destination, unit) > 6 && (risk > 0 || unit.HP < unit.MaxHP / 2)) score -= 15;
        }
        scores[key] = score;
        return score;
    }

    public bool TryGetAssignedTarget(Status scout, out Vector3 target)
    {
        Prepare();
        return assignments.TryGetValue(scout.GetInstanceID(), out target);
    }

    public float InformationAt(Vector3Int cell) { Prepare(); return interest.TryGetValue(GridHelper.ToGridXZ(cell), out float value) ? value : 0; }
    public bool TryGetContactTarget(out Vector3 target) { Prepare(); target = contactTarget; return hasContact; }
    static int Sector(Vector3 direction) => ((Mathf.RoundToInt(Mathf.Atan2(direction.z, direction.x) * 4 / Mathf.PI) % 8) + 8) % 8;
    static bool IsRanged(Kind kind) => kind == Kind.Archer || kind == Kind.Crossbow || kind == Kind.Magicsniper || kind == Kind.Magic || kind == Kind.Bomber;
}
