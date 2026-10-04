#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

public static class ThirdFactionTests
{
    static void Check(string label, bool value)
    { if (!value) throw new Exception("[ThirdFactionTests] FAIL " + label); Debug.Log("[ThirdFactionTests] PASS " + label); }
    public static void EditMode()
    {
        var state = new ThirdFactionDirectorState { CurrentIP = 49 };
        var ip = new ThirdFactionIPSystem(state);
        ip.BeginTurn(); Check("IP refill caps at 50", ip.Current == 50);
        for (int i = 0; i < 10; i++) ip.BeginTurn(); Check("full IP never exceeds cap", ip.Current == 50);
        using (var spend = ip.TryReserve(20)) { Check("reservation subtracts cost", ip.Current == 30); }
        Check("failed preparation refunds IP", ip.Current == 50);
        using (var spend = ip.TryReserve(20)) spend.Commit();
        Check("committed event spends exactly once", ip.Current == 30);
        Check("insufficient IP cannot reserve", ip.TryReserve(31) == null && ip.Current == 30);
        Check("negative and above-cap costs rejected", ip.TryReserve(-1) == null && ip.TryReserve(51) == null);
        using (var spend = ip.TryReserve(10))
        { Check("nested reservation rejected", ip.TryReserve(1) == null); spend.Commit(); spend.Commit(); }
        Check("commit idempotent while reserved", ip.Current == 20);
        ip.BeginTurn(); Check("IP refill is exactly five", ip.Current == 25);
        var cfg = ScriptableObject.CreateInstance<ThirdFactionDirectorConfig>();
        var def = ScriptableObject.CreateInstance<ThirdFactionEventDefinition>();
        def.EventId = "test"; def.IpCost = 10; def.Scale = ThirdFactionEventScale.Major;
        var candidate = new ThirdFactionEventCandidate { Definition = def, PlayerLoss = 4 };
        candidate.SpawnCells.Add(new Vector3(20, 1, 20));
        var w = new ThirdFactionWorldSnapshot { Turn = 20, ThreatLevel = 15 };
        w.Player.MilitaryPower = 120; w.Enemy.MilitaryPower = 100;
        state.CurrentIP = 50;
        Check("legitimate small pressure allowed", ThirdFactionInterventionGuard.Reject(candidate, w, cfg, state) == null);
        candidate.PlayerLoss = 21;
        Check("leader flip prevented", ThirdFactionInterventionGuard.Reject(candidate, w, cfg, state) == "would_erase_legitimate_advantage");
        candidate.PlayerLoss = 15;
        Check("excess advantage erase prevented", ThirdFactionInterventionGuard.Reject(candidate, w, cfg, state) == "would_erase_legitimate_advantage");
        candidate.PlayerLoss = 10; w.BalancedWar = true;
        Check("balanced war preserved", ThirdFactionInterventionGuard.Reject(candidate, w, cfg, state) == "active_balanced_war");
        w.BalancedWar = false; w.Decisive = true;
        Check("decisive king or crystal phase protected", ThirdFactionInterventionGuard.Reject(candidate, w, cfg, state) == "decisive_phase_protected");
        w.Decisive = false; state.LastMajorEventTurn = 18;
        Check("major cooldown", ThirdFactionInterventionGuard.Reject(candidate, w, cfg, state) == "major_event_cooldown");
        state.LastMajorEventTurn = -10000; state.Events.Add(new ThirdFactionEventStamp { Id = "test", Turn = 19 });
        Check("same event cooldown", ThirdFactionInterventionGuard.Reject(candidate, w, cfg, state) == "same_event_cooldown");
        state.Events.Clear(); candidate.PlayerLoss = 100;
        Check("game-breaking swing rejected", ThirdFactionInterventionGuard.Reject(candidate, w, cfg, state) == "would_break_game");
        candidate.PlayerLoss = 0; candidate.SpawnCells.Clear();
        Check("missing spawn rejected", ThirdFactionInterventionGuard.Reject(candidate, w, cfg, state) == "invalid_spawn_position");
        candidate.SpawnCells.Add(new Vector3(20, 1, 20)); state.CurrentIP = 1;
        Check("guard insufficient IP", ThirdFactionInterventionGuard.Reject(candidate, w, cfg, state) == "insufficient_ip");
        state.CurrentIP = 50; w.ThreatLevel = 0;
        Check("threat gate", ThirdFactionInterventionGuard.Reject(candidate, w, cfg, state) == "threat_level_locked");
        w.Actors.Add(new ThirdFactionWorldSnapshot.Actor { Objective = true, Cell = new Vector3Int(3, 1, 3), Team = Team.Player });
        Check("objective immediate lethal proximity prohibited", !ThirdFactionInterventionGuard.SafeSpawn(w, cfg, new Vector3(4, 1, 4)));
        Check("occupied spawn prohibited", !ThirdFactionInterventionGuard.SafeSpawn(w, cfg, new Vector3(3, 1, 3)));
        Check("remote spawn permitted", ThirdFactionInterventionGuard.SafeSpawn(w, cfg, new Vector3(20, 1, 20)));
        w.Player.ObjectiveHPRatio = .2f; ThirdFactionWorldAnalyzer.Analyze(w, cfg);
        Check("objective HP recognized as decisive", w.Decisive);
        w.Player.ObjectiveHPRatio = 1; w.ArmyDistance = 5; ThirdFactionWorldAnalyzer.Analyze(w, cfg);
        Check("natural impending war predicted", w.Prediction.Probability >= .7f && w.Prediction.EstimatedTurns <= 2);
        state.CurrentIP = 50; candidate.PlayerLoss = 0;
        Check("null transaction fails without spending", !ThirdFactionEventExecutor.Execute(candidate, ip, a => null) && ip.Current == 50);
        var broken = new FakeEvent(true);
        Check("failed execution rolls back and refunds", !ThirdFactionEventExecutor.Execute(candidate, ip, a => broken)
            && broken.RolledBack && broken.Disposed && ip.Current == 50);
        var success = new FakeEvent(false);
        Check("successful execution commits without refund", ThirdFactionEventExecutor.Execute(candidate, ip, a => success)
            && success.Disposed && !success.RolledBack && ip.Current == 40);
        Check("rollback and cleanup errors never strand IP or turn", !ThirdFactionEventExecutor.Execute(candidate, ip, a => new BrokenCleanupEvent())
            && ip.Current == 40);
        var path = new ThirdFactionPathPlanner();
        Check("blocked direct route detours with legal first step", path.TryNextStep(Kind.Knight, Vector3.zero, new Vector3(2, 0, 0), PathFixtureStep,
            64, 100, out var detour) && detour.x == 0 && Mathf.Abs(detour.z) == 1);
        Check("path budget exhaustion returns no movement", !path.TryNextStep(Kind.Knight, Vector3.zero, new Vector3(2, 0, 0), PathFixtureStep,
            64, 0, out var stopped) && stopped == Vector3.zero);
        Check("nonmoving kinds cannot invent a route", !path.TryNextStep(Kind.Crystal, Vector3.zero, new Vector3(2, 0, 0), PathFixtureStep,
            64, 100, out _));
        state.Objectives.Add(new ThirdFactionObjective { ActorId = 7, TargetCell = new Vector3Int(9, 1, 12), ExpireTurn = 25 });
        state.ActiveEvents.Add(new ThirdFactionActiveEvent { EventId = "saved", ExpireTurn = 26 });
        var loaded = JsonUtility.FromJson<ThirdFactionDirectorState>(JsonUtility.ToJson(state));
        Check("Director save round-trip", loaded.CurrentIP == state.CurrentIP && loaded.Objectives[0].TargetCell == state.Objectives[0].TargetCell
            && loaded.ActiveEvents[0].EventId == "saved" && loaded.LastMajorEventTurn == state.LastMajorEventTurn);
        for (int i = 0; i < 100; i++) Check("RNG preserved after loading " + i, state.NextRandom(1000) == loaded.NextRandom(1000));
        UnityEngine.Object.DestroyImmediate(cfg); UnityEngine.Object.DestroyImmediate(def);
    }
    sealed class FakeEvent : IThirdFactionPreparedEvent
    {
        readonly bool fail;
        public bool RolledBack, Disposed;
        public FakeEvent(bool value) { fail = value; }
        public void Commit() { if (fail) throw new Exception("injected event failure"); }
        public void Rollback() { RolledBack = true; }
        public void Dispose() { Disposed = true; }
    }
    sealed class BrokenCleanupEvent : IThirdFactionPreparedEvent
    {
        public void Commit() => throw new Exception("injected commit failure");
        public void Rollback() => throw new Exception("injected rollback failure");
        public void Dispose() => throw new Exception("injected cleanup failure");
    }
    static bool PathFixtureStep(Vector3 from, Vector2Int offset, out Vector3 cell, out float cost)
    {
        cell = from + new Vector3(offset.x, 0, offset.y); cost = 1;
        return cell.x >= -1 && cell.x <= 3 && Mathf.Abs(cell.z) <= 1 && !(cell.x == 1 && cell.z == 0);
    }

