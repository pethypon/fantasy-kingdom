#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Explicit, idempotent art bake. User OBJ bodies are reduced offline; the game loads only meshes.</summary>
public static partial class DarkEnemyVisualInstaller
{
    public const string Folder = "Assets/_FantasyKingdom/DarkEnemyVisuals";
    public const string EnemyMaterialPath = "Assets/Prefabs/Units/Materials/Materials/キャラクターマテリアル/異形　キャラクター　黒.mat";
    public const string BossMaterialPath = "Assets/Prefabs/Units/Materials/Materials/キャラクターマテリアル/敵　キャラクター　赤.mat";
    public static readonly Kind[] EnemyKinds = { Kind.King, Kind.Knight, Kind.Archer, Kind.Magic, Kind.Assassin, Kind.Scout,
        Kind.Priest, Kind.Guardian, Kind.Crossbow, Kind.Magicsniper, Kind.Bomber, Kind.WhiteMage, Kind.HolyKnight,
        Kind.Samurai, Kind.Ninja, Kind.Berserker, Kind.Navigator };
    public static readonly WildBossArchetype[] Bosses = { WildBossArchetype.GhostKing, WildBossArchetype.Dragon,
        WildBossArchetype.RebelKnight, WildBossArchetype.ThunderMagus };
    public static string EnemyPrefabPath(Kind kind) => Folder + "/Prefabs/Enemy_" + kind + ".prefab";
    public static string BossPrefabPath(WildBossArchetype boss) => Folder + "/Prefabs/Boss_" + boss + ".prefab";
    public static string EnemyMeshPath(Kind kind) => Folder + "/Meshes/Enemy_" + kind + ".asset";
    public static string BossMeshPath(WildBossArchetype boss) => Folder + "/Meshes/Boss_" + boss + ".asset";
    static readonly Dictionary<string, ImportedBody> Sources = new Dictionary<string, ImportedBody>();
    [Serializable] sealed class ImportedBody
    {
        public int formatVersion;
        public string sourceFile, sourceSha256;
        public int originalTriangles, bodyTriangles, bakedTriangles;
        public float[] positions, normals;
    }

