#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>Paid paths use an actual saved prefab that destroys its clone in Awake.</summary>
public static class BuildingTransactionTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
    public static void Run(GameSystems live)
    {
        int checks = 0;
        void Check(bool passed, string reason) { if (!passed) throw new InvalidOperationException("[BuildingTransaction] " + reason); checks++; }
        using (var fixture = new Fixture(live))
        {
            int events = 0; fixture.AP.OnNonMoveAction += _ => events++;
            void Failure(Team team, Func<bool> place, string label)
            {
                string resources = JsonUtility.ToJson(fixture.State.GetResources(team)); int ap = fixture.State.GetAP(team);
                int childCount = fixture.Builder.GetBuildingParent(team).childCount;
                int subCrystals = fixture.State.GetSubCrystals(team), beforeEvents = events;
                Check(!place(), label + " returns failure for the broken prefab");
                Check(resources == JsonUtility.ToJson(fixture.State.GetResources(team)), label + " resources unchanged");
                Check(fixture.State.GetAP(team) == ap, label + " AP unchanged");
                Check(fixture.State.GetSubCrystals(team) == subCrystals, label + " sub-crystals unchanged");
                Check(events == beforeEvents, label + " no successful non-move action event");
                Check(fixture.Builder.GetBuildingParent(team).childCount == childCount, label + " no orphan building shell");
                Check(!fixture.Builder.HasBuildingAt(fixture.Cell), label + " no ghost occupied cell");
            }
            var builder = fixture.Builder;
            Failure(Team.Enemy, () => builder.AIPlaceBuilding(fixture.Cell, FacilityKind.Field, Team.Enemy), "enemy standard purchase");
            Failure(Team.Player, () => builder.AIPlaceBuilding(fixture.Cell, FacilityKind.Field, Team.Player), "developer player AI standard purchase");
            builder.StartBuildMode(FacilityKind.Field);
            Check(builder.IsActive, "manual build mode opens in the isolated hub");
            var cursor = (BuildCursorController)typeof(BuildSystem).GetField("cursor", Private).GetValue(builder);
            cursor.UpdatePosition(fixture.Cell, true);
            Failure(Team.Player, builder.TryPlace, "manual standard purchase");
            Check(builder.IsActive, "failed manual purchase keeps mode available for retry");
            Failure(Team.Player, () => builder.TryPlaceDefinition(fixture.Cell, fixture.Definition, Team.Player), "authored player purchase");
            Failure(Team.Enemy, () => builder.TryPlaceDefinition(fixture.Cell, fixture.Definition, Team.Enemy), "authored enemy purchase");
            Check(builder.GetLastPlacedBuilding() == null, "failed purchases cannot publish a last placed building");
            builder.CancelBuildMode();
            fixture.Definition.prefab = fixture.HealthyPrefab;
            var info = fixture.Definition.GetInfo(); var before = JsonUtility.FromJson<FactionState.ResourceData>(JsonUtility.ToJson(fixture.State.EnemyResources));
            int beforeAP = fixture.State.GetAP(Team.Enemy), beforeEvent = events;
            Check(builder.AIPlaceBuilding(fixture.Cell, FacilityKind.Field, Team.Enemy), "healthy prefab still succeeds");
            var after = fixture.State.EnemyResources;
            Check(after.Wood == before.Wood - info.BuildCost.Wood && after.Stone == before.Stone - info.BuildCost.Stone
                && after.Iron == before.Iron - info.BuildCost.Iron && after.MagicOre == before.MagicOre - info.BuildCost.MagicOre
                && after.Water == before.Water - info.BuildCost.Water && after.Citizen == before.Citizen - info.BuildCost.Citizen,
                "healthy purchase consumes every configured resource exactly once");
            Check(fixture.State.GetAP(Team.Enemy) == beforeAP - info.APCost && events == beforeEvent + 1, "healthy purchase consumes AP and emits one committed action");
            var created = builder.GetLastPlacedBuilding(); var status = created == null ? null : created.GetComponent<Status>();
            Check(status != null && status.facilityKind == FacilityKind.Field && status.team == Team.Enemy && status.type == Type.Building,
                "healthy purchase keeps normal facility/team/status configuration");
            Check(builder.HasBuildingAt(fixture.Cell) && builder.EnemyBuildingParent.childCount == 1, "successful building is registered in the original cell");
        }
        Debug.Log("[BuildingTransaction] ALL PASSED checks=" + checks + " actualBrokenPrefab=AwakeDestroyed routes=5");
    }

    sealed class Fixture : IDisposable
    {
        readonly GameObject root;
        readonly BuildSystem oldBuildRef;
        readonly AIMode oldMode;
        readonly object oldCatalog, oldLoaded;
        readonly string folder;
        readonly FacilityAuthoringCatalog catalog;
        public readonly FactionState State;
        public readonly APSystem AP;
        public readonly BuildSystem Builder;
        public readonly FacilityDefinitionData Definition;
        public readonly GameObject HealthyPrefab;
        public readonly Vector3Int Cell;
        readonly GameSystems live;
        static FieldInfo CatalogField(string name) => typeof(FacilityAuthoringCatalog).GetField(name, StaticPrivate);

        public Fixture(GameSystems systems)
        {
            live = systems; oldBuildRef = live.MoveGenerator.BuildSystemRef; oldMode = AIConfig.Mode;
            oldCatalog = CatalogField("loaded").GetValue(null); oldLoaded = CatalogField("didLoad").GetValue(null);
            folder = "Assets/_FK_BuildTransaction_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            root = new GameObject("Isolated paid construction fixture");
            catalog = ScriptableObject.CreateInstance<FacilityAuthoringCatalog>();
            Definition = ScriptableObject.CreateInstance<FacilityDefinitionData>();
            try
            {
                var turn = Child("Hub").AddComponent<TurnGenerator>(); turn.enabled = false; turn.MarkDeveloperMatch();
                var territory = Child("Territory").AddComponent<TerritorySystem>();
                foreach (var position in live.MapCreate.SetPos)
                    if (!GridHelper.MatchXZ(position, GridHelper.ToGrid(live.CrystalSystem.PCP))
                        && !GridHelper.MatchXZ(position, GridHelper.ToGrid(live.CrystalSystem.ECP)))
                    { Cell = GridHelper.ToGrid(position); break; }
                territory.PTSetPos = new List<Vector3> { Cell }; territory.ETSetPos = new List<Vector3> { Cell };
                var crystals = Child("Crystals").AddComponent<CrystalSystem>(); crystals.PCP = live.CrystalSystem.PCP; crystals.ECP = live.CrystalSystem.ECP;
                State = Child("State").AddComponent<FactionState>();
                State.PlayerNation = Child("Player nation").AddComponent<NationState>(); State.EnemyNation = Child("Enemy nation").AddComponent<NationState>();
                foreach (var team in new[] { Team.Player, Team.Enemy })
                {
                    var resources = State.GetResources(team);
                    resources.Wood = resources.Stone = resources.Iron = resources.MagicOre = resources.Water = resources.Bread = resources.Wheat = 400;
                    resources.Citizen = 5; State.SetAP(team, 40);
                }
                AP = Child("AP").AddComponent<APSystem>(); AP.Init(State);
                Definition.definitionId = "test.paid-construction"; Definition.behaviourKind = FacilityKind.Field;
                Definition.buildAP = 4; Definition.buildCost = new FacilityData.ResourceCost(wood: 9, stone: 7, iron: 3, magicOre: 1, water: 2, citizen: 1);
                Definition.levels = new[] { new FacilityData.FacilityLevelData { HP = 100 } };
                Definition.prefab = MakePrefab("Broken", true); HealthyPrefab = MakePrefab("Healthy", false);
                catalog.standardOverrides.Add(Definition); CatalogField("loaded").SetValue(null, catalog); CatalogField("didLoad").SetValue(null, true);
                GameContentRevision.NotifyChanged();
                Builder = Child("Builder").AddComponent<BuildSystem>();
                Builder.PlayerBuildingParent = Child("Player buildings").transform; Builder.EnemyBuildingParent = Child("Enemy buildings").transform;
                turn.Systems.CrystalSystem = crystals; turn.Systems.MapCreate = live.MapCreate; turn.Systems.MoveGenerator = live.MoveGenerator;
                turn.Systems.BuildSystem = Builder; turn.Systems.APSystem = AP; turn.Systems.FactionState = State; turn.Systems.TerritorySystem = territory;
                Builder.Init(turn, territory, AP, State, live.MoveGenerator, live.MapCreate);
            }
            catch { Dispose(); throw; }
        }
        GameObject Child(string name) { var go = new GameObject(name); go.transform.SetParent(root.transform); return go; }
        GameObject MakePrefab(string name, bool fail)
        {
            var template = GameObject.CreatePrimitive(PrimitiveType.Cube); template.name = name + " paid construction prefab";
            try
            {
                // Add while unarmed: its first Awake leaves the original template
                // alive. Saving the armed bool makes only its newly-created clone
                // fail during Unity's real Instantiate lifecycle.
                var failure = template.AddComponent<FailedBuildingPrefab>(); failure.destroyCloneOnAwake = fail;
                var result = PrefabUtility.SaveAsPrefabAsset(template, folder + "/" + name + ".prefab");
                if (result == null) throw new InvalidOperationException("Could not create the actual failed-prefab fixture.");
                return result;
            }
            finally { if (template != null) UnityEngine.Object.DestroyImmediate(template); }
        }
        public void Dispose()
        {
            try
            {
                if (root != null) foreach (var actor in root.GetComponentsInChildren<Status>(true)) UnitRegistry.Instance?.Unregister(actor);
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }
            finally
            {
                live.MoveGenerator.BuildSystemRef = oldBuildRef; AIConfig.Mode = oldMode;
                CatalogField("loaded").SetValue(null, oldCatalog); CatalogField("didLoad").SetValue(null, oldLoaded); GameContentRevision.NotifyChanged();
                if (Definition != null) UnityEngine.Object.DestroyImmediate(Definition);
                if (catalog != null) UnityEngine.Object.DestroyImmediate(catalog);
                // This unique generated folder was created by this fixture only.
                // No user asset or pre-existing directory is removed.
                if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
#endif
