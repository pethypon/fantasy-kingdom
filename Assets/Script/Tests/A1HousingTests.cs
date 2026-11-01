#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;

/// <summary>Callable native housing contracts. Run() is scene-free; Run(GameSystems) also exercises paid construction.</summary>
public static class A1HousingTests
{
    static int passed;
    static void Check(string id, bool value)
    {
        if (!value) throw new InvalidOperationException("[A1Housing] FAIL " + id);
        passed++; Debug.Log("[A1Housing] PASS " + id);
    }

    public static void Run()
    {
        passed = 0;
        using var settings = new Settings();
        APResetAndCapacity(settings);
        SequentialPurchases();
        Debug.Log("[A1Housing] RESULT scene-free checks=" + passed);
    }

    public static void Run(GameSystems live)
    {
        if (live?.MapCreate == null) throw new ArgumentNullException(nameof(live));
        Run();
        using var settings = new Settings();
        var random = UnityEngine.Random.state;
        try
        {
            foreach (var team in new[] { Team.Player, Team.Enemy })
            {
                using (var fixture = new R2ConstructionEconomyTests.Fixture(live, team)) HousingPaths(fixture, settings);
                using (var fixture = new R2ConstructionEconomyTests.Fixture(live, team)) PendingAPDemand(fixture, live, settings);
            }
        }
        finally { UnityEngine.Random.state = random; }
        Debug.Log("[A1Housing] RESULT native checks=" + passed);
    }

    static void APResetAndCapacity(Settings settings)
    {
        var ap = new FactionState.APData { Current = 1, Reset = 30, Plus = 777, Minus = 4 };
        string original = JsonUtility.ToJson(ap);
        foreach (int citizens in new[] { 0, 1, 5, 20, 100, int.MaxValue })
        {
            var formal = new FactionState.APData { Reset = ap.Reset, Minus = ap.Minus,
                Plus = (int)Math.Min(GameConstants.MaxAP, (long)citizens * EconomySystem.CitizenAPBonus) };
            formal.ResetForTurn();
            Check("HS03 formal-reset citizen=" + citizens,
                AIStrategicGovernor.HousingAPAtCitizenStock(ap, citizens) == formal.Current);
        }
        Check("HS04 current-game maximum is60, no stale15 clamp", APSystem.MaximumAP == 60
            && AIStrategicGovernor.HousingAPAtCitizenStock(ap, int.MaxValue) == 60);
        Check("HS03 penalty is included in required stock", AIStrategicGovernor.HousingCitizensForAP(ap, 40) == 14);
        Check("HS01 reset already funds target without citizens", AIStrategicGovernor.HousingCitizensForAP(ap, 20) == 0);
        Check("HS08 AP preview never mutates live state", JsonUtility.ToJson(ap) == original);
        settings.Rules.maximumAP = 12;
        Check("HS04 authored lower AP maximum is honored", AIStrategicGovernor.HousingAPAtCitizenStock(ap, 100) == 12
            && AIStrategicGovernor.HousingCitizensForAP(ap, 60) == 0);
        settings.Rules.maximumAP = 60; settings.Rules.citizenAP = 0;
        Check("HS04 zero citizen bonus cannot justify AP housing", AIStrategicGovernor.HousingCitizensForAP(ap, 40) == 0);
        settings.Rules.citizenAP = 2;
        Check("HS03 authored citizen bonus uses minimum whole citizens", AIStrategicGovernor.HousingCitizensForAP(ap, 35) == 5);
        ap.Reset = 3; ap.Minus = 1000;
        Check("HS04 unreachable reset penalty cannot cause endless houses", AIStrategicGovernor.HousingCitizensForAP(ap, 60) == 0);
        settings.Rules.citizenAP = 1;
    }

    static void SequentialPurchases()
    {
        Check("HS01 HS05 enough capacity despite empty stock", AIStrategicGovernor.RequiredHousingStock(1, 3, 3, 3, 0, 2, 0) == 5);
        Check("HS02 one expensive recruit plus reserve", AIStrategicGovernor.RequiredHousingStock(2, 9, 9, 9, 0, 2, 0) == 11);
        Check("HS02 construction and duplicate army demand are not summed", AIStrategicGovernor.RequiredHousingStock(4, 9, 9, 9, 0, 2, 0) == 11);
        Check("HS03 AP stock shares the same citizens", AIStrategicGovernor.RequiredHousingStock(4, 9, 9, 9, 0, 2, 14) == 14);
        Check("HS03 no reserve added twice to AP target", AIStrategicGovernor.RequiredHousingStock(0, 0, 0, 0, 0, 2, 14) == 14);
    }

