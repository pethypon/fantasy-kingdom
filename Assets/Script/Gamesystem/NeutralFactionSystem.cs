using System.Collections.Generic;
using UnityEngine;

/// <summary>魔物と乱入者の独立勢力。出現内容はR1ContentCatalogで定義する。</summary>
public class NeutralFactionSystem : MonoBehaviour
{
    [System.Serializable]
    public class SpawnRecord
    {
        public string EncounterId;
        public Team Team;
        public int MemberIndex = -1;
        public SaveSystem.UnitSaveData Unit;
    }

    public Transform UnitParent { get; private set; }
    public int PreviousMonsterCount { get; private set; }
    public int LastRound { get; private set; } = -1;
    public readonly List<string> SpawnedIntruders = new List<string>();
    readonly Dictionary<Status, (R1ContentCatalog.Encounter encounter, int member)> origins = new Dictionary<Status, (R1ContentCatalog.Encounter, int)>();
    readonly List<Status> combatTargets = new List<Status>();
    GameSystems systems;
    R1ContentCatalog catalog;

    public void Init(GameSystems value)
    {
        systems = value;
        catalog = R1ContentCatalog.Load();
        var holder = new GameObject("IndependentFactions");
        holder.transform.SetParent(transform, false);
        UnitParent = holder.transform;
    }

    public static bool AreHostile(Status a, Status b)
    {
        if (a == null || b == null || a.team == b.team || a.team == Team.None || b.team == Team.None) return false;
        if ((a.team == Team.Intruder && (b.isWildBoss || b.team == Team.Obstacle))
            || (b.team == Team.Intruder && (a.isWildBoss || a.team == Team.Obstacle))) return false;
        return true;
    }

    public static bool ShouldReplenish(int round, int previous, int current)
        => round > 0 && round % 10 == 0 && current * 2 <= previous;

