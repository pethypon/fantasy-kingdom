#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>R2 real board/Fog/save routes. No match progression, profile writes or project asset edits.</summary>
public static class ExplorationR2IntegrationTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int passed;
    static void Check(string label, bool result)
    {
        if (!result) throw new InvalidOperationException("[ExplorationR2Integration] FAIL " + label);
        passed++; Debug.Log("[ExplorationR2Integration] PASS " + label);
    }
    static AIBoardState Board(GameSystems live, EconomyR2Tests.Fixture f, VisionGenerator vision)
    {
        var board = new AIBoardState(live.MoveGenerator, live.AttackGenerator, f.AP, f.Units, f.Crystals,
            vision, f.Builder, null, f.State, null, 20, null, f.Team);
        board.MapCreate = live.MapCreate; board.ReconThreatLevel = 15;
        board.Governor = new AIStrategicGovernor(); board.Governor.Evaluate(board); return board;
    }
    static AICommander Commander(GameSystems live, EconomyR2Tests.Fixture f, TurnGenerator hub, VisionGenerator vision)
        => new AICommander(hub, live.MoveGenerator, live.AttackGenerator, live.BattleSystem, vision, f.AP,
            f.Units, f.Crystals, live.MapCreate, MajorPersonality.Combat, f.Builder, null, f.State,
            live.SkillSystem, null, 15, 51217, f.Team);
    public static void PlayMode(GameSystems systems)
    {
        passed = 0;
        foreach (var team in new[] { Team.Enemy, Team.Player }) TestFaction(systems, team);
        RealLargeMapMovement(systems);
        Debug.Log("[ExplorationR2Integration] " + passed + " observed board/persistence/safety checks passed");
    }
    static void TestFaction(GameSystems live, Team team)
    {
        using (var f = new EconomyR2Tests.Fixture(live, team))
        {
            var extra = new GameObject("R2 isolated vision and save hub"); extra.SetActive(false);
            var vision = extra.AddComponent<VisionGenerator>(); var hub = extra.AddComponent<TurnGenerator>(); hub.MarkDeveloperMatch();
            var heightsField = typeof(MapCreate).GetField("topY", Private); var heights = (int[,])heightsField.GetValue(live.MapCreate);
            Vector3 unknown = Vector3.zero; bool found = false; int oldHeight = 0;
            try
            {
                var cells = f.AdjacentCells(); var seen = new HashSet<Vector3Int>();
                foreach (var cell in cells) seen.Add(GridHelper.ToGridXZ(cell));
                foreach (string name in new[] { "_playerVisionBox", "_enemyVisionBox", "_playerExplored", "_enemyExplored" })
                    typeof(VisionGenerator).GetField(name, Private).SetValue(vision, new HashSet<Vector3Int>(name.StartsWith(team == Team.Player ? "_player" : "_enemy") ? seen : new HashSet<Vector3Int>()));
                var scout = f.Unit(); scout.kind = Kind.Scout; scout.transform.position = cells[0];
                foreach (var cell in live.MapCreate.SetPos)
                    if (!seen.Contains(GridHelper.ToGridXZ(cell)) && Mathf.Abs(cell.x - cells[0].x) + Mathf.Abs(cell.z - cells[0].z) > 10)
                    { unknown = cell; found = true; break; }
                Check(team + " fixture has real unobserved terrain", found);
                oldHeight = heights[(int)unknown.x, (int)unknown.z];
                var hidden = f.Opponent(unknown, 999); var board = Board(live, f, vision);
                var original = board.Exploration.GetObjective(scout.GetInstanceID());
                Check(team + " real board creates a persistent exploration objective", original != null && original.State == ObjectiveState.Active);
                Check(team + " current sight and territory stay separate from unknown", board.AlivePlayerUnits.Count == 0
                    && board.Exploration.Memory.Count == seen.Count && !board.Exploration.Memory.ContainsKey(GridHelper.ToGridXZ(unknown)));
                float before = board.Recon.ScoreMove(scout, cells[1]); Vector3Int target = original.TargetCell;
                hidden.transform.position = unknown + Vector3.forward; hidden.HP = 1; hidden.ATK = 9999;
                heights[(int)unknown.x, (int)unknown.z] = oldHeight == 2 ? 0 : 2;
                var changed = Board(live, f, vision); float after = changed.Recon.ScoreMove(scout, cells[1]);
                Check("Exploration_DoesNotUseHiddenEnemyPositions " + team, changed.AlivePlayerUnits.Count == 0
                    && Mathf.Abs(before - after) < .001f && changed.Exploration.GetObjective(scout.GetInstanceID()).TargetCell == target);
                Check(team + " hidden terrain change cannot alter frontier scoring or stored knowledge", changed.Exploration.Memory.Count == seen.Count
                    && !changed.Exploration.Memory.ContainsKey(GridHelper.ToGridXZ(unknown)));
                heights[(int)unknown.x, (int)unknown.z] = oldHeight;

                hub.Context.Turn = 20; hub.Systems.MapCreate = live.MapCreate; hub.Systems.CrystalSystem = f.Crystals;
                hub.Systems.UnitSetting = f.Units; hub.Systems.BuildSystem = f.Builder; hub.Systems.FactionState = f.State;
                var commander = Commander(live, f, hub, vision); hub.Systems.AICommander = commander;
                var governor = (AIStrategicGovernor)typeof(AICommander).GetField("_governor", Private).GetValue(commander);
                board.Governor = governor; governor.Evaluate(board); typeof(AICommander).GetField("_board", Private).SetValue(commander, board);
                int objectiveId = board.Exploration.GetObjective(scout.GetInstanceID()).ObjectiveId;
                var game = SaveSystem.CollectGameState(hub, f.State, null, vision, commander);
                game = JsonUtility.FromJson<SaveSystem.GameSaveData>(JsonUtility.ToJson(game));
                Check(team + " actual save collection includes frontier objectives and visits", game.AI.Exploration != null
                    && game.AI.Exploration.Objectives.Count > 0 && game.AI.Exploration.Cells.Count == seen.Count
                    && game.AI.Exploration.ActorTeam == team);
                var unitSave = SaveSystem.CaptureUnit(scout);
                scout.gameObject.SetActive(false); var loadedScout = f.Unit(); loadedScout.kind = Kind.Scout;
                SaveGameApplier.ApplyStatusFields(loadedScout, unitSave);
                var restoredCommander = Commander(live, f, hub, vision); SaveSystem.RestoreAICommander(game.AI, restoredCommander);
                var restoredBoard = Board(live, f, vision);
                restoredBoard.Governor = (AIStrategicGovernor)typeof(AICommander).GetField("_governor", Private).GetValue(restoredCommander);
                typeof(AICommander).GetField("_board", Private).SetValue(restoredCommander, restoredBoard);
                restoredBoard.Governor.Evaluate(restoredBoard);
                var restoredGoal = restoredBoard.Exploration.GetObjective(loadedScout.GetInstanceID());
                Check(team + " loaded life identity reconnects target to the new runtime instance", restoredGoal != null
                    && restoredGoal.ObjectiveId == objectiveId && restoredGoal.ActorLifeId == loadedScout.ReflectionLifeId
                    && restoredGoal.AssignedUnitId == loadedScout.GetInstanceID());
                Check(team + " reconstruction keeps the same frontier and recon target", restoredBoard.Recon.TryGetAssignedTarget(loadedScout, out var restoredTarget)
                    && GridHelper.MatchXZ(restoredTarget, target));
                f.Crystal.transform.position = cells[0]; f.SetOwnCrystalPosition(cells[0]); f.Crystal.HP = 1;
                hidden.transform.position = cells[2]; hidden.HP = 100; hidden.ATK = 999;
                restoredBoard.Refresh(); restoredBoard.Governor.Evaluate(restoredBoard);
                Check(team + " real governor emergency suspends exploration", restoredBoard.Governor.Mode == StrategicMode.EmergencyDefense
                    && restoredBoard.Exploration.GetObjective(loadedScout.GetInstanceID()).State == ObjectiveState.Suspended);
                hidden.gameObject.SetActive(false); f.Crystal.HP = f.Crystal.MaxHP;
                restoredBoard.Refresh(); restoredBoard.Governor.Evaluate(restoredBoard);
                Check(team + " valid exploration resumes after real emergency interruption", restoredBoard.Exploration.GetObjective(loadedScout.GetInstanceID()).State == ObjectiveState.Active);

                long processed = restoredBoard.Exploration.ObservationCellsProcessed, rebuilds = restoredBoard.Exploration.FrontierRegionRebuildCount;
                int ap = f.State.GetAP(team); var position = loadedScout.transform.position;
                var clock = Stopwatch.StartNew();
                for (int i = 0; i < 2000; i++) { restoredBoard.Exploration.Update(restoredBoard, false); restoredBoard.Recon.ScoreMove(loadedScout, cells[1]); }
                clock.Stop();
                Check(team + " repeated preview/update work uses cached observations", processed == restoredBoard.Exploration.ObservationCellsProcessed
                    && rebuilds == restoredBoard.Exploration.FrontierRegionRebuildCount && clock.Elapsed.TotalMilliseconds < 1000);
                Check(team + " inspection/scoring consumes no actions or live movement", f.State.GetAP(team) == ap && loadedScout.transform.position == position);
                Debug.Log($"[ExplorationR2IntegrationPerf] faction={team} previews=2000 known={seen.Count} ms={clock.Elapsed.TotalMilliseconds:F2} changedCells={processed} frontierRebuilds={rebuilds}");
            }
            finally
            {
                if (found) heights[(int)unknown.x, (int)unknown.z] = oldHeight;
                UnityEngine.Object.DestroyImmediate(extra);
            }
        }
    }

    static void RealLargeMapMovement(GameSystems live)
    {
        using (var f = new EconomyR2Tests.Fixture(live))
        {
            var root = new GameObject("R2 512-grid real movement fixture"); root.SetActive(false);
            var settings = ScriptableObject.CreateInstance<ExplorationAISettings>(); settings.EnableJsonlTelemetry = false;
            try
            {
                var map = root.AddComponent<MapCreate>(); map.maxX = map.maxZ = 512;
                map.SetPos = new List<Vector3>();
                var heights = new int[512, 512]; map.UseR1Terrain = true;
                typeof(MapCreate).GetField("topY", Private).SetValue(map, heights);
                var hub = root.AddComponent<TurnGenerator>(); hub.MarkDeveloperMatch();
                var moves = root.AddComponent<MoveGenerator>(); moves.mapcreate = map; moves.turnGenerator = hub;
                foreach (var pair in new[] { ("crystalsystem", (object)f.Crystals), ("unitsetting", (object)f.Units),
                    ("PlayerUnit", (object)f.Units.PlayerUnit), ("EnemyUnit", (object)f.Units.EnemyUnit) })
                    typeof(MoveGenerator).GetField(pair.Item1, Private).SetValue(moves, pair.Item2);
                f.Crystals.ECP = new Vector3(20, 1, 30); f.Crystals.PCP = new Vector3(511, 1, 511);
                f.Crystal.transform.position = f.Crystals.ECP;
                hub.Systems.MapCreate = map; hub.Systems.MoveGenerator = moves; hub.Systems.CrystalSystem = f.Crystals;
                hub.Systems.UnitSetting = f.Units; hub.Systems.FactionState = f.State; hub.Systems.APSystem = f.AP;
                var scout = f.Unit(); scout.kind = Kind.Scout; scout.transform.position = new Vector3(20, 1, 32);
                var observation = new ExplorationObservation { Width = 512, Depth = 512, Turn = 0, CellVersion = 1, ViewVersion = 1,
                    OwnBase = new Vector3Int(20, 0, 30), ActorTeam = Team.Enemy };
                for (int x = 0; x < 90; x++) for (int z = 0; z < 64; z++)
                {
                    bool wall = z == 31 && x <= 78;
                    var cell = new Vector3Int(x, wall ? 3 : 1, z);
                    heights[x, z] = wall ? 2 : 0;
                    if (!wall) map.SetPos.Add(cell);
                    observation.KnownCells.Add(new ExplorationObservedCell(cell, !wall, 0));
                }
                // This regression exercises the northern frontier reached around the known wall's eastern opening.
                observation.CanReach = (_, cell) => cell.x >= 79 && cell.z < 31;
                var planner = new AIExplorationPlanner(settings); planner.Update(observation, false);
                observation.Turn = 1; observation.Actors.Add(new ExplorationActor(scout.GetInstanceID(), GridHelper.ToGrid(scout.transform.position),
                    actorLifeId: scout.ReflectionLifeId)); planner.Update(observation, false);
                var initial = planner.GetObjective(scout.GetInstanceID());
                Check("real 512-grid unit receives a frontier more than fifty cells away", initial != null
                    && GridHelper.ChebyshevDistance(scout.transform.position, initial.TargetCell) > 50);
                Check("known detour wall blocks north while the northern frontier has a proved route", initial.TargetCell.z < 31
                    && !map.CanTraverse(scout.transform.position, scout.transform.position + Vector3.back * 2)
                    && initial.UsesKnownRouteDistance && planner.GetGoalProgress(scout.GetInstanceID(), new Vector3Int(21, 1, 32)) > 0);
                float initialDistance = initial.LastDistance;
                int id = initial.ObjectiveId, executions = 0;
                var executor = new AIActionExecutor(hub, moves, live.AttackGenerator, live.BattleSystem, f.AP,
                    live.SkillSystem, null, null, null, new AILearning(false), Team.Enemy);
                var clock = Stopwatch.StartNew();
                for (int turn = 2; turn <= 19; turn++)
                {
                    hub.Context.Turn = turn; f.State.ResetAPForTurn(f.Team); scout.Fatigue = 0; scout.HasMovedThisTurn = false;
                    observation.Turn = turn; var actor = observation.Actors[0]; actor.Cell = GridHelper.ToGrid(scout.transform.position);
                    observation.Actors[0] = actor; planner.Update(observation, false);
                    var board = new AIBoardState(moves, live.AttackGenerator, f.AP, f.Units, f.Crystals, null,
                        null, null, f.State, null, turn); board.MapCreate = map;
                    for (int step = 0; step < 3; step++)
                    {
                        // A corridor can advance the minor axis after the other distance component dominates.
                        var destination = scout.transform.position + Vector3.right;
                        var action = new AIAction { ActionType = AIActionType.Move, Unit = scout, TargetPos = destination };
                        int previousAP = f.State.GetAP(f.Team), cost = board.CalcMoveCost(scout, destination);
                        Check("real legal move succeeds while preserving the distant target T" + turn + "." + step,
                            executor.Execute(action, board) && scout.transform.position == destination && f.State.GetAP(f.Team) == previousAP - cost);
                        executions++; planner.RecordAction(scout.GetInstanceID(), GridHelper.ToGrid(destination), true);
                        actor.Cell = GridHelper.ToGrid(destination); observation.Actors[0] = actor; planner.Update(observation, false);
                    }
                    Check("real long-range movement retains its objective at turn " + turn,
                        planner.GetObjective(scout.GetInstanceID())?.ObjectiveId == id && planner.GetObjective(scout.GetInstanceID()).State == ObjectiveState.Active);
                }
                clock.Stop();
                Check("real exploration covers fifty-four cells over eighteen turns", executions == 54 && Mathf.RoundToInt(scout.transform.position.x) == 74
                    && planner.GetObjective(scout.GetInstanceID()).LastDistance < initialDistance);
                Check("large map legal execution stays bounded without generating every terrain object", clock.Elapsed.TotalMilliseconds < 2000
                    && root.GetComponentsInChildren<Renderer>(true).Length == 0);
                Debug.Log($"[ExplorationR2ActualMovement] grid=512x512 known={observation.KnownCells.Count} turns=18 legalMoves={executions} distanceMoved=54 ms={clock.Elapsed.TotalMilliseconds:F2}");
                // Open-flat selection is independent: remove the physical and observed wall before creating a new plan.
                map.SetPos.Clear(); observation.KnownCells.Clear(); observation.Actors.Clear(); observation.CanReach = null;
                observation.Turn = 0; observation.CellVersion++;
                // Keep the open board's own crystal off the scout's legal north/south route.
                f.Crystals.ECP = new Vector3(10, 1, 10); f.Crystal.transform.position = f.Crystals.ECP;
                observation.OwnBase = GridHelper.ToGridXZ(f.Crystals.ECP);
                for (int x = 0; x < 90; x++) for (int z = 0; z < 64; z++)
                {
                    heights[x, z] = 0; var cell = new Vector3Int(x, 1, z); map.SetPos.Add(cell);
                    observation.KnownCells.Add(new ExplorationObservedCell(cell, true, 0));
                }
                scout.transform.position = new Vector3(20, 1, 32); scout.HasMovedThisTurn = false; scout.Fatigue = 0; f.State.ResetAPForTurn(f.Team);
                var openPlanner = new AIExplorationPlanner(settings); openPlanner.Update(observation, false);
                observation.Turn = 1; observation.Actors.Add(new ExplorationActor(scout.GetInstanceID(), GridHelper.ToGrid(scout.transform.position), actorLifeId: scout.ReflectionLifeId));
                openPlanner.Update(observation, false);
                CheckCommanderDirectionAtPlateau(live, f, hub, moves, map, scout, openPlanner, openPlanner.GetObjective(scout.GetInstanceID()).TargetCell);
                RealCommanderSelectedMovement(live, f, hub, moves, map, scout, openPlanner.CaptureState());
                LocalMovementMasksAndCost(live, f, moves, map, scout);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(settings); }
        }
    }

    static void CheckCommanderDirectionAtPlateau(GameSystems live, EconomyR2Tests.Fixture f, TurnGenerator hub,
        MoveGenerator moves, MapCreate map, Status scout, AIExplorationPlanner source, Vector3Int target)
    {
        Vector3 originalPosition = scout.transform.position;
        int verticalDifference = Math.Abs(target.z - Mathf.RoundToInt(originalPosition.z));
        Check("direction fixture reaches a genuine diagonal distance plateau", verticalDifference > 0);
        try
        {
            scout.transform.position = new Vector3(target.x - verticalDifference, 1, originalPosition.z);
            hub.Context.Turn = 2;
            var commander = new AICommander(hub, moves, live.AttackGenerator, live.BattleSystem, null, f.AP,
                f.Units, f.Crystals, map, MajorPersonality.Combat, null, null, f.State, live.SkillSystem,
                null, 100, 71181, Team.Enemy);
            commander.RestoreExplorationState(source.CaptureState());
            var board = new AIBoardState(moves, live.AttackGenerator, f.AP, f.Units, f.Crystals, null,
                null, null, f.State, null, 2); board.MapCreate = map; board.ReconThreatLevel = 100;
            board.Governor = (AIStrategicGovernor)typeof(AICommander).GetField("_governor", Private).GetValue(commander);
            typeof(AICommander).GetField("_board", Private).SetValue(commander, board); board.Governor.Evaluate(board);
            var east = scout.transform.position + Vector3.right;
            var diagonal = east + Vector3.forward * Math.Sign(target.z - scout.transform.position.z);
            var north = scout.transform.position + Vector3.forward * 2 * Math.Sign(target.z - scout.transform.position.z);
            Check("distance-plateau candidates are real legal moves", MovePatterns.CanMove(scout, scout.direction, 1, 0)
                && MovePatterns.CanMove(scout, scout.direction, 1, diagonal.z - scout.transform.position.z)
                && map.CanTraverse(scout.transform.position, diagonal));
            float eastScore = board.Recon.ScoreMove(scout, east), diagonalScore = board.Recon.ScoreMove(scout, diagonal);
            Check("real R2 score favors a diagonal that shrinks the frontier distance", diagonalScore > eastScore
                && GridHelper.ChebyshevDistance(diagonal, target) < GridHelper.ChebyshevDistance(east, target));
            var candidates = new List<AIAction>();
            foreach (var destination in new[] { east, diagonal, north }) candidates.Add(new AIAction
            {
                ActionType = AIActionType.Move, Unit = scout, TargetPos = destination,
                APCost = board.CalcMoveCost(scout, destination), Score = board.Recon.ScoreMove(scout, destination), StrategicPriority = 4
            });
            board.Governor.Filter(candidates, board);
            var selected = (AIAction)typeof(AICommander).GetMethod("SelectBestAction", Private)
                .Invoke(commander, new object[] { candidates, new HashSet<string>(), null });
            foreach (var candidate in candidates)
                Debug.Log($"[ExplorationR2Plateau] from={scout.transform.position} target={target} candidate={candidate.TargetPos} priority={candidate.StrategicPriority} score={candidate.Score:F2} routeProgress={board.Exploration.GetGoalProgress(scout.GetInstanceID(), GridHelper.ToGrid(candidate.TargetPos)):F2} selected={ReferenceEquals(candidate, selected)}");
            // A legal two-cell north move can reduce proved-route length while Chebyshev distance remains flat.
            Check("real commander chooses a higher-valued proved-route advance", selected != null && selected.Score >= diagonalScore && selected.TargetPos != east
                && board.Exploration.GetGoalProgress(scout.GetInstanceID(), GridHelper.ToGrid(selected.TargetPos)) > 0);
        }
        finally { scout.transform.position = originalPosition; }
    }

    static void RealCommanderSelectedMovement(GameSystems live, EconomyR2Tests.Fixture f, TurnGenerator hub,
        MoveGenerator moves, MapCreate map, Status scout, AIExplorationState initialState)
    {
        scout.transform.position = new Vector3(20, 1, 32); scout.Fatigue = 0; scout.HasMovedThisTurn = false;
        var commander = new AICommander(hub, moves, live.AttackGenerator, live.BattleSystem, null, f.AP,
            f.Units, f.Crystals, map, MajorPersonality.Combat, null, null, f.State, live.SkillSystem,
            null, 100, 17171, Team.Enemy);
        commander.RestoreExplorationState(initialState);
        var governor = (AIStrategicGovernor)typeof(AICommander).GetField("_governor", Private).GetValue(commander);
        var select = typeof(AICommander).GetMethod("SelectBestAction", Private);
        var record = typeof(AICommander).GetMethod("RecordExplorationAction", Private);
        var executor = new AIActionExecutor(hub, moves, live.AttackGenerator, live.BattleSystem, f.AP,
            live.SkillSystem, null, null, null, new AILearning(false), Team.Enemy);
        int originalId = initialState.Objectives[0].ObjectiveId, executions = 0, diagonalSteps = 0;
        float initialDistance = initialState.Objectives[0].LastDistance, totalProvedProgress = 0;
        var clock = Stopwatch.StartNew();
        for (int turn = 2; turn <= 19; turn++)
        {
            hub.Context.Turn = turn; f.State.ResetAPForTurn(f.Team); scout.Fatigue = 0; scout.HasMovedThisTurn = false;
            var board = new AIBoardState(moves, live.AttackGenerator, f.AP, f.Units, f.Crystals, null,
                null, null, f.State, null, turn); board.MapCreate = map; board.ReconThreatLevel = 100; board.Governor = governor;
            typeof(AICommander).GetField("_board", Private).SetValue(commander, board); governor.Evaluate(board);
            for (int step = 0; step < 3; step++)
            {
                var candidates = new List<AIAction>();
                // Use the complete real movement mask, including Scout's legal two-cell vertical steps.
                foreach (var destination in board.GetValidMoves(scout))
                {
                    candidates.Add(new AIAction { ActionType = AIActionType.Move, Unit = scout, TargetPos = destination,
                        APCost = board.CalcMoveCost(scout, destination), Score = board.Recon.ScoreMove(scout, destination), StrategicPriority = 4 });
                }
                governor.Filter(candidates, board);
                var selected = (AIAction)select.Invoke(commander, new object[] { candidates, new HashSet<string>(), null });
                float progress = selected == null ? 0 : board.Exploration.GetGoalProgress(scout.GetInstanceID(), GridHelper.ToGrid(selected.TargetPos));
                Debug.Log($"[ExplorationR2CommanderStep] turn={turn}.{step} from={scout.transform.position} target={commander.Exploration.GetObjective(scout.GetInstanceID())?.TargetCell} selected={selected?.TargetPos} score={selected?.Score:F2} priority={selected?.StrategicPriority} routeProgress={progress:F2} legalCandidates={candidates.Count}");
                Check("commander selects a legal frontier-progress step T" + turn + "." + step, selected != null && progress > 0);
                var previous = scout.transform.position; int ap = f.State.GetAP(f.Team), cost = board.CalcMoveCost(scout, selected.TargetPos);
                bool success = executor.Execute(selected, board); board.Refresh(); record.Invoke(commander, new object[] { selected, success });
                Check("commander-selected step executes and consumes exact AP T" + turn + "." + step,
                    success && scout.transform.position == selected.TargetPos && f.State.GetAP(f.Team) == ap - cost);
                if (Mathf.Abs(scout.transform.position.x - previous.x) == 1 && Mathf.Abs(scout.transform.position.z - previous.z) == 1) diagonalSteps++;
                executions++; totalProvedProgress += progress;
            }
            var objective = commander.Exploration.GetObjective(scout.GetInstanceID());
            Check("commander-selected travel keeps the same frontier at turn " + turn, objective != null && objective.ObjectiveId == originalId
                && objective.State == ObjectiveState.Active);
        }
        clock.Stop();
        Check("commander R2 route makes fifty-four real moves with proved progress", executions == 54 && totalProvedProgress > 0
            && commander.Exploration.GetObjective(scout.GetInstanceID()).LastDistance < initialDistance);
        Check("open large-map commander selection stays bounded without terrain renderers", clock.Elapsed.TotalMilliseconds < 2000
            && map.GetComponentsInChildren<Renderer>(true).Length == 0);
        Debug.Log($"[ExplorationR2CommanderMovement] grid=512x512 turns=18 legalMoves={executions} diagonalMoves={diagonalSteps} provedProgress={totalProvedProgress:F1} initialDistance={initialDistance:F1} finalDistance={commander.Exploration.GetObjective(scout.GetInstanceID()).LastDistance:F1} ms={clock.Elapsed.TotalMilliseconds:F2}");
    }

    static List<Vector3> LegacyMoveScan(AIBoardState board, MoveGenerator moves, Status unit)
    {
        var result = new List<Vector3>(); var from = unit.transform.position;
        foreach (var cell in moves.mapcreate.SetPos)
        {
            if (!MovePatterns.CanMove(unit, unit.direction, cell.x - from.x, cell.z - from.z) || !moves.mapcreate.CanTraverse(from, cell)) continue;
            bool occupied = GridHelper.MatchXZ(cell, GridHelper.ToGrid(board.EnemyCrystalPos))
                || board.CanUsePlayerCrystalAsTarget() && GridHelper.MatchXZ(cell, GridHelper.ToGrid(board.PlayerCrystalPos));
            foreach (var ally in board.AliveEnemyUnits) if (GridHelper.MatchXZ(cell, ally.GridPosition)) occupied = true;
            foreach (var foe in board.AlivePlayerUnits) if (GridHelper.MatchXZ(cell, foe.GridPosition)) occupied = true;
            if (!occupied) result.Add(cell);
        }
        result.Sort((a, b) => { int x = a.x.CompareTo(b.x); return x != 0 ? x : a.z.CompareTo(b.z); }); return result;
    }
    static bool SameMoves(List<Vector3> actual, List<Vector3> expected)
    {
        if (actual.Count != expected.Count) return false;
        for (int i = 0; i < actual.Count; i++) if (actual[i] != expected[i]) return false;
        return true;
    }
    static void LocalMovementMasksAndCost(GameSystems live, EconomyR2Tests.Fixture f, MoveGenerator moves, MapCreate map, Status unit)
    {
        var profile = ScriptableObject.CreateInstance<BoardActionProfile>();
        var heights = (int[,])typeof(MapCreate).GetField("topY", Private).GetValue(map);
        var riverField = typeof(MapCreate).GetField("rivers", Private); object previousRiver = riverField.GetValue(map);
        var rivers = new bool[512, 512]; riverField.SetValue(map, rivers);
        var previousPositions = map.SetPos; Vector3 previousPosition = unit.transform.position;
        var previousKind = unit.kind; var previousDirection = unit.direction; var previousProfile = unit.GrowthData.actionProfile;
        try
        {
            map.SetPos = new List<Vector3>(65536);
            for (int x = 0; x < 256; x++) for (int z = 0; z < 256; z++) map.SetPos.Add(new Vector3(x, 1, z));
            unit.transform.position = new Vector3(128, 1, 128); unit.GrowthData.actionProfile = null;
            heights[128, 129] = 2; rivers[127, 128] = true;
            var ally = f.Unit(); ally.transform.position = new Vector3(129, 1, 128);
            var foe = f.Opponent(new Vector3(128, 1, 127), 1);
            var board = new AIBoardState(moves, live.AttackGenerator, f.AP, f.Units, f.Crystals, null, null, null, f.State);
            board.MapCreate = map; board.AlivePlayerUnits.Add(foe);
            foreach (var kind in MovePatterns.Map.Keys)
            foreach (var direction in new[] { Direction.N, Direction.S })
            {
                unit.kind = kind; unit.direction = direction;
                Check("local offsets preserve legacy legal candidates " + kind + "/" + direction,
                    SameMoves(board.GetValidMoves(unit), LegacyMoveScan(board, moves, unit)));
            }
            unit.kind = Kind.Scout; profile.movement.useCustom = true;
            profile.movement.SetCell(7, 7, true); profile.movement.SetCell(1, 2, true); profile.movement.SetCell(4, 3, true);
            unit.GrowthData.actionProfile = profile;
            foreach (bool independent in new[] { false, true })
            foreach (var direction in new[] { Direction.N, Direction.S })
            {
                profile.movement.directionIndependent = independent; unit.direction = direction;
                Check("local offsets preserve authored mask " + independent + "/" + direction,
                    SameMoves(board.GetValidMoves(unit), LegacyMoveScan(board, moves, unit)));
            }
            profile.actionType = ActorActionType.Stationary;
            Check("stationary authored actors have no movement candidates", board.GetValidMoves(unit).Count == 0 && LegacyMoveScan(board, moves, unit).Count == 0);
            unit.GrowthData.actionProfile = null; unit.kind = Kind.Scout; unit.direction = Direction.N;
            var expected = board.GetValidMoves(unit);
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < 4000; i++) board.GetValidMoves(unit);
            clock.Stop();
            Check("512-grid movement queries stay local with sixty-five-thousand terrain entries", clock.Elapsed.TotalMilliseconds < 1500
                && SameMoves(board.GetValidMoves(unit), expected));
            var largeMs = clock.Elapsed.TotalMilliseconds;
            map.SetPos = new List<Vector3>();
            for (int x = 123; x <= 133; x++) for (int z = 123; z <= 133; z++) map.SetPos.Add(new Vector3(x, 1, z));
            Check("local movement result is independent of far-away map-list entries", SameMoves(board.GetValidMoves(unit), expected));
            clock.Restart(); for (int i = 0; i < 4000; i++) board.GetValidMoves(unit); clock.Stop();
            Debug.Log($"[ExplorationR2LocalMovementPerf] grid=512x512 queries=4000 offsets={MovePatterns.Offsets(unit).Count} largeMapEntries=65536 largeMs={largeMs:F2} localMapEntries=121 localMs={clock.Elapsed.TotalMilliseconds:F2}");
        }
        finally
        {
            heights[128, 129] = 0; riverField.SetValue(map, previousRiver); map.SetPos = previousPositions;
            unit.transform.position = previousPosition; unit.kind = previousKind; unit.direction = previousDirection;
            unit.GrowthData.actionProfile = previousProfile; UnityEngine.Object.DestroyImmediate(profile);
        }
    }
}
#endif
