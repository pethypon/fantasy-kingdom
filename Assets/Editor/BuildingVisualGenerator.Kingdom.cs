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

public static partial class BuildingVisualGenerator
{
    public const string EnemyMaterialPath = ArtFolder + "/EnemyBuildingPalette.mat";
    public const string KingdomReviewPath = ArtFolder + "/Review/KingdomBuildings.png";
    public static string DarkMeshPath(FacilityKind kind) => ArtFolder + "/EnemyMeshes/" + kind + ".asset";
    public static string DarkPrefabPath(FacilityKind kind) => ArtFolder + "/EnemyPrefabs/" + kind + ".prefab";

    // White marble, weathered metal and restrained warm fittings. Swatch meanings
    // remain unchanged, so authored gameplay data never participates in the bake.
    static readonly Color32[] MarblePalette = {
        new Color32(153, 156, 153, 255), new Color32(207, 206, 192, 255), new Color32(191, 196, 194, 255),
        new Color32(105, 121, 132, 255), new Color32(151, 130, 83, 255), new Color32(67, 76, 82, 255),
        new Color32(36, 114, 120, 255), new Color32(172, 181, 184, 255), new Color32(208, 158, 81, 255),
        new Color32(178, 156, 95, 255), new Color32(63, 88, 63, 255), new Color32(225, 226, 214, 255),
        new Color32(100, 89, 69, 255), new Color32(92, 174, 182, 255), new Color32(33, 41, 46, 255),
        new Color32(213, 219, 216, 255)
    };
    static readonly Color32[] ObsidianPalette = {
        new Color32(40, 39, 43, 255), new Color32(61, 59, 62, 255), new Color32(69, 72, 78, 255),
        new Color32(30, 34, 40, 255), new Color32(107, 90, 70, 255), new Color32(28, 32, 38, 255),
        new Color32(40, 82, 86, 255), new Color32(42, 43, 48, 255), new Color32(148, 72, 48, 255),
        new Color32(115, 104, 66, 255), new Color32(42, 60, 43, 255), new Color32(88, 85, 88, 255),
        new Color32(49, 44, 40, 255), new Color32(133, 55, 66, 255), new Color32(16, 19, 24, 255),
        new Color32(123, 126, 133, 255)
    };

