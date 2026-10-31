#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>Explicit configuration commands only; importing these scripts never writes assets.</summary>
public static class AIIntegratedConfigMenu
{
    [MenuItem("Fantasy Kingdom/AI/R1 部隊編成の設定")]
    public static void OpenSquadConfiguration()
    {
        if (!CanWriteConfiguration()) return;
        const string path = "Assets/Resources/" + AIOperationSquadConfig.ResourcePath + ".asset";
        var config = AssetDatabase.LoadAssetAtPath<AIOperationSquadConfig>(path);
        if (config == null)
        {
            if (HasDifferentExistingAsset(path)) return;
            EnsureFolder();
            config = ScriptableObject.CreateInstance<AIOperationSquadConfig>();
            config.Validate();
            AssetDatabase.CreateAsset(config, path);
            AssetDatabase.SaveAssetIfDirty(config);
        }
        Selection.activeObject = config; EditorGUIUtility.PingObject(config);
    }

    [MenuItem("Fantasy Kingdom/AI/R1 戦況別の思考時間")]
    public static void OpenThinkingConfiguration()
    {
        if (!CanWriteConfiguration()) return;
        const string path = "Assets/Resources/AI/ThinkingBudgetConfig.asset";
        var config = AssetDatabase.LoadAssetAtPath<AIThinkingBudgetConfig>(path);
        if (config == null)
        {
            if (HasDifferentExistingAsset(path)) return;
            EnsureFolder();
            config = ScriptableObject.CreateInstance<AIThinkingBudgetConfig>();
            AssetDatabase.CreateAsset(config, path);
            AssetDatabase.SaveAssetIfDirty(config);
        }
        // Existing policy values are selected as-is. Opening a setting does not
        // replace a user's tuning or save any other dirty resource/font/prefab.
        Selection.activeObject = config; EditorGUIUtility.PingObject(config);
    }

    static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder("Assets/Resources/AI")) AssetDatabase.CreateFolder("Assets/Resources", "AI");
    }

    static bool HasDifferentExistingAsset(string path)
    {
        var existing = AssetDatabase.LoadMainAssetAtPath(path);
        string absolute = System.IO.Path.Combine(Application.dataPath, path.Substring("Assets/".Length));
        if (existing == null && !System.IO.File.Exists(absolute)) return false;
        Debug.LogWarning("[AI設定] 同じ場所に別のデータがあります。上書きせず処理を中止しました: " + path);
        if (existing != null) { Selection.activeObject = existing; EditorGUIUtility.PingObject(existing); }
        return true;
    }
    [MenuItem("Fantasy Kingdom/AI/R1 部隊編成の設定", true)]
    static bool CanOpenSquadConfiguration() => CanWriteConfiguration();
    [MenuItem("Fantasy Kingdom/AI/R1 戦況別の思考時間", true)]
    static bool CanOpenThinkingConfiguration() => CanWriteConfiguration();
    static bool CanWriteConfiguration() => !EditorApplication.isPlayingOrWillChangePlaymode
        && !EditorApplication.isCompiling && !EditorApplication.isUpdating;
}
#endif