    public void ProcessRound(int round)
    {
        if (round <= LastRound || catalog == null) return;
        LastRound = round;
        var units = UnitParent.GetComponentsInChildren<Status>();
        int monsters = 0;
        foreach (var unit in units) if (unit.IsAlive && unit.team == Team.Monster) monsters++;
        if (PreviousMonsterCount == 0 || ShouldReplenish(round, PreviousMonsterCount, monsters))
        {
            foreach (var entry in catalog.Monsters)
                if (entry != null && entry.FirstRound <= round)
                    for (int i = 0; i < entry.Count; i++) Spawn(entry, Team.Monster);
            PreviousMonsterCount = 0;
            foreach (var unit in UnitParent.GetComponentsInChildren<Status>())
                if (unit.IsAlive && unit.team == Team.Monster) PreviousMonsterCount++;
        }
        foreach (var entry in catalog.Intruders)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Id) || entry.FirstRound > round || SpawnedIntruders.Contains(entry.Id)) continue;
            if (Spawn(entry, Team.Intruder) == null) continue;
            SpawnedIntruders.Add(entry.Id);
            for (int i = 0; i < entry.Retinue.Length; i++) Spawn(entry, Team.Intruder, i);
        }
        // No units are spawned during these actions. Recheck liveness before using this snapshot.
        CombatRegistry.Collect(combatTargets);
        var targets = combatTargets;
        ActFaction(Team.Monster, targets);
        ActFaction(Team.Intruder, targets);
        systems.MoveGenerator.UnitPointCore();
        systems.RefreshVision();
    }

    Status Spawn(R1ContentCatalog.Encounter entry, Team team, int member = -1, Vector3? savedPosition = null)
    {
        var prefab = member < 0 ? entry.Prefab : entry.Retinue[member];
        if (string.IsNullOrWhiteSpace(entry.Id) || prefab == null || (member < 0 && entry.Stats == null)) return null;
        systems.MoveGenerator.UnitPointCore();
        var candidates = new List<Vector3>();
        foreach (var cell in systems.MapCreate.SetPos)
            if (!systems.MoveGenerator.IsOccupied(systems.MoveGenerator.Cell(cell))
                && !systems.BuildSystem.HasBuildingAt(GridHelper.ToGrid(cell))
                && !systems.TerritorySystem.IsInAnyTerritory(Mathf.RoundToInt(cell.x), Mathf.RoundToInt(cell.z))) candidates.Add(cell);
        if (!savedPosition.HasValue && candidates.Count == 0) return null;
        var position = savedPosition ?? candidates[Random.Range(0, candidates.Count)];
        var obj = Instantiate(prefab, position, Quaternion.identity, UnitParent);
        var status = obj.GetComponentInChildren<Status>();
        if (status == null) { Destroy(obj); return null; }
        status.team = team;
        status.type = Type.Unit;
        status.isWildBoss = false;
        int level = AverageCombatLevel();
        if (member < 0 && entry.Stats != null) entry.Stats.ApplyToStatus(status, level);
        else if (systems.UnitSetting.UnitDataMap.TryGetValue(status.kind, out var memberStats)) memberStats.ApplyToStatus(status, level);
        status.AssignedSkillId = -1;
        SkillData.AssignFixedSkill(status);
        origins[status] = (entry, member);
        systems.MoveGenerator.AddOccupied(systems.MoveGenerator.Cell(position));
        UnitHeadUI.Attach(obj);
        return status;
    }

    int AverageCombatLevel()
    {
        int sum = 0, count = 0;
        foreach (var parent in new[] { systems.UnitSetting.PlayerUnit, systems.UnitSetting.EnemyUnit })
            foreach (var unit in parent.GetComponentsInChildren<Status>())
                if (unit.IsAlive && unit.type == Type.Unit) { sum += unit.Level; count++; }
        return count == 0 ? 1 : Mathf.Max(1, Mathf.RoundToInt((float)sum / count));
    }

    void ActFaction(Team team, List<Status> targets)
    {
        if (systems.MoveGenerator.turnGenerator != null && systems.MoveGenerator.turnGenerator.IsGameOver) return;
        StatusEffectSystem.TickAllUnits(team, UnitParent);
        foreach (var unit in UnitParent.GetComponentsInChildren<Status>())
        {
            if (systems.MoveGenerator.turnGenerator != null && systems.MoveGenerator.turnGenerator.IsGameOver) return;
            if (!unit.IsAlive || unit.team != team || !origins.TryGetValue(unit, out var origin)) continue;
            if (StatusEffectSystem.IsStunned(unit)) continue;
            Status target = null;
            float nearest = float.MaxValue;
            foreach (var other in targets)
            {
                if (other == null || !other.gameObject.activeInHierarchy || !other.IsAlive || !AreHostile(unit, other)) continue;
                float distance = GridHelper.ChebyshevDistance(unit.GridPosition, other.GridPosition);
                if (distance > origin.encounter.VisionRange || distance >= nearest) continue;
                if (!systems.MapCreate.HasClearTerrainLine(unit.transform.position, other.transform.position)) continue;
                target = other; nearest = distance;
            }
            if (target != null && nearest <= 1)
            {
                int actual = target.ShieldTurns > 0 ? 0 : target.ApplyDamage(DamageCalculator.CalcNormal(unit, target));
                Status.AwardDamageExperience(unit, target, actual, systems.FactionState);
                if (!target.IsAlive && (target.type == Type.Building || target.type == Type.Wall))
                    systems.SubCrystalSystem.DestroyBuilding(target);
                else target.HandleDeathIfDead();
                continue;
            }
            if (StatusEffectSystem.IsMovementBlocked(unit)) continue;
            systems.MoveGenerator.UnitPointCore();
            var moves = new List<Vector3>();
            var position = unit.GridPosition;
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dz == 0) continue;
                int x = position.x + dx, z = position.z + dz;
                if (!systems.MapCreate.TryGetHeight(x, z, out float y)) continue;
                var cell = new Vector3(x, y, z);
                if (systems.MoveGenerator.IsOccupied(systems.MoveGenerator.Cell(cell))) continue;
                if (!systems.MapCreate.CanTraverse(unit.transform.position, cell)) continue;
                moves.Add(cell);
            }
            if (moves.Count == 0) continue;
            Vector3 destination = moves[Random.Range(0, moves.Count)];
            if (target != null)
                foreach (var cell in moves)
                    if ((cell - target.transform.position).sqrMagnitude < (destination - target.transform.position).sqrMagnitude) destination = cell;
            unit.transform.position = destination;
        }
    }

    public void GrantRelic(Status defeated, Team winner)
    {
        if (!origins.TryGetValue(defeated, out var origin) || origin.member >= 0 || defeated.team != Team.Intruder) return;
        UniqueRewardSystem.Grant(winner, origin.encounter.Relic, RewardCategory.IntruderRelic, systems);
        origins.Remove(defeated);
    }

    public List<SpawnRecord> Capture()
    {
        var records = new List<SpawnRecord>();
        foreach (var pair in origins)
            if (pair.Key != null && pair.Key.IsAlive && pair.Key.gameObject.activeInHierarchy)
                records.Add(new SpawnRecord { EncounterId = pair.Value.encounter.Id, MemberIndex = pair.Value.member,
                    Team = pair.Key.team, Unit = SaveSystem.CaptureUnit(pair.Key) });
        return records;
    }

    public void Restore(List<SpawnRecord> records, int previous, int round, List<string> spawned)
    {
        if (catalog == null || records == null) return;
        foreach (Transform child in UnitParent)
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
        origins.Clear();
        PreviousMonsterCount = previous; LastRound = round;
        SpawnedIntruders.Clear(); if (spawned != null) SpawnedIntruders.AddRange(spawned);
        foreach (var record in records)
        {
            var pool = record.Team == Team.Monster ? catalog.Monsters : catalog.Intruders;
            var entry = pool.Find(e => e != null && e.Id == record.EncounterId);
            if (entry == null || record.Unit == null || record.MemberIndex >= entry.Retinue.Length) continue;
            var unit = record.Unit;
            var status = Spawn(entry, record.Team, record.MemberIndex, new Vector3(unit.PosX, unit.PosY, unit.PosZ));
            if (status != null) SaveGameApplier.ApplyStatusFields(status, unit);
        }
    }
}

public class IndependentFactionState : TurnState
{
    public IndependentFactionState(TurnGenerator turn) : base(turn) { }
    public override void Entry()
    {
        Systems.NeutralFactionSystem?.ProcessRound(Context.Turn);
        if (!Turn.IsGameOver) Turn.ChangeState(new WildBossState(Turn));
    }
}