    [MenuItem("Fantasy Kingdom/開発スタジオ/見た目/白大理石と黒石の建物18種を導入")]
    public static void InstallKingdomArt()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("ゲームを停止してから建物の見た目を作成してください。");
        EnsureFolder(ArtFolder + "/Meshes"); EnsureFolder(ArtFolder + "/EnemyMeshes");
        EnsureFolder(ArtFolder + "/EnemyPrefabs"); EnsureFolder(ArtFolder + "/Review");
        var white = BakeKingdomMaterial(ArtFolder + "/BuildingPalette.mat", MarblePalette);
        var black = BakeKingdomMaterial(EnemyMaterialPath, ObsidianPalette);
        int updated = 0;
        foreach (FacilityKind kind in Enum.GetValues(typeof(FacilityKind)))
        {
            // The user-authored crystal and its material are intentionally outside this art pass.
            // In particular, do not reserialize even the installed generated crystal assets.
            if (kind == FacilityKind.SubCrystal) continue;
            var normal = BakeKingdomMesh(kind, false);
            var dark = BakeKingdomMesh(kind, true);
            if (UpgradeKnownBuilding(PrefabPath(kind), kind, normal, white)) updated++;
            string legacy = LegacyFolder + "/" + kind + "/Visual.prefab";
            if (File.Exists(legacy)) UpgradeKnownBuilding(legacy, kind, normal, white);
            var root = new GameObject("EnemyBuilding_" + kind);
            try
            {
                root.AddComponent<MeshFilter>().sharedMesh = dark;
                ConfigureKingdomRenderer(root.AddComponent<MeshRenderer>(), black);
                PrefabUtility.SaveAsPrefabAsset(root, DarkPrefabPath(kind));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        AssetDatabase.SaveAssets();
        ValidateKingdomArt();
        RenderKingdomPreview();
        Debug.Log("[KingdomBuildings] 17 white marble / 17 dark stone models refined; crystal assets retained; upgraded standard prefabs=" + updated + ". Custom mesh references retained.");
    }

    static Mesh BakeKingdomMesh(FacilityKind kind, bool dark)
    {
        string path = dark ? DarkMeshPath(kind) : MeshPath(kind);
        var baked = MakeBuilding(kind, dark).Build(kind + (dark ? "_DarkKingdom" : "_MarbleKingdom"));
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null) { AssetDatabase.CreateAsset(baked, path); return baked; }
        EditorUtility.CopySerialized(baked, existing); UnityEngine.Object.DestroyImmediate(baked);
        EditorUtility.SetDirty(existing); return existing;
    }
    static Material BakeKingdomMaterial(string path, Color32[] colors)
    {
        string texturePath = path.Replace(".mat", ".png");
        var texture = new Texture2D(16, 2, TextureFormat.RGB24, false);
        for (int y = 0; y < 2; y++) for (int x = 0; x < 16; x++) texture.SetPixel(x, y, colors[x]);
        File.WriteAllBytes(texturePath, texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
        importer.filterMode = FilterMode.Point; importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.isReadable = false; importer.SaveAndReimport();
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, path); }
        material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        material.mainTextureScale = Vector2.one; material.mainTextureOffset = Vector2.zero;
        material.color = Color.white; material.SetFloat("_Metallic", .12f); material.SetFloat("_Glossiness", .31f);
        material.enableInstancing = true; EditorUtility.SetDirty(material); return material;
    }
    static void ConfigureKingdomRenderer(MeshRenderer renderer, Material material)
    {
        renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true; renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    }
    static bool UpgradeKnownBuilding(string path, FacilityKind kind, Mesh mesh, Material material)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null) return false;
        var filter = asset.GetComponent<MeshFilter>();
        // An author-selected mesh is authoritative. This command updates only the
        // generated stock art at its known mesh path, preserving prefab GUIDs.
        if (filter == null || filter.sharedMesh == null || AssetDatabase.GetAssetPath(filter.sharedMesh) != MeshPath(kind)) return false;
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var status = root.GetComponent<Status>();
            string before = status == null ? null : EditorJsonUtility.ToJson(status);
            root.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.GetComponent<MeshRenderer>();
            if (renderer == null) throw new InvalidOperationException("標準建物のRendererがありません: " + path);
            ConfigureKingdomRenderer(renderer, material);
            var box = root.GetComponent<BoxCollider>();
            if (box != null) { box.center = mesh.bounds.center; box.size = mesh.bounds.size + Vector3.one * .025f; }
            if (status != null && before != EditorJsonUtility.ToJson(status))
                throw new InvalidOperationException("見た目作成で建物のステータスが変化しました: " + path);
            PrefabUtility.SaveAsPrefabAsset(root, path); return true;
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static void AddKingdomDetails(Builder b, FacilityKind kind, bool dark)
    {
        if (kind == FacilityKind.SubCrystal) return;
        // Inlaid corners make the footprint legible without a second draw call.
        for (int x = -1; x <= 1; x += 2) for (int z = -1; z <= 1; z += 2)
        {
            b.Box(new Vector3(x * .36f, .102f, z * .36f), new Vector3(.07f, .014f, .07f), 2);
            b.Box(new Vector3(x * .36f, .113f, z * .36f), new Vector3(.028f, .009f, .028f), 4, 45);
        }
        bool residence = kind == FacilityKind.House || kind == FacilityKind.LuxuryHouse || kind == FacilityKind.Bakery
            || kind == FacilityKind.Barracks || kind == FacilityKind.Warehouse || kind == FacilityKind.LoggingCamp;
        if (residence)
        {
            // A carved portal, projecting sill and a heraldic crest differentiate
            // masonry from a plain cube while retaining each building's tool/sign.
            float z = kind == FacilityKind.Warehouse ? -.185f : kind == FacilityKind.LoggingCamp ? -.085f : -.242f;
            if (kind == FacilityKind.Barracks) z = -.32f;
            float x = kind == FacilityKind.Bakery || kind == FacilityKind.Barracks ? -.08f : kind == FacilityKind.Warehouse ? -.06f : 0;
            b.Box(new Vector3(x, .16f, z), new Vector3(.27f, .045f, .09f), 2);
            for (int side = -1; side <= 1; side += 2)
                b.Box(new Vector3(x + side * .12f, .31f, z + .015f), new Vector3(.037f, .25f, .04f), 11);
            // Two sloping voussoirs form a real pointed portal instead of a painted sign.
            for (int side = -1; side <= 1; side += 2)
            {
                var from = new Vector3(x + side * .12f, .432f, z + .01f);
                var to = new Vector3(x, .53f, z + .01f);
                var axis = to - from;
                b.Frustum(from, .025f, .025f, axis.magnitude, 2, 4, axis.normalized);
            }
            b.Frustum(new Vector3(x, .48f, z), .047f, 0, .024f, 4, 4, Vector3.back);
            b.Box(new Vector3(x, .45f, z - .01f), new Vector3(.012f, .065f, .01f), 15);
            if (kind != FacilityKind.LoggingCamp)
            {
                // Low buttresses anchor the rear corners and give lateral views depth.
                for (int side = -1; side <= 1; side += 2)
                {
                    b.Box(new Vector3(side * .31f, .24f, .25f), new Vector3(.07f, .25f, .12f), 2);
                    b.Roof(new Vector3(side * .31f, .365f, .25f), .075f, .12f, .055f, 11);
                }
            }
        }
        if (kind == FacilityKind.Well)
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4;
                b.Box(new Vector3(Mathf.Cos(a) * .285f, .295f, Mathf.Sin(a) * .285f), new Vector3(.014f, .17f, .014f), 3, i * 45);
            }
        AddFunctionCarvings(b, kind);
        if (kind == FacilityKind.HeroSword)
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI / 2 + Mathf.PI / 4;
                var p = new Vector3(Mathf.Cos(a) * .29f, .12f, Mathf.Sin(a) * .29f);
                b.Frustum(p, .025f, 0, .13f, 15, 4);
            }
        if (!dark) return;
        // Enemy buildings keep their readable function, but defensive thorn ribs,
        // tapered finials and a recessed red seal create a related dark silhouette.
        bool tall = residence || kind == FacilityKind.Mine || kind == FacilityKind.Quarry;
        int count = tall ? 4 : 2;
        for (int i = 0; i < count; i++)
        {
            int side = (i & 1) == 0 ? -1 : 1;
            float z = i < 2 ? .31f : -.31f;
            var p = new Vector3(side * .385f, .12f, z);
            b.Frustum(p, .035f, .022f, tall ? .40f : .22f, 5, 5);
            if (tall)
            {
                // Attached diagonal ribs make these weight-bearing buttresses,
                // rather than a separate picket fence around a normal house.
                var end = new Vector3(side * .28f, .54f, z * .78f);
                var start = p + Vector3.up * .17f;
                var axis = end - start;
                b.Frustum(start, .035f, .024f, axis.magnitude, 2, 4, axis.normalized);
                b.Frustum(p, .063f, .048f, .11f, 2, 4);
            }
            b.Frustum(p + Vector3.up * (tall ? .40f : .22f), .043f, 0, .16f, 15, 5,
                new Vector3(-side * .2f, 1, -.15f));
            b.Ring(p + Vector3.up * .09f, .042f, .027f, .025f, 4, 6);
        }
        b.Frustum(new Vector3(0, .115f, -.35f), .06f, .06f, .012f, 5, 8);
        b.Frustum(new Vector3(0, .129f, -.35f), .035f, .035f, .006f, 13, 4);
    }

    static void AddFunctionCarvings(Builder b, FacilityKind kind)
    {
        switch (kind)
        {
            case FacilityKind.Bakery:
                // Recessed oven ledge, stacked fuel and the stone chimney crown.
                b.Box(new Vector3(-.08f, .18f, -.29f), new Vector3(.29f, .045f, .11f), 2);
                for (int i = 0; i < 3; i++)
                    b.Frustum(new Vector3(-.29f + i * .055f, .17f, .28f), .028f, .028f, .13f, 0, 6, Vector3.forward);
                break;
            case FacilityKind.LoggingCamp:
                b.Box(new Vector3(.24f, .20f, -.19f), new Vector3(.07f, .15f, .23f), 5);
                for (int side = -1; side <= 1; side += 2)
                    b.Box(new Vector3(.13f, .21f, -.22f + side * .12f), new Vector3(.45f, .035f, .022f), 4);
                break;
            case FacilityKind.Quarry:
                for (int side = -1; side <= 1; side += 2)
                {
                    var start = new Vector3(side * .29f, .67f, .23f);
                    var axis = new Vector3(-side * .23f, .32f, 0);
                    b.Frustum(start, .026f, .026f, axis.magnitude, 5, 4, axis.normalized);
                    b.Box(new Vector3(side * .29f, .18f, .23f), new Vector3(.16f, .13f, .19f), 2);
                }
                break;
            case FacilityKind.Mine:
                for (int side = -1; side <= 1; side += 2)
                {
                    b.Box(new Vector3(side * .18f, .53f, -.28f), new Vector3(.09f, .045f, .11f), 2);
                    b.Frustum(new Vector3(side * .265f, .27f, -.20f), .025f, .025f, .15f, 4, 5);
                }
                break;
            case FacilityKind.Barracks:
                for (int side = -1; side <= 1; side += 2)
                {
                    b.Frustum(new Vector3(side * .23f, .21f, -.32f), .016f, .016f, .43f, 15, 4,
                        new Vector3(-side * .23f, 1, 0));
                    b.Box(new Vector3(side * .21f, .26f, -.32f), new Vector3(.10f, .02f, .025f), 4);
                }
                break;
            case FacilityKind.House:
            case FacilityKind.LuxuryHouse:
                b.Box(new Vector3(0, .50f, -.245f), new Vector3(.20f, .035f, .075f), 2);
                break;
            case FacilityKind.Well:
                for (int side = -1; side <= 1; side += 2)
                    b.Box(new Vector3(side * .29f, .44f, 0), new Vector3(.09f, .06f, .11f), 4);
                break;
            case FacilityKind.Warehouse:
                b.Box(new Vector3(-.06f, .55f, -.195f), new Vector3(.38f, .04f, .08f), 2);
                b.Box(new Vector3(-.06f, .48f, -.205f), new Vector3(.025f, .19f, .025f), 5);
                break;
            case FacilityKind.StoneWall:
                for (int side = -1; side <= 1; side += 2)
                {
                    b.Box(new Vector3(side * .33f, .35f, -.21f), new Vector3(.11f, .47f, .12f), 2);
                    b.Roof(new Vector3(side * .33f, .585f, -.21f), .13f, .14f, .075f, 11);
                }
                break;
            case FacilityKind.Mortar:
                for (int side = -1; side <= 1; side += 2)
                {
                    var start = new Vector3(side * .25f, .20f, .04f);
                    var axis = new Vector3(-side * .12f, .22f, 0);
                    b.Frustum(start, .03f, .025f, axis.magnitude, 4, 5, axis.normalized);
                }
                break;
            case FacilityKind.Cannon:
                for (int side = -1; side <= 1; side += 2)
                    for (int spoke = 0; spoke < 4; spoke++)
                    {
                        float angle = spoke * Mathf.PI * .5f;
                        var axis = new Vector3(0, Mathf.Cos(angle), Mathf.Sin(angle));
                        b.Frustum(new Vector3(side * .35f, .26f, .10f), .013f, .013f, .13f, 4, 4, axis);
                    }
                break;
        }
    }

    public static void ValidateKingdomArt()
    {
        int total = 0;
        var white = AssetDatabase.LoadAssetAtPath<Material>(ArtFolder + "/BuildingPalette.mat");
        var black = AssetDatabase.LoadAssetAtPath<Material>(EnemyMaterialPath);
        if (white == null || black == null || white == black || !white.enableInstancing || !black.enableInstancing)
            throw new InvalidOperationException("建物の陣営別共有マテリアルがありません。");
        foreach (FacilityKind kind in Enum.GetValues(typeof(FacilityKind)))
            for (int faction = 0; faction < 2; faction++)
            {
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(faction == 0 ? MeshPath(kind) : DarkMeshPath(kind));
                if (mesh == null || mesh.subMeshCount != 1 || mesh.triangles.Length / 3 > 2500 || mesh.bounds.min.x < -.497f
                    || mesh.bounds.max.x > .497f || mesh.bounds.min.z < -.497f || mesh.bounds.max.z > .497f
                    || Mathf.Abs(mesh.bounds.min.y + .5f) > .0001f || mesh.bounds.max.y > .9f
                    || mesh.vertices.Any(v => !Finite(v.x) || !Finite(v.y) || !Finite(v.z)))
                    throw new InvalidOperationException("建物の接地・1マス・描画予算の違反: " + kind + "/" + faction);
                total += mesh.triangles.Length / 3;
                if (faction == 0) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DarkPrefabPath(kind));
                if (prefab == null || prefab.GetComponentsInChildren<Renderer>(true).Length != 1
                    || prefab.GetComponent<MeshFilter>().sharedMesh != mesh || prefab.GetComponent<MeshRenderer>().sharedMaterial != black
                    || prefab.GetComponentsInChildren<Collider>(true).Length != 0 || prefab.GetComponentsInChildren<MonoBehaviour>(true).Length != 0)
                    throw new InvalidOperationException("敵建物の見た目Prefabは描画のみである必要があります: " + kind);
            }
        Debug.Log("[KingdomBuildings] ALL PASSED 36 distinct meshes / 2 shared palette materials / totalTriangles=" + total);
    }
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    public static void RenderKingdomPreview()
    {
        const int width = 300, height = 320;
        EnsureFolder(ArtFolder + "/Review");
        var scene = EditorSceneManager.NewPreviewScene();
        var atlas = new Texture2D(width * 6, height * 6, TextureFormat.RGB24, false);
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        var readback = new Texture2D(width, height, TextureFormat.RGB24, false);
        var previousTarget = RenderTexture.active;
        var ambient = RenderSettings.ambientLight; var mode = RenderSettings.ambientMode;
        try
        {
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.47f, .49f, .53f);
            var cameraObject = new GameObject("Kingdom building review camera"); SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>(); camera.scene = scene; camera.orthographic = true; camera.orthographicSize = .94f;
            camera.transform.position = new Vector3(1.8f, 1.6f, -2.6f); camera.transform.LookAt(new Vector3(0, .55f, 0));
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(28, 33, 39, 255);
            camera.targetTexture = target; camera.nearClipPlane = .05f; camera.farClipPlane = 20; camera.allowHDR = false;
            var lightObject = new GameObject("Kingdom review key"); SceneManager.MoveGameObjectToScene(lightObject, scene);
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.15f;
            light.transform.rotation = Quaternion.Euler(40, -35, 0); light.color = new Color(1, .94f, .85f); light.shadows = LightShadows.None;
            var label = new GameObject("Building label"); SceneManager.MoveGameObjectToScene(label, scene);
            label.transform.rotation = camera.transform.rotation;
            label.transform.position = camera.transform.position + camera.transform.forward * 2 - camera.transform.up * .80f;
            var text = label.AddComponent<TextMesh>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 30; text.characterSize = .05f; text.anchor = TextAnchor.MiddleCenter; text.color = new Color32(202, 196, 176, 255);
            label.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            foreach (FacilityKind kind in Enum.GetValues(typeof(FacilityKind)))
                for (int side = 0; side < 2; side++)
                {
                    var model = new GameObject("Review building"); SceneManager.MoveGameObjectToScene(model, scene);
                    try
                    {
                        model.transform.position = Vector3.up * .5f;
                        model.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(side == 0 ? MeshPath(kind) : DarkMeshPath(kind));
                        model.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(side == 0 ? ArtFolder + "/BuildingPalette.mat" : EnemyMaterialPath);
                        text.text = kind + (side == 0 ? " / White" : " / Black");
                        camera.Render(); RenderTexture.active = target; readback.ReadPixels(new Rect(0, 0, width, height), 0, 0); readback.Apply();
                        int index = (int)kind + side * 18;
                        atlas.SetPixels(index % 6 * width, (5 - index / 6) * height, width, height, readback.GetPixels());
                    }
                    finally { UnityEngine.Object.DestroyImmediate(model); }
                }
            atlas.Apply(); File.WriteAllBytes(KingdomReviewPath, atlas.EncodeToPNG()); AssetDatabase.ImportAsset(KingdomReviewPath);
        }
        finally
        {
            RenderTexture.active = previousTarget; RenderSettings.ambientLight = ambient; RenderSettings.ambientMode = mode;
            EditorSceneManager.ClosePreviewScene(scene); target.Release();
            UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(readback); UnityEngine.Object.DestroyImmediate(atlas);
        }
    }
}
#endif
