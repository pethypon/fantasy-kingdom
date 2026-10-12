#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>Real AssetDatabase/Undo retirement fixtures, confined to a newly created validation folder.</summary>
public static class StudioDeletionTests
{
    static int passed;
    static void Check(string label, bool ok)
    {
        if (!ok) throw new InvalidOperationException("[StudioDeletion] FAIL " + label);
        passed++;
    }
    public static void EditMode()
    {
        if (!Application.dataPath.Contains("UnityValidation")) throw new InvalidOperationException("Deletion regression requires isolated project");
        passed = 0;
        using (var f = new Fixture())
        {
            UnitRetirement(f);
            BuildingRetirement(f);
            ReplacedDefinitionRestoration(f);
            ReferenceProtection(f);
            ProfileRetirement(f);
            DraftAndProtectedAssets(f);
            Check("no fixture damages formal runtime content", LongTermContentValidation.ValidateProject().IsValid);
        }
        Debug.Log("[StudioDeletion] " + passed + " retirement/reference/Undo/restore/identity checks passed");
    }
    static void UnitRetirement(Fixture f)
    {
        var unit = f.Unit("One");
        f.Units.units.Add(new UnitAuthoringCatalog.Entry { data = unit, prefab = f.Prefab, kind = unit.kind, standaloneDefinition = true });
        f.Save(); string path = AssetDatabase.GetAssetPath(unit), guid = AssetDatabase.AssetPathToGUID(path);
        Check("registered unit can be retired", StudioDeletion.TryDeleteUnit(unit, f.Units, f.Archive, out _));
        Check("retirement removes normal production and AI enumeration", f.Units.units.Count == 0 && !f.Units.EnumerateStandaloneDefinitions().Any());
        Check("retired definition keeps old save identity", f.Units.GetById(unit.definitionId) == unit && f.Units.retiredUnits.Count == 1);
        Check("asset identity and shared prefab survive retirement", AssetDatabase.LoadAssetAtPath<UnitData>(path) == unit
            && AssetDatabase.AssetPathToGUID(path) == guid && unit.prefab == f.Prefab && AssetDatabase.Contains(f.Prefab));
        int revision = GameContentRevision.Revision; Undo.PerformUndo();
        Check("Ctrl+Z restores catalog and removes deleted marker", f.Units.units.Count == 1 && f.Units.retiredUnits.Count == 0 && !f.Archive.IsDeleted(unit));
        Check("Undo invalidates content caches without an open studio window", GameContentRevision.Revision > revision);
        Undo.PerformRedo();
        Check("redo retires the same identity again", f.Units.units.Count == 0 && f.Archive.IsDeleted(unit) && f.Units.GetById(unit.definitionId) == unit);
        var copy = f.Unit("Duplicate"); copy.definitionId = unit.definitionId;
        bool rejected = false; try { StudioDeletion.RequireAvailableId(copy.definitionId, copy, f.Archive); } catch (ArgumentException) { rejected = true; }
        Check("deleted stable IDs cannot be reused by another object", rejected);
        Check("explicit restoration returns the original production registration", StudioDeletion.TryRestore(unit, f.Units, f.Buildings, f.Archive, out _)
            && f.Units.units.Single().standaloneDefinition && !f.Archive.IsDeleted(unit));
        f.Units.units.Clear();

        var standard = f.Unit("Standard"); f.Units.units.Add(new UnitAuthoringCatalog.Entry { data = standard, prefab = f.Prefab, kind = standard.kind });
        f.Save(); Check("standard unit override can be retired", StudioDeletion.TryDeleteUnit(standard, f.Units, f.Archive, out _));
        var map = new Dictionary<Kind, UnitData> { [Kind.Knight] = unit }; f.Units.Apply(map, null);
        Check("retiring override leaves standard role defaults unchanged", map[Kind.Knight] == unit);
        f.Units.units.Add(new UnitAuthoringCatalog.Entry { data = copy, kind = copy.kind, prefab = f.Prefab });
        Check("restore never overwrites a newer standard role", !StudioDeletion.TryRestore(standard, f.Units, f.Buildings, f.Archive, out string reason)
            && reason.Contains("標準兵種"));
        f.Units.units.Clear();
        string originalId = standard.definitionId; standard.definitionId = "changed.id";
        Check("restoring a changed archived ID is rejected", !StudioDeletion.TryRestore(standard, f.Units, f.Buildings, f.Archive, out reason)
            && reason.Contains("保存用ID"));
        standard.definitionId = originalId;
        Check("restored standard unit remains a standard override", StudioDeletion.TryRestore(standard, f.Units, f.Buildings, f.Archive, out _)
            && !f.Units.units.Single().standaloneDefinition);
        f.Units.units.Clear();
    }
    static void BuildingRetirement(Fixture f)
    {
        var building = f.Building("House"); building.behaviourKind = FacilityKind.House;
        building.buildCost = new FacilityData.ResourceCost(wood: 19); building.levels[0].SpecialValue = 8;
        f.Buildings.standardOverrides.Add(building); f.Save(); f.UseBuildings();
        Check("standard override is active before retirement", FacilityData.Table[FacilityKind.House].BuildCost.Wood == 19);
        string id = building.definitionId;
        Check("standard building can be retired", StudioDeletion.TryDeleteBuilding(building, f.Buildings, f.Archive, out _));
        f.UseBuildings();
        Check("retiring standard override restores built-in behavior", f.Buildings.standardOverrides.Count == 0
            && FacilityData.Table[FacilityKind.House].BuildCost.Wood != 19);
        Check("retired building still resolves old saves", f.Buildings.Find(id) == building && f.Buildings.retiredDefinitions.Contains(building));
        Undo.PerformUndo(); f.UseBuildings();
        Check("Ctrl+Z restores standard override and effective UI cost", f.Buildings.standardOverrides.Contains(building)
            && FacilityData.Table[FacilityKind.House].BuildCost.Wood == 19 && !f.Archive.IsDeleted(building));
        Undo.PerformRedo(); f.UseBuildings();
        var newer = f.Building("Newer"); newer.behaviourKind = FacilityKind.House; f.Buildings.standardOverrides.Add(newer);
        Check("restore does not silently replace another standard building", !StudioDeletion.TryRestore(building, f.Units, f.Buildings, f.Archive, out string reason)
            && reason.Contains("標準建物"));
        f.Buildings.standardOverrides.Clear();
        Check("explicit building restoration recovers its original mode", StudioDeletion.TryRestore(building, f.Units, f.Buildings, f.Archive, out _)
            && f.Buildings.standardOverrides.Contains(building) && !f.Buildings.buildings.Contains(building));
        f.Buildings.standardOverrides.Clear();

        var independent = f.Building("Independent"); f.Buildings.buildings.Add(independent); f.Save();
        Check("independent building retirement removes only its own row", StudioDeletion.TryDeleteBuilding(independent, f.Buildings, f.Archive, out _)
            && f.Buildings.buildings.Count == 0 && independent.prefab == f.Prefab);
        Check("independent building restore preserves independent identity", StudioDeletion.TryRestore(independent, f.Units, f.Buildings, f.Archive, out _)
            && f.Buildings.buildings.Contains(independent) && f.Buildings.standardOverrides.Count == 0);
        f.Buildings.buildings.Clear();
    }
    static void ReplacedDefinitionRestoration(Fixture f)
    {
        var oldUnit = f.Unit("ReplacedUnit"); var currentUnit = f.Unit("CurrentUnit");
        var unitModel = new GameObject("Retired unit prefab"); GameObject oldPrefab;
        try
        {
            unitModel.AddComponent<Status>().GrowthData = oldUnit;
            string folder = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(oldUnit)).Replace('\\', '/');
            oldPrefab = PrefabUtility.SaveAsPrefabAsset(unitModel, folder + "/RetiredUnit.prefab");
        }
        finally { UnityEngine.Object.DestroyImmediate(unitModel); }
        // The legacy workshop stores a standard unit's prefab on its catalog entry only.
        oldUnit.prefab = null;
        f.Units.retiredUnits.Add(new UnitAuthoringCatalog.Entry { data = oldUnit, prefab = oldPrefab, kind = oldUnit.kind });
        f.Units.units.Add(new UnitAuthoringCatalog.Entry { data = currentUnit, prefab = f.Prefab, kind = currentUnit.kind });
        f.Save();
        Check("already-retired standard unit can be hidden despite its own prefab reference",
            StudioDeletion.TryDeleteUnit(oldUnit, f.Units, f.Archive, out _));
        Check("hiding replaced unit keeps old save identity", f.Units.GetById(oldUnit.definitionId) == oldUnit);
        Check("restoring replaced unit keeps retirement and its original prefab", StudioDeletion.TryRestore(oldUnit, f.Units, f.Buildings, f.Archive, out _)
            && f.Units.GetById(oldUnit.definitionId) == oldUnit && f.Units.units.Single().data == currentUnit
            && f.Units.retiredUnits.Single(item => item.data == oldUnit).prefab == oldPrefab
            && !f.Units.retiredUnits.Single(item => item.data == oldUnit).standaloneDefinition
            && f.Units.TryGetPrefab(oldUnit, out var resolvedPrefab) && resolvedPrefab == oldPrefab);
        Undo.PerformUndo();
        Check("Undo of replaced unit restoration preserves save identity", f.Archive.IsDeleted(oldUnit)
            && f.Units.GetById(oldUnit.definitionId) == oldUnit && f.Units.units.Single().data == currentUnit);
        Undo.PerformRedo();
        Check("redo of replaced unit restoration keeps the newer standard role", !f.Archive.IsDeleted(oldUnit)
            && f.Units.GetById(oldUnit.definitionId) == oldUnit && f.Units.units.Single().data == currentUnit);
        f.Units.units.Clear();

