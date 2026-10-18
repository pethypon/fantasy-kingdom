#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Explicit asset bake. No initialization hook, runtime mesh creation, collider, Status, or visibility override.</summary>
public static partial class TerrainEnvironmentProductionInstaller
{
    public const string Folder = "Assets/_FantasyKingdom/EnvironmentVisuals";
    public const string SettingsPath = Folder + "/EnvironmentProductionSettings.asset";
    public const string CatalogPath = "Assets/Resources/Terrain/EnvironmentCatalog.asset";
    static readonly Color32[] Palette = {
        new Color32(40, 54, 39, 255), new Color32(37, 65, 43, 255), new Color32(53, 85, 51, 255),
        new Color32(80, 110, 61, 255), new Color32(56, 39, 28, 255), new Color32(93, 65, 41, 255),
        new Color32(65, 73, 75, 255), new Color32(99, 109, 109, 255), new Color32(141, 147, 133, 255),
        new Color32(37, 47, 34, 255), new Color32(70, 91, 41, 255), new Color32(128, 151, 61, 255),
        new Color32(66, 46, 33, 255), new Color32(91, 67, 44, 255), new Color32(137, 122, 95, 255),
        new Color32(28, 91, 99, 255), new Color32(50, 139, 140, 255), new Color32(120, 188, 168, 255),
        new Color32(24, 29, 30, 255), new Color32(65, 74, 78, 255), new Color32(92, 108, 111, 255),
        new Color32(130, 144, 140, 255), new Color32(170, 142, 77, 255), new Color32(194, 184, 147, 255)
    };
    public static string PrefabPath(TerrainEnvironmentKind kind) => Folder + "/Prefabs/" + kind + ".prefab";
    public static string MeshPath(TerrainEnvironmentKind kind) => Folder + "/Meshes/" + kind + ".asset";

    [MenuItem("Fantasy Kingdom/開発スタジオ/環境/開始時の環境と見た目を導入（未設定値のみ）")]
    public static void Install() => InstallAssets();

    [MenuItem("Fantasy Kingdom/開発スタジオ/環境/暫定の導入値を開く")]
    public static void OpenSettings()
    {
        EnsureFolder(Folder);
        Selection.activeObject = GetSettings();
    }

