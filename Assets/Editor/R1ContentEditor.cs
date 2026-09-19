#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class R1ContentEditor
{
    [MenuItem("Fantasy Kingdom/Edit R1 Content")]
    public static void Open()
    {
        const string folder = "Assets/Resources";
        const string path = folder + "/R1ContentCatalog.asset";
        var catalog = AssetDatabase.LoadAssetAtPath<R1ContentCatalog>(path);
        if (catalog == null)
        {
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "Resources");
            catalog = ScriptableObject.CreateInstance<R1ContentCatalog>();
            AssetDatabase.CreateAsset(catalog, path);
            AssetDatabase.SaveAssets();
        }
        Selection.activeObject = catalog;
        EditorGUIUtility.PingObject(catalog);
    }
}
#endif
