#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Run after explicit R1 installation. Real recipes and masks must survive live/search recruitment.</summary>
public static class R1ProductionAIRecruitmentTests
{
    static readonly Kind[] NewKinds = { Kind.WhiteMage, Kind.HolyKnight, Kind.Samurai, Kind.Ninja, Kind.Berserker, Kind.Navigator, Kind.HeavyWarrior, Kind.DragonKnight };
    static readonly Kind[] BaseKinds = { Kind.Priest, Kind.Knight, Kind.Knight, Kind.Assassin, Kind.Knight, Kind.Guardian, Kind.Knight, Kind.Knight };
    static readonly string[] Roles = { "Healer", "Infantry", "Infantry", "Ranged", "Infantry", "Guardian", "Infantry", "Infantry" };
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static void Check(bool value, string label)
    { if (!value) throw new InvalidOperationException("[R1ProductionAI] FAIL " + label); checks++; }

    public static void Run()
    {
        checks = 0;
        var definitions = new Dictionary<Kind, UnitData>();
        var catalog = UnitAuthoringCatalog.Load();
        Check(catalog != null, "installed catalog exists");
        catalog.Apply(definitions, null);
        Check(UnitRoster.SummonableKinds.Length == 18 && new HashSet<Kind>(UnitRoster.SummonableKinds).Count == 18,
            "live UI and AI roster contains eighteen unique kinds");
        Check(ReferenceEquals(BuildSummonUIBuilder.SummonableKinds, UnitRoster.SummonableKinds), "UI shares common roster");
        var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<R1ProductionContentSettings>(R1ProductionContentInstaller.SettingsPath);
        Check(settings != null && settings.installed.Count >= 16 && settings.installed.Count <= 18, "sixteen established roles and only explicitly installed barrier roles");
        foreach (var record in settings.installed)
        {
            var recipe = new SimRecruitmentDefinition(record.data);
            Check(recipe.SkillId == -1 && recipe.Passive == PassiveSkill.None,
                "fixed R1 ability never falls back to legacy skill/passive " + record.kind);
        }
        for (int i = 0; i < NewKinds.Length; i++)
        {
            Kind kind = NewKinds[i];
            if (i >= 6 && !settings.installed.Exists(record => record != null && record.kind == kind))
            {
                Check(!definitions.TryGetValue(kind,out var draft) || !draft.AvailableFor(Team.Player) && !draft.AvailableFor(Team.Enemy),
                    "unapproved role remains unavailable " + kind);
                continue;
            }
            Check(Array.IndexOf(UnitRoster.SummonableKinds, kind) >= 0 && definitions.TryGetValue(kind, out _), "registered " + kind);
            var data = definitions[kind];
            Check(data.IsValidForAuthoring && data.AvailableFor(Team.Player) && data.AvailableFor(Team.Enemy), "valid both-faction recipe " + kind);
            Check(AIConstants.GetPieceValue(kind) == AIConstants.GetPieceValue(BaseKinds[i]), "inherited valuation " + kind);
            Check(AIActionFeatureExtractor.Role(kind) == Roles[i], "evaluation role " + kind);
            Check(AIPlayerModel.IsRanged(kind) == (kind == Kind.Ninja), "ranged classification " + kind);
            foreach (var team in new[] { Team.Enemy, Team.Player }) Simulate(data, team);
        }
        Check(UnitRoster.IsDedicatedHealer(Kind.WhiteMage) && !UnitRoster.IsDedicatedHealer(Kind.HolyKnight),
            "dedicated healer and frontline sanctuary remain distinct roles");
        Debug.Log("[R1ProductionAI] " + checks + " production recruitment/recipe/mask checks passed");
    }

