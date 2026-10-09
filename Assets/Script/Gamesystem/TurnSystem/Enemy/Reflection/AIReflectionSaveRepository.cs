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
    public AIReflectionSaveRepository(string storageRoot, string profileId, AIReflectionConfig config = null)
    {
        this.config = config != null ? config : AIReflectionConfig.Active;
        this.profileId = SafeFileName(profileId);
        ProfilePath = Path.Combine(storageRoot ?? Path.Combine(Application.persistentDataPath, "FantasyKingdom", "AI"),
            "Learning", this.profileId + ".v1.json");
    }
    public AIActionLearningProfile Load()
    {
        var profile = ReadValid(ProfilePath);
        if (profile != null) return profile;
        profile = ReadValid(ProfilePath + ".bak");
        return profile ?? new AIActionLearningProfile { ProfileId = profileId };
    }
    AIActionLearningProfile ReadValid(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("Profile exceeds size limit.");
            var profile = JsonUtility.FromJson<AIActionLearningProfile>(File.ReadAllText(path, Encoding.UTF8));
            if (!IsValid(profile)) throw new InvalidDataException("Invalid reflection profile schema or values.");
            profile.RebuildIndex();
            return profile;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            Debug.LogWarning("[AI自己評価] 学習データを読み込めないため、安全な初期値またはバックアップを使用します: " + exception.Message);
            try
            {
                string archive = path + ".corrupt." + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
                File.Move(path, archive);
            }
            catch (Exception archiveException) when (IsRecoverable(archiveException)) { /* Original remains recoverable. */ }
            return null;
        }
    }
    public bool Save(AIActionLearningProfile profile)
    {
        if (!IsValid(profile)) return false;
        string temp = ProfilePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath));
            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(profile));
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            if (File.Exists(ProfilePath))
            {
                try { File.Replace(temp, ProfilePath, ProfilePath + ".bak"); }
                catch (PlatformNotSupportedException) { BackupAndCopy(temp); }
                catch (IOException) { BackupAndCopy(temp); }
            }
            else File.Move(temp, ProfilePath);
            return true;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        { Debug.LogWarning("[AI自己評価] 学習データを保存できませんでした。AIは続行します: " + exception.Message); return false; }
    }
    void BackupAndCopy(string temp)
    {
        File.Copy(ProfilePath, ProfilePath + ".bak", true);
        File.Copy(temp, ProfilePath, true);
        File.Delete(temp);
    }
    public bool IsValid(AIActionLearningProfile profile)
    {
        if (profile == null || profile.SchemaVersion != 1 || profile.ProfileId != profileId
            || profile.BattlesPlayed < 0 || profile.BattlesPlayed > 1000000 || profile.Wins < 0 || profile.Losses < 0
            || profile.Wins > profile.BattlesPlayed || profile.Losses > profile.BattlesPlayed
            || profile.Entries == null || profile.Entries.Count > config.EntryLimit || profile.Sequence < 0) return false;
        if (profile.CompletedBattleIds == null) profile.CompletedBattleIds = new List<string>();
        if (profile.CompletedBattleIds.Count > 128) return false;
        foreach (string battle in profile.CompletedBattleIds) if (battle == null || battle.Length > 256) return false;
        if (profile.StrategyOutcomes == null) profile.StrategyOutcomes = new List<AIReflectionStrategyOutcome>();
        if (profile.StrategyOutcomes.Count > 32) return false;
        foreach (var strategy in profile.StrategyOutcomes)
            if (strategy == null || !Enum.IsDefined(typeof(TurnStrategy), strategy.Strategy)
                || strategy.ThreatBand < 0 || strategy.ThreatBand > 2 || strategy.Battles < 0 || strategy.Battles > 1000000
                || strategy.OwnTurns < 0 || strategy.OwnTurns > 1000000 || strategy.Wins < 0 || strategy.Losses < 0 || strategy.Draws < 0
                || strategy.Wins > strategy.Battles || strategy.Losses > strategy.Battles || strategy.Draws > strategy.Battles
                || !Finite(strategy.CumulativeBattleReward)) return false;
        var keys = new HashSet<string>();
        foreach (var entry in profile.Entries)
        {
            if (entry == null || string.IsNullOrEmpty(entry.ContextKey) || entry.ContextKey.Length > 512
                || string.IsNullOrEmpty(entry.ActionKey) || entry.ActionKey.Length > 256
                || entry.Samples < 0 || entry.Samples > 1000000 || entry.SuccessCount < 0 || entry.FailureCount < 0
                || entry.SuccessCount > entry.Samples || entry.FailureCount > entry.Samples
                || entry.LastUseSequence < 0 || !Finite(entry.LearnedValue) || entry.LearnedValue < config.Lower
                || entry.LearnedValue > config.Upper || !Finite(entry.CumulativeReward) || !Finite(entry.AverageReward)
                || !Finite(entry.MaxReward) || !Finite(entry.MinReward)
                || !keys.Add(entry.ContextKey + "\n" + entry.ActionKey)) return false;
            if (entry.FailureReasons == null) entry.FailureReasons = new List<FailureReasonCount>();
            if (entry.FailureReasons.Count > 32) return false;
            foreach (var reason in entry.FailureReasons)
                if (reason == null || reason.Count < 0 || reason.Count > 1000000
                    || !Enum.IsDefined(typeof(AIFailureReason), reason.Reason)) return false;
        }
        return true;
    }
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
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
