using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Stages a Resources identity for one build, then restores previous bytes and GUID metadata.
/// A small Library journal permits cleanup after an interrupted build. Cleanup only touches
/// known generated files and refuses to overwrite edits made while the build was running.
/// </summary>
public sealed class FKBuildMetadataStage
{
    public const string AssetPath = "Assets/Resources/Build/FKBuildInfo.json";
    public const string JournalRelativePath = "Library/FKBuildMetadata/restore.json";
    const int MaximumOriginalBytes = 65536, MaximumJournalBytes = 262144;
    readonly string root, asset, meta, folder, folderMeta, journal;

    [Serializable]
    sealed class RestoreJournal
    {
        public int Version = 1;
        public bool OriginalExists, OriginalMetaExists, FolderCreated;
        public string Original, OriginalMeta, OriginalHash, GeneratedHash, GeneratedMetaHash, GeneratedFolderMetaHash;
    }

    public FKBuildMetadataStage(string projectRoot)
    {
        root = Path.GetFullPath(projectRoot);
        asset = Path.Combine(root, AssetPath); meta = asset + ".meta";
        folder = Path.GetDirectoryName(asset); folderMeta = folder + ".meta";
        journal = Path.Combine(root, JournalRelativePath);
    }
    public bool HasPendingRestore => File.Exists(journal);

    public void Stage(string json)
    {
        if (!FKBuildInfo.TryParse(json, out _)) throw new InvalidDataException("ビルド情報JSONが不正です。");
        if (HasPendingRestore) Restore();
        var record = new RestoreJournal {
            OriginalExists = File.Exists(asset), OriginalMetaExists = File.Exists(meta),
            FolderCreated = !Directory.Exists(folder), GeneratedHash = Hash(Encoding.UTF8.GetBytes(json))
        };
        if (record.OriginalExists)
        { var bytes = ReadBounded(asset, MaximumOriginalBytes); record.Original = Convert.ToBase64String(bytes); record.OriginalHash = Hash(bytes); }
        if (record.OriginalMetaExists) record.OriginalMeta = Convert.ToBase64String(ReadBounded(meta, MaximumOriginalBytes));
        Directory.CreateDirectory(Path.GetDirectoryName(journal));
        WriteAtomic(journal, Encoding.UTF8.GetBytes(JsonUtility.ToJson(record)));
        Directory.CreateDirectory(folder);
        WriteAtomic(asset, Encoding.UTF8.GetBytes(json));
    }

    public void RecordImportedMetadata()
    {
        var record = ReadJournal();
        if (File.Exists(meta)) record.GeneratedMetaHash = Hash(ReadBounded(meta, MaximumOriginalBytes));
        if (record.FolderCreated && File.Exists(folderMeta)) record.GeneratedFolderMetaHash = Hash(ReadBounded(folderMeta, MaximumOriginalBytes));
        WriteAtomic(journal, Encoding.UTF8.GetBytes(JsonUtility.ToJson(record)));
    }

    public string ReadStagedJson() => Encoding.UTF8.GetString(ReadBounded(asset, FKBuildInfo.MaximumJsonCharacters * 4));

    public void Restore()
    {
        if (!HasPendingRestore) return;
        var record = ReadJournal();
        if (File.Exists(asset))
        {
            string actual = Hash(ReadBounded(asset, MaximumOriginalBytes));
            if (actual != record.GeneratedHash && (!record.OriginalExists || actual != record.OriginalHash))
                throw new IOException("ビルド中に情報アセットが変更されたため上書きしません。復元バックアップ: " + journal);
        }
        if (File.Exists(meta) && (record.OriginalMetaExists || !string.IsNullOrEmpty(record.GeneratedMetaHash)))
        {
            string actual = Hash(ReadBounded(meta, MaximumOriginalBytes));
            string original = record.OriginalMetaExists ? Hash(DecodeBounded(record.OriginalMeta)) : null;
            if (actual != record.GeneratedMetaHash && actual != original)
                throw new IOException("ビルド中に情報アセットの.metaが変更されたため上書きしません。復元バックアップ: " + journal);
        }
        if (record.OriginalExists) WriteAtomic(asset, DecodeBounded(record.Original));
        else if (File.Exists(asset)) File.Delete(asset);
        if (record.OriginalMetaExists) WriteAtomic(meta, DecodeBounded(record.OriginalMeta));
        else DeleteIfGenerated(meta, record.GeneratedMetaHash);
        if (record.FolderCreated && Directory.Exists(folder) && Directory.GetFileSystemEntries(folder).Length == 0)
        { Directory.Delete(folder); DeleteIfGenerated(folderMeta, record.GeneratedFolderMetaHash); }
        File.Delete(journal);
    }

