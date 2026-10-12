#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Standard building art is baked in the Editor. Runtime prefabs contain one mesh,
/// one shared palette material and one box collider; no procedural Update is needed.
/// Rebuilding edits visual components only, retaining definition and gameplay data.
/// </summary>
public static partial class BuildingVisualGenerator
{
    public const string ArtFolder = "Assets/_FantasyKingdom/Art/Buildings";
    public const string PrefabFolder = "Assets/Prefabs/Facilities";
    public const string LegacyFolder = "Assets/_FantasyKingdom/GameContent/Buildings/既存の建物データ";
    public const string SystemPrefab = "Assets/Prefabs/GameSystem/GameGenerator 1.prefab";
    static readonly string[] PrefabNames = {
        "Field", "Bakery", "LoggingCamp", "Quarry", "Mine", "Barracks", "House", "LuxuryHouse",
        "Well", "Warehouse", "WoodWall", "StoneWall", "Mortar", "Cannon", "RestraintTrap", "Spiketrap", "Hero'sSword", "SubCrystal"
    };
    static readonly Color[] Palette = {
        new Color32(68, 43, 31, 255), new Color32(139, 91, 47, 255), new Color32(120, 126, 130, 255),
        new Color32(42, 59, 76, 255), new Color32(193, 142, 65, 255), new Color32(53, 64, 72, 255),
        new Color32(30, 159, 166, 255), new Color32(138, 43, 36, 255), new Color32(242, 160, 59, 255),
        new Color32(223, 185, 82, 255), new Color32(76, 112, 64, 255), new Color32(220, 209, 173, 255),
        new Color32(89, 61, 39, 255), new Color32(89, 186, 205, 255), new Color32(16, 24, 32, 255),
        new Color32(213, 224, 224, 255)
    };

    public static string PrefabPath(FacilityKind kind) => PrefabFolder + "/" + PrefabNames[(int)kind] + ".prefab";
    public static string MeshPath(FacilityKind kind) => ArtFolder + "/Meshes/" + kind + ".asset";

