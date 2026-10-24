#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>Current-turn collision outcomes; never supplies an unseen occupant to AI targeting.</summary>
public static class AITerritoryCollisionTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly MethodInfo Execute = typeof(AICommander).GetMethod("ExecuteReflectedAction", Hidden);
    static readonly MethodInfo Select = typeof(AICommander).GetMethod("SelectBestAction", Hidden);
    static int checks;
    static void Check(string label, bool condition)
    { if (!condition) throw new InvalidOperationException("[AITerritoryCollision] FAIL " + label); checks++; Debug.Log("[AITerritoryCollision] PASS " + label); }
    public static void Run(GameSystems systems, TurnGenerator turn)
    {
        if (!Application.isPlaying || systems?.MapCreate == null || turn == null) throw new InvalidOperationException("A native initialized match is required.");
        checks = 0; Contracts();
        using (BoardActorAnimation.SuppressEffects())
            foreach (var team in new[] { Team.Player, Team.Enemy }) NativeFailure(systems, team);
        Debug.Log("[AITerritoryCollision] ALL PASSED " + checks + " (controlled public collision outcomes)");
    }
    static void Contracts()
    {
        var root = new GameObject("Turn collision contract actors"); root.SetActive(false);
        try
        {
            var a = root.AddComponent<Status>();
            var child = new GameObject("Other actor"); child.transform.SetParent(root.transform); var b = child.AddComponent<Status>();
            var history = new AITurnMovementHistory(); var cell = new Vector3(7, 3, 8);
            AIAction Action(AIActionType type, Status actor = null, Vector3? destination = null)
                => new AIAction { ActionType = type, Unit = actor ?? a, TargetPos = destination ?? cell };
            history.Record(a, Vector3.zero, cell, false);
            Check("an unspecified failure does not invent a blocked destination", history.BlockedDestinationCount == 0 && !history.ShouldReject(Action(AIActionType.Move), null));
            history.RecordBlockedDestination(cell); history.RecordBlockedDestination(cell + Vector3.up * 5);
            Check("a public collision is one unique XZ record", history.BlockedDestinationCount == 1);
            foreach (var type in new[] { AIActionType.Move, AIActionType.Support, AIActionType.Surround, AIActionType.Retreat, AIActionType.DefenseRepos })
                Check(type + " cannot repeat the same collision with another own actor", history.ShouldReject(Action(type, b), null));
            Check("movement outcome does not invent an attack or skill restriction", !history.ShouldReject(Action(AIActionType.Attack), null)
                && !history.ShouldReject(Action(AIActionType.SkillUse), null));
            history.Record(b, cell, cell + Vector3.right, false);
            Check("a failed departure does not clear a blocked cell", history.BlockedDestinationCount == 1);
            history.Record(b, cell, cell + Vector3.right, true);
            Check("a successful own departure invalidates only the vacated cell", history.BlockedDestinationCount == 0 && history.Count == 1);
            history.RecordBlockedDestination(cell); history.InvalidateBlockedDestinations();
            Check("combat invalidation preserves existing anti-oscillation history", history.BlockedDestinationCount == 0 && history.Count == 1);
            history.Clear();
            for (int i = 0; i < 80; i++) history.RecordBlockedDestination(new Vector3(1000 + i, 0, 1000));
            Check("collision knowledge is bounded to 64 requested cells", history.BlockedDestinationCount == 64
                && !history.ShouldReject(Action(AIActionType.Move, b, new Vector3(1000, 0, 1000)), null)
                && history.ShouldReject(Action(AIActionType.Move, b, new Vector3(1079, 0, 1000)), null));
            history.Clear();
            Check("next-turn/release clear removes collision knowledge", history.Count == 0 && history.BlockedDestinationCount == 0
                && !history.ShouldReject(Action(AIActionType.Move, b, new Vector3(1079, 0, 1000)), null));
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    static void NativeFailure(GameSystems live, Team team)
    {
        using var f = new EconomyR2Tests.Fixture(live, team);
        var host = new GameObject("Native current-turn collision hub"); host.SetActive(false);
        var profile = ScriptableObject.CreateInstance<BoardActionProfile>(); AICommander commander = null;
        try
        {
            var cells = Corridor(live.MapCreate);
            f.Rules.aiEconomy.plannedDemandWeight = 0; f.Resources.Citizen = 0; f.Food(100);
            f.Building(f.Definition(FacilityKind.LoggingCamp, wood: 30)); f.Building(f.Definition(FacilityKind.Quarry, stone: 30));
            f.Building(f.Definition(FacilityKind.Mine, iron: 30, magic: 30));
            f.Crystal.transform.position = cells[0]; f.SetOwnCrystalPosition(cells[0]);
            if (team == Team.Player) f.Crystals.ECP = new Vector3(33000, 0, 34000); else f.Crystals.PCP = new Vector3(33000, 0, 34000);
            var actor = f.Unit(); actor.transform.position = cells[1]; actor.ATK = 5000;
            var other = f.Unit(); other.transform.position = cells[3];
            profile.actionType = ActorActionType.Combat; profile.movement.useCustom = profile.attack.useCustom = true;
            profile.movement.directionIndependent = profile.attack.directionIndependent = true;
            foreach (int x in new[] { 2, 4 }) { profile.movement.SetCell(x, 3, true); profile.attack.SetCell(x, 3, true); }
            actor.GrowthData.actionProfile = other.GrowthData.actionProfile = profile;
            var blocker = f.Opponent(cells[2], 0); blocker.HP = 1;
            var hub = host.AddComponent<TurnGenerator>(); hub.MarkDeveloperMatch(); hub.Context.Turn = 60;
            var moves = host.AddComponent<MoveGenerator>(); moves.mapcreate = live.MapCreate; moves.turnGenerator = hub; moves.BuildSystemRef = f.Builder;
            var battle = host.AddComponent<BattleSystem>();
            var skills = host.AddComponent<SkillSystem>(); skills.turnGenerator = hub; skills.battlesystem = battle; skills.moveGenerator = moves; skills.Init(f.State);
            Set(moves, "crystalsystem", f.Crystals); Set(moves, "unitsetting", f.Units); Set(moves, "PlayerUnit", f.Units.PlayerUnit); Set(moves, "EnemyUnit", f.Units.EnemyUnit);
            var vision = host.AddComponent<VisionGenerator>(); var territory = host.AddComponent<TerritorySystem>();
            territory.PTSetPos = new List<Vector3>(); territory.ETSetPos = new List<Vector3>();
            Set(vision, "PlayerUnit", f.Units.PlayerUnit); Set(vision, "EnemyUnit", f.Units.EnemyUnit); Set(vision, "territorysystem", territory);
            var seen = new HashSet<Vector3Int>(cells.Take(2).Select(GridHelper.ToGridXZ)); var explored = new HashSet<Vector3Int>(cells.Select(GridHelper.ToGridXZ));
            foreach (var name in new[] { "_playerVisionBox", "_enemyVisionBox", "_playerExplored", "_enemyExplored" })
                Set(vision, name, name == (team == Team.Player ? "_playerVisionBox" : "_enemyVisionBox") ? seen
                    : name == (team == Team.Player ? "_playerExplored" : "_enemyExplored") ? explored : new HashSet<Vector3Int>());
            hub.Systems.MapCreate = live.MapCreate; hub.Systems.MoveGenerator = moves; hub.Systems.UnitSetting = f.Units;
            hub.Systems.CrystalSystem = f.Crystals; hub.Systems.BuildSystem = f.Builder; hub.Systems.FactionState = f.State;
            hub.Systems.APSystem = f.AP; hub.Systems.BattleSystem = battle; hub.Systems.SkillSystem = skills; hub.Systems.VisionGenerator = vision;
            commander = new AICommander(hub, moves, live.AttackGenerator, battle, vision, f.AP, f.Units, f.Crystals,
                live.MapCreate, MajorPersonality.Combat, f.Builder, null, f.State, skills, null, 15, 73155, team);
            var board = new AIBoardState(moves, live.AttackGenerator, f.AP, f.Units, f.Crystals, vision, f.Builder, null, f.State,
                null, 60, null, team) { MapCreate = live.MapCreate, TerritorySystem = territory, ReconThreatLevel = 15, Governor = commander.StrategicGovernor };
            typeof(AICommander).GetField("_board", Hidden).SetValue(commander, board); board.Refresh(); board.Governor.Evaluate(board);
            var history = (AITurnMovementHistory)typeof(AICommander).GetField("turnMovementHistory", Hidden).GetValue(commander);
            var action = new AIAction { ActionType = AIActionType.Move, Unit = actor, TargetPos = cells[2], APCost = board.CalcMoveCost(actor, cells[2]), Score = 100 };
            Check(team + " hidden occupancy remains a legal unobserved movement proposal", board.GetValidMoves(actor).Contains(cells[2])
                && !board.AlivePlayerUnits.Contains(blocker) && board.GetAttackTargets(actor).Count == 0);
            int ap = f.State.GetAP(team); var before = actor.transform.position;
            bool failed = !(bool)Execute.Invoke(commander, new object[] { action });
            Check(team + " real reflected executor refusal records one cell without AP or movement", failed && history.BlockedDestinationCount == 1
                && f.State.GetAP(team) == ap && actor.transform.position == before && !board.AlivePlayerUnits.Contains(blocker));
            var proposals = new List<AIAction>(); AIActionGenerator.GenerateMoveCandidates(actor, board, proposals); AIActionGenerator.GenerateMoveCandidates(other, board, proposals);
            foreach (var type in new[] { AIActionType.Support, AIActionType.Surround, AIActionType.Retreat, AIActionType.DefenseRepos })
                proposals.Add(new AIAction { ActionType = type, Unit = other, TargetPos = cells[2], Score = 500, APCost = 3 });
            foreach (var proposal in proposals) proposal.Score = proposal.TargetPos == cells[2] ? 500 : 1;
            var chosen = (AIAction)Select.Invoke(commander, new object[] { proposals, new HashSet<string>(), null });
            Check(team + " selection rejects every alias/actor retry before another executor query", chosen != null && chosen.TargetPos != cells[2]
                && history.BlockedDestinationCount == 1 && f.State.GetAP(team) == ap && actor.transform.position == before);
            var hiddenAttack = new AIAction { ActionType = AIActionType.Attack, Unit = actor, TargetUnit = blocker, TargetPos = cells[2] };
            Check(team + " failed hidden attack cannot clear collision knowledge or invent visibility", !(bool)Execute.Invoke(commander, new object[] { hiddenAttack })
                && history.BlockedDestinationCount == 1 && !board.AlivePlayerUnits.Contains(blocker) && f.State.GetAP(team) == ap);
            seen.Add(GridHelper.ToGridXZ(cells[2])); board.Refresh();
            var attack = new AIAction { ActionType = AIActionType.Attack, Unit = actor, TargetUnit = blocker, TargetPos = cells[2], APCost = board.CalcAttackCost(actor) };
            Check(team + " successful legal observed attack invalidates transient collision outcome", (bool)Execute.Invoke(commander, new object[] { attack })
                && history.BlockedDestinationCount == 0 && blocker.HP <= 0);
            // A subsequent genuine occupancy refusal also exercises successful skill invalidation.
            var blocker2 = f.Opponent(cells[4], 0); board.Refresh();
            var move2 = new AIAction { ActionType = AIActionType.Move, Unit = other, TargetPos = cells[4], APCost = board.CalcMoveCost(other, cells[4]) };
            Check(team + " another actual occupancy refusal is remembered independently", !(bool)Execute.Invoke(commander, new object[] { move2 }) && history.BlockedDestinationCount == 1);
            var skill = SkillData.Table.Values.First(s => s.Target == SkillTarget.Self && s.GrantBuff != BuffType.None && s.APCost > 0);
            actor.AssignedSkillId = skill.Id; actor.SkillCooldown = 0;
            var use = new AIAction { ActionType = AIActionType.SkillUse, Unit = actor, TargetUnit = actor, TargetPos = actor.transform.position,
                Skill = skill, APCost = board.CalcSkillCost(actor, skill) };
            Check(team + " successful real skill invalidates potentially changed occupancy", (bool)Execute.Invoke(commander, new object[] { use }) && history.BlockedDestinationCount == 0);
            Debug.Log("[AITerritoryCollisionNative] team=" + team + " firstCell=" + GridHelper.ToGridXZ(cells[2])
                + " firstFailureSpentAP=0 duplicateAliasAttempts=0 observedAttackClear=True skillClear=True");
        }
        finally { commander?.ReleaseRuntimeReferences(); UnityEngine.Object.DestroyImmediate(profile); UnityEngine.Object.DestroyImmediate(host); }
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
        throw new InvalidOperationException("Collision fixture needs six connected flat safe cells.");
    }
}
#endif
