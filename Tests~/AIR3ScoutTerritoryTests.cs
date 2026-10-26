#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>Controlled native P1 diagnosis: real perception, candidate generation and governor policy.
/// Scripted known/current sight on a synthetic flat map; no complete turn, movement execution or match claim.</summary>
public static class AIR3ScoutTerritoryTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static int passed, failed;
    static void Check(string label, bool condition)
    { if (!condition) throw new InvalidOperationException("[AIR3ScoutTerritory] FAIL " + label); passed++; Debug.Log("[AIR3ScoutTerritory] PASS " + label); }
    static void Case(string label, Action run)
    {
        try { run(); }
        catch (Exception error) { failed++; Debug.Log("[AIR3ScoutTerritoryCase] FAIL " + label + " " + (error.InnerException ?? error).Message); }
    }
    public static void Run(GameSystems live)
    {
        if (!Application.isPlaying || live?.MapCreate == null) throw new InvalidOperationException("Initialized native Play Mode required.");
        passed = failed = 0;
        var random = UnityEngine.Random.state;
        string player = JsonUtility.ToJson(live.FactionState.PlayerNation), enemy = JsonUtility.ToJson(live.FactionState.EnemyNation);
        using (BoardActorAnimation.SuppressEffects())
            foreach (var team in new[] { Team.Player, Team.Enemy })
            {
                Case(team + " unseen-territory control", () => FrontierPriority(live, team, false));
                Case(team + " territory-and-frontier coexist", () => FrontierPriority(live, team, true));
                Case(team + " unproductive-viewpoint", () => UnproductiveViewpoint(live, team));
                Case(team + " nonprogress-move", () => Nonprogress(live, team, false));
                Case(team + " same-turn-repeat", () => Nonprogress(live, team, true));
            }
        Check("live nations/AP/resources preserved", player == JsonUtility.ToJson(live.FactionState.PlayerNation)
            && enemy == JsonUtility.ToJson(live.FactionState.EnemyNation));
        Check("gameplay random state preserved", random.Equals(UnityEngine.Random.state));
        if (failed > 0) throw new InvalidOperationException("[AIR3ScoutTerritory] failed cases=" + failed + " passed checks=" + passed);
        Debug.Log("[AIR3ScoutTerritory] ALL PASSED " + passed);
    }

    static void FrontierPriority(GameSystems live, Team team, bool territoryVisible)
    {
        using var f = new Fixture(live, team, territoryVisible);
        var board = f.Board(60); var goal = f.Governor.Exploration.GetObjective(f.Scout.GetInstanceID());
        string label = team + (territoryVisible ? " observed territory" : " no territory evidence");
        Check(label + " no hostile units/crystal or commander memory is supplied", board.AlivePlayerUnits.Count == 0
            && !board.HasPrincipalOpponentContact && !board.PlayerCrystalVisible && !board.GetLastKnownPlayerCrystal().Valid
            && !board.GetLastKnownPrincipalCommander(out _, out _));
        Check(label + " governor is funded and has the intended territory state", f.Governor.Mode != StrategicMode.EmergencyDefense
            && f.Governor.Mode != StrategicMode.EconomicRecovery && f.Governor.HasActiveTerritoryAdvance == territoryVisible
            && f.Governor.TerritoryIntel.HasObservedTerritory == territoryVisible);
        Check(label + " scout has a fresh active unfinished frontier", goal != null && goal.State == ObjectiveState.Active
            && !goal.IsIntelRevisit && !GridHelper.MatchXZ(goal.TargetCell, GridHelper.ToGridXZ(f.Scout.transform.position))
            && f.Governor.Exploration.Expeditions.Count == 0 && HasUnknownNeighbour(f.Governor.Exploration, goal.TargetCell));
        var raw = new List<AIAction>(); AIActionGenerator.GenerateAllCandidates(board, raw);
        var scout = Move(raw, f.Scout, f.Scout.transform.position + Vector3.right);
        var army = Move(raw, f.Army, f.Army.transform.position + Vector3.right);
        Check(label + " production generator supplies both legal known advances without ordinary combat", scout != null && army != null
            && raw.All(a => a.ActionType != AIActionType.Attack) && board.IsTerrainKnown(scout.TargetPos)
            && board.IsTerrainKnown(army.TargetPos) && f.Governor.Exploration.GetGoalProgress(f.Scout.GetInstanceID(), GridHelper.ToGrid(scout.TargetPos)) > 0
            && board.EstimateCounterDamageAt(scout.TargetPos, f.Scout) == 0);
        int before = raw.Count; f.Governor.Filter(raw, board);
        Check(label + " governor retains the real scout and army candidates", raw.Contains(scout) && raw.Contains(army)
            && scout.StrategicRejectReason == null && army.StrategicRejectReason == null);
        if (territoryVisible) Check(label + " main-force move really advances its observed territory waypoint", army.StrategicPriority == 3
            && f.Governor.TerritoryIntel.IsProgress(f.Army, army.TargetPos, board));
        var evaluated = AIActionEvaluator.EvaluateAll(f.Personality, board, null, territoryVisible ? TurnStrategy.Assault : TurnStrategy.ScoutSearch);
        var finalScout = Move(evaluated, f.Scout, scout.TargetPos); var finalArmy = Move(evaluated, f.Army, army.TargetPos);
        Debug.Log($"[AIR3ScoutTerritoryTrace] side={team} territory={territoryVisible} mode={f.Governor.Mode} known={f.Governor.Exploration.Memory.Count}"
            + $" activeTerritory={f.Governor.HasActiveTerritoryAdvance} frontier={goal.TargetCell} unknownNeighbour={HasUnknownNeighbour(f.Governor.Exploration, goal.TargetCell)}"
            + $" raw={before} afterGovernor={raw.Count} scoutP={scout.StrategicPriority} armyP={army.StrategicPriority}"
            + $" finalScoutP={finalScout?.StrategicPriority} finalScoutScore={finalScout?.Score} finalArmyP={finalArmy?.StrategicPriority} finalArmyScore={finalArmy?.Score}");
        Check(label + " valid first scouting progress retains P3 alongside deployment", scout.StrategicPriority == 3
            && finalScout != null && finalScout.StrategicPriority == 3);
    }

    static void UnproductiveViewpoint(GameSystems live, Team team)
    {
        using var f = new Fixture(live, team, true);
        var first = f.Board(60); var goal = f.Governor.Exploration.GetObjective(f.Scout.GetInstanceID());
        Check(team + " unproductive fixture starts with an active safe frontier", goal != null && goal.State == ObjectiveState.Active);
        Vector3Int reached = goal.TargetCell;
        // Fixed observed inputs deliberately reveal nothing on arrival. This is a planner fixture,
        // not an executed move or a claim that automatic Fog generated no new terrain here.
        f.Scout.transform.position = reached; f.Board(61);
        Check(team + " an actual observed arrival without information records bounded viewpoint evidence",
            f.Governor.Exploration.Expeditions.Any(r => r.ActorLifeId == f.Scout.ReflectionLifeId && r.Unproductive && GridHelper.MatchXZ(r.Viewpoint, reached)));
        f.Scout.transform.position = (Vector3)reached + Vector3.left;
        var board = f.Board(62); var raw = new List<AIAction>(); AIActionGenerator.GenerateAllCandidates(board, raw);
        var repeated = Move(raw, f.Scout, reached);
        Check(team + " stale viewpoint remains a legal tile but is no longer current objective progress", repeated != null
            && f.Governor.Exploration.GetGoalProgress(f.Scout.GetInstanceID(), reached) <= 0);
        f.Governor.Filter(raw, board);
        Check(team + " information-free revisit is not blindly promoted to P3", !raw.Contains(repeated) || repeated.StrategicPriority > 3);
    }

    static void Nonprogress(GameSystems live, Team team, bool alreadyMoved)
    {
        using var f = new Fixture(live, team, true);
        f.Scout.HasMovedThisTurn = alreadyMoved;
        var board = f.Board(60); var raw = new List<AIAction>(); AIActionGenerator.GenerateAllCandidates(board, raw);
        var destination = f.Scout.transform.position + (alreadyMoved ? Vector3.right : Vector3.left);
        var candidate = Move(raw, f.Scout, destination);
        string label = team + (alreadyMoved ? " same-turn repeated scout move" : " move away from the frontier");
        Check(label + " is a real legal generated candidate", candidate != null);
        if (!alreadyMoved) Check(label + " has no objective progress", f.Governor.Exploration.GetGoalProgress(f.Scout.GetInstanceID(), GridHelper.ToGrid(destination)) <= 0);
        f.Governor.Filter(raw, board);
        Check(label + " receives no blanket exploration P3", !raw.Contains(candidate) || candidate.StrategicPriority > 3);
    }

    static AIAction Move(List<AIAction> candidates, Status actor, Vector3 cell)
        => candidates.FirstOrDefault(a => a.ActionType == AIActionType.Move && a.Unit == actor && GridHelper.MatchXZ(a.TargetPos, GridHelper.ToGridXZ(cell)));
    static bool HasUnknownNeighbour(AIExplorationPlanner planner, Vector3Int at)
        => new[] { Vector3Int.left, Vector3Int.right, Vector3Int.forward, Vector3Int.back }
            .Any(offset => !planner.Memory.ContainsKey(GridHelper.ToGridXZ(at + offset)));

    sealed class Fixture : IDisposable
    {
        public readonly EconomyR2Tests.Fixture Economy;
        public readonly Status Scout, Army;
        public readonly AIStrategicGovernor Governor = new AIStrategicGovernor();
        public readonly AIPersonality Personality = new AIPersonality(MajorPersonality.Combat, 74291);
        readonly GameSystems live;
        readonly GameObject root;
        readonly MapCreate map;
        readonly MoveGenerator moves;
        readonly VisionGenerator vision;
        readonly TerritorySystem territory;
        readonly BoardActionProfile profile;
        public Fixture(GameSystems live, Team team, bool showTerritory)
        {
            this.live = live; Economy = new EconomyR2Tests.Fixture(live, team);
            root = new GameObject("R3 scouting and territory observed-input fixture"); root.SetActive(false);
            try
            {
                Economy.Rules.aiEconomy.plannedDemandWeight = 0; Economy.Resources.Citizen = 0; Economy.Food(100);
                Economy.Building(Economy.Definition(FacilityKind.LoggingCamp, wood: 30));
                Economy.Building(Economy.Definition(FacilityKind.Quarry, stone: 30));
                Economy.Building(Economy.Definition(FacilityKind.Well, water: 30));
                Economy.Building(Economy.Definition(FacilityKind.Mine, iron: 30, magic: 30));
                map = root.AddComponent<MapCreate>(); map.maxX = 24; map.maxZ = 16; map.UseR1Terrain = true;
                map.SetPos = new List<Vector3>();
                for (int x = 0; x < map.maxX; x++) for (int z = 0; z < map.maxZ; z++) map.SetPos.Add(new Vector3(x, 1, z));
                Set(map, "topY", new int[map.maxX, map.maxZ]); Set(map, "rivers", new bool[map.maxX, map.maxZ]);
                var neutral = new GameObject("Empty isolated neutral parent"); neutral.transform.SetParent(root.transform);
                moves = root.AddComponent<MoveGenerator>(); moves.mapcreate = map;
                Set(moves, "crystalsystem", Economy.Crystals); Set(moves, "unitsetting", Economy.Units);
                Set(moves, "PlayerUnit", Economy.Units.PlayerUnit); Set(moves, "EnemyUnit", Economy.Units.EnemyUnit); moves.NeutralParent = neutral.transform;
                Economy.Crystal.transform.position = new Vector3(1, 1, 5); Economy.SetOwnCrystalPosition(Economy.Crystal.transform.position);
                if (team == Team.Player) Economy.Crystals.ECP = new Vector3(22, 1, 14); else Economy.Crystals.PCP = new Vector3(22, 1, 14);
                profile = ScriptableObject.CreateInstance<BoardActionProfile>(); profile.actionType = ActorActionType.Combat;
                profile.movement.useCustom = profile.attack.useCustom = true;
                profile.movement.directionIndependent = profile.attack.directionIndependent = true;
                foreach (var offset in new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down })
                { profile.movement.SetCell(3 + offset.x, 3 + offset.y, true); profile.attack.SetCell(3 + offset.x, 3 + offset.y, true); }
                Army = Actor(Kind.Knight, new Vector3(3, 1, 5)); Scout = Actor(Kind.Scout, new Vector3(5, 1, 5));
                territory = root.AddComponent<TerritorySystem>(); territory.PTSetPos = new List<Vector3>(); territory.ETSetPos = new List<Vector3>();
                territory.GetTerritory(team).Add(Economy.Crystal.transform.position);
                territory.GetTerritory(team == Team.Player ? Team.Enemy : Team.Player).Add(new Vector3(8, 1, 5));
                var explored = new HashSet<Vector3Int>(); for (int x = 1; x <= 8; x++) explored.Add(new Vector3Int(x, 0, 5));
                var visible = new HashSet<Vector3Int>(explored); if (!showTerritory) visible.Remove(new Vector3Int(8, 0, 5));
                vision = root.AddComponent<VisionGenerator>();
                foreach (var field in new[] { "_playerVisionBox", "_enemyVisionBox", "_playerExplored", "_enemyExplored" })
                    Set(vision, field, field == (team == Team.Player ? "_playerVisionBox" : "_enemyVisionBox") ? visible
                        : field == (team == Team.Player ? "_playerExplored" : "_enemyExplored") ? explored : new HashSet<Vector3Int>());
                Set(vision, "PlayerUnit", Economy.Units.PlayerUnit); Set(vision, "EnemyUnit", Economy.Units.EnemyUnit); Set(vision, "territorysystem", territory);
            }
            catch { Dispose(); throw; }
        }
        Status Actor(Kind kind, Vector3 position)
        {
            var actor = Economy.Unit(); actor.kind = kind; actor.GrowthData.kind = kind; actor.transform.position = position;
            actor.GrowthData.actionProfile = profile; actor.HP = actor.MaxHP = 100; actor.ATK = 25; actor.HasMovedThisTurn = false;
            return actor;
        }
        public AIBoardState Board(int turn)
        {
            var board = new AIBoardState(moves, live.AttackGenerator, Economy.AP, Economy.Units, Economy.Crystals,
                vision, Economy.Builder, null, Economy.State, null, turn, null, Economy.Team)
                { MapCreate = map, TerritorySystem = territory, ReconThreatLevel = 1, Governor = Governor };
            board.Refresh(); Governor.Evaluate(board); return board;
        }
        public void Dispose()
        { if (profile != null) UnityEngine.Object.DestroyImmediate(profile); if (root != null) UnityEngine.Object.DestroyImmediate(root); Economy?.Dispose(); }
    }
    static void Set(object source, string field, object value) => source.GetType().GetField(field, Hidden).SetValue(source, value);
}
#endif
