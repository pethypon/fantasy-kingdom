#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>Live Play Mode policy reproduction. Isolated actors/economy; unchanged production map and legal-action evaluators.</summary>
public static class AITerritoryAdvanceValidation
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    [Serializable] public sealed class Report
    {
        public string Method = "Real initialized Play Mode, isolated sustainable faction fixture, production observation/candidate/strategy/governor/operation evaluation. Ranked choices are NOT a full commander autoplay or executed moves.";
        public string Limits = "No terrain edits. No hidden actor is provided as an AI target. Territory ownership is queried only for current visible cells. Save phase uses existing governor/exploration/operation DTOs, not a full disk game save. Actual native movement and full-match continuation require separate validation.";
        public List<Observation> Observations = new List<Observation>();
    }
    [Serializable] public sealed class Observation
    {
        public string Team, Phase, Governor, EconomicState, Strategy, StrategyReason;
        public int Turn, VisibleOpponentTerritoryCells, ObservedOpponents, KnownBases, CandidateCount, MovementCandidates;
        public bool PrincipalContact, CrystalVisible, CrystalKnown, ExplorationAssigned, GovernorObjectiveActive, GovernorObjectivePaused;
        public Vector3 ActorPosition, TerritoryGoal, GovernorObjective;
        public string PrimaryOperation, OperationState, OperationCandidates, TerritoryIntelJson;
        public string BestAction, BestMainMove, MainMoveRejectReason;
        public int MainMovePriority, MainMoveAP;
        public float MainMoveScore, MainMoveGoalDistanceGain;
        public bool MainMoveLegal, MainMoveSafeArmyAdvance;
        public bool TerritoryMemoryAvailable, HasObservedTerritory, HasTerritoryWaypoint, TerritorySuspended;
        public int RememberedTerritoryCells, TerritoryFirstObservedTurn, TerritoryLastConfirmedTurn, TerritoryLastProgressTurn;
        public Vector3 TerritoryWaypoint;
        public string TerritoryReason;
    }
    public static void Run(GameSystems systems, string output)
    {
        if (!Application.isPlaying || systems?.MapCreate == null)
            throw new InvalidOperationException("AITerritoryAdvanceValidation requires an initialized real Play Mode match.");
        Directory.CreateDirectory(output);
        var report = new Report();
        using var writer = new StreamWriter(Path.Combine(output, "territory-candidates.csv"), false, new UTF8Encoding(false));
        writer.WriteLine("team,phase,turn,rank,kind,action,x,z,score,priority,ap,legal,goal_distance_gain,safe_army_advance,rejection");
        using (BoardActorAnimation.SuppressEffects())
            foreach (var team in new[] { Team.Player, Team.Enemy }) Scenario(systems, team, report, writer);
        File.WriteAllText(Path.Combine(output, "territory-observations.json"), JsonUtility.ToJson(report, true), new UTF8Encoding(false));
        Debug.Log("[AITerritoryAdvanceValidation] Native policy observations=" + report.Observations.Count + " output=" + output);
    }

    static void Scenario(GameSystems live, Team team, Report report, StreamWriter writer)
    {
        using var f = new EconomyR2Tests.Fixture(live, team);
        var host = new GameObject("Territory-only isolated observation"); host.SetActive(false);
        var vision = host.AddComponent<VisionGenerator>();
        var territory = host.AddComponent<TerritorySystem>();
        var profile = ScriptableObject.CreateInstance<BoardActionProfile>();
        var config = ScriptableObject.CreateInstance<AIOperationConfig>();
        config.EnablePersistentLearning = false;
        try
        {
            var cells = FlatCorridor(live.MapCreate);
            f.Rules.aiEconomy.plannedDemandWeight = 0;
            f.Resources.Citizen = 0;
            var bakery = f.Food(100);
            f.Building(f.Definition(FacilityKind.LoggingCamp, wood: 30));
            f.Building(f.Definition(FacilityKind.Quarry, stone: 30));
            f.Building(f.Definition(FacilityKind.Mine, iron: 30, magic: 30));
            f.Crystal.transform.position = cells[0]; f.SetOwnCrystalPosition(cells[0]);
            var actor = f.Unit(); actor.name = "Territory main combat Knight";
            actor.transform.position = cells[2]; actor.HP = actor.MaxHP = 100; actor.ATK = 25;
            profile.actionType = ActorActionType.Combat;
            profile.movement.useCustom = true; profile.movement.directionIndependent = true;
            profile.movement.SetCell(2, 3, true); profile.movement.SetCell(4, 3, true);
            actor.GrowthData.actionProfile = profile;
            // A real hidden hostile exists, but its cell is never in the sight sets.
            var hiddenFoe = f.Opponent(new Vector3(-14000, 0, -15000), 5000);
            var hiddenCrystal = new Vector3(17000, 0, 16000);
            if (team == Team.Player) f.Crystals.ECP = hiddenCrystal; else f.Crystals.PCP = hiddenCrystal;
            territory.PTSetPos = new List<Vector3>(); territory.ETSetPos = new List<Vector3>();
            territory.GetTerritory(team).Add(cells[0]);
            territory.GetTerritory(team == Team.Player ? Team.Enemy : Team.Player).AddRange(new[] { cells[4], cells[5], new Vector3(25000, 0, 25000) });
            Set(vision, "PlayerUnit", f.Units.PlayerUnit); Set(vision, "EnemyUnit", f.Units.EnemyUnit);
            Set(vision, "territorysystem", territory);
            var seen = new HashSet<Vector3Int>(cells.Take(4).Select(GridHelper.ToGridXZ));
            var explored = new HashSet<Vector3Int>(cells.Select(GridHelper.ToGridXZ));
            foreach (var name in new[] { "_playerVisionBox", "_enemyVisionBox", "_playerExplored", "_enemyExplored" }) Set(vision, name, new HashSet<Vector3Int>());
            Set(vision, team == Team.Player ? "_playerVisionBox" : "_enemyVisionBox", seen);
            Set(vision, team == Team.Player ? "_playerExplored" : "_enemyExplored", explored);
            var memory = AIBoardState.CreateSharedMemory();
            var governor = new AIStrategicGovernor();
            AIBoardState Board(int turn) => new AIBoardState(live.MoveGenerator, live.AttackGenerator,
                f.AP, f.Units, f.Crystals, vision, f.Builder, null, f.State, null, turn, memory, team)
                { MapCreate = live.MapCreate, TerritorySystem = territory, ReconThreatLevel = 15, Governor = governor };
            var board = Board(60); board.Refresh();
            var manager = new AIOperationManager(config); manager.BeginBattle(team, "TerritoryNative_" + team);
            var planner = new TurnStrategyPlanner(); var threat = new AIThreatLevel(15);
            int ownTurn = 1;
            Observe("00-territory-unseen-control", 60);
            seen.Add(GridHelper.ToGridXZ(cells[4])); seen.Add(GridHelper.ToGridXZ(cells[5]));
            Observe("01-only-territory-visible", 61);
            seen.Remove(GridHelper.ToGridXZ(cells[4])); seen.Remove(GridHelper.ToGridXZ(cells[5]));
            hiddenFoe.transform.position = new Vector3(18000, 20, -19000); hiddenFoe.HP = 1;
            Observe("02-territory-in-fog", 62);
            Observe("03-nine-turn-memory", 70);
            int bread = f.Resources.Bread;
            var recipe = bakery.levels[0]; int output = recipe.Output.Bread;
            f.Resources.Bread = 0; recipe.Output.Bread = 0; bakery.levels[0] = recipe; GameContentRevision.NotifyChanged();
            Observe("04-economic-interruption", 71);
            f.Resources.Bread = bread; recipe.Output.Bread = output; bakery.levels[0] = recipe; GameContentRevision.NotifyChanged();
            Observe("05-economic-recovery", 72);
            var objective = governor.Snapshot(); var exploration = governor.Exploration.CaptureState(); var operation = manager.Snapshot(false);
            object territoryState = CaptureIntel(board);
            governor = new AIStrategicGovernor(); governor.Restore(objective); governor.RestoreExploration(exploration);
            RestoreIntel(governor, territoryState, team);
            board = Board(73); board.Refresh();
            manager = new AIOperationManager(config); manager.BeginBattle(team, "TerritoryNative_" + team, operation);
            Observe("06-existing-dto-resume", 73);

            void Observe(string phase, int turn)
            {
                typeof(AIBoardState).GetProperty("TurnCount").SetValue(board, turn);
                actor.HasMovedThisTurn = false; f.State.SetAP(team, 40); board.Refresh();
                board.Governor.Evaluate(board);
                var strategy = planner.DecideStrategy(board, f.Personality, threat, turn);
                manager.BeginTurn(board, strategy.Strategy, ownTurn++, personality: f.Personality);
                var actions = AIActionEvaluator.EvaluateAll(f.Personality, board, new AILearning(false), strategy.Strategy);
                foreach (var action in actions) action.Score += manager.ActionBonus(action, board);
                actions.Sort(AIAction.ComparePriorityThenScore);
                var ownMoves = actions.Where(a => a.Unit == actor && IsMovement(a)).ToList();
                var best = ownMoves.FirstOrDefault();
                int visibleTerritory = 0;
                foreach (var visible in board.OwnVisionCells)
                    if (territory.IsInTerritory(visible, board.OpponentTeam)) visibleTerritory++;
                var entry = new Observation
                {
                    Team = team.ToString(), Phase = phase, Turn = turn,
                    VisibleOpponentTerritoryCells = visibleTerritory, ObservedOpponents = board.AlivePlayerUnits.Count,
                    PrincipalContact = board.HasPrincipalOpponentContact, CrystalVisible = board.PlayerCrystalVisible,
                    CrystalKnown = board.GetLastKnownPlayerCrystal().Valid, Governor = board.Governor.Mode.ToString(),
                    EconomicState = board.ProductionDemand.State.ToString(), Strategy = strategy.Strategy.ToString(), StrategyReason = strategy.Reason,
                    ActorPosition = actor.transform.position, TerritoryGoal = cells[4],
                    GovernorObjectiveActive = board.Governor.Objective.Active, GovernorObjectivePaused = board.Governor.Objective.Suspended,
                    GovernorObjective = board.Governor.Objective.Target,
                    ExplorationAssigned = board.Governor.Exploration.IsAssignedUnit(actor), KnownBases = manager.KnownEnemyBases.Count,
                    PrimaryOperation = manager.PrimaryOperation?.PrimaryGoal.ToString() ?? "none",
                    OperationState = manager.PrimaryOperation?.Status.ToString() ?? "none",
                    OperationCandidates = string.Join(";", manager.LastCandidates.Select(c => c.Plan.PrimaryGoal + ":" + c.Plan.Status + ":" + c.Plan.StrategicReason)),
                    CandidateCount = actions.Count, MovementCandidates = ownMoves.Count,
                    BestAction = actions.Count > 0 ? actions[0].ToString() : "none",
                    BestMainMove = best?.ToString() ?? "none", MainMovePriority = best?.StrategicPriority ?? -1,
                    MainMoveScore = best?.Score ?? 0, MainMoveAP = best?.APCost ?? 0,
                    MainMoveRejectReason = best?.StrategicRejectReason ?? "",
                    MainMoveGoalDistanceGain = best != null ? Distance(actor.transform.position, cells[4]) - Distance(best.TargetPos, cells[4]) : 0,
                    MainMoveLegal = best != null && board.GetValidMoves(actor).Contains(best.TargetPos),
                    MainMoveSafeArmyAdvance = best != null && SafeAdvance(board, best), TerritoryIntelJson = ReadIntel(board)
                };
                PopulateIntel(entry, Intel(board));
                report.Observations.Add(entry);
                int rank = 0;
                foreach (var action in actions)
                {
                    bool move = action.Unit == actor && IsMovement(action);
                    writer.WriteLine(string.Join(",", new[] { team.ToString(), phase, turn.ToString(), (rank++).ToString(), action.Unit?.kind.ToString() ?? "none",
                        action.ActionType.ToString(), action.TargetPos.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture), action.TargetPos.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                        action.Score.ToString("R", System.Globalization.CultureInfo.InvariantCulture), action.StrategicPriority.ToString(), action.APCost.ToString(),
                        (move && board.GetValidMoves(actor).Contains(action.TargetPos)).ToString(),
                        (move ? Distance(actor.transform.position, cells[4]) - Distance(action.TargetPos, cells[4]) : 0).ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                        (move && SafeAdvance(board, action)).ToString(), Escape(action.StrategicRejectReason) }));
                }
                writer.Flush();
                if (board.HasPrincipalOpponentContact || board.PlayerCrystalVisible || board.GetLastKnownPlayerCrystal().Valid)
                    throw new InvalidOperationException("[AITerritoryAdvanceValidation] Territory-only fixture leaked principal hostile/crystal intelligence.");
                Debug.Log("[AITerritoryAdvanceValidation] " + team + " " + phase + " visibleTerritory=" + visibleTerritory
                    + " contact=False crystalKnown=False governor=" + entry.Governor + " strategy=" + entry.Strategy
                    + " mainMove=" + entry.BestMainMove + " safeAdvance=" + entry.MainMoveSafeArmyAdvance);
                manager.EndTurn(board, ownTurn - 1);
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(config); UnityEngine.Object.DestroyImmediate(profile); UnityEngine.Object.DestroyImmediate(host);
        }
    }
    static bool IsMovement(AIAction action) => action.ActionType == AIActionType.Move || action.ActionType == AIActionType.Support
        || action.ActionType == AIActionType.Surround || action.ActionType == AIActionType.DefenseRepos || action.ActionType == AIActionType.Retreat;
    static float Distance(Vector3 a, Vector3 b) => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.z - b.z));
    static bool SafeAdvance(AIBoardState board, AIAction action)
        => (bool)typeof(AIStrategicGovernor).GetMethod("IsSafeArmyAdvance", Hidden).Invoke(board.Governor, new object[] { action, board });
    static object Member(object source, string name)
    {
        if (source == null) return null;
        var property = source.GetType().GetProperty(name); var field = source.GetType().GetField(name);
        return property != null ? property.GetValue(source) : field?.GetValue(source);
    }
    static object Intel(AIBoardState board) => Member(board.Governor, "TerritoryIntel") ?? Member(board, "TerritoryIntel")
        ?? Member(board, "EnemyTerritoryIntel") ?? Member(board, "EnemyTerritoryMemory");
    static object CaptureIntel(AIBoardState board)
    {
        object intel = Intel(board);
        return intel?.GetType().GetMethod("CaptureState")?.Invoke(intel, null);
    }
    static void RestoreIntel(AIStrategicGovernor governor, object state, Team team)
    {
        object intel = Member(governor, "TerritoryIntel");
        if (state == null || intel == null) return;
        var restore = intel.GetType().GetMethods().FirstOrDefault(m => m.Name == "RestoreState" && m.GetParameters().Length == 2);
        restore?.Invoke(intel, new[] { state, (object)team });
    }
    static string ReadIntel(AIBoardState board)
    {
        object state = CaptureIntel(board) ?? Intel(board);
        return state != null && !(state is UnityEngine.Object) ? JsonUtility.ToJson(state) : "not_exposed_in_this_revision";
    }
    static void PopulateIntel(Observation entry, object intel)
    {
        entry.TerritoryMemoryAvailable = intel != null;
        entry.HasObservedTerritory = Member(intel, "HasObservedTerritory") as bool? ?? false;
        entry.HasTerritoryWaypoint = Member(intel, "HasWaypoint") as bool? ?? false;
        entry.TerritorySuspended = Member(intel, "Suspended") as bool? ?? false;
        entry.RememberedTerritoryCells = Member(intel, "ObservedCellCount") as int? ?? 0;
        entry.TerritoryFirstObservedTurn = Member(intel, "FirstObservedTurn") as int? ?? -1;
        entry.TerritoryLastConfirmedTurn = Member(intel, "LastConfirmedTurn") as int? ?? -1;
        entry.TerritoryLastProgressTurn = Member(intel, "LastProgressTurn") as int? ?? -1;
        object waypoint = Member(intel, "Waypoint");
        entry.TerritoryWaypoint = waypoint is Vector3 position ? position : waypoint is Vector3Int cell ? cell : Vector3.zero;
        entry.TerritoryReason = Member(intel, "Reason") as string ?? "not_exposed_in_this_revision";
    }
    static void Set(VisionGenerator vision, string name, object value) => typeof(VisionGenerator).GetField(name, Hidden).SetValue(vision, value);
    static string Escape(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    static Vector3[] FlatCorridor(MapCreate map)
    {
        foreach (var origin in map.SetPos)
        {
            var result = new Vector3[6]; bool valid = true;
            for (int i = 0; i < result.Length; i++)
            {
                int x = Mathf.RoundToInt(origin.x) + i, z = Mathf.RoundToInt(origin.z);
                if (!map.TryGetHeight(x, z, out float y) || Mathf.Abs(y - origin.y) > .01f || map.IsRiver(x, z) || map.IsHighMountain(x, z)) { valid = false; break; }
                result[i] = new Vector3(x, y, z);
                if (map.Environment.HasCell(result[i]) || i > 0 && !map.CanTraverse(result[i - 1], result[i])) { valid = false; break; }
            }
            if (valid) return result;
        }
        throw new InvalidOperationException("Territory fixture needs six connected level map cells without environmental hazards.");
    }
}
#endif
