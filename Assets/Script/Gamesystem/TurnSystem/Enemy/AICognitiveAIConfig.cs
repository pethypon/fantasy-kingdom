using UnityEngine;

/// <summary>R4 policy only. No game rules or additional thinking time are configured here.</summary>
[CreateAssetMenu(menuName = "Fantasy Kingdom/AI/R4 自律戦略", fileName = "CognitiveAIConfig")]
public sealed class AICognitiveAIConfig : ScriptableObject
{
    public const int Version = 1;
    [Header("R4は未検証です。検証完了まで実行への反映は無効にしてください")]
    [InspectorName("R4を使用")] public bool Enabled;
    [InspectorName("記録のみ（既存AIの行動を変更しない）")] public bool ShadowMode = true;
    [InspectorName("観測世界モデル")] public bool EnableWorldModel = true;
    [InspectorName("問題と原因仮説")] public bool EnableHypothesis = true;
    [InspectorName("戦略知識")] public bool EnableKnowledge = true;
    [InspectorName("複数案の計画")] public bool EnableAutonomousPlanning = true;
    [InspectorName("計画を行動選択へ反映")] public bool EnablePlanExecutionHints;
    [InspectorName("部隊の複数手順")] public bool EnableMultiStepSquadPlanning;
    [InspectorName("試合をまたぐ経験を利用")] public bool EnableGeneralizedExperience;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [InspectorName("判断の記録")] public bool EnableDecisionDiagnostics = true;
#else
    [InspectorName("判断の記録")] public bool EnableDecisionDiagnostics;
#endif
    [Header("上限と試作値（ゲームバランスの確定値ではありません）")]
    [InspectorName("知識検索の上限")] public int MaxKnowledgeMatches = 12;
    [InspectorName("目標ごとの案の上限")] public int MaxPlansPerGoal = 6;
    [InspectorName("全計画の上限")] public int MaxPlansTotal = 8;
    [InspectorName("手順の上限")] public int MaxOperatorsPerPlan = 6;
    [InspectorName("相手応答の上限")] public int MaxEnemyResponses = 3;
    [InspectorName("探索の幅")] public int BeamWidth = 6;
    [InspectorName("計画の切替差")] public float SwitchMargin = .18f;
    [InspectorName("経験を使う最少件数")] public int MinMeaningfulSamples = 2;
    [InspectorName("経験の上限")] public int MaxCrossMatchEntries = 512;
    [InspectorName("判断記録の上限")] public int MaxTraceEntries = 128;
    public bool MayInfluenceActions => Enabled && EnableWorldModel && EnableAutonomousPlanning
        && EnablePlanExecutionHints && !ShadowMode;

    static AICognitiveAIConfig active;
    static bool ownsActive;
    public static AICognitiveAIConfig Active
    {
        get
        {
            if (active != null) return active;
            active = Resources.Load<AICognitiveAIConfig>("AI/CognitiveAIConfig");
            if (active == null)
            { active = CreateInstance<AICognitiveAIConfig>(); active.hideFlags = HideFlags.HideAndDontSave; ownsActive = true; }
            active.Validate(); return active;
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntime()
    {
        if (ownsActive && active != null) DestroyImmediate(active);
        active = null; ownsActive = false;
    }
    void OnValidate() => Validate();
    public void Validate()
    {
        MaxKnowledgeMatches = Mathf.Clamp(MaxKnowledgeMatches, 1, 12);
        MaxPlansPerGoal = Mathf.Clamp(MaxPlansPerGoal, 1, 6);
        MaxPlansTotal = Mathf.Clamp(MaxPlansTotal, 1, 8);
        MaxOperatorsPerPlan = Mathf.Clamp(MaxOperatorsPerPlan, 1, 6);
        MaxEnemyResponses = Mathf.Clamp(MaxEnemyResponses, 1, 3);
        BeamWidth = Mathf.Clamp(BeamWidth, 1, 6);
        SwitchMargin = Mathf.Clamp(float.IsNaN(SwitchMargin) || float.IsInfinity(SwitchMargin) ? .18f : SwitchMargin, 0, 1);
        MinMeaningfulSamples = Mathf.Clamp(MinMeaningfulSamples, 2, 100);
        MaxCrossMatchEntries = Mathf.Clamp(MaxCrossMatchEntries, 1, 512);
        MaxTraceEntries = Mathf.Clamp(MaxTraceEntries, 1, 128);
    }
}
