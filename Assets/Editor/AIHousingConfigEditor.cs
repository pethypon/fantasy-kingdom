#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AIHousingConfig))]
public sealed class AIHousingConfigEditor : Editor
{
    [MenuItem("Fantasy Kingdom/AI/住宅投資の設定")]
    static void Open()
    {
        const string path = "Assets/Resources/AI/HousingConfig.asset";
        var config = AssetDatabase.LoadAssetAtPath<AIHousingConfig>(path);
        if (config == null) { config = CreateInstance<AIHousingConfig>(); AssetDatabase.CreateAsset(config, path); AssetDatabase.SaveAssets(); }
        Selection.activeObject = config; EditorGUIUtility.PingObject(config);
    }
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.HelpBox("市民は召喚・建築に使う空き資源です。住宅は上限を増やしますが、毎ターンの補充速度は増やしません。容量が将来の支出を妨げる時だけ投資します。建築費・容量増加は建物の正式データを参照します。", MessageType.Info);
        Draw("Enabled", "住宅の投資判断を有効にする");
        Draw("ConsiderCitizenRatio", "使用済み枠の検討率"); Draw("UrgentCitizenRatio", "使用済み枠の緊急率");
        Draw("ForecastTurns", "建築後の経済予測ターン数"); Draw("MinimumFreeCitizenTarget", "計画実行後に残したい空き市民");
        Draw("ShortTermCombatRecruits", "接敵後の短期軍備候補数"); Draw("EmergencyRecruitWaitTurns", "防衛用補充を待てるターン数");
        Draw("PopulationNeedWeight", "現在の市民圧迫の評価"); Draw("FuturePopulationNeedWeight", "将来の容量不足の評価");
        Draw("EconomicStabilityWeight", "経済が安定している時の評価"); Draw("MilitaryExpansionWeight", "軍備・作戦需要の評価");
        Draw("ResourceRiskPenalty", "建築後の資源余裕不足の減点"); Draw("EmergencyPenalty", "直接防衛を妨げる時の減点");
        Draw("ExcessHousingPenalty", "不要な住宅の減点"); Draw("RepeatedHousePenalty", "過剰な容量増加の減点");
        Draw("EnableDecisionLogs", "採用・却下理由を開発ログに出す");
        serializedObject.ApplyModifiedProperties();
        EditorGUILayout.HelpBox("住宅ログは長時間AI設定のログレベルをVerbose以上にした時だけ生成します。王の退避・直接防衛・経済破綻回避の既存優先順位は維持します。", MessageType.None);
    }
    void Draw(string property, string label) => EditorGUILayout.PropertyField(serializedObject.FindProperty(property), new GUIContent(label), true);
}
#endif
