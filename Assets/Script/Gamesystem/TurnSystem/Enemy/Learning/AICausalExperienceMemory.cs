using System;
using System.Collections.Generic;

[Serializable]
public sealed class AICausalExperienceEntry
{
    public int KnowledgeVersion = 1;
    public string KnowledgeId, OperatorSequenceSignature, ContextSignature;
    public AIOperationGoal ObjectiveGoal;
    public string FailureMode;
    public int Samples, Successes, Failures, NeutralOrAbort;
    public float MeanOutcome, PredictionErrorAverage, Confidence;
    public long LastUseSequence;
}

[Serializable]
public sealed class AICausalExperienceProfile
{
    public int SchemaVersion = 1;
    public Team Owner;
    public long Sequence;
    public List<AICausalExperienceEntry> Entries = new List<AICausalExperienceEntry>();
    // Persistent, fixed-size duplicate filter. Collisions may suppress a sample, never grant another reward.
    // It contains hashes of completion receipts, not battle positions or actor identities.
    public int[] CompletionBits = new int[128];
}

/// <summary>Generalized calibration only; never modifies Reflection rewards or operation rewards.</summary>
public sealed class AICausalExperienceMemory
{
    readonly int limit;
    readonly Dictionary<string, AICausalExperienceEntry> index = new Dictionary<string, AICausalExperienceEntry>();
    public AICausalExperienceProfile Profile { get; private set; }
    public bool Dirty { get; private set; }
    public AICausalExperienceMemory(Team owner, int maximumEntries = 512)
    { limit = Math.Max(1, Math.Min(512, maximumEntries)); Profile = new AICausalExperienceProfile { Owner = owner }; }
    static string Key(string knowledge, string operators, string context, AIOperationGoal goal)
        => knowledge + "|" + operators + "|" + context + "|" + (int)goal;
    public bool Restore(AICausalExperienceProfile profile)
    {
        if (!Valid(profile, Profile.Owner, limit)) return false;
        Profile = profile; index.Clear();
        foreach (var entry in Profile.Entries) index.Add(Key(entry.KnowledgeId, entry.OperatorSequenceSignature,
            entry.ContextSignature, entry.ObjectiveGoal), entry);
        Dirty = false; return true;
    }
    public bool Record(string receipt, string knowledge, string operators, string context, AIOperationGoal goal,
        float observedOutcome, float predictionError, bool strategicAbort, string failureMode)
    {
        if (string.IsNullOrEmpty(receipt) || receipt.Length > 512 || !ValidKnowledgeId(knowledge)
            || !ValidSignature(operators, 256) || !ValidSignature(context, 128)
            || !Enum.IsDefined(typeof(AIOperationGoal), goal) || !Finite(observedOutcome) || !Finite(predictionError)
            || !ValidSignature(failureMode ?? "None", 64) || HasReceipt(receipt)) return false;
        string key = Key(knowledge, operators, context, goal);
        if (!index.TryGetValue(key, out var entry))
        {
            if (Profile.Entries.Count >= limit)
            {
                var oldest = Profile.Entries[0];
                foreach (var item in Profile.Entries)
                    if (item.LastUseSequence < oldest.LastUseSequence) oldest = item;
                Profile.Entries.Remove(oldest);
                index.Remove(Key(oldest.KnowledgeId, oldest.OperatorSequenceSignature, oldest.ContextSignature, oldest.ObjectiveGoal));
            }
            entry = new AICausalExperienceEntry { KnowledgeId = knowledge, OperatorSequenceSignature = operators,
                ContextSignature = context, ObjectiveGoal = goal };
            Profile.Entries.Add(entry); index.Add(key, entry);
        }
        MarkReceipt(receipt);
        if (entry.Samples >= 1000000 || Profile.Sequence == long.MaxValue) return false;
        float outcome = Math.Max(-1, Math.Min(1, observedOutcome));
        entry.Samples++;
        if (strategicAbort || Math.Abs(outcome) < .01f) entry.NeutralOrAbort++;
        else if (outcome > 0) entry.Successes++; else entry.Failures++;
        // A necessary emergency suspension supplies evidence, not an automatic negative reward.
        if (strategicAbort) outcome = 0;
        entry.MeanOutcome += (outcome - entry.MeanOutcome) / entry.Samples;
        entry.PredictionErrorAverage += (Math.Max(0, Math.Min(1, predictionError)) - entry.PredictionErrorAverage) / entry.Samples;
        entry.Confidence = Math.Min(1, entry.Samples / 10f);
        entry.FailureMode = failureMode ?? "None"; entry.LastUseSequence = ++Profile.Sequence;
        Dirty = true; return true;
    }
    public float Calibration(string knowledge, string operators, string context, AIOperationGoal goal, int minimumSamples = 2)
    {
        if (!index.TryGetValue(Key(knowledge, operators, context, goal), out var entry)
            || entry.Samples < Math.Max(2, minimumSamples)) return 0;
        return Math.Max(-.1f, Math.Min(.1f, entry.MeanOutcome * entry.Confidence * (1 - entry.PredictionErrorAverage) * .1f));
    }
    public void MarkSaved() => Dirty = false;
    static uint Hash(string text, uint seed)
    { unchecked { uint h = seed; foreach (char ch in text) { h ^= ch; h *= 16777619; } return h; } }
    bool HasReceipt(string receipt)
    {
        for (uint i = 0; i < 3; i++)
        { uint h = Hash(receipt, 2166136261u + i * 101u) % 4096; if ((Profile.CompletionBits[h / 32] & (1 << (int)(h % 32))) == 0) return false; }
        return true;
    }
    void MarkReceipt(string receipt)
    {
        for (uint i = 0; i < 3; i++)
        { uint h = Hash(receipt, 2166136261u + i * 101u) % 4096; Profile.CompletionBits[h / 32] |= 1 << (int)(h % 32); }
    }
    public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    public static bool ValidSignature(string value, int maximum)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maximum) return false;
        foreach (char ch in value) if (!(ch >= 'A' && ch <= 'Z' || ch >= 'a' && ch <= 'z'
            || ch >= '0' && ch <= '9' || ch == '-' || ch == '_' || ch == ':' || ch == ',' || ch == '.')) return false;
        return true;
    }
    public static bool ValidKnowledgeId(string id)
    {
        if (id == null || id.Length != 5 || id[2] != '-' || id[3] != '0' && id[3] != '1'
            || id[4] < '0' || id[4] > '9') return false;
        int number = (id[3] - '0') * 10 + id[4] - '0'; string category = id.Substring(0, 2);
        int maximum = category == "CH" ? 10 : category == "RT" ? 8 : category == "SL" ? 7
            : category == "SH" || category == "GO" || category == "HI" ? 5 : 0;
        return number >= 1 && number <= maximum;
    }
    public static bool Valid(AICausalExperienceProfile profile, Team owner, int maximum = 512)
    {
        if (profile == null || profile.SchemaVersion != 1 || profile.Owner != owner || profile.Sequence < 0
            || profile.Entries == null || profile.Entries.Count > Math.Max(1, Math.Min(512, maximum))
            || profile.CompletionBits == null || profile.CompletionBits.Length != 128) return false;
        var keys = new HashSet<string>();
        foreach (var entry in profile.Entries)
        {
            if (entry == null || entry.KnowledgeVersion != 1 || !ValidKnowledgeId(entry.KnowledgeId)
                || !ValidSignature(entry.OperatorSequenceSignature, 256) || !ValidSignature(entry.ContextSignature, 128)
                || !ValidSignature(entry.FailureMode ?? "None", 64) || !Enum.IsDefined(typeof(AIOperationGoal), entry.ObjectiveGoal)
                || entry.Samples < 0 || entry.Samples > 1000000 || entry.Successes < 0 || entry.Failures < 0 || entry.NeutralOrAbort < 0
                || (long)entry.Successes + entry.Failures + entry.NeutralOrAbort != entry.Samples
                || entry.LastUseSequence < 0 || entry.LastUseSequence > profile.Sequence
                || !Finite(entry.MeanOutcome) || entry.MeanOutcome < -1 || entry.MeanOutcome > 1
                || !Finite(entry.PredictionErrorAverage) || entry.PredictionErrorAverage < 0 || entry.PredictionErrorAverage > 1
                || !Finite(entry.Confidence) || entry.Confidence < 0 || entry.Confidence > 1
                || !keys.Add(Key(entry.KnowledgeId, entry.OperatorSequenceSignature, entry.ContextSignature, entry.ObjectiveGoal))) return false;
        }
        return true;
    }
}