    static SimBoardState Board(UnitData data)
    {
        var board = new SimBoardState
        {
            Units = new List<SimUnit>(), MapTiles = new HashSet<Vector3Int>(), EnemyAP = 60, PlayerAP = 60,
            EnemyCrystalPos = Vector3Int.zero, PlayerCrystalPos = new Vector3Int(30, 0, 30),
            EnemyBuildingCounts = new Dictionary<FacilityKind, int>(), PlayerBuildingCounts = new Dictionary<FacilityKind, int>()
        };
        for (int x = -6; x <= 6; x++) for (int z = -6; z <= 6; z++) board.MapTiles.Add(new Vector3Int(x, 0, z));
        // Both test teams use the same controlled legal area.
        board.PlayerCrystalPos = Vector3Int.zero;
        typeof(SimBoardState).GetField("RecruitmentDefinitions", Private).SetValue(board,
            new Dictionary<Kind, SimRecruitmentDefinition> { { data.kind, new SimRecruitmentDefinition(data) } });
        typeof(SimBoardState).GetField("RecruitmentKinds", Private).SetValue(board, new[] { data.kind });
        board.RebuildOccupied();
        return board;
    }

    static void Simulate(UnitData data, Team team)
    {
        var board = Board(data); SimBoardState copy = null;
        bool hadStatic = UnitStaticData.Table.TryGetValue(data.kind, out var oldStatic);
        try
        {
            // A stale static table entry must never override the formal recipe.
            UnitStaticData.Table[data.kind] = new UnitStaticData.UnitInfo { BaseHP = 1, BaseATK = 1, BaseDEF = 1, CostAP = data.costAP + 9 };
            var actions = SimActionGenerator.GenerateAllActions(board, team);
            var summon = actions.Find(a => a.Type == SimActionType.Summon && a.SummonKind == data.kind);
            Check(summon != null && summon.APCost == data.costAP, team + " formal AP overrides static " + data.kind);
            copy = board.Clone();
            Check(copy.TryGetRecruitment(data.kind, out var recipe) && recipe.ActionProfile == data.actionProfile
                && recipe.APCost == data.costAP, team + " clone preserves recipe " + data.kind);
            int ap = copy.GetAP(team);
            Check(copy.ApplyAction(summon), team + " summon applies " + data.kind);
            var unit = copy.Units[copy.Units.Count - 1];
            Check(unit.Kind == data.kind && unit.Team == team && unit.MaxHP == data.baseHP && unit.HP == data.baseHP
                && unit.ATK == data.baseATK && unit.DEF == data.baseDEF && copy.GetAP(team) == ap - data.costAP,
                team + " exact summon stats and AP " + data.kind);
            Check(unit.ActionProfile == data.actionProfile, team + " exact summon mask " + data.kind);
            int expectedBarrier=BarrierSystem.UsesBarrier(data.kind,Type.Unit)
                ? BarrierSystem.Maximum(data.baseBarrier>=0?data.baseBarrier:BarrierSystem.Initial(data.kind),1):0;
            Check(unit.MaxBarrier==expectedBarrier&&unit.Barrier==expectedBarrier,team+" exact Lv1 summon barrier "+data.kind);
            var after = SimActionGenerator.GenerateAllActions(copy, team);
            bool moves = false, legal = true;
            foreach (var action in after)
                if (action.Type == SimActionType.Move && action.UnitId == unit.Id)
                {
                    moves = true;
                    legal &= MovePatterns.CanMove(data.kind, unit.Direction, action.TargetPos.x - unit.Position.x,
                        action.TargetPos.z - unit.Position.z, data.actionProfile);
                }
            Check(moves && legal, team + " move candidates use authored mask " + data.kind);
            var target = new SimUnit { Id = copy.NextUnitId(), Team = team == Team.Enemy ? Team.Player : Team.Enemy,
                Kind = Kind.Knight, Type = Type.Unit, HP = 1000, MaxHP = 1000, AssignedSkillId = -1 };
            bool found = false;
            foreach (var cell in copy.MapTiles)
                if (!copy.IsOccupied(cell) && AttackPatterns.CanAttack(data.kind, unit.Direction,
                    cell.x - unit.Position.x, cell.z - unit.Position.z, data.actionProfile))
                { target.Position = cell; found = true; break; }
            Check(found, team + " attack mask has reachable cell " + data.kind);
            copy.Units.Add(target); copy.RebuildOccupied();
            after = SimActionGenerator.GenerateAllActions(copy, team);
            Check(after.Exists(a => a.Type == SimActionType.Attack && a.UnitId == unit.Id && a.TargetUnitId == target.Id),
                team + " attack candidates use authored mask " + data.kind);
            if (data.costAP > 0)
            {
                board.EnemyAP = board.PlayerAP = data.costAP - 1;
                Check(!SimActionGenerator.GenerateAllActions(board, team).Exists(a => a.Type == SimActionType.Summon),
                    team + " unaffordable recipe excluded " + data.kind);
            }
            var restricted = UnityEngine.Object.Instantiate(data);
            try
            {
                restricted.availableToPlayer = true; restricted.availableToEnemy = false;
                var oneFaction = new SimRecruitmentDefinition(restricted);
                board.RealActorTeam = Team.Player;
                Check(board.RecruitmentAvailable(oneFaction, Team.Enemy) && !board.RecruitmentAvailable(oneFaction, Team.Player),
                    "player AI recruitment ownership " + data.kind);
            }
            finally { UnityEngine.Object.DestroyImmediate(restricted); }
        }
        finally
        {
            if (hadStatic) UnitStaticData.Table[data.kind] = oldStatic; else UnitStaticData.Table.Remove(data.kind);
            if (copy != null) SimBoardPool.ReturnBoard(copy);
            SimBoardPool.ReturnBoard(board);
        }
    }

