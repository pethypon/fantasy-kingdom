#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Controlled subsystem Play Mode test on real terrain, with real legal
/// movement and AP payment. This is not an ordinary campaign or victory test.</summary>
[InitializeOnLoad]
public static class SquadMarchValidation
{
    const string Key = "FK.SquadMarch";
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static bool clicked;
    static int frame;
    static SquadMarchValidation() => EditorApplication.update += Tick;
    public static void Run()
    {
        if (new DirectoryInfo(Path.GetDirectoryName(Application.dataPath)).Name != "UnityValidation") throw new InvalidOperationException("Isolated clone required");
        PlayerSettings.companyName = "CodexValidation"; PlayerSettings.productName = "SquadMarch_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        SessionState.SetBool(Key, true); EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity"); EditorApplication.EnterPlaymode();
    }
    static void Check(string name, bool ok)
    { if (!ok) throw new InvalidOperationException("[SquadMarch] FAIL " + name); Debug.Log("[SquadMarch] PASS " + name); }
    static void Set(object owner, string field, object value) => owner.GetType().GetField(field, Hidden).SetValue(owner, value);
    static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (!clicked)
            {
                var button = UIBuilder.ScreenCanvas?.transform.Find("TitleScreenOverlay/ButtonArea/はじめからBtn")?.GetComponent<Button>();
                if (button == null) return;
                UnityEngine.Random.InitState(161803); button.onClick.Invoke(); clicked = true; frame = Time.frameCount; return;
            }
            var turn = UnityEngine.Object.FindFirstObjectByType<TurnGenerator>();
            if (turn == null || Time.frameCount - frame < 20) return;
            SessionState.SetBool(Key, false);
            foreach (var side in new[] { Team.Player, Team.Enemy }) { March(turn.Systems, side); FocusFire(turn.Systems, side); }
            CrystalGrowthValidation.Run(turn.Systems);
            Debug.Log("[SquadMarch] COMPLETE real Play Mode frames=" + (Time.frameCount - frame)); EditorApplication.Exit(0);
        }
        catch (Exception e) { SessionState.SetBool(Key, false); Debug.LogException(e); EditorApplication.Exit(1); }
    }

    static void March(GameSystems live, Team side)
    {
        using var effects = BoardActorAnimation.SuppressEffects();
        using var f = new R2ConstructionEconomyTests.Fixture(live, side);
        var config = ScriptableObject.CreateInstance<AIOperationConfig>();
        var actor = f.Engineer; actor.kind = Kind.Knight; actor.GrowthData.kind = Kind.Knight;
        actor.ATK = 20; actor.HP = actor.MaxHP = 100; actor.direction = Direction.N;
        var profile = actor.GrowthData.actionProfile; profile.actionType = ActorActionType.All;
        profile.movement.useCustom = profile.movement.directionIndependent = true;
        profile.attack.useCustom = profile.attack.directionIndependent = true;
        for (int x = 2; x <= 4; x++) for (int z = 2; z <= 4; z++)
            if (x != 3 || z != 3) { profile.movement.SetCell(x, z, true); profile.attack.SetCell(x, z, true); }
        var path = FindPath(actor, live.MapCreate, f.Sites[0]);
        var goal = path[path.Count - 1];
        try
        {
            var board = f.Board();
            var known = new HashSet<Vector3Int>(path);
            string prefix = side + " ";
            void RestoreKnowledge()
            {
                Set(f.Vision, side == Team.Player ? "_playerExplored" : "_enemyExplored", new HashSet<Vector3Int>(known));
                Set(f.Vision, side == Team.Player ? "_playerVisionBox" : "_enemyVisionBox", new HashSet<Vector3Int>(known));
            }
            RestoreKnowledge(); board.Refresh();
            var manager = new AIOperationManager(config); manager.BeginBattle(side, "march-test-" + side);
            board.Operations = manager;
            var plan = new AIOperationPlan { OperationId = 1, PrimaryGoal = AIOperationGoal.LocateEnemyMainForce,
                OriginalGoal = AIOperationGoal.LocateEnemyMainForce, Status = AIOperationStatus.Active, StrategicPriority = 3,
                StartTurn = 1, ExpectedEndTurn = 8, MaximumEndTurn = 15, LastProgressTurn = 1, IsPrimary = true,
                HasTargetPosition = true, TargetX = goal.x, TargetZ = goal.z, TargetCategory = "Territory",
                AssignedUnitLifeIds = new List<string> { actor.ReflectionLifeId },
                OriginalAssignedUnitLifeIds = new List<string> { actor.ReflectionLifeId },
                Steps = new List<AIOperationStep> { new AIOperationStep { StepId = 1, Type = AIOperationStepType.Approach } } };
            manager.State.NextOperationId = 1; manager.State.ActiveOperations.Add(plan);
            var executor = new AIActionExecutor(f.Turn, f.Moves, live.AttackGenerator, live.BattleSystem,
                f.AP, null, null, f.Builder, null, new AILearning(false), side);
            float initialDistance = GridHelper.ChebyshevDistance(actor.transform.position, goal);
            int moved = 0;
            for (int ownTurn = 1; ownTurn <= 3; ownTurn++)
            {
                AITurnBudget.Begin(5000); actor.HasMovedThisTurn = false; actor.Fatigue = 0; f.State.SetAP(side, 40);
                RestoreKnowledge(); board.Refresh(); manager.BeginTurn(board, TurnStrategy.ScoutSearch, ownTurn);
                Check(prefix + "retained-operation-turn-" + ownTurn, manager.State.ActiveOperations.Contains(plan) && plan.OperationId == 1);
                Check(prefix + "formation-owned-by-operation-turn-" + ownTurn, manager.TryGetSquad(actor.ReflectionLifeId, out var squad) && squad.OperationId == plan.OperationId);
                var member = squad.Members.Find(item => item.ActorLifeId == actor.ReflectionLifeId);
                Check(prefix + "route-produces-slot-turn-" + ownTurn, member != null && member.HasSlot && member.MarchSlot);
                live.MapCreate.TryGetHeight(member.SlotX, member.SlotZ, out float height);
                var destination = new Vector3(member.SlotX, height, member.SlotZ);
                var action = new AIAction { Unit = actor, ActionType = AIActionType.Move, TargetPos = destination, APCost = board.CalcMoveCost(actor, destination) };
                int beforeAP = f.State.GetAP(side);
                Vector3 origin = actor.transform.position;
                Check(prefix + "existing-legal-command-turn-" + ownTurn, board.GetValidMoves(actor).Contains(destination) && manager.IsPlannedMarch(action, board));
                Check(prefix + "official-executor-turn-" + ownTurn, executor.Execute(action, board));
                Check(prefix + "exact-AP-payment-turn-" + ownTurn, f.State.GetAP(side) == beforeAP - action.APCost && actor.transform.position == destination);
                moved++; board.Refresh();
                Check(prefix + "old-slot-invalid-after-generation-turn-" + ownTurn, !manager.IsPlannedMarch(action, board));
                manager.Refresh(board, TurnStrategy.Balanced);
                var reverse = new AIAction { Unit = actor, ActionType = AIActionType.Move, TargetPos = origin,
                    APCost = board.CalcMoveCost(actor, origin) };
                if (GridHelper.ChebyshevDistance(origin, goal) > GridHelper.ChebyshevDistance(actor.transform.position, goal))
                    Check(prefix + "healthy-march-not-undone-by-later-fallback-turn-" + ownTurn, manager.ReversesHealthyMarch(reverse, board));
                manager.EndTurn(board, ownTurn);
            }
            Check(prefix + "multi-turn-real-displacement", moved == 3 && GridHelper.ChebyshevDistance(actor.transform.position, goal) < initialDistance);
            var saved = JsonUtility.FromJson<AIOperationBattleState>(JsonUtility.ToJson(manager.State));
            var resumed = new AIOperationManager(config); resumed.BeginBattle(side, "march-test-" + side, saved);
            Check(prefix + "operation-and-squad-survive-save", resumed.State.ActiveOperations.Count == 1 && resumed.State.Squads.Count == 1
                && resumed.State.Squads[0].OperationId == 1);
            board.Governor = new AIStrategicGovernor(); board.Refresh(); manager.Refresh(board, TurnStrategy.CrystalDefense);
            bool falseDefense = false;
            foreach (var active in manager.State.ActiveOperations) falseDefense |= active.PrimaryGoal == AIOperationGoal.DefendOwnCrystal;
            Check(prefix + "fallback-label-does-not-invent-defense-emergency", board.Governor.Mode != StrategicMode.EmergencyDefense && !falseDefense);
            board.Governor = null;
            AITurnBudget.Begin(5000); board.Refresh(); manager.Refresh(board, TurnStrategy.CrystalDefense);
            Check(prefix + "emergency-suspends-retained-operation", plan.Status == AIOperationStatus.Suspended);
            Check(prefix + "same-turn-emergency-releases-and-reassigns-actor", manager.TryGetSquad(actor.ReflectionLifeId, out var emergencySquad)
                && emergencySquad.OperationId != plan.OperationId);
            // Controlled end of the temporary defense order. No enemy is marked
            // dead or objective credited by this test setup.
            foreach (var active in manager.State.ActiveOperations)
                if (active.OperationId != plan.OperationId) active.Status = AIOperationStatus.Aborted;
            board.Refresh(); manager.Refresh(board, TurnStrategy.Balanced);
            Check(prefix + "resume-retains-original-plan-and-member", plan.Status == AIOperationStatus.Active
                && manager.TryGetSquad(actor.ReflectionLifeId, out var originalSquad) && originalSquad.OperationId == plan.OperationId);
            for (int index = 0; index < 2; index++)
            {
                var go = new GameObject("controlled-reinforcement-" + index); go.SetActive(false);
                go.transform.SetParent(actor.transform.parent); go.transform.position = f.Sites[index + 2];
                var reinforcement = go.AddComponent<Status>(); reinforcement.kind = Kind.Knight; reinforcement.type = Type.Unit;
                reinforcement.team = side; reinforcement.GrowthData = actor.GrowthData;
                reinforcement.HP = reinforcement.MaxHP = 100; reinforcement.ATK = 20;
                reinforcement.RestoreReflectionLifeId(Guid.NewGuid().ToString("N")); go.SetActive(true);
            }
            for (int refresh = 0; refresh < 2; refresh++)
            {
                AITurnBudget.Begin(5000); board.Refresh(); manager.Refresh(board, TurnStrategy.Balanced);
                Check(prefix + "reinforcement-reassignment-counts-distinct-actors-" + refresh,
                    plan.AssignedUnitLifeIds.Count == 3 && new HashSet<string>(plan.AssignedUnitLifeIds).Count == 3);
            }
            Status healer = null;
            foreach (var candidate in (side == Team.Player ? f.Units.PlayerUnit : f.Units.EnemyUnit).GetComponentsInChildren<Status>())
                if (candidate != actor && candidate.team == side && candidate.type == Type.Unit) { healer = candidate; break; }
            Check(prefix + "production-white-mage-definition-exists", live.UnitSetting.UnitDataMap.TryGetValue(Kind.WhiteMage, out var healerData));
            healer.GrowthData = healerData; healer.kind = Kind.WhiteMage; healerData.ApplyToStatus(healer, 1);
            board.Refresh(); manager.Refresh(board, TurnStrategy.Balanced);
            Check(prefix + "healer-recognizes-its-operation-patient", manager.SupportsSquadPatient(healer, actor));
            Check(prefix + "non-healer-cannot-claim-support-priority", !manager.SupportsSquadPatient(actor, healer));
            // Controlled four-member deployment. The whole generated map is
            // explicitly known in this fixture; it is not a Fog/campaign result.
            var fourthGo = new GameObject("controlled-fourth-member"); fourthGo.SetActive(false);
            fourthGo.transform.SetParent(actor.transform.parent); fourthGo.transform.position = path[0];
            var fourth = fourthGo.AddComponent<Status>(); fourth.kind = Kind.Knight; fourth.type = Type.Unit;
            fourth.team = side; fourth.GrowthData = actor.GrowthData; fourth.HP = fourth.MaxHP = 100;
            fourth.ATK = 20; fourth.RestoreReflectionLifeId(Guid.NewGuid().ToString("N")); fourthGo.SetActive(true);
            plan.AssignedUnitLifeIds.Add(fourth.ReflectionLifeId); plan.OriginalAssignedUnitLifeIds.Add(fourth.ReflectionLifeId);
            healer.transform.position = path[2]; actor.HP = 60;
            f.Turn.Systems.UnitSetting = f.Units; f.Turn.Systems.APSystem = f.AP; f.Turn.Systems.MoveGenerator = f.Moves;
            f.Turn.Systems.BattleSystem = live.BattleSystem; f.Turn.Systems.AttackGenerator = live.AttackGenerator;
            board.SpecialSystems = f.Turn.Systems;
            foreach (var member in actor.transform.parent.GetComponentsInChildren<Status>()) member.HasMovedThisTurn = false;
            f.State.SetAP(side, 40); f.Vision.MarkVisionDirty(); f.Vision.VisionPoint(live.MapCreate, f.Moves, f.Crystals);
            known.Clear(); foreach (var cell in live.MapCreate.SetPos) known.Add(GridHelper.ToGrid(cell));
            RestoreKnowledge(); AITurnBudget.Begin(5000); board.Refresh(); manager.Refresh(board, TurnStrategy.Balanced);
            var support = new List<AIAction>(); AIActionGenerator.GenerateSpecialSkillCandidates(healer, board, support);
            var healAction = support.Find(a => a.TargetUnit == actor);
            Check(prefix + "four-member-order-generates-real-heal", healAction != null && plan.AssignedUnitLifeIds.Count == 4);
            var supportExecutor = new AIActionExecutor(f.Turn, f.Moves, live.AttackGenerator, live.BattleSystem,
                f.AP, null, null, f.Builder, null, new AILearning(false), side);
            int beforeHP = actor.HP, supportAP = f.State.GetAP(side);
            Check(prefix + "support-heal-executes-with-real-AP", supportExecutor.Execute(healAction, board)
                && actor.HP > beforeHP && f.State.GetAP(side) == supportAP - healAction.APCost);
            int membersMoved = 0;
            foreach (var member in actor.transform.parent.GetComponentsInChildren<Status>())
            {
                AITurnBudget.Begin(5000); board.Refresh(); manager.Refresh(board, TurnStrategy.Balanced);
                var commands = new List<AIAction>(); AIActionGenerator.GenerateMoveCandidates(member, board, commands);
                var move = commands.Find(a => manager.IsPlannedMarch(a, board));
                if (move == null) continue;
                var from = member.transform.position; supportAP = f.State.GetAP(side);
                Check(prefix + "four-member-real-command-" + membersMoved, supportExecutor.Execute(move, board)
                    && member.transform.position != from && f.State.GetAP(side) == supportAP - move.APCost);
                membersMoved++;
            }
            Check(prefix + "at-least-three-members-execute-shared-deployment", membersMoved >= 3);
        }
        finally { UnityEngine.Object.DestroyImmediate(config); }
    }

    static List<Vector3Int> FindPath(Status actor, MapCreate map, Vector3Int start)
    {
        var queue = new Queue<Vector3Int>(); var parent = new Dictionary<Vector3Int, Vector3Int>();
        var depth = new Dictionary<Vector3Int, int>(); queue.Enqueue(start); depth[start] = 0;
        while (queue.Count > 0)
        {
            var from = queue.Dequeue();
            if (depth[from] >= 7)
            {
                var path = new List<Vector3Int> { from };
                while (parent.TryGetValue(from, out var previous)) { path.Add(previous); from = previous; }
                path.Reverse(); return path;
            }
            foreach (var offset in MovePatterns.Offsets(actor))
            {
                int x = from.x + offset.x, z = from.z + offset.y;
                if (!map.TryGetHeight(x, z, out float y)) continue;
                var next = new Vector3Int(x, Mathf.RoundToInt(y), z);
                if (depth.ContainsKey(next) || !map.IsLegalMove(actor, from, next)) continue;
                parent[next] = from; depth[next] = depth[from] + 1; queue.Enqueue(next);
            }
        }
        throw new InvalidOperationException("Controlled march requires a legal seven-step corridor in the real map");
    }

    static void FocusFire(GameSystems live, Team side)
    {
        using var effects = BoardActorAnimation.SuppressEffects();
        using var f = new R2ConstructionEconomyTests.Fixture(live, side);
        string prefix = side + " focus-fire ";
        var actors = new List<Status> { f.Engineer };
        for (int index = 0; index < 2; index++)
        {
            var go = new GameObject("controlled-fire-partner"); go.SetActive(false); go.transform.SetParent(f.Engineer.transform.parent);
            var actor = go.AddComponent<Status>(); actor.team = side; actor.type = Type.Unit;
            actor.RestoreReflectionLifeId(Guid.NewGuid().ToString("N")); actors.Add(actor);
        }
        for (int index = 0; index < actors.Count; index++)
        {
            var actor = actors[index]; actor.kind = index < 2 ? Kind.Knight : Kind.Archer;
            var definition = live.UnitSetting.UnitDataMap[actor.kind]; actor.GrowthData = definition;
            definition.ApplyToStatus(actor, 1); actor.HP = actor.MaxHP;
            actor.gameObject.SetActive(true);
        }
        Vector3Int targetCell = default; bool positioned = false;
        foreach (var point in live.MapCreate.SetPos)
        {
            if (live.MapCreate.IsRiver(Mathf.RoundToInt(point.x), Mathf.RoundToInt(point.z))
                || live.MapCreate.IsHighMountain(Mathf.RoundToInt(point.x), Mathf.RoundToInt(point.z))
                || live.MapCreate.Environment.BlocksMovement(Mathf.RoundToInt(point.x), Mathf.RoundToInt(point.z))) continue;
            var reserved = new HashSet<Vector3Int>(); bool complete = true;
            foreach (var actor in actors)
            {
                bool found = false; int reach = Mathf.Clamp(AICapabilityRoles.For(actor).MaximumAttackReach, 1, 7);
                for (int dx = -reach; dx <= reach && !found; dx++) for (int dz = -reach; dz <= reach && !found; dz++)
                {
                    int x = Mathf.RoundToInt(point.x) + dx, z = Mathf.RoundToInt(point.z) + dz;
                    var key = new Vector3Int(x, 0, z);
                    if (dx == 0 && dz == 0 || reserved.Contains(key) || !live.MapCreate.TryGetHeight(x, z, out float height)
                        || live.MapCreate.IsRiver(x, z) || live.MapCreate.IsHighMountain(x, z)
                        || live.MapCreate.Environment.BlocksMovement(x, z)) continue;
                    actor.transform.position = new Vector3(x, height, z); actor.direction = point.z >= z ? Direction.N : Direction.S;
                    if (!AttackPatterns.CanAttack(actor, actor.direction, point.x - x, point.z - z)
                        || !live.MapCreate.CanAttackAcrossTerrain(actor, point)) continue;
                    reserved.Add(key); found = true;
                }
                if (!found) { complete = false; break; }
            }
            if (complete) { targetCell = GridHelper.ToGrid(point); positioned = true; break; }
        }
        Check(prefix + "three-distinct-real-firing-positions-exist", positioned);
        var opponent = side == Team.Player ? Team.Enemy : Team.Player;
        f.Territory.GetTerritory(opponent).Add(targetCell);
        var target = f.Builder.PlaceBuildingForLoad(targetCell, FacilityKind.Mine, opponent);
        // Authored full-health 30HP target for two production Knights and an
        // Archer. This tests coordination, not a campaign finishing position.
        f.Mine.levels[0].HP = 30; target.AuthoredFacility = f.Mine;
        target.HP = target.MaxHP = 30; target.Barrier = 0;
        f.Turn.Systems.APSystem = f.AP; f.Turn.Systems.MoveGenerator = f.Moves;
        f.Turn.Systems.UnitSetting = f.Units; f.Turn.Systems.BuildSystem = f.Builder;
        f.Turn.Systems.BattleSystem = live.BattleSystem; f.Turn.Systems.AttackGenerator = live.AttackGenerator;
        f.Units.UnitDataMap[Kind.Knight] = live.UnitSetting.UnitDataMap[Kind.Knight];
        f.Units.UnitDataMap[Kind.Archer] = live.UnitSetting.UnitDataMap[Kind.Archer];
        var board = f.Board(); board.ReconThreatLevel = 15; board.SpecialSystems = f.Turn.Systems;
        f.Vision.MarkVisionDirty(); f.Vision.VisionPoint(live.MapCreate, f.Moves, f.Crystals); board.Refresh();
        var manager = new AIOperationManager(); manager.BeginBattle(side, "formal-fire-" + side); board.Operations = manager;
        // This contract gives the tactical layer an already issued, observed
        // attack order. Strategic target discovery is tested separately; no
        // target witness or attack-range cache is injected here.
        var order = new AIOperationPlan { OperationId = 1, PrimaryGoal = AIOperationGoal.BreakEnemyDefense,
            OriginalGoal = AIOperationGoal.BreakEnemyDefense, Status = AIOperationStatus.Active, IsPrimary = true,
            StrategicPriority = 3, StartTurn = 1, LastProgressTurn = 1, ExpectedEndTurn = 8, MaximumEndTurn = 15,
            TargetLifeId = target.ReflectionLifeId, TargetCategory = "Building", HasTargetPosition = true,
            TargetX = targetCell.x, TargetZ = targetCell.z,
            Steps = new List<AIOperationStep> { new AIOperationStep { StepId = 1, Type = AIOperationStepType.Attack } } };
        foreach (var actor in actors) { order.AssignedUnitLifeIds.Add(actor.ReflectionLifeId); order.OriginalAssignedUnitLifeIds.Add(actor.ReflectionLifeId); }
        manager.State.NextOperationId = 1; manager.State.ActiveOperations.Add(order);
        board.RemainingPlannedActionSlots = 8; AITurnBudget.Begin(5000);
        // A full-health durable, unarmed facility cannot grant every chip a
        // battle-only tier forever while a paid recruitment/deployment order waits.
        f.Mine.levels[0].HP = target.HP = target.MaxHP = 1000;
        board.Refresh(); manager.BeginTurn(board, TurnStrategy.ContactEngage, 1);
        var pressure = new List<AIAction>(); AIActionGenerator.GenerateAttackCandidates(actors[0], board, pressure);
        var pressureAttack = pressure.Find(a => a.TargetUnit == target && a.Unit == actors[0]);
        Check(prefix + "durable-facility-has-an-official-attack", pressureAttack != null);
        Check(prefix + "nonfinishing-passive-pressure-shares-recruitment-tier",
            AIActionEvaluator.IsNonFinishingPassiveStructureAttack(pressureAttack, board)
            && manager.SquadActionPriority(pressureAttack, board) == 3);
        var governor = new AIStrategicGovernor();
        Set(governor.TerritoryIntel, "<HasWaypoint>k__BackingField", true);
        Set(governor, "<Mode>k__BackingField", StrategicMode.StrategicAttack);
        var priority = typeof(AIStrategicGovernor).GetMethod("Priority", Hidden);
        board.Governor = governor; board.Operations = null;
        Check(prefix + "generic-territory-battle-gate-also-preserves-recruitment-tier",
            governor.HasActiveTerritoryAdvance && (int)priority.Invoke(governor, new object[] { pressureAttack, board }) == 3);
        board.Governor = null; board.Operations = manager;
        target.ATK = 20;
        Check(prefix + "armed-defense-retains-battle-priority",
            !AIActionEvaluator.IsNonFinishingPassiveStructureAttack(pressureAttack, board)
            && manager.SquadActionPriority(pressureAttack, board) == 2);
        var originalKind = target.kind;
        target.ATK = 0; target.kind = Kind.Crystal;
        Check(prefix + "main-crystal-retains-existing-priority",
            !AIActionEvaluator.IsNonFinishingPassiveStructureAttack(pressureAttack, board));
        target.kind = Kind.SubCrystal;
        Check(prefix + "durable-subcrystal-is-not-a-permanent-urgent-battle",
            AIActionEvaluator.IsNonFinishingPassiveStructureAttack(pressureAttack, board));
        target.kind = Kind.Knight; target.type = Type.Unit;
        Check(prefix + "hostile-unit-is-never-a-passive-structure",
            !AIActionEvaluator.IsNonFinishingPassiveStructureAttack(pressureAttack, board));
        target.kind = originalKind; target.type = Type.Building;
        f.Mine.levels[0].HP = target.HP = target.MaxHP = 30;
        board.Refresh(); manager.Refresh(board, TurnStrategy.ContactEngage);
        Check(prefix + "attack-order-has-three-distinct-live-members", order.AssignedUnitLifeIds.Count == 3
            && new HashSet<string>(order.AssignedUnitLifeIds).Count == 3 && manager.Squads.Count > 0);
        var executor = new AIActionExecutor(f.Turn, f.Moves, live.AttackGenerator, live.BattleSystem,
            f.AP, null, null, f.Builder, null, new AILearning(false), side);
        var candidates = new List<AIAction>(); var participants = new HashSet<string>(); bool planned = false;
        for (int step = 0; step < 5 && target.IsAlive; step++)
        {
            AITurnBudget.Begin(5000); board.Refresh(); manager.Refresh(board, TurnStrategy.ContactEngage); candidates.Clear();
            foreach (var actor in actors) AIActionGenerator.GenerateAttackCandidates(actor, board, candidates);
            candidates.RemoveAll(action => action.TargetUnit != target);
            Check(prefix + "official-observed-target-candidates-" + step, candidates.Count > 0);
            if (step == 0) Check(prefix + "all-three-normal-units-have-formal-attack-candidates", candidates.Count == 3);
            manager.UpdateTacticalCoordination(candidates, board);
            AIAction selected = candidates.Find(action => manager.IsCoordinatedKillHead(action, board));
            if (selected != null) planned = true;
            else foreach (var action in candidates) if (selected == null || action.APCost < selected.APCost) selected = action;
            int before = f.State.GetAP(side), hp = target.HP;
            Check(prefix + "official-executor-" + step, executor.Execute(selected, board));
            Check(prefix + "exact-AP-and-real-damage-" + step, f.State.GetAP(side) == before - selected.APCost && target.HP < hp);
            participants.Add(selected.Unit.ReflectionLifeId);
        }
        Check(prefix + "coordinator-formed-an-affordable-multi-actor-kill", planned);
        Check(prefix + "real-multi-actor-kill-without-target-witness-injection", !target.IsAlive && participants.Count >= 2);
    }
}
#endif
