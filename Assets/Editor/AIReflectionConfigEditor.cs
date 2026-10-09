#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AIReflectionConfig))]
public sealed class AIReflectionConfigEditor : Editor
{
    const string AssetPath = "Assets/Resources/AI/ReflectionConfig.asset";
    [MenuItem("Fantasy Kingdom/AI/自己評価・日記の設定", priority = 15)]
    public static void OpenSettings()
    {
        var asset = AssetDatabase.LoadAssetAtPath<AIReflectionConfig>(AssetPath);
        if (asset == null)
        {
            UnitWorkshopWindow.EnsureFolder("Assets/Resources/AI");
            asset = CreateInstance<AIReflectionConfig>();
            AssetDatabase.CreateAsset(asset, AssetPath);
            AssetDatabase.SaveAssets();
        }
        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
    }
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.LabelField("AIの自己評価・学習・日記", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("既存AIの判断を、過去の実際の成果から小さく補正します。王の生存・緊急防衛の優先順位とFogの制限は維持します。開発用プレイヤーAIは、通常の学習データに保存しません。", MessageType.Info);
        Field("EnableReflection", "自己評価と記録を有効にする");
        Field("ApplyLearningToSelection", "学習結果を行動の選択にも使う");
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("AI日記", EditorStyles.boldLabel);
        Field("DiaryIntervalOwnTurns", "自軍の何ターンごとに書くか");
        Field("EnableFileDiary", "日記をテキストファイルに保存する");
        Field("EnableConsoleDiary", "開発時に日記の要約をConsoleへ表示する");
        Field("EnableCandidateDebug", "候補の評価を詳しくログに出す");
        EditorGUILayout.HelpBox("標準は自軍15ターンごとと勝敗確定時です。ゲームのメニューから「AI日記を見る」で最新の日記を読めます。詳しい候補ログは必要な時だけ有効にしてください。", MessageType.Info);
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("学習の強さと保存量", EditorStyles.boldLabel);
        Field("ReflectionLearningRate", "学習率"); Field("MinimumLearningRate", "学習率の下限");
        Field("MinLearnedValue", "評価補正の下限"); Field("MaxLearnedValue", "評価補正の上限");
        Field("LearningInfluenceMultiplier", "選択への影響倍率");
        Field("MaxKnowledgeEntries", "保持する学習パターンの最大数");
        Field("MaxRecentRecords", "保持する最近の行動記録数");
        Field("MaxRewardEvents", "１戦で保持する報酬の重複防止記録数");
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("失敗の評価（調整対象）", EditorStyles.boldLabel);
        Field("RepeatedNoProgressPenalty", "進展のない同一行動の減点");
        Field("OscillationPenalty", "意味のない往復移動の減点");
        Field("FailedActionPenalty", "行動実行失敗の減点"); Field("DefeatReward", "敗北時の戦闘報酬（標準０）");
        EditorGUILayout.HelpBox("固定報酬：通常撃破＋1、明示された陣形撃破＋2、アーティファクト獲得＋1、経済安定＋1（自軍１ターンに１回）、勝利＋100（戦闘全体のみ）。減点の値はBALANCE_TODOです。", MessageType.Info);
        serializedObject.ApplyModifiedProperties();
    }
    void Field(string name, string label)
        => EditorGUILayout.PropertyField(serializedObject.FindProperty(name), new GUIContent(label));
}
#endif