    [MenuItem("Fantasy Kingdom/開発スタジオ/駒の見た目/敵の異形17種と強敵4種を導入")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("ゲームを停止してから見た目を導入してください。");
        // Read all source data first; a missing file must not leave half of a roster installed.
        Sources.Clear(); foreach (var source in new[] { "Koi", "Ram", "Crab", "Hound" }) LoadSource(source);
        EnsureFolder(Folder + "/Meshes"); EnsureFolder(Folder + "/Prefabs"); EnsureFolder(Folder + "/Review");
        var black = LoadMaterial(false); var red = LoadMaterial(true);
        foreach (var kind in EnemyKinds)
            Bake(EnemyMeshPath(kind), EnemyPrefabPath(kind), "異形_" + kind, MakeEnemy(kind), black);
        foreach (var boss in Bosses)
            Bake(BossMeshPath(boss), BossPrefabPath(boss), "強敵_" + boss, MakeBoss(boss), red);
        AssetDatabase.SaveAssets(); ValidateAssets();
        Debug.Log("[DarkEnemyVisuals] 敵17種・強敵4種。各1Mesh / 1Renderer / Collider・Status・追加スクリプト0。既存Prefabは保護。");
    }
    static void Bake(string meshPath, string prefabPath, string label, Sculpture sculpture, Material material)
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (mesh == null) { mesh = sculpture.Build(label); AssetDatabase.CreateAsset(mesh, meshPath); }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null) return;
        var root = new GameObject(label);
        try
        {
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
            if (PrefabUtility.SaveAsPrefabAsset(root, prefabPath) == null) throw new InvalidOperationException("見た目Prefabを保存できません: " + label);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    static ImportedBody LoadSource(string key)
    {
        if (Sources.TryGetValue(key, out var data)) return data;
        string path = Folder + "/Sources/" + key + ".json";
        if (!File.Exists(path)) throw new InvalidOperationException("軽量化済みOBJデータがありません: " + path);
        data = JsonUtility.FromJson<ImportedBody>(File.ReadAllText(path));
        if (data == null || data.formatVersion != 1 || data.positions == null || data.normals == null
            || data.positions.Length != data.normals.Length || data.positions.Length != data.bakedTriangles * 9
            || data.bakedTriangles < 100 || data.bakedTriangles > 2400 || string.IsNullOrEmpty(data.sourceSha256))
            throw new InvalidOperationException("OBJデータの形式または軽量化制限が不正です: " + path);
        Sources.Add(key, data); return data;
    }
    static Material LoadMaterial(bool boss)
    {
        string path = boss ? BossMaterialPath : EnemyMaterialPath;
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) throw new InvalidOperationException("既存のキャラクターマテリアルがありません: " + path);
        return material;
    }
    /// <summary>Explicit mesh-only refresh for unpublished validation assets. Prefab GUIDs and materials stay unchanged.</summary>
    public static void RebuildForValidation()
    {
        if (!string.Equals(new DirectoryInfo(Application.dataPath).Parent.Name, "UnityValidation", StringComparison.Ordinal))
            throw new InvalidOperationException("モデルの上書き検証はUnityValidationプロジェクトでだけ実行できます。");
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("ゲームを停止してからモデルを検証してください。");
        Sources.Clear(); foreach (string source in new[] { "Koi", "Ram", "Crab", "Hound" }) LoadSource(source);
        foreach (var kind in EnemyKinds) ReplaceValidationMesh(EnemyMeshPath(kind), MakeEnemy(kind), "異形_" + kind);
        foreach (var boss in Bosses) ReplaceValidationMesh(BossMeshPath(boss), MakeBoss(boss), "強敵_" + boss);
        AssetDatabase.SaveAssets(); ValidateAssets(); RenderPreview();
        Debug.Log("[DarkEnemyVisuals] VALIDATION REBUILD: 21 Mesh、Prefab GUID・元Material変更0。");
    }
    static void ReplaceValidationMesh(string path, Sculpture sculpture, string label)
    {
        var target = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (target == null) throw new InvalidOperationException("先に敵モデルを導入してください: " + path);
        var baked = sculpture.Build(label);
        try { EditorUtility.CopySerialized(baked, target); EditorUtility.SetDirty(target); }
        finally { UnityEngine.Object.DestroyImmediate(baked); }
    }
    [MenuItem("Fantasy Kingdom/開発スタジオ/駒の見た目/敵と強敵の生成モデルを検証")]
    public static void ValidateAssets()
    {
        int total = 0;
        foreach (var kind in EnemyKinds) total += Validate(EnemyMeshPath(kind), EnemyPrefabPath(kind), 3400, .95f);
        foreach (var boss in Bosses) total += Validate(BossMeshPath(boss), BossPrefabPath(boss), 4600, 1.25f);
        Debug.Log("[DarkEnemyVisuals] 検証OK: 21 Mesh、21 Renderer、合計 " + total + " triangles。");
    }
    static int Validate(string meshPath, string prefabPath, int limit, float height)
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath); var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (mesh == null || mesh.subMeshCount != 1 || mesh.triangles.Length / 3 > limit || mesh.bounds.min.y < -.501f
            || mesh.bounds.max.y > height || mesh.bounds.min.x < -.49f || mesh.bounds.max.x > .49f
            || mesh.bounds.min.z < -.49f || mesh.bounds.max.z > .49f)
            throw new InvalidOperationException("異形モデルのサイズ・描画予算が不正です: " + meshPath);
        if (prefab == null || prefab.GetComponentsInChildren<Renderer>(true).Length != 1
            || prefab.GetComponentsInChildren<MeshFilter>(true).Length != 1 || prefab.GetComponentsInChildren<Collider>(true).Length != 0
            || prefab.GetComponentsInChildren<MonoBehaviour>(true).Length != 0 || prefab.GetComponent<MeshFilter>().sharedMesh != mesh
            || !prefab.GetComponent<MeshRenderer>().sharedMaterial.enableInstancing)
            throw new InvalidOperationException("異形モデルの共有描画条件が不正です: " + prefabPath);
        return mesh.triangles.Length / 3;
    }
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
    [MenuItem("Fantasy Kingdom/開発スタジオ/駒の見た目/敵と強敵の見本を作成")]
    public static void RenderPreview()
    {
        var names = new List<string>(); var meshes = new List<string>(); var materials = new List<string>();
        foreach (var kind in EnemyKinds) { names.Add(kind.ToString()); meshes.Add(EnemyMeshPath(kind)); materials.Add(EnemyMaterialPath); }
        RenderSheet("EnemyAberrations.png", names, meshes, materials, 4);
        names.Clear(); meshes.Clear(); materials.Clear();
        foreach (var boss in Bosses) { names.Add(boss.ToString()); meshes.Add(BossMeshPath(boss)); materials.Add(BossMaterialPath); }
        RenderSheet("BossAberrations.png", names, meshes, materials, 4);
    }
    static void RenderSheet(string filename, List<string> names, List<string> meshPaths, List<string> materialPaths, int columns)
    {
        const int width = 360, height = 400; int rows = (names.Count + columns - 1) / columns;
        EnsureFolder(Folder + "/Review"); var scene = EditorSceneManager.NewPreviewScene();
        var atlas = new Texture2D(width * columns, height * rows, TextureFormat.RGB24, false);
        var readback = new Texture2D(width, height, TextureFormat.RGB24, false);
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active; var ambient = RenderSettings.ambientLight; var ambientMode = RenderSettings.ambientMode;
        Camera camera = null;
        try
        {
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.64f, .68f, .74f);
            var co = new GameObject("異形見本カメラ"); SceneManager.MoveGameObjectToScene(co, scene);
            camera = co.AddComponent<Camera>(); camera.scene = scene; camera.orthographic = true; camera.orthographicSize = .89f;
            camera.nearClipPlane = .01f; camera.farClipPlane = 20; camera.targetTexture = target; camera.allowHDR = false;
            camera.transform.position = new Vector3(1.5f, 1.15f, -2.6f); camera.transform.LookAt(new Vector3(0, .06f, 0));
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(34, 38, 48, 255);
            var lo = new GameObject("異形見本照明"); SceneManager.MoveGameObjectToScene(lo, scene);
            var light = lo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.7f;
            light.color = new Color(1, .87f, .76f); light.transform.rotation = Quaternion.Euler(38, -32, 0); light.shadows = LightShadows.None;
            // A cool fill exposes the black stone silhouette without adding lights to the game.
            var fillObject = new GameObject("異形見本補助光"); SceneManager.MoveGameObjectToScene(fillObject, scene);
            var fill = fillObject.AddComponent<Light>(); fill.type = LightType.Directional; fill.intensity = .8f;
            fill.color = new Color(.55f, .72f, 1f); fill.transform.rotation = Quaternion.Euler(25, 145, 0); fill.shadows = LightShadows.None;
            for (int i = 0; i < names.Count; i++)
            {
                var model = new GameObject(names[i]); SceneManager.MoveGameObjectToScene(model, scene);
                try
                {
                    model.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPaths[i]);
                    model.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPaths[i]);
                    camera.Render(); RenderTexture.active = target; readback.ReadPixels(new Rect(0, 0, width, height), 0, 0); readback.Apply();
                    atlas.SetPixels((i % columns) * width, (rows - 1 - i / columns) * height, width, height, readback.GetPixels());
                }
                finally { UnityEngine.Object.DestroyImmediate(model); }
            }
            atlas.Apply(); File.WriteAllBytes(Folder + "/Review/" + filename, atlas.EncodeToPNG()); AssetDatabase.ImportAsset(Folder + "/Review/" + filename);
            File.WriteAllLines(Folder + "/Review/" + Path.GetFileNameWithoutExtension(filename) + ".txt", names);
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
