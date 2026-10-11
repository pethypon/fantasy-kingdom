using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>Versioned, bounded profiles saved with a complete temp file and recoverable backup.</summary>
public sealed class AIReflectionSaveRepository
{
    readonly AIReflectionConfig config;
    readonly string profileId;
    public string ProfilePath { get; }
    public string LegacyProfilePath { get; }
    public AIReflectionSaveRepository(string storageRoot, string profileId, AIReflectionConfig config = null)
    {
        this.config = config != null ? config : AIReflectionConfig.Active;
        this.profileId = SafeFileName(profileId);
        ProfilePath = Path.Combine(storageRoot ?? Path.Combine(Application.persistentDataPath, "FantasyKingdom", "AI"),
            "Learning", this.profileId + ".v2.json");
        LegacyProfilePath = Path.Combine(Path.GetDirectoryName(ProfilePath), this.profileId + ".v1.json");
    }
    bool futureSchema;
    public AIActionLearningProfile Load()
    {
        using var performanceScope = AIPerformanceCounters.Measure(AIPerformanceStage.Persistence);
        futureSchema = false;
        var profile = ReadValid(ProfilePath);
        if (profile != null) return profile;
        if (!futureSchema) profile = ReadValid(LegacyProfilePath);
        return profile ?? new AIActionLearningProfile { ProfileId = profileId };
    }
    AIActionLearningProfile ReadValid(string path)
    {
        if (!AtomicSaveStorage.HasAny(path)) return null;
        if (!AtomicSaveStorage.TryRead(path, 16 * 1024 * 1024, ValidateJson, out string json, out string reason, out var source))
        {
            Debug.LogWarning("[AI自己評価] 学習データを使用できないため、この試合では安全な初期値を使用します: " + reason);
            if (!futureSchema) AtomicSaveStorage.PreserveInvalid(path, 16 * 1024 * 1024, ValidateJson);
            return null;
        }
        if (source != AtomicSaveStorage.RecoverySource.Current)
            AtomicSaveStorage.PreserveInvalid(path, 16 * 1024 * 1024, ValidateJson);
        AIPerformanceCounters.MarkFileRead();
        var profile = JsonUtility.FromJson<AIActionLearningProfile>(json);
        int entries = profile.Entries?.Count ?? 0;
        AIReflectionMigration.TryUpgradeProfile(profile, config);
        if (string.IsNullOrEmpty(profile.CreatedVersion)) profile.CreatedVersion = "legacy";
        if (profile.Entries.Count != entries)
            Debug.LogWarning("[AI自己評価] 不正な学習パターンを " + (entries - profile.Entries.Count) + " 件除外し、有効な経験を復元しました。");
        profile.RebuildIndex(); return profile;
    }
    AtomicSaveStorage.Validation ValidateJson(string json, out string reason)
    {
        var profile = JsonUtility.FromJson<AIActionLearningProfile>(json);
        if (profile != null && profile.SchemaVersion > 2)
        { futureSchema = true; reason = "新しい形式の学習データは読み込み・上書きしません。"; return AtomicSaveStorage.Validation.FutureSchema; }
        if (profile == null || profile.ProfileId != profileId || !AIReflectionMigration.TryUpgradeProfile(profile, config) || !IsValid(profile))
        { reason = "学習データの形式または値が不正です。"; return AtomicSaveStorage.Validation.Invalid; }
        reason = ""; return AtomicSaveStorage.Validation.Valid;
    }
    public bool Save(AIActionLearningProfile profile)
    {
        using var performanceScope = AIPerformanceCounters.Measure(AIPerformanceStage.Persistence);
        if (profile?.SchemaVersion == 1 && !AIReflectionMigration.TryUpgradeProfile(profile, config)) return false;
        if (!IsValid(profile)) return false;
        // Legacy files also belong to this profile. Never hide a newer schema by creating a new filename.
        if (AtomicSaveStorage.HasAny(LegacyProfilePath))
        {
            foreach (string legacy in new[] { LegacyProfilePath, LegacyProfilePath + ".previous", LegacyProfilePath + ".bak" })
            {
                if (!File.Exists(legacy)) continue;
                if (!AtomicSaveStorage.ReadBounded(legacy, 16 * 1024 * 1024, out string old, out _)) return false;
                try { if (ValidateJson(old, out _) == AtomicSaveStorage.Validation.FutureSchema) return false; }
                catch (Exception exception) when (IsRecoverable(exception)) { }
            }
        }
        var copy = JsonUtility.FromJson<AIActionLearningProfile>(JsonUtility.ToJson(profile));
        copy.GameVersion = copy.LastSavedVersion = FKBuildInfo.Current.GameVersion;
        if (string.IsNullOrEmpty(copy.CreatedVersion)) copy.CreatedVersion = copy.GameVersion;
        bool saved = AtomicSaveStorage.TryWrite(ProfilePath, JsonUtility.ToJson(copy), 16 * 1024 * 1024, ValidateJson, out string reason);
        if (saved) AIPerformanceCounters.MarkFileWrite();
        else Debug.LogWarning("[AI自己評価] 学習データを保存できませんでした。AIは続行します: " + reason);
        return saved;
    }
    public bool IsValid(AIActionLearningProfile profile)
    {
        if (profile == null || profile.SchemaVersion != 2 || profile.ProfileId != profileId
            || !AIReflectionMigration.ValidProfileHeader(profile, config)) return false;
        if (profile.CompletedBattleIds == null) profile.CompletedBattleIds = new List<string>();
        if (profile.CompletedBattleIds.Count > 128) return false;
        foreach (string battle in profile.CompletedBattleIds) if (battle == null || battle.Length > 256) return false;
        if (profile.StrategyOutcomes == null) profile.StrategyOutcomes = new List<AIReflectionStrategyOutcome>();
        if (profile.StrategyOutcomes.Count > 32) return false;
        foreach (var strategy in profile.StrategyOutcomes)
            if (!AIReflectionMigration.ValidStrategy(strategy)) return false;
        var keys = new HashSet<string>();
        foreach (var entry in profile.Entries)
        {
            if (!AIReflectionMigration.ValidEntry(entry, config)
                || !keys.Add(entry.ContextKey + "\n" + entry.ActionKey)) return false;
            if (entry.FailureReasons == null) entry.FailureReasons = new List<FailureReasonCount>();
            if (entry.FailureReasons.Count > 32) return false;
            foreach (var reason in entry.FailureReasons)
                if (reason == null || reason.Count < 0 || reason.Count > 1000000
                    || !Enum.IsDefined(typeof(AIFailureReason), reason.Reason)) return false;
            if (entry.SuccessReasons == null) entry.SuccessReasons = new List<SuccessReasonCount>();
        }
        return true;
    }
    internal static bool IsRecoverable(Exception exception) => exception is IOException
        || exception is UnauthorizedAccessException || exception is ArgumentException
        || exception is NotSupportedException || exception is System.Security.SecurityException;
    public static string SafeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Default";
        var builder = new StringBuilder(Math.Min(value.Length, 80));
        foreach (char character in value)
        {
            if (builder.Length >= 80) break;
            builder.Append(char.IsLetterOrDigit(character) || character == '_' || character == '-' ? character : '_');
        }
        return builder.ToString();
    }
}
