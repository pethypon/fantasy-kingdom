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
        public int ActorId, ReadyRound;
        public SaveSystem.UnitSaveData Unit;
        public WildBossSystem.Snapshot Boss;
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
        var third = GetComponent<ThirdFactionSystem>();
        if (third == null) third = gameObject.AddComponent<ThirdFactionSystem>();
        systems.ThirdFactionSystem = third;
        third.Init(systems, this);
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
        var work = ProcessRoundSteps(round);
        try { while (work.MoveNext()) { } } finally { (work as System.IDisposable)?.Dispose(); }
    }
    public System.Collections.IEnumerator ProcessRoundSteps(int round)
    {
        if (round <= LastRound) yield break;
        LastRound = round;
        PruneDeadActors();
        // Initial inhabitants are world content; later reinforcements must go through IP/Director guards.
        if (!initialPopulationCreated)
        {
            initialPopulationCreated = true;
            if (catalog != null) foreach (var entry in catalog.Monsters)
                if (entry != null && entry.FirstRound <= round)
                    for (int i = 0; i < Mathf.Clamp(entry.Count, 0, 16); i++) Spawn(entry, Team.Monster);
            PreviousMonsterCount = Capture().FindAll(u => u.Team == Team.Monster).Count;
        }
        var work = systems.ThirdFactionSystem.ProcessTurn(round);
        try { while (work.MoveNext()) yield return null; } finally { (work as System.IDisposable)?.Dispose(); }
        systems.MoveGenerator.UnitPointCore();
        systems.RefreshVision();
    }
    readonly List<Status> deadActors = new List<Status>();
    void PruneDeadActors()
    {
        deadActors.Clear();
        foreach (var pair in actorIds)
            if (pair.Key == null || !pair.Key.IsAlive || !pair.Key.gameObject.activeInHierarchy) deadActors.Add(pair.Key);
        foreach (var actor in deadActors) UnregisterSpawn(actor);
    }
    bool initialPopulationCreated;
    public R1ContentCatalog Catalog => catalog;
    public int ActorId(Status status) => actorIds.TryGetValue(status, out int id) ? id : 0;
    public bool CanAct(Status status, int round) => readyRounds.TryGetValue(status, out int ready) && ready <= round;
    readonly Dictionary<Status, int> actorIds = new Dictionary<Status, int>();
    readonly Dictionary<Status, int> readyRounds = new Dictionary<Status, int>();
    public R1ContentCatalog.Encounter Origin(Status status) => origins.TryGetValue(status, out var origin) ? origin.encounter : null;
    public void RegisterSpawn(Status status, R1ContentCatalog.Encounter entry, int member, int ready, int stableId = 0)
    {
        origins[status] = (entry, member);
        actorIds[status] = stableId > 0 ? stableId : systems.ThirdFactionSystem.State.NextActorId++;
        readyRounds[status] = ready;
        systems.MoveGenerator.AddOccupied(systems.MoveGenerator.Cell(status.transform.position));
    }
    public void UnregisterSpawn(Status status)
    { origins.Remove(status); actorIds.Remove(status); readyRounds.Remove(status); }
    public Status StageSpawn(R1ContentCatalog.Encounter entry, Team team, int member, Vector3 position, Transform staging)
    {
        var prefab = member < 0 ? entry.Prefab : entry.Retinue[member];
        if (prefab == null || string.IsNullOrWhiteSpace(entry.Id)) return null;
        var obj = Instantiate(prefab, position, Quaternion.identity, staging);
        var status = obj.GetComponentInChildren<Status>(true);
        if (status == null) { obj.SetActive(false); Destroy(obj); return null; }
        // Prefabs may put Status on an offset child. The actual actor must occupy the validated cell.
        obj.transform.position += position - status.transform.position;
        status.team = team; status.type = Type.Unit; status.isWildBoss = false;
        if (member < 0 && entry.Stats != null) status.kind = entry.Stats.kind;
        int level = AverageCombatLevel();
        UnitData stats = member < 0 ? entry.Stats : systems.UnitSetting.GetDefinitionById(status.unitDefinitionId);
        if (stats == null) systems.UnitSetting.UnitDataMap.TryGetValue(status.kind, out stats);
        if (stats != null) { stats.ApplyToStatus(status, level); stats.InitializeAbilities(status); }
        else { status.AssignedSkillId = -1; SkillData.AssignFixedSkill(status); }
        return status;
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
                && !systems.TerritorySystem.IsInAnyTerritory(Mathf.RoundToInt(cell.x), Mathf.RoundToInt(cell.z))
                && systems.MapCreate.CanTraverse(cell, cell)
                && GridHelper.ChebyshevDistance(cell, systems.CrystalSystem.PCP) >= 6
                && GridHelper.ChebyshevDistance(cell, systems.CrystalSystem.ECP) >= 6) candidates.Add(cell);
        if (!savedPosition.HasValue && candidates.Count == 0) return null;
        var position = savedPosition ?? candidates[systems.ThirdFactionSystem.State.NextRandom(candidates.Count)];
        var staging = new GameObject("NeutralSpawnStaging"); staging.SetActive(false);
        var status = StageSpawn(entry, team, member, position, staging.transform);
        if (status == null) { Destroy(staging); return null; }
        var root = status.transform;
        while (root.parent != null && root.parent != staging.transform) root = root.parent;
        root.SetParent(UnitParent, true); root.gameObject.SetActive(true); Destroy(staging);
        RegisterSpawn(status, entry, member, 0);
        UnitHeadUI.Attach(status.gameObject);
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

    public void GrantRelic(Status defeated, Team winner)
    {
        if (!origins.TryGetValue(defeated, out var origin) || origin.member >= 0 || defeated.team != Team.Intruder) return;
        UniqueRewardSystem.Grant(winner, origin.encounter.Relic, RewardCategory.IntruderRelic, systems);
        UnregisterSpawn(defeated);
    }

    public List<SpawnRecord> Capture()
    {
        var records = new List<SpawnRecord>();
        foreach (var pair in origins)
            if (pair.Key != null && pair.Key.IsAlive && pair.Key.gameObject.activeInHierarchy)
                records.Add(new SpawnRecord { EncounterId = pair.Value.encounter.Id, MemberIndex = pair.Value.member,
                    ActorId = ActorId(pair.Key), ReadyRound = readyRounds[pair.Key], Team = pair.Key.team,
                    Unit = SaveSystem.CaptureUnit(pair.Key), Boss = WildBossSystem.CaptureBossStatus(pair.Key) });
        return records;
    }

    public void Restore(List<SpawnRecord> records, int previous, int round, List<string> spawned)
    {
        if (records == null) return;
        foreach (Transform child in UnitParent)
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
        origins.Clear(); actorIds.Clear(); readyRounds.Clear(); initialPopulationCreated = round >= 0 || records.Count > 0 || previous > 0;
        PreviousMonsterCount = previous; LastRound = round;
        PruneDeadActors();
        SpawnedIntruders.Clear(); if (spawned != null) SpawnedIntruders.AddRange(spawned);
        foreach (var record in records)
        {
            var pool = catalog == null ? null : record.Team == Team.Monster ? catalog.Monsters : catalog.Intruders;
            var entry = pool?.Find(e => e != null && e.Id == record.EncounterId)
                ?? systems.ThirdFactionSystem.FindEncounter(record.EncounterId);
            if (entry == null || record.Unit == null || record.MemberIndex >= entry.Retinue.Length) continue;
            var unit = record.Unit;
            if (!string.IsNullOrEmpty(unit.DefinitionId))
            {
                var definition = systems.UnitSetting.GetDefinitionById(unit.DefinitionId);
                GameObject prefab = definition != null ? definition.prefab : null;
                if (prefab == null) UnitAuthoringCatalog.Load()?.TryGetPrefab(definition, out prefab);
                if (definition == null || prefab == null)
                { Debug.LogWarning("[NeutralFaction] 保存された駒の設定が見つかりません: " + unit.DefinitionId); continue; }
                // Copy the encounter so changing an event's future roster cannot alter saved actors.
                var copy = new R1ContentCatalog.Encounter { Id = entry.Id, Count = entry.Count,
                    FirstRound = entry.FirstRound, VisionRange = entry.VisionRange, Relic = entry.Relic,
                    Prefab = entry.Prefab, Stats = entry.Stats, Retinue = (GameObject[])entry.Retinue.Clone() };
                if (record.MemberIndex < 0) { copy.Stats = definition; copy.Prefab = prefab; }
                else copy.Retinue[record.MemberIndex] = prefab;
                entry = copy;
            }
            var status = Spawn(entry, record.Team, record.MemberIndex, new Vector3(unit.PosX, unit.PosY, unit.PosZ));
            if (status != null)
            {
                SaveGameApplier.ApplyStatusFields(status, unit);
                WildBossSystem.RestoreBossStatus(status, record.Boss);
                RegisterSpawn(status, entry, record.MemberIndex, record.ReadyRound, record.ActorId);
            }
        }
    }
}

public sealed class IndependentFactionState : TurnState
{
    System.Collections.IEnumerator work;
    bool stepping, disposed, finishing;
    float activeSeconds;
    public IndependentFactionState(TurnGenerator turn) : base(turn) { }
    public override void Entry() { work = Systems.NeutralFactionSystem?.ProcessRoundSteps(Context.Turn); }
    public override void Update()
    {
        bool done = work == null || (activeSeconds += Time.unscaledDeltaTime) > 15;
        try { if (!done) { stepping = true; done = !work.MoveNext(); } }
        catch (System.Exception error) { Debug.LogException(error); done = true; }
        finally { stepping = false; if (disposed) DisposeWork(); }
        if (done && !finishing && !Turn.IsGameOver && Turn.CurrentState == this)
        {
            finishing = true; DisposeWork();
            Turn.ChangeState(new WildBossState(Turn));
        }
    }
    void DisposeWork()
    {
        var value = work; work = null;
        try { (value as System.IDisposable)?.Dispose(); } catch (System.Exception error) { Debug.LogException(error); }
    }
    public override void Exit() { disposed = true; if (!stepping) DisposeWork(); }
}