    [MenuItem("Fantasy Kingdom/ゲーム制作/建物デザイン/標準18種類を生成・更新")]
    public static void Generate()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("建物デザインの生成はプレイ停止中に行ってください。");
        EnsureFolder(ArtFolder + "/Meshes"); EnsureFolder(PrefabFolder);
        var material = MakeMaterial();
        var paths = new Dictionary<FacilityKind, GameObject>();
        var descriptions = new List<string>();
        foreach (FacilityKind kind in Enum.GetValues(typeof(FacilityKind)))
        {
            var mesh = MakeBuilding(kind).Build(kind + "_BakedVisual");
            var existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(kind));
            if (existingMesh == null) AssetDatabase.CreateAsset(mesh, MeshPath(kind));
            else { EditorUtility.CopySerialized(mesh, existingMesh); UnityEngine.Object.DestroyImmediate(mesh); mesh = existingMesh; }
            paths[kind] = ConfigurePrefab(PrefabPath(kind), kind, mesh, material, false);
            string legacy = LegacyFolder + "/" + kind + "/Visual.prefab";
            if (File.Exists(legacy)) ConfigurePrefab(legacy, kind, mesh, material, true);
            descriptions.Add(kind + "," + mesh.vertexCount + "," + mesh.triangles.Length / 3 + ",1,1,1");
        }
        ConfigureSystemReferences(paths);
        AssetDatabase.SaveAssets();
        string docs = Path.GetFullPath("Docs"); Directory.CreateDirectory(docs);
        File.WriteAllLines(Path.Combine(docs, "Building_Visual_Budget.csv"), new[] { "Kind,Vertices,Triangles,Renderers,Materials,Colliders" }.Concat(descriptions));
        RenderPreview(paths, Path.Combine(docs, "Building_Design_20261012.png"));
        Debug.Log("[BuildingVisual] Generated 18 standard buildings. One renderer, material and box collider per building; preview: Docs/Building_Design_20261012.png");
    }

    // Batch entry point. The caller supplies an isolated Unity validation project.
    public static void Run()
    {
        try { Generate(); Validate(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    public static void Validate()
    {
        var shared = AssetDatabase.LoadAssetAtPath<Material>(ArtFolder + "/BuildingPalette.mat");
        if (shared == null || !shared.enableInstancing) throw new InvalidOperationException("Building palette material missing or instancing disabled.");
        int totalTriangles = 0;
        var geometry = new HashSet<string>();
        foreach (FacilityKind kind in Enum.GetValues(typeof(FacilityKind)))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(kind));
            if (prefab == null) throw new InvalidOperationException("Building prefab missing: " + kind);
            var renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
            var colliders = prefab.GetComponentsInChildren<Collider>(true);
            if (renderers.Length != 1 || colliders.Length != 1 || !(colliders[0] is BoxCollider)) throw new InvalidOperationException("Building component budget: " + kind);
            var filter = prefab.GetComponent<MeshFilter>();
            var mesh = filter == null ? null : filter.sharedMesh;
            if (mesh == null || mesh.subMeshCount != 1 || renderers[0].sharedMaterial != shared || mesh.triangles.Length / 3 > 2500)
                throw new InvalidOperationException("Building geometry/material budget: " + kind);
            if (Mathf.Abs(mesh.bounds.center.x) + mesh.bounds.extents.x > .51f || Mathf.Abs(mesh.bounds.center.z) + mesh.bounds.extents.z > .51f)
                throw new InvalidOperationException("Building extends outside one board cell: " + kind);
            if (mesh.bounds.max.y > .9f || Mathf.Abs(mesh.bounds.min.y + .5f) > .0001f || !mesh.uv.All(v => v.x >= 0 && v.x <= 1 && v.y >= 0 && v.y <= 1))
                throw new InvalidOperationException("Building height/palette bounds: " + kind);
            var box = (BoxCollider)colliders[0];
            if ((box.center - mesh.bounds.center).sqrMagnitude > .000001f
                || (box.size - mesh.bounds.size - new Vector3(.025f, .025f, .025f)).sqrMagnitude > .000001f
                || box.center.x + box.size.x / 2 > .51f || box.center.x - box.size.x / 2 < -.51f
                || box.center.z + box.size.z / 2 > .51f || box.center.z - box.size.z / 2 < -.51f)
                throw new InvalidOperationException("Building collider does not match the grounded one-cell visual: " + kind);
            if (prefab.transform.localScale != Vector3.one) throw new InvalidOperationException("Building scale is not normalized: " + kind);
            geometry.Add(AssetDatabase.GetAssetPath(mesh)); totalTriangles += mesh.triangles.Length / 3;
            var authored = AssetDatabase.LoadAssetAtPath<GameObject>(LegacyFolder + "/" + kind + "/Visual.prefab");
            var authoredFilter = authored == null ? null : authored.GetComponent<MeshFilter>();
            if (authored != null && (authored.GetComponent<Status>() == null || authoredFilter == null || authoredFilter.sharedMesh != mesh))
                throw new InvalidOperationException("Authored building visual/status missing: " + kind);
        }
        if (geometry.Count != 18) throw new InvalidOperationException("Building silhouettes must use 18 distinct meshes.");
        var systemObject = AssetDatabase.LoadAssetAtPath<GameObject>(SystemPrefab);
        var system = systemObject == null ? null : systemObject.GetComponentInChildren<BuildSystem>(true);
        if (system == null) throw new InvalidOperationException("BuildSystem prefab is missing.");
        var serialized = new SerializedObject(system); var entries = serialized.FindProperty("facilityPrefabs");
        if (entries.arraySize != 18) throw new InvalidOperationException("BuildSystem must contain exactly 18 standard building mappings.");
        for (int i = 0; i < entries.arraySize; i++)
            if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("kind").intValue != i || entries.GetArrayElementAtIndex(i).FindPropertyRelative("prefab").objectReferenceValue != AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath((FacilityKind)i)))
                throw new InvalidOperationException("BuildSystem mapping does not match the building kind: " + i);
        Debug.Log("[BuildingVisual] ALL PASSED buildings=18 sharedMaterials=1 totalTriangles=" + totalTriangles);
    }

    static Material MakeMaterial()
    {
        string texturePath = ArtFolder + "/BuildingPalette.png";
        var palette = new Texture2D(16, 16, TextureFormat.RGB24, false);
        for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) palette.SetPixel(x, y, Palette[x]);
        File.WriteAllBytes(texturePath, palette.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(palette);
        AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
        importer.filterMode = FilterMode.Point; importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.isReadable = false; importer.SaveAndReimport();
        string materialPath = ArtFolder + "/BuildingPalette.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, materialPath); }
        material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        material.mainTextureScale = Vector2.one; material.mainTextureOffset = Vector2.zero;
        material.color = Color.white; material.SetFloat("_Metallic", .18f); material.SetFloat("_Glossiness", .22f);
        material.enableInstancing = true; EditorUtility.SetDirty(material); return material;
    }

    static GameObject ConfigurePrefab(string path, FacilityKind kind, Mesh mesh, Material material, bool mustRetainStatus)
    {
        GameObject root = null; bool loaded = false;
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        // A number of old stock prefabs inherit deleted source GUIDs. Rebuild their
        // visual shell at the existing asset path (.meta/GUID is never replaced).
        if (asset != null && !PrefabUtility.IsPrefabAssetMissing(asset))
        {
            try { root = PrefabUtility.LoadPrefabContents(path); loaded = true; }
            catch (Exception) { root = null; }
        }
        if (root != null && !mustRetainStatus && (PrefabUtility.IsPrefabAssetMissing(root) || root.GetComponent<MeshFilter>() == null))
        {
            PrefabUtility.UnloadPrefabContents(root); root = null; loaded = false;
        }
        if (root == null)
        {
            if (mustRetainStatus) throw new InvalidOperationException("Cannot safely edit authored building prefab: " + path);
            root = new GameObject(PrefabNames[(int)kind]);
        }
        try
        {
            if (mustRetainStatus && root.GetComponent<Status>() == null) throw new InvalidOperationException("Authored Status must be retained: " + path);
            var preservedStatus = root.GetComponent<Status>();
            string statusBefore = preservedStatus == null ? null : EditorJsonUtility.ToJson(preservedStatus);
            // Unpack inherited visual shells so a later edit of one standard building
            // cannot alter the appearance or scale of an unrelated building.
            if (PrefabUtility.IsPartOfPrefabInstance(root)) PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            root.transform.localScale = Vector3.one;
            // Unity's destroyed/missing component wrappers compare equal to null,
            // but are non-null CLR objects: null-coalescing cannot be used here.
            var filter = root.GetComponent<MeshFilter>();
            if (filter == null) filter = root.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = root.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { material };
            renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            foreach (var old in root.GetComponentsInChildren<Renderer>(true)) if (old != renderer) old.enabled = false;
            var collider = root.GetComponent<BoxCollider>();
            if (collider == null) collider = root.AddComponent<BoxCollider>();
            foreach (var old in root.GetComponentsInChildren<Collider>(true)) if (old != collider) UnityEngine.Object.DestroyImmediate(old);
            collider.center = mesh.bounds.center; collider.size = mesh.bounds.size + new Vector3(.025f, .025f, .025f);
            collider.isTrigger = false;
            if (!mustRetainStatus) root.name = PrefabNames[(int)kind];
            if (preservedStatus != null && statusBefore != EditorJsonUtility.ToJson(preservedStatus))
                throw new InvalidOperationException("Visual rebuild changed building gameplay data: " + path);
            return PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { if (loaded) PrefabUtility.UnloadPrefabContents(root); else UnityEngine.Object.DestroyImmediate(root); }
    }

    static void ConfigureSystemReferences(Dictionary<FacilityKind, GameObject> prefabs)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(SystemPrefab) == null) return;
        var root = PrefabUtility.LoadPrefabContents(SystemPrefab);
        try
        {
            foreach (var build in root.GetComponentsInChildren<BuildSystem>(true))
            {
                var serialized = new SerializedObject(build); var list = serialized.FindProperty("facilityPrefabs");
                list.arraySize = prefabs.Count;
                foreach (var pair in prefabs)
                {
                    var item = list.GetArrayElementAtIndex((int)pair.Key);
                    item.FindPropertyRelative("kind").intValue = (int)pair.Key;
                    item.FindPropertyRelative("prefab").objectReferenceValue = pair.Value;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            PrefabUtility.SaveAsPrefabAsset(root, SystemPrefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    static void RenderPreview(Dictionary<FacilityKind, GameObject> prefabs, string output)
    {
        const int width = 320, height = 300;
        var scene = EditorSceneManager.NewPreviewScene();
        var atlas = new Texture2D(width * 6, height * 3, TextureFormat.RGB24, false);
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        var readback = new Texture2D(width, height, TextureFormat.RGB24, false);
        var previousTarget = RenderTexture.active; var previousAmbient = RenderSettings.ambientLight;
        var previousAmbientMode = RenderSettings.ambientMode;
        try
        {
            var cameraObj = new GameObject("Building preview camera"); SceneManager.MoveGameObjectToScene(cameraObj, scene);
            var camera = cameraObj.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = .88f;
            camera.scene = scene;
            camera.transform.position = new Vector3(1.8f, 1.6f, -2.6f); camera.transform.LookAt(new Vector3(0, .42f, 0));
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(24, 32, 43, 255);
            camera.targetTexture = target; camera.nearClipPlane = .05f; camera.farClipPlane = 20; camera.allowHDR = false;
            var lightObj = new GameObject("Building preview key light"); SceneManager.MoveGameObjectToScene(lightObj, scene);
            var light = lightObj.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.35f; light.color = new Color(1, .92f, .8f);
            light.transform.rotation = Quaternion.Euler(40, -35, 0); light.shadows = LightShadows.None;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.4f, .45f, .55f);
            var label = new GameObject("Building name"); SceneManager.MoveGameObjectToScene(label, scene);
            label.transform.rotation = camera.transform.rotation;
            label.transform.position = camera.transform.position + camera.transform.forward * 2 + camera.transform.up * -.74f;
            var text = label.AddComponent<TextMesh>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 38; text.characterSize = .052f; text.anchor = TextAnchor.MiddleCenter; text.color = new Color32(224, 190, 117, 255);
            label.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            foreach (var pair in prefabs)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(pair.Value, scene);
                // Gameplay roots represent the center of the occupied cell, half
                // a unit above the terrain. Preview uses a floor-relative origin.
                instance.transform.SetPositionAndRotation(Vector3.up * .5f, Quaternion.identity);
                text.text = ((int)pair.Key + 1).ToString("00") + "  " + pair.Key;
                camera.Render(); RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, width, height), 0, 0); readback.Apply();
                int index = (int)pair.Key;
                atlas.SetPixels(index % 6 * width, (2 - index / 6) * height, width, height, readback.GetPixels());
                UnityEngine.Object.DestroyImmediate(instance);
            }
            atlas.Apply(); File.WriteAllBytes(output, atlas.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previousTarget; RenderSettings.ambientLight = previousAmbient; RenderSettings.ambientMode = previousAmbientMode;
            EditorSceneManager.ClosePreviewScene(scene); target.Release();
            UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(readback); UnityEngine.Object.DestroyImmediate(atlas);
        }
    }

    sealed class Builder
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int> indices = new List<int>();
        void Triangle(Vector3 a, Vector3 b, Vector3 c, int color)
        {
            int start = vertices.Count; var normal = Vector3.Cross(b - a, c - a).normalized;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            for (int i = 0; i < 3; i++) { normals.Add(normal); uv.Add(new Vector2((color + .5f) / 16f, .5f)); indices.Add(start + i); }
        }
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int color)
        {
            int start = vertices.Count; var normal = Vector3.Cross(b - a, c - a).normalized;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            for (int i = 0; i < 4; i++) { normals.Add(normal); uv.Add(new Vector2((color + .5f) / 16f, .5f)); }
            indices.Add(start); indices.Add(start + 1); indices.Add(start + 2);
            indices.Add(start); indices.Add(start + 2); indices.Add(start + 3);
        }
        public void Box(Vector3 position, Vector3 size, int color, float yaw = 0)
        {
            var p = new Vector3[8]; var rotation = Quaternion.Euler(0, yaw, 0);
            for (int i = 0; i < 8; i++) p[i] = position + rotation * Vector3.Scale(new Vector3((i & 1) == 0 ? -.5f : .5f, (i & 2) == 0 ? -.5f : .5f, (i & 4) == 0 ? -.5f : .5f), size);
            Quad(p[0], p[2], p[3], p[1], color); Quad(p[4], p[5], p[7], p[6], color);
            Quad(p[0], p[4], p[6], p[2], color); Quad(p[1], p[3], p[7], p[5], color);
            Quad(p[0], p[1], p[5], p[4], color); Quad(p[2], p[6], p[7], p[3], color);
        }
        public void Frustum(Vector3 bottom, float radiusBottom, float radiusTop, float length, int color, int segments = 8, Vector3? axis = null)
        {
            var up = (axis ?? Vector3.up).normalized;
            var right = Vector3.Cross(Mathf.Abs(up.y) < .9f ? Vector3.up : Vector3.forward, up).normalized;
            var forward = Vector3.Cross(up, right);
            var top = bottom + up * length;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2 / segments, next = (i + 1) * Mathf.PI * 2 / segments;
                var radial = right * Mathf.Cos(angle) + forward * Mathf.Sin(angle);
                var radialNext = right * Mathf.Cos(next) + forward * Mathf.Sin(next);
                var a = bottom + radial * radiusBottom; var b = bottom + radialNext * radiusBottom;
                var c = top + radialNext * radiusTop; var d = top + radial * radiusTop;
                if (radiusTop == 0) Triangle(a, b, top, color);
                else Quad(a, b, c, d, color);
                Triangle(bottom, b, a, color);
                if (radiusTop > 0) Triangle(top, d, c, color);
            }
        }
        public void Ring(Vector3 center, float outer, float inner, float thickness, int color, int segments = 12, Vector3? axis = null)
        {
            var up = (axis ?? Vector3.up).normalized;
            var right = Vector3.Cross(Mathf.Abs(up.y) < .9f ? Vector3.up : Vector3.forward, up).normalized;
            var forward = Vector3.Cross(up, right);
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2 / segments, next = (i + 1) * Mathf.PI * 2 / segments;
                var radial = right * Mathf.Cos(angle) + forward * Mathf.Sin(angle);
                var radialNext = right * Mathf.Cos(next) + forward * Mathf.Sin(next);
                var a = center + radial * outer; var b = center + radialNext * outer;
                var c = center + radialNext * inner; var d = center + radial * inner;
                var offset = up * thickness;
                Quad(a, b, b + offset, a + offset, color); Quad(d, d + offset, c + offset, c, color);
                Quad(a + offset, b + offset, c + offset, d + offset, color); Quad(a, d, c, b, color);
            }
        }
        public void Roof(Vector3 center, float width, float depth, float rise, int color)
        {
            var a = center + new Vector3(-width / 2, 0, -depth / 2); var b = center + new Vector3(width / 2, 0, -depth / 2);
            var c = center + new Vector3(width / 2, 0, depth / 2); var d = center + new Vector3(-width / 2, 0, depth / 2);
            var front = center + new Vector3(0, rise, -depth / 2); var back = center + new Vector3(0, rise, depth / 2);
            Quad(a, d, back, front, color); Quad(front, back, c, b, color);
            Triangle(a, front, b, color); Triangle(d, c, back, color); Quad(a, b, c, d, color);
        }
        public Mesh Build(string name)
        {
            // MapCreate.TryGetHeight gives the actor-center Y (top block + 1),
            // whereas SurfaceTop gives top block + .5. Keep actor/save grid Y
            // intact and bake the visual base at local -.5 so it touches terrain.
            var groundedVertices = new List<Vector3>(vertices.Count);
            foreach (var vertex in vertices) groundedVertices.Add(vertex + Vector3.down * .5f);
            var result = new Mesh { name = name }; result.SetVertices(groundedVertices); result.SetNormals(normals); result.SetUVs(0, uv); result.SetTriangles(indices, 0);
            result.RecalculateBounds(); return result;
        }
    }
}
#endif