        var oldBuilding = f.Building("ReplacedBuilding"); var currentBuilding = f.Building("CurrentBuilding");
        oldBuilding.behaviourKind = currentBuilding.behaviourKind = FacilityKind.House;
        f.Buildings.retiredDefinitions.Add(oldBuilding); f.Buildings.standardOverrides.Add(currentBuilding); f.Save();
        Check("already-retired standard building can be hidden", StudioDeletion.TryDeleteBuilding(oldBuilding, f.Buildings, f.Archive, out _));
        Check("restoring replaced building preserves old save resolution and current override",
            StudioDeletion.TryRestore(oldBuilding, f.Units, f.Buildings, f.Archive, out _)
            && f.Buildings.Find(oldBuilding.definitionId) == oldBuilding && f.Buildings.retiredDefinitions.Contains(oldBuilding)
            && f.Buildings.standardOverrides.Single() == currentBuilding && !f.Buildings.buildings.Contains(oldBuilding));
        Undo.PerformUndo();
        Check("Undo of replaced building restoration preserves save identity", f.Archive.IsDeleted(oldBuilding)
            && f.Buildings.Find(oldBuilding.definitionId) == oldBuilding);
        Undo.PerformRedo();
        Check("redo of replaced building restoration preserves retirement", !f.Archive.IsDeleted(oldBuilding)
            && f.Buildings.Find(oldBuilding.definitionId) == oldBuilding && f.Buildings.standardOverrides.Single() == currentBuilding);
        f.Buildings.standardOverrides.Clear();
    }
    static void ReferenceProtection(Fixture f)
    {
        var unit = f.Unit("Referenced");
        f.Units.units.Add(new UnitAuthoringCatalog.Entry { data = unit, kind = unit.kind, prefab = f.Prefab, standaloneDefinition = true });
        var raid = f.Asset<ThirdFactionEventDefinition>("Raid"); raid.Stats = unit; f.Save();
        Check("third-faction content references block deletion with an exact reason", !StudioDeletion.TryDeleteUnit(unit, f.Units, f.Archive, out string reason)
            && reason.Contains("Raid.asset") && f.Units.units.Count == 1 && !f.Archive.IsDeleted(unit));
        raid.Stats = null; EditorUtility.SetDirty(raid); f.Save();
        var sceneActor = new GameObject("Deletion live scene reference");
        try
        {
            sceneActor.AddComponent<Status>().GrowthData = unit;
            Check("unsaved scene references are protected", !StudioDeletion.TryDeleteUnit(unit, f.Units, f.Archive, out reason)
                && reason.Contains("開いているシーン"));
        }
        finally { UnityEngine.Object.DestroyImmediate(sceneActor); }
        Check("deletion becomes possible after removing actual references", StudioDeletion.TryDeleteUnit(unit, f.Units, f.Archive, out _));
    }
    static void ProfileRetirement(Fixture f)
    {
        var unit = f.Unit("ProfileOwner"); var other = f.Unit("ProfileOther"); var profile = f.Asset<BoardActionProfile>("SharedProfile");
        unit.actionProfile = other.actionProfile = profile; f.Save();
        Check("shared action masks are protected", !StudioDeletion.TryDeleteProfile(profile, unit, f.Archive, out string reason)
            && reason.Contains("ProfileOther.asset") && unit.actionProfile == profile);
        other.actionProfile = null; EditorUtility.SetDirty(other); f.Save();
        Check("unshared action setting can be retired and detached", StudioDeletion.TryDeleteProfile(profile, unit, f.Archive, out _)
            && unit.actionProfile == null && f.Archive.IsDeleted(profile) && AssetDatabase.Contains(profile));
        Undo.PerformUndo();
        Check("Ctrl+Z restores the original action mask assignment", unit.actionProfile == profile && !f.Archive.IsDeleted(profile));
        Undo.PerformRedo();
        Check("explicit mask restore can assign to another authored owner", StudioDeletion.TryRestoreProfile(profile, other, f.Archive, out _)
            && other.actionProfile == profile && !f.Archive.IsDeleted(profile));
        unit.actionProfile = null; other.actionProfile = null; f.Save();
    }
    static void DraftAndProtectedAssets(Fixture f)
    {
        var draft = f.Unit("Draft");
        Check("unregistered drafts can be hidden and kept recoverable", StudioDeletion.TryDeleteUnit(draft, f.Units, f.Archive, out _)
            && f.Archive.IsDeleted(draft));
        int active = f.Units.units.Count;
        Check("draft restoration does not implicitly register it in game", StudioDeletion.TryRestore(draft, f.Units, f.Buildings, f.Archive, out _)
            && f.Units.units.Count == active && !f.Archive.IsDeleted(draft));
        var legacy = UnitStaticData.CreateUnitData(Kind.Knight);
        try
        {
            Check("built-in and external data cannot be deleted", !StudioDeletion.TryDeleteUnit(legacy, f.Units, f.Archive, out string reason)
                && reason.Contains("標準データ"));
        }
        finally { UnityEngine.Object.DestroyImmediate(legacy); }
        Check("sharing a prefab never deletes or replaces it", AssetDatabase.Contains(f.Prefab) && f.Prefab.GetComponentsInChildren<Status>(true).Length == 1);
    }

    sealed class Fixture : IDisposable
    {
        static FieldInfo Static(string name) => typeof(FacilityAuthoringCatalog).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
        readonly object oldCatalog = Static("loaded").GetValue(null), oldLoaded = Static("didLoad").GetValue(null);
        readonly string folder;
        public readonly UnitAuthoringCatalog Units;
        public readonly FacilityAuthoringCatalog Buildings;
        public readonly StudioArchiveData Archive;
        public readonly GameObject Prefab;
        public Fixture()
        {
            UnitWorkshopWindow.EnsureFolder(GameDevelopmentStudioWindow.ContentFolder);
            folder = GameDevelopmentStudioWindow.ContentFolder + "/DeletionRegression_" + Guid.NewGuid().ToString("N");
            string parent = GameDevelopmentStudioWindow.ContentFolder;
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
            Units = Asset<UnitAuthoringCatalog>("Units"); Buildings = Asset<FacilityAuthoringCatalog>("Buildings"); Archive = Asset<StudioArchiveData>("Archive");
            var model = new GameObject("Shared deletion fixture prefab");
            try { model.AddComponent<Status>(); Prefab = PrefabUtility.SaveAsPrefabAsset(model, folder + "/Shared.prefab"); }
            finally { UnityEngine.Object.DestroyImmediate(model); }
            Save();
        }
        public T Asset<T>(string name) where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset, folder + "/" + name + ".asset"); return asset;
        }
        public UnitData Unit(string name)
        {
            var unit = UnitStaticData.CreateUnitData(Kind.Knight); unit.definitionId = "deletion.unit." + name;
            unit.prefab = Prefab; AssetDatabase.CreateAsset(unit, folder + "/" + name + ".asset"); return unit;
        }
        public FacilityDefinitionData Building(string name)
        {
            var building = Asset<FacilityDefinitionData>(name); building.definitionId = "deletion.building." + name; building.prefab = Prefab; return building;
        }
        public void Save()
        {
            EditorUtility.SetDirty(Units); EditorUtility.SetDirty(Buildings); EditorUtility.SetDirty(Archive);
            foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { folder }))
                EditorUtility.SetDirty(AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid)));
            AssetDatabase.SaveAssets(); Undo.FlushUndoRecordObjects();
        }
        public void UseBuildings()
        {
            Static("loaded").SetValue(null, Buildings); Static("didLoad").SetValue(null, true); GameContentRevision.NotifyChanged();
        }
        public void Dispose()
        {
            // Only the fresh, exact fixture folder may be cleaned. Production archives and IDs remain untouched.
            if (folder.StartsWith(GameDevelopmentStudioWindow.ContentFolder + "/DeletionRegression_", StringComparison.Ordinal))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { folder }))
                    Undo.ClearUndo(AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid)));
                AssetDatabase.DeleteAsset(folder);
            }
            Static("loaded").SetValue(null, oldCatalog); Static("didLoad").SetValue(null, oldLoaded); GameContentRevision.NotifyChanged();
        }
    }
}
#endif
