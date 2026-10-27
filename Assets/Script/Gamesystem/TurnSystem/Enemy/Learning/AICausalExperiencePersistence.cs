using System;
using System.IO;
using UnityEngine;

/// <summary>Separate namespace from operation/ML profiles. Future versions are never overwritten.</summary>
public sealed class AICausalExperiencePersistence
{
    readonly Team owner;
    readonly int limit;
    bool futureSchema;
    public string ProfilePath { get; }
    public AICausalExperiencePersistence(string root, Team owner, int limit = 512)
    {
        this.owner = owner; this.limit = Math.Max(1, Math.Min(512, limit));
        ProfilePath = Path.Combine(root, "Cognition", "R4_" + owner + ".v1.json");
    }
    AtomicSaveStorage.Validation Validate(string json, out string reason)
    {
        var data = JsonUtility.FromJson<AICausalExperienceProfile>(json);
        if (data != null && data.SchemaVersion > 1)
        { futureSchema = true; reason = "future_cognitive_schema"; return AtomicSaveStorage.Validation.FutureSchema; }
        bool valid = AICausalExperienceMemory.Valid(data, owner, limit);
        reason = valid ? "" : "invalid_cognitive_profile";
        return valid ? AtomicSaveStorage.Validation.Valid : AtomicSaveStorage.Validation.Invalid;
    }
    public AICausalExperienceProfile Load()
    {
        futureSchema = false;
        if (!AtomicSaveStorage.HasAny(ProfilePath)) return null;
        if (!AtomicSaveStorage.TryRead(ProfilePath, 2 * 1024 * 1024, Validate, out string json, out string reason, out _))
        { Debug.LogWarning("[R4経験] 読み込みを省略: " + reason); return null; }
        AIPerformanceCounters.MarkFileRead(); return JsonUtility.FromJson<AICausalExperienceProfile>(json);
    }
    public bool Save(AICausalExperienceProfile data)
    {
        if (futureSchema || !AICausalExperienceMemory.Valid(data, owner, limit)) return false;
        bool saved = AtomicSaveStorage.TryWrite(ProfilePath, JsonUtility.ToJson(data), 2 * 1024 * 1024, Validate, out string reason);
        if (saved) AIPerformanceCounters.MarkFileWrite();
        else Debug.LogWarning("[R4経験] 保存を省略: " + reason);
        return saved;
    }
}
