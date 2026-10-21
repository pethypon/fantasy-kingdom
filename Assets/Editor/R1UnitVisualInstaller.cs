#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Explicit editor bake; the playable prefabs gain no scripts, colliders, or material instances.</summary>
public static partial class R1UnitVisualInstaller
{
    public const string Folder = "Assets/_FantasyKingdom/UnitVisuals";
    public const string MaterialPath = Folder + "/ChessMetal.mat";
    public static readonly Kind[] Kinds = {
        Kind.Knight, Kind.Archer, Kind.Magic, Kind.Assassin, Kind.Scout, Kind.Priest, Kind.Guardian, Kind.Crossbow,
        Kind.Magicsniper, Kind.Bomber, Kind.WhiteMage, Kind.HolyKnight, Kind.Samurai, Kind.Ninja, Kind.Berserker, Kind.Navigator,
        Kind.HeavyWarrior, Kind.DragonKnight
    };
    static readonly Color32[] Palette = {
        new Color32(37, 46, 57, 255), new Color32(60, 73, 87, 255), new Color32(102, 119, 133, 255),
        new Color32(176, 192, 198, 255), new Color32(208, 177, 110, 255), new Color32(134, 96, 47, 255),
        new Color32(232, 225, 195, 255), new Color32(40, 139, 146, 255), new Color32(121, 224, 215, 255),
        new Color32(122, 40, 43, 255), new Color32(185, 78, 54, 255), new Color32(25, 30, 38, 255)
    };
    public static string MeshPath(Kind kind) => Folder + "/Meshes/" + kind + ".asset";
    public static bool IsNewRole(Kind kind) => kind == Kind.WhiteMage || kind == Kind.HolyKnight || kind == Kind.Samurai
        || kind == Kind.Ninja || kind == Kind.Berserker || kind == Kind.Navigator
        || kind == Kind.HeavyWarrior || kind == Kind.DragonKnight;
    public static string PrefabPath(Kind kind) => IsNewRole(kind) ? R1ProductionContentInstaller.Folder + "/Units/" + kind + "/Visual.prefab"
        : "Assets/Prefabs/Units/Player/" + (kind == Kind.Magicsniper ? "MagicSniper" : kind.ToString()) + ".prefab";
    public static string DataPath(Kind kind)
    {
        if (IsNewRole(kind)) return R1ProductionContentInstaller.Folder + "/Units/" + kind + "/Stats.asset";
        var settings = AssetDatabase.LoadAssetAtPath<R1ProductionContentSettings>(R1ProductionContentInstaller.SettingsPath);
        var entry = settings == null || settings.entries == null ? null : settings.entries.Find(value => value != null && value.kind == kind);
        return entry == null || entry.sourceData == null ? null : AssetDatabase.GetAssetPath(entry.sourceData);
    }

