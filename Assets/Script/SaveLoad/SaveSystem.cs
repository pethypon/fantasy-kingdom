using System;
using System.IO;
using UnityEngine;

/// <summary>Persistence facade: validate before applying, and recoverable atomic UTF-8 writes.</summary>
public static partial class SaveSystem
{
    const string SaveDir = "saves", ProfileFile = "profile.json";
    const int MaxSlots = 3;
    public const int CurrentSaveVersion = 10, CurrentProfileVersion = 2;
    public const string PlayerTurnCheckpoint = "player_turn";
    public const int MaximumSaveBytes = 64 * 1024 * 1024, MaximumProfileBytes = 16 * 1024 * 1024;
    static string testStorageRoot = null;
    static string GetSaveDir() => Path.Combine(testStorageRoot ?? Application.persistentDataPath, SaveDir);
    static string GetSlotPath(int slot) => Path.Combine(GetSaveDir(), $"save_{slot}.json");
    static string GetProfilePath() => Path.Combine(GetSaveDir(), ProfileFile);
    static bool ValidSlot(int slot) => slot >= 0 && slot < MaxSlots;

    public static bool TryLoadProfile(out ProfileData data, out string reason)
    {
        data = null;
        if (!AtomicSaveStorage.HasAny(GetProfilePath())) { data = new ProfileData(); reason = ""; return true; }
        if (!AtomicSaveStorage.TryRead(GetProfilePath(), MaximumProfileBytes, ValidateProfileJson, out string json,
            out reason, out var source)) return false;
        AIPerformanceCounters.MarkFileRead(); data = ParseProfile(json); return true;
    }
    public static ProfileData LoadProfile()
    {
        if (TryLoadProfile(out var data, out string reason)) return data;
        Debug.LogWarning("[SaveSystem] " + reason); return new ProfileData();
    }
    public static bool TrySaveProfile(ProfileData data, out string reason)
    {
        reason = "";
        if (data == null || data.SchemaVersion > CurrentProfileVersion)
        { reason = "新しい形式または不正なプロファイルを上書きできません。"; return false; }
        if (!ValidateProfileData(data, out reason)) return false;
        var copy = JsonUtility.FromJson<ProfileData>(JsonUtility.ToJson(data));
        copy.SchemaVersion = CurrentProfileVersion;
        copy.GameVersion = copy.LastSavedVersion = FKBuildInfo.Current.GameVersion;
        if (string.IsNullOrEmpty(copy.CreatedVersion)) copy.CreatedVersion = copy.GameVersion;
        bool saved = AtomicSaveStorage.TryWrite(GetProfilePath(), JsonUtility.ToJson(copy, true), MaximumProfileBytes,
            ValidateProfileJson, out reason);
        if (saved) AIPerformanceCounters.MarkFileWrite(); return saved;
    }
    public static void SaveProfile(ProfileData data)
    { if (!TrySaveProfile(data, out string reason)) Debug.LogWarning("[SaveSystem] " + reason); }
    public static bool TrySaveGame(int slot, GameSaveData data, out string reason)
    {
        reason = "";
        if (!ValidSlot(slot)) { reason = "保存スロットが範囲外です。"; return false; }
        // Guard before stamping: a future DTO must never silently become an older save.
        if (data == null || data.Version > CurrentSaveVersion || data.SchemaVersion > CurrentSaveVersion)
        { reason = "新しい形式または不正なセーブデータを上書きできません。"; return false; }
        if (!ValidateGameData(data, out reason)) return false;
        var copy = JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(data));
        copy.Version = copy.SchemaVersion = CurrentSaveVersion;
        copy.GameVersion = copy.LastSavedVersion = FKBuildInfo.Current.GameVersion;
        if (string.IsNullOrEmpty(copy.CreatedVersion)) copy.CreatedVersion = copy.GameVersion;
        copy.SaveDate = DateTime.Now.ToString("yyyy/MM/dd HH:mm");
        bool saved = AtomicSaveStorage.TryWrite(GetSlotPath(slot), JsonUtility.ToJson(copy, true), MaximumSaveBytes,
            ValidateGameJson, out reason);
        if (saved) AIPerformanceCounters.MarkFileWrite(); return saved;
    }
    public static void SaveGame(int slot, GameSaveData data)
    { if (!TrySaveGame(slot, data, out string reason)) Debug.LogWarning("[SaveSystem] " + reason); }
    public static bool TryLoadGame(int slot, out GameSaveData data, out string reason)
    {
        data = null;
        if (!ValidSlot(slot)) { reason = "保存スロットが範囲外です。"; return false; }
        if (!AtomicSaveStorage.TryRead(GetSlotPath(slot), MaximumSaveBytes, ValidateGameJson, out string json,
            out reason, out var source)) return false;
        AIPerformanceCounters.MarkFileRead(); data = RunMigrations(ParseGame(json)); return true;
    }
    public static GameSaveData LoadGame(int slot)
    {
        if (TryLoadGame(slot, out var data, out string reason)) return data;
        Debug.LogWarning("[SaveSystem] " + reason); return null;
    }
    public static bool HasSaveData(int slot) => ValidSlot(slot) && AtomicSaveStorage.HasAny(GetSlotPath(slot));
    public static string GetSlotSummary(int slot)
    {
        if (!HasSaveData(slot)) return "--- 空き ---";
        if (!TryLoadGame(slot, out var data, out string reason)) return "--- 読込不可 --- " + reason;
        return $"Turn {data.Turn}  脅威度{data.ThreatLevel}  {data.SaveDate}" + (string.IsNullOrEmpty(reason) ? "" : "（復旧）");
    }
    static AtomicSaveStorage.Validation ValidateGameJson(string json, out string reason)
    {
        var data = ParseGame(json);
        if (data != null && (data.Version > CurrentSaveVersion || data.SchemaVersion > CurrentSaveVersion))
        { reason = "このセーブは新しいゲーム形式です。対応する新しい版で開いてください。"; return AtomicSaveStorage.Validation.FutureSchema; }
        if (data == null || data.Version < 0 || data.SchemaVersion < 0 || data.Version >= 7 && data.Version != data.SchemaVersion)
        { reason = "セーブの形式番号が不正です。"; return AtomicSaveStorage.Validation.Invalid; }
        data = RunMigrations(data);
        return ValidateGameData(data, out reason) ? AtomicSaveStorage.Validation.Valid : AtomicSaveStorage.Validation.Invalid;
    }
    [Serializable] sealed class SaveHeader { public int Version = 0; public int SchemaVersion = 0; }
    static GameSaveData ParseGame(string json)
    {
        var data = JsonUtility.FromJson<GameSaveData>(json);
        var header = JsonUtility.FromJson<SaveHeader>(json);
        if (data != null && header != null) { data.Version = header.Version; data.SchemaVersion = header.SchemaVersion; }
        if (data != null && header != null && header.Version >= 9 && !HasJsonField(json, "MatchId")) data.MatchId = null;
        // Unity can construct absent nested serializable payloads. Absence must not invent a battle/session.
        if (data?.AI != null)
        {
            if (!HasJsonField(json, "Reflection")) data.AI.Reflection = null;
            if (!HasJsonField(json, "Operations")) data.AI.Operations = null;
            if (!HasJsonField(json, "Exploration")) data.AI.Exploration = null;
            if (!HasJsonField(json, "Operation")) data.AI.Operation = null;
            if (!HasJsonField(json, "StrategicObjective")) data.AI.StrategicObjective = null;
        }
        return data;
    }
    static bool HasJsonField(string json, string field)
    {
        for (int i = 0; i < json.Length; i++)
        {
            if (json[i] != '"') continue;
            int start = ++i; bool escaped = false;
            while (i < json.Length && json[i] != '"')
            {
                if (json[i] == '\\') { escaped = true; i++; }
                i++;
            }
            int end = i, next = i + 1;
            while (next < json.Length && char.IsWhiteSpace(json[next])) next++;
            if (!escaped && end - start == field.Length && next < json.Length && json[next] == ':'
                && string.CompareOrdinal(json, start, field, 0, field.Length) == 0) return true;
        }
        return false;
    }
    static ProfileData ParseProfile(string json)
    {
        var data = JsonUtility.FromJson<ProfileData>(json);
        if (data == null) return null;
        if (data.MatchHistory == null) data.MatchHistory = new System.Collections.Generic.List<MatchAnalysisData>();
        if (data.CompletedMatchIds == null) data.CompletedMatchIds = new System.Collections.Generic.List<string>();
        if (string.IsNullOrEmpty(data.CreatedVersion)) data.CreatedVersion = "legacy";
        data.SchemaVersion = CurrentProfileVersion; return data;
    }
    static AtomicSaveStorage.Validation ValidateProfileJson(string json, out string reason)
    {
        var header = JsonUtility.FromJson<ProfileData>(json);
        if (header != null && header.SchemaVersion > CurrentProfileVersion)
        { reason = "このプロファイルは新しいゲーム形式です。以前の版では上書きしません。"; return AtomicSaveStorage.Validation.FutureSchema; }
        if (header == null || header.SchemaVersion < 0)
        { reason = "プロファイル形式が不正です。"; return AtomicSaveStorage.Validation.Invalid; }
        return ValidateProfileData(ParseProfile(json), out reason) ? AtomicSaveStorage.Validation.Valid : AtomicSaveStorage.Validation.Invalid;
    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    internal static IDisposable UseTestStorageRoot(string root)
    {
        string full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string temporary = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(temporary, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Test storage must be below the OS temporary directory.");
        return new TestStorageScope(full);
    }
    sealed class TestStorageScope : IDisposable
    {
        readonly string previous; bool disposed;
        public TestStorageScope(string root) { previous = testStorageRoot; testStorageRoot = root; }
        public void Dispose() { if (!disposed) { testStorageRoot = previous; disposed = true; } }
    }
#endif
}
