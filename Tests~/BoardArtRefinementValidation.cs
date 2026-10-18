#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

/// <summary>Explicit rebake in the isolated clone, with original crystal assets protected.</summary>
public static class BoardArtRefinementValidation
{
    public static void Run()
    {
        if (new DirectoryInfo(Application.dataPath).Parent.Name != "UnityValidation")
            throw new InvalidOperationException("Isolated UnityValidation project required");
        var crystalAssets = new Dictionary<string, string>();
        foreach (string path in new[] {
            BuildingVisualGenerator.MeshPath(FacilityKind.SubCrystal), BuildingVisualGenerator.PrefabPath(FacilityKind.SubCrystal),
            BuildingVisualGenerator.DarkMeshPath(FacilityKind.SubCrystal), BuildingVisualGenerator.DarkPrefabPath(FacilityKind.SubCrystal),
            BoardVisualThemeInstaller.ExistingWhiteMaterialPath, BoardVisualThemeInstaller.ExistingBlackMaterialPath,
            BoardVisualThemeInstaller.ExistingRedMaterialPath })
            crystalAssets[path] = Sha(path);
        var system = AssetDatabase.LoadAssetAtPath<GameObject>(BuildingVisualGenerator.SystemPrefab);
        var crystals = system.GetComponentInChildren<CrystalSystem>(true);
        var serialized = new SerializedObject(crystals);
        foreach (var name in new[] { "PlayerCrystal", "EnemyCrystal" })
        {
            var prefab = serialized.FindProperty(name).objectReferenceValue;
            foreach (var dependency in AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(prefab), true))
                if (File.Exists(dependency)) crystalAssets[dependency] = Sha(dependency);
        }
        R1UnitVisualInstaller.RebuildForValidation();
        BuildingVisualGenerator.InstallKingdomArt();
        DarkEnemyVisualInstaller.RebuildForValidation();
        BoardVisualThemeInstaller.ValidateAssets();
        BoardVisualThemeInstaller.RenderPreview();
        foreach (var pair in crystalAssets)
            if (Sha(pair.Key) != pair.Value) throw new InvalidOperationException("Original crystal asset changed: " + pair.Key);
        KingdomWorldVisualTests.Run();
        BoardVisualThemeTests.Run();
        R1UnitVisualTests.Run();
        Debug.Log("[BoardArtRefinement] ALL PASSED originalCrystalAssets=" + crystalAssets.Count);
    }
    static string Sha(string path)
    {
        using (var hash = SHA256.Create()) using (var stream = File.OpenRead(path))
            return BitConverter.ToString(hash.ComputeHash(stream));
    }
}
#endif
