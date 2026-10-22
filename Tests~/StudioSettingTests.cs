#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;

/// <summary>Authoring propagation boundaries. Fixtures change memory only, never production assets or profiles.</summary>
public static class StudioSettingTests
{
    static int passed;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(string label, bool ok)
    {
        if (!ok) throw new InvalidOperationException("[StudioSettings] FAIL " + label);
        passed++;
    }
    static bool Near(float a, float b) => Mathf.Abs(a - b) < .05f;
    static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private).GetValue(owner);
    static TextMeshProUGUI Text(GameObject root, string name)
    {
        foreach (var text in root.GetComponentsInChildren<TextMeshProUGUI>(true))
            if (text.name == name) return text;
        throw new MissingFieldException("UI text " + name);
    }
    static TextMeshProUGUI ButtonText(GameObject root, string buttonName)
    {
        foreach (var text in root.GetComponentsInChildren<TextMeshProUGUI>(true))
            if (text.transform.parent != null && text.transform.parent.name == buttonName) return text;
        throw new MissingFieldException("UI button " + buttonName);
    }

    public static void EditMode()
    {
        passed = 0;
        using (var catalog = new MemoryCatalog())
        {
            foreach (FacilityKind kind in Enum.GetValues(typeof(FacilityKind)))
                Check("all standard facility kinds registered: " + kind, FacilityData.IsKnownKind(kind));
            Check("offensive facility recipes survive static construction", FacilityData.GetLevel(FacilityKind.Cannon, 1).ATK > 0
                && FacilityData.GetLevel(FacilityKind.Mortar, 1).ATK > 0 && FacilityData.GetMaxLevel(FacilityKind.HeroSword) > 0);

            var house = catalog.Definition(FacilityKind.House);
            house.buildCost = new FacilityData.ResourceCost(wood: 30, stone: 13, citizen: 2);
            house.buildAP = 7;
            house.levels = new[] { new FacilityData.FacilityLevelData { HP = 230, DEF = 18, SpecialValue = 8 } };
            catalog.Value.standardOverrides.Add(house); GameContentRevision.NotifyChanged();
            var oldView = FacilityData.Table;
            Check("standard registration changes costs and stats without another row", oldView[FacilityKind.House].BuildCost.Wood == 30
                && oldView[FacilityKind.House].APCost == 7 && oldView[FacilityKind.House].HP == 230);
            Check("capacity UI reads the same resolved recipe", BuildSummonUIBuilder.FormatBuildingDetails(FacilityKind.House,
                FacilityData.GetLevel(FacilityKind.House, 1)).Contains("市民上限 +8"));
            house.buildCost = new FacilityData.ResourceCost(wood: 35, stone: 11);
            GameContentRevision.NotifyChanged();
            Check("one revision replaces the view without corrupting existing enumerators", FacilityData.Table != oldView
                && oldView[FacilityKind.House].BuildCost.Wood == 30 && FacilityData.Table[FacilityKind.House].BuildCost.Wood == 35);
            Check("standards never leak into standalone build list", catalog.Value.buildings.Count == 0);
            house.availableToPlayer = false; GameContentRevision.NotifyChanged();
            Check("standard faction restrictions are effective", !FacilityData.IsAvailableFor(FacilityKind.House, Team.Player)
                && FacilityData.IsAvailableFor(FacilityKind.House, Team.Enemy));
            var duplicate = catalog.Definition(FacilityKind.House);
            catalog.Value.standardOverrides.Add(duplicate); GameContentRevision.NotifyChanged();
            Check("ambiguous standard roles fail closed to legacy balance", catalog.Value.FindStandard(FacilityKind.House) == null
                && FacilityData.Table[FacilityKind.House].BuildCost.Wood != 35);
            catalog.Value.standardOverrides.Remove(duplicate); GameContentRevision.NotifyChanged();
            Check("standalone identity resolves separately from standard behavior", catalog.Value.Find(house.definitionId) == house
                && catalog.Value.Find(duplicate.definitionId) == null);
            catalog.Value.retiredDefinitions.Add(duplicate);
            Check("retired identities remain readable for old saves", catalog.Value.Find(duplicate.definitionId) == duplicate);
        }
        GrowthAndPendingRevision();
        Debug.Log("[StudioSettings] " + passed + " edit-mode catalog/revision checks passed");
    }

    public static void PlayMode(GameSystems systems, TurnGenerator turn)
    {
        passed = 0;
        try
        {
            BuildingTransactionAndUI(systems);
            ProductionAndForecast(systems);
            SpecialStandardRecipeForecast(systems);
            CapacityOverflowBoundary(systems);
            SummonTransactionAndUI(systems, turn);
            UnitStatsAndUpkeep(systems);
        }
        finally { GameContentRevision.NotifyChanged(); }
        Debug.Log("[StudioSettings] " + passed + " UI/transaction/production/forecast/rebalance checks passed");
    }

    static void BuildingTransactionAndUI(GameSystems systems)
    {
        using (var f = new EconomyR2Tests.Fixture(systems))
        {
            var house = f.Definition(FacilityKind.House); f.Catalog.buildings.Remove(house); f.Catalog.standardOverrides.Add(house);
            house.displayName = "調整済み住宅"; house.buildAP = 6;
            house.buildCost = new FacilityData.ResourceCost(wood: 30, stone: 13, citizen: 2);
            house.levels = new[] { new FacilityData.FacilityLevelData { HP = 230, DEF = 18, SpecialValue = 8 } };
            var subCrystal = f.Definition(FacilityKind.SubCrystal); f.Catalog.buildings.Remove(subCrystal); f.Catalog.standardOverrides.Add(subCrystal);
            subCrystal.buildCost = new FacilityData.ResourceCost(wood: 23); subCrystal.buildAP = 3; f.State.PlayerSubCrystals = 1;
            GameContentRevision.NotifyChanged();
            GameObject uiRoot = new GameObject("Studio build UI fixture", typeof(RectTransform));
            try
            {
                var ui = new BuildSummonUIBuilder(UIFactory.LoadDefaultFont());
                ui.CreateBuildScrollView("Build view", uiRoot.GetComponent<RectTransform>());
                ui.InitBuildButtons(f.Builder, f.AP, f.State);
                Check("actual build menu shows authored cost and citizen capacity", Text(uiRoot, "Cost_House").text.Contains("木30")
                    && Text(uiRoot, "Cost_House").text.Contains("石13") && Text(uiRoot, "Cost_House").text.Contains("市民上限 +8"));
                Check("actual build menu title uses authored name and AP", ButtonText(uiRoot, "Build_House").text.Contains("調整済み住宅")
                    && ButtonText(uiRoot, "Build_House").text.Contains("AP:6"));
                Check("subcrystal UI includes authored material cost and AP", Text(uiRoot, "Cost_SubCrystal").text.Contains("木23")
                    && ButtonText(uiRoot, "Build_SubCrystal").text.Contains("AP:3"));
                f.State.PlayerResources.Wood = 22; ui.RefreshBuildButtons();
                Check("subcrystal token cannot bypass authored material shortage", BuildValidator.CostFailure(FacilityKind.SubCrystal, f.State) == "木材不足"
                    && !f.AP.CanBuild(Team.Player, FacilityKind.SubCrystal, f.State));
                f.State.PlayerResources.Wood = 400; ui.RefreshBuildButtons();
                var board = f.Board(); Check("real building fixture has legal placement", board.BuildablePositions.Count > 0);
                int wood = f.Resources.Wood, stone = f.Resources.Stone, citizen = f.Resources.Citizen, ap = f.State.GetAP(f.Team);
                Check("normal standard building transaction succeeds", f.Builder.AIPlaceBuilding(board.BuildablePositions[0], FacilityKind.House, f.Team));
                Check("transaction consumes exact authored resource and AP costs", f.Resources.Wood == wood - 30
                    && f.Resources.Stone == stone - 13 && f.Resources.Citizen == citizen - 2 && f.State.GetAP(f.Team) == ap - 6);
                var placed = f.Buildings.Find(actor => actor.facilityKind == FacilityKind.House);
                Check("created actor uses authored HP and defense", placed != null && placed.MaxHP == 230 && placed.DEF == 18);
                Check("normal transaction immediately updates its own faction capacity", f.State.GetCitizenCap(f.Team) == FactionState.BaseCitizenCap + 8);
                f.Economy.RecalculateCapacity(f.Team);
                Check("capacity refresh immediately grants authored eight citizens", f.State.GetCitizenCap(f.Team) == FactionState.BaseCitizenCap + 8);
                int bread = f.Resources.Bread;
                f.Economy.RecalculateCapacity(f.Team);
                Check("capacity refresh never pays production or maintenance", f.Resources.Bread == bread && f.Resources.Wood == wood - 30);
                house.buildCost = new FacilityData.ResourceCost(wood: 17); var level = house.levels[0]; level.SpecialValue = 11; house.levels[0] = level;
                GameContentRevision.NotifyChanged(); ui.RefreshContentRows(); f.Economy.RecalculateCapacity(f.Team);
                Check("open UI refresh and capacity both consume next revision", Text(uiRoot, "Cost_House").text.Contains("木17")
                    && Text(uiRoot, "Cost_House").text.Contains("市民上限 +11") && f.State.GetCitizenCap(f.Team) == FactionState.BaseCitizenCap + 11);
                house.availableToEnemy = false; GameContentRevision.NotifyChanged();
                Check("normal AI build cannot bypass authored faction permission", !f.AP.CanBuild(f.Team, FacilityKind.House, f.State));
            }
            finally { UnityEngine.Object.DestroyImmediate(uiRoot); GameContentRevision.NotifyChanged(); }
        }
        GameContentRevision.NotifyChanged();
    }

    static void ProductionAndForecast(GameSystems systems)
    {
        using (var f = new EconomyR2Tests.Fixture(systems))
        {
            var definition = f.Definition(FacilityKind.LoggingCamp); f.Catalog.buildings.Remove(definition); f.Catalog.standardOverrides.Add(definition);
            definition.levels = new[] { new FacilityData.FacilityLevelData
            {
                HP = 100, ProductionIntervalTurns = 2, Input = new FacilityData.ProductionBundle { Stone = 2 },
                Output = new FacilityData.ProductionBundle { Wood = 9 }, Maintenance = new FacilityData.ProductionBundle { Water = 3 }
            } };
            var actor = f.Building(definition); actor.AuthoredFacility = null; actor.authoredFacilityId = null;
            f.Rules.buildingProduction.applyAdjustments = true;
            f.Rules.buildingProduction.adjustments = new[] { new BuildingProductionAdjustment
            {
                buildingType = FacilityKind.LoggingCamp, definitionId = string.Empty, level = 1,
                output = new FacilityData.ProductionBundle { Wood = 999 }, intervalTurns = 1
            } };
            GameContentRevision.NotifyChanged();
            var board = f.Board(); var forecast = new StrategicEconomyForecast { ForecastTurns = 2 };
            var diagnosis = forecast.Diagnose(board, false);
            Check("AI predicts authored interval output", Near(diagnosis.Get(ResourceKind.Wood).ProductionPerTurn, 4.5f));
            Check("legacy empty-ID production adjustment cannot silently override a formal standard definition",
                FacilityData.GetLevel(FacilityKind.LoggingCamp, 1).Output.Wood == 9);
            Check("AI predicts production inputs and every-turn upkeep separately", Near(diagnosis.Get(ResourceKind.Stone).MandatoryDemandPerTurn, 1)
                && Near(diagnosis.Get(ResourceKind.Water).MandatoryDemandPerTurn, 3));
            string details = BuildSummonUIBuilder.FormatBuildingDetails(FacilityKind.LoggingCamp, FacilityData.GetLevel(FacilityKind.LoggingCamp, 1));
            Check("production UI displays output interval input and maintenance", details.Contains("木9") && details.Contains("2ターン")
                && details.Contains("石2") && details.Contains("水3"));
            int wood = f.Resources.Wood, stone = f.Resources.Stone, water = f.Resources.Water;
            f.Economy.ProcessTurn(f.Team);
            Check("production cycle executes exact authored output and input", f.Resources.Wood == wood + 9 && f.Resources.Stone == stone - 2 && f.Resources.Water == water - 3);
            f.State.GetNation(f.Team).TurnsAlive++;
            f.Economy.ProcessTurn(f.Team);
            Check("off-cycle keeps output/input idle while upkeep is still paid", f.Resources.Wood == wood + 9 && f.Resources.Stone == stone - 2 && f.Resources.Water == water - 6);
            var data = definition.levels[0]; data.Output.Wood = 16; data.ProductionIntervalTurns = 1; definition.levels[0] = data;
            GameContentRevision.NotifyChanged();
            Check("same board generation cannot retain stale AI forecast", Near(forecast.Diagnose(board, false).Get(ResourceKind.Wood).ProductionPerTurn, 16));
            f.Economy.ProcessTurn(f.Team);
            Check("next production turn consumes revised recipe", f.Resources.Wood == wood + 25 && f.Resources.Stone == stone - 4 && f.Resources.Water == water - 9);
            f.Rules.buildingProduction.adjustments[0].definitionId = definition.definitionId;
            f.Rules.buildingProduction.adjustments[0].output.Wood = 4; GameContentRevision.NotifyChanged();
            Check("explicit identity-specific adjustment is shared by recipe UI and AI", FacilityData.GetLevel(FacilityKind.LoggingCamp, 1).Output.Wood == 4
                && BuildSummonUIBuilder.FormatBuildingDetails(FacilityKind.LoggingCamp, FacilityData.GetLevel(FacilityKind.LoggingCamp, 1)).Contains("木4")
                && Near(forecast.Diagnose(board, false).Get(ResourceKind.Wood).ProductionPerTurn, 4));
            wood = f.Resources.Wood; f.Economy.ProcessTurn(f.Team);
            Check("ordinary runtime production uses the explicit applied adjustment", f.Resources.Wood == wood + 4);
        }
        GameContentRevision.NotifyChanged();
    }

    static void SpecialStandardRecipeForecast(GameSystems systems)
    {
        using (var f = new EconomyR2Tests.Fixture(systems))
        {
            var house = f.Definition(FacilityKind.House); f.Catalog.buildings.Remove(house); f.Catalog.standardOverrides.Add(house);
            house.buildCost = default;
            house.levels = new[]
            {
                new FacilityData.FacilityLevelData { HP = 100, SpecialValue = 8, Maintenance = new FacilityData.ProductionBundle { Bread = 7 } },
                new FacilityData.FacilityLevelData { HP = 140, SpecialValue = 12, Maintenance = new FacilityData.ProductionBundle { Bread = 11 } }
            };
            GameContentRevision.NotifyChanged(); var board = f.Board(); var forecast = new StrategicEconomyForecast { ForecastTurns = 1 };
            float before = forecast.Simulate(board).NetBreadPerTurn;
            var build = new AIAction { ActionType = AIActionType.Build, Facility = FacilityKind.House };
            Check("standard housing candidate includes authored maintenance before construction", Near(forecast.Simulate(board, build).NetBreadPerTurn, before - 9));
            // New capacity permits one natural citizen: one growth bread plus one additional citizen meal.
            var actor = f.Building(house); actor.AuthoredFacility = null; actor.authoredFacilityId = null; board.Refresh();
            before = forecast.Simulate(board).NetBreadPerTurn;
            var upgrade = new AIAction { ActionType = AIActionType.Upgrade, Unit = actor };
            Check("standard special upgrade replaces upkeep instead of ignoring it", Near(forecast.Simulate(board, upgrade).NetBreadPerTurn, before - 4));
            f.Economy.ProcessTurn(f.Team);
            Check("actual special building maintenance uses the same seven bread", f.Resources.Bread == 400 - 7 - 1 - 6);
        }
        GameContentRevision.NotifyChanged();
    }

    static void UnitStatsAndUpkeep(GameSystems systems)
    {
        using (var f = new EconomyR2Tests.Fixture(systems))
        {
            var actor = f.Unit(); var definition = actor.GrowthData;
            definition.baseHP = 100; definition.baseATK = 20; definition.baseDEF = 10;
            definition.hpGrowth = definition.atkGrowth = definition.defGrowth = 0;
            definition.useAuthoredUpkeep = true; definition.upkeepWood = 2; definition.upkeepIron = 4; definition.upkeepBread = 3; definition.upkeepWater = 5;
            definition.ApplyToStatus(actor, 1); actor.HP = 50; actor.ATK += 6; actor.DEF += 3; actor.MaxHP += 20; actor.HP = 60;
            int cooldown = actor.SkillCooldown = 2; actor.Experience = 4;
            definition.baseHP = 200; definition.baseATK = 34; definition.baseDEF = 19;
            Check("rebalance changes the existing actor", StatusBalanceRefresh.ApplyLatestDefinition(actor));
            Check("rebalance preserves HP proportion and earned HP bonus", actor.MaxHP == 220 && actor.HP == 110);
            Check("rebalance retains earned attack/defense bonuses", actor.ATK == 40 && actor.DEF == 22);
            Check("rebalance does not erase experience or ability cooldown", actor.Experience == 4 && actor.SkillCooldown == cooldown);
            StatusBalanceRefresh.ApplyLatestDefinition(actor);
            Check("repeated reload cannot compound rewards or heal", actor.MaxHP == 220 && actor.HP == 110 && actor.ATK == 40);
            var old = UnitPanelUI.SelectedStatus;
            try
            {
                systems.UnitPanelUI.ShowInspection(actor);
                Check("status UI displays updated attack and the current barrier rule without obsolete DEF", Field<TextMeshProUGUI>(systems.UnitPanelUI, "atkText").text.Contains("40")
                    && Field<TextMeshProUGUI>(systems.UnitPanelUI, "barrierText").gameObject.activeSelf == BarrierSystem.UsesBarrier(actor));
            }
            finally { if (old != null && old.IsAlive) systems.UnitPanelUI.Show(old); else systems.UnitPanelUI.Hide(); }
            var target = f.Opponent(actor.transform.position + Vector3.forward, 0); target.kind = Kind.Scout; target.DEF = 0;
            target.passiveskill = actor.passiveskill = PassiveSkill.None; actor.kind = Kind.Scout; actor.direction = target.direction = Direction.N;
            int low = SkillSystem.CalcNormalDamage(actor, target); actor.ATK = 80;
            int high = SkillSystem.CalcNormalDamage(actor, target); actor.ATK = 40;
            Check("real battle damage calculator responds to authored live attack", high > low && low > 0);
            Check("upkeep UI formatter shows authored iron and bread instead of legacy defaults", BuildSummonUIBuilder.FormatProductionBundle(definition.GetUpkeep(1)).Contains("鉄4")
                && BuildSummonUIBuilder.FormatProductionBundle(definition.GetUpkeep(1)).Contains("パン3"));
            var board = f.Board(); var forecast = new StrategicEconomyForecast { ForecastTurns = 1 };
            var diagnosis = forecast.Diagnose(board, false);
            Check("AI upkeep forecast shares authored iron water and wood", Near(diagnosis.Get(ResourceKind.Iron).MandatoryDemandPerTurn, 4)
                && Near(diagnosis.Get(ResourceKind.Water).MandatoryDemandPerTurn, 5) && Near(diagnosis.Get(ResourceKind.Wood).MandatoryDemandPerTurn, 2));
            int wood = f.Resources.Wood, iron = f.Resources.Iron, water = f.Resources.Water;
            f.Economy.ProcessTurn(f.Team);
            Check("next ordinary economy turn pays authored unit upkeep", f.Resources.Wood == wood - 2 && f.Resources.Iron == iron - 4
                && f.Resources.Water == water - 5 && actor.UpkeepUnpaidTurns == 0);
            var model = new GameObject("Offset model root"); model.transform.SetParent(f.Units.EnemyUnit, false);
            actor.transform.SetParent(model.transform, false);
            definition.upkeepIron = 9; definition.upkeepWater = 8; GameContentRevision.NotifyChanged();
            iron = f.Resources.Iron; water = f.Resources.Water;
            f.Economy.ProcessTurn(f.Team);
            Check("authored child-Status model pays updated upkeep too", f.Resources.Iron == iron - 9 && f.Resources.Water == water - 8);
            actor.HP = 0; definition.baseHP = 300; StatusBalanceRefresh.ApplyLatestDefinition(actor);
            Check("rebalance never revives a defeated actor", actor.HP == 0);
        }
        GameContentRevision.NotifyChanged();
    }

    static void SummonTransactionAndUI(GameSystems systems, TurnGenerator turn)
    {
        using (var f = new EconomyR2Tests.Fixture(systems))
        {
            var definition = f.UnitDefinition(); definition.displayName = "調整済み騎士";
            definition.baseATK = 34; definition.baseDEF = 19;
            definition.costWood = 12; definition.costStone = 4; definition.costIron = 6;
            definition.costWater = 5; definition.costBread = 9; definition.costCitizen = 1; definition.costAP = 7;
            definition.useAuthoredUpkeep = true; definition.upkeepIron = 4; definition.upkeepBread = 3;
            f.Units.UnitDataMap[Kind.Knight] = definition;
            var uiRoot = new GameObject("Studio summon UI fixture", typeof(RectTransform));
            try
            {
                var summon = uiRoot.AddComponent<SummonSystem>();
                summon.Init(turn, systems.TerritorySystem, f.AP, f.State, systems.MoveGenerator, systems.MapCreate, f.Units, systems.VisionGenerator);
                var ui = new BuildSummonUIBuilder(UIFactory.LoadDefaultFont());
                ui.InitSummonButtons(summon, f.AP, f.State, f.Units);
                ui.CreateScrollView("Summon view", uiRoot.GetComponent<RectTransform>()); ui.RefreshSummonButtons();
                Check("actual summon menu shows authored resource costs and upkeep", Text(uiRoot, "Cost_Knight").text.Contains("木材 12")
                    && Text(uiRoot, "Cost_Knight").text.Contains("パン 9") && Text(uiRoot, "Cost_Knight").text.Contains("鉄4")
                    && Text(uiRoot, "Cost_Knight").text.Contains("パン3"));
                Check("actual summon menu title shows authored name and AP", ButtonText(uiRoot, "Summon_Knight").text.Contains("調整済み騎士")
                    && ButtonText(uiRoot, "Summon_Knight").text.Contains("AP:7"));
                var cells = summon.AIGetSummonablePositions(f.Team);
                Check("real summon fixture has legal placement", cells.Count > 0);
                int wood = f.Resources.Wood, bread = f.Resources.Bread, iron = f.Resources.Iron, ap = f.State.GetAP(f.Team);
                Check("normal summon transaction succeeds with authored definition", summon.AISummonUnit(cells[0], definition, f.Team));
                Check("normal summon consumes exact displayed costs", f.Resources.Wood == wood - 12 && f.Resources.Bread == bread - 9
                    && f.Resources.Iron == iron - 6 && f.State.GetAP(f.Team) == ap - 7);
                var actors = f.Units.EnemyUnit.GetComponentsInChildren<Status>();
                Check("normal spawned actor uses current formal stats", actors.Length == 1 && actors[0].ATK == 34
                    && actors[0].DEF == 19 && actors[0].MaxHP == 100 && actors[0].GrowthData == definition);
                definition.costWood = 19; definition.costAP = 9; definition.upkeepBread = 8; GameContentRevision.NotifyChanged();
                ui.RefreshContentRows();
                Check("open summon UI refreshes costs AP and upkeep together", Text(uiRoot, "Cost_Knight").text.Contains("木材 19")
                    && Text(uiRoot, "Cost_Knight").text.Contains("パン8") && ButtonText(uiRoot, "Summon_Knight").text.Contains("AP:9"));
                var board = f.Board();
                Check("AI recruits read the same revised definition", board.ResolveUnitDefinition(Kind.Knight).costWood == 19
                    && board.ResolveUnitDefinition(Kind.Knight).GetUpkeep(1).Bread == 8);
                definition.availableToEnemy = false;
                Check("authored summon faction restriction is enforced", !summon.CanSummon(f.Team, definition));
            }
            finally { UnityEngine.Object.DestroyImmediate(uiRoot); }
        }
        GameContentRevision.NotifyChanged();
    }

    static void CapacityOverflowBoundary(GameSystems systems)
    {
        using (var f = new EconomyR2Tests.Fixture(systems))
        {
            var house = f.Definition(FacilityKind.House);
            var data = house.levels[0]; data.SpecialValue = int.MaxValue; house.levels[0] = data;
            f.Building(house); f.Building(house);
            f.Economy.RecalculateCapacity(f.Team);
            Check("large authored capacity sums cannot wrap negative during immediate refresh", f.State.GetCitizenCap(f.Team) == int.MaxValue);
            f.Economy.ProcessTurn(f.Team);
            Check("ordinary turn recomputation uses the same overflow-safe capacity", f.State.GetCitizenCap(f.Team) == int.MaxValue);
        }
        GameContentRevision.NotifyChanged();
    }

    static void GrowthAndPendingRevision()
    {
        var root = new GameObject("Studio growth fixture"); var definition = ScriptableObject.CreateInstance<UnitData>();
        try
        {
            var actor = root.AddComponent<Status>(); actor.team = Team.Enemy; actor.kind = Kind.Knight; actor.type = Type.Unit;
            definition.kind = Kind.Knight; definition.baseHP = 100; definition.baseATK = 20; definition.baseDEF = 10;
            definition.ApplyToStatus(actor, 1); actor.MaxHP += 20; actor.HP = 60; actor.ATK += 7; actor.DEF += 3;
            actor.GainExperience(Status.XPRequiredForLevel(2));
            Check("level-up records the new formal baseline without counting rewards", actor.Level == 2 && actor.MaxHP == 122 && actor.HP == 62
                && actor.DefinitionBalanceMaxHP == 102 && actor.DefinitionBalanceATK == 22);
            definition.baseHP = 200; definition.baseATK = 30; definition.baseDEF = 18;
            GameContentRevision.NotifyChanged(); StatusBalanceRefresh.ApplyLatestDefinition(actor);
            Check("level-up then rebalance cannot double-count growth", actor.MaxHP == 222 && actor.HP == 113 && actor.ATK == 39 && actor.DEF == 23);
            StatusBalanceRefresh.ApplyLatestDefinition(actor);
            Check("grown actor remains stable across repeated reloads", actor.MaxHP == 222 && actor.HP == 113 && actor.ATK == 39);

            definition.baseHP = 100; definition.baseATK = 20; definition.baseDEF = 10;
            definition.hpGrowth = definition.atkGrowth = definition.defGrowth = 0;
            definition.ApplyToStatus(actor, 1); actor.Experience = 0; actor.HP = 50;
            definition.baseHP = 300; definition.baseATK = 34; definition.baseDEF = 19;
            definition.hpGrowth = definition.atkGrowth = definition.defGrowth = .1f; GameContentRevision.NotifyChanged();
            // Gain XP before UnitSetting.Update gets a frame: the definition refresh must come first.
            actor.GainExperience(Status.XPRequiredForLevel(2));
            Check("XP arriving before the runtime updater applies both base stats and new growth", actor.Level == 2
                && actor.MaxHP == 330 && actor.HP == 180 && actor.ATK == 37 && actor.DEF == 21);
            StatusBalanceRefresh.ApplyLatestDefinition(actor);
            Check("late runtime updater does not duplicate the already applied pending revision", actor.MaxHP == 330 && actor.HP == 180
                && actor.DefinitionBalanceRevision == GameContentRevision.Revision);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(definition); }
    }

    sealed class MemoryCatalog : IDisposable
    {
        static FieldInfo Static(string name) => typeof(FacilityAuthoringCatalog).GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
        readonly object previous = Static("loaded").GetValue(null), previousLoaded = Static("didLoad").GetValue(null);
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        public readonly FacilityAuthoringCatalog Value;
        public MemoryCatalog()
        {
            Value = ScriptableObject.CreateInstance<FacilityAuthoringCatalog>(); owned.Add(Value);
            Static("loaded").SetValue(null, Value); Static("didLoad").SetValue(null, true); GameContentRevision.NotifyChanged();
        }
        public FacilityDefinitionData Definition(FacilityKind kind)
        {
            var value = ScriptableObject.CreateInstance<FacilityDefinitionData>(); owned.Add(value);
            value.definitionId = "studio.fixture." + owned.Count; value.behaviourKind = kind; return value;
        }
        public void Dispose()
        {
            Static("loaded").SetValue(null, previous); Static("didLoad").SetValue(null, previousLoaded);
            for (int i = owned.Count - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(owned[i]);
            GameContentRevision.NotifyChanged();
        }
    }
}
#endif