    RestoreJournal ReadJournal()
    {
        var value = JsonUtility.FromJson<RestoreJournal>(Encoding.UTF8.GetString(ReadBounded(journal, MaximumJournalBytes)));
        if (value == null || value.Version != 1 || string.IsNullOrEmpty(value.GeneratedHash)) throw new InvalidDataException("ビルド情報の復元記録が不正です: " + journal);
        return value;
    }
    static byte[] DecodeBounded(string value)
    {
        if (value == null || value.Length > MaximumOriginalBytes * 2) throw new InvalidDataException("ビルド情報の復元データが上限を超えています。");
        var bytes = Convert.FromBase64String(value);
        if (bytes.Length > MaximumOriginalBytes) throw new InvalidDataException("ビルド情報の復元データが上限を超えています。");
        return bytes;
    }
    static void DeleteIfGenerated(string path, string expectedHash)
    { if (!string.IsNullOrEmpty(expectedHash) && File.Exists(path) && Hash(ReadBounded(path, MaximumOriginalBytes)) == expectedHash) File.Delete(path); }
    public static byte[] ReadBounded(string path, int maximumBytes)
    {
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (file.Length < 0 || file.Length > maximumBytes) throw new InvalidDataException("読込上限を超えています: " + path);
            var bytes = new byte[(int)file.Length]; int offset = 0;
            while (offset < bytes.Length)
            { int count = file.Read(bytes, offset, bytes.Length - offset); if (count == 0) throw new EndOfStreamException(path); offset += count; }
            return bytes;
        }
    }
    public static void WriteAtomic(string path, byte[] bytes)
    {
        string temporary = path + ".tmp";
        using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
        if (File.Exists(path)) File.Replace(temporary, path, null);
        else File.Move(temporary, path);
    }
    public static string Hash(byte[] bytes)
    { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant(); }
}

