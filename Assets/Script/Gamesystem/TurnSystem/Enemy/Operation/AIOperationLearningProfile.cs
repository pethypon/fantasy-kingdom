using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Bounded operation learning, independent of action rewards and indexed without a candidate-time scan.</summary>
[Serializable]
public sealed class AIOperationLearningProfile
{
    public int SchemaVersion = 1;
    public string ProfileId;
    public long Sequence;
    public List<AIOperationKnowledgeEntry> Entries = new List<AIOperationKnowledgeEntry>();
    // These are battle GUID + operation counter receipts, never a Unity or unit identity.
    public List<string> CompletedOperationIds = new List<string>();
    [NonSerialized] Dictionary<(string context, AIOperationGoal goal, string pattern), AIOperationKnowledgeEntry> index;
    [NonSerialized] HashSet<string> completed;

    public AIOperationKnowledgeEntry Find(string contextKey, AIOperationGoal goal, string pattern)
    {
        if (contextKey == null || pattern == null) return null;
        EnsureIndex();
        index.TryGetValue((contextKey, goal, pattern), out var entry);
        return entry;
    }

    public float GetModifier(string contextKey, AIOperationGoal goal, string pattern, AIOperationConfig config,
        float similarity = 1f)
    {
        if (config == null || !config.Enabled || !config.ApplyLearningToSelection) return 0;
        var entry = Find(contextKey, goal, pattern);
        if (entry == null || entry.Samples < Mathf.Clamp(config.MinimumSamplesForSelection, 2, MaximumCounter)) return 0;
        float confidence = Mathf.Clamp01(entry.Samples / (float)Mathf.Clamp(config.FullConfidenceSamples, 1, MaximumCounter));
        float value = Mathf.Clamp(Finite(entry.LearnedValue), Lower(config), Upper(config))
            * confidence * Mathf.Clamp01(Finite(similarity));
        float limit = Mathf.Clamp(Finite(config.MaxOperationExperienceModifier), 0, 3);
        return Mathf.Clamp(value, -limit, limit);
    }

    /// <returns>The actual change to generalized experience. Provisional or already learned results return zero.</returns>
    public float Learn(AIOperationPlan plan, AIOperationEvaluationResult result, AIOperationConfig config)
    {
        if (plan == null || result == null || config == null || !config.Enabled || result.IsProvisional
            || !Terminal(plan.Status)
            || !ValidKey(plan.ContextKey) || !ValidPattern(plan.PatternKey)
            || !Enum.IsDefined(typeof(AIOperationGoal), plan.PrimaryGoal) || plan.PrimaryGoal == AIOperationGoal.None
            || !IsFinite(result.FinalScore) || !IsFinite(result.LearningReward)) return 0;
        string receipt = Receipt(plan.BattleId, plan.OperationId);
        if (receipt == null) return 0;
        EnsureIndex();
        if (!completed.Add(receipt)) return 0;
        if (receipt != null)
        {
            CompletedOperationIds.Add(receipt);
            int limit = config.EventLimit;
            while (CompletedOperationIds.Count > limit)
            { completed.Remove(CompletedOperationIds[0]); CompletedOperationIds.RemoveAt(0); }
        }
        var entry = Find(plan.ContextKey, plan.PrimaryGoal, plan.PatternKey);
        if (entry == null)
        {
            if (Entries.Count >= config.EntryLimit) PruneOldest();
            entry = new AIOperationKnowledgeEntry
            { ContextKey = plan.ContextKey, Goal = plan.PrimaryGoal, OperationPattern = plan.PatternKey };
            Entries.Add(entry); index[(entry.ContextKey, entry.Goal, entry.OperationPattern)] = entry;
        }
        if (entry.SuccessReasons == null) entry.SuccessReasons = new List<AIOperationSuccessReason>();
        if (entry.FailureReasons == null) entry.FailureReasons = new List<AIOperationFailureReason>();
        float before = Mathf.Clamp(Finite(entry.LearnedValue), Lower(config), Upper(config));
        entry.Samples = Mathf.Clamp(entry.Samples, 0, MaximumCounter);
        entry.AverageScore = Mathf.Clamp(Finite(entry.AverageScore), 0, Mathf.Max(0, Finite(config.PerfectScore)));
        float reward = Mathf.Clamp(result.LearningReward, Lower(config), Upper(config));
        if (!result.PrimaryGoalAchieved) reward = Mathf.Min(reward, Mathf.Clamp(Finite(config.MaxLearningRewardWithoutPrimaryGoal), 0, .5f));
        int samples = Math.Min(MaximumCounter, entry.Samples + 1);
        float alpha = Mathf.Clamp01(Finite(config.LearningRate)) / Mathf.Sqrt(Math.Max(1, samples));
        entry.LearnedValue = Mathf.Clamp(before + alpha * (reward - before), Lower(config), Upper(config));
        entry.AverageScore += (Mathf.Clamp(result.FinalScore, 0, Mathf.Max(0, Finite(config.PerfectScore))) - entry.AverageScore) / samples;
        entry.Samples = samples;
        if (result.PrimaryGoalAchieved) entry.Successes = Math.Min(MaximumCounter, entry.Successes + 1);
        // Rational strategic aborts are retained without turning a successful individual action into a failure.
        else if (!plan.StrategicAbort) entry.Failures = Math.Min(MaximumCounter, entry.Failures + 1);
        entry.Confidence = Mathf.Clamp01(samples / (float)Mathf.Clamp(config.FullConfidenceSamples, 1, MaximumCounter));
        entry.LastUsedTurn = Math.Max(0, plan.ActualEndTurn);
        if (Sequence < 0) Sequence = 0;
        if (Sequence < long.MaxValue) Sequence++;
        entry.LastUseSequence = Sequence;
        MergeReasons(entry.SuccessReasons, result.SuccessReasons);
        MergeReasons(entry.FailureReasons, result.FailureReasons);
        plan.LearnedValueBefore = before;
        plan.LearnedValueAfter = entry.LearnedValue;
        plan.LearningReward = reward;
        return entry.LearnedValue - before;
    }