    static void HousingPaths(R2ConstructionEconomyTests.Fixture f, Settings settings)
    {
        var house = ScriptableObject.CreateInstance<FacilityDefinitionData>();
        try
        {
            house.definitionId = "a1.test.house"; house.behaviourKind = FacilityKind.House; house.buildAP = 3;
            house.buildCost = new FacilityData.ResourceCost(5, 5, citizen: 1);
            house.levels = new[] {
                new FacilityData.FacilityLevelData { HP = 100, SpecialValue = 4 },
                new FacilityData.FacilityLevelData { HP = 150, SpecialValue = 8, UpgradeAP = 3, UpgradeCost = new FacilityData.ResourceCost(4, 4) }
            };
            var board = f.Board(); var governor = new AIStrategicGovernor { BreadReserveTurns = 1 }; board.Governor = governor;
            var custom = new AIAction { ActionType = AIActionType.Build, Facility = FacilityKind.House,
                FacilityDefinition = house, APCost = 3, TargetPos = f.Sites[1] };
            Check(f.Team + " HS01 surplus capacity rejects custom house", !governor.HousingAssessment(custom, board).Needed);
            f.Resources.Citizen = 0; board.Refresh();
            Check(f.Team + " HS05 empty citizen stock is growth, not capacity", governor.HousingAssessment(custom, board).Reason == "housing_existing_growth_can_recover_stock");
            f.Resources.Citizen = 5; board.Refresh(); board.OperationRequiredCitizenDemand = 7;
            var needed = governor.HousingAssessment(custom, board);
            Check(f.Team + " HS02 a real purchase above capacity requests housing", needed.Needed && needed.CapacityBlocked && needed.RequiredStock == 9);
            Check(f.Team + " HS07 direct fallback retains housing-only demand", AIBuildPlanner.IsFallbackInvestment(custom, board, governor));
            var ordinary = new AIAction { ActionType = AIActionType.Build, Facility = FacilityKind.House,
                APCost = FacilityData.Table[FacilityKind.House].APCost, TargetPos = f.Sites[1] };
            var luxury = new AIAction { ActionType = AIActionType.Build, Facility = FacilityKind.LuxuryHouse,
                APCost = FacilityData.Table[FacilityKind.LuxuryHouse].APCost, TargetPos = f.Sites[1] };
            Check(f.Team + " HS07 ordinary custom luxury agree on capacity need",
                governor.HousingAssessment(ordinary, board).Needed && governor.HousingAssessment(luxury, board).Needed);
            int bread = f.Resources.Bread; f.Resources.Bread = 0; board.Refresh();
            Check(f.Team + " HS06 no food means supply recovery first", governor.HousingAssessment(custom, board).Reason == "housing_supply_recovery_first");
            f.Resources.Bread = bread; board.Refresh();
            int capacity = f.State.GetCitizenCap(f.Team), ap = f.State.GetAP(f.Team), wood = f.Resources.Wood, citizens = f.Resources.Citizen;
            Check(f.Team + " HS08 outside territory cannot construct", !f.Builder.TryPlaceDefinition(f.Sites[1], house, f.Team));
            Check(f.Team + " HS08 failed placement leaves payment intact", f.Resources.Wood == wood && f.Resources.Citizen == citizens && f.State.GetAP(f.Team) == ap);
            f.Territory.GetTerritory(f.Team).Add(f.Sites[1]);
            Check(f.Team + " HS08 formal paid house succeeds", f.Builder.TryPlaceDefinition(f.Sites[1], house, f.Team));
            Check(f.Team + " HS08 exact formal payment", f.Resources.Wood == wood - house.buildCost.Wood
                && f.Resources.Citizen == citizens - house.buildCost.Citizen && f.State.GetAP(f.Team) == ap - house.buildAP);
            Check(f.Team + " HS09 capacity recalculates immediately", f.State.GetCitizenCap(f.Team) == capacity + 4);
            board.Refresh();
            Check(f.Team + " HS09 satisfied capacity stops repeated builds", !governor.HousingAssessment(custom, board).Needed);
            board.OperationRequiredCitizenDemand = 11;
            var actor = f.Builder.GetLastPlacedBuilding().GetComponent<Status>();
            var upgrade = new AIAction { ActionType = AIActionType.Upgrade, Unit = actor, TargetUnit = actor,
                Facility = FacilityKind.House, FacilityDefinition = house, APCost = 3 };
            Check(f.Team + " HS07 upgrade shares need and actual delta", governor.HousingAssessment(upgrade, board).Needed
                && governor.HousingAssessment(upgrade, board).CapacityGain == 4);
            Check(f.Team + " HS09 paid upgrade updates capacity", f.Builder.TryUpgrade(actor) && f.State.GetCitizenCap(f.Team) == capacity + 8);
            board.Refresh();
            Check(f.Team + " HS10 upgrade capacity suppresses further housing", !governor.HousingAssessment(custom, board).Needed);
            Check(f.Team + " HS10 unrelated actions are not housing", !governor.HousingAssessment(new AIAction { ActionType = AIActionType.Wait }, board).IsHousing);
        }
        finally { UnityEngine.Object.DestroyImmediate(house); }
    }

