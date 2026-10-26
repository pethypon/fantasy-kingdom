using UnityEngine;

[CreateAssetMenu(menuName = "Fantasy Kingdom/AI/作戦部隊設定", fileName = "OperationSquadConfig")]
public sealed class AIOperationSquadConfig : ScriptableObject
{
    [InspectorName("部隊編成を有効化")] public bool Enabled = true;
    [Header("AI運用の提案初期値（ゲームルールを変更しない）")]
    [Range(3, 5), InspectorName("標準部隊人数")] public int StandardSize = 5;
    [Range(1, 16), InspectorName("保存する最大部隊数")] public int MaximumSquads = 16;
    [Range(2, 8), InspectorName("連携距離の目安")] public int CohesionRadius = 4;
    [Range(1, 5), InspectorName("集合を待つ自軍ターン数")] public int AssemblyGraceTurns = 2;
    [Range(2, 5), InspectorName("連携の最大行動数")] public int MaximumChainActions = 5;
    [Range(1, 8), InspectorName("連携を比較する最大目標数")] public int MaximumChainTargets = 8;
    [Range(1, 250), InspectorName("勝利連携の1行動に確保する猶予ms")] public int MinimumChainStepBudgetMs = 50;
    [Range(0f, 3f), InspectorName("同一優先度内の部隊補正上限")] public float MaximumScoreBonus = 2;
    static AIOperationSquadConfig active;
    public static AIOperationSquadConfig Active
    {
        get
        {
            if (active != null) return active;
            active = Resources.Load<AIOperationSquadConfig>("AI/OperationSquadConfig");
            if (active == null) { active = CreateInstance<AIOperationSquadConfig>(); active.hideFlags = HideFlags.HideAndDontSave; }
            return active;
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() => active = null;
    public int Size => Mathf.Clamp(StandardSize, 3, 5);
    public int Limit => Mathf.Clamp(MaximumSquads, 1, 16);
}
