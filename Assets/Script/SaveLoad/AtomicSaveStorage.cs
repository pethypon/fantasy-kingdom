using System;
using System.IO;
using System.Text;

/// <summary>Value-only UTF-8 file boundary. Never replaces a primary through delete/copy fallback.</summary>
public static class AtomicSaveStorage
{
    public enum Validation { Valid, Invalid, FutureSchema }
    public enum RecoverySource { None, Current, Previous, Backup }
    public delegate Validation JsonValidator(string json, out string reason);
    static readonly object writeGate = new object();
    static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    internal static Action<string, string, string> ReplaceForTests = null;
#endif

    public static bool HasAny(string path) => File.Exists(path) || File.Exists(path + ".previous") || File.Exists(path + ".bak");

    public static bool TryRead(string path, int maximumBytes, JsonValidator validator,
        out string json, out string reason, out RecoverySource source)
    {
        json = null; reason = "保存データがありません。"; source = RecoverySource.None;
        string[] paths = { path, path + ".previous", path + ".bak" };
        for (int i = 0; i < paths.Length; i++)
        {
            if (!File.Exists(paths[i])) continue;
            if (!ReadBounded(paths[i], maximumBytes, out string candidate, out reason)) continue;
            Validation result = Check(validator, candidate, out reason);
            // A newer format is not corruption. Do not hide it by silently loading an older generation.
            if (result == Validation.FutureSchema) return false;
            if (result != Validation.Valid) continue;
            json = candidate; source = (RecoverySource)(i + 1);
            reason = i == 0 ? "" : (i == 1 ? "直前の正常な保存データから復旧しました。" : "バックアップから復旧しました。");
            return true;
        }
        return false;
    }

    public static bool TryWrite(string path, string json, int maximumBytes, JsonValidator validator, out string reason)
    {
        reason = "";
        if (string.IsNullOrWhiteSpace(path) || maximumBytes < 1 || Check(validator, json, out reason) != Validation.Valid)
            return false;
        lock (writeGate)
        {
            string temporary = path + ".tmp";
            try
            {
                byte[] bytes = Utf8.GetBytes(json);
                if (bytes.Length > maximumBytes) { reason = "保存データがサイズ上限を超えています。"; return false; }
                string previous = path + ".previous", backup = path + ".bak";
                bool currentValid = false, previousValid = false;
                foreach (string existing in new[] { path, previous, backup })
                {
                    if (!File.Exists(existing)) continue;
                    if (new FileInfo(existing).Length == 0) continue; // Empty corruption has no future schema to protect.
                    // A file that cannot be inspected must not be overwritten or rotated.
                    if (!ReadBounded(existing, maximumBytes, out string old, out reason)) return false;
                    Validation result = Check(validator, old, out reason);
                    if (result == Validation.FutureSchema) return false;
                    if (existing == path) currentValid = result == Validation.Valid;
                    if (existing == previous) previousValid = result == Validation.Valid;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                WriteFlushed(temporary, bytes);
                if (!ReadBounded(temporary, maximumBytes, out string verify, out reason)
                    || Check(validator, verify, out reason) != Validation.Valid) return false;
                if (!File.Exists(path)) File.Move(temporary, path);
                else
                {
                    if (currentValid && previousValid) AtomicCopy(previous, backup);
                    if (!currentValid) AtomicCopy(path, path + ".corrupt");
                    // Invalid primary never displaces a valid previous generation.
                    Replace(temporary, path, currentValid ? previous : null);
                }
                reason = ""; return true;
            }
            catch (Exception exception) when (Recoverable(exception))
            { reason = "安全に保存できませんでした。以前のデータを維持します: " + exception.Message; return false; }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception exception) when (Recoverable(exception)) { }
            }
        }
    }

    public static bool ReadBounded(string path, int maximumBytes, out string json, out string reason)
    {
        json = null; reason = "";
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length <= 0 || stream.Length > maximumBytes)
            { reason = "保存ファイルが空、またはサイズ上限を超えています。"; return false; }
            byte[] bytes = new byte[(int)stream.Length];
            int read = 0;
            while (read < bytes.Length)
            {
                int count = stream.Read(bytes, read, bytes.Length - read);
                if (count == 0) throw new EndOfStreamException("保存ファイルを最後まで読み込めません。");
                read += count;
            }
            json = Utf8.GetString(bytes);
            // Legacy StreamWriter files may contain a UTF-8 BOM.
            if (json.Length > 0 && json[0] == '\uFEFF') json = json.Substring(1);
            return true;
        }
        catch (Exception exception) when (Recoverable(exception))
        { reason = "保存ファイルを読み込めません: " + exception.Message; return false; }
    }
    /// <summary>One bounded diagnostic copy. Future formats and original files remain untouched.</summary>
    public static void PreserveInvalid(string path, int maximumBytes, JsonValidator validator)
    {
        if (!File.Exists(path) || !ReadBounded(path, maximumBytes, out string json, out _)
            || Check(validator, json, out _) != Validation.Invalid) return;
        try { AtomicCopy(path, path + ".corrupt.latest"); }
        catch (Exception exception) when (Recoverable(exception)) { }
    }

    static Validation Check(JsonValidator validator, string json, out string reason)
    {
        try
        {
            if (validator == null || string.IsNullOrWhiteSpace(json))
            { reason = "保存データが空です。"; return Validation.Invalid; }
            return validator(json, out reason);
        }
        catch (Exception exception) when (Recoverable(exception))
        { reason = "保存データの形式が不正です: " + exception.Message; return Validation.Invalid; }
    }
    static void AtomicCopy(string source, string destination)
    {
        string staging = destination + ".tmp";
        try
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(staging, FileMode.Create, FileAccess.Write, FileShare.None))
            { input.CopyTo(output); output.Flush(true); }
            if (File.Exists(destination)) Replace(staging, destination, null);
            else File.Move(staging, destination);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }
    static void WriteFlushed(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
    }
    static void Replace(string source, string destination, string backup)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (ReplaceForTests != null) { ReplaceForTests(source, destination, backup); return; }
#endif
        File.Replace(source, destination, backup);
    }
    public static bool Recoverable(Exception exception) => exception is IOException || exception is UnauthorizedAccessException
        || exception is ArgumentException || exception is NotSupportedException || exception is System.Security.SecurityException;
}