    static void PendingAPDemand(R2ConstructionEconomyTests.Fixture f, GameSystems live, Settings settings)
    {
        // A single expensive authored attack is legal by geometry even when the current shared AP is exhausted.
        // This proves AP capacity derives from an intended legal action instead of always requesting maximum AP.
        var host = new GameObject("A1 isolated AP target and query");
        var targetData = ScriptableObject.CreateInstance<UnitData>();
        try
        {
            host.transform.SetParent(f.Units.transform);
            var query = host.AddComponent<AttackGenerator>(); query.mapcreate = live.MapCreate; query.moveGenerator = f.Moves;
            var markers = new GameObject("A1 isolated attack markers"); markers.transform.SetParent(host.transform); query.APparent = markers.transform;
            var targetObject = new GameObject("A1 observed target"); targetObject.transform.SetParent(f.Team == Team.Player ? f.Units.EnemyUnit : f.Units.PlayerUnit);
            targetObject.transform.position = f.Sites[1];
            var target = targetObject.AddComponent<Status>(); target.team = f.Team == Team.Player ? Team.Enemy : Team.Player;
            target.type = Type.Unit; target.kind = Kind.Knight; target.HP = target.MaxHP = 100; target.GrowthData = targetData;
            targetData.kind = Kind.Knight; targetData.baseHP = 100; targetData.upkeepExempt = true;
            f.Engineer.kind = Kind.Knight; f.Engineer.GrowthData.kind = Kind.Knight; f.Engineer.GrowthData.upkeepExempt = true;
            var profile = f.Engineer.GrowthData.actionProfile; profile.attack.useCustom = true; profile.attack.directionIndependent = true;
            profile.attack.SetCell(4, 3, true);
            settings.Rules.attackAP = 20;
            f.State.GetAPData(f.Team).Reset = 3; f.State.GetAPData(f.Team).Minus = 0; f.Resources.Citizen = 5;
            f.State.UpdateCitizenAPBonus(f.Team); f.State.SetAP(f.Team, 0);
            f.Board(); // Refresh the fixture's legitimate individual/shared observation.
            var board = new AIBoardState(f.Moves, query, f.AP, f.Units, f.Crystals, f.Vision, f.Builder,
                null, f.State, null, 20, null, f.Team) { MapCreate = live.MapCreate };
            var governor = new AIStrategicGovernor { BreadReserveTurns = 1 }; board.Governor = governor;
            var house = new AIAction { ActionType = AIActionType.Build, Facility = FacilityKind.House, APCost = 3 };
            governor.HousingAssessment(house, board);
            Check(f.Team + " HS03 planned legal attack creates bounded AP target", governor.HousingAPPlan.PlannedActions == 1
                && governor.HousingAPPlan.DesiredAP == 20 && governor.HousingAPPlan.CapacityAP == 8);
            Check(f.Team + " HS03 distant citizen recovery is deferred by policy", governor.HousingAPPlan.RequiredCitizens == 0
                && governor.HousingAPPlan.Reason == "housing_ap_growth_outside_horizon");
            settings.Housing.APPreparationTurns = 20; board.Refresh(); governor.HousingAssessment(house, board);
            Check(f.Team + " HS03 actual reset inversion sets required stock", governor.HousingAPPlan.RequiredCitizens == 17);
            board.AliveEnemyUnits.Add(f.Engineer); // A malformed duplicate view must not count the same actor twice.
            GameContentRevision.NotifyChanged(); governor.HousingAssessment(house, board);
            Check(f.Team + " HS03 same life is counted once", governor.HousingAPPlan.PlannedActions == 1 && governor.HousingAPPlan.DesiredAP == 20);
            settings.Rules.attackAP = 2; board.Refresh(); GameContentRevision.NotifyChanged(); governor.HousingAssessment(house, board);
            Check(f.Team + " HS01 cheap real plan does not request maximum AP", governor.HousingAPPlan.DesiredAP == 2 && governor.HousingAPPlan.RequiredCitizens == 0);
            f.Turn.Systems.APSystem = f.AP; board.SpecialSystems = f.Turn.Systems;
            settings.Housing.EnableRepeatAttackAPPlanning = true;
            board.Refresh(); GameContentRevision.NotifyChanged(); governor.HousingAssessment(house, board);
            Check(f.Team + " HS04 fatigue-aware-repeat-is-a-real-bounded-AP-plan", governor.HousingAPPlan.PlannedActions == 2
                && governor.HousingAPPlan.DesiredAP == 5);
            targetData.costAP = 6; targetData.costWood = targetData.costStone = targetData.costIron = 0;
            targetData.costMagic = targetData.costBread = targetData.costWater = 0;
            var recruits = (System.Collections.Generic.List<AIRecruitmentNeed>)governor.RecruitmentPlan;
            recruits.Clear(); recruits.Add(new AIRecruitmentNeed(targetData, "frontline", "controlled_pending_recruit"));
            settings.Housing.EnableRecruitmentAPPlanning = true;
            var pending = (AIHousingAPPlan)typeof(AIStrategicGovernor).GetMethod("PrepareHousingAPPlan", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(governor, new object[] { board, settings.Housing });
            Check(f.Team + " HS04 pending-paid-reinforcement-shares-army-AP", pending.PlannedActions == 3 && pending.DesiredAP == 11);
            settings.Housing.EnableRecruitmentAPPlanning = false; recruits.Clear();
            board.SpecialSystems = null; settings.Rules.attackAP = 20; settings.Housing.APPreparationTurns = 10;
            settings.Housing.EnableIncrementalAPPreparation = true;
            board.Refresh(); GameContentRevision.NotifyChanged(); governor.HousingAssessment(house, board);
            Check(f.Team + " HS04 reachable-partial-capacity-is-prepared-instead-of-discarding-all", governor.HousingAPPlan.RequiredCitizens == 15
                && governor.HousingAPPlan.DesiredAP == 18);
            settings.Housing.EnableIncrementalAPPreparation = false;
            settings.Rules.attackAP = 2; settings.Housing.APPreparationTurns = 10;
        }
        finally { UnityEngine.Object.DestroyImmediate(host); UnityEngine.Object.DestroyImmediate(targetData); }
    }

    sealed class Settings : IDisposable
    {
        const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic;
        readonly FieldInfo rules = typeof(GameAuthoringRules).GetField("loaded", Flags), didLoad = typeof(GameAuthoringRules).GetField("didLoad", Flags);
        readonly FieldInfo housing = typeof(AIHousingConfig).GetField("active", Flags);
        readonly object oldRules, oldDidLoad, oldHousing;
        public readonly GameAuthoringRules Rules;
        public readonly AIHousingConfig Housing;
        public Settings()
        {
            oldRules = rules.GetValue(null); oldDidLoad = didLoad.GetValue(null); oldHousing = housing.GetValue(null);
            Rules = ScriptableObject.CreateInstance<GameAuthoringRules>(); Rules.applyRules = true; Rules.maximumAP = 60; Rules.citizenAP = 1;
            Rules.aiEconomy.basicResources.enabled = false;
            Housing = ScriptableObject.CreateInstance<AIHousingConfig>(); Housing.EnableDevelopmentGrowth = false;
            rules.SetValue(null, Rules); didLoad.SetValue(null, true); housing.SetValue(null, Housing);
            GameContentRevision.NotifyChanged();
        }
        public void Dispose()
        {
            rules.SetValue(null, oldRules); didLoad.SetValue(null, oldDidLoad); housing.SetValue(null, oldHousing);
            UnityEngine.Object.DestroyImmediate(Rules); UnityEngine.Object.DestroyImmediate(Housing); GameContentRevision.NotifyChanged();
        }
    }
}
#endif
