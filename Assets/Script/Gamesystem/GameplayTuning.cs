using UnityEngine;

/// <summary>Shared formal rules, read by the live game and AI alike. Missing assets use the same defaults.</summary>
[CreateAssetMenu(menuName = "Fantasy Kingdom/ゲーム設定/共通戦闘・召喚・クリスタル")]
public sealed class GameplayTuning : ScriptableObject
{
    public const string ResourcePath = "GameContent/GameplayTuning";
    [Header("クリスタル（新規ゲームで適用）")]
    [InspectorName("メインクリスタルの初期HP"), Min(1)] public int mainCrystalHP = 1000;
    [InspectorName("メインクリスタルのHP上限"), Min(1)] public int mainCrystalCap = 2000;
    [InspectorName("サブクリスタルのHP"), Min(1)] public int subCrystalHP = 500;
    [InspectorName("欠片１個の最大HP増加"), Min(1)] public int shardHP = 1000;
    [InspectorName("上限時の欠片の魔石報酬"), Min(0)] public int shardMagicOre = 30;
    [Header("各陣営の自軍ターンごとにリセット")]
    [InspectorName("１ターンの召喚上限（全兵種・拠点で共有）"), Range(1, 16)] public int summonsPerTurn = 1;
    [InspectorName("駒１体の通常攻撃回数（スキル・反撃は別枠）"), Range(1, 16)] public int normalAttacksPerTurn = 2;
    static GameplayTuning cached;
    static bool loaded;
    public static GameplayTuning Active { get { if (!loaded) { cached = Resources.Load<GameplayTuning>(ResourcePath); loaded = true; } return cached; } }
    public static void ResetCache() { cached = null; loaded = false; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetSession() => ResetCache();
    void OnValidate()
    {
        mainCrystalHP = Mathf.Max(1, mainCrystalHP); mainCrystalCap = Mathf.Max(mainCrystalHP, mainCrystalCap);
        subCrystalHP = Mathf.Max(1, subCrystalHP); shardHP = Mathf.Max(1, shardHP); shardMagicOre = Mathf.Max(0, shardMagicOre);
        summonsPerTurn = Mathf.Clamp(summonsPerTurn, 1, 16); normalAttacksPerTurn = Mathf.Clamp(normalAttacksPerTurn, 1, 16);
        GameContentRevision.NotifyChanged();
    }
}
