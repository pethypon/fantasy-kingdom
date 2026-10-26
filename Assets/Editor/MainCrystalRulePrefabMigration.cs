#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

/// <summary>Only the two main-crystal prefabs are touched. No scene, art, or other dirty asset is saved.</summary>
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
