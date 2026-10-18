#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEditor;
public static class DarkMarbleVisualBakeValidation
{
    public static void Run()
    {
        if (!Application.dataPath.Contains("UnityValidation")) throw new Exception("Isolated clone only");
        BoardVisualThemeInstaller.UseExistingFactionMaterials();
        DarkEnemyVisualInstaller.RebuildForValidation();
        BoardVisualThemeInstaller.Install();
        BoardVisualThemeInstaller.DiscardUnpublishedMaterialsForValidation();
        BoardVisualThemeInstaller.RenderPreview();
        BoardVisualThemeTests.Run();
        Debug.Log("[DarkMarbleVisualBakeValidation] ALL PASSED");
    }
}
#endif