    /// <summary>Creates only a missing stock mesh; existing generated and author-edited assets remain intact.</summary>
    public static Mesh LoadOrCreateRoleMesh(Kind kind)
    {
        if (Array.IndexOf(Kinds, kind) < 0) throw new ArgumentException("標準モデルのない兵種です: " + kind);
        EnsureFolder(Folder + "/Meshes");
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(kind));
        if (mesh == null) { mesh = MakeVisual(kind).Build("R1_" + kind + "_Chess"); AssetDatabase.CreateAsset(mesh, MeshPath(kind)); }
        return mesh;
    }

    [MenuItem("Fantasy Kingdom/開発スタジオ/駒の見た目/18職種のチェス駒を導入（未加工モデルのみ）")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("ゲームを停止してから駒の見た目を導入してください。");
        // Preflight every target before baking or replacing anything.
        foreach (var kind in Kinds)
        {
            var data = AssetDatabase.LoadAssetAtPath<UnitData>(DataPath(kind));
            if (data == null || data.kind != kind || (IsNewRole(kind) && data.definitionId != "r1.unit." + kind.ToString().ToLowerInvariant()))
                throw new InvalidOperationException("先にR1本番データを導入してください: " + kind);
        }
        EnsureFolder(Folder + "/Meshes"); EnsureFolder(Folder + "/Review");
        var material = LoadOrCreateMaterial();
        int replaced = 0, preserved = 0;
        foreach (var kind in Kinds)
        {
            var mesh = LoadOrCreateRoleMesh(kind);
            var data = AssetDatabase.LoadAssetAtPath<UnitData>(DataPath(kind));
            var prefab = IsNewRole(kind) ? data.prefab : AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(kind));
            // A catalog replacement, edited/generated mesh, hierarchy, or extra component is author-owned.
            // On repeat runs even our own baked model is preserved rather than regenerated.
            string preservation = prefab == null ? "Prefab未設定" : AssetDatabase.GetAssetPath(prefab) != PrefabPath(kind)
                ? "別のPrefabに差し替え済み" : PrimitivePreservationReason(prefab, kind);
            if (preservation != null) { Debug.Log("[R1UnitVisuals] 保護: " + kind + " / " + preservation); preserved++; continue; }
            var root = PrefabUtility.LoadPrefabContents(PrefabPath(kind));
            try
            {
                root.GetComponent<MeshFilter>().sharedMesh = mesh;
                root.GetComponent<MeshRenderer>().sharedMaterial = material;
                if (PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(kind)) == null)
                    throw new InvalidOperationException("駒の見た目を保存できませんでした: " + kind);
                replaced++;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
        ValidateAssets();
        Debug.Log("[R1UnitVisuals] " + Kinds.Length + "職種の見た目導入=" + replaced + "、既存設定保護=" + preserved + "。各1 Mesh/1 Renderer、追加Runtimeスクリプト0。");
    }

    /// <summary>Recognize only the untouched R1 sphere copy. Never infer ownership from the asset path alone.</summary>
    public static bool CanReplacePrimitivePrefab(GameObject prefab, Kind kind) => PrimitivePreservationReason(prefab, kind) == null;

    static string PrimitivePreservationReason(GameObject prefab, Kind kind)
    {
        if (prefab == null || prefab.transform.childCount != 0 || (IsNewRole(kind) && prefab.transform.localPosition != Vector3.zero)
            || prefab.transform.localRotation != Quaternion.identity || prefab.transform.localScale != Vector3.one) return "独自の階層またはTransform";
        var actor = prefab.GetComponent<Status>(); var filter = prefab.GetComponent<MeshFilter>(); var renderer = prefab.GetComponent<MeshRenderer>();
        var sphere = prefab.GetComponent<SphereCollider>(); var components = prefab.GetComponents<Component>();
        if (actor == null || actor.kind != kind || actor.type != Type.Unit || filter == null || renderer == null || sphere == null || components.Length != 5
            || sphere.center != Vector3.zero || !Mathf.Approximately(sphere.radius, .5f) || sphere.isTrigger || !sphere.enabled) return "独自のComponent、Statusまたは選択Collider";
        var originalMaterial = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath("d34b0ff78bdc5d846b5e910a255c76ce"));
        if (originalMaterial == null || renderer.sharedMaterials.Length != 1 || renderer.sharedMaterial != originalMaterial
            || !renderer.enabled || renderer.shadowCastingMode != ShadowCastingMode.On || !renderer.receiveShadows)
            return "独自のMaterialまたはRenderer設定";
        if (filter.sharedMesh == null) return "Mesh未設定";
        // Serialized Unity primitives use a stable built-in GUID/fileID. Resources.GetBuiltinResource
        // may return a distinct resource wrapper, so object identity with that lookup is unsuitable.
        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(filter.sharedMesh, out string guid, out long fileId)
            || guid != "0000000000000000e000000000000000" || fileId != 10207)
            return "標準球体以外のMesh (" + guid + "/" + fileId + ")";
        return null;
    }

    [MenuItem("Fantasy Kingdom/開発スタジオ/駒の見た目/生成モデルを検証")]
    public static void ValidateAssets()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null || !material.enableInstancing) throw new InvalidOperationException("駒の共有マテリアルがありません。");
        var white = AssetDatabase.LoadAssetAtPath<Material>(BoardVisualThemeInstaller.ExistingWhiteMaterialPath);
        int triangles = 0;
        foreach (var kind in Kinds)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(kind));
            if (mesh == null || mesh.subMeshCount != 1 || mesh.triangles.Length / 3 > 1800 || mesh.bounds.min.x < -.48f
                || mesh.bounds.max.x > .48f || mesh.bounds.min.z < -.48f || mesh.bounds.max.z > .48f
                || mesh.bounds.min.y < -.501f || mesh.bounds.max.y > .90f)
                throw new InvalidOperationException("駒の生成モデルがサイズ・描画制限を超えています: " + kind);
            triangles += mesh.triangles.Length / 3;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(kind));
            var filter = prefab == null ? null : prefab.GetComponent<MeshFilter>();
            // User-authored replacements are valid and stay outside the generated-model contract.
            if (filter == null || filter.sharedMesh != mesh) continue;
            if (prefab.GetComponentsInChildren<Renderer>(true).Length != 1 || prefab.GetComponentsInChildren<MeshFilter>(true).Length != 1
                || (prefab.GetComponent<MeshRenderer>().sharedMaterial != material && prefab.GetComponent<MeshRenderer>().sharedMaterial != white)
                || prefab.GetComponentsInChildren<Collider>(true).Length != 1
                || prefab.GetComponentsInChildren<Status>(true).Length != 1 || prefab.GetComponentsInChildren<MonoBehaviour>(true).Length != 1)
                throw new InvalidOperationException("駒のPrefabの既存動作・共有描画条件が不正です: " + kind);
        }
        Debug.Log("[R1UnitVisuals] 生成モデル検証OK: " + Kinds.Length + " Mesh / triangles " + triangles);
    }

    static Material LoadOrCreateMaterial()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (existing != null) return existing;
        string path = Folder + "/ChessPalette.png";
        if (!File.Exists(path))
        {
            var texture = new Texture2D(Palette.Length, 2, TextureFormat.RGB24, false);
            try { for (int y = 0; y < 2; y++) for (int x = 0; x < Palette.Length; x++) texture.SetPixel(x, y, Palette[x]); File.WriteAllBytes(path, texture.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.filterMode = FilterMode.Point; importer.wrapMode = TextureWrapMode.Clamp; importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None; importer.textureCompression = TextureImporterCompression.Uncompressed; importer.isReadable = false;
            importer.SaveAndReimport();
        }
        var shader = Shader.Find("Standard");
        if (shader == null) throw new InvalidOperationException("Standard shaderがありません。");
        var material = new Material(shader) { name = "R1 Chess Metal", enableInstancing = true };
        material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path); material.color = Color.white;
        material.SetFloat("_Metallic", .55f); material.SetFloat("_Glossiness", .32f);
        AssetDatabase.CreateAsset(material, MaterialPath); return material;
    }
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    [MenuItem("Fantasy Kingdom/開発スタジオ/駒の見た目/18職種の見本画像を作成")]
    public static void RenderPreview()
    {
        const int width = 330, height = 360, columns = 4;
        int rows = (Kinds.Length + columns - 1) / columns;
        EnsureFolder(Folder + "/Review");
        var scene = EditorSceneManager.NewPreviewScene();
        var atlas = new Texture2D(width * columns, height * rows, TextureFormat.RGB24, false);
        var readback = new Texture2D(width, height, TextureFormat.RGB24, false);
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active; var ambient = RenderSettings.ambientLight; var ambientMode = RenderSettings.ambientMode;
        Camera camera = null;
        try
        {
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.58f, .63f, .70f);
            var cameraObject = new GameObject("駒見本カメラ"); SceneManager.MoveGameObjectToScene(cameraObject, scene);
            camera = cameraObject.AddComponent<Camera>(); camera.scene = scene; camera.orthographic = true; camera.orthographicSize = .79f;
            camera.nearClipPlane = .01f; camera.farClipPlane = 20; camera.targetTexture = target; camera.allowHDR = false;
            camera.transform.position = new Vector3(1.7f, 1.35f, -2.6f); camera.transform.LookAt(new Vector3(0, .04f, 0));
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(27, 33, 43, 255);
            var lightObject = new GameObject("駒見本照明"); SceneManager.MoveGameObjectToScene(lightObject, scene);
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.35f;
            light.color = new Color(1, .9f, .76f); light.transform.rotation = Quaternion.Euler(38, -32, 0); light.shadows = LightShadows.None;
            for (int i = 0; i < Kinds.Length; i++)
            {
                var model = new GameObject(Kinds[i].ToString()); SceneManager.MoveGameObjectToScene(model, scene);
                try
                {
                    model.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(Kinds[i]));
                    model.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(BoardVisualThemeInstaller.ExistingWhiteMaterialPath);
                    camera.Render(); RenderTexture.active = target; readback.ReadPixels(new Rect(0, 0, width, height), 0, 0); readback.Apply();
                    atlas.SetPixels((i % columns) * width, (rows - 1 - i / columns) * height, width, height, readback.GetPixels());
                }
                finally { UnityEngine.Object.DestroyImmediate(model); }
            }
            atlas.Apply(); File.WriteAllBytes(Folder + "/Review/R1ChessUnits.png", atlas.EncodeToPNG()); AssetDatabase.ImportAsset(Folder + "/Review/R1ChessUnits.png");
        }
        finally
        {
            RenderTexture.active = previous; RenderSettings.ambientLight = ambient; RenderSettings.ambientMode = ambientMode;
            if (camera != null) camera.targetTexture = null;
            target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(readback); UnityEngine.Object.DestroyImmediate(atlas);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    public static void InstallAndReview() { Install(); RenderPreview(); }
}
#endif
