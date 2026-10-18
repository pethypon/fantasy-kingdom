#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;

/// <summary>
/// Shutdown controls for -diag-job-temp-memory-leak-validation. No build, forced GC,
/// profiler recording, asset save, or game save is performed. Run in the isolated clone only.
/// Copy this file to Assets/Editor there, and omit -quit because this driver exits itself.
/// Idle: editor startup only. EmptyScene: an unsaved empty scene. PlayOnly: real title
/// new-game command, 20 game frames, normal Play stop, then editor shutdown.
/// </summary>
[InitializeOnLoad]
public static class R1MemoryDiagnostics
{
    const string Key = "FK.R1MemoryDiagnostics.";
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    static int lastFrame = -1;
    static IDisposable saveScope;

    [Serializable]
    public sealed class Sample
    {
        public string phase, utc;
        public int frame, gameObjects, meshes, materials, textures;
        public long unityAllocatedBytes, unityReservedBytes, unityUnusedReservedBytes;
        public long monoUsedBytes, monoHeapBytes, processPrivateBytes, processWorkingSetBytes;
        public bool processMemoryAvailable;
    }

    [Serializable]
    public sealed class Report
    {
        public string mode, unity, projectPath, persistentDataPath, result;
        public bool diagnosticFlagPresent;
        public int playedFrames;
        public string interpretation = "Editor-process snapshots; Unity tracked memory, managed heap, and OS process memory are separate measures. No value alone attributes a JobTempAlloc allocation or proves a leak.";
        public List<Sample> samples = new List<Sample>();
    }

    static R1MemoryDiagnostics() { EditorApplication.update += Tick; }
    public static void Idle() => Begin("idle");
    public static void EmptyScene() => Begin("empty-scene");
    public static void PlayOnly() => Begin("play-only");

    static void Begin(string mode)
    {
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        if (!string.Equals(Path.GetFileName(project), "UnityValidation", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("R1MemoryDiagnostics requires the isolated UnityValidation project.");
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Start diagnostics from edit mode.");
        string output = Environment.GetEnvironmentVariable("FK_MEMORY_DIAGNOSTICS_OUTPUT");
        if (string.IsNullOrEmpty(output)) output = Path.Combine(Path.GetDirectoryName(project), "r1-memory-" + mode + ".json");
        SessionState.SetString(Key + "Output", Path.GetFullPath(output));
        var report = new Report { mode = mode, unity = Application.unityVersion, projectPath = project,
            persistentDataPath = Application.persistentDataPath, result = "Running",
            diagnosticFlagPresent = Array.IndexOf(Environment.GetCommandLineArgs(), "-diag-job-temp-memory-leak-validation") >= 0 };
        SaveReport(report);
        SessionState.SetInt(Key + "Stage", 1);
        SessionState.SetInt(Key + "Frames", 0);
        SessionState.SetFloat(Key + "Started", (float)EditorApplication.timeSinceStartup);
        SessionState.SetFloat(Key + "Ready", (float)EditorApplication.timeSinceStartup + 3);
        SessionState.SetBool(Key + "Running", true);
        Debug.Log("[R1MemoryDiagnostics] START mode=" + mode + " diagnosticFlag=" + report.diagnosticFlagPresent);
        Capture("editor-start");
        if (mode == "empty-scene")
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Capture("empty-scene-open");
        }
        else if (mode == "play-only")
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            SessionState.SetInt(Key + "Stage", 2);
            EditorApplication.EnterPlaymode();
        }
    }

