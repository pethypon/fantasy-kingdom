using System;
using UnityEngine;

public enum AIThinkingBudgetCategory { Normal, Combat, Coordination, Critical }

/// <summary>Observed decision context only; no hidden actors or authoritative reporting data.</summary>
public readonly struct AIThinkingBudgetFacts
{
    public readonly bool Critical, CoordinatedCombat, Combat;
    public AIThinkingBudgetFacts(bool critical, bool coordinatedCombat, bool combat)
    { Critical = critical; CoordinatedCombat = coordinatedCombat; Combat = combat; }
}

public readonly struct AIThinkingBudgetDecision
{
    public readonly Team Team;
    public readonly int Turn;
    public readonly AIThinkingBudgetCategory Category;
    public readonly double RequestedMilliseconds, Milliseconds, CompletionDeadlineMilliseconds;
    public readonly bool OfficialTimerLimited;
    public readonly string Reason;
    internal AIThinkingBudgetDecision(Team team, int turn, AIThinkingBudgetCategory category, double requested,
        double adopted, double completion, bool limited, string reason)
    {
        Team = team; Turn = turn; Category = category; RequestedMilliseconds = requested;
        Milliseconds = adopted; CompletionDeadlineMilliseconds = completion;
        OfficialTimerLimited = limited; Reason = reason;
    }
}

/// <summary>AI policy, not a change to the player's turn/total clock or action rules.
/// Ranges are required by R1. The defaults 2.5/5/6/7.5 seconds are adjustable proposal values.</summary>
[CreateAssetMenu(menuName = "Fantasy Kingdom/AI/戦況別の思考時間", fileName = "ThinkingBudgetConfig")]
public sealed class AIThinkingBudgetConfig : ScriptableObject
{
    public const double MaximumMilliseconds = 8000;
    public const double MaximumCompletionGraceMilliseconds = 500;
    [InspectorName("戦況別思考時間を使用")] public bool Enabled = true;
    [Header("R1の範囲内の試作初期値（確定バランス値ではありません）")]
    [InspectorName("通常・探索・経済（秒）"), Range(2, 3)] public float NormalSeconds = 2.5f;
    [InspectorName("交戦（秒）"), Range(4, 6)] public float CombatSeconds = 5;
    [InspectorName("複雑な部隊連携（秒）"), Range(5, 7)] public float CoordinationSeconds = 6;
    [InspectorName("クリスタル攻略・緊急救命（秒）"), Range(6, 8)] public float CriticalSeconds = 7.5f;
    [InspectorName("確定済み行動の終了猶予（秒）"), Range(0, .5f)] public float CompletionGraceSeconds = .5f;

    static AIThinkingBudgetConfig active;
    static bool ownsActive;
    public static AIThinkingBudgetConfig Active
    {
        get
        {
            if (active != null) return active;
            active = Resources.Load<AIThinkingBudgetConfig>("AI/ThinkingBudgetConfig");
            if (active == null)
            { active = CreateInstance<AIThinkingBudgetConfig>(); active.hideFlags = HideFlags.HideAndDontSave; ownsActive = true; }
            return active;
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntime()
    {
        if (ownsActive && active != null) DestroyImmediate(active);
        active = null; ownsActive = false;
    }
    static float Seconds(float value, float fallback, float minimum, float maximum)
        => Mathf.Clamp(float.IsNaN(value) || float.IsInfinity(value) ? fallback : value, minimum, maximum);

    public AIThinkingBudgetDecision Decide(AIThinkingBudgetFacts facts, Team team, int turn,
        double officialRemainingMilliseconds = double.PositiveInfinity, double legacyMilliseconds = 3000)
    {
        var category = facts.Critical ? AIThinkingBudgetCategory.Critical
            : facts.CoordinatedCombat ? AIThinkingBudgetCategory.Coordination
            : facts.Combat ? AIThinkingBudgetCategory.Combat : AIThinkingBudgetCategory.Normal;
        double milliseconds = category == AIThinkingBudgetCategory.Critical ? Seconds(CriticalSeconds, 7.5f, 6, 8) * 1000d
            : category == AIThinkingBudgetCategory.Coordination ? Seconds(CoordinationSeconds, 6, 5, 7) * 1000d
            : category == AIThinkingBudgetCategory.Combat ? Seconds(CombatSeconds, 5, 4, 6) * 1000d
            : Seconds(NormalSeconds, 2.5f, 2, 3) * 1000d;
        if (!Enabled) milliseconds = double.IsNaN(legacyMilliseconds) || double.IsInfinity(legacyMilliseconds)
            ? 3000 : Math.Min(MaximumMilliseconds, Math.Max(0, legacyMilliseconds));
        // A game clock is a hard rule. Never extend the player's remaining time to
        // satisfy a policy budget; expose the conflict to diagnostics instead.
        double available = double.IsPositiveInfinity(officialRemainingMilliseconds) ? double.PositiveInfinity
            : double.IsNaN(officialRemainingMilliseconds) || double.IsNegativeInfinity(officialRemainingMilliseconds)
                ? 0 : Math.Max(0, officialRemainingMilliseconds);
        double adopted = Math.Min(milliseconds, available);
        double grace = Seconds(CompletionGraceSeconds, .5f, 0, .5f) * 1000d;
        bool limited = adopted < milliseconds;
        string reason = !Enabled ? "configured_legacy_budget"
            : category == AIThinkingBudgetCategory.Critical ? "observed_critical_objective_or_exposure"
            : category == AIThinkingBudgetCategory.Coordination ? "coordinated_observed_engagement"
            : category == AIThinkingBudgetCategory.Combat ? "observed_legal_combat_or_exposure" : "ordinary_observed_progress";
        if (limited) reason += ":official_timer_limit";
        return new AIThinkingBudgetDecision(team, turn, category, milliseconds, adopted,
            Math.Min(adopted + grace, available), limited, reason);
    }
}
