#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>Actual native arrival outcomes with controlled sight; no full-match or victory claim.</summary>
public static class AITerritoryArrivalTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static void Check(string label, bool condition)
    { if (!condition) throw new InvalidOperationException("[AITerritoryArrival] FAIL " + label); checks++; Debug.Log("[AITerritoryArrival] PASS " + label); }
    public static void Run(GameSystems systems, TurnGenerator turn)
    {
        if (!Application.isPlaying || systems?.MapCreate == null || turn == null) throw new InvalidOperationException("An initialized native match is required.");
        checks = 0;
        using (BoardActorAnimation.SuppressEffects())
            foreach (var team in new[] { Team.Player, Team.Enemy }) Scenario(systems, team);
        Debug.Log("[AITerritoryArrival] ALL PASSED " + checks + " (actual arrival, controlled post-arrival continuation)");
    }
    static void Scenario(GameSystems live, Team team)
    {
        using var f = new EconomyR2Tests.Fixture(live, team);
        var host = new GameObject("Territory arrival native isolated hub"); host.SetActive(false);
        var profile = ScriptableObject.CreateInstance<BoardActionProfile>();
        try
        {
            var cells = Corridor(live.MapCreate);
            f.Rules.aiEconomy.plannedDemandWeight = 0; f.Resources.Citizen = 0; f.Food(100);
            f.Building(f.Definition(FacilityKind.LoggingCamp, wood: 30)); f.Building(f.Definition(FacilityKind.Quarry, stone: 30));
            f.Building(f.Definition(FacilityKind.Mine, iron: 30, magic: 30));
            f.Crystal.transform.position = cells[0]; f.SetOwnCrystalPosition(cells[0]);
            if (team == Team.Player) f.Crystals.ECP = new Vector3(33000, 0, 34000); else f.Crystals.PCP = new Vector3(33000, 0, 34000);
            var actor = f.Unit(); actor.transform.position = cells[3]; actor.ATK = 25;
            profile.actionType = ActorActionType.Combat; profile.movement.useCustom = profile.attack.useCustom = true;
            profile.movement.directionIndependent = profile.attack.directionIndependent = true;
            foreach (int x in new[] { 2, 4 }) { profile.movement.SetCell(x, 3, true); profile.attack.SetCell(x, 3, true); }
            actor.GrowthData.actionProfile = profile;
            var hub = host.AddComponent<TurnGenerator>(); hub.MarkDeveloperMatch();
            var moves = host.AddComponent<MoveGenerator>(); moves.mapcreate = live.MapCreate; moves.turnGenerator = hub; moves.BuildSystemRef = f.Builder;
            Set(moves, "crystalsystem", f.Crystals); Set(moves, "unitsetting", f.Units); Set(moves, "PlayerUnit", f.Units.PlayerUnit); Set(moves, "EnemyUnit", f.Units.EnemyUnit);
            var battle = host.AddComponent<BattleSystem>(); var vision = host.AddComponent<VisionGenerator>(); var territory = host.AddComponent<TerritorySystem>();
            territory.PTSetPos = new List<Vector3>(); territory.ETSetPos = new List<Vector3>();
            territory.GetTerritory(team == Team.Player ? Team.Enemy : Team.Player).Add(cells[5]);
            Set(vision, "PlayerUnit", f.Units.PlayerUnit); Set(vision, "EnemyUnit", f.Units.EnemyUnit); Set(vision, "territorysystem", territory);
            var seen = new HashSet<Vector3Int>(cells.Select(GridHelper.ToGridXZ)); var explored = new HashSet<Vector3Int>(seen);
            foreach (var name in new[] { "_playerVisionBox", "_enemyVisionBox", "_playerExplored", "_enemyExplored" })
                Set(vision, name, name == (team == Team.Player ? "_playerVisionBox" : "_enemyVisionBox") ? seen
                    : name == (team == Team.Player ? "_playerExplored" : "_enemyExplored") ? explored : new HashSet<Vector3Int>());
            hub.Systems.MapCreate = live.MapCreate; hub.Systems.MoveGenerator = moves; hub.Systems.UnitSetting = f.Units;
            hub.Systems.CrystalSystem = f.Crystals; hub.Systems.BuildSystem = f.Builder; hub.Systems.FactionState = f.State;
            hub.Systems.APSystem = f.AP; hub.Systems.BattleSystem = battle;
            // Sight remains an explicit fixture input until the observed-attack check.
            hub.Systems.VisionGenerator = null;
            var governor = new AIStrategicGovernor(); var memory = AIBoardState.CreateSharedMemory();
            var executor = new AIActionExecutor(hub, moves, live.AttackGenerator, battle, f.AP, live.SkillSystem, null, f.Builder, null, new AILearning(false), team);
            var board = Board(60);
            Check(team + " initial observed approach is one real legal step away", governor.TerritoryIntel.HasWaypoint
                && governor.TerritoryIntel.Waypoint == GridHelper.ToGrid(cells[4]));
            Arrive(board, cells[4]);
            var firstArrival = governor.TerritoryIntel.CaptureState();
            Check(team + " actual arrival marks only its own approach completed", firstArrival.Visits.Count(v => v.Completed) == 1
                && firstArrival.Visits.Any(v => v.Completed && v.Cell == GridHelper.ToGridXZ(cells[4])));
            seen.Remove(GridHelper.ToGridXZ(cells[5]));
            board = Board(61);
            Check(team + " first arrival still permits another unvisited known staging point", governor.HasActiveTerritoryAdvance
                && governor.TerritoryIntel.Waypoint == GridHelper.ToGrid(cells[5]));
            Arrive(board, cells[5]);
            var bothArrivals = governor.TerritoryIntel.CaptureState();
            int elapsed = Mathf.Clamp(ExplorationAISettings.Active.LoopCooldownTurns, 1, 64) + 2;
            // Controlled vacancy removes own occupancy as an explanation; it is not a gameplay move.
            actor.transform.position = cells[2];
            governor.TerritoryIntel.RestoreState(firstArrival, team); board = Board(61 + elapsed);
            Check(team + " completed approach is excluded after its ordinary cooldown expires", governor.TerritoryIntel.CompletedApproachCount == 1
                && (!governor.TerritoryIntel.HasWaypoint || governor.TerritoryIntel.Waypoint != GridHelper.ToGrid(cells[4])));

            string legacyJson = JsonUtility.ToJson(firstArrival).Replace(",\"Completed\":true", "");
            var legacy = JsonUtility.FromJson<EnemyTerritoryIntelState>(legacyJson);
            Check(team + " old state-v1 payload omits the optional completion flag", !legacyJson.Contains("\"Completed\"")
                && legacy.Visits.All(v => !v.Completed) && EnemyTerritoryIntel.IsValidState(legacy, live.MapCreate.maxX, live.MapCreate.maxZ, team));
            governor.TerritoryIntel.RestoreState(legacy, team); board = Board(62 + elapsed);
            Check(team + " reproduction: ordinary expired cooldown reselects the already reached old border", governor.TerritoryIntel.HasWaypoint
                && governor.TerritoryIntel.Waypoint == GridHelper.ToGrid(cells[4]));

            var roundTrip = JsonUtility.FromJson<EnemyTerritoryIntelState>(JsonUtility.ToJson(bothArrivals));
            Check(team + " completed approach state passes real save validation", EnemyTerritoryIntel.IsValidState(roundTrip, live.MapCreate.maxX, live.MapCreate.maxZ, team));
            governor.TerritoryIntel.RestoreState(roundTrip, team);
            for (int ownTurn = 63 + elapsed; ownTurn <= 66 + elapsed; ownTurn++)
            {
                board = Board(ownTurn);
                Check(team + " exhausted old approaches request reconnaissance T" + ownTurn, !governor.HasActiveTerritoryAdvance
                    && governor.TerritoryIntel.CompletedApproachCount == 2 && governor.TerritoryIntel.ObservedCellCount == 1
                    && governor.TerritoryIntel.TryGetReconTarget(board, out _) && !board.HasPrincipalOpponentContact && !board.GetLastKnownPlayerCrystal().Valid);
            }
            governor.TerritoryIntel.Update(board, true, "economic_recovery");
            Check(team + " interrupted reconnaissance retains completed approach knowledge", governor.TerritoryIntel.Suspended
                && governor.TerritoryIntel.CompletedApproachCount == 2 && !governor.TerritoryIntel.HasWaypoint);
            governor.TerritoryIntel.Update(board, false, null);
            Check(team + " clearing interruption resumes reconnaissance without restarting the patrol", !governor.TerritoryIntel.Suspended
                && !governor.TerritoryIntel.HasWaypoint && governor.TerritoryIntel.TryGetReconTarget(board, out _));

            var foe = f.Opponent(cells[3], 0); foe.HP = foe.MaxHP = 1000; seen.Add(GridHelper.ToGridXZ(cells[3]));
            board = Board(67 + elapsed); hub.Systems.VisionGenerator = vision;
            var attack = new AIAction { ActionType = AIActionType.Attack, Unit = actor, TargetUnit = foe, TargetPos = cells[3], APCost = board.CalcAttackCost(actor) };
            Check(team + " actual observed enemy remains a legal attack target after approach completion", board.GetAttackTargets(actor).Contains(foe)
                && board.HasPrincipalOpponentContact && governor.TerritoryIntel.CompletedApproachCount == 2);
            int hp = foe.HP, barrier = foe.Barrier;
            bool attacked = executor.Execute(attack, board);
            Debug.Log("[AITerritoryArrivalCombat] team=" + team + " executed=" + attacked + " HP=" + hp + "->" + foe.HP
                + " Barrier=" + barrier + "->" + foe.Barrier + " completedApproaches=" + governor.TerritoryIntel.CompletedApproachCount);
            Check(team + " real observed attack reduces HP or barrier while retaining completed approaches", attacked
                && (long)foe.HP + foe.Barrier < (long)hp + barrier && governor.TerritoryIntel.CompletedApproachCount == 2);
            foe.gameObject.SetActive(false);

            // Only fresh visible loss of the old affiliation invalidates those reached approaches.
            territory.GetTerritory(team == Team.Player ? Team.Enemy : Team.Player).Clear(); seen.Add(GridHelper.ToGridXZ(cells[5]));
            board = Board(68 + elapsed);
            Check(team + " fresh ownership loss removes completed points without inventing another target", governor.TerritoryIntel.ObservedCellCount == 0
                && governor.TerritoryIntel.CompletedApproachCount == 0 && !governor.TerritoryIntel.HasWaypoint);

            AIBoardState Board(int ownTurn)
            {
                hub.Context.Turn = ownTurn; f.State.ResetAPForTurn(team); actor.Fatigue = 0; actor.HasMovedThisTurn = false;
                var next = new AIBoardState(moves, live.AttackGenerator, f.AP, f.Units, f.Crystals, vision, f.Builder, null, f.State,
                    null, ownTurn, memory, team) { MapCreate = live.MapCreate, TerritorySystem = territory, ReconThreatLevel = 15, Governor = governor };
                next.Refresh(); governor.Evaluate(next); return next;
            }
            void Arrive(AIBoardState current, Vector3 destination)
            {
                var actions = AIActionEvaluator.EvaluateAll(f.Personality, current, new AILearning(false), TurnStrategy.Balanced);
                var action = actions.FirstOrDefault(a => a.Unit == actor && a.ActionType == AIActionType.Move && a.TargetPos == destination);
                int ap = f.State.GetAP(team), cost = current.CalcMoveCost(actor, destination);
                Check(team + " arrival uses a legal production candidate with route progress", action != null && action.StrategicPriority == 3
                    && current.GetValidMoves(actor).Contains(destination) && governor.TerritoryIntel.IsProgress(actor, destination, current));
                bool success = executor.Execute(action, current);
                governor.TerritoryIntel.RecordAction(action, success, current);
                Check(team + " actual arrival consumes exact AP and completes the selected approach", success && actor.transform.position == destination
                    && f.State.GetAP(team) == ap - cost && !governor.TerritoryIntel.HasWaypoint);
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(profile); UnityEngine.Object.DestroyImmediate(host); }
    }
    static void Set(object source, string name, object value) => source.GetType().GetField(name, Hidden).SetValue(source, value);
    static Vector3[] Corridor(MapCreate map)
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
        throw new InvalidOperationException("Arrival fixture needs six flat connected safe cells.");
    }
}
#endif
