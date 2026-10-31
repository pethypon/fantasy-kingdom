using System;
using UnityEngine;

/// <summary>R2's explicitly provisional interpretation, editable without altering the rules engine.</summary>
[CreateAssetMenu(menuName = "Fantasy Kingdom/ルール/R2 暫定細則")]
public sealed class R2EconomyRules : ScriptableObject
{
    [Header("暫定：仕様書18章。正式な細則決定まで調整可能")]
    [Tooltip("未払い20%の分母。仕様書の暫定値は最大HP。")]
    public bool unpaidUsesMaximumHP = true;
    [Tooltip("暫定値は無効。工兵のAPは通常建築と同じです。")]
    public bool engineerDoubleAP;
    [Tooltip("暫定値は無効。工兵の消費市民は通常建築と同じです。")]
    public bool engineerDoubleCitizen;
    [Tooltip("暫定：自領土内の新規鉱山建築を禁止します。既存鉱山の所有・保存は維持します。")]
    public bool disallowNewHomeMines = true;

    static R2EconomyRules cached;
    static bool loaded;
    public static R2EconomyRules Active
    {
        get
        {
            if (!loaded) { cached = Resources.Load<R2EconomyRules>("R2EconomyRules"); loaded = true; }
            return cached;
        }
    }
    public static int UnpaidDamage(int hp, int maximumHP)
    {
        if (hp <= 0) return 0;
        int basis = Active == null || Active.unpaidUsesMaximumHP ? maximumHP : hp;
        return Math.Min(hp, (int)Math.Max(1L, ((long)Math.Max(0, basis) + 4L) / 5L));
    }
    public static int DoubleNonNegative(int value) => (int)Math.Min(int.MaxValue, (long)Math.Max(0, value) * 2);
    public static FacilityData.ResourceCost EngineerCost(FacilityData.ResourceCost ordinary) => new FacilityData.ResourceCost(
        DoubleNonNegative(ordinary.Wood), DoubleNonNegative(ordinary.Stone), DoubleNonNegative(ordinary.Iron),
        DoubleNonNegative(ordinary.MagicOre), DoubleNonNegative(ordinary.Water),
        Active != null && Active.engineerDoubleCitizen ? DoubleNonNegative(ordinary.Citizen) : Math.Max(0, ordinary.Citizen));
    public static int EngineerAP(int ordinary)
        => Active != null && Active.engineerDoubleAP ? DoubleNonNegative(ordinary) : Math.Max(0, ordinary);
    public static bool DisallowNewHomeMines => Active == null || Active.disallowNewHomeMines;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { cached = null; loaded = false; }
}