    public void RebuildIndex() { index = null; completed = null; EnsureIndex(); }
    static bool Terminal(AIOperationStatus status) => status == AIOperationStatus.Aborted || status == AIOperationStatus.Success
        || status == AIOperationStatus.PartialSuccess || status == AIOperationStatus.Failure;

    void EnsureIndex()
    {
        if (index != null && completed != null) return;
        if (Entries == null) Entries = new List<AIOperationKnowledgeEntry>();
        if (CompletedOperationIds == null) CompletedOperationIds = new List<string>();
        index = new Dictionary<(string, AIOperationGoal, string), AIOperationKnowledgeEntry>(Entries.Count);
        foreach (var entry in Entries)
            if (entry != null && entry.ContextKey != null && entry.OperationPattern != null)
                index[(entry.ContextKey, entry.Goal, entry.OperationPattern)] = entry;
        completed = new HashSet<string>(CompletedOperationIds, StringComparer.Ordinal);
    }

    void PruneOldest()
    {
        if (Entries.Count == 0) return;
        int remove = 0;
        for (int i = 1; i < Entries.Count; i++)
            if (Entries[i].LastUseSequence < Entries[remove].LastUseSequence) remove = i;
        var victim = Entries[remove];
        index.Remove((victim.ContextKey, victim.Goal, victim.OperationPattern));
        Entries.RemoveAt(remove);
    }

    static void MergeReasons<T>(List<T> destination, List<T> source) where T : struct
    {
        if (source == null) return;
        foreach (var reason in source)
            if (Convert.ToInt32(reason) != 0 && Enum.IsDefined(typeof(T), reason) && !destination.Contains(reason)) destination.Add(reason);
    }

    internal const int MaximumCounter = 1000000;
    internal static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    internal static float Finite(float value) => IsFinite(value) ? value : 0;
    internal static float Lower(AIOperationConfig config) => Mathf.Clamp(Finite(config.OperationLearningMin), -3, 0);
    internal static float Upper(AIOperationConfig config) => Mathf.Clamp(Finite(config.OperationLearningMax), 0, 3);
    internal static string Receipt(string battleId, long operationId)
    {
        if (string.IsNullOrEmpty(battleId) || battleId.Length > 128 || operationId <= 0) return null;
        foreach (char c in battleId) if (!char.IsLetterOrDigit(c) && c != '_' && c != '-') return null;
        return battleId + ":" + operationId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
    internal static bool ValidReceipt(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 150) return false;
        int separator = value.LastIndexOf(':');
        return separator > 0 && long.TryParse(value.Substring(separator + 1), out long id)
            && Receipt(value.Substring(0, separator), id) == value;
    }
    internal static bool ValidKey(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 512) return false;
        // A generalized band key cannot contain raw coordinate tuples, current HP or runtime identities.
        foreach (char c in value) if (char.IsControl(c) || c == '(' || c == ')' || c == ',' || c == '{' || c == '}') return false;
        string lower = value.ToLowerInvariant();
        if (lower.Contains("lifeid") || lower.Contains("instanceid") || lower.Contains("currenthp")
            || lower.Contains("targethp") || lower.Contains("position") || lower.Contains("coordinate")) return false;
        string[] parts = value.Split('|', ';');
        foreach (string part in parts)
        {
            int equals = part.IndexOf('=');
            if (equals <= 0) continue;
            string key = part.Substring(0, equals).Trim().ToLowerInvariant();
            if (key == "x" || key == "y" || key == "z" || key == "hp" || key == "pos" || key == "id") return false;
        }
        return true;
    }
    internal static bool ValidPattern(string pattern)
    {
        if (string.IsNullOrEmpty(pattern) || pattern.Length > 512) return false;
        string[] steps = pattern.Split('>');
        if (steps.Length > 32) return false;
        foreach (string step in steps)
            if (!Enum.TryParse(step, out AIOperationStepType type) || !Enum.IsDefined(typeof(AIOperationStepType), type)) return false;
        return true;
    }
}
