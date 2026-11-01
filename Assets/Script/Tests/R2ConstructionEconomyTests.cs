#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Explicit native contracts and paid commands. No map generation or live nation mutation.</summary>
public static class R2ConstructionEconomyTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static int passed;
    static void Check(string id, bool value)
    {
        if (!value) throw new InvalidOperationException("[R2ConstructionEconomy] FAIL " + id);
        passed++; Debug.Log("[R2ConstructionEconomy] PASS " + id);
    }
    public static void Run(GameSystems live)
    {
        if (live?.MapCreate == null) throw new ArgumentNullException(nameof(live));
        passed = 0; var random = UnityEngine.Random.state;
        int mapX = live.MapCreate.maxX, mapZ = live.MapCreate.maxZ;
        try
        {
            CostsAndProduction();
            foreach (var team in new[] { Team.Player, Team.Enemy })
            {
                using var f = new Fixture(live, team);
                EngineerCommands(f);
                UnpaidMaintenance(f);
            }
            Check("MN14 map dimensions and generated map are untouched", live.MapCreate.maxX == mapX && live.MapCreate.maxZ == mapZ);
        }
        finally { UnityEngine.Random.state = random; }
        Debug.Log("[R2ConstructionEconomy] RESULT checks=" + passed);
    }
    public static void PlayMode(GameSystems live) => Run(live);
    static void CostsAndProduction()
    {
        var cost = R2EconomyRules.EngineerCost(new FacilityData.ResourceCost(70, 110, 3, 5, 7, 1));
        Check("construction costs are ordinary after engineer retirement", cost.Wood == 70 && cost.Stone == 110 && cost.Iron == 3 && cost.MagicOre == 5 && cost.Water == 7);
        Check("EN09 provisional ordinary AP and citizens preserved", cost.Citizen == 1 && R2EconomyRules.EngineerAP(3) == 3);
        Check("EN08 saturated integer prices do not overflow", R2EconomyRules.DoubleNonNegative(int.MaxValue) == int.MaxValue);
        Check("UP03 maximumHP100 loses20", R2EconomyRules.UnpaidDamage(100, 100) == 20);
        Check("UP04 maximumHP175 loses35", R2EconomyRules.UnpaidDamage(175, 175) == 35);
        Check("UP05 maximumHP7 rounds up2", R2EconomyRules.UnpaidDamage(7, 7) == 2);
        Check("UP05 oneHP losesone without negativeHP", R2EconomyRules.UnpaidDamage(1, 1) == 1 && R2EconomyRules.UnpaidDamage(0, 100) == 0);
        var host = new GameObject("R2 position production fixture");
        try
        {
            var territory = host.AddComponent<TerritorySystem>();
            territory.PTSetPos = new List<Vector3> { new Vector3(2, 9, 3) };
            territory.ETSetPos = new List<Vector3> { new Vector3(4, 0, 5) };
            var recipe = new FacilityData.FacilityLevelData
            {
                Input = new FacilityData.ProductionBundle { Water = 3 }, Maintenance = new FacilityData.ProductionBundle { Bread = 2 },
                Output = new FacilityData.ProductionBundle { Wood = 7, Iron = 3 }, BonusOutput1 = new FacilityData.ProductionBundle { MagicOre = 1, Stone = 2 }, BonusChance1 = .35f,
                BonusOutput2 = new FacilityData.ProductionBundle { MagicOre = 2 }, BonusChance2 = .7f, ProductionIntervalTurns = 3
            };
            var inside = FacilityData.RestrictProductionAt(recipe, new Vector3(2.1f, -100, 3.1f), Team.Player, territory);
            var outside = FacilityData.RestrictProductionAt(recipe, new Vector3(9, 0, 9), Team.Player, territory);
            var enemy = FacilityData.RestrictProductionAt(recipe, new Vector3(4, 30, 5), Team.Enemy, territory);
            Check("MN01 own territory preserves authored ore", inside.Output.Iron == 3 && inside.BonusOutput1.MagicOre == 1 && inside.BonusOutput2.MagicOre == 2);
            Check("MN02 neutral output and probability preserved", outside.Output.Iron == 3 && outside.BonusOutput1.MagicOre == 1 && outside.BonusChance1 == .35f && outside.BonusChance2 == .7f);
            Check("MN03 both factions use ordinary production", enemy.Output.Iron == 3 && enemy.BonusOutput1.MagicOre == 1);
            Check("MN04 other nation's territory is not confused with own territory", FacilityData.CanProduceRareOre(new Vector3(4, 0, 5), Team.Player, territory));
            Check("MN09 territory change is applied without a stale cache", FacilityData.CanProduceRareOre(new Vector3(2, 0, 3), Team.Player, territory));
            territory.PTSetPos.Clear();
            Check("MN09 removal of territory immediately permits output", FacilityData.CanProduceRareOre(new Vector3(2, 0, 3), Team.Player, territory));
            Check("MN10 inputs maintenance interval and other resources preserved", inside.Input.Water == 3 && inside.Maintenance.Bread == 2 && inside.ProductionIntervalTurns == 3 && inside.Output.Wood == 7 && inside.BonusOutput1.Stone == 2 && inside.BonusChance1 == .35f);
            Check("MN13 custom recipe probabilities and outputs remain intact", inside.BonusChance2 == .7f && inside.Output.Iron == 3 && recipe.Output.Iron == 3);
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }
    }
    static void EngineerCommands(Fixture f)
    {
        string before = JsonUtility.ToJson(f.Resources); int ap = f.State.GetAP(f.Team);
        Check(f.Team + " retired engineer cannot place a building", !f.Builder.TryEngineerPlace(f.Engineer, f.Sites[1], f.Mine));
        Check(f.Team + " retired command spends nothing", before == JsonUtility.ToJson(f.Resources) && f.State.GetAP(f.Team) == ap);
    }
    static void UnpaidMaintenance(Fixture f)
    {
        // Isolate the maintenance transaction from the real mines built in the preceding contracts.
        f.Mine.levels = new[] { new FacilityData.FacilityLevelData { HP = 100 } };
        var unit = f.Engineer; unit.kind = Kind.Knight; unit.GrowthData.kind = Kind.Knight; unit.HP = unit.MaxHP = 100; unit.Barrier = 20;
        unit.GrowthData.upkeepExempt = false; unit.GrowthData.useAuthoredUpkeep = true;
        unit.GrowthData.upkeepIron = 1; unit.GrowthData.upkeepBread = 0;
        f.Resources.Iron = 0; f.Resources.Citizen = 0;
        f.State.GetNation(f.Team).TurnsAlive = 21;
        f.Economy.ProcessTurn(f.Team);
        Check(f.Team + " UP02 UP03 unpaid full check loses20 HP and bypasses barrier", unit.HP == 80 && unit.Barrier == 20 && unit.UpkeepUnpaidTurns == 1);
        f.Economy.ProcessTurn(f.Team);
        Check(f.Team + " UP11 same owner turn cannot apply damage twice", unit.HP == 80 && unit.UpkeepUnpaidTurns == 1);
        f.Resources.Iron = 100; f.State.GetNation(f.Team).TurnsAlive++;
        f.Economy.ProcessTurn(f.Team);
        Check(f.Team + " UP01 successful payment clears unpaid counter without damage", unit.HP == 80 && unit.UpkeepUnpaidTurns == 0 && f.Resources.Iron == 100 - unit.GrowthData.GetFixedUpkeep().Iron);
        int attack = unit.ATK, defence = unit.DEF;
        unit.HP = 100; f.Resources.Iron = 0;
        for (int i = 0; i < 5; i++) { f.State.GetNation(f.Team).TurnsAlive++; f.Economy.ProcessTurn(f.Team); }
        Check(f.Team + " UP06 UP07 five unpaid turns kill by HP, no ten-turn defection", unit.HP == 0 && !unit.IsAlive && unit.UpkeepUnpaidTurns <= 5);
        Check(f.Team + " UP08 old unpaid stat reduction is inactive", unit.UpkeepPenaltyATKDEF == 0 && unit.ATK == attack && unit.DEF == defence);
    }
    public sealed class Fixture : IDisposable
    {
        public readonly Team Team;
        public readonly BuildSystem Builder;
        public readonly TurnGenerator Turn;
        public readonly TerritorySystem Territory;
        public readonly VisionGenerator Vision;
        public readonly FactionState State;
        public FactionState.ResourceData Resources => State.GetResources(Team);
        public readonly Status Engineer;
        public readonly FacilityDefinitionData Mine, Supply;
        public readonly EconomySystem Economy;
        public readonly APSystem AP;
        public readonly MoveGenerator Moves;
        public readonly UnitSetting Units;
        public readonly CrystalSystem Crystals;
        public readonly Vector3Int[] Sites;
        public readonly GameObject InvalidatingPrefab;
        readonly GameObject root;
        readonly GameSystems live;
        readonly List<UnityEngine.Object> assets = new List<UnityEngine.Object>();
        public Fixture(GameSystems live, Team team)
        {
            this.live = live; Team = team; root = new GameObject("R2 isolated engineer economy fixture");
            var hub = Child("inactive hub"); hub.SetActive(false); Turn = hub.AddComponent<TurnGenerator>();
            Set(Turn, "_stateManager", team == global::Team.Player ? (StateCore)new PlayerMove(Turn) : new EnemyMove(Turn));
            State = Child("factions").AddComponent<FactionState>();
            State.PlayerNation = Child("player nation").AddComponent<NationState>(); State.EnemyNation = Child("enemy nation").AddComponent<NationState>();
            foreach (var side in new[] { global::Team.Player, global::Team.Enemy })
            {
                var stock = State.GetResources(side); stock.Wood = stock.Stone = stock.Iron = stock.MagicOre = stock.Water = stock.Bread = stock.Wheat = 5000; stock.Citizen = 5;
                State.GetNation(side).TurnsAlive = 20; State.SetAP(side, 40);
            }
            var ap = Child("ap").AddComponent<APSystem>(); ap.Init(State);
            AP = ap;
            Territory = Child("territory").AddComponent<TerritorySystem>(); Territory.PTSetPos = new List<Vector3>(); Territory.ETSetPos = new List<Vector3>();
            var crystals = Child("crystals").AddComponent<CrystalSystem>(); crystals.PCP = live.CrystalSystem.PCP; crystals.ECP = live.CrystalSystem.ECP;
            Crystals = crystals;
            crystals.Playercrystal = Child("player crystal parent").transform; crystals.Enemycrystal = Child("enemy crystal parent").transform;
            var moves = Child("moves").AddComponent<MoveGenerator>(); moves.mapcreate = live.MapCreate;
            Moves = moves;
            Vision = Child("vision").AddComponent<VisionGenerator>();
            Builder = Child("builder").AddComponent<BuildSystem>(); Builder.PlayerBuildingParent = Child("player buildings").transform; Builder.EnemyBuildingParent = Child("enemy buildings").transform;
            Turn.Systems.MapCreate = live.MapCreate; Turn.Systems.VisionGenerator = Vision; Turn.Systems.CrystalSystem = crystals; Turn.Systems.TerritorySystem = Territory; Turn.Systems.FactionState = State;
            Builder.Init(Turn, Territory, ap, State, moves, live.MapCreate);
            Sites = FindSites(live);
            var units = Child("units").AddComponent<UnitSetting>(); units.PlayerUnit = Child("player units").transform; units.EnemyUnit = Child("enemy units").transform;
            Units = units;
            var profile = ScriptableObject.CreateInstance<BoardActionProfile>(); assets.Add(profile); profile.vision.useCustom = true; profile.vision.directionIndependent = true;
            profile.vision.SetCell(4, 3, true); profile.vision.SetCell(3, 4, true); profile.vision.SetCell(2, 3, true);
            var data = ScriptableObject.CreateInstance<UnitData>(); assets.Add(data); data.kind = Kind.Engineer; data.baseHP = 100; data.actionProfile = profile;
            var go = Child("engineer"); go.transform.SetParent((team == global::Team.Player ? units.PlayerUnit : units.EnemyUnit)); go.transform.position = Sites[0];
            Engineer = go.AddComponent<Status>(); Engineer.kind = Kind.Engineer; Engineer.type = Type.Unit; Engineer.team = team; Engineer.HP = Engineer.MaxHP = 100; Engineer.GrowthData = data;
            units.UnitDataMap[Kind.Engineer] = data;
            moves.turnGenerator = Turn;
            Set(moves, "crystalsystem", crystals); Set(moves, "unitsetting", units);
            Set(moves, "PlayerUnit", units.PlayerUnit); Set(moves, "EnemyUnit", units.EnemyUnit);
            Set(Vision, "PlayerUnit", units.PlayerUnit); Set(Vision, "EnemyUnit", units.EnemyUnit); Set(Vision, "territorysystem", Territory);
            Mine = Definition(FacilityKind.Mine); Supply = Definition(FacilityKind.LoggingCamp);
            InvalidatingPrefab = Child("invalidating template"); InvalidatingPrefab.transform.position = new Vector3(-5000, 0, -5000);
            InvalidatingPrefab.AddComponent<R2EngineerPrefabInvalidator>().Target = Engineer;
            Economy = Child("economy").AddComponent<EconomySystem>(); Economy.Init(Builder, State, units, crystals);
        }
        public AIBoardState Board()
        {
            Vision.RefreshActorVision(Engineer, live.MapCreate, Crystals);
            foreach (var team in new[] { global::Team.Player, global::Team.Enemy })
            {
                var cells = team == Team ? new HashSet<Vector3Int>(Engineer.VisionCell) : new HashSet<Vector3Int>();
                Set(Vision, team == global::Team.Player ? "_playerVisionBox" : "_enemyVisionBox", cells);
                Set(Vision, team == global::Team.Player ? "_playerExplored" : "_enemyExplored", new HashSet<Vector3Int>(cells));
            }
            var board = new AIBoardState(Moves, live.AttackGenerator, AP, Units, Crystals, Vision, Builder, null, State, null, 20, null, Team);
            board.MapCreate = live.MapCreate;
            return board;
        }
        FacilityDefinitionData Definition(FacilityKind kind)
        {
            var d = ScriptableObject.CreateInstance<FacilityDefinitionData>(); assets.Add(d); d.definitionId = "r2.engineer.fixture." + kind; d.behaviourKind = kind; d.buildAP = 3;
            d.buildCost = new FacilityData.ResourceCost(70, 110, citizen: 1);
            d.levels = new[] { new FacilityData.FacilityLevelData { HP = 100, Output = new FacilityData.ProductionBundle { Iron = kind == FacilityKind.Mine ? 3 : 0, Wood = kind == FacilityKind.LoggingCamp ? 10 : 0 }, BonusOutput1 = new FacilityData.ProductionBundle { MagicOre = kind == FacilityKind.Mine ? 1 : 0 }, BonusChance1 = kind == FacilityKind.Mine ? .35f : 0 } };
            return d;
        }
        GameObject Child(string name) { var go = new GameObject(name); go.transform.SetParent(root.transform); return go; }
        static Vector3Int[] FindSites(GameSystems live)
        {
            var actors = new List<Status>(); CombatRegistry.Collect(actors);
            foreach (var candidate in live.MapCreate.SetPos)
            {
                var origin = GridHelper.ToGrid(candidate); var sites = new Vector3Int[4]; bool valid = true;
                var offsets = new[] { Vector3Int.zero, Vector3Int.right, new Vector3Int(0, 0, 1), Vector3Int.left };
                for (int i = 0; i < sites.Length; i++)
                {
                    int x = origin.x + offsets[i].x, z = origin.z + offsets[i].z;
                    if (!live.MapCreate.TryGetHeight(x, z, out float y) || Mathf.RoundToInt(y) != origin.y
                        || live.MapCreate.Environment.BlocksMovement(x, z)) { valid = false; break; }
                    sites[i] = new Vector3Int(x, origin.y, z);
                    foreach (var actor in actors) if (GridHelper.MatchXZ(actor.transform.position, sites[i])) { valid = false; break; }
                    if (!valid || !live.MapCreate.HasClearTerrainLine(origin, sites[i], true) || !live.MapCreate.Environment.HasClearVision(origin, sites[i])) { valid = false; break; }
                }
                if (valid && live.MapCreate.Environment.VisionModifier(origin) >= 0) return sites;
            }
            throw new InvalidOperationException("R2 fixture needs four unoccupied level cells with clear official terrain vision");
        }
        public void Dispose()
        {
            if (root != null) foreach (var actor in root.GetComponentsInChildren<Status>(true)) UnitRegistry.Instance?.Unregister(actor);
            UnityEngine.Object.DestroyImmediate(root);
            foreach (var asset in assets) UnityEngine.Object.DestroyImmediate(asset);
        }
    }
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Hidden).SetValue(target, value);
}
public sealed class R2EngineerPrefabInvalidator : MonoBehaviour
{
    public Status Target;
    void OnEnable() { if (Target != null) Target.HP = 0; }
}
#endif
