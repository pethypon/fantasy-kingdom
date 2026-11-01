using UnityEngine;

[CreateAssetMenu(menuName = "Fantasy Kingdom/AI/作戦部隊設定", fileName = "OperationSquadConfig")]
public sealed class AIOperationSquadConfig : ScriptableObject
{
    public const string ResourcePath = "AI/OperationSquadConfig";
    [InspectorName("部隊編成を有効化")] public bool Enabled = true;
    [Header("AI運用の提案初期値（ゲームルールを変更しない）")]
    [Range(3, 5), InspectorName("標準部隊人数")] public int StandardSize = 4;
    [Range(1, 16), InspectorName("保存する最大部隊数")] public int MaximumSquads = 16;
    [Range(2, 8), InspectorName("連携距離の目安")] public int CohesionRadius = 4;
    [Range(1, 5), InspectorName("集合を待つ自軍ターン数")] public int AssemblyGraceTurns = 2;
    [Range(2, 5), InspectorName("連携の最大行動数")] public int MaximumChainActions = 5;
    [Range(1, 8), InspectorName("連携を比較する最大目標数")] public int MaximumChainTargets = 8;
    [Range(1, 250), InspectorName("勝利連携の1行動に確保する猶予ms")] public int MinimumChainStepBudgetMs = 50;
    [Range(0f, 3f), InspectorName("同一優先度内の部隊補正上限")] public float MaximumScoreBonus = 2;
    static AIOperationSquadConfig active;
    static bool ownsActive;
    public static AIOperationSquadConfig Active
    {
        get
        {
            if (active != null) return active;
            ownsActive = false;
            active = Resources.Load<AIOperationSquadConfig>(ResourcePath);
            if (active == null)
            {
                active = CreateInstance<AIOperationSquadConfig>();
                active.hideFlags = HideFlags.HideAndDontSave; ownsActive = true;
            }
            active.Validate();
            return active;
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeCache()
    {
        // Loaded assets belong to Unity/the user. Only dispose our generated fallback,
        // including when domain reload is disabled for repeated Play Mode sessions.
        if (ownsActive && active != null) DestroyImmediate(active);
        active = null; ownsActive = false;
    }
    void OnValidate() => Validate();
    public void Validate()
    {
        StandardSize = Mathf.Clamp(StandardSize, 3, 5);
        MaximumSquads = Mathf.Clamp(MaximumSquads, 1, 16);
        CohesionRadius = Mathf.Clamp(CohesionRadius, 2, 8);
        AssemblyGraceTurns = Mathf.Clamp(AssemblyGraceTurns, 1, 5);
        MaximumChainActions = Mathf.Clamp(MaximumChainActions, 2, 5);
        MaximumChainTargets = Mathf.Clamp(MaximumChainTargets, 1, 8);
        MinimumChainStepBudgetMs = Mathf.Clamp(MinimumChainStepBudgetMs, 1, 250);
        MaximumScoreBonus = Mathf.Clamp(float.IsNaN(MaximumScoreBonus) || float.IsInfinity(MaximumScoreBonus)
            ? 2 : MaximumScoreBonus, 0, 3);
    }
    public int Size => Mathf.Clamp(StandardSize, 3, 5);
    public int Limit => Mathf.Clamp(MaximumSquads, 1, 16);
}
