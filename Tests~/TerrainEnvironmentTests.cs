using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class TerrainEnvironmentTests
{
    static int checks;
    static readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
    const int Origin = 19000;
    static Vector3 Position(int x, int z) => new Vector3(Origin + x, 1, Origin + z);
    static Vector2Int Cell(int x, int z) => new Vector2Int(Origin + x, Origin + z);
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception("[Environment] " + message); }
    static TerrainEnvironmentDefinition Definition(TerrainEnvironmentKind kind)
    {
        var value = ScriptableObject.CreateInstance<TerrainEnvironmentDefinition>(); owned.Add(value);
        value.kind = kind; value.definitionId = "test." + kind; value.enabledForPlay = true;
        return value;
    }
    static void Place(TerrainEnvironmentCatalog catalog, TerrainEnvironmentDefinition definition, int x, int z)
    {
        catalog.definitions.Add(definition);
        catalog.placements.Add(new TerrainEnvironmentPlacement { definition = definition, cell = Cell(x, z) });
    }
    static Status Actor(Team team, int x, int z)
    {
        var go = new GameObject("EnvironmentFixture"); owned.Add(go);
        var actor = go.AddComponent<Status>(); actor.team = team; actor.type = Type.Unit; actor.kind = Kind.Knight;
        actor.HP = actor.MaxHP = 100; actor.transform.position = Position(x, z); CombatRegistry.Register(actor); return actor;
    }
    public static void PlayMode(GameSystems liveSystems)
    {
        checks = 0;
        try
        {
            var go = new GameObject("EnvironmentMapFixture"); owned.Add(go);
            var map = go.AddComponent<MapCreate>(); map.UseR1Terrain = false;
            for (int x = 0; x < 15; x++) for (int z = 0; z < 15; z++) map.SetPos.Add(Position(x, z));
            var catalog = ScriptableObject.CreateInstance<TerrainEnvironmentCatalog>(); owned.Add(catalog); catalog.enabledForPlay = true;
            var forest = Definition(TerrainEnvironmentKind.Forest);
            forest.blocksVision = forest.concealsOccupants = forest.burnable = true;
            forest.revealDistance = 1; forest.burnSmokeRounds = 3; forest.burnSmokeRevealDistance = 0; Place(catalog, forest, 2, 1);
            var bush = Definition(TerrainEnvironmentKind.Bush); bush.concealsOccupants = true; bush.revealDistance = 2; Place(catalog, bush, 4, 1);
            var rock = Definition(TerrainEnvironmentKind.Rock); rock.blocksMovement = rock.blocksShots = rock.blocksVision = true; Place(catalog, rock, 6, 1);
            var swamp = Definition(TerrainEnvironmentKind.PoisonSwamp); swamp.damageOnEntry = swamp.damageOnOwnerTurn = true; swamp.flatDamage = 10; Place(catalog, swamp, 2, 4);
            var mud = Definition(TerrainEnvironmentKind.Mud); mud.moveAPAdjustment = 2; Place(catalog, mud, 3, 4);
            var road = Definition(TerrainEnvironmentKind.Road); road.moveAPAdjustment = -1; Place(catalog, road, 4, 4);
            var spring = Definition(TerrainEnvironmentKind.HealingSpring); spring.maximumHpHealFraction = .1f; Place(catalog, spring, 5, 4);
            var geyser = Definition(TerrainEnvironmentKind.Geyser); geyser.damageOnEntry = geyser.damageOnOwnerTurn = true; geyser.maximumHpDamageFraction = .25f; Place(catalog, geyser, 6, 4);
            var cave = Definition(TerrainEnvironmentKind.Cave); Place(catalog, cave, 1, 7);
            catalog.placements[catalog.placements.Count - 1].hasCaveExit = true;
            catalog.placements[catalog.placements.Count - 1].caveExit = Cell(10, 10);
            var smoke = Definition(TerrainEnvironmentKind.Smoke); smoke.blocksVision = smoke.concealsOccupants = true; smoke.revealDistance = 0; Place(catalog, smoke, 8, 1);
            map.InitializeEnvironment(catalog);
            Check(map.Environment.Count == 10, "all ten configured environment types installed");
            Check(!map.Environment.HasClearVision(Position(0, 1), Position(2, 1)), "forest hides occupants beyond authored reveal distance");
            Check(map.Environment.HasClearVision(Position(1, 1), Position(2, 1)), "nearby observer can inspect forest");
            Check(!map.Environment.HasClearVision(Position(1, 1), Position(3, 1)), "forest blocks vision behind itself");
            Check(map.Environment.HasClearVision(Position(2, 1), Position(2, 4)), "observer inside forest can look out");
            Check(map.Environment.HasClearVision(Position(4, 3), Position(4, 1)), "lighter bush reveals at configured distance");
            Check(!map.Environment.HasClearVision(Position(4, 4), Position(4, 1)), "bush conceals beyond its configured distance");
            Check(map.Environment.HasClearVision(Position(4, 0), Position(4, 2)), "bush concealment alone does not block view past tile");
            Check(!map.CanTraverse(Position(5, 1), Position(7, 1)), "rock blocks movement through tile");
            Check(!map.CanTraverse(Position(5, 1), Position(6, 2)), "supercover corner cannot cut through rock");
            var actor = Actor(Team.Player, 5, 1);
            actor.kind = Kind.Archer;
            Check(!map.CanAttackAcrossTerrain(actor, Position(7, 1)), "rock blocks shot even for arcing attack");
            Check(!map.Environment.HasClearVision(Position(7, 1), Position(8, 1)), "smoke hides its occupants");
            Check(!map.Environment.HasClearVision(Position(7, 1), Position(9, 1)), "smoke blocks vision through tile");
            Check(map.Environment.MoveAPAdjustment(Position(2, 4), Position(3, 4)) == 2, "mud uses authored movement penalty");
            Check(map.Environment.MoveAPAdjustment(Position(3, 4), Position(4, 4)) == -1, "road uses authored movement reduction");
            Check(map.Environment.MoveAPAdjustment(Position(3, 4), Position(10, 4)) == 0, "normal terrain has no movement adjustment");
            Check(map.Environment.TryCaveExit(Position(1, 7), out var exit) && exit == Position(10, 10), "cave resolves explicit distant exit");
            Check(!MovePatterns.CanMove(actor, actor.direction, 9, 3) && map.IsLegalMove(actor, Position(1, 7), exit), "cave movement is legal outside unit movement mask");
            Check(!map.IsLegalMove(actor, Position(1, 7), Position(6, 1)), "cave does not authorize unrelated destinations");
            Check(!map.Environment.IsCaveConnection(Position(10, 10), Position(1, 7)), "cave links are explicitly directional");
            var systems = new GameSystems { MapCreate = map };
            actor.transform.position = Position(2, 4); actor.HP = 100;
            SpecialSkillRuntime.State(actor).damageNullUnused = true; actor.Special.damageNullTurns = 2;
            map.Environment.OnMoved(actor, Position(1, 4), Position(2, 4), systems);
            Check(actor.HP == 100 && !actor.Special.damageNullUnused, "environment damage follows actual damage-null rule");
            map.Environment.OnMoved(actor, Position(1, 4), Position(2, 4), systems);
            Check(actor.HP == 90, "poison swamp entry applies common HP damage");
            map.Environment.OnOwnerTurnStart(Team.Player, 70000, systems);
            Check(actor.HP == 80, "poison swamp stay damages on owner turn");
            map.Environment.OnOwnerTurnStart(Team.Player, 70000, systems);
            Check(actor.HP == 80, "same owner-turn callback cannot double environmental damage");
            map.Environment.OnOwnerTurnStart(Team.Enemy, 70000, systems);
            Check(actor.HP == 80, "opponent turn cannot damage this faction a second time");
            actor.transform.position = Position(5, 4);
            map.Environment.OnOwnerTurnStart(Team.Player, 70001, systems);
            Check(actor.HP == 90, "healing spring recovers authored maximum-HP fraction");
            map.Environment.OnOwnerTurnStart(Team.Player, 70002, systems);
            map.Environment.OnOwnerTurnStart(Team.Player, 70003, systems);
            Check(actor.HP == 100, "spring cannot exceed maximum HP");
            actor.transform.position = Position(6, 4);
            map.Environment.OnMoved(actor, Position(5, 4), Position(6, 4), systems);
            Check(actor.HP == 75, "geyser uses explicitly configured fraction, not hardcoded quarter HP");
            Check(map.Environment.Ignite(Position(2, 1), systems), "burnable forest can be burned");
            Check(!map.Environment.Ignite(Position(2, 1), systems) && !map.Environment.Ignite(Position(4, 1), systems), "burning is idempotent and respects burnable flag");
            Check(map.Environment.IsConcealing(Position(2, 1)) && map.Environment.DefinitionAt(Position(2, 1)) == null, "burned forest becomes smoke and loses forest rule");
            var obstacle = Actor(Team.Obstacle, 2, 4);
            map.Environment.OnOwnerTurnStart(Team.Obstacle, 70003, systems);
            Check(obstacle.HP == 90, "wild-boss faction uses the same owner-turn environment rules");
            var saved = JsonUtility.FromJson<TerrainEnvironmentRuntime.Snapshot>(JsonUtility.ToJson(map.Environment.Capture()));
            map.Environment.OnOwnerTurnStart(Team.Player, 70006, systems);
            Check(!map.Environment.IsConcealing(Position(2, 1)), "burn smoke expires by rounds, not faction callbacks");
            map.Environment.Restore(saved);
            map.Environment.OnOwnerTurnStart(Team.Obstacle, 70003, systems);
            Check(obstacle.HP == 90, "wild-boss owner-turn stamp survives save load without duplicate damage");
            Check(map.Environment.IsConcealing(Position(2, 1)), "save load restores burned forest smoke duration");
            Check(map.Environment.TryCaveExit(Position(1, 7), out exit) && exit == Position(10, 10), "save load restores cave connection");
            map.Environment.OnOwnerTurnStart(Team.Player, 70003, systems);
            Check(actor.HP == 50, "loaded owner-turn stamp prevents a repeated environmental tick");
            map.Environment.OnOwnerTurnStart(Team.Enemy, 70006, systems);
            Check(!map.Environment.IsConcealing(Position(2, 1)), "loaded weather progression matches original expiry");
            var savedForest = saved.cells.Find(cell => cell.cell == Cell(2, 1)); savedForest.burned = false;
            Check(map.Environment.Capture().cells.Find(cell => cell.cell == Cell(2, 1)).burned, "snapshot mutation does not alias live cells");
            var unresolved = Definition(TerrainEnvironmentKind.Geyser); unresolved.damageOnEntry = true;
            Check(unresolved.maximumHpDamageFraction == -1 && unresolved.ConfigurationError != null, "proposed geyser damage is not invented");
            unresolved.maximumHpDamageFraction = float.NaN;
            Check(unresolved.ConfigurationError != null, "NaN damage fraction is rejected");
            unresolved.maximumHpDamageFraction = float.PositiveInfinity;
            Check(unresolved.ConfigurationError != null, "infinite damage fraction is rejected");
            unresolved.maximumHpDamageFraction = -1; unresolved.maximumHpHealFraction = float.NegativeInfinity;
            Check(unresolved.ConfigurationError != null, "infinite healing fraction is rejected");
            int oldMaximum = actor.MaxHP; actor.MaxHP = int.MaxValue;
            geyser.maximumHpDamageFraction = 1;
            Check(map.Environment.EntryDamage(actor, Position(6, 4)) == int.MaxValue, "maximum-HP fraction calculation cannot overflow");
            spring.maximumHpHealFraction = 1;
            Check(map.Environment.StandingHeal(actor, Position(5, 4)) == int.MaxValue, "maximum-HP healing calculation cannot overflow");
            actor.MaxHP = oldMaximum;
            var unresolvedMud = Definition(TerrainEnvironmentKind.Mud);
            Check(unresolvedMud.ConfigurationError != null, "unknown movement penalty remains unconfigured");
            var unresolvedForest = Definition(TerrainEnvironmentKind.Forest); unresolvedForest.concealsOccupants = true;
            Check(unresolvedForest.ConfigurationError != null, "unknown concealment distance remains unconfigured");
            CheckAI(liveSystems, map);
            catalog.enabledForPlay = false; map.InitializeEnvironment(catalog);
            Check(map.Environment.Count == 0 && map.CanTraverse(Position(5, 1), Position(7, 1)), "disabled catalog preserves original terrain behavior");
            Debug.Log("[TerrainEnvironmentTests] " + checks + " movement/visibility/hazard/cave/save checks passed");
        }
        finally
        {
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }
    }
    static void CheckAI(GameSystems live, MapCreate map)
    {
        const BindingFlags Fields = BindingFlags.NonPublic | BindingFlags.Instance;
        using (var fixture = new EconomyR2Tests.Fixture(live))
        {
            var go = new GameObject("EnvironmentBoardFixture"); owned.Add(go); go.SetActive(false);
            var vision = go.AddComponent<VisionGenerator>();
            var known = new HashSet<Vector3Int>();
            foreach (string name in new[] { "_playerVisionBox", "_enemyVisionBox", "_playerExplored", "_enemyExplored" })
                typeof(VisionGenerator).GetField(name, Fields).SetValue(vision, name.StartsWith("_enemy") ? known : new HashSet<Vector3Int>());
            var moves = go.AddComponent<MoveGenerator>(); moves.mapcreate = map;
            typeof(MoveGenerator).GetField("crystalsystem", Fields).SetValue(moves, fixture.Crystals);
            typeof(MoveGenerator).GetField("PlayerUnit", Fields).SetValue(moves, fixture.Units.PlayerUnit);
            typeof(MoveGenerator).GetField("EnemyUnit", Fields).SetValue(moves, fixture.Units.EnemyUnit);
            var actor = fixture.Unit(); actor.transform.position = Position(1, 7);
            var board = new AIBoardState(moves, live.AttackGenerator, fixture.AP, fixture.Units, fixture.Crystals,
                vision, fixture.Builder, null, fixture.State);
            Check(board.KnownEnvironmentDamage(actor, Position(2, 4), true) == 0 && !board.HasKnownEnvironment,
                "AI cannot read environmental damage or presence outside explored cells");
            known.Add(GridHelper.ToGridXZ(Position(1, 7))); board.Refresh();
            Check(!board.GetValidMoves(actor).Contains(Position(10, 10)), "AI cannot teleport to an unexplored cave exit");
            known.Add(GridHelper.ToGridXZ(Position(10, 10))); known.Add(GridHelper.ToGridXZ(Position(2, 4))); board.Refresh();
            Check(board.GetValidMoves(actor).Contains(Position(10, 10)), "AI generates a known distant cave move");
            Check(board.HasKnownEnvironment && board.KnownEnvironmentDamage(actor, Position(2, 4), true) == 10,
                "AI values authored poison damage after discovery");
            Check(board.KnownEnvironmentRisk(actor, Position(2, 4)) == 20, "royal safety forecast includes both entry and stay danger");
            SpecialSkillRuntime.State(actor).damageNullUnused = true; actor.Special.damageNullTurns = 2;
            Check(board.KnownEnvironmentRisk(actor, Position(2, 4)) == 10, "single damage-null charge is not reused for two hazard packets");
            actor.Special.damageNullUnused = false;
            var movement = new AIAction { Unit = actor, ActionType = AIActionType.Move, TargetPos = Position(2, 4) };
            Check(AIActionEvaluator.CalcEnvironmentAdjustment(movement, board) < 0 && movement.PreserveSpecialEvaluation,
                "AI penalizes known hazard and preserves truthful model instead of legacy overlay-free simulation");
            var blocker = fixture.Unit(); blocker.transform.position = Position(10, 10); board.Refresh();
            Check(!board.GetValidMoves(actor).Contains(Position(10, 10)), "AI refuses an occupied cave exit");
            known.Remove(GridHelper.ToGridXZ(Position(2, 4))); board.Refresh();
            Check(board.KnownEnvironmentDamage(actor, Position(2, 4), true) == 0,
                "AI damage query remains observation-scoped after board refresh");
        }
    }
}
