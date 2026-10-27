#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>Creates only the optional AI configuration. Never edits a scene, prefab or map.</summary>
public static class AICognitiveConfigMenu
{
    [MenuItem("Fantasy Kingdom/AI/R4 自律戦略の設定")]
    public static void OpenConfiguration()
    {
        const string path = "Assets/Resources/AI/CognitiveAIConfig.asset";
        var config = AssetDatabase.LoadAssetAtPath<AICognitiveAIConfig>(path);
        if (config == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder("Assets/Resources/AI")) AssetDatabase.CreateFolder("Assets/Resources", "AI");
            config = ScriptableObject.CreateInstance<AICognitiveAIConfig>();
            config.Validate(); AssetDatabase.CreateAsset(config, path); AssetDatabase.SaveAssetIfDirty(config);
        }
        Selection.activeObject = config; EditorGUIUtility.PingObject(config);
    }
}
#endif
