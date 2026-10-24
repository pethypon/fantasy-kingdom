#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// R2 T14 and the observed-base portion of T16. These controlled boards are not
/// normal-match evidence and do not certify scouting, retreat or recruitment saves.
/// </summary>
public static class AIR2ObjectiveTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static void Check(string label, bool condition)
    {
        if (!condition) throw new InvalidOperationException("[AIR2Objective] FAIL " + label);
        checks++; Debug.Log("[AIR2Objective] PASS " + label);
    }

    public static void Run(GameSystems live)
    {
        if (!Application.isPlaying || live?.MapCreate == null) throw new InvalidOperationException("A started native game is required.");
        checks = 0;
        string nations = JsonUtility.ToJson(live.FactionState.PlayerNation) + JsonUtility.ToJson(live.FactionState.EnemyNation);
        var random = UnityEngine.Random.state;
        using (BoardActorAnimation.SuppressEffects())
            foreach (var team in new[] { Team.Player, Team.Enemy })
                foreach (var kind in new[] { Kind.King, Kind.Crystal }) Verify(live, team, kind);
        Check("controlled objective checks preserve both live nations", nations == JsonUtility.ToJson(live.FactionState.PlayerNation) + JsonUtility.ToJson(live.FactionState.EnemyNation));
        Check("controlled objective checks preserve gameplay random state", random.Equals(UnityEngine.Random.state));
        Debug.Log("[AIR2Objective] ALL PASSED " + checks);
    }

    static void Verify(GameSystems live, Team team, Kind kind)
    {
        using var f = new EconomyR2Tests.Fixture(live, team);
        var host = new GameObject("R2 isolated objective services"); host.SetActive(false);
        var profile = ScriptableObject.CreateInstance<BoardActionProfile>();
        var config = ScriptableObject.CreateInstance<AIOperationConfig>();
        config.EnablePersistentLearning = false; config.MinimumBaseAssaultScore = 0;
        try
        {
            var hub = host.AddComponent<TurnGenerator>(); hub.MarkDeveloperMatch();
            var vision = host.AddComponent<VisionGenerator>();
            var battle = host.AddComponent<BattleSystem>();
            var attacks = host.AddComponent<AttackGenerator>();
            var attackPoints = new GameObject("R2 isolated attack points"); attackPoints.transform.SetParent(host.transform);
            attacks.mapcreate = live.MapCreate; attacks.moveGenerator = live.MoveGenerator; attacks.APparent = attackPoints.transform;
            foreach (string field in new[] { "_playerVisionBox", "_enemyVisionBox", "_playerExplored", "_enemyExplored" })
                typeof(VisionGenerator).GetField(field, Hidden).SetValue(vision, new HashSet<Vector3Int>());
            typeof(VisionGenerator).GetField("PlayerUnit", Hidden).SetValue(vision, f.Units.PlayerUnit);
            typeof(VisionGenerator).GetField("EnemyUnit", Hidden).SetValue(vision, f.Units.EnemyUnit);
            typeof(VisionGenerator).GetField("territorysystem", Hidden).SetValue(vision, live.TerritorySystem);
            var seen = new HashSet<Vector3Int>(); var explored = new HashSet<Vector3Int>();
            string prefix = team == Team.Player ? "_player" : "_enemy";
            typeof(VisionGenerator).GetField(prefix + "VisionBox", Hidden).SetValue(vision, seen);
            typeof(VisionGenerator).GetField(prefix + "Explored", Hidden).SetValue(vision, explored);
            hub.Systems.MapCreate = live.MapCreate; hub.Systems.MoveGenerator = live.MoveGenerator; hub.Systems.AttackGenerator = attacks;
            hub.Systems.UnitSetting = f.Units; hub.Systems.FactionState = f.State; hub.Systems.APSystem = f.AP;
            hub.Systems.CrystalSystem = f.Crystals; hub.Systems.VisionGenerator = vision; hub.Systems.BattleSystem = battle;
            var actor = f.Unit(); actor.Level = 1; actor.ATK = 20; actor.DEF = 0;
            actor.HP = actor.MaxHP = 100; actor.GrowthData.actionProfile = profile;
            profile.actionType = ActorActionType.Stationary; profile.attack.useCustom = true;
            profile.attack.directionIndependent = true; profile.attack.SetCell(profile.attack.originX + 1, profile.attack.originZ, true);
            var cells = AttackPair(live.MapCreate, actor);
            var target = f.Opponent(cells[1], 0); target.kind = kind; target.DEF = 0; target.HP = target.MaxHP = 1000;
            if (kind == Kind.Crystal)
            {
                target.type = Type.Building;
                target.transform.SetParent(target.team == Team.Player ? f.Crystals.Playercrystal : f.Crystals.Enemycrystal);
                if (target.team == Team.Player) f.Crystals.PCP = target.transform.position; else f.Crystals.ECP = target.transform.position;
            }
            f.Food(30); f.Building(f.Definition(FacilityKind.LoggingCamp, wood: 30)); f.Building(f.Definition(FacilityKind.Quarry, stone: 30));
            f.Building(f.Definition(FacilityKind.Mine, iron: 30, magic: 30));
            var board = new AIBoardState(live.MoveGenerator, attacks, f.AP, f.Units, f.Crystals, vision, f.Builder,
                null, f.State, null, 60, null, team) { MapCreate = live.MapCreate, ReconThreatLevel = 15, Governor = new AIStrategicGovernor() };
            board.Governor.Evaluate(board);
            string label = team + " " + kind;
            var candidates = new List<AIAction>();
            AIActionGenerator.GenerateAttackCandidates(actor, board, candidates);
            Check(label + " unobserved decisive target generates no attack", candidates.Count == 0 && !board.IsSpecialActorObserved(target));
            seen.Add(GridHelper.ToGridXZ(cells[0])); seen.Add(GridHelper.ToGridXZ(cells[1]));
            explored.UnionWith(seen); board.Refresh(); board.Governor.Evaluate(board);
            Check(label + " fixture is a legally observed decisive target", MatchObjectiveRules.IsDecisive(target)
                && board.IsSpecialActorObserved(target) && live.MapCreate.CanAttackAcrossTerrain(actor, target.transform.position));
            CrisisAuthority(live, f, board, target, seen, label);
            candidates.Clear(); AIActionGenerator.GenerateAttackCandidates(actor, board, candidates);
            Check(label + " T14 normal generator produces the legal objective attack", candidates.Count == 1 && candidates[0].TargetUnit == target
                && candidates[0].APCost == board.CalcAttackCost(actor));
            var commander = new AICommander(hub, live.MoveGenerator, attacks, battle, vision, f.AP, f.Units, f.Crystals,
                live.MapCreate, MajorPersonality.Combat, f.Builder, null, f.State, null, null, 15, 24014, team);
            typeof(AICommander).GetField("_board", Hidden).SetValue(commander, board);
            // Use production scoring and selection against waiting. This intentionally does
            // not claim that every strategic economy/retreat alternative was simulated.
            candidates.Add(new AIAction { ActionType = AIActionType.Wait, Unit = actor, APCost = 0 });
            board.Governor.Filter(candidates, board);
            foreach (var action in candidates) action.Score = AIActionEvaluator.CalcBuildScorePublic(action, commander.Personality, board, commander.Learning);
            var selected = (AIAction)typeof(AICommander).GetMethod("SelectBestAction", Hidden).Invoke(commander,
                new object[] { candidates, new HashSet<string>(), null });
            Check(label + " T14 standard selection chooses objective attack over waiting", selected?.ActionType == AIActionType.Attack && selected.TargetUnit == target);
            int hp = target.HP, ap = f.State.GetAP(team); string stocks = JsonUtility.ToJson(f.Resources);
            var executor = new AIActionExecutor(hub, live.MoveGenerator, attacks, battle, f.AP, null, null, f.Builder, null, new AILearning(false), team);
            Check(label + " T14 real executor applies damage and exact attack AP", executor.Execute(selected, board)
                && target.HP < hp && target.HP > 0 && f.State.GetAP(team) == ap - selected.APCost
                && JsonUtility.ToJson(f.Resources) == stocks);
            // Zero AP and visibility loss independently remove ordinary target opportunities.
            f.State.SetAP(team, 0); board.Refresh(); candidates.Clear(); AIActionGenerator.GenerateAttackCandidates(actor, board, candidates);
            Check(label + " zero AP never produces a paid objective attack", candidates.Count == 0);
            f.State.SetAP(team, 40); seen.Clear(); board.Refresh(); candidates.Clear(); AIActionGenerator.GenerateAttackCandidates(actor, board, candidates);
            Check(label + " remembered position never becomes a hidden attack", candidates.Count == 0 && !board.IsSpecialActorObserved(target));
            if (kind == Kind.Crystal) BaseSave(live, f, hub, vision, attacks, battle, board, target, seen, config, label);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(host); UnityEngine.Object.DestroyImmediate(profile); UnityEngine.Object.DestroyImmediate(config);
        }
    }

    static void CrisisAuthority(GameSystems live, EconomyR2Tests.Fixture f, AIBoardState board, Status objective,
        HashSet<Vector3Int> seen, string label)
    {
        // A visible but immobile non-attacker near home used to erase the whole
        // offensive intent even though the governor saw no legal incoming damage.
        var bystander = f.Opponent(f.Crystal.transform.position + Vector3.right, 0);
        bystander.GrowthData.actionProfile.attack.cells = 0;
        bystander.AssignedSkillId = -1;
        seen.Add(GridHelper.ToGridXZ(bystander.transform.position)); board.Refresh(); board.Governor.Evaluate(board);
        var policy = new AIEvolutionPolicy(); policy.BeginTurn(board);
        Check(label + " harmless observed proximity keeps the governor-approved objective", board.Governor.Mode != StrategicMode.EmergencyDefense
            && board.Governor.IncomingDamage(board, f.Crystal, f.Crystal.transform.position, null) == 0
            && policy.HasObjective && policy.Objective == objective.transform.position);
        var governor = board.Governor; board.Governor = null;
        try
        {
            policy = new AIEvolutionPolicy(); policy.BeginTurn(board);
            Check(label + " legacy callers without a governor retain their proximity fallback", !policy.HasObjective);
        }
        finally { board.Governor = governor; bystander.gameObject.SetActive(false); board.Refresh(); board.Governor.Evaluate(board); }
    }

    static Vector3[] AttackPair(MapCreate map, Status actor)
    {
        foreach (var cell in map.SetPos)
            if (map.TryGetHeight(Mathf.RoundToInt(cell.x) + 1, Mathf.RoundToInt(cell.z), out float height)
                && Mathf.Abs(height - cell.y) < .01f)
            {
                actor.transform.position = cell;
                var adjacent = cell + Vector3.right;
                if (map.CanAttackAcrossTerrain(actor, adjacent)) return new[] { cell, adjacent };
            }
        throw new InvalidOperationException("R2 objective fixture requires one real, legal adjacent attack lane.");
    }

    static void BaseSave(GameSystems live, EconomyR2Tests.Fixture f, TurnGenerator hub, VisionGenerator vision,
        AttackGenerator attacks, BattleSystem battle, AIBoardState board, Status target, HashSet<Vector3Int> seen, AIOperationConfig config, string label)
    {
        seen.Add(GridHelper.ToGridXZ(target.transform.position)); board.Refresh(); board.Governor.Evaluate(board);
        var manager = new AIOperationManager(config); manager.BeginBattle(f.Team, "R2_T16_" + f.Team); manager.BeginTurn(board, TurnStrategy.Balanced, 1);
        Check(label + " observed main base enters value-only operation memory", manager.KnownEnemyBases.Count == 1
            && manager.KnownEnemyBases[0].StableTargetId == target.ReflectionLifeId);
        int rememberedX = manager.KnownEnemyBases[0].X, rememberedZ = manager.KnownEnemyBases[0].Z;
        float rememberedHP = manager.KnownEnemyBases[0].LastKnownHpRatio;
        seen.Clear(); board.Refresh(); manager.Refresh(board, TurnStrategy.Balanced);
        var commander = new AICommander(hub, live.MoveGenerator, attacks, battle, vision, f.AP, f.Units, f.Crystals,
            live.MapCreate, MajorPersonality.Combat, f.Builder, null, f.State, null, null, 15, 24016, f.Team);
        typeof(AICommander).GetField("operations", Hidden).SetValue(commander, manager);
        string ap = JsonUtility.ToJson(f.State.GetAPData(f.Team)), resources = JsonUtility.ToJson(f.Resources);
        var saved = new SaveSystem.AISaveData();
        typeof(SaveSystem).GetMethod("CollectAI", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { saved, commander });
        saved = JsonUtility.FromJson<SaveSystem.AISaveData>(JsonUtility.ToJson(saved));
        target.transform.position += Vector3.right * 10000; target.HP = 1;
        var restored = new AICommander(hub, live.MoveGenerator, attacks, battle, vision, f.AP, f.Units, f.Crystals,
            live.MapCreate, MajorPersonality.Combat, f.Builder, null, f.State, null, null, 15, 24016, f.Team);
        SaveSystem.RestoreAICommander(saved, restored);
        var bases = restored.Operations?.KnownEnemyBases;
        Check(label + " T16 base-memory portion survives actual AI save/restore without hidden refresh", bases != null && bases.Count == 1
            && bases[0].X == rememberedX && bases[0].Z == rememberedZ && Mathf.Approximately(bases[0].LastKnownHpRatio, rememberedHP)
            && !bases[0].CurrentlyVisible && !bases[0].ConfirmedDestroyed);
        Check(label + " AI save restoration does not spend AP or resources", ap == JsonUtility.ToJson(f.State.GetAPData(f.Team)) && resources == JsonUtility.ToJson(f.Resources));
    }
}
#endif
