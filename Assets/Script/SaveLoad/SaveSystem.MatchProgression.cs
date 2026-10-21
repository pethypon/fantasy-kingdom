using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

public static partial class SaveSystem
{
    // Never evict a recorded result and accidentally reward an old checkpoint again.
    // About 100,000 matches fit within the existing profile byte limit; reaching the
    // bound fails explicitly and preserves the last valid profile.
    public const int MaximumCompletedMatches = 100000;

    public struct MatchProgression
    {
        public int PreviousThreatLevel, ThreatLevel;
        // DeveloperTest is retained as compatibility metadata: autoplay use does
        // not disqualify a completed match from progression.
        public bool Recorded, AlreadyRecorded, DeveloperTest;
        public bool Increased => Recorded && ThreatLevel > PreviousThreatLevel;
    }

    public static bool IsValidMatchId(string value)
        => value != null && Guid.TryParseExact(value, "N", out var id)
            && string.Equals(value, id.ToString("N"), StringComparison.Ordinal);

    /// <summary>Commit one terminal result, including autoplay. A completed checkpoint cannot award it again.</summary>
    public static bool TryRecordMatchResult(string matchId, GameResult result, bool developerTest,
        out MatchProgression progression, out string reason)
    {
        progression = new MatchProgression { DeveloperTest = developerTest };
        reason = "";
        if (!IsValidMatchId(matchId)) { reason = "試合の識別子が不正です。"; return false; }
        bool won = result == GameResult.Win || result == GameResult.TimeUpWin;
        bool lost = result == GameResult.Lose || result == GameResult.TimeUpLose;
        if (!won && !lost && result != GameResult.TimeUpDraw) { reason = "終了していない試合は記録できません。"; return false; }
        if (!TryLoadProfile(out var profile, out reason)) return false;
        progression.PreviousThreatLevel = progression.ThreatLevel = profile.ThreatLevel;
        if (profile.CompletedMatchIds.Contains(matchId))
        { progression.AlreadyRecorded = true; return true; }
        if (profile.CompletedMatchIds.Count >= MaximumCompletedMatches)
        { reason = "試合の完了記録が保存上限に達しました。プロファイルを保護するため更新しませんでした。"; return false; }

        profile.CompletedMatchIds.Add(matchId);
        if (won)
        {
            profile.ThreatLevel = Math.Min(AIThreatLevel.MaxLevel, profile.ThreatLevel + 1);
            if (profile.TotalWins < int.MaxValue) profile.TotalWins++;
        }
        else if (lost && profile.TotalLosses < int.MaxValue) profile.TotalLosses++;
        if (!TrySaveProfile(profile, out reason)) return false;
        progression.Recorded = true;
        progression.ThreatLevel = profile.ThreatLevel;
        return true;
    }

    static string LegacyMatchId(GameSaveData data)
    {
        // Binary field encoding avoids locale, JSON whitespace, current turn, and save-date changes.
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, Encoding.UTF8, true))
        {
            writer.Write("FantasyKingdom-legacy-match-v1");
            writer.Write(data.MapWidth); writer.Write(data.MapDepth);
            writer.Write(data.MapSeedX); writer.Write(data.MapSeedZ);
            writer.Write(data.PCPx); writer.Write(data.PCPy); writer.Write(data.PCPz);
            writer.Write(data.ECPx); writer.Write(data.ECPy); writer.Write(data.ECPz);
            writer.Write(data.AI?.RngSeed ?? 0);
            writer.Write(data.AI?.Reflection?.BattleId ?? "");
        }
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(bytes.ToArray());
        return new Guid(new byte[] { hash[0], hash[1], hash[2], hash[3], hash[4], hash[5], hash[6], hash[7],
            hash[8], hash[9], hash[10], hash[11], hash[12], hash[13], hash[14], hash[15] }).ToString("N");
    }
}
