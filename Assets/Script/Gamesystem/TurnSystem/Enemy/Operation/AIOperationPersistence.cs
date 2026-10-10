using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>Independent version-one operation knowledge storage with bounded input and recoverable atomic replacement.</summary>
public sealed class AIOperationPersistence
{
    readonly AIOperationConfig config;
    readonly string profileId;
    public string ProfilePath { get; }

    public AIOperationPersistence(string storageDirectory, string profileId, AIOperationConfig config = null)
    {
        this.config = config != null ? config : AIOperationConfig.Active;
        this.profileId = SafeName(profileId);
        ProfilePath = Path.Combine(storageDirectory ?? Path.Combine(Application.persistentDataPath, "FantasyKingdom", "AI"),
            "Operations", this.profileId + ".AIOperationProfile.v1.json");
    }

    public AIOperationLearningProfile Load()
    {
        if (!CanPersist) return Fresh();
        return ReadValid(ProfilePath) ?? ReadValid(ProfilePath + ".bak") ?? Fresh();
    }

    public bool Save(AIOperationLearningProfile profile)
    {
        if (!CanPersist || !IsValid(profile)) return false;
        string temporary = ProfilePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath));
            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(profile));
            if (bytes.Length > FileLimit) return false;
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            if (!File.Exists(ProfilePath)) File.Move(temporary, ProfilePath);
            else
            {
                try { File.Replace(temporary, ProfilePath, ProfilePath + ".bak"); }
                catch (PlatformNotSupportedException) { ReplaceWithBackup(temporary); }
                catch (IOException) { ReplaceWithBackup(temporary); }
            }
            return true;
        }
        catch (Exception exception) when (Recoverable(exception))
        {
            Debug.LogWarning("[AI作戦] 作戦経験を保存できませんでした。通常AIは続行します: " + exception.Message);
            return false;
        }
    }

    public bool IsValid(AIOperationLearningProfile profile)
    {
        if (!ValidHeader(profile)) return false;
        var keys = new HashSet<(string, AIOperationGoal, string)>();
        foreach (var entry in profile.Entries)
            if (!ValidEntry(entry) || !keys.Add((entry.ContextKey, entry.Goal, entry.OperationPattern))) return false;
        var receipts = new HashSet<string>(StringComparer.Ordinal);
        if (profile.CompletedOperationIds != null)
            foreach (string receipt in profile.CompletedOperationIds)
                if (!AIOperationLearningProfile.ValidReceipt(receipt) || !receipts.Add(receipt)) return false;
        return true;
    }

    /// <summary>Salvage valid generalized entries without accepting a different schema or profile identity.</summary>
    public bool TrySanitize(AIOperationLearningProfile profile)
    {
        if (!ValidHeader(profile, true)) return false;
        var entries = new List<AIOperationKnowledgeEntry>(profile.Entries.Count);
        var keys = new HashSet<(string, AIOperationGoal, string)>();
        foreach (var entry in profile.Entries)
        {
            if (!ValidEntry(entry) || !keys.Add((entry.ContextKey, entry.Goal, entry.OperationPattern))) continue;
            if (entry.SuccessReasons == null) entry.SuccessReasons = new List<AIOperationSuccessReason>();
            if (entry.FailureReasons == null) entry.FailureReasons = new List<AIOperationFailureReason>();
            entry.Confidence = Mathf.Clamp01(entry.Samples / (float)Mathf.Clamp(config.FullConfidenceSamples, 1,
                AIOperationLearningProfile.MaximumCounter));
            entries.Add(entry);
        }
        if (entries.Count > config.EntryLimit)
        {
            // Reducing the authoring limit retains the most recently used knowledge rather than resetting the profile.
            entries.Sort((left, right) => right.LastUseSequence.CompareTo(left.LastUseSequence));
            entries.RemoveRange(config.EntryLimit, entries.Count - config.EntryLimit);
        }
        profile.Entries = entries;
        var receipts = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (profile.CompletedOperationIds != null)
            foreach (string receipt in profile.CompletedOperationIds)
                if (AIOperationLearningProfile.ValidReceipt(receipt) && seen.Add(receipt)) receipts.Add(receipt);
        if (receipts.Count > config.EventLimit) receipts.RemoveRange(0, receipts.Count - config.EventLimit);
        profile.CompletedOperationIds = receipts;
        profile.RebuildIndex();
        return IsValid(profile);
    }

    AIOperationLearningProfile ReadValid(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            if (new FileInfo(path).Length > FileLimit) throw new InvalidDataException("作戦経験ファイルがサイズ上限を超えています。");
            var profile = JsonUtility.FromJson<AIOperationLearningProfile>(File.ReadAllText(path, Encoding.UTF8));
            int previousCount = profile?.Entries?.Count ?? 0;
            if (!TrySanitize(profile)) throw new InvalidDataException("作戦経験の形式またはヘッダーが無効です。");
            if (previousCount != profile.Entries.Count)
                Debug.LogWarning("[AI作戦] 不正な作戦経験を " + (previousCount - profile.Entries.Count)
                    + " 件除外し、有効な経験を復元しました。");
            return profile;
        }
        catch (Exception exception) when (Recoverable(exception))
        {
            Debug.LogWarning("[AI作戦] 作戦経験を読み込めないため、バックアップまたは初期状態を使用します: " + exception.Message);
            // Preserve the broken input for inspection; recovery never silently erases it.
            try { File.Move(path, path + ".corrupt." + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")); }
            catch (Exception archiveException) when (Recoverable(archiveException)) { }
            return null;
        }
    }

    bool ValidHeader(AIOperationLearningProfile profile, bool allowReducedLimit = false) => profile != null && profile.SchemaVersion == 1
        && profile.ProfileId == profileId && profile.Sequence >= 0 && profile.Entries != null
        && profile.Entries.Count <= (allowReducedLimit ? 8192 : config.EntryLimit)
        && (profile.CompletedOperationIds == null || profile.CompletedOperationIds.Count <= (allowReducedLimit ? 8192 : config.EventLimit));

    bool ValidEntry(AIOperationKnowledgeEntry entry)
    {
        if (entry == null || !AIOperationLearningProfile.ValidKey(entry.ContextKey)
            || !AIOperationLearningProfile.ValidPattern(entry.OperationPattern)
            || !Enum.IsDefined(typeof(AIOperationGoal), entry.Goal) || entry.Goal == AIOperationGoal.None
            || entry.Samples < 0 || entry.Samples > AIOperationLearningProfile.MaximumCounter
            || entry.Successes < 0 || entry.Failures < 0 || entry.Successes > entry.Samples || entry.Failures > entry.Samples
            || entry.Successes + entry.Failures > entry.Samples || entry.LastUsedTurn < 0 || entry.LastUseSequence < 0
            || !AIOperationLearningProfile.IsFinite(entry.AverageScore) || entry.AverageScore < 0 || entry.AverageScore > config.PerfectScore
            || !AIOperationLearningProfile.IsFinite(entry.LearnedValue)
            || entry.LearnedValue < AIOperationLearningProfile.Lower(config) || entry.LearnedValue > AIOperationLearningProfile.Upper(config)
            || !AIOperationLearningProfile.IsFinite(entry.Confidence) || entry.Confidence < 0 || entry.Confidence > 1) return false;
        return ValidReasons(entry.SuccessReasons) && ValidReasons(entry.FailureReasons);
    }

    static bool ValidReasons<T>(List<T> values) where T : struct
    {
        if (values == null) return true;
        if (values.Count > Enum.GetValues(typeof(T)).Length) return false;
        var unique = new HashSet<T>();
        foreach (var value in values) if (!Enum.IsDefined(typeof(T), value) || !unique.Add(value)) return false;
        return true;
    }

    void ReplaceWithBackup(string temporary)
    {
        File.Copy(ProfilePath, ProfilePath + ".bak", true);
        File.Copy(temporary, ProfilePath, true);
        File.Delete(temporary);
    }
    bool CanPersist => config != null && config.Enabled && config.EnablePersistentLearning;
    int FileLimit => Mathf.Clamp(config.MaxProfileFileBytes, 1024, 64 * 1024 * 1024);
    AIOperationLearningProfile Fresh() => new AIOperationLearningProfile { ProfileId = profileId };
    static bool Recoverable(Exception exception) => exception is IOException || exception is UnauthorizedAccessException
        || exception is ArgumentException || exception is NotSupportedException || exception is System.Security.SecurityException;
    static string SafeName(string value)
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