    public static Status SpawnFixture(GameSystems s)
    {
        var neutral = s.NeutralFactionSystem; var third = s.ThirdFactionSystem;
        var definition = ScriptableObject.CreateInstance<ThirdFactionEventDefinition>();
        definition.EventId = "fixture-director-intruder"; definition.SpawnTeam = Team.Intruder;
        definition.IpCost = 0; definition.CanActImmediately = false;
        var encounter = neutral.Catalog.Intruders.First();
        var candidate = new ThirdFactionEventCandidate { Definition = definition, Encounter = encounter };
        var world = third.LastSnapshot;
        var cell = s.MapCreate.SetPos.First(p => !s.MoveGenerator.IsOccupied(s.MoveGenerator.Cell(p))
            && !s.BuildSystem.HasBuildingAt(GridHelper.ToGrid(p)) && s.MapCreate.CanTraverse(p, p)
            && ThirdFactionInterventionGuard.SafeSpawn(world, third.Config, p));
        candidate.SpawnCells.Add(cell);
        int count = neutral.Capture().Count, ip = third.IP.Current, ap = s.APSystem.GetAP(Team.Monster);
        Check("real spawn transaction succeeds", ThirdFactionEventExecutor.Execute(candidate, third.IP, third.PrepareEvent));
        Check("event and AP separated", third.IP.Current == ip && s.APSystem.GetAP(Team.Monster) == ap);
        Check("only one prepared actor committed", neutral.Capture().Count == count + 1);
        var unit = neutral.UnitParent.GetComponentsInChildren<Status>().First(u => u.team == Team.Intruder && neutral.Origin(u).Id == encounter.Id);
        Check("event actor waits for response turn", !neutral.CanAct(unit, third.State.LastProcessedTurn));
        Check("event actor available next turn", neutral.CanAct(unit, third.State.LastProcessedTurn + 1));
        UnityEngine.Object.DestroyImmediate(definition);
        return unit;
    }
    public static void PlayMode(GameSystems s)
    {
        var third = s.ThirdFactionSystem; var saved = third.Capture();
        int playerAP = s.APSystem.GetAP(Team.Player), enemyAP = s.APSystem.GetAP(Team.Enemy);
        s.FactionState.SetAP(Team.Monster, 0); s.APSystem.ResetAP(Team.Monster);
        Check("third faction AP fully refills", s.APSystem.GetAP(Team.Monster) == s.APSystem.GetMaxAP(Team.Monster));
        Check("neutral AP reset never touches principal AP", s.APSystem.GetAP(Team.Player) == playerAP && s.APSystem.GetAP(Team.Enemy) == enemyAP);
        Check("unknown registry team never includes enemy actors", UnitRegistry.Instance.GetActiveUnits(Team.None).Count == 0);
        Check("neutral registry query respects faction", UnitRegistry.Instance.GetActiveUnits(Team.Monster).All(u => u.team == Team.Monster));
        var neutralActor = s.NeutralFactionSystem.UnitParent.GetComponentsInChildren<Status>().First(u => u.IsAlive && u.team == Team.Monster);
        var old = neutralActor.transform.position;
        var legal = s.MapCreate.SetPos.FirstOrDefault(p => p != old && MovePatterns.CanMove(neutralActor.kind, neutralActor.direction, p.x - old.x, p.z - old.z)
            && s.MapCreate.CanTraverse(old, p) && !s.MoveGenerator.IsOccupied(s.MoveGenerator.Cell(p)));
        int ipBeforeMove = third.IP.Current;
        if (legal != default)
        {
            s.APSystem.Consume(neutralActor.team, APSystem.ActionType.Move, neutralActor, old, legal);
            neutralActor.transform.position = legal;
            Check("normal legal move consumes only AP", third.IP.Current == ipBeforeMove
                && s.APSystem.GetAP(Team.Monster) < s.APSystem.GetMaxAP(Team.Monster)
                && s.APSystem.GetAP(Team.Enemy) == enemyAP);
            neutralActor.transform.position = old; neutralActor.Fatigue--;
        }
        s.MoveGenerator.UnitPointCore();
        var buffer = new List<Status>();
        var world = ThirdFactionWorldSnapshot.Capture(s, 30, 15, third.State, buffer);
        var noConfig = ScriptableObject.CreateInstance<ThirdFactionDirectorConfig>(); noConfig.DirectorBudgetMs = 20;
        var state = new ThirdFactionDirectorState { CurrentIP = 50 };
        world.ArmyDistance = 999; world.RecentBattles = 0; world.Player.ObjectiveHPRatio = world.Enemy.ObjectiveHPRatio = 1;
        var decision = new ThirdFactionDirector(noConfig).Decide(world, state, s, null);
        Check("full IP does not force event", decision.NoEvent && state.CurrentIP == 50);
        world.ArmyDistance = 5;
        decision = new ThirdFactionDirector(noConfig).Decide(world, state, s, null);
        Check("future conflict reserves even full IP", decision.NoEvent && decision.Reason == "reserve_for_predicted_conflict");
        world.Turn += 5;
        decision = new ThirdFactionDirector(noConfig).Decide(world, state, s, null);
        Check("unfulfilled prediction releases reservation mode", decision.Mode != ThirdFactionDirectorMode.Reserve);
        var tuning = UnityEngine.Object.Instantiate(third.Config);
        tuning.DirectorBudgetMs = 20; tuning.MaxSpawnCells = 64;
        tuning.Events = new[] { UnityEngine.Object.Instantiate(third.Config.Events[0]) };
        tuning.Events[0].MinThreatLevel = 1;
        var opportunity = new ThirdFactionWorldSnapshot { Turn = 50, ThreatLevel = 15, ArmyDistance = 999 };
        opportunity.Player.MilitaryPower = opportunity.Enemy.MilitaryPower = 1000;
        var repeatState = new ThirdFactionDirectorState { CurrentIP = 50 };
        var originalDecision = new ThirdFactionDirector(tuning).Decide(opportunity, repeatState, s, null);
        Check("safe opportunity can win over NoEvent", !originalDecision.NoEvent);
        var cloneState = JsonUtility.FromJson<ThirdFactionDirectorState>(JsonUtility.ToJson(repeatState));
        var again = new ThirdFactionDirector(tuning).Decide(opportunity, repeatState, s, null);
        var afterLoad = new ThirdFactionDirector(tuning).Decide(opportunity, cloneState, s, null);
        Check("candidate and spawn reproducible after load", again.Event?.Definition.EventId == afterLoad.Event?.Definition.EventId
            && again.Event.TargetCell == afterLoad.Event.TargetCell && again.Event.SpawnCells[0] == afterLoad.Event.SpawnCells[0]);
        repeatState.Events.Add(new ThirdFactionEventStamp { Id = "other", Category = tuning.Events[0].Category, Turn = 50, RepeatCount = 10 });
        Check("repeated category loses value", new ThirdFactionDirector(tuning).Decide(opportunity, repeatState, s, null).NoEvent);
        opportunity.RecentBattles = 4;
        Check("real decision preserves balanced active war", new ThirdFactionDirector(tuning).Decide(opportunity, new ThirdFactionDirectorState { CurrentIP = 50 }, s, null).Reason == "active_balanced_war");
        opportunity.RecentBattles = 0; opportunity.Enemy.ObjectiveHPRatio = .1f;
        Check("real decision protects decisive phase", new ThirdFactionDirector(tuning).Decide(opportunity, new ThirdFactionDirectorState { CurrentIP = 50 }, s, null).Reason == "decisive_phase_protected");
        opportunity.Enemy.ObjectiveHPRatio = 1; tuning.DirectorBudgetMs = 0;
        var expired = new ThirdFactionDirector(tuning).Decide(opportunity, new ThirdFactionDirectorState { CurrentIP = 50 }, s, null);
        Check("time budget fallback is NoEvent", expired.NoEvent && expired.Reason == "director_time_budget");
        TacticalSingleSkill(s, third);
        BossActorRoundTrip(s);
        NestedPrefabSpawn(s);
        UnityEngine.Object.DestroyImmediate(tuning.Events[0]); UnityEngine.Object.DestroyImmediate(tuning);
        third.Restore(saved);
        Check("live Director restores without IP/AP refill", third.IP.Current == saved.CurrentIP
            && s.APSystem.GetAP(Team.Monster) == saved.MonsterAP.Current);
        var one = third.Capture(); third.Restore(one); var two = third.Capture();
        Check("live save remains deterministic", JsonUtility.ToJson(one) == JsonUtility.ToJson(two));
        UnityEngine.Object.DestroyImmediate(noConfig);
    }
    static void TacticalSingleSkill(GameSystems s, ThirdFactionSystem third)
    {
        var holder = new GameObject("ThirdFactionSkillFixture"); holder.SetActive(false);
        var attackerObject = new GameObject("Attacker"); attackerObject.transform.SetParent(holder.transform);
        var defenderObject = new GameObject("Defender"); defenderObject.transform.SetParent(holder.transform);
        var attacker = attackerObject.AddComponent<Status>(); var defender = defenderObject.AddComponent<Status>();
        attacker.team = Team.Monster; defender.team = Team.Intruder;
        attacker.kind = defender.kind = Kind.Knight; attacker.type = defender.type = Type.Unit;
        attacker.HP = attacker.MaxHP = defender.HP = defender.MaxHP = 1000;
        attacker.ATK = 20; defender.DEF = 1; attacker.direction = Direction.N; attacker.AssignedSkillId = 1;
        try
        {
            bool found = false;
            foreach (var cell in s.MapCreate.SetPos)
            {
                if (!s.MapCreate.TryGetHeight(Mathf.RoundToInt(cell.x), Mathf.RoundToInt(cell.z) + 1, out float y)) continue;
                var next = new Vector3(cell.x, y, cell.z + 1);
                if (!s.MapCreate.CanTraverse(cell, next)) continue;
                attacker.transform.position = cell; defender.transform.position = next; found = true; break;
            }
            Check("single skill fixture has a legal terrain target", found);
            holder.SetActive(true);
            var tactical = new ThirdFactionTacticalAI(s, s.NeutralFactionSystem, third.Config);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            ((List<Status>)typeof(ThirdFactionTacticalAI).GetField("targets", flags).GetValue(tactical)).Add(defender);
            s.APSystem.ResetAP(Team.Monster);
            int beforeAP = s.APSystem.GetAP(Team.Monster), beforeIP = third.IP.Current;
            bool used = (bool)typeof(ThirdFactionTacticalAI).GetMethod("TrySkill", flags).Invoke(tactical, new object[] { attacker, 3 });
            Check("third faction evaluates and executes single-target skill", used && defender.HP < 1000 && attacker.SkillCooldown > 0);
            Check("third faction skill spends AP without IP", s.APSystem.GetAP(Team.Monster) == beforeAP - 4 && third.IP.Current == beforeIP);
            StatusEffectSystem.TickAllUnits(Team.Monster, holder.transform);
            Check("third faction skill cooldown ticks once at turn start", attacker.SkillCooldown == 0);
        }
        finally { UnityEngine.Object.DestroyImmediate(holder); s.MoveGenerator.UnitPointCore(); }
    }
    static void BossActorRoundTrip(GameSystems s)
    {
        var neutral = s.NeutralFactionSystem;
        var original = neutral.Capture();
        int previous = neutral.PreviousMonsterCount, round = neutral.LastRound;
        var intruders = neutral.SpawnedIntruders.ToList();
        var actor = neutral.UnitParent.GetComponentsInChildren<Status>().First(u => u.IsAlive && u.team == Team.Monster);
        int id = neutral.ActorId(actor);
        actor.isWildBoss = true; actor.wildBossArchetype = WildBossArchetype.RebelKnight;
        actor.wildBossTerritoryCenter = actor.GridPosition; actor.wildBossTerritoryRadius = 3;
        actor.wildBossAP = 7; actor.wildBossMaxAP = 20; actor.wildBossTurnCounter = 5; actor.wildBossAtkBuffTurns = 2;
        try
        {
            var records = neutral.Capture();
            var bossRecord = records.First(r => r.ActorId == id);
            Check("Director boss marker is included in neutral save", bossRecord.Boss != null && bossRecord.Boss.AP == 7);
            neutral.Restore(records, previous, round, intruders);
            var restored = neutral.UnitParent.GetComponentsInChildren<Status>().First(u => neutral.ActorId(u) == id);
            Check("Director boss attributes survive actual restore", restored.isWildBoss && restored.wildBossArchetype == WildBossArchetype.RebelKnight
                && restored.wildBossTerritoryRadius == 3 && restored.wildBossAP == 7 && restored.wildBossTurnCounter == 5 && restored.wildBossAtkBuffTurns == 2);
        }
        finally { neutral.Restore(original, previous, round, intruders); s.MoveGenerator.UnitPointCore(); }
    }
    static void NestedPrefabSpawn(GameSystems s)
    {
        var prefab = new GameObject("NestedActorFixture"); prefab.SetActive(false);
        var child = new GameObject("OffsetStatus"); child.transform.SetParent(prefab.transform);
        child.transform.localPosition = new Vector3(.75f, .5f, .25f); child.AddComponent<Status>();
        var staging = new GameObject("NestedActorStaging"); staging.SetActive(false);
        try
        {
            var data = new R1ContentCatalog.Encounter { Id = "nested-fixture", Prefab = prefab,
                Stats = s.NeutralFactionSystem.Catalog.Monsters.First().Stats };
            var position = s.MapCreate.SetPos[0];
            var status = s.NeutralFactionSystem.StageSpawn(data, Team.Monster, -1, position, staging.transform);
            Check("nested prefab Status stays on validated spawn cell", status != null && status.transform.position == position);
        }
        finally { UnityEngine.Object.DestroyImmediate(staging); UnityEngine.Object.DestroyImmediate(prefab); }
    }
}
#endif
