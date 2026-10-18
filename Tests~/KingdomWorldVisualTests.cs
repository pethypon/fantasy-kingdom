#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Editor validation for a caller-owned, isolated Unity project.</summary>
public static class KingdomWorldVisualTests
{
    public static void Run()
    {
        int checks = 0;
        BuildingVisualGenerator.ValidateKingdomArt();
        TerrainEnvironmentProductionInstaller.ValidateAssets();
        var standardMeshes = new HashSet<Mesh>(); var darkMeshes = new HashSet<Mesh>();
        foreach (FacilityKind kind in Enum.GetValues(typeof(FacilityKind)))
        {
            var white = AssetDatabase.LoadAssetAtPath<Mesh>(BuildingVisualGenerator.MeshPath(kind));
            var black = AssetDatabase.LoadAssetAtPath<Mesh>(BuildingVisualGenerator.DarkMeshPath(kind));
            Require(white != null && black != null && white != black, "distinct faction meshes: " + kind, ref checks);
            Require(black.vertexCount > white.vertexCount, "dark finials are baked into the same mesh: " + kind, ref checks);
            standardMeshes.Add(white); darkMeshes.Add(black);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BuildingVisualGenerator.PrefabPath(kind));
            if (prefab != null && prefab.GetComponent<MeshFilter>()?.sharedMesh == white)
                Require(prefab.GetComponentsInChildren<Renderer>(true).Length == 1 && prefab.GetComponentsInChildren<Collider>(true).Length == 1,
                    "stock building component budget: " + kind, ref checks);
            var authored = AssetDatabase.LoadAssetAtPath<GameObject>(BuildingVisualGenerator.LegacyFolder + "/" + kind + "/Visual.prefab");
            if (authored != null) Require(authored.GetComponent<Status>() != null, "authoring Status retained: " + kind, ref checks);
        }
        Require(standardMeshes.Count == 18 && darkMeshes.Count == 18, "all 18 function-specific silhouettes retained", ref checks);
        foreach (TerrainEnvironmentKind kind in Enum.GetValues(typeof(TerrainEnvironmentKind)))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TerrainEnvironmentProductionInstaller.PrefabPath(kind));
            Require(prefab != null && prefab.GetComponentsInChildren<MonoBehaviour>(true).Length == 0, "no environment Update components: " + kind, ref checks);
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(TerrainEnvironmentProductionInstaller.MeshPath(kind));
            Require(mesh != null && mesh.triangles.Length / 3 <= 900, "environment geometry budget: " + kind, ref checks);
            if (kind != TerrainEnvironmentKind.Forest && kind != TerrainEnvironmentKind.Bush && kind != TerrainEnvironmentKind.Rock) continue;
            foreach (var vertex in mesh.vertices)
                Require(vertex.y < -.32f || vertex.x * vertex.x + vertex.z * vertex.z >= .16f * .16f,
                    "central piece footprint remains open: " + kind, ref checks);
        }
        string texturePath = TerrainEnvironmentProductionInstaller.Folder + "/EnvironmentPalette.png";
        var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
        Require(importer != null && !importer.isReadable && !importer.mipmapEnabled, "shared environment texture has no retained CPU copy", ref checks);
        Debug.Log("[KingdomWorldVisual] ALL PASSED checks=" + checks);
    }

    public static void InstallAndReview()
    {
        try
        {
            var snapshots = new Dictionary<UnityEngine.Object, string>();
            foreach (FacilityKind kind in Enum.GetValues(typeof(FacilityKind)))
            {
                string path = BuildingVisualGenerator.LegacyFolder + "/" + kind + "/Visual.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path); var status = prefab == null ? null : prefab.GetComponent<Status>();
                if (status != null) snapshots[status] = EditorJsonUtility.ToJson(status);
            }
            var catalog = AssetDatabase.LoadAssetAtPath<TerrainEnvironmentCatalog>(TerrainEnvironmentProductionInstaller.CatalogPath);
            string catalogBefore = catalog == null ? null : EditorJsonUtility.ToJson(catalog);
            if (catalog != null)
                foreach (var definition in catalog.definitions)
                    if (definition != null) snapshots[definition] = EditorJsonUtility.ToJson(definition);
            BuildingVisualGenerator.InstallKingdomArt();
            TerrainEnvironmentProductionInstaller.Install(); TerrainEnvironmentProductionInstaller.RenderPreview();
            foreach (var snapshot in snapshots)
            {
                UnityEngine.Object asset = snapshot.Key;
                if (asset == null || EditorJsonUtility.ToJson(asset) != snapshot.Value)
                    throw new InvalidOperationException("Visual bake changed authored gameplay data: " + snapshot.Key);
            }
            if (catalog != null && EditorJsonUtility.ToJson(catalog) != catalogBefore)
                throw new InvalidOperationException("Visual bake changed environment generation configuration.");
            Run();
            Debug.Log("[KingdomWorldVisual] REVIEW PASSED, gameplay definitions unchanged.");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    static void Require(bool condition, string message, ref int checks)
    {
        checks++; if (!condition) throw new InvalidOperationException("[KingdomWorldVisual] " + message);
    }
}
#endif
