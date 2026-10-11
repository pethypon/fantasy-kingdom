#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AIPerformanceConfig))]
public sealed class AIPerformanceConfigEditor : Editor
{
    [MenuItem("Fantasy Kingdom/パフォーマンス/長時間AIの設定")]
    static void Open()
    {
        const string path = "Assets/Resources/AI/PerformanceConfig.asset";
        var asset = AssetDatabase.LoadAssetAtPath<AIPerformanceConfig>(path);
        if (asset == null)
        {
            asset = CreateInstance<AIPerformanceConfig>(); AssetDatabase.CreateAsset(asset, path); AssetDatabase.SaveAssets();
        }
        Selection.activeObject = asset; EditorGUIUtility.PingObject(asset);
    }
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.HelpBox("思考の深さ・候補数・学習の強さは変更しません。数値はPERF_TODOとして計測結果に合わせて調整してください。", MessageType.Info);
        Draw("EnableInstrumentation", "開発時に計測する"); Draw("MaintenanceIntervalOwnTurns", "履歴の掃除間隔（自軍ターン）");
        Draw("PresentationMaxQueue", "表示待ちの最大件数"); Draw("PresentationHighWaterMark", "追いつきモード開始件数");
        Draw("PresentationLowWaterMark", "通常表示に戻る件数"); Draw("CatchUpAnimationMultiplier", "混雑時の演出時間倍率");
        Draw("MaxVisibleDiaryEvents", "日記1ページの最大行動数"); Draw("MaxDiaryVisibleCharacters", "日記1ページの最大文字数");
        Draw("DiaryRefreshHz", "日記の最大更新回数／秒"); Draw("LogLevel", "ログ詳細度（Off／Error／Summary／Verbose／Trace）");
        Draw("MaximumLateTurnTimeRatio", "耐久試験の終盤／序盤時間の上限比"); Draw("RetainedTurnSamples", "計測結果の保持ターン数");
        serializedObject.ApplyModifiedProperties();
        EditorGUILayout.HelpBox("隠し日記：Shift＋F7　見下ろし：Ctrl＋Space。もう一度押すと閉じる／元の視点へ戻ります。", MessageType.None);
    }
    void Draw(string name, string label) => EditorGUILayout.PropertyField(serializedObject.FindProperty(name), new GUIContent(label), true);
}
#endif
