using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Owns turn sequencing, persistence, and event transactions; combat remains in shared systems.</summary>
public sealed class ThirdFactionSystem : MonoBehaviour
{
    [SerializeField] ThirdFactionDirectorConfig config;
    public ThirdFactionDirectorConfig Config => config;
    public ThirdFactionDirectorState State { get; private set; }
    public ThirdFactionWorldSnapshot LastSnapshot { get; private set; }
    public ThirdFactionDecision LastDecision { get; private set; }
    public ThirdFactionIPSystem IP { get; private set; }
    public int LastIPBefore { get; private set; }
    public int LastIPAfterRegen { get; private set; }
    readonly List<Status> actors = new List<Status>();
    GameSystems systems;
    NeutralFactionSystem neutral;
    ThirdFactionDirector director;
    ThirdFactionTacticalAI tactical;
    bool ownsConfig;

    public void Init(GameSystems value, NeutralFactionSystem factions)
    {
        systems = value; neutral = factions;
        if (config == null) config = Resources.Load<ThirdFactionDirectorConfig>("AI/ThirdFaction/DirectorConfig");
        if (config == null) { config = ScriptableObject.CreateInstance<ThirdFactionDirectorConfig>(); ownsConfig = true; }
        State = new ThirdFactionDirectorState { CurrentIP = Mathf.Clamp(config.InitialIP, 0, 50) };
        State.RandomState = unchecked(Mathf.RoundToInt(systems.MapCreate.SeedX * 1000) ^ Mathf.RoundToInt(systems.MapCreate.SeedZ * 1000) ^ 19790317);
        IP = new ThirdFactionIPSystem(State);
        director = new ThirdFactionDirector(config);
        tactical = new ThirdFactionTacticalAI(systems, neutral, config);
    }
    public R1ContentCatalog.Encounter FindEncounter(string id)
    {
        foreach (var d in config.Events ?? Array.Empty<ThirdFactionEventDefinition>())
            if (d != null && (d.EventId == id || d.EncounterId == id)) return ThirdFactionDirector.ResolveEncounter(d, neutral.Catalog);
        return null;
    }
    public void RecordDamage(Status target, int damage)
    {
        if (State == null || damage <= 0 || target == null) return;
        if (target.team != Team.Player && target.team != Team.Enemy) return;
        State.BattleCount++;
        if (target.kind == Kind.King || target.kind == Kind.Crystal)
            State.ObjectiveDamage = (int)Math.Min(int.MaxValue, (long)State.ObjectiveDamage + damage);
    }

