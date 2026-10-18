#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Both actual faction perspectives: known owned blockers, fog-safe hostile checks and forced-only structure rules.</summary>
public static class R1AIOwnWallLegalityTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const int Origin = 61000;
    static int checks;
    static Vector3 At(int x, int z) => new Vector3(Origin + x, 1, Origin + z);
    static void Check(bool value, string label)
    { if (!value) throw new InvalidOperationException("[R1OwnWall] FAIL " + label); checks++; }
    static bool Contains(List<Vector3> cells, Vector3 cell) => cells.Exists(value => GridHelper.MatchXZ(value, GridHelper.ToGridXZ(cell)));

    public static void PlayMode(GameSystems live)
    {
        checks = 0;
        foreach (var team in new[] { Team.Player, Team.Enemy }) Faction(live, team);
        Debug.Log("[R1OwnWall] " + checks + " owned wall/normal building/fog/forced-swap checks passed");
    }

    static void Faction(GameSystems live, Team team)
    {
        using (var f = new EconomyR2Tests.Fixture(live, team))
        {
            var originalSystems = live.TurnGenerator.Systems;
            var root = new GameObject("R1 owned-wall controlled map"); root.SetActive(false);
            SpecialSkillDefinition swap = null;
            try
            {
                var map = root.AddComponent<MapCreate>(); map.UseR1Terrain = false; map.SetPos = new List<Vector3>();
                for (int x = 0; x < 9; x++) for (int z = 0; z < 9; z++) map.SetPos.Add(At(x, z));
                var moves = root.AddComponent<MoveGenerator>(); moves.mapcreate = map; moves.BuildSystemRef = f.Builder;
                foreach (var item in new[] { ("PlayerUnit", (object)f.Units.PlayerUnit), ("EnemyUnit", (object)f.Units.EnemyUnit), ("crystalsystem", (object)f.Crystals) })
                    typeof(MoveGenerator).GetField(item.Item1, Private).SetValue(moves, item.Item2);
                var attacks = root.AddComponent<AttackGenerator>(); attacks.mapcreate = map; attacks.moveGenerator = moves;
                var vision = root.AddComponent<VisionGenerator>(); var visible = new HashSet<Vector3Int>();
                foreach (string field in new[] { "_playerVisionBox", "_enemyVisionBox", "_playerExplored", "_enemyExplored" })
                    typeof(VisionGenerator).GetField(field, Private).SetValue(vision,
                        field.StartsWith(team == Team.Player ? "_player" : "_enemy") ? visible : new HashSet<Vector3Int>());
                f.AP.Init(f.State, map);
                if (team == Team.Player) { f.Crystals.PCP = At(7, 4); f.Crystals.ECP = At(8, 8); }
                else { f.Crystals.ECP = At(7, 4); f.Crystals.PCP = At(8, 8); }
                f.Crystal.transform.position = At(7, 4); f.Crystal.HP = 1;
                var systems = new GameSystems { MapCreate = map, MoveGenerator = moves, AttackGenerator = attacks,
                    APSystem = f.AP, FactionState = f.State, UnitSetting = f.Units, CrystalSystem = f.Crystals, BuildSystem = f.Builder };
                // The controlled observation component is read by the board. Execution refresh has no live-scene vision side effects.
                typeof(GameSystems).GetProperty("TurnGenerator").SetValue(systems, live.TurnGenerator);
                typeof(TurnGenerator).GetProperty("Systems").SetValue(live.TurnGenerator, systems);
                AIBoardState Board() => new AIBoardState(moves, attacks, f.AP, f.Units, f.Crystals, vision,
                    f.Builder, null, f.State, null, 20, null, team) { MapCreate = map, SpecialSystems = systems };
                var executor = new AIActionExecutor(live.TurnGenerator, moves, attacks, live.BattleSystem, f.AP,
                    live.SkillSystem, null, f.Builder, null, new AILearning(false), team);
                var actor = f.Unit(); actor.kind = Kind.Boss; actor.transform.position = At(3, 4); actor.HP = 30;
                var weak = f.Unit(); weak.transform.position = At(5, 4); weak.HP = 20;
                var wall = f.Building(f.Definition(FacilityKind.WoodWall)); wall.type = Type.Wall; wall.transform.position = At(4, 4);
                var board = Board();
                SpecialSkillRuntime.State(actor).ritualTargetId = "fixture-target";
                Check(board.GetValidMoves(actor).Count == 0, team + " ritual channels block all shared movement candidates");
                SpecialSkillRuntime.State(actor).ritualTargetId = null;
                SpecialSkillRuntime.State(actor).holyKnightTurns = 2;
                Check(board.GetValidMoves(actor).Count == 0, team + " active sanctuary blocks all shared movement candidates");
                SpecialSkillRuntime.State(actor).holyKnightTurns = 0;
                SpecialSkillRuntime.State(actor).holyKnightRecoveryLock = true;
                Check(board.GetValidMoves(actor).Count == 0, team + " sanctuary recovery blocks all shared movement candidates");
                SpecialSkillRuntime.State(actor).holyKnightRecoveryLock = false;
                Check(!Contains(board.GetValidMoves(actor), wall.transform.position), team + " owned wall is excluded outside current vision");
                var candidates = new List<AIAction>();
                AIActionGenerator.GenerateMoveCandidates(actor, board, candidates);
                AIActionGenerator.GenerateSupportCandidates(actor, board, candidates);
                AIActionGenerator.GenerateRetreatCandidates(actor, board, candidates);
                AIActionGenerator.GenerateDefenseReposCandidates(actor, board, candidates);
                foreach (var type in new[] { AIActionType.Move, AIActionType.Support, AIActionType.Retreat, AIActionType.DefenseRepos })
                    Check(candidates.Exists(action => action.ActionType == type)
                        && !candidates.Exists(action => action.ActionType == type && GridHelper.MatchXZ(action.TargetPos, wall.GridPosition)),
                        team + " " + type + " uses the same owned-wall snapshot");
                int ap = f.State.GetAP(team);
                Check(!executor.Execute(new AIAction { ActionType = AIActionType.Move, Unit = actor, TargetPos = wall.transform.position }, board)
                    && f.State.GetAP(team) == ap, team + " authoritative owned-wall move also rejects without payment");

                var hostileRoot = new GameObject("Hidden hostile wall"); hostileRoot.transform.SetParent(f.Builder.GetBuildingParent(team == Team.Player ? Team.Enemy : Team.Player));
                var hostile = hostileRoot.AddComponent<Status>(); hostile.team = team == Team.Player ? Team.Enemy : Team.Player;
                hostile.type = Type.Wall; hostile.kind = Kind.WoodWall; hostile.HP = hostile.MaxHP = 100; hostile.transform.position = At(2, 4);
                board.Refresh();
                Check(Contains(board.GetValidMoves(actor), hostile.transform.position), team + " hidden hostile wall is not leaked into candidates");
                ap = f.State.GetAP(team);
                Check(!executor.Execute(new AIAction { ActionType = AIActionType.Move, Unit = actor, TargetPos = hostile.transform.position }, board)
                    && f.State.GetAP(team) == ap, team + " hidden occupancy can legitimately reject at execution");
                visible.Add(hostile.GridPosition); board.Refresh();
                Check(!Contains(board.GetValidMoves(actor), hostile.transform.position), team + " currently observed hostile wall is excluded");
                hostile.HP = 0; wall.HP = 0; board.Refresh();
                Check(Contains(board.GetValidMoves(actor), wall.transform.position), team + " destroyed owned wall leaves the next snapshot");
                Check(executor.Execute(new AIAction { ActionType = AIActionType.Move, Unit = actor, TargetPos = wall.transform.position }, board),
                    team + " actual Boss can enter the cleared cell");

                var house = f.Building(f.Definition(FacilityKind.House)); house.transform.position = At(3, 3);
                board.Refresh();
                Check(Contains(board.GetValidMoves(actor), house.transform.position), team + " normal movement still permits non-wall buildings");
                actor.transform.position = house.transform.position;
                var ninja = f.Unit(); ninja.kind = Kind.Ninja; ninja.transform.position = At(1, 1);
                swap = ScriptableObject.CreateInstance<SpecialSkillDefinition>(); swap.ability = UnitSpecialSkill.NinjaSwap;
                swap.enabledForPlay = true; swap.apCost = 13; swap.cooldown = 0; swap.ninjaChargeCurve = AnimationCurve.Linear(0, 1, 5, 2);
                ninja.GrowthData.specialSkill = swap; f.State.SetAP(team, 40); board.Refresh();
                Check(SpecialSkillRuntime.IsLegalTarget(ninja, actor, out _), team + " owned Boss remains a valid allied swap target");
                candidates.Clear(); AIActionGenerator.GenerateSpecialSkillCandidates(ninja, board, candidates);
                Check(!candidates.Exists(action => action.TargetUnit == actor), team + " swap target on owned building is prefiltered");
                ap = f.State.GetAP(team);
                Check(!SpecialSkillRuntime.TryExecute(new SpecialSkillCommand(ninja, actor), systems, out _)
                    && f.State.GetAP(team) == ap, team + " forced destination independently rejects the occupied target cell");
                house.HP = 0; board.Refresh(); candidates.Clear(); AIActionGenerator.GenerateSpecialSkillCandidates(ninja, board, candidates);
                Check(candidates.Exists(action => action.TargetUnit == actor), team + " cleared target building restores swap candidate");
                house.HP = 100; house.transform.position = ninja.transform.position; board.Refresh();
                candidates.Clear(); AIActionGenerator.GenerateSpecialSkillCandidates(ninja, board, candidates);
                Check(candidates.Count == 0, team + " swap caster on an owned structure is also prefiltered");
                house.HP = 0; board.Refresh();
                var from = ninja.transform.position; var to = actor.transform.position;
                Check(SpecialSkillRuntime.TryExecute(new SpecialSkillCommand(ninja, actor), systems, out _)
                    && GridHelper.MatchXZ(ninja.transform.position, GridHelper.ToGridXZ(to)) && GridHelper.MatchXZ(actor.transform.position, GridHelper.ToGridXZ(from)),
                    team + " actual swap succeeds after both owned structure blockers are cleared");
            }
            finally
            {
                typeof(TurnGenerator).GetProperty("Systems").SetValue(live.TurnGenerator, originalSystems);
                if (swap != null) UnityEngine.Object.DestroyImmediate(swap);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
#endif
