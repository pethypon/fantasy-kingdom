#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Explicit presentation-only bake. Reuses the three existing faction materials without editing them.</summary>
public static partial class BoardVisualThemeInstaller
{
    public const string Folder = "Assets/_FantasyKingdom/BoardVisualTheme";
    public const string CatalogPath = "Assets/Resources/GameContent/BoardVisualTheme.asset";
    public const string ExistingMaterialFolder = "Assets/Prefabs/Units/Materials/Materials/キャラクターマテリアル";
    public const string ExistingWhiteMaterialPath = ExistingMaterialFolder + "/プレイヤー　キャラクター　白.mat";
    public const string ExistingBlackMaterialPath = ExistingMaterialFolder + "/異形　キャラクター　黒.mat";
    public const string ExistingRedMaterialPath = ExistingMaterialFolder + "/敵　キャラクター　赤.mat";
    public static string PlayerUnitPrefabPath(Kind kind) => Folder + "/Prefabs/PlayerUnit_" + kind + ".prefab";
    public static string PlayerBuildingPrefabPath(FacilityKind kind) => Folder + "/Prefabs/PlayerBuilding_" + kind + ".prefab";

    [MenuItem("Fantasy Kingdom/開発スタジオ/見た目/既存の白・黒・赤マテリアルで陣営別モデルを導入")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("ゲームを停止してから陣営別の見た目を導入してください。");
        Preflight(); EnsureFolder(Folder + "/Prefabs"); EnsureFolder(Folder + "/Review"); EnsureFolder("Assets/Resources/GameContent");
        LoadFactionMaterials(out var white, out var black, out var red);
        var catalog = AssetDatabase.LoadAssetAtPath<BoardVisualThemeCatalog>(CatalogPath);
        if (catalog == null) { catalog = ScriptableObject.CreateInstance<BoardVisualThemeCatalog>(); catalog.name = "陣営別モデルと既存マテリアル"; AssetDatabase.CreateAsset(catalog, CatalogPath); }
        ApplyMaterials(catalog, white, black, red);
        foreach (var kind in DarkEnemyVisualInstaller.EnemyKinds)
        {
            GameObject player = kind == Kind.King ? null : MakePureVisual(PlayerUnitPrefabPath(kind), RequirePrefab(R1UnitVisualInstaller.PrefabPath(kind)), white);
            if (!catalog.units.Exists(value => value != null && value.kind == kind))
                catalog.units.Add(new BoardVisualThemeCatalog.UnitStyle { kind = kind, playerVisual = player,
                    enemyVisual = RequirePrefab(DarkEnemyVisualInstaller.EnemyPrefabPath(kind)) });
        }
        foreach (FacilityKind kind in Enum.GetValues(typeof(FacilityKind)))
        {
            var player = MakePureVisual(PlayerBuildingPrefabPath(kind), RequirePrefab(BuildingVisualGenerator.PrefabPath(kind)), white);
            if (!catalog.buildings.Exists(value => value != null && value.kind == kind))
                catalog.buildings.Add(new BoardVisualThemeCatalog.BuildingStyle { kind = kind, playerVisual = player,
                    enemyVisual = RequirePrefab(BuildingVisualGenerator.DarkPrefabPath(kind)) });
        }
        foreach (var boss in DarkEnemyVisualInstaller.Bosses)
            if (!catalog.bosses.Exists(value => value != null && value.kind == boss))
                catalog.bosses.Add(new BoardVisualThemeCatalog.BossStyle { kind = boss, visual = RequirePrefab(DarkEnemyVisualInstaller.BossPrefabPath(boss)) });
        CollectStandardSources(catalog); AssignGeneratedPrefabMaterials(white, black, red);
        EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets(); ValidateAssets();
        Debug.Log("[BoardVisualTheme] 導入完了。既存の白・黒・赤3マテリアルを共有参照。元Material変更0、新Material/Shader/Texture生成0。");
    }
    public static void LoadFactionMaterials(out Material white, out Material black, out Material red)
    {
        white = AssetDatabase.LoadAssetAtPath<Material>(ExistingWhiteMaterialPath);
        black = AssetDatabase.LoadAssetAtPath<Material>(ExistingBlackMaterialPath);
        red = AssetDatabase.LoadAssetAtPath<Material>(ExistingRedMaterialPath);
        if (white == null || black == null || red == null) throw new InvalidOperationException("既存の白・黒・赤のキャラクターマテリアルが見つかりません。");
    }
    static void ApplyMaterials(BoardVisualThemeCatalog catalog, Material white, Material black, Material red)
    {
        catalog.playerUnitMaterial = catalog.playerBuildingMaterial = catalog.playerPlainMaterial = white;
        catalog.enemyUnitMaterial = catalog.enemyBuildingMaterial = catalog.enemyPlainMaterial = black;
        catalog.bossMaterial = catalog.bossPlainMaterial = red;
    }
    /// <summary>Explicit migration from the unpublished palette prototype to the user's existing materials.</summary>
    [MenuItem("Fantasy Kingdom/開発スタジオ/見た目/カタログを既存の白・黒・赤マテリアルへ統一")]
    public static void UseExistingFactionMaterials()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("ゲームを停止してから材質を切り替えてください。");
        var catalog = AssetDatabase.LoadAssetAtPath<BoardVisualThemeCatalog>(CatalogPath);
        if (catalog == null) throw new InvalidOperationException("先に陣営別モデルを導入してください。");
        LoadFactionMaterials(out var white, out var black, out var red); ApplyMaterials(catalog, white, black, red);
        AssignGeneratedPrefabMaterials(white, black, red); EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets(); ValidateAssets();
    }
    static void AssignGeneratedPrefabMaterials(Material white, Material black, Material red)
    {
        foreach (var kind in DarkEnemyVisualInstaller.EnemyKinds)
        {
            if (kind != Kind.King) AssignPureMaterial(PlayerUnitPrefabPath(kind), white);
            AssignPureMaterial(DarkEnemyVisualInstaller.EnemyPrefabPath(kind), black);
        }
        foreach (FacilityKind kind in Enum.GetValues(typeof(FacilityKind))) AssignPureMaterial(PlayerBuildingPrefabPath(kind), white);
        foreach (var boss in DarkEnemyVisualInstaller.Bosses) AssignPureMaterial(DarkEnemyVisualInstaller.BossPrefabPath(boss), red);
        // Stock building prefabs keep their original authored material references. Runtime applies the catalog's shared black.
    }
    static void AssignPureMaterial(string path, Material material)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (prefab == null) return;
        var renderer = prefab.GetComponent<MeshRenderer>();
        if (renderer == null || renderer.sharedMaterial == material) return;
        if (prefab.GetComponentsInChildren<MonoBehaviour>(true).Length != 0 || prefab.GetComponentsInChildren<Collider>(true).Length != 0)
            throw new InvalidOperationException("表示専用Prefab以外の材質は変更しません: " + path);
        var root = PrefabUtility.LoadPrefabContents(path);
        try { root.GetComponent<MeshRenderer>().sharedMaterial = material; PrefabUtility.SaveAsPrefabAsset(root, path); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    /// <summary>Remove only the unpublished prototype assets inside the isolated validation clone.</summary>
    public static void DiscardUnpublishedMaterialsForValidation()
    {
        if (!string.Equals(new DirectoryInfo(Application.dataPath).Parent.Name, "UnityValidation", StringComparison.Ordinal))
            throw new InvalidOperationException("未公開の試作品の除去はUnityValidationプロジェクトでだけ実行できます。");
        UseExistingFactionMaterials();
        var dependencies = new HashSet<string>(AssetDatabase.GetDependencies(CatalogPath, true), StringComparer.Ordinal);
        foreach (string role in new[] { "PlayerUnits", "EnemyUnits", "WildBoss", "PlayerBuildings", "EnemyBuildings", "PlayerPlain", "EnemyPlain", "BossPlain" })
        {
            DeleteUnreferenced(Folder + "/Materials/" + role + ".mat", dependencies);
            DeleteUnreferenced(Folder + "/Textures/" + role + "Palette.png", dependencies);
        }
        DeleteUnreferenced(Folder + "/Textures/MarbleVeins.png", dependencies);
        DeleteUnreferenced(Folder + "/Shaders/BoardMarble.shader", dependencies);
        AssetDatabase.SaveAssets(); Debug.Log("[BoardVisualTheme] 検証用の未公開8材質・独自Shader・模様Textureを撤去。既存3材質は変更していません。");
    }
    static void DeleteUnreferenced(string path, HashSet<string> dependencies)
    {
        if (dependencies.Contains(path)) throw new InvalidOperationException("まだ参照されている試作品は除去しません: " + path);
        if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
    }
    static void Preflight()
    {
        foreach (var kind in DarkEnemyVisualInstaller.EnemyKinds)
        {
            RequirePrefab(DarkEnemyVisualInstaller.EnemyPrefabPath(kind));
            if (kind != Kind.King) RequirePrefab(R1UnitVisualInstaller.PrefabPath(kind));
        }
        foreach (FacilityKind kind in Enum.GetValues(typeof(FacilityKind)))
        { RequirePrefab(BuildingVisualGenerator.PrefabPath(kind)); RequirePrefab(BuildingVisualGenerator.DarkPrefabPath(kind)); }
        foreach (var boss in DarkEnemyVisualInstaller.Bosses) RequirePrefab(DarkEnemyVisualInstaller.BossPrefabPath(boss));
        LoadFactionMaterials(out _, out _, out _);
    }
    static GameObject RequirePrefab(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null || prefab.GetComponent<MeshFilter>() == null || prefab.GetComponent<MeshFilter>().sharedMesh == null)
            throw new InvalidOperationException("先に標準の駒・建物・敵モデルを作成してください: " + path);
        return prefab;
    }
    static GameObject MakePureVisual(string path, GameObject source, Material material)
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (existing != null) return existing;
        var root = new GameObject(Path.GetFileNameWithoutExtension(path));
        try
        {
            root.AddComponent<MeshFilter>().sharedMesh = source.GetComponent<MeshFilter>().sharedMesh;
            var renderer = root.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    static void CollectStandardSources(BoardVisualThemeCatalog catalog)
    {
        var meshes = new HashSet<Mesh>(catalog.managedSourceMeshes);
        var ids = new HashSet<string>(catalog.managedDefinitionIds, StringComparer.Ordinal);
        foreach (var kind in R1UnitVisualInstaller.Kinds)
        {
            AddPrefabMeshes(RequirePrefab(R1UnitVisualInstaller.PrefabPath(kind)), meshes);
            var data = AssetDatabase.LoadAssetAtPath<UnitData>(R1UnitVisualInstaller.DataPath(kind));
            AddId(data == null ? null : data.definitionId, ids);
        }
        foreach (FacilityKind kind in Enum.GetValues(typeof(FacilityKind)))
        {
            AddPrefabMeshes(RequirePrefab(BuildingVisualGenerator.PrefabPath(kind)), meshes);
            var data = AssetDatabase.LoadAssetAtPath<FacilityDefinitionData>(BuildingVisualGenerator.LegacyFolder + "/" + kind + "/Building.asset");
            AddId(data == null ? null : data.definitionId, ids);
        }
        var systems = AssetDatabase.LoadAssetAtPath<GameObject>(BuildingVisualGenerator.SystemPrefab);
        if (systems == null) throw new InvalidOperationException("ゲーム設定Prefabがありません。");
        foreach (var setting in systems.GetComponentsInChildren<UnitSetting>(true))
        {
            var serialized = new SerializedObject(setting);
            foreach (string name in new[] { "KingPiece", "StrangePiece", "KnightPiece", "ArcherPiece", "ScoutPiece" })
                AddPropertyMeshes(serialized.FindProperty(name), meshes);
            var initial = serialized.FindProperty("initialEnemyCommanderData")?.objectReferenceValue as UnitData;
            AddId(initial == null ? null : initial.definitionId, ids);
            var dataList = serialized.FindProperty("_unitDataList");
            if (dataList != null) for (int i = 0; i < dataList.arraySize; i++)
            {
                var data = dataList.GetArrayElementAtIndex(i).FindPropertyRelative("data").objectReferenceValue as UnitData;
                AddId(data == null ? null : data.definitionId, ids);
            }
        }
        foreach (var crystals in systems.GetComponentsInChildren<CrystalSystem>(true))
        {
            var serialized = new SerializedObject(crystals);
            AddPropertyMeshes(serialized.FindProperty("PlayerCrystal"), meshes); AddPropertyMeshes(serialized.FindProperty("EnemyCrystal"), meshes);
        }
        // Summon/Build tables are the actual initial stock references, even when prefab names differ.
        foreach (var summon in systems.GetComponentsInChildren<SummonSystem>(true))
            AddTableMeshes(new SerializedObject(summon).FindProperty("unitPrefabs"), meshes);
        foreach (var build in systems.GetComponentsInChildren<BuildSystem>(true))
            AddTableMeshes(new SerializedObject(build).FindProperty("facilityPrefabs"), meshes);
        catalog.managedSourceMeshes = new List<Mesh>(meshes);
        catalog.managedSourceMeshes.Sort((a, b) => string.CompareOrdinal(AssetDatabase.GetAssetPath(a) + ":" + a.name, AssetDatabase.GetAssetPath(b) + ":" + b.name));
        catalog.managedDefinitionIds = new List<string>(ids); catalog.managedDefinitionIds.Sort(StringComparer.Ordinal);
    }
    static void AddId(string id, HashSet<string> ids) { if (!string.IsNullOrWhiteSpace(id)) ids.Add(id); }
    static void AddPropertyMeshes(SerializedProperty property, HashSet<Mesh> meshes)
    { if (property != null) AddPrefabMeshes(property.objectReferenceValue as GameObject, meshes); }
    static void AddTableMeshes(SerializedProperty list, HashSet<Mesh> meshes)
    {
        if (list == null || !list.isArray) return;
        for (int i = 0; i < list.arraySize; i++) AddPropertyMeshes(list.GetArrayElementAtIndex(i).FindPropertyRelative("prefab"), meshes);
    }
    static void AddPrefabMeshes(GameObject prefab, HashSet<Mesh> meshes)
    {
        if (prefab == null) return;
        foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true)) if (filter.sharedMesh != null) meshes.Add(filter.sharedMesh);
        foreach (var skin in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (skin.sharedMesh != null) meshes.Add(skin.sharedMesh);
    }
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
    public static void InstallAndReview() { Install(); RenderPreview(); }
}
#endif
