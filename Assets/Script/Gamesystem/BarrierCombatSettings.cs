using System;
using System.Collections.Generic;
using UnityEngine;

public enum BarrierRecoveryRounding { Ceiling, Floor, Nearest }

/// <summary>Explicitly configured combat revision; unapproved balance drafts never activate themselves.</summary>
[CreateAssetMenu(menuName = "Fantasy Kingdom/ゲーム設定/バリア戦闘設定")]
public sealed class BarrierCombatSettings : ScriptableObject
{
    public const string ResourcePath = "GameContent/BarrierCombat";
    [Serializable] public struct Entry { public Kind kind; [Min(0)] public int initialBarrier; }
    [InspectorName("バリア戦闘を有効化")] public bool enabledForPlay;
    [InspectorName("未確定の戦闘細則を調整中")] public bool trialBalance;
    [HideInInspector] public int specificationRevision;
    [Header("自然回復（HP最大到達から所有陣営の3境界後、毎回20%）")]
    [InspectorName("自軍ターン開始時に回復（無効なら終了時）")] public bool recoveryAtOwnerTurnStart;
    [HideInInspector] public bool recoverOnThresholdTurn = true; // Legacy R1 compatibility; R2 always uses the third boundary.
    [HideInInspector] public bool repeatRecoveryEveryTurn = true; // Legacy R1 compatibility; R2 repeats while full HP.
    [HideInInspector] public bool fullyNegatedHitsResetRecovery; // Legacy R1 compatibility; nullified/barrier-only hits do not reset.
    [InspectorName("回復量の端数処理")] public BarrierRecoveryRounding recoveryRounding = BarrierRecoveryRounding.Ceiling;
    [HideInInspector] public bool growCurrentBarrierOnLevelUp; // Legacy serialized field; fixed barriers never grow.
    [HideInInspector] public bool barrierDamageGrantsExperience; // Legacy serialized field; unit XP is abolished.
    [Header("DEF廃止後の仮ダメージ式（最終決定ではありません）")]
    [Min(0), InspectorName("仮の基礎ダメージ")] public float provisionalDamageBase = 1;
    [Min(0), InspectorName("仮のATK係数")] public float provisionalAttackCoefficient = .7f;
    [InspectorName("職種ごとの初期バリア")] public List<Entry> initialBarriers = new List<Entry>();
    [InspectorName("重戦士の通常攻撃・バリア部分の倍率"), Min(1)] public float heavyNormalBarrierMultiplier = 1;
    static BarrierCombatSettings loaded, fallback;
    static bool triedLoading;
    readonly Dictionary<Kind, int> initialCache = new Dictionary<Kind, int>();
    bool initialCacheValid;
    public static BarrierCombatSettings Active
    {
        get
        {
            if (!triedLoading) { loaded = Resources.Load<BarrierCombatSettings>(ResourcePath); triedLoading = true; }
            if (loaded != null) return loaded;
            if (fallback == null) { fallback = CreateInstance<BarrierCombatSettings>(); fallback.hideFlags = HideFlags.HideAndDontSave; }
            return fallback;
        }
    }
    public int Initial(Kind kind)
    {
        if (!initialCacheValid)
        {
            initialCache.Clear();
            if (initialBarriers != null) foreach (var entry in initialBarriers)
                if (!initialCache.ContainsKey(entry.kind)) initialCache.Add(entry.kind, Mathf.Max(0, entry.initialBarrier));
            initialCacheValid = true;
        }
        return initialCache.TryGetValue(kind, out int value) ? value : 0;
    }
    /// <summary>R1 on 2026-10-21 supersedes every earlier table. The combat details remain provisional.</summary>
    public void PopulateR1Values()
    {
        initialBarriers = new List<Entry>();
        Kind[] kinds = { Kind.Knight, Kind.Archer, Kind.Magic, Kind.Assassin, Kind.Scout, Kind.Priest, Kind.Guardian,
            Kind.Crossbow, Kind.Magicsniper, Kind.Bomber, Kind.WhiteMage, Kind.HolyKnight, Kind.Samurai, Kind.Ninja,
            Kind.Berserker, Kind.Navigator, Kind.HeavyWarrior, Kind.DragonKnight };
        int[] values = { 70, 25, 30, 5, 20, 20, 15, 40, 20, 40, 15, 50, 30, 10, 30, 30, 50, 30 };
        for (int i = 0; i < kinds.Length; i++) initialBarriers.Add(new Entry { kind = kinds[i], initialBarrier = values[i] });
        specificationRevision = 1; trialBalance = true; initialCacheValid = false;
    }
    // Compatibility name for existing validation fixtures; it now uses the confirmed R1 table.
    public void PopulateTrialValues() => PopulateR1Values();
    public static void InvalidateLoadedSettings() { loaded = null; triedLoading = false; }
    public static IDisposable UseForValidation(BarrierCombatSettings settings)
    {
        var previous = loaded; bool previousTried = triedLoading; loaded = settings; triedLoading = true;
        return new Restore(() => { loaded = previous; triedLoading = previousTried; });
    }
    sealed class Restore : IDisposable
    {
        Action action; public Restore(Action value) => action = value;
        public void Dispose() { var value = action; action = null; value?.Invoke(); }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetRuntimeCache() { loaded = null; triedLoading = false; }
    void OnValidate() { initialCacheValid = false; GameContentRevision.NotifyChanged(); }
}
