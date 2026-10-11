using System;
using UnityEngine;

/// <summary>
/// Immutable build identity. Owner: application; lifetime: one runtime session.
/// The only runtime input is a packaged TextAsset. Missing/invalid metadata falls back to
/// engine identity; gameplay and saves never depend on Git, the network, or a writable project.
/// </summary>
public sealed class FKBuildInfo
{
    public const string ResourcePath = "Build/FKBuildInfo";
    public const int MaximumJsonCharacters = 8192;
    static FKBuildInfo current;

    public string GameVersion { get; }
    public string Commit { get; }
    public string UnityVersion { get; }
    public string BuildUtc { get; }
    public bool Dirty { get; }
    public int SaveSchema { get; }
    public string PackageLockHash { get; }

    [Serializable]
    sealed class SerializedIdentity
    {
        public string GameVersion, Commit, UnityVersion, BuildUtc, PackageLockHash;
        public bool Dirty;
        public int SaveSchema;
    }

    FKBuildInfo(SerializedIdentity value)
    {
        GameVersion = value.GameVersion; Commit = value.Commit; UnityVersion = value.UnityVersion;
        BuildUtc = value.BuildUtc; Dirty = value.Dirty; SaveSchema = value.SaveSchema;
        PackageLockHash = value.PackageLockHash;
    }

    public static FKBuildInfo Current
    {
        get
        {
            if (current != null) return current;
#if !UNITY_EDITOR
            var text = Resources.Load<TextAsset>(ResourcePath);
            if (text != null && TryParse(text.text, out var packaged)) return current = packaged;
#endif
            // Editor ignores metadata left by an interrupted build: it identifies the current editor session.
            return current = Create(Application.version, "unversioned", Application.unityVersion,
                string.Empty, true, SaveSystem.CurrentSaveVersion, string.Empty);
        }
    }

    public static FKBuildInfo Create(string gameVersion, string commit, string unityVersion,
        string buildUtc, bool dirty, int saveSchema, string packageLockHash)
    {
        return new FKBuildInfo(new SerializedIdentity {
            GameVersion = Bounded(gameVersion, 128), Commit = Bounded(commit, 64),
            UnityVersion = Bounded(unityVersion, 64), BuildUtc = Bounded(buildUtc, 64), Dirty = dirty,
            SaveSchema = Math.Max(0, saveSchema), PackageLockHash = Bounded(packageLockHash, 64)
        });
    }

    public string ToJson(bool prettyPrint = true) => JsonUtility.ToJson(new SerializedIdentity {
        GameVersion = GameVersion, Commit = Commit, UnityVersion = UnityVersion, BuildUtc = BuildUtc,
        Dirty = Dirty, SaveSchema = SaveSchema, PackageLockHash = PackageLockHash
    }, prettyPrint);

    public static bool TryParse(string json, out FKBuildInfo result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumJsonCharacters) return false;
        try
        {
            var value = JsonUtility.FromJson<SerializedIdentity>(json);
            if (value == null || string.IsNullOrWhiteSpace(value.GameVersion)
                || string.IsNullOrWhiteSpace(value.UnityVersion) || string.IsNullOrWhiteSpace(value.Commit)
                || value.SaveSchema < 0) return false;
            result = Create(value.GameVersion, value.Commit, value.UnityVersion, value.BuildUtc,
                value.Dirty, value.SaveSchema, value.PackageLockHash);
            return true;
        }
        catch (Exception) { return false; }
    }

    static string Bounded(string value, int maximum)
        => string.IsNullOrEmpty(value) ? string.Empty : value.Length <= maximum ? value : value.Substring(0, maximum);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession() => current = null;
}
