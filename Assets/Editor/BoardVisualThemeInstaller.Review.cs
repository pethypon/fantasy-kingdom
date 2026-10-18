#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static partial class BoardVisualThemeInstaller
{
    [MenuItem("Fantasy Kingdom/開発スタジオ/見た目/陣営別モデルと材質を検証")]
    public static void ValidateAssets()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<BoardVisualThemeCatalog>(CatalogPath);
        if (catalog == null) throw new InvalidOperationException("陣営別モデルのカタログがありません。");
        LoadFactionMaterials(out var white, out var black, out var red);
        if (catalog.playerUnitMaterial != white || catalog.playerBuildingMaterial != white || catalog.playerPlainMaterial != white
            || catalog.enemyUnitMaterial != black || catalog.enemyBuildingMaterial != black || catalog.enemyPlainMaterial != black
            || catalog.bossMaterial != red || catalog.bossPlainMaterial != red)
            throw new InvalidOperationException("カタログは既存の白・黒・赤3マテリアルを共有参照してください。");
        var materials = new HashSet<Material> { white, black, red };
        if (materials.Count != 3 || white.shader == null || black.shader == null || red.shader == null)
            throw new InvalidOperationException("既存の陣営別マテリアルまたはShaderが不正です。");
        int unitCount = 0, buildingCount = 0, bossCount = 0;
        var kinds = new HashSet<Kind>();
        foreach (var style in catalog.units)
            if (style != null && !kinds.Add(style.kind)) throw new InvalidOperationException("駒の見た目が重複しています: " + style.kind);
        foreach (var kind in DarkEnemyVisualInstaller.EnemyKinds)
        {
            var style = catalog.Unit(kind); if (style == null || style.enemyVisual == null || (kind != Kind.King && style.playerVisual == null))
                throw new InvalidOperationException("駒の陣営別モデルが未設定です: " + kind);
            ValidatePure(style.enemyVisual); if (style.playerVisual != null) ValidatePure(style.playerVisual); unitCount++;
        }
        var facilities = new HashSet<FacilityKind>();
        foreach (var style in catalog.buildings)
            if (style != null && !facilities.Add(style.kind)) throw new InvalidOperationException("建物の見た目が重複しています: " + style.kind);
        foreach (FacilityKind kind in Enum.GetValues(typeof(FacilityKind)))
        {
            var style = catalog.Building(kind); if (style == null || style.playerVisual == null || style.enemyVisual == null)
                throw new InvalidOperationException("建物の陣営別モデルが未設定です: " + kind);
            ValidatePure(style.playerVisual); ValidatePure(style.enemyVisual); buildingCount++;
        }
        var bosses = new HashSet<WildBossArchetype>();
        foreach (var style in catalog.bosses)
            if (style != null && !bosses.Add(style.kind)) throw new InvalidOperationException("強敵の見た目が重複しています: " + style.kind);
        foreach (var boss in DarkEnemyVisualInstaller.Bosses)
        {
            var style = catalog.Boss(boss); if (style == null || style.visual == null) throw new InvalidOperationException("強敵のモデルが未設定です: " + boss);
            ValidatePure(style.visual); bossCount++;
        }
        if (new HashSet<Mesh>(catalog.managedSourceMeshes).Count != catalog.managedSourceMeshes.Count
            || new HashSet<string>(catalog.managedDefinitionIds, StringComparer.Ordinal).Count != catalog.managedDefinitionIds.Count
            || catalog.managedSourceMeshes.Contains(null) || catalog.managedDefinitionIds.Contains(string.Empty))
            throw new InvalidOperationException("標準モデル・定義IDの許可リストが不正です。");
        foreach (var kind in R1UnitVisualInstaller.Kinds)
        {
            var mesh = RequirePrefab(R1UnitVisualInstaller.PrefabPath(kind)).GetComponent<MeshFilter>().sharedMesh;
            if (!catalog.ManagesMesh(mesh)) throw new InvalidOperationException("標準駒のメッシュが未登録です: " + kind);
        }
        Debug.Log("[BoardVisualTheme] ALL PASSED units=" + unitCount + " buildings=" + buildingCount + " bosses=" + bossCount
            + " sharedMaterials=" + materials.Count + " managedMeshes=" + catalog.managedSourceMeshes.Count + " managedIds=" + catalog.managedDefinitionIds.Count);
    }
    static void ValidatePure(GameObject prefab)
    {
        if (prefab.GetComponentsInChildren<Renderer>(true).Length != 1 || prefab.GetComponentsInChildren<MeshFilter>(true).Length != 1
            || prefab.GetComponentsInChildren<Collider>(true).Length != 0 || prefab.GetComponentsInChildren<MonoBehaviour>(true).Length != 0
            || prefab.GetComponent<MeshFilter>() == null || prefab.GetComponent<MeshFilter>().sharedMesh == null)
            throw new InvalidOperationException("表示専用モデルの部品が不正です: " + AssetDatabase.GetAssetPath(prefab));
    }
    sealed class PreviewItem
    {
        public string label;
        public Mesh mesh;
        public Material material;
        public PreviewItem(string label, GameObject prefab, Material material)
        { this.label = label; mesh = prefab == null ? null : prefab.GetComponent<MeshFilter>().sharedMesh; this.material = material; }
    }
    [MenuItem("Fantasy Kingdom/開発スタジオ/見た目/本番の白・黒・赤の見本を作成")]
    public static void RenderPreview()
    {
        ValidateAssets(); var catalog = AssetDatabase.LoadAssetAtPath<BoardVisualThemeCatalog>(CatalogPath);
        var players = new List<PreviewItem>(); var enemies = new List<PreviewItem>(); var bosses = new List<PreviewItem>();
        foreach (var kind in DarkEnemyVisualInstaller.EnemyKinds)
        {
            var style = catalog.Unit(kind);
            var player = style.playerVisual;
            if (kind == Kind.King && player == null) player = FindInitialKingPrefab();
            players.Add(new PreviewItem(kind.ToString(), player, kind == Kind.King ? catalog.playerPlainMaterial : catalog.playerUnitMaterial));
            enemies.Add(new PreviewItem(kind.ToString(), style.enemyVisual, catalog.enemyUnitMaterial));
        }
        foreach (var boss in DarkEnemyVisualInstaller.Bosses)
            bosses.Add(new PreviewItem(boss.ToString(), catalog.Boss(boss).visual, catalog.bossMaterial));
        RenderSheet("PlayerWhiteUnits.png", players, 4, .91f);
        RenderSheet("EnemyBlackUnits.png", enemies, 4, .91f);
        RenderSheet("CrimsonBosses.png", bosses, 4, .98f);
        var buildings = new List<PreviewItem>();
        foreach (FacilityKind kind in Enum.GetValues(typeof(FacilityKind)))
        {
            if (kind == FacilityKind.SubCrystal)
            {
                var original = AssetDatabase.LoadAssetAtPath<GameObject>(BuildingVisualGenerator.PrefabPath(kind));
                buildings.Add(new PreviewItem("Original / SubCrystal", original, original.GetComponent<MeshRenderer>().sharedMaterial));
                continue;
            }
            var style = catalog.Building(kind);
            buildings.Add(new PreviewItem("White / " + kind, style.playerVisual, catalog.playerBuildingMaterial));
            buildings.Add(new PreviewItem("Black / " + kind, style.enemyVisual, catalog.enemyBuildingMaterial));
        }
        RenderSheet("FactionBuildings.png", buildings, 6, 1.03f);
    }
    static GameObject FindInitialKingPrefab()
    {
        var systems = AssetDatabase.LoadAssetAtPath<GameObject>(BuildingVisualGenerator.SystemPrefab);
        var setting = systems == null ? null : systems.GetComponentInChildren<UnitSetting>(true);
        if (setting == null) return null;
        return new SerializedObject(setting).FindProperty("KingPiece")?.objectReferenceValue as GameObject;
    }
    static void RenderSheet(string name, List<PreviewItem> items, int columns, float size)
    {
        const int width = 340, height = 380; int rows = (items.Count + columns - 1) / columns;
        EnsureFolder(Folder + "/Review"); var scene = EditorSceneManager.NewPreviewScene();
        var atlas = new Texture2D(width * columns, height * rows, TextureFormat.RGB24, false);
        var background = new Color32(36, 42, 52, 255);
        var emptyPixels = new Color32[atlas.width * atlas.height];
        for (int i = 0; i < emptyPixels.Length; i++) emptyPixels[i] = background;
        atlas.SetPixels32(emptyPixels);
        var readback = new Texture2D(width, height, TextureFormat.RGB24, false);
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active; var ambient = RenderSettings.ambientLight; var ambientMode = RenderSettings.ambientMode;
        Camera camera = null;
        try
        {
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.58f, .63f, .70f);
            var co = new GameObject("陣営別見本カメラ"); SceneManager.MoveGameObjectToScene(co, scene);
            camera = co.AddComponent<Camera>(); camera.scene = scene; camera.orthographic = true; camera.orthographicSize = size;
            camera.nearClipPlane = .01f; camera.farClipPlane = 20; camera.targetTexture = target; camera.allowHDR = false;
            camera.transform.position = new Vector3(1.5f, 1.20f, -2.6f); camera.transform.LookAt(new Vector3(0, .10f, 0));
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = background;
            AddPreviewLight(scene, "見本主照明", new Color(1, .91f, .8f), 1.5f, new Vector3(38, -32, 0));
            AddPreviewLight(scene, "見本輪郭照明", new Color(.60f, .76f, 1), .75f, new Vector3(25, 145, 0));
            var labelObject = new GameObject("見本職種名"); SceneManager.MoveGameObjectToScene(labelObject, scene);
            labelObject.transform.rotation = camera.transform.rotation;
            labelObject.transform.position = camera.transform.position + camera.transform.forward * 2 + camera.transform.up * (-size + .10f);
            var label = labelObject.AddComponent<TextMesh>(); label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 36; label.characterSize = .037f; label.anchor = TextAnchor.MiddleCenter; label.color = new Color32(224, 201, 150, 255);
            labelObject.GetComponent<MeshRenderer>().sharedMaterial = label.font.material;
            for (int i = 0; i < items.Count; i++)
            {
                var model = new GameObject(items[i].label); SceneManager.MoveGameObjectToScene(model, scene);
                try
                {
                    model.AddComponent<MeshFilter>().sharedMesh = items[i].mesh;
                    model.AddComponent<MeshRenderer>().sharedMaterial = items[i].material; label.text = items[i].label;
                    if (items[i].mesh != null)
                    {
                        // Imported kings can have a large mesh offset compensated by their
                        // gameplay prefab. Fit the review copy without editing that prefab.
                        var bounds = items[i].mesh.bounds;
                        float extent = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                        float scale = extent > 1.4f ? 1.4f / extent : 1f;
                        model.transform.localScale = Vector3.one * scale;
                        model.transform.position = new Vector3(0, .10f, 0) - bounds.center * scale;
                    }
                    camera.Render(); RenderTexture.active = target; readback.ReadPixels(new Rect(0, 0, width, height), 0, 0); readback.Apply();
                    atlas.SetPixels((i % columns) * width, (rows - 1 - i / columns) * height, width, height, readback.GetPixels());
                }
                finally { UnityEngine.Object.DestroyImmediate(model); }
            }
            atlas.Apply(); File.WriteAllBytes(Folder + "/Review/" + name, atlas.EncodeToPNG()); AssetDatabase.ImportAsset(Folder + "/Review/" + name);
        }
        finally
        {
            RenderTexture.active = previous; RenderSettings.ambientLight = ambient; RenderSettings.ambientMode = ambientMode;
            if (camera != null) camera.targetTexture = null;
            target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(readback); UnityEngine.Object.DestroyImmediate(atlas);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
    static void AddPreviewLight(Scene scene, string name, Color color, float intensity, Vector3 rotation)
    {
        var lo = new GameObject(name); SceneManager.MoveGameObjectToScene(lo, scene);
        var light = lo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = intensity;
        light.color = color; light.transform.rotation = Quaternion.Euler(rotation); light.shadows = LightShadows.None;
    }
}
#endif