/// <summary>Git and package files are build tools only; packaged runtime identity remains offline.</summary>
[InitializeOnLoad]
public sealed class FKLongTermBuildProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    public int callbackOrder => -1000;
    static FKLongTermBuildProcessor() { EditorApplication.delayCall += RecoverInterruptedBuild; }
    static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

    public void OnPreprocessBuild(BuildReport report)
    {
        LongTermContentValidation.RequireValidForBuild();
        try
        {
            var identity = CaptureIdentity(ProjectRoot, PlayerSettings.bundleVersion, Application.unityVersion, SaveSystem.CurrentSaveVersion);
            var stage = new FKBuildMetadataStage(ProjectRoot); stage.Stage(identity.ToJson());
            AssetDatabase.ImportAsset(FKBuildMetadataStage.AssetPath, ImportAssetOptions.ForceUpdate);
            stage.RecordImportedMetadata();
            EditorApplication.update -= RecoverWhenBuildEnds;
            EditorApplication.update += RecoverWhenBuildEnds;
            Debug.Log("[BuildInfo] version=" + identity.GameVersion + " commit=" + identity.Commit + " dirty=" + identity.Dirty + " schema=" + identity.SaveSchema);
        }
        catch (Exception ex)
        {
            RecoverInterruptedBuild();
            throw new BuildFailedException("ビルド情報の準備に失敗しました: " + ex.Message);
        }
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        try
        {
            var stage = new FKBuildMetadataStage(ProjectRoot);
            string json = stage.ReadStagedJson();
            string output = report.summary.outputPath;
            string directory = Directory.Exists(output) ? output : Path.GetDirectoryName(output);
            FKBuildMetadataStage.WriteAtomic(Path.Combine(directory, "FKBuildInfo.json"), Encoding.UTF8.GetBytes(json));
        }
        finally { RestoreAssets(); }
    }

    [MenuItem("Fantasy Kingdom/長期運用/中断したビルドの情報アセットを復元")]
    public static void RecoverInterruptedBuild()
    {
        if (BuildPipeline.isBuildingPlayer) { SubscribeRecovery(); return; }
        try { RestoreAssets(); }
        catch (Exception ex) { Debug.LogWarning("[BuildInfo] 自動復元を中止しました。" + ex.Message); }
    }
    static void SubscribeRecovery()
    { EditorApplication.update -= RecoverWhenBuildEnds; EditorApplication.update += RecoverWhenBuildEnds; }
    static void RecoverWhenBuildEnds()
    {
        if (BuildPipeline.isBuildingPlayer || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        EditorApplication.update -= RecoverWhenBuildEnds;
        RecoverInterruptedBuild();
    }
    static void RestoreAssets()
    {
        var stage = new FKBuildMetadataStage(ProjectRoot);
        if (!stage.HasPendingRestore) return;
        stage.Restore();
        // Refresh only after a build or interrupted-build recovery, never every frame.
        AssetDatabase.Refresh();
        EditorApplication.update -= RecoverWhenBuildEnds;
    }

    public static FKBuildInfo CaptureIdentity(string projectRoot, string version, string unityVersion, int schema)
    {
        string supplied = Environment.GetEnvironmentVariable("FK_BUILD_COMMIT");
        string commit = IsCommit(supplied) ? supplied.ToLowerInvariant() : Git(projectRoot, "rev-parse HEAD");
        if (!IsCommit(commit)) commit = "unversioned";
        string dirtySetting = Environment.GetEnvironmentVariable("FK_BUILD_DIRTY");
        bool dirty;
        if (string.Equals(dirtySetting, "false", StringComparison.OrdinalIgnoreCase) || dirtySetting == "0") dirty = false;
        else if (string.Equals(dirtySetting, "true", StringComparison.OrdinalIgnoreCase) || dirtySetting == "1") dirty = true;
        else dirty = !string.IsNullOrEmpty(supplied) || commit == "unversioned" || Git(projectRoot, "-c core.fsmonitor=false status --porcelain --untracked-files=no") != string.Empty;
        DateTime utc = DateTime.UtcNow;
        string epoch = Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH");
        if (!string.IsNullOrEmpty(epoch))
        {
            if (!long.TryParse(epoch, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)) throw new InvalidDataException("SOURCE_DATE_EPOCHはUTCのUnix秒で指定してください。");
            utc = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
        }
        string lockPath = Path.Combine(projectRoot, "Packages/packages-lock.json");
        string hash = File.Exists(lockPath) ? FKBuildMetadataStage.Hash(FKBuildMetadataStage.ReadBounded(lockPath, 4 * 1024 * 1024)) : string.Empty;
        return FKBuildInfo.Create(version, commit, unityVersion, utc.ToString("O", CultureInfo.InvariantCulture), dirty, schema, hash);
    }
    static bool IsCommit(string value)
    {
        if (string.IsNullOrEmpty(value) || (value.Length != 40 && value.Length != 64)) return false;
        foreach (char c in value) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
        return true;
    }
    static string Git(string root, string arguments)
    {
        try
        {
            using (var process = new Process { StartInfo = new ProcessStartInfo("git", arguments) {
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            } })
            {
                if (!process.Start()) return null;
                var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(1500)) { try { process.Kill(); } catch (Exception) { } return null; }
                if (process.ExitCode != 0 || !System.Threading.Tasks.Task.WaitAll(new System.Threading.Tasks.Task[] { output, errors }, 200)) return null;
                string value = output.Result; return value.Length <= 1024 * 1024 ? value.Trim() : null;
            }
        }
        catch (Exception) { return null; }
    }
}