    public IEnumerator ProcessTurn(int round)
    {
        if (State.LastProcessedTurn >= round) yield break;
        State.LastProcessedTurn = round;
        var ap = systems.APSystem;
        ap.ResetAP(Team.Monster); ap.ResetAP(Team.Intruder);
        ap.ResetFatigue(neutral.UnitParent);
        StatusEffectSystem.TickAllUnits(Team.Monster, neutral.UnitParent);
        StatusEffectSystem.TickAllUnits(Team.Intruder, neutral.UnitParent);
        SpecialAbilitySystem.OnTurnStart(neutral.UnitParent);
        LastIPBefore = IP.Current;
        IP.BeginTurn();
        LastIPAfterRegen = IP.Current;
        if (State.StrongEnemyBaseRadius <= 0 && systems.WildBossSystem?.SpawnedBoss != null)
            State.StrongEnemyBaseRadius = systems.WildBossSystem.SpawnedBoss.wildBossTerritoryRadius;
        if (GameOver) yield break;
        try
        {
            LastSnapshot = ThirdFactionWorldSnapshot.Capture(systems, round,
                systems.AICommander?.ThreatLevel.Level ?? 1, State, actors);
        }
        catch (Exception error)
        {
            Debug.LogException(error); LastSnapshot = null;
            LastDecision = new ThirdFactionDecision { Reason = "snapshot_error" };
        }
        yield return null;
        if (LastSnapshot != null) LastDecision = director.Decide(LastSnapshot, State, systems, neutral.Catalog);
        if (!LastDecision.NoEvent)
        {
            if (ThirdFactionEventExecutor.Execute(LastDecision.Event, IP, PrepareEvent))
                RecordEvent(LastDecision.Event, round);
            else { LastDecision.Event = null; LastDecision.Reason = "event_execution_failed"; }
        }
        if (LastSnapshot != null) StoreHistory(round);
        State.Objectives.RemoveAll(o => o == null || o.ExpireTurn < round);
        State.ActiveEvents.RemoveAll(e => e == null || e.ExpireTurn < round);
        yield return null;
        var steps = tactical.Execute(State, round);
        try { while (!GameOver && steps.MoveNext()) yield return null; }
        finally { (steps as IDisposable)?.Dispose(); }
        if (!GameOver) SpecialAbilitySystem.OnTurnEnd(neutral.UnitParent);
        RecordTelemetry(round);
    }
    [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    void RecordTelemetry(int round)
    {
        var w = LastSnapshot;
        DevelopmentLog.Log($"[ThirdFaction] turn={round} ip_before={LastIPBefore} ip_regen=5 ip_after_regen={LastIPAfterRegen} ip_after_decision={IP.Current} mode={LastDecision.Mode} decision={LastDecision.Event?.Definition.EventId ?? "NoEvent"} reason={LastDecision.Reason} active_war={w?.ActiveWar:F2} stagnation={w?.Stagnation:F2} natural_conflict={w?.Prediction?.Probability:F2} decisive={(w?.Decisive == true ? 1 : 0)} player_power={w?.Player.MilitaryPower:F1} enemy_power={w?.Enemy.MilitaryPower:F1} third_power={w?.Third.MilitaryPower:F1}");
        foreach (var a in LastDecision.Candidates)
            DevelopmentLog.Log($"[ThirdFactionCandidate] turn={round} id={a.Definition.EventId} cost={a.Definition.IpCost} score={a.Score:F1} guard={(a.Safe ? "allow" : "reject")} reason={a.Rejection ?? "eligible"}");
    }
    bool GameOver => systems.MoveGenerator.turnGenerator != null && systems.MoveGenerator.turnGenerator.IsGameOver;
    void StoreHistory(int round)
    {
        var w = LastSnapshot;
        State.History.Add(new ThirdFactionHistory { Turn = round, Battles = State.BattleCount,
            ObjectiveDamage = State.ObjectiveDamage, Territory = w.Player.Territory + w.Enemy.Territory,
            PlayerPower = w.Player.MilitaryPower, EnemyPower = w.Enemy.MilitaryPower,
            PlayerEconomy = w.Player.EconomyPower, EnemyEconomy = w.Enemy.EconomyPower, ObjectiveHP = w.Player.ObjectiveHP + w.Enemy.ObjectiveHP });
        while (State.History.Count > Mathf.Clamp(config.HistoryTurns, 2, 20)) State.History.RemoveAt(0);
        State.BattleCount = 0; State.ObjectiveDamage = 0;
    }
    void RecordEvent(ThirdFactionEventCandidate a, int turn)
    {
        var d = a.Definition;
        State.LastAnyEventTurn = turn;
        if (d.IsMajor) State.LastMajorEventTurn = turn;
        var stamp = State.Events.Find(e => e.Id == d.EventId);
        if (stamp == null) { stamp = new ThirdFactionEventStamp { Id = d.EventId }; State.Events.Add(stamp); }
        stamp.RepeatCount = turn - stamp.Turn > 10 ? 1 : Mathf.Min(10, stamp.RepeatCount + 1);
        stamp.Turn = turn; stamp.Category = d.Category;
    }
    public IThirdFactionPreparedEvent PrepareEvent(ThirdFactionEventCandidate a)
    {
        if (GameOver || a == null || !a.Safe) return null;
        var w = LastSnapshot;
        // Validation is repeated against current occupancy before reserving any live objects.
        foreach (var cell in a.SpawnCells)
            if (!systems.MapCreate.TryGetHeight(Mathf.RoundToInt(cell.x), Mathf.RoundToInt(cell.z), out float y)
                || Mathf.Abs(y - cell.y) > .01f || !systems.MapCreate.CanTraverse(cell, cell)
                || systems.MoveGenerator.IsOccupied(systems.MoveGenerator.Cell(cell))
                || systems.BuildSystem.HasBuildingAt(GridHelper.ToGrid(cell))
                || !ThirdFactionInterventionGuard.SafeSpawn(w, config, cell)) return null;
        return new PreparedEvent(this, a);
    }

    sealed class PreparedEvent : IThirdFactionPreparedEvent
    {
        readonly ThirdFactionSystem owner;
        readonly ThirdFactionEventCandidate candidate;
        readonly List<Status> spawned = new List<Status>();
        readonly List<GameObject> spawnedRoots = new List<GameObject>();
        readonly List<int> members = new List<int>();
        GameObject staging;
        readonly int previousId, previousRadius;
        Status expandedBoss;
        bool committed;
        ThirdFactionActiveEvent active;
        public PreparedEvent(ThirdFactionSystem system, ThirdFactionEventCandidate value)
        {
            owner = system; candidate = value; previousId = owner.State.NextActorId;
            try
            {
                if (value.Definition.Category == ThirdFactionEventCategory.StrongEnemyExpansion)
                {
                    expandedBoss = owner.systems.WildBossSystem.SpawnedBoss;
                    previousRadius = expandedBoss.wildBossTerritoryRadius;
                    foreach (var a in owner.LastSnapshot.Actors)
                        if (a.Objective && GridHelper.ChebyshevDistance(a.Cell, expandedBoss.wildBossTerritoryCenter)
                            < previousRadius + value.Definition.LocalExpansionRadius + owner.config.ResponseTurns * 2)
                            throw new InvalidOperationException("Unsafe boss territory expansion");
                    return;
                }
                staging = new GameObject("ThirdFactionEventStaging"); staging.SetActive(false);
                staging.transform.SetParent(owner.transform, false);
                int leaders = Mathf.Clamp(value.Definition.SpawnCount, 1, 8);
                for (int i = 0; i < value.SpawnCells.Count; i++)
                {
                    int member = i < leaders ? -1 : i - leaders;
                    var u = owner.neutral.StageSpawn(value.Encounter, value.Definition.SpawnTeam, member, value.SpawnCells[i], staging.transform);
                    if (u == null) throw new InvalidOperationException("Event prefab has no Status");
                    if (value.Definition.Category == ThirdFactionEventCategory.SummonStrongEnemy)
                    {
                        u.isWildBoss = true; u.wildBossTerritoryCenter = u.GridPosition; u.wildBossTerritoryRadius = 2;
                        u.wildBossMaxAP = 20; u.wildBossAP = 20;
                    }
                    var root = u.transform;
                    while (root.parent != null && root.parent != staging.transform) root = root.parent;
                    spawned.Add(u); spawnedRoots.Add(root.gameObject); members.Add(member);
                }
            }
            catch { Rollback(); throw; }
        }
        public void Commit()
        {
            if (committed) throw new InvalidOperationException("Event already committed");
            var d = candidate.Definition;
            int round = owner.State.LastProcessedTurn;
            active = new ThirdFactionActiveEvent { EventId = d.EventId, StartedTurn = round, ExpireTurn = round + d.ObjectiveTurns };
            owner.State.ActiveEvents.Add(active);
            if (expandedBoss != null) expandedBoss.wildBossTerritoryRadius = previousRadius + d.LocalExpansionRadius;
            for (int i = 0; i < spawned.Count; i++)
            {
                var u = spawned[i];
                var root = spawnedRoots[i].transform;
                root.SetParent(owner.neutral.UnitParent, true);
                root.gameObject.SetActive(true);
                owner.neutral.RegisterSpawn(u, candidate.Encounter, members[i], d.CanActImmediately ? round : round + 1);
                int id = owner.neutral.ActorId(u); active.Actors.Add(id);
                owner.State.Objectives.Add(new ThirdFactionObjective { ActorId = id, Kind = d.Objective,
                    TargetCell = candidate.TargetCell, Priority = d.IsMajor ? 2 : 1, ExpireTurn = round + d.ObjectiveTurns });
                UnitHeadUI.Attach(u.gameObject);
            }
            if (d.SpawnTeam == Team.Intruder && !owner.neutral.SpawnedIntruders.Contains(candidate.Encounter.Id))
                owner.neutral.SpawnedIntruders.Add(candidate.Encounter.Id);
            committed = true;
        }
        public void Rollback()
        {
            if (expandedBoss != null) expandedBoss.wildBossTerritoryRadius = previousRadius;
            if (active != null)
            {
                owner.State.Objectives.RemoveAll(o => active.Actors.Contains(o.ActorId));
                owner.State.ActiveEvents.Remove(active);
            }
            for (int i = 0; i < spawned.Count; i++)
            {
                if (spawned[i] != null) owner.neutral.UnregisterSpawn(spawned[i]);
                if (spawnedRoots[i] != null) { spawnedRoots[i].SetActive(false); Destroy(spawnedRoots[i]); }
            }
            owner.State.NextActorId = previousId;
            owner.systems.MoveGenerator.UnitPointCore();
            committed = false;
        }
        public void Dispose()
        {
            if (!committed) Rollback();
            if (staging != null) { staging.SetActive(false); Destroy(staging); staging = null; }
        }
    }

    public ThirdFactionDirectorState Capture()
    {
        State.MonsterAP = systems.FactionState.MonsterAP; State.IntruderAP = systems.FactionState.IntruderAP;
        return JsonUtility.FromJson<ThirdFactionDirectorState>(JsonUtility.ToJson(State));
    }
    public void Restore(ThirdFactionDirectorState saved)
    {
        if (saved == null) return; // Older saves retain a fresh, safe Director state.
        State = JsonUtility.FromJson<ThirdFactionDirectorState>(JsonUtility.ToJson(saved));
        State.Events = State.Events ?? new List<ThirdFactionEventStamp>();
        State.History = State.History ?? new List<ThirdFactionHistory>();
        State.Objectives = State.Objectives ?? new List<ThirdFactionObjective>();
        State.ActiveEvents = State.ActiveEvents ?? new List<ThirdFactionActiveEvent>();
        State.NextActorId = Mathf.Max(1, State.NextActorId);
        foreach (var u in neutral.UnitParent.GetComponentsInChildren<Status>()) State.NextActorId = Mathf.Max(State.NextActorId, neutral.ActorId(u) + 1);
        State.CurrentIP = Mathf.Clamp(State.CurrentIP, 0, 50);
        RestoreAP(State.MonsterAP, systems.FactionState.MonsterAP);
        RestoreAP(State.IntruderAP, systems.FactionState.IntruderAP);
        IP = new ThirdFactionIPSystem(State); LastSnapshot = null; LastDecision = null;
    }
    static void RestoreAP(FactionState.APData source, FactionState.APData destination)
    {
        if (source == null) return;
        destination.Reset = Mathf.Clamp(source.Reset, 3, GameConstants.MaxAP);
        destination.Plus = Mathf.Clamp(source.Plus, 0, GameConstants.MaxAP);
        destination.Minus = Mathf.Clamp(source.Minus, 0, GameConstants.MaxAP);
        destination.Current = Mathf.Clamp(source.Current, 0, destination.Maximum);
    }
    void OnDestroy() { if (ownsConfig && config != null) Destroy(config); }
}