    public static TerrainEnvironmentCatalog InstallAssets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("ゲームを停止してから環境を導入してください。");
        EnsureFolder(Folder + "/Meshes"); EnsureFolder(Folder + "/Prefabs");
        var settings = GetSettings(); settings.Validate();
        var catalog = AssetDatabase.LoadAssetAtPath<TerrainEnvironmentCatalog>(CatalogPath);
        if (catalog == null) { TerrainEnvironmentEditor.InstallDefaults(); catalog = AssetDatabase.LoadAssetAtPath<TerrainEnvironmentCatalog>(CatalogPath); }
        if (catalog == null) throw new InvalidOperationException("環境カタログを作成できませんでした。");
        // Validate copies before mutating the playable data; an invalid authored value remains the author's decision.
        foreach (var definition in catalog.definitions)
        {
            if (definition == null || settings.installed.Contains(definition)) continue;
            var candidate = UnityEngine.Object.Instantiate(definition);
            try
            {
                FillUnconfiguredValues(candidate, settings);
                if (candidate.ConfigurationError != null) throw new InvalidOperationException(definition.name + ": " + candidate.ConfigurationError);
            }
            finally { UnityEngine.Object.DestroyImmediate(candidate); }
        }
        var material = BakePalette();
        var prefabs = new Dictionary<TerrainEnvironmentKind, GameObject>();
        foreach (TerrainEnvironmentKind kind in Enum.GetValues(typeof(TerrainEnvironmentKind)))
        {
            var mesh = MakeVisual(kind).Build(kind + "_BakedEnvironment");
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(kind));
            if (existing == null) AssetDatabase.CreateAsset(mesh, MeshPath(kind));
            else { EditorUtility.CopySerialized(mesh, existing); UnityEngine.Object.DestroyImmediate(mesh); mesh = existing; EditorUtility.SetDirty(mesh); }
            prefabs[kind] = BakePrefab(kind, mesh, material);
        }
        int changed = 0;
        foreach (var definition in catalog.definitions)
        {
            if (definition == null) continue;
            if (!settings.installed.Contains(definition))
            {
                FillUnconfiguredValues(definition, settings); definition.enabledForPlay = true;
                settings.installed.Add(definition); EditorUtility.SetDirty(definition); changed++;
            }
            if (definition.visualPrefab == null) { definition.visualPrefab = prefabs[definition.kind]; EditorUtility.SetDirty(definition); }
        }
        if (!settings.generationInstalled)
        {
            if (catalog.generation == null || !catalog.generation.enabled)
                catalog.generation = TerrainEnvironmentGenerationSettings.CreatePlayableDefaults();
            catalog.generation.enabled = true;
            catalog.enabledForPlay = true; settings.generationInstalled = true; EditorUtility.SetDirty(catalog);
        }
        EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
        ValidateAssets();
        Debug.Log("[EnvironmentProduction] 導入=" + changed + "種類、見た目=10種類、共有マテリアル=1、環境Collider=0。調整状態: " + settings.balanceStatus);
        return catalog;
    }

    public static void FillUnconfiguredValues(TerrainEnvironmentDefinition target, TerrainEnvironmentProductionSettings settings)
    {
        // Only the untouched, disabled standard draft is migrated. Later authoring remains authoritative.
        if (settings.makeUninstalledStandardRockPassable && target.kind == TerrainEnvironmentKind.Rock
            && target.definitionId == "environment.rock" && !target.enabledForPlay && target.visualPrefab == null
            && target.blocksMovement && target.blocksShots && target.blocksVision && !target.concealsOccupants
            && target.revealDistance == -1 && target.moveAPAdjustment == 0 && !target.damageOnEntry && !target.damageOnOwnerTurn
            && target.flatDamage == -1 && target.maximumHpDamageFraction == -1 && target.maximumHpHealFraction == -1)
        {
            target.blocksMovement = false; target.blocksShots = false;
        }
        if (target.concealsOccupants && target.revealDistance < 0) target.revealDistance = settings.concealRevealDistance;
        if (target.kind == TerrainEnvironmentKind.Forest && target.attackRevealRounds < 0) target.attackRevealRounds = settings.forestAttackRevealRounds;
        if (target.kind == TerrainEnvironmentKind.Cave && target.caveCooldownTurns < 0) target.caveCooldownTurns = settings.caveCooldownTurns;
        if (target.kind == TerrainEnvironmentKind.Mud && target.moveAPAdjustment == 0) target.moveAPAdjustment = settings.mudMoveAP;
        if (target.kind == TerrainEnvironmentKind.Road && target.moveAPAdjustment == 0) target.moveAPAdjustment = -settings.roadMoveAPDiscount;
        if (target.kind == TerrainEnvironmentKind.PoisonSwamp && target.flatDamage < 0 && target.maximumHpDamageFraction < 0)
            target.maximumHpDamageFraction = settings.poisonMaximumHpDamageFraction;
        if (target.kind == TerrainEnvironmentKind.Geyser && target.maximumHpDamageFraction < 0)
            target.maximumHpDamageFraction = TerrainEnvironmentDefinition.GeyserDamageFraction;
        if (target.kind == TerrainEnvironmentKind.HealingSpring && target.maximumHpHealFraction < 0)
            target.maximumHpHealFraction = settings.springMaximumHpHealFraction;
        if (target.burnable || target.kind == TerrainEnvironmentKind.Smoke)
        {
            if (target.burnSmokeRounds < 0) target.burnSmokeRounds = settings.smokeRounds;
            if (target.burnSmokeDamageFraction < 0) target.burnSmokeDamageFraction = settings.smokeMaximumHpDamageFraction;
            if (target.burnSmokeRevealDistance < 0) target.burnSmokeRevealDistance = settings.smokeRevealDistance;
        }
        if (target.kind == TerrainEnvironmentKind.Smoke && target.maximumHpDamageFraction < 0)
            target.maximumHpDamageFraction = settings.smokeMaximumHpDamageFraction;
    }

    static TerrainEnvironmentProductionSettings GetSettings()
    {
        var settings = AssetDatabase.LoadAssetAtPath<TerrainEnvironmentProductionSettings>(SettingsPath);
        if (settings == null) { settings = ScriptableObject.CreateInstance<TerrainEnvironmentProductionSettings>(); AssetDatabase.CreateAsset(settings, SettingsPath); }
        if (settings.installed == null) settings.installed = new List<TerrainEnvironmentDefinition>();
        return settings;
    }
    static Material BakePalette()
    {
        string texturePath = Folder + "/EnvironmentPalette.png";
        var texture = new Texture2D(Palette.Length, 2, TextureFormat.RGB24, false);
        for (int y = 0; y < 2; y++) for (int x = 0; x < Palette.Length; x++) texture.SetPixel(x, y, Palette[x]);
        File.WriteAllBytes(texturePath, texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
        importer.filterMode = FilterMode.Point; importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = false; importer.npotScale = TextureImporterNPOTScale.None;
        importer.textureCompression = TextureImporterCompression.Uncompressed; importer.isReadable = false;
        importer.SaveAndReimport();
        string path = Folder + "/EnvironmentPalette.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            var shader = Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("Standard shaderがありません。");
            material = new Material(shader); AssetDatabase.CreateAsset(material, path);
        }
        material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        material.mainTextureScale = Vector2.one; material.mainTextureOffset = Vector2.zero; material.color = Color.white;
        material.SetFloat("_Metallic", 0); material.SetFloat("_Glossiness", .12f);
        material.enableInstancing = true; EditorUtility.SetDirty(material); return material;
    }
    static GameObject BakePrefab(TerrainEnvironmentKind kind, Mesh mesh, Material material)
    {
        var root = new GameObject("Environment_" + kind);
        try
        {
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(kind));
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    [MenuItem("Fantasy Kingdom/開発スタジオ/環境/見た目と導入状態を検証")]
    public static void ValidateAssets()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/EnvironmentPalette.mat");
        if (material == null || !material.enableInstancing) throw new InvalidOperationException("環境共有マテリアルがありません。");
        int triangles = 0;
        foreach (TerrainEnvironmentKind kind in Enum.GetValues(typeof(TerrainEnvironmentKind)))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(kind));
            if (prefab == null) throw new InvalidOperationException("環境Prefabがありません: " + kind);
            var renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
            var filter = prefab.GetComponent<MeshFilter>(); var mesh = filter == null ? null : filter.sharedMesh;
            if (renderers.Length != 1 || mesh == null || mesh.subMeshCount != 1 || renderers[0].sharedMaterial != material
                || mesh.triangles.Length / 3 > 900 || prefab.GetComponentsInChildren<Collider>(true).Length != 0
                || prefab.GetComponentsInChildren<Status>(true).Length != 0 || prefab.GetComponentsInChildren<MonoBehaviour>(true).Length != 0
                || renderers[0].shadowCastingMode != ShadowCastingMode.Off || prefab.transform.localScale != Vector3.one)
                throw new InvalidOperationException("環境の描画・非干渉制限を超えています: " + kind);
            if (mesh.bounds.min.x < -.5f || mesh.bounds.max.x > .5f || mesh.bounds.min.z < -.5f || mesh.bounds.max.z > .5f
                || mesh.bounds.min.y < -.501f || mesh.bounds.max.y > .55f)
                throw new InvalidOperationException("環境が1マスまたは高さ制限を超えています: " + kind);
            triangles += mesh.triangles.Length / 3;
        }
        Debug.Log("[EnvironmentProduction] 見た目検証OK: 10 prefab / 1 material / Collider 0 / Status 0 / triangles " + triangles);
    }

    // Optional caller-owned review artifact. Does not open or mutate the user's scene.
    public static void RenderPreview()
    {
        const int tileWidth = 320, tileHeight = 300;
        EnsureFolder(Folder + "/Review");
        var scene = EditorSceneManager.NewPreviewScene();
        var atlas = new Texture2D(tileWidth * 5, tileHeight * 2, TextureFormat.RGB24, false);
        var target = new RenderTexture(tileWidth, tileHeight, 24, RenderTextureFormat.ARGB32);
        var readback = new Texture2D(tileWidth, tileHeight, TextureFormat.RGB24, false);
        var oldTarget = RenderTexture.active;
        var oldAmbient = RenderSettings.ambientLight; var oldAmbientMode = RenderSettings.ambientMode;
        try
        {
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.6f, .64f, .65f);
            var cameraObject = new GameObject("Environment review camera"); SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>(); camera.scene = scene;
            camera.orthographic = true; camera.orthographicSize = .76f; camera.nearClipPlane = .01f; camera.farClipPlane = 20;
            camera.transform.position = new Vector3(1.5f, 1.7f, -2.3f); camera.transform.LookAt(new Vector3(0, -.12f, 0));
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(27, 34, 36, 255);
            camera.targetTexture = target; camera.allowHDR = false;
            var lightObject = new GameObject("Environment review light"); SceneManager.MoveGameObjectToScene(lightObject, scene);
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.1f;
            light.color = new Color(1, .93f, .82f); light.transform.rotation = Quaternion.Euler(40, -35, 0); light.shadows = LightShadows.None;
            for (int i = 0; i < 10; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath((TerrainEnvironmentKind)i));
                if (prefab == null) throw new InvalidOperationException("先に環境を導入してください。");
                var instance = UnityEngine.Object.Instantiate(prefab); SceneManager.MoveGameObjectToScene(instance, scene);
                try
                {
                    camera.Render(); RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0, 0, tileWidth, tileHeight), 0, 0); readback.Apply();
                    atlas.SetPixels((i % 5) * tileWidth, (1 - i / 5) * tileHeight, tileWidth, tileHeight, readback.GetPixels());
                }
                finally { UnityEngine.Object.DestroyImmediate(instance); }
            }
            atlas.Apply(); File.WriteAllBytes(Folder + "/Review/EnvironmentVisuals.png", atlas.EncodeToPNG());
            AssetDatabase.ImportAsset(Folder + "/Review/EnvironmentVisuals.png");
        }
        finally
        {
            RenderTexture.active = oldTarget; RenderSettings.ambientLight = oldAmbient; RenderSettings.ambientMode = oldAmbientMode;
            CleanupPreview(target, readback, atlas); EditorSceneManager.ClosePreviewScene(scene);
        }
    }
    static void CleanupPreview(RenderTexture target, Texture2D readback, Texture2D atlas)
    { target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(readback); UnityEngine.Object.DestroyImmediate(atlas); }
}
#endif