    /// <summary>Also exercise the real AI executor in an initialized scene; restores factions and removes every test spawn.</summary>
    public static void PlayMode(GameSystems live)
    {
        checks = 0;
        foreach (var team in new[] { Team.Player, Team.Enemy })
        {
            var resources = live.FactionState.GetResources(team);
            string oldResources = JsonUtility.ToJson(resources); int oldAP = live.FactionState.GetAP(team);
            var spawned = new List<Status>();
            try
            {
                foreach (var kind in NewKinds)
                {
                    if ((kind == Kind.HeavyWarrior || kind == Kind.DragonKnight)
                        && (!live.UnitSetting.UnitDataMap.TryGetValue(kind,out var draft) || !draft.AvailableFor(team))) continue;
                    resources.Wood = resources.Stone = resources.Iron = resources.MagicOre = resources.Water = resources.Bread = resources.Citizen = 100000;
                    live.FactionState.SetAP(team, 60);
                    var board = new AIBoardState(live.MoveGenerator, live.AttackGenerator, live.APSystem, live.UnitSetting,
                        live.CrystalSystem, live.VisionGenerator, live.BuildSystem, live.SummonSystem, live.FactionState,
                        null, 20, null, team);
                    var candidates = new List<AIAction>(); AIActionGenerator.GenerateSummonCandidates(board, candidates);
                    var action = candidates.Find(a => a.SummonKind == kind);
                    var data = live.UnitSetting.UnitDataMap[kind];
                    Check(board.AffordableUnits.Contains(kind) && action != null && action.SummonDefinition == data && action.APCost == data.costAP,
                        team + " live AI uses installed definition " + kind);
                    var parent = team == Team.Player ? live.UnitSetting.PlayerUnit : live.UnitSetting.EnemyUnit;
                    var before = new HashSet<Status>(parent.GetComponentsInChildren<Status>());
                    var executor = new AIActionExecutor(live.TurnGenerator, live.MoveGenerator, live.AttackGenerator,
                        live.BattleSystem, live.APSystem, live.SkillSystem, null, live.BuildSystem, live.SummonSystem, null, team);
                    // SetAP obeys this faction's current Maximum; a new game need not have a 60 AP cap.
                    int apBefore = live.FactionState.GetAP(team);
                    int wood = resources.Wood, stone = resources.Stone, iron = resources.Iron, magic = resources.MagicOre,
                        water = resources.Water, bread = resources.Bread, citizen = resources.Citizen;
                    Check(executor.ExecuteSummon(action, board), team + " real AI summon succeeds " + kind);
                    Status actor = null;
                    foreach (var value in parent.GetComponentsInChildren<Status>())
                        if (!before.Contains(value)) { spawned.Add(value); actor = value; }
                    string label = team + " " + kind;
                    Check(actor != null, label + " spawned actor exists");
                    Check(actor.kind == kind && actor.team == team && actor.GrowthData == data,
                        label + " identity: actual=" + actor.kind + "/" + actor.team + " definition=" + (actor.GrowthData == data));
                    Check(actor.Level == 1 && actor.MaxHP == data.baseHP && actor.ATK == data.baseATK && actor.DEF == data.baseDEF,
                        label + " Lv1 stats: actual=" + actor.Level + "/" + actor.MaxHP + "/" + actor.ATK + "/" + actor.DEF
                        + " expected=1/" + data.baseHP + "/" + data.baseATK + "/" + data.baseDEF);
                    Check(BoardActionProfile.For(actor) == data.actionProfile, label + " exact installed action mask");
                    Check(SpecialSkillRuntime.Definition(actor) == data.specialSkill, label + " exact installed ability");
                    Check(live.FactionState.GetAP(team) == apBefore - data.costAP,
                        label + " AP: before=" + apBefore + " cost=" + data.costAP + " after=" + live.FactionState.GetAP(team));
                    Check(resources.Wood == wood - data.costWood, label + " Wood delta=" + (wood - resources.Wood) + " expected=" + data.costWood);
                    Check(resources.Stone == stone - data.costStone, label + " Stone delta=" + (stone - resources.Stone) + " expected=" + data.costStone);
                    Check(resources.Iron == iron - data.costIron, label + " Iron delta=" + (iron - resources.Iron) + " expected=" + data.costIron);
                    Check(resources.MagicOre == magic - data.costMagic, label + " MagicOre delta=" + (magic - resources.MagicOre) + " expected=" + data.costMagic);
                    Check(resources.Water == water - data.costWater, label + " Water delta=" + (water - resources.Water) + " expected=" + data.costWater);
                    Check(resources.Bread == bread - data.costBread, label + " Bread delta=" + (bread - resources.Bread) + " expected=" + data.costBread);
                    Check(resources.Citizen == citizen - data.costCitizen, label + " Citizen delta=" + (citizen - resources.Citizen) + " expected=" + data.costCitizen);
                    Debug.Log("[R1ProductionAI] " + label + " AP=" + apBefore + "->" + live.FactionState.GetAP(team)
                        + " cost=" + data.costAP + " stats=" + actor.MaxHP + "/" + actor.ATK + "/" + actor.DEF + " exact recipe/mask/ability passed");
                    // Isolate the role/query classification from unrelated live support units.
                    board.AliveEnemyUnits.Clear(); board.AliveEnemyUnits.Add(actor); board.Allies.Rebuild(board.AliveEnemyUnits);
                    var roles = new AIRoleAssigner();
                    roles.AssignRoles(board, TurnStrategy.Balanced, new AIPersonality(MajorPersonality.Growth, 1701));
                    var expectedRole = kind == Kind.WhiteMage ? UnitRole.Support : kind == Kind.Ninja ? UnitRole.Striker : UnitRole.Vanguard;
                    Check(roles.GetRole(actor) == expectedRole, team + " live tactical role " + kind);
                    bool healing = kind == Kind.WhiteMage || kind == Kind.HolyKnight;
                    Check(board.HasHealerInRange(actor.transform.position, 0) == healing, team + " special healer query " + kind);
                    Check(ReferenceEquals(AISpecialSkillKnowledge.For(actor).Definition, data.specialSkill), team + " shared ability knowledge " + kind);
                }
            }
            finally
            {
                foreach (var actor in spawned)
                    if (actor != null)
                    {
                        UnitRegistry.Instance?.Unregister(actor);
                        var parent = team == Team.Player ? live.UnitSetting.PlayerUnit : live.UnitSetting.EnemyUnit;
                        var root = actor.transform;
                        while (root.parent != null && root.parent != parent) root = root.parent;
                        UnityEngine.Object.DestroyImmediate(root.gameObject);
                    }
                JsonUtility.FromJsonOverwrite(oldResources, resources); live.FactionState.SetAP(team, oldAP);
                live.MoveGenerator.UnitPointCore(); live.RefreshVision();
            }
        }
        Debug.Log("[R1ProductionAI] " + checks + " real both-faction recruitment checks passed");
    }
}
#endif
