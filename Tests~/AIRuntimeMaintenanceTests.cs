#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Tests only reference lifetimes and the established history horizon; never changes search parameters.</summary>
public static class AIRuntimeMaintenanceTests
{
    static int passed;
    static void Check(string name, bool ok)
    {
        if (!ok) throw new InvalidOperationException("[AIRuntimeMaintenance] FAIL " + name);
        passed++; Debug.Log("[AIRuntimeMaintenance] PASS " + name);
    }

    public static void PlayMode(GameSystems live)
    {
        passed = 0;
        foreach (var team in new[] { Team.Enemy, Team.Player })
        using (var f = new EconomyR2Tests.Fixture(live, team))
        {
            var keeper = new AIRuntimeMaintenance();
            var history = new Dictionary<Status, List<Vector3Int>>();
            var actor = f.Unit(); var parent = team == Team.Enemy ? f.Units.EnemyUnit : f.Units.PlayerUnit;
            for (int i = 0; i < 100; i++) AIRuntimeMaintenance.RecordPosition(history, actor, new Vector3Int(i, 0, i));
            Check("four-position horizon remains the exact latest four moves " + team,
                history[actor].Count == 4 && history[actor][0].x == 96 && history[actor][3].x == 99);
            Check("live own unit history remains " + team,
                keeper.PrunePositionHistory(history, team, parent) == 0 && history.Count == 1);

            var killed = f.Unit(); var dormant = f.Unit(); var departed = f.Unit(); var transferred = f.Unit();
            AIRuntimeMaintenance.RecordPosition(history, killed, Vector3Int.one);
            AIRuntimeMaintenance.RecordPosition(history, dormant, Vector3Int.one);
            AIRuntimeMaintenance.RecordPosition(history, departed, Vector3Int.one);
            AIRuntimeMaintenance.RecordPosition(history, transferred, Vector3Int.one);
            killed.HP = 0; dormant.gameObject.SetActive(false); departed.transform.SetParent(parent.parent, true);
            transferred.team = team == Team.Enemy ? Team.Player : Team.Enemy;
            Check("death, inactivity, battle departure, and faction transfer are removed " + team,
                keeper.PrunePositionHistory(history, team, parent) == 4 && history.Count == 1 && history.ContainsKey(actor));
            Check("cleanup is idempotent " + team, keeper.PrunePositionHistory(history, team, parent) == 0);

            var destroyed = f.Unit(); AIRuntimeMaintenance.RecordPosition(history, destroyed, Vector3Int.zero);
            UnityEngine.Object.DestroyImmediate(destroyed.gameObject);
            Check("destroyed Unity key is removed safely " + team,
                keeper.PrunePositionHistory(history, team, parent) == 1 && history.Count == 1);
            var buffer = (List<Status>)typeof(AIRuntimeMaintenance).GetField("removedUnits", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keeper);
            Check("scratch does not retain dead Unity references " + team, buffer.Count == 0);

            var oldMemory = AIBoardState.CreateSharedMemory();
            oldMemory[18323] = new AIBoardState.LastKnownInfo { Valid = true, Turn = -100, Kind = Kind.Assassin, Type = Type.Unit };
            oldMemory[int.MinValue] = new AIBoardState.LastKnownInfo { Valid = true, Turn = -100, Kind = Kind.Crystal, Type = Type.Building };
            keeper.PrunePositionHistory(history, team, parent); keeper.Clear();
            Check("own-history maintenance neither reads nor expires shared Fog memory " + team,
                oldMemory.Count == 2 && oldMemory[18323].Valid && oldMemory[int.MinValue].Valid);
            var eventFields = typeof(AIRuntimeMaintenance).GetEvents(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Check("maintenance adds no scene or battle event subscription " + team, eventFields.Length == 0);

            var hubObject = new GameObject("Runtime maintenance isolated turn"); hubObject.SetActive(false);
            var hub = hubObject.AddComponent<TurnGenerator>(); hub.MarkDeveloperMatch();
            try
            {
                var commander = new AICommander(hub, live.MoveGenerator, live.AttackGenerator, live.BattleSystem,
                    live.VisionGenerator, f.AP, f.Units, f.Crystals, live.MapCreate, MajorPersonality.Combat,
                    f.Builder, null, f.State, live.SkillSystem, null, 15, 8173, team);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var tracked = (Dictionary<Status, List<Vector3Int>>)typeof(AICommander).GetField("_unitPositionHistory", flags).GetValue(commander);
                var shared = (Dictionary<int, AIBoardState.LastKnownInfo>)typeof(AICommander).GetField("_sharedMemory", flags).GetValue(commander);
                tracked[actor] = new List<Vector3Int> { Vector3Int.one };
                shared[18323] = oldMemory[18323];
                int configuredInterval = AIPerformanceConfig.Active.MaintenanceIntervalOwnTurns;
                try
                {
                    AIPerformanceConfig.Active.MaintenanceIntervalOwnTurns = 5;
                    actor.HP = 0;
                    var boundary = typeof(AICommander).GetMethod("MaintainRuntimeHistory", flags);
                    for (int ownTurn = 1; ownTurn < 5; ownTurn++) boundary.Invoke(commander, new object[] { ownTurn });
                    Check("own-turn maintenance does not scan on every turn " + team, tracked.Count == 1);
                    boundary.Invoke(commander, new object[] { 5 });
                    Check("fifth own-turn boundary releases dead history " + team, tracked.Count == 0);
                    var counts = commander.GetRuntimeCounts();
                    Check("retention snapshot neither initializes learning nor expires shared memory " + team,
                        counts.PositionHistoryUnits == 0 && counts.SharedMemory == 1 && counts.ReflectionActions == 0
                        && typeof(AICommander).GetField("reflection", flags).GetValue(commander) == null
                        && commander.Operations == null && shared.ContainsKey(18323));
                    actor.HP = 10; tracked[actor] = new List<Vector3Int> { Vector3Int.zero };
                    commander.ReleaseRuntimeReferences();
                    Check("battle cleanup releases own histories without clearing enemy observation memory " + team,
                        tracked.Count == 0 && shared.ContainsKey(18323));
                }
                finally { AIPerformanceConfig.Active.MaintenanceIntervalOwnTurns = configuredInterval; }
            }
            finally { UnityEngine.Object.DestroyImmediate(hubObject); }
            UnityEngine.Object.DestroyImmediate(departed.gameObject);
        }
        AttackCandidateFastPaths(live);
        MovementParity(live);
        using (var fixture = new EconomyR2Tests.Fixture(live))
        {
            var history = new Dictionary<Status, List<Vector3Int>>(); var maintenance = new AIRuntimeMaintenance();
            var survivor = fixture.Unit(); AIRuntimeMaintenance.RecordPosition(history, survivor, Vector3Int.zero);
            for (int i = 0; i < 200; i++)
            { var dead = fixture.Unit(); AIRuntimeMaintenance.RecordPosition(history, dead, Vector3Int.zero); dead.HP = 0; }
            Check("two hundred retired actor keys are released in one maintenance boundary",
                maintenance.PrunePositionHistory(history, Team.Enemy, fixture.Units.EnemyUnit) == 200 && history.Count == 1 && history.ContainsKey(survivor));
        }
        Debug.Log("[AIRuntimeMaintenance] " + passed + " lifetime/horizon checks passed");
    }

    // Retains the former query exactly as a regression oracle; production still uses that path
    // whenever any legitimate observed target source is populated.
    static List<Status> LegacyAttackTargets(GameSystems live, AIBoardState board, Status unit)
    {
        var targets = new List<Status>();
        live.AttackGenerator.NormalAttackPData(unit, unit.transform.position);
        if (live.AttackGenerator.AttackP == null) return targets;
        foreach (var position in live.AttackGenerator.AttackP)
        {
            var cell = live.MoveGenerator.Cell(position);
            foreach (var target in board.AlivePlayerUnits)
            {
                if (target == null || !target.gameObject.activeInHierarchy) continue;
                if (live.MoveGenerator.Cell(target.transform.position) == cell) { targets.Add(target); break; }
            }
            if (board.PlayerCrystalVisible && live.MoveGenerator.Cell(board.PlayerCrystalPos) == cell)
            {
                var buffer = new List<Status>();
                if (board.OpponentCrystalParent != null) board.OpponentCrystalParent.GetComponentsInChildren(true, buffer);
                foreach (var crystal in buffer)
                    if (crystal.kind == Kind.Crystal) { if (crystal.HP > 0) targets.Add(crystal); break; }
            }
        }
        live.AttackGenerator.AtkpDestroy();
        return targets;
    }

    static bool SameTargets(List<Status> a, List<Status> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
        return true;
    }

    static List<Vector3> LegacyMoves(GameSystems live, AIBoardState board, Status unit)
    {
        var origin = unit.transform.position; var result = new List<Vector3>();
        var offsets = MovePatterns.Offsets(unit);
        int direction = MovePatterns.IsDirectionIndependent(unit) ? 1 : MovePatterns.DirZ(unit.direction);
        foreach (var offset in offsets)
        {
            int x = Mathf.RoundToInt(origin.x) + offset.x, z = Mathf.RoundToInt(origin.z) + offset.y * direction;
            if (!live.MoveGenerator.mapcreate.TryGetHeight(x, z, out float height)) continue;
            var position = new Vector3(x, height, z);
            if (!MovePatterns.CanMove(unit, unit.direction, position.x - origin.x, position.z - origin.z)
                || !live.MoveGenerator.mapcreate.CanTraverse(origin, position)) continue;
            bool occupied = GridHelper.MatchXZ(position, GridHelper.ToGrid(board.EnemyCrystalPos))
                || board.CanUsePlayerCrystalAsTarget() && GridHelper.MatchXZ(position, GridHelper.ToGrid(board.PlayerCrystalPos));
            foreach (var ally in board.AliveEnemyUnits) if (GridHelper.MatchXZ(position, ally.GridPosition)) occupied = true;
            foreach (var enemy in board.AlivePlayerUnits) if (GridHelper.MatchXZ(position, enemy.GridPosition)) occupied = true;
            if (!occupied) result.Add(position);
        }
        result.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.z.CompareTo(b.z)); return result;
    }
    static void MovementParity(GameSystems live)
    {
        foreach (var team in new[] { Team.Enemy, Team.Player })
        using (var fixture = new EconomyR2Tests.Fixture(live, team))
        {
            var cells = fixture.AdjacentCells(); var actor = fixture.Unit(); actor.transform.position = cells[0];
            var ally = fixture.Unit(); ally.transform.position = cells[1]; fixture.Opponent(cells[2], 10);
            var profile = ScriptableObject.CreateInstance<BoardActionProfile>();
            try
            {
                profile.movement.useCustom = true;
                for (int x = 0; x < 8; x++) for (int z = 0; z < 8; z++) profile.movement.SetCell(x, z, (x + z) % 2 == 0);
                foreach (var kind in new[] { Kind.Scout, Kind.Knight, Kind.Archer })
                foreach (var direction in new[] { Direction.N, Direction.S })
                foreach (bool custom in new[] { false, true })
                {
                    actor.kind = actor.GrowthData.kind = kind; actor.direction = direction;
                    actor.GrowthData.actionProfile = custom ? profile : null;
                    var board = fixture.Board(); var old = LegacyMoves(live, board, actor); var actual = board.GetValidMoves(actor);
                    bool equal = old.Count == actual.Count;
                    for (int i = 0; equal && i < old.Count; i++) equal &= old[i] == actual[i];
                    Check("move candidates and order remain exact for " + team + "/" + kind + "/" + direction + "/custom=" + custom, equal);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }
    }

    static void AttackCandidateFastPaths(GameSystems live)
    {
        foreach (var team in new[] { Team.Enemy, Team.Player })
        using (var f = new EconomyR2Tests.Fixture(live, team))
        {
            var extra = new GameObject("Attack candidates isolated visibility"); extra.SetActive(false);
            var neutral = new GameObject("Attack candidates isolated intruders");
            var profile = ScriptableObject.CreateInstance<BoardActionProfile>();
            var previousNeutral = live.MoveGenerator.NeutralParent;
            var previousObstacles = live.MoveGenerator.ObstacleParent;
            var previousTarget = live.AttackGenerator.TargetUnit;
            var previousPositions = live.AttackGenerator.AttackP;
            var previousOrigin = live.AttackGenerator.objp;
            try
            {
                live.MoveGenerator.NeutralParent = neutral.transform; live.MoveGenerator.ObstacleParent = null;
                var vision = extra.AddComponent<VisionGenerator>();
                var cells = f.AdjacentCells(); var actor = f.Unit(); var sentinel = f.Unit();
                actor.transform.position = cells[0]; actor.AssignedSkillId = 1;
                sentinel.transform.position = cells[0] + Vector3.back;
                profile.attack.useCustom = true; profile.attack.directionIndependent = true;
                profile.attack.SetCell(4, 3, true); actor.GrowthData.actionProfile = profile;
                void Seen(bool visible)
                {
                    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    string prefix = team == Team.Player ? "_player" : "_enemy";
                    var seen = new HashSet<Vector3Int>();
                    if (visible) foreach (var cell in cells) seen.Add(GridHelper.ToGridXZ(cell));
                    typeof(VisionGenerator).GetField(prefix + "VisionBox", flags).SetValue(vision, seen);
                    typeof(VisionGenerator).GetField(prefix + "Explored", flags).SetValue(vision, new HashSet<Vector3Int>(seen));
                }
                Seen(false);
                var board = new AIBoardState(live.MoveGenerator, live.AttackGenerator, f.AP, f.Units,
                    f.Crystals, vision, f.Builder, null, f.State, null, 20, null, team);
                board.MapCreate = live.MapCreate;
                Check("empty hostile inputs match the original attack query " + team,
                    SameTargets(LegacyAttackTargets(live, board, actor), board.GetAttackTargets(actor)));
                live.AttackGenerator.TargetUnit = sentinel;
                var empty = board.GetAttackTargets(actor); empty.Add(sentinel);
                Check("empty fast path skips full-map AttackGenerator query and returns caller-owned storage " + team,
                    live.AttackGenerator.TargetUnit == sentinel && board.GetAttackTargets(actor).Count == 0);
                var enemySkill = new SkillData { Target = SkillTarget.EnemySingle };
                Check("single hostile skill skips attack map when no observed hostile exists " + team,
                    board.GetSkillTargets(actor, enemySkill).Count == 0 && live.AttackGenerator.TargetUnit == sentinel);
                Check("self and ally skill targets remain available without observed enemies " + team,
                    board.GetSkillTargets(actor, new SkillData { Target = SkillTarget.Self }).Contains(actor)
                    && board.GetSkillTargets(actor, new SkillData { Target = SkillTarget.AllySingle }).Contains(sentinel));
                live.AttackGenerator.TargetUnit = sentinel;
                board.GetSkillTargets(actor, new SkillData { Target = SkillTarget.DesignatedTile, Area = SkillAreaShape.Single });
                Check("ground area skill retains the previous query path " + team, live.AttackGenerator.TargetUnit == actor);

                var hostile = f.Opponent(cells[1], 10); board.Refresh();
                Check("hidden current enemy cannot suppress the empty optimization or become an attack target " + team,
                    board.AlivePlayerUnits.Count == 0 && SameTargets(LegacyAttackTargets(live, board, actor), board.GetAttackTargets(actor)));
                Seen(true); board.Refresh();
                var original = LegacyAttackTargets(live, board, actor); var actual = board.GetAttackTargets(actor);
                Check("visible hostile preserves exact nonempty target order " + team,
                    actual.Contains(hostile) && SameTargets(original, actual));

                hostile.team = Team.Intruder; hostile.transform.SetParent(neutral.transform); board.Refresh();
                original = LegacyAttackTargets(live, board, actor); actual = board.GetAttackTargets(actor);
                Check("visible third faction retains the real observed-target fallback " + team,
                    board.AlivePlayerUnits.Contains(hostile) && actual.Contains(hostile) && SameTargets(original, actual));

                hostile.team = board.OpponentTeam; hostile.type = Type.Building; hostile.kind = Kind.Scout;
                hostile.transform.SetParent(f.Builder.GetBuildingParent(board.OpponentTeam)); board.Refresh();
                original = LegacyAttackTargets(live, board, actor); actual = board.GetAttackTargets(actor);
                Check("visible opposing facility retains the real observed-target fallback " + team,
                    board.AlivePlayerUnits.Contains(hostile) && actual.Contains(hostile) && SameTargets(original, actual));

                hostile.kind = Kind.Crystal; hostile.transform.SetParent(board.OpponentCrystalParent);
                if (team == Team.Enemy) f.Crystals.PCP = cells[1]; else f.Crystals.ECP = cells[1];
                board.Refresh(); original = LegacyAttackTargets(live, board, actor); actual = board.GetAttackTargets(actor);
                Check("visible living crystal keeps fallback when no observed unit exists " + team,
                    board.AlivePlayerUnits.Count == 0 && board.PlayerCrystalVisible && board.PlayerCrystalHP > 0
                    && actual.Contains(hostile) && SameTargets(original, actual));

                hostile.HP = 0; board.Refresh(); live.AttackGenerator.TargetUnit = sentinel;
                actual = board.GetAttackTargets(actor);
                Check("confirmed zero-HP crystal cannot create a target and skips full-map work " + team,
                    actual.Count == 0 && live.AttackGenerator.TargetUnit == sentinel);

                // The supported byte counter is calibrated by the parent performance suite. An unavailable
                // byte counter returns -1; it is never reported as zero allocations.
                const int repeats = 50;
                var watch = new System.Diagnostics.Stopwatch();
                AIPerformanceCounters.BeginAllocationSlice(team); watch.Start();
                for (int i = 0; i < repeats; i++) LegacyAttackTargets(live, board, actor);
                watch.Stop(); double oldMs = watch.Elapsed.TotalMilliseconds;
                long oldBytes = AIPerformanceCounters.EndAllocationSlice(team);
                AIPerformanceCounters.BeginAllocationSlice(team); watch.Restart();
                for (int i = 0; i < repeats; i++) board.GetAttackTargets(actor);
                watch.Stop(); double newMs = watch.Elapsed.TotalMilliseconds;
                long newBytes = AIPerformanceCounters.EndAllocationSlice(team);
                if (oldBytes >= 0 && newBytes >= 0)
                    Check("empty optimization does not increase supported measured allocations " + team, newBytes <= oldBytes);
                Debug.Log($"[AIAttackFastPath] team={team} queries={repeats} beforeMs={oldMs:F4} afterMs={newMs:F4} beforeBytes={oldBytes} afterBytes={newBytes}");
            }
            finally
            {
                live.MoveGenerator.NeutralParent = previousNeutral; live.MoveGenerator.ObstacleParent = previousObstacles;
                live.AttackGenerator.TargetUnit = previousTarget; live.AttackGenerator.AttackP = previousPositions; live.AttackGenerator.objp = previousOrigin;
                UnityEngine.Object.DestroyImmediate(profile); UnityEngine.Object.DestroyImmediate(extra); UnityEngine.Object.DestroyImmediate(neutral);
            }
        }
    }
}
#endif
