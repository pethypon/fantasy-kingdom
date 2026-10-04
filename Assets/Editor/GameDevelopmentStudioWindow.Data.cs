using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed partial class GameDevelopmentStudioWindow
{
    static string NewFolder(string category, string title)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) title = title.Replace(c, '_');
        title = title.Trim().TrimEnd('.');
        if (string.IsNullOrEmpty(title)) title = "新しい設定";
        string parent = ContentFolder + "/" + category;
        UnitWorkshopWindow.EnsureFolder(parent);
        string folder = AssetDatabase.GenerateUniqueAssetPath(parent + "/" + title);
        UnitWorkshopWindow.EnsureFolder(folder);
        return folder;
    }
    public static UnitData CreateUnitDefinition(string title)
    {
        string folder = NewFolder("Units", title);
        var data = UnitStaticData.CreateUnitData(Kind.Knight);
        if (data == null) { data = CreateInstance<UnitData>(); data.kind = Kind.Knight; data.baseHP = 32; data.baseATK = 10; data.baseDEF = 6; }
        data.hideFlags = HideFlags.None;
        data.definitionId = "unit-" + Guid.NewGuid().ToString("N");
        data.displayName = title; data.category = "戦闘";
        AssetDatabase.CreateAsset(data, folder + "/Stats.asset");
        data.actionProfile = CreateProfile(folder + "/Stats.asset", data.kind, false);
        data.prefab = CreateVisualPrefab(data, null, null);
        EditorUtility.SetDirty(data); RegisterUnit(data); AssetDatabase.SaveAssets();
        return data;
    }
    public static FacilityDefinitionData CreateBuildingDefinition(string title)
    {
        string folder = NewFolder("Buildings", title);
        return CreateBuildingFromRole(folder,title,FacilityKind.LoggingCamp,true);
    }
    static FacilityDefinitionData CreateBuildingFromRole(string folder,string title,FacilityKind kind,bool register)
    {
        var data = CreateInstance<FacilityDefinitionData>();
        data.definitionId = "building-" + Guid.NewGuid().ToString("N");
        data.displayName = title; data.behaviourKind = kind;
        var info = FacilityData.Table[data.behaviourKind];
        data.buildCost = info.BuildCost; data.buildAP = info.APCost;
        data.levels = new FacilityData.FacilityLevelData[info.MaxLevel];
        for (int i = 0; i < data.levels.Length; i++) data.levels[i] = FacilityData.GetLevel(data.behaviourKind, i + 1);
        AssetDatabase.CreateAsset(data, folder + "/Building.asset");
        data.actionProfile = CreateProfile(folder + "/Building.asset", BuildingRole(data.behaviourKind), true);
        // Buildings retain the original automatic attack/vision rules until explicitly customized.
        data.actionProfile.attack.useCustom = false; data.actionProfile.vision.useCustom = false;
        data.prefab = CreateVisualPrefab(null, data, null);
        EditorUtility.SetDirty(data); if(register)RegisterBuilding(data); AssetDatabase.SaveAssets();
        return data;
    }
    public static void ImportLegacyBuildings()
    {
        string folder = ContentFolder + "/Buildings/既存の建物データ";
        UnitWorkshopWindow.EnsureFolder(folder);
        foreach(var pair in FacilityData.Table)
        {
            string entryFolder=folder+"/"+pair.Key; string assetPath=entryFolder+"/Building.asset";
            if(AssetDatabase.LoadAssetAtPath<FacilityDefinitionData>(assetPath)!=null)continue;
            UnitWorkshopWindow.EnsureFolder(entryFolder);
            var data=CreateBuildingFromRole(entryFolder,pair.Value.DisplayName,pair.Key,false);
            data.category="既存の建物（編集用の複製）";EditorUtility.SetDirty(data);
        }
        AssetDatabase.SaveAssets();
    }
    public static BoardActionProfile CreateProfile(string ownerPath, Kind role, bool building)
    {
        string folder = Path.GetDirectoryName(ownerPath).Replace('\\', '/');
        var profile = CreateInstance<BoardActionProfile>();
        profile.actionType = building ? ActorActionType.Stationary : ActorActionType.All;
        var patterns = new[] { profile.movement, profile.attack, profile.vision };
        for (int i = 0; i < patterns.Length; i++)
        {
            patterns[i].useCustom = true;
            DevelopmentStudioTiles.FillLegacy(patterns[i], role, i);
        }
        if (building) profile.movement.cells = 0;
        AssetDatabase.CreateAsset(profile, AssetDatabase.GenerateUniqueAssetPath(folder + "/ActionTiles.asset"));
        return profile;
    }
    public static void RegisterUnit(UnitData data)
    {
        if (data == null || !data.IsValidForAuthoring || data.kind == Kind.None)
            throw new ArgumentException("HP・能力・費用、基礎役割を確認してください。");
        if (data.prefab == null || data.prefab.GetComponentsInChildren<Status>(true).Length != 1)
            throw new ArgumentException("駒１体分のStatusを持つPrefabが必要です。「基本形のPrefabを作成」から作れます。");
        if (string.IsNullOrWhiteSpace(data.definitionId)) { Undo.RecordObject(data,"保存用ID発行"); data.definitionId = "unit-" + Guid.NewGuid().ToString("N"); EditorUtility.SetDirty(data); }
        var catalog = UnitWorkshopWindow.GetCatalog();
        foreach (var entry in catalog.units)
            if (entry?.data != null && entry.data != data && entry.data.definitionId == data.definitionId)
                throw new ArgumentException("別の駒と保存用IDが重複しています。「選択した設定を複製」で新しく作成してください。");
        Undo.RecordObject(catalog,"駒を追加登録");
        var existing = catalog.units.Find(e => e != null && e.data == data);
        if (existing == null) { existing = new UnitAuthoringCatalog.Entry(); catalog.units.Add(existing); }
        existing.kind = data.kind; existing.data = data; existing.prefab = data.prefab; existing.standaloneDefinition = true;
        EditorUtility.SetDirty(catalog);
    }
    static FacilityAuthoringCatalog GetBuildingCatalog()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<FacilityAuthoringCatalog>(FacilityCatalogPath);
        if (catalog != null) return catalog;
        UnitWorkshopWindow.EnsureFolder("Assets/Resources/GameContent");
        catalog = CreateInstance<FacilityAuthoringCatalog>(); AssetDatabase.CreateAsset(catalog, FacilityCatalogPath); return catalog;
    }
    public static void RegisterBuilding(FacilityDefinitionData data)
    {
        if (data == null) throw new ArgumentException("建物が選択されていません。");
        if (string.IsNullOrWhiteSpace(data.definitionId)) { Undo.RecordObject(data,"保存用ID発行"); data.definitionId = "building-" + Guid.NewGuid().ToString("N"); EditorUtility.SetDirty(data); }
        if (!data.IsValid || data.prefab == null || data.prefab.GetComponentsInChildren<Status>(true).Length != 1)
            throw new ArgumentException("レベル設定と、Statusを１つ持つPrefabを確認してください。");
        var catalog = GetBuildingCatalog();
        foreach (var item in catalog.buildings)
            if (item != null && item != data && item.definitionId == data.definitionId)
                throw new ArgumentException("建物の保存用IDが重複しています。開発スタジオの複製機能を使ってください。");
        if (!catalog.buildings.Contains(data)) { Undo.RecordObject(catalog,"建物を追加登録"); catalog.buildings.Add(data); EditorUtility.SetDirty(catalog); }
    }
    void DuplicateSelected()
    {
        if (tab == 0 && selectedUnit != null)
        {
            var source = selectedUnit; string folder = NewFolder("Units", source.DisplayName + " 複製");
            var copy = Instantiate(source); copy.definitionId = "unit-" + Guid.NewGuid().ToString("N"); copy.displayName = source.DisplayName + " 複製";
            AssetDatabase.CreateAsset(copy, folder + "/Stats.asset");
            copy.actionProfile = DuplicateProfile(source.actionProfile, folder, copy.kind, false);
            copy.prefab = CreateVisualPrefab(copy, null, source.prefab); EditorUtility.SetDirty(copy); RegisterUnit(copy); selectedUnit = copy;
        }
        else if (tab == 1 && selectedBuilding != null)
        {
            var source = selectedBuilding; string folder = NewFolder("Buildings", source.displayName + " 複製");
            var copy = Instantiate(source); copy.definitionId = "building-" + Guid.NewGuid().ToString("N"); copy.displayName = source.displayName + " 複製";
            AssetDatabase.CreateAsset(copy, folder + "/Building.asset");
            copy.actionProfile = DuplicateProfile(source.actionProfile, folder, BuildingRole(copy.behaviourKind), true);
            copy.prefab = CreateVisualPrefab(null, copy, source.prefab); EditorUtility.SetDirty(copy); RegisterBuilding(copy); selectedBuilding = copy;
        }
        else throw new ArgumentException("複製する駒または建物を選択してください。");
        visualModel = null;
    }
    static BoardActionProfile DuplicateProfile(BoardActionProfile source, string folder, Kind role, bool building)
    {
        if (source == null) return CreateProfile(folder + "/Stats.asset", role, building);
        var copy = Instantiate(source); AssetDatabase.CreateAsset(copy, folder + "/ActionTiles.asset"); return copy;
    }
    public static GameObject CreateVisualPrefab(UnitData unit, FacilityDefinitionData building, GameObject model)
    {
        if ((unit == null) == (building == null)) throw new ArgumentException("駒か建物を１つ指定してください。");
        var owner = unit != null ? (UnityEngine.Object)unit : building;
        string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(owner)).Replace('\\', '/');
        if (string.IsNullOrEmpty(folder)) throw new ArgumentException("データを先に保存してください。");
        GameObject root = null;
        try
        {
            root = model != null ? Instantiate(model) : GameObject.CreatePrimitive(unit != null ? PrimitiveType.Capsule : PrimitiveType.Cube);
            root.SetActive(false); root.name = unit != null ? unit.DisplayName : building.displayName;
            if (PrefabUtility.IsPartOfPrefabInstance(root)) PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var statuses = root.GetComponentsInChildren<Status>(true);
            if (statuses.Length > 1) throw new ArgumentException("元モデルのStatusが複数あります。１体分のモデルを選択してください。");
            var actor = statuses.Length == 1 ? statuses[0] : root.AddComponent<Status>();
            if(actor.gameObject!=root)
            {
                // Economy and selection traverse actor roots. Normalize only the generated copy.
                DestroyImmediate(actor);actor=root.AddComponent<Status>();
            }
            actor.direction = Direction.N;
            if (unit != null)
            { actor.kind = unit.kind; actor.type = Type.Unit; actor.team = unit.defaultTeam; actor.AuthoredFacility = null; unit.ApplyToStatus(actor,1); unit.InitializeAbilities(actor); }
            else
            {
                var level = building.GetLevel(1); actor.team = building.defaultTeam; actor.kind = BuildingRole(building.behaviourKind);
                actor.facilityKind = building.behaviourKind; actor.AuthoredFacility = building; actor.GrowthData = null;
                actor.type = building.behaviourKind == FacilityKind.WoodWall || building.behaviourKind == FacilityKind.StoneWall ? Type.Wall : Type.Building;
                actor.Level = 1; actor.HP = actor.MaxHP = level.HP; actor.ATK = level.ATK; actor.DEF = level.DEF;
            }
            if (root.GetComponentInChildren<Collider>(true) == null) actor.gameObject.AddComponent<BoxCollider>();
            root.SetActive(true);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, AssetDatabase.GenerateUniqueAssetPath(folder + "/Visual.prefab"));
            if (prefab == null) throw new InvalidOperationException("Prefabの保存に失敗しました。");
            return prefab;
        }
        finally { if (root != null) DestroyImmediate(root); }
    }
    static Kind BuildingRole(FacilityKind kind) => kind == FacilityKind.SubCrystal ? Kind.SubCrystal
        : kind == FacilityKind.WoodWall ? Kind.WoodWall : kind == FacilityKind.StoneWall ? Kind.StoneWall : Kind.None;
}
