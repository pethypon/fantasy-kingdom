#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class GameUIThemeEditor
{
    [MenuItem("Fantasy Kingdom/UI/テーマ設定を選択")]
    public static void SelectTheme()
    {
        var theme = AssetDatabase.LoadAssetAtPath<GameUITheme>("Assets/Resources/UI/SteampunkUITheme.asset");
        if (theme == null) { Debug.LogWarning("SteampunkUITheme.asset が見つかりません。"); return; }
        Selection.activeObject = theme;
        EditorGUIUtility.PingObject(theme);
    }
}
#endif
