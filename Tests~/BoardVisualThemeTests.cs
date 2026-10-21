#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

/// <summary>Editor regression for the presentation-only importer. Run after all visual bakes.</summary>
public static class BoardVisualThemeTests
{
    static int checks;
    static void Check(bool pass, string message) { checks++; if (!pass) throw new Exception("[BoardVisualThemeTests] " + message); }
    public static void Run()
    {
        checks = 0; BoardVisualThemeInstaller.ValidateAssets();
        var catalog = AssetDatabase.LoadAssetAtPath<BoardVisualThemeCatalog>(BoardVisualThemeInstaller.CatalogPath);
        Check(catalog != null, "catalog exists");
        Check(catalog.units.Count == DarkEnemyVisualInstaller.EnemyKinds.Length
            && catalog.buildings.Count == Enum.GetValues(typeof(FacilityKind)).Length
            && catalog.bosses.Count == DarkEnemyVisualInstaller.Bosses.Length,
            "all current stock unit, building and boss styles");
        Check(AssetDatabase.FindAssets("t:Material", new[] { BoardVisualThemeInstaller.Folder }).Length == 0, "no new materials generated");
        Check(catalog.Unit(Kind.Boss) == catalog.Unit(Kind.King), "enemy commander uses King presentation alias");
        Check(catalog.Unit(Kind.King).playerVisual == null, "initial player King silhouette is retained");
        Check(!catalog.ManagesDefinition("test.author.unknown.unit"), "custom definitions remain authoritative");
        var unknown = new Mesh();
        try { Check(!catalog.ManagesMesh(unknown), "custom mesh remains authoritative"); }
        finally { UnityEngine.Object.DestroyImmediate(unknown); }
        foreach (var kind in R1UnitVisualInstaller.Kinds)
        {
            var data = AssetDatabase.LoadAssetAtPath<UnitData>(R1UnitVisualInstaller.DataPath(kind));
            Check(data != null && catalog.ManagesDefinition(data.definitionId), "standard unit definition: " + kind);
            var style = catalog.Unit(kind);
            Check(style.playerVisual.GetComponent<MeshFilter>().sharedMesh != style.enemyVisual.GetComponent<MeshFilter>().sharedMesh,
                "enemy uses a distinct creature silhouette: " + kind);
        }
        foreach (FacilityKind kind in Enum.GetValues(typeof(FacilityKind)))
        {
            var style = catalog.Building(kind);
            Check(style.playerVisual.GetComponent<MeshFilter>().sharedMesh != style.enemyVisual.GetComponent<MeshFilter>().sharedMesh,
                "enemy building has its own silhouette: " + kind);
            var data = AssetDatabase.LoadAssetAtPath<FacilityDefinitionData>(BuildingVisualGenerator.LegacyFolder + "/" + kind + "/Building.asset");
            Check(data == null || catalog.ManagesDefinition(data.definitionId), "standard building definition: " + kind);
        }
        foreach (string dependency in AssetDatabase.GetDependencies(BoardVisualThemeInstaller.CatalogPath, true))
            Check(!dependency.StartsWith(DarkEnemyVisualInstaller.Folder + "/Sources/", StringComparison.Ordinal),
                "catalog does not ship source importer data: " + dependency);
        var protectedFiles = new Dictionary<string, string>();
        foreach (string path in new[] {
            BoardVisualThemeInstaller.ExistingWhiteMaterialPath, BoardVisualThemeInstaller.ExistingBlackMaterialPath, BoardVisualThemeInstaller.ExistingRedMaterialPath,
            R1UnitVisualInstaller.MaterialPath, BuildingVisualGenerator.ArtFolder + "/BuildingPalette.mat", BuildingVisualGenerator.EnemyMaterialPath,
            "Assets/Resources/GameContent/UnitCatalog.asset", "Assets/Resources/GameContent/FacilityCatalog.asset" })
            if (File.Exists(path)) protectedFiles[path] = Sha256(path);
        bool enabled = catalog.enabledForPlay;
        try
        {
            // An intentional disabled state and model choices must survive a repeat install.
            catalog.enabledForPlay = false;
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
            string before = EditorJsonUtility.ToJson(catalog);
            BoardVisualThemeInstaller.Install();
            Check(before == EditorJsonUtility.ToJson(catalog), "repeat import preserves author-controlled catalog settings");
            Check(AssetDatabase.FindAssets("t:Material", new[] { BoardVisualThemeInstaller.Folder }).Length == 0, "repeat import creates no materials");
        }
        finally
        {
            catalog.enabledForPlay = enabled;
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
        }
        foreach (var pair in protectedFiles) Check(pair.Value == Sha256(pair.Key), "source asset unchanged: " + pair.Key);
        BoardVisualThemeInstaller.LoadFactionMaterials(out var white, out var black, out var red);
        Check(catalog.playerUnitMaterial == white && catalog.playerBuildingMaterial == white && catalog.playerPlainMaterial == white, "exact existing white material reference");
        Check(catalog.enemyUnitMaterial == black && catalog.enemyBuildingMaterial == black && catalog.enemyPlainMaterial == black, "exact existing black material reference");
        Check(catalog.bossMaterial == red && catalog.bossPlainMaterial == red, "exact existing red material reference");
        Check(!ShaderUtil.ShaderHasError(white.shader) && !ShaderUtil.ShaderHasError(black.shader) && !ShaderUtil.ShaderHasError(red.shader), "existing marble shaders compile");
        Debug.Log("[BoardVisualThemeTests] ALL PASSED checks=" + checks);
    }
    static string Sha256(string path)
    {
        using (var hash = SHA256.Create()) using (var stream = File.OpenRead(path))
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty);
    }
}
#endif