    static void Tick()
    {
        if (!SessionState.GetBool(Key + "Running", false) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        try
        {
            if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Key + "Started", 0) > 180)
                throw new TimeoutException("Memory diagnostic exceeded three minutes.");
            int stage = SessionState.GetInt(Key + "Stage", 0);
            if (stage == 1)
            {
                if (EditorApplication.timeSinceStartup < SessionState.GetFloat(Key + "Ready", 0)) return;
                Capture("editor-idle"); Finish();
            }
            else if (stage == 2 && EditorApplication.isPlaying)
            {
                // Use the real title callback and let GameGenerator.Update run its normal startup.
                var title = TitleScreenUI.Instance;
                if (title == null) return;
                string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                var method = typeof(SaveSystem).GetMethod("UseTestStorageRoot", BindingFlags.Static | BindingFlags.NonPublic);
                saveScope = (IDisposable)method.Invoke(null, new object[] { Path.Combine(Path.GetTempPath(), "FantasyKingdomR1Memory-" + Guid.NewGuid().ToString("N")) });
                var turn = UnityEngine.Object.FindFirstObjectByType<TurnGenerator>();
                turn?.MarkDeveloperMatch(); // Prevents persistent learning during this diagnostic match.
                UnityEngine.Random.InitState(56192);
                typeof(TitleScreenUI).GetMethod("OnNewGame", PrivateInstance).Invoke(title, null);
                SessionState.SetInt(Key + "Stage", 3);
            }
            else if (stage == 3 && EditorApplication.isPlaying)
            {
                var turn = UnityEngine.Object.FindFirstObjectByType<TurnGenerator>();
                if (turn?.CurrentState == null || TitleScreenUI.Instance != null && TitleScreenUI.Instance.IsActive) return;
                Capture("game-started"); lastFrame = Time.frameCount;
                SessionState.SetInt(Key + "Stage", 4);
            }
            else if (stage == 4 && EditorApplication.isPlaying)
            {
                if (lastFrame == Time.frameCount) return;
                lastFrame = Time.frameCount;
                int frames = SessionState.GetInt(Key + "Frames", 0) + 1;
                SessionState.SetInt(Key + "Frames", frames);
                if (frames < 20) return;
                Capture("play-after-20-frames");
                // Keep the test save root through OnDisable/OnDestroy; the Play domain reload
                // clears both this scope and SaveSystem's private override after teardown.
                SessionState.SetInt(Key + "Stage", 5);
                EditorApplication.ExitPlaymode();
            }
            else if (stage == 5 && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Capture("play-stopped");
                SessionState.SetFloat(Key + "Ready", (float)EditorApplication.timeSinceStartup + 3);
                SessionState.SetInt(Key + "Stage", 1);
            }
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            saveScope?.Dispose(); saveScope = null;
            var report = ReadReport(); report.result = "Failed: " + error.Message; SaveReport(report);
            SessionState.SetBool(Key + "Running", false); EditorApplication.Exit(1);
        }
    }

    static void Capture(string phase)
    {
        var sample = new Sample { phase = phase, utc = DateTime.UtcNow.ToString("O"), frame = Time.frameCount,
            unityAllocatedBytes = Profiler.GetTotalAllocatedMemoryLong(),
            unityReservedBytes = Profiler.GetTotalReservedMemoryLong(),
            unityUnusedReservedBytes = Profiler.GetTotalUnusedReservedMemoryLong(),
            monoUsedBytes = Profiler.GetMonoUsedSizeLong(), monoHeapBytes = Profiler.GetMonoHeapSizeLong() };
        ReadProcessMemory(sample);
        // Count after memory sampling so these temporary query arrays do not inflate the snapshot above.
        sample.gameObjects = Resources.FindObjectsOfTypeAll<GameObject>().Length;
        sample.meshes = Resources.FindObjectsOfTypeAll<Mesh>().Length;
        sample.materials = Resources.FindObjectsOfTypeAll<Material>().Length;
        sample.textures = Resources.FindObjectsOfTypeAll<Texture>().Length;
        var report = ReadReport(); report.samples.Add(sample); SaveReport(report);
        Debug.Log("[R1MemoryDiagnostics] SAMPLE " + JsonUtility.ToJson(sample));
    }

    static void ReadProcessMemory(Sample sample)
    {
        try
        {
            using (var process = Process.GetCurrentProcess())
            {
                process.Refresh();
                sample.processPrivateBytes = process.PrivateMemorySize64;
                sample.processWorkingSetBytes = process.WorkingSet64;
                // Unity Mono may supply zeros. Treat them as unavailable; use the external
                // host-process.csv sampler for OS process memory instead of interpreting zeros.
                sample.processMemoryAvailable = sample.processPrivateBytes > 0 && sample.processWorkingSetBytes > 0;
            }
        }
        catch (NotSupportedException) { }
    }

    static Report ReadReport() => JsonUtility.FromJson<Report>(SessionState.GetString(Key + "Report", "{}"));
    static void SaveReport(Report report)
    {
        string json = JsonUtility.ToJson(report, true);
        SessionState.SetString(Key + "Report", json);
        string path = SessionState.GetString(Key + "Output", "");
        if (string.IsNullOrEmpty(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, json);
    }
    static void Finish()
    {
        var report = ReadReport(); report.playedFrames = SessionState.GetInt(Key + "Frames", 0);
        report.result = "Passed"; SaveReport(report);
        SessionState.SetBool(Key + "Running", false);
        Debug.Log("[R1MemoryDiagnostics] COMPLETED mode=" + report.mode + " playedFrames=" + report.playedFrames);
        EditorApplication.Exit(0);
    }
}
#endif
