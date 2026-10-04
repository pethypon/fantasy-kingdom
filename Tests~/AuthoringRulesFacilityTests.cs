using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Called by the isolated Editor validation runner; creates no project assets.</summary>
public static class AuthoringRulesFacilityTests
{
    static void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException("[AuthoringRulesFacilities] FAIL " + message);
        Debug.Log("[AuthoringRulesFacilities] PASS " + message);
    }
    static FieldInfo StaticField(System.Type type, string name) => type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic);

    public static void EditMode()
    {
        var rules = ScriptableObject.CreateInstance<GameAuthoringRules>();
        var definition = ScriptableObject.CreateInstance<FacilityDefinitionData>();
        var restored = ScriptableObject.CreateInstance<FacilityDefinitionData>();
        var other = ScriptableObject.CreateInstance<FacilityDefinitionData>();
        var catalog = ScriptableObject.CreateInstance<FacilityAuthoringCatalog>();
        try
        {
            definition.definitionId = "test.forge.one"; definition.displayName = "試験鍛冶場";
            definition.behaviourKind = FacilityKind.Mine;
            definition.buildCost = new FacilityData.ResourceCost(wood: 7, stone: 3);
            definition.levels = new[] { new FacilityData.FacilityLevelData { HP = 150, Output = new FacilityData.ProductionBundle { Iron = 13 },
                Input = new FacilityData.ProductionBundle { Wood = 2 }, BonusChance1 = .25f, BonusOutput1 = new FacilityData.ProductionBundle { MagicOre = 1 } } };
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(definition), restored);
            Check(restored.definitionId == definition.definitionId && restored.buildCost.Wood == 7
                && restored.levels[0].Input.Wood == 2 && restored.levels[0].Output.Iron == 13
                && restored.levels[0].BonusChance1 == .25f, "building costs, levels and production serialize");
            other.definitionId = "test.forge.two"; other.behaviourKind = FacilityKind.Mine;
            catalog.buildings.Add(definition); catalog.buildings.Add(other);
            Check(catalog.Find(definition.definitionId) == definition && catalog.Find(other.definitionId) == other,
                "two buildings with the same behaviour keep independent IDs");
            definition.buildCost = new FacilityData.ResourceCost(wood: -10);
            definition.levels[0].HP = -1;
            Check(definition.GetInfo().BuildCost.Wood == 0 && definition.GetLevel(1).HP == 1, "invalid negative build data cannot grant resources");
            Check(definition.GetLevel(999).HP == 1 && definition.GetLevel(0).HP == 1, "level queries clamp safely");
            Check(!definition.IsAvailable(Team.Monster) && !definition.IsAvailable(Team.Obstacle), "principal economy cannot be charged to neutral teams");

            var loadedField = StaticField(typeof(GameAuthoringRules), "loaded");
            var didLoadField = StaticField(typeof(GameAuthoringRules), "didLoad");
            var oldLoaded = loadedField.GetValue(null); var oldDidLoad = didLoadField.GetValue(null);
            try
            {
                loadedField.SetValue(null, rules); didLoadField.SetValue(null, true);
                Check(GameAuthoringRules.Active == null && APSystem.BaseMoveCost == 3 && APSystem.BaseAttackCost == 2, "disabled authoring preserves legacy action costs");
                rules.applyRules = true; rules.moveAP = 7; rules.attackAP = 4; rules.citizenAP = 2;
                Check(APSystem.BaseMoveCost == 7 && APSystem.BaseAttackCost == 4 && EconomySystem.CitizenAPBonus == 2, "enabled rules reach action and economy calculations");
                rules.moveAP = -4; rules.attackAP = 100;
                Check(APSystem.BaseMoveCost == 1 && APSystem.BaseAttackCost == 50, "AP rule values have runtime limits");
            }
            finally { loadedField.SetValue(null, oldLoaded); didLoadField.SetValue(null, oldDidLoad); }

            var save = new SaveSystem.UnitSaveData { AuthoredFacilityId = "test.forge.one", FacilityKind = FacilityKind.Mine.ToString() };
            Check(JsonUtility.FromJson<SaveSystem.UnitSaveData>(JsonUtility.ToJson(save)).AuthoredFacilityId == save.AuthoredFacilityId, "save keeps authored building identity");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(rules); UnityEngine.Object.DestroyImmediate(definition);
            UnityEngine.Object.DestroyImmediate(restored); UnityEngine.Object.DestroyImmediate(other); UnityEngine.Object.DestroyImmediate(catalog);
        }
    }

    public static void PlayMode(GameSystems systems)
    {
        var objects = new List<GameObject>();
        var definitions = new List<FacilityDefinitionData>();
        var catalog = ScriptableObject.CreateInstance<FacilityAuthoringCatalog>();
        var rules = ScriptableObject.CreateInstance<GameAuthoringRules>();
        var catalogField = StaticField(typeof(FacilityAuthoringCatalog), "loaded");
        var catalogDidLoad = StaticField(typeof(FacilityAuthoringCatalog), "didLoad");
        var rulesField = StaticField(typeof(GameAuthoringRules), "loaded");
        var rulesDidLoad = StaticField(typeof(GameAuthoringRules), "didLoad");
        var oldCatalog = catalogField.GetValue(null); var oldCatalogDidLoad = catalogDidLoad.GetValue(null);
        var oldRules = rulesField.GetValue(null); var oldRulesDidLoad = rulesDidLoad.GetValue(null);
        var oldBuildRef = systems.MoveGenerator.BuildSystemRef;
        var actors = new List<Status>();
        BuildSystem builder = null;
        try
        {
            catalogField.SetValue(null, catalog); catalogDidLoad.SetValue(null, true);
            rulesField.SetValue(null, rules); rulesDidLoad.SetValue(null, true);
            GameObject CreateObject(string name) { var go = new GameObject(name); objects.Add(go); return go; }
            var state = CreateObject("Authoring test factions").AddComponent<FactionState>();
            state.PlayerNation = CreateObject("Test player nation").AddComponent<NationState>();
            state.EnemyNation = CreateObject("Test enemy nation").AddComponent<NationState>();
            state.PlayerResources.Wood = 100; state.PlayerResources.Stone = 30; state.PlayerResources.Bread = 100;
            state.PlayerAP.Current = 30;
            rules.FindFaction(Team.Player).initialAP = 22; rules.FindFaction(Team.Monster).initialAP = 17;
            rules.FindFaction(Team.Player).initialResources.Wood = 99;
            rules.ApplyNewGame(state);
            Check(state.PlayerAP.Reset == 30 && state.PlayerResources.Wood == 100, "disabled new-game rules do not overwrite state");
            rules.applyRules = true; rules.ApplyNewGame(state);
            Check(state.PlayerAP.Reset == 22 && state.MonsterAP.Reset == 17 && state.PlayerResources.Wood == 99, "new-game faction AP and resources are editable");
            rules.turnSeconds = 45; rules.totalSeconds = 1000; rules.timeBonusSeconds = 5;
            var timer = CreateObject("Authoring test timer").AddComponent<TimerSystem>();
            timer.Init(null, null); timer.StartTurn(Team.Player); timer.StopTurn(); timer.StopTurn();
            Check(timer.TurnTimeRemaining == 45 && timer.PlayerTotalTime == 1005 && timer.EnemyTotalTime == 1000, "turn time and manual end bonus apply once");
            var savedTimer = new SaveSystem.TimerSaveData { TurnTimeLimit = 91, PlayerTotalTime = 72, EnemyTotalTime = 83, TurnTimeRemaining = 19 };
            SaveSystem.RestoreTimer(savedTimer, timer);
            Check(timer.TurnTimeLimit == 91 && timer.TurnTimeRemaining == 19 && timer.PlayerTotalTime == 72, "saved timer overrides new-game rules");
            rules.applyRules = false;

            var ap = CreateObject("Authoring test AP").AddComponent<APSystem>(); ap.Init(state);
            builder = CreateObject("Authoring test builder").AddComponent<BuildSystem>();
            builder.Init(UnityEngine.Object.FindFirstObjectByType<TurnGenerator>(), systems.TerritorySystem, ap, state, systems.MoveGenerator, systems.MapCreate);
            objects.Add(builder.PlayerBuildingParent.gameObject); objects.Add(builder.EnemyBuildingParent.gameObject);
            var occupied = new HashSet<Vector3Int>(); CombatRegistry.Collect(actors);
            foreach (var actor in actors) occupied.Add(GridHelper.ToGridXZ(actor.transform.position)); actors.Clear();
            var cells = new List<Vector3Int>();
            foreach (var position in systems.TerritorySystem.GetTerritory(Team.Player))
            {
                var cell = GridHelper.ToGrid(position);
                if (systems.MapCreate.HasTileAt(cell.x, cell.z) && !occupied.Contains(GridHelper.ToGridXZ(position))
                    && !systems.BuildSystem.HasBuildingAt(cell) && !GridHelper.MatchXZ(position, GridHelper.ToGridXZ(systems.CrystalSystem.PCP))
                    && !GridHelper.MatchXZ(position, GridHelper.ToGridXZ(systems.CrystalSystem.ECP))) cells.Add(cell);
                if (cells.Count == 3) break;
            }
            Check(cells.Count >= 3, "fixture has three free legal building cells");
            for (int i = 0; i < 2; i++)
            {
                var definition = ScriptableObject.CreateInstance<FacilityDefinitionData>(); definitions.Add(definition);
                definition.definitionId = "test.sawmill." + i; definition.displayName = "試験製材所 " + i;
                definition.behaviourKind = FacilityKind.LoggingCamp; definition.buildAP = 2;
                definition.buildCost = new FacilityData.ResourceCost(wood: 7);
                definition.levels = new[] {
                    new FacilityData.FacilityLevelData { HP = 150 + i, Output = new FacilityData.ProductionBundle { Wood = 13 + i } },
                    new FacilityData.FacilityLevelData { HP = 180 + i, Output = new FacilityData.ProductionBundle { Wood = 25 + i },
                        UpgradeAP = 4, UpgradeCost = new FacilityData.ResourceCost(stone: 3) } };
                catalog.buildings.Add(definition);
                int beforeAP = state.PlayerAP.Current, beforeWood = state.PlayerResources.Wood;
                Check(builder.TryPlaceDefinition(cells[i], definition, Team.Player), "custom building can be constructed");
                var built = builder.GetLastPlacedBuilding().GetComponent<Status>(); actors.Add(built);
                Check(state.PlayerAP.Current == beforeAP - 2 && state.PlayerResources.Wood == beforeWood - 7
                    && built.AuthoredFacility == definition && built.HP == 150 + i, "build charges authored costs and applies exact definition");
            }
            int failedAP = state.PlayerAP.Current, failedWood = state.PlayerResources.Wood;
            Check(!builder.TryPlaceDefinition(cells[0], definitions[0], Team.Player)
                && state.PlayerAP.Current == failedAP && state.PlayerResources.Wood == failedWood, "invalid placement does not consume AP or resources");
            int upgradeStone = state.PlayerResources.Stone, upgradeAP = state.PlayerAP.Current;
            Check(builder.TryUpgrade(actors[0]) && actors[0].Level == 2 && actors[0].MaxHP == 180
                && state.PlayerAP.Current == upgradeAP - 4 && state.PlayerResources.Stone == upgradeStone - 3, "upgrade uses the chosen building levels and costs");
            var economy = CreateObject("Authoring test economy").AddComponent<EconomySystem>(); economy.Init(builder, state);
            int woodBeforeProduction = state.PlayerResources.Wood;
            economy.ProcessTurn(Team.Player);
            Check(state.PlayerResources.Wood == woodBeforeProduction + 20 + 25 + 14, "same-behaviour buildings produce their own configured amounts");
            var saved = SaveSystem.CaptureUnit(actors[1]);
            Check(saved.AuthoredFacilityId == definitions[1].definitionId, "live building saves its stable authored ID");
            var loaded = builder.PlaceBuildingForLoad(cells[2], FacilityKind.LoggingCamp, Team.Player, definitions[1].definitionId);
            actors.Add(loaded); SaveGameApplier.ApplyStatusFields(loaded, saved);
            Check(loaded.AuthoredFacility == definitions[1] && loaded.authoredFacilityId == definitions[1].definitionId
                && loaded.HP == actors[1].HP, "load resolves the exact same-behaviour definition");
        }
        finally
        {
            foreach (var actor in actors) if (actor != null) UnitRegistry.Instance?.Unregister(actor);
            systems.MoveGenerator.BuildSystemRef = oldBuildRef;
            foreach (var go in objects) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            foreach (var definition in definitions) UnityEngine.Object.DestroyImmediate(definition);
            UnityEngine.Object.DestroyImmediate(catalog); UnityEngine.Object.DestroyImmediate(rules);
            catalogField.SetValue(null, oldCatalog); catalogDidLoad.SetValue(null, oldCatalogDidLoad);
            rulesField.SetValue(null, oldRules); rulesDidLoad.SetValue(null, oldRulesDidLoad);
            systems.MoveGenerator.UnitPointCore(); systems.VisionGenerator?.MarkVisionDirty();
        }
    }
}
