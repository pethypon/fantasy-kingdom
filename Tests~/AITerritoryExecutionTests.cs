#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>Executed commander-selected territory approaches on unchanged real map geometry.</summary>
public static class AITerritoryExecutionTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly FieldInfo CommanderBoard = typeof(AICommander).GetField("_board", Hidden);
    static readonly FieldInfo CommanderStrategy = typeof(AICommander).GetField("_currentStrategy", Hidden);
    static readonly MethodInfo Select = typeof(AICommander).GetMethod("SelectBestAction", Hidden);
    static readonly MethodInfo ExecuteReflected = typeof(AICommander).GetMethod("ExecuteReflectedAction", Hidden);
    static int checks;

    [Serializable] sealed class Report
    {
        public string Method = "Initialized native Play Mode; both faction perspectives; isolated sustainable faction systems, scripted current/explored sight sets and original map geometry; complete production candidate generation/evaluation, real commander selection and real AIActionExecutor movement/AP consumption. Final approach uses AICommander.ExecuteReflectedAction with enabled isolated reflection to test completion ordering.";
        public string Limits = "Controlled four-own-turn sequence, three actual moves per faction, with one observed economic interruption. Sight sets, turn-start AP/fatigue reset and a temporary post-completion vacancy are fixture inputs; this is not complete commander turn execution, dynamic Fog validation, a full match, or a victory claim. No terrain is edited and no hidden hostile actor or crystal is an AI target.";
        public List<Step> Steps = new List<Step>();
    }
    [Serializable] sealed class Step
    {
        public string Team, Phase, Governor, Economy, Strategy, StrategyReason, TerritoryReason, Action, Rejection;
        public int Turn, CandidateCount, VisibleOpponentTerritory, RememberedCells, APBefore, APCost, APAfter, Priority, LastProgressTurn;
        public Vector3 Before, After, Destination, Waypoint;
        public float GoalDistanceBefore, GoalDistanceAfter, RouteProgress;
        public bool Suspended, Legal, Executed, NoPrincipalContact, NoCrystalKnowledge, NoScout;
    }
    static void Check(string label, bool result)
    {
        if (!result) throw new InvalidOperationException("[AITerritoryExecution] FAIL " + label);
        checks++; Debug.Log("[AITerritoryExecution] PASS " + label);
    }

    public static void Run(GameSystems systems, TurnGenerator turn)
    {
        if (!Application.isPlaying || systems?.MapCreate == null || turn == null || turn.IsGameOver)
            throw new InvalidOperationException("A real initialized unfinished Play Mode match is required.");
        checks = 0;
        var report = new Report();
        var random = UnityEngine.Random.state;
        int liveTurn = turn.Context.Turn;
        string playerNation = JsonUtility.ToJson(systems.FactionState.PlayerNation), enemyNation = JsonUtility.ToJson(systems.FactionState.EnemyNation);
        using (BoardActorAnimation.SuppressEffects())
            foreach (var team in new[] { Team.Player, Team.Enemy })
            {
                ExecuteApproach(systems, team, report);
                ReconnaissanceRequest(systems, team, false);
                ReconnaissanceRequest(systems, team, true);
            }
        Check("live nations/AP/resources and live turn are unchanged", liveTurn == turn.Context.Turn
            && playerNation == JsonUtility.ToJson(systems.FactionState.PlayerNation)
            && enemyNation == JsonUtility.ToJson(systems.FactionState.EnemyNation));
        Check("gameplay random state is unchanged", random.Equals(UnityEngine.Random.state));
        string output = Environment.GetEnvironmentVariable("FK_TERRITORY_OUTPUT");
        if (!string.IsNullOrEmpty(output))
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "territory-executed-movement.json"), JsonUtility.ToJson(report, true), new UTF8Encoding(false));
        }
        Debug.Log("[AITerritoryExecution] ALL PASSED " + checks + " executedMoves=" + report.Steps.Count(s => s.Executed)
            + " (controlled execution, not a full match)");
    }

    static void ReconnaissanceRequest(GameSystems live, Team team, bool scout)
    {
        string label = team + (scout ? " scout" : " no-scout fallback");
        using var f = new EconomyR2Tests.Fixture(live, team);
        var host = new GameObject("Territory route reconnaissance isolated hub"); host.SetActive(false);
        var neutral = new GameObject("Territory route reconnaissance observed intruder");
        var profile = ScriptableObject.CreateInstance<BoardActionProfile>();
        AICommander commander = null;
        try
        {
            var cells = FlatCorridor(live.MapCreate);
            f.Rules.aiEconomy.plannedDemandWeight = 0; f.Resources.Citizen = 0; f.Food(100);
            f.Building(f.Definition(FacilityKind.LoggingCamp, wood: 30));
            f.Building(f.Definition(FacilityKind.Quarry, stone: 30));
            f.Building(f.Definition(FacilityKind.Mine, iron: 30, magic: 30));
            f.Crystal.transform.position = cells[0]; f.SetOwnCrystalPosition(cells[0]);
            if (team == Team.Player) f.Crystals.ECP = new Vector3(33000, 0, 34000);
            else f.Crystals.PCP = new Vector3(33000, 0, 34000);
            var actor = f.Unit(); actor.kind = scout ? Kind.Scout : Kind.Knight; actor.transform.position = cells[1];
            actor.HP = actor.MaxHP = 100; actor.ATK = 25;
            profile.actionType = ActorActionType.Combat; profile.movement.useCustom = true; profile.movement.directionIndependent = true;
            profile.movement.SetCell(2, 3, true); profile.movement.SetCell(4, 3, true); actor.GrowthData.actionProfile = profile;
            var hub = host.AddComponent<TurnGenerator>(); hub.MarkDeveloperMatch();
            var moves = host.AddComponent<MoveGenerator>(); moves.mapcreate = live.MapCreate; moves.turnGenerator = hub; moves.BuildSystemRef = f.Builder;
            Set(moves, "crystalsystem", f.Crystals); Set(moves, "unitsetting", f.Units);
            Set(moves, "PlayerUnit", f.Units.PlayerUnit); Set(moves, "EnemyUnit", f.Units.EnemyUnit); moves.NeutralParent = neutral.transform;
            var vision = host.AddComponent<VisionGenerator>(); var territory = host.AddComponent<TerritorySystem>();
            territory.PTSetPos = new List<Vector3>(); territory.ETSetPos = new List<Vector3>(); territory.GetTerritory(team).Add(cells[0]);
            territory.GetTerritory(team == Team.Player ? Team.Enemy : Team.Player).Add(cells[5]);
            // Another observer previously saw the territory; two intervening cells remain unknown.
            var visible = new HashSet<Vector3Int>(new[] { cells[0], cells[1], cells[2], cells[5] }.Select(GridHelper.ToGridXZ));
            var explored = new HashSet<Vector3Int>(visible);
            foreach (var name in new[] { "_playerVisionBox", "_enemyVisionBox", "_playerExplored", "_enemyExplored" })
                Set(vision, name, name == (team == Team.Player ? "_playerVisionBox" : "_enemyVisionBox") ? visible
                    : name == (team == Team.Player ? "_playerExplored" : "_enemyExplored") ? explored : new HashSet<Vector3Int>());
            Set(vision, "PlayerUnit", f.Units.PlayerUnit); Set(vision, "EnemyUnit", f.Units.EnemyUnit); Set(vision, "territorysystem", territory);
            hub.Systems.MapCreate = live.MapCreate; hub.Systems.MoveGenerator = moves; hub.Systems.UnitSetting = f.Units;
            hub.Systems.CrystalSystem = f.Crystals; hub.Systems.BuildSystem = f.Builder; hub.Systems.FactionState = f.State; hub.Systems.APSystem = f.AP;
            commander = new AICommander(hub, moves, live.AttackGenerator, live.BattleSystem, vision, f.AP, f.Units, f.Crystals,
                live.MapCreate, MajorPersonality.Combat, f.Builder, null, f.State, live.SkillSystem, null, 15, 64517, team);
            var governor = commander.StrategicGovernor; var memory = AIBoardState.CreateSharedMemory();
            var board = Board(80);
            Check(label + " unknown gap cannot be promoted to a proved army route", !governor.HasActiveTerritoryAdvance
                && governor.TerritoryIntel.Reason == "territory_route_recon_required" && !board.IsTerrainKnown(cells[3]));
            Check(label + " missing route exposes only the actually observed territory direction",
                governor.TerritoryIntel.TryGetReconTarget(board, out var hint) && GridHelper.MatchXZ(hint, GridHelper.ToGridXZ(cells[5]))
                && board.Exploration.IsAssignedUnit(actor));
            var plans = new AIPlanManager(); plans.Update(board);
            Check(label + " reconnaissance plan retains the observed direction without a proved army waypoint",
                plans.Current != null && plans.Current.Goal == AIPlanGoal.Reconnaissance && GridHelper.MatchXZ(plans.Current.Target, hint));
            float forward = board.Recon.ScoreMove(actor, cells[2]);

            var intruderObject = new GameObject("Visible unrelated intruder"); intruderObject.transform.SetParent(neutral.transform); intruderObject.transform.position = cells[5];
            var intruder = intruderObject.AddComponent<Status>(); intruder.team = Team.Intruder; intruder.kind = Kind.Knight;
            intruder.type = Type.Unit; intruder.HP = intruder.MaxHP = 100; intruder.ATK = 0; intruder.GrowthData = actor.GrowthData;
            board = Board(81);
            Check(label + " visible third-faction contact does not replace territory intelligence", board.AlivePlayerUnits.Contains(intruder)
                && !board.HasPrincipalOpponentContact && governor.TerritoryIntel.TryGetReconTarget(board, out var afterContact) && afterContact == hint);
            var otherPlans = new AIPlanManager(); otherPlans.Update(board);
            var decision = commander.StrategyPlanner.DecideStrategy(board, commander.Personality, commander.ThreatLevel, 81);
            Check(label + " third faction cannot become the principal reconnaissance operation", otherPlans.Current != null
                && otherPlans.Current.Goal == AIPlanGoal.Reconnaissance && GridHelper.MatchXZ(otherPlans.Current.Target, hint)
                && decision.Strategy != TurnStrategy.ContactEngage && !board.PlayerCrystalVisible && !board.GetLastKnownPlayerCrystal().Valid);
            Check(label + " directed known safe frontier is preferred to returning home", forward > board.Recon.ScoreMove(actor, cells[0]));
            var actions = AIActionEvaluator.EvaluateAll(commander.Personality, board, commander.Learning, decision.Strategy);
            var selected = (AIAction)Select.Invoke(commander, new object[] { actions, new HashSet<string>(), null });
            Check(label + " real commander selects the known safe approach to the unknown gap", selected != null
                && selected.Unit == actor && selected.ActionType == AIActionType.Move && selected.TargetPos == cells[2]);
            var executor = new AIActionExecutor(hub, moves, live.AttackGenerator, live.BattleSystem, f.AP,
                live.SkillSystem, null, f.Builder, null, new AILearning(false), team);
            int ap = f.State.GetAP(team), cost = board.CalcMoveCost(actor, selected.TargetPos);
            Check(label + " requested reconnaissance is a real legal paid move", executor.Execute(selected, board)
                && actor.transform.position == cells[2] && f.State.GetAP(team) == ap - cost);
            // Controlled new observations supply the missing route; no terrain or legal rule changes.
            visible.Add(GridHelper.ToGridXZ(cells[3])); visible.Add(GridHelper.ToGridXZ(cells[4]));
            explored.UnionWith(visible); board = Board(82);
            Check(label + " observing the gap reconnects the persistent territory army route", governor.HasActiveTerritoryAdvance
                && governor.TerritoryIntel.ObservedCellCount == 1 && governor.TerritoryIntel.IsProgress(actor, cells[3], board));
            if (!scout)
            {
                decision = commander.StrategyPlanner.DecideStrategy(board, commander.Personality, commander.ThreatLevel, 82);
                actions = AIActionEvaluator.EvaluateAll(commander.Personality, board, commander.Learning, decision.Strategy);
                selected = (AIAction)Select.Invoke(commander, new object[] { actions, new HashSet<string>(), null });
                Check(label + " the main army resumes a promoted advance after reconnaissance", selected != null && selected.Unit == actor
                    && selected.ActionType == AIActionType.Move && selected.TargetPos == cells[3] && selected.StrategicPriority == 3
                    && executor.Execute(selected, board) && actor.transform.position == cells[3]);
            }

            AIBoardState Board(int ownTurn)
            {
                hub.Context.Turn = ownTurn; f.State.ResetAPForTurn(team); actor.Fatigue = 0; actor.HasMovedThisTurn = false;
                var next = new AIBoardState(moves, live.AttackGenerator, f.AP, f.Units, f.Crystals, vision, f.Builder, null, f.State,
                    null, ownTurn, memory, team) { MapCreate = live.MapCreate, TerritorySystem = territory, ReconThreatLevel = 15, Governor = governor };
                CommanderBoard.SetValue(commander, next); next.Refresh(); governor.Evaluate(next); return next;
            }
        }
        finally
        {
            commander?.ReleaseRuntimeReferences();
            foreach (var actor in neutral.GetComponentsInChildren<Status>()) UnitRegistry.Instance?.Unregister(actor);
            UnityEngine.Object.DestroyImmediate(profile); UnityEngine.Object.DestroyImmediate(neutral); UnityEngine.Object.DestroyImmediate(host);
        }
    }

    static void ExecuteApproach(GameSystems live, Team team, Report report)
    {
        using var f = new EconomyR2Tests.Fixture(live, team);
        var host = new GameObject("Territory execution isolated hub"); host.SetActive(false);
        BoardActionProfile profile = null;
        AIReflectionConfig reflectionConfig = null;
        AICommander commander = null;
        try
        {
            var cells = FlatCorridor(live.MapCreate);
            f.Rules.aiEconomy.plannedDemandWeight = 0; f.Resources.Citizen = 0; f.Food(100);
            f.Building(f.Definition(FacilityKind.LoggingCamp, wood: 30));
            f.Building(f.Definition(FacilityKind.Quarry, stone: 30));
            var mine = f.Definition(FacilityKind.Mine, iron: 30, magic: 30); f.Building(mine);
            f.Crystal.transform.position = cells[0]; f.SetOwnCrystalPosition(cells[0]);
            if (team == Team.Player) f.Crystals.ECP = new Vector3(33000, 0, 34000);
            else f.Crystals.PCP = new Vector3(33000, 0, 34000);
            var actor = f.Unit(); actor.name = "Territory executed main combat Knight";
            actor.transform.position = cells[1]; actor.HP = actor.MaxHP = 100; actor.ATK = 25;
            profile = ScriptableObject.CreateInstance<BoardActionProfile>(); profile.actionType = ActorActionType.Combat;
            profile.movement.useCustom = true; profile.movement.directionIndependent = true;
            profile.movement.SetCell(2, 3, true); profile.movement.SetCell(4, 3, true);
            actor.GrowthData.actionProfile = profile;
            actor.GrowthData.upkeepMagic = 1;
            f.Opponent(new Vector3(-33000, 0, -34000), 5000);

            var hub = host.AddComponent<TurnGenerator>(); hub.MarkDeveloperMatch();
            var moves = host.AddComponent<MoveGenerator>(); moves.mapcreate = live.MapCreate; moves.turnGenerator = hub;
            Set(moves, "crystalsystem", f.Crystals); Set(moves, "unitsetting", f.Units);
            Set(moves, "PlayerUnit", f.Units.PlayerUnit); Set(moves, "EnemyUnit", f.Units.EnemyUnit);
            moves.BuildSystemRef = f.Builder;
            var vision = host.AddComponent<VisionGenerator>(); var territory = host.AddComponent<TerritorySystem>();
            territory.PTSetPos = new List<Vector3>(); territory.ETSetPos = new List<Vector3>();
            territory.GetTerritory(team).Add(cells[0]);
            territory.GetTerritory(team == Team.Player ? Team.Enemy : Team.Player).Add(cells[5]);
            var visible = new HashSet<Vector3Int>(cells.Select(GridHelper.ToGridXZ));
            var explored = new HashSet<Vector3Int>(visible);
            foreach (var name in new[] { "_playerVisionBox", "_enemyVisionBox", "_playerExplored", "_enemyExplored" })
                Set(vision, name, name == (team == Team.Player ? "_playerVisionBox" : "_enemyVisionBox") ? visible
                    : name == (team == Team.Player ? "_playerExplored" : "_enemyExplored") ? explored : new HashSet<Vector3Int>());
            Set(vision, "PlayerUnit", f.Units.PlayerUnit); Set(vision, "EnemyUnit", f.Units.EnemyUnit); Set(vision, "territorysystem", territory);
            hub.Systems.MapCreate = live.MapCreate; hub.Systems.MoveGenerator = moves; hub.Systems.UnitSetting = f.Units;
            hub.Systems.CrystalSystem = f.Crystals; hub.Systems.BuildSystem = f.Builder; hub.Systems.FactionState = f.State;
            hub.Systems.APSystem = f.AP;
            // Keep native movement/AP execution isolated from the live scene's visibility system.
            // The board and commander consume only the explicit current/explored fixture sight above.
            hub.Systems.VisionGenerator = null;
            commander = new AICommander(hub, moves, live.AttackGenerator, live.BattleSystem, vision, f.AP, f.Units,
                f.Crystals, live.MapCreate, MajorPersonality.Combat, f.Builder, null, f.State, live.SkillSystem,
                null, 1, 64517, team);
            var governor = commander.StrategicGovernor;
            var executor = new AIActionExecutor(hub, moves, live.AttackGenerator, live.BattleSystem, f.AP,
                live.SkillSystem, null, f.Builder, null, new AILearning(false), team);
            var memory = AIBoardState.CreateSharedMemory();
            var first = Board(60);
            Check(team + " territory-only no-scout fixture has a known deployment", governor.HasActiveTerritoryAdvance
                && first.AliveEnemyUnits.All(a => a.kind != Kind.Scout) && first.AlivePlayerUnits.Count == 0
                && !first.PlayerCrystalVisible && !first.GetLastKnownPlayerCrystal().Valid);
            Vector3 waypoint = governor.TerritoryIntel.Waypoint;
            float startingDistance = Distance(actor.transform.position, waypoint);
            Check(team + " known approach needs at least three legal turns", startingDistance >= 3);
            Move(first, "00-only-territory-executed");

            visible.Remove(GridHelper.ToGridXZ(cells[5]));
            var recipe = mine.levels[0]; recipe.Output.MagicOre = 0; mine.levels[0] = recipe;
            f.Resources.MagicOre = 0; GameContentRevision.NotifyChanged();
            var paused = Board(61); var heldAt = actor.transform.position;
            Check(team + " actual supply forecast pauses the retained territory direction", governor.Mode == StrategicMode.EconomicRecovery
                && governor.Economy.UpkeepPaymentFailed && governor.TerritoryIntel.Suspended
                && governor.TerritoryIntel.HasWaypoint && (Vector3)governor.TerritoryIntel.Waypoint == waypoint);
            var pauseDecision = commander.StrategyPlanner.DecideStrategy(paused, commander.Personality, commander.ThreatLevel, 61);
            var pauseActions = AIActionEvaluator.EvaluateAll(commander.Personality, paused, commander.Learning, pauseDecision.Strategy);
            Check(team + " interrupted deployment loses its advance priority", !pauseActions.Any(a => a.Unit == actor
                && a.ActionType == AIActionType.Move && a.StrategicPriority == 3));
            Record(paused, "01-economic-interruption-policy", pauseDecision, null, false, true, heldAt, heldAt, 0, pauseActions.Count);

            recipe.Output.MagicOre = 30; mine.levels[0] = recipe; f.Resources.MagicOre = 400; GameContentRevision.NotifyChanged();
            var resumed = Board(62);
            Check(team + " recovery resumes the same memory goal outside current sight", governor.HasActiveTerritoryAdvance
                && (Vector3)governor.TerritoryIntel.Waypoint == waypoint && governor.TerritoryIntel.ObservedCellCount == 1
                && !visible.Contains(GridHelper.ToGridXZ(cells[5])));
            // Reject stale or unaffordable requests through the same executor, with zero action/AP effect.
            var illegal = new AIAction { ActionType = AIActionType.Move, Unit = actor, TargetPos = actor.transform.position + Vector3.right * 8 };
            int ap = f.State.GetAP(team); var before = actor.transform.position;
            Check(team + " illegal movement still fails without AP or position change", !executor.Execute(illegal, resumed)
                && actor.transform.position == before && f.State.GetAP(team) == ap);
            f.State.SetAP(team, 0); resumed.Refresh();
            var noAP = new AIAction { ActionType = AIActionType.Move, Unit = actor, TargetPos = cells[3], APCost = resumed.CalcMoveCost(actor, cells[3]) };
            Check(team + " insufficient AP still fails without position change", noAP.APCost > 0 && !executor.Execute(noAP, resumed)
                && actor.transform.position == before && f.State.GetAP(team) == 0);
            f.State.ResetAPForTurn(team); resumed.Refresh(); governor.Evaluate(resumed);
            Move(resumed, "02-memory-resumed-executed");
            Move(Board(63), "03-reflected-goal-completion-executed", reflected: true);
            Check(team + " three selected legal executions reach the observed approach with positive distance gain",
                actor.transform.position == waypoint && startingDistance - Distance(actor.transform.position, waypoint) >= 3
                && report.Steps.Count(s => s.Team == team.ToString() && s.Executed) == 3);

            AIBoardState Board(int ownTurn)
            {
                hub.Context.Turn = ownTurn; f.State.ResetAPForTurn(team); actor.Fatigue = 0; actor.HasMovedThisTurn = false;
                var board = new AIBoardState(moves, live.AttackGenerator, f.AP, f.Units, f.Crystals, vision,
                    f.Builder, null, f.State, null, ownTurn, memory, team)
                    { MapCreate = live.MapCreate, TerritorySystem = territory, ReconThreatLevel = 1, Governor = governor };
                CommanderBoard.SetValue(commander, board); board.Refresh(); governor.Evaluate(board); return board;
            }
            void Move(AIBoardState board, string phase, bool reflected = false)
            {
                var decision = commander.StrategyPlanner.DecideStrategy(board, commander.Personality, commander.ThreatLevel, board.TurnCount);
                CommanderStrategy.SetValue(commander, decision.Strategy);
                var actions = AIActionEvaluator.EvaluateAll(commander.Personality, board, commander.Learning, decision.Strategy);
                var selected = (AIAction)Select.Invoke(commander, new object[] { actions, new HashSet<string>(), null });
                var from = actor.transform.position;
                bool legal = selected != null && selected.Unit == actor && selected.ActionType == AIActionType.Move
                    && board.GetValidMoves(actor).Contains(selected.TargetPos) && live.MapCreate.IsLegalMove(actor, from, selected.TargetPos);
                float progress = selected == null ? 0 : governor.TerritoryIntel.ProgressScore(actor, selected.TargetPos, board);
                Check(team + " T" + board.TurnCount + " real commander selects a legal territorial progress move", legal
                    && selected.StrategicPriority == 3 && progress > 0 && (Vector3)governor.TerritoryIntel.Waypoint == waypoint);
                int previousAP = f.State.GetAP(team), cost = board.CalcMoveCost(actor, selected.TargetPos);
                bool executed;
                AIActionReflectionSystem reflection = null;
                if (reflected)
                {
                    Check(team + " reflected regression begins one legal step from its active goal", selected.TargetPos == waypoint
                        && Distance(from, waypoint) == 1 && governor.TerritoryIntel.HasWaypoint);
                    reflectionConfig = ScriptableObject.CreateInstance<AIReflectionConfig>();
                    reflectionConfig.EnableReflection = true; reflectionConfig.EnableFileDiary = false; reflectionConfig.EnableConsoleDiary = false;
                    reflection = new AIActionReflectionSystem(team, reflectionConfig, persistent: false);
                    reflection.BeginBattle("TerritoryCompletionOrdering_" + team, commander.ThreatLevel.Level, commander.Personality.Major.ToString());
                    Set(commander, "reflection", reflection);
                    reflection.BeginTurn(board.TurnCount, board, decision.Strategy, commander.ThreatLevel.Level);
                    Check(team + " goal completion uses enabled reflection and the real commander integration", reflection.Enabled
                        && ReferenceEquals(commander.Reflection, reflection) && ExecuteReflected != null);
                    executed = (bool)ExecuteReflected.Invoke(commander, new object[] { selected });
                }
                else
                {
                    executed = executor.Execute(selected, board); board.Refresh(); governor.TerritoryIntel.RecordAction(selected, executed, board);
                }
                Check(team + " T" + board.TurnCount + " movement executes and debits exact legal AP", executed
                    && actor.transform.position == selected.TargetPos && actor.HasMovedThisTurn
                    && f.State.GetAP(team) == previousAP - cost && selected.APCost == cost);
                Check(team + " T" + board.TurnCount + " goal distance decreases and recorded progress advances", Distance(actor.transform.position, waypoint) < Distance(from, waypoint)
                    && governor.TerritoryIntel.LastProgressTurn == board.TurnCount);
                Record(board, phase, decision, selected, executed, legal, from, actor.transform.position, progress, actions.Count,
                    previousAP, cost);
                if (reflected)
                {
                    var completed = reflection.LastRecord;
                    Check(team + " reflection completes the successful move with finalized actor position", completed != null
                        && completed.ExecutionSucceeded && completed.After != null && completed.After.ActorX == Mathf.RoundToInt(waypoint.x)
                        && completed.After.ActorY == Mathf.RoundToInt(waypoint.z));
                    var state = governor.TerritoryIntel.CaptureState();
                    var reached = GridHelper.ToGridXZ(waypoint);
                    Check(team + " reflected completion records the reached old goal before re-selection", state?.Visits != null
                        && state.Visits.Any(v => v.Cell == reached && v.UntilTurn > board.TurnCount));
                    board.Refresh(); governor.Evaluate(board);
                    Check(team + " the next governor update excludes the completed cooling goal", !governor.TerritoryIntel.HasWaypoint
                        || GridHelper.ToGridXZ(governor.TerritoryIntel.Waypoint) != reached);
                    // Remove occupancy as an alternative explanation for exclusion. This temporary
                    // fixture relocation is not counted as an executed move or a gameplay result.
                    var atGoal = actor.transform.position;
                    try
                    {
                        actor.transform.position = from; board.Refresh(); governor.Evaluate(board);
                        Check(team + " completed-goal cooldown still excludes it after a controlled vacancy", !governor.TerritoryIntel.HasWaypoint
                            || GridHelper.ToGridXZ(governor.TerritoryIntel.Waypoint) != reached);
                    }
                    finally { actor.transform.position = atGoal; board.Refresh(); governor.Evaluate(board); }
                }
            }
            void Record(AIBoardState board, string phase, TurnStrategyPlanner.StrategyDecision decision, AIAction action,
                bool executed, bool legal, Vector3 from, Vector3 after, float progress, int candidateCount, int previousAP = -1, int cost = 0)
            {
                int visibleTerritory = board.OwnVisionCells.Count(c => territory.IsInTerritory(c, board.OpponentTeam));
                var entry = new Step { Team = team.ToString(), Phase = phase, Turn = board.TurnCount, Governor = governor.Mode.ToString(),
                    Economy = governor.Economy.State.ToString(), Strategy = decision.Strategy.ToString(), StrategyReason = decision.Reason,
                    TerritoryReason = governor.TerritoryIntel.Reason, Suspended = governor.TerritoryIntel.Suspended,
                    Before = from, After = after, Destination = action?.TargetPos ?? after, Waypoint = waypoint,
                    GoalDistanceBefore = Distance(from, waypoint), GoalDistanceAfter = Distance(after, waypoint), RouteProgress = progress,
                    APBefore = previousAP >= 0 ? previousAP : f.State.GetAP(team), APCost = cost, APAfter = f.State.GetAP(team),
                    Action = action?.ActionType.ToString() ?? "deployment_suspended_no_execution", Rejection = action?.StrategicRejectReason ?? "",
                    Priority = action?.StrategicPriority ?? -1, CandidateCount = candidateCount, Legal = legal, Executed = executed,
                    VisibleOpponentTerritory = visibleTerritory, RememberedCells = governor.TerritoryIntel.ObservedCellCount,
                    LastProgressTurn = governor.TerritoryIntel.LastProgressTurn, NoPrincipalContact = !board.HasPrincipalOpponentContact,
                    NoCrystalKnowledge = !board.PlayerCrystalVisible && !board.GetLastKnownPlayerCrystal().Valid,
                    NoScout = board.AliveEnemyUnits.All(a => a.kind != Kind.Scout) };
                report.Steps.Add(entry);
                Check(team + " " + phase + " never invents principal contact or crystal intelligence", entry.NoPrincipalContact && entry.NoCrystalKnowledge && entry.NoScout);
                Debug.Log("[AITerritoryExecutedStep] " + JsonUtility.ToJson(entry));
            }
        }
        finally
        {
            commander?.ReleaseRuntimeReferences();
            if (reflectionConfig != null) UnityEngine.Object.DestroyImmediate(reflectionConfig);
            if (profile != null) UnityEngine.Object.DestroyImmediate(profile);
            UnityEngine.Object.DestroyImmediate(host);
        }
    }
    static float Distance(Vector3 a, Vector3 b) => GridHelper.ChebyshevDistance(a, b);
    static void Set(object source, string field, object value) => source.GetType().GetField(field, Hidden).SetValue(source, value);
    static Vector3[] FlatCorridor(MapCreate map)
    {
        foreach (var origin in map.SetPos)
        {
            var result = new Vector3[6]; bool valid = true;
            for (int i = 0; i < result.Length; i++)
            {
                int x = Mathf.RoundToInt(origin.x) + i, z = Mathf.RoundToInt(origin.z);
                if (!map.TryGetHeight(x, z, out float y) || Mathf.Abs(y - origin.y) > .01f || map.IsRiver(x, z) || map.IsHighMountain(x, z))
                { valid = false; break; }
                result[i] = new Vector3(x, y, z);
                if (map.Environment.HasCell(result[i]) || i > 0 && !map.CanTraverse(result[i - 1], result[i])) { valid = false; break; }
            }
            if (valid) return result;
        }
        throw new InvalidOperationException("Territory execution fixture needs six connected level cells without environmental hazards.");
    }
}
#endif
