#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

/// <summary>Updates official crystal prefabs/stock sub recipe only; scenes, art and dirty assets are untouched.</summary>
public sealed class MainCrystalRulePrefabMigration : IPreprocessBuildWithReport
{
    public int callbackOrder => -1100;
    public void OnPreprocessBuild(BuildReport report) => CrystalPrefabEditor.ApplyMainCrystalRules();
    [InitializeOnLoadMethod]
    static void Schedule()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            CrystalPrefabEditor.ApplyMainCrystalRules();
        };
    }
}
#endif
