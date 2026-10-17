#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

/// <summary>
/// Observes ordinary production matches. Install this file in Assets/Editor only in UnityValidation.
/// -executeMethod R1ProductionPlaytest.Run (without -quit).
/// FK_R1_SEEDS=161803,271828,314159; FK_R1_WALL_SECONDS=1200; FK_R1_OUTPUT=absolute directory.
/// No fixture, timer/AP/resource override, forced GC, timeScale or AI budget change is used.
/// Editor-wide profiler/objects/process measurements include the observer and are not AI-exclusive.
/// The external Editor -logFile remains authoritative for startup/shutdown/native diagnostics.
/// </summary>
[InitializeOnLoad]
public static class R1ProductionPlaytest
{
    const string Key = "FK.R1ProductionPlaytest";
    const string Prefix = "[R1ProductionPlaytest]";
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    static readonly object LogLock = new object();
    static readonly string[] NativeWarningPatterns = { "JobTempAlloc", "TLS Allocator", "ALLOC_TEMP", "Native Collection", "NativeArray", "NativeLeakDetection", "Leak Detected", "memory leak", "Internal: deleting an allocation" };
    static Config config;
    static BatchReport batch;
    static MatchReport match;
    static TurnGenerator turn;
    static SideObserver player, enemy;
    static StreamWriter snapshots, actions, diagnostics;
    static ProfilerRecorder frameAllocation;
    static bool ready, clicked, finishing, logWriteFailed;
    static int lastFrame = -1, sampledOwnerTurns = -1, frameSamples;
    static int warnings, errors, exceptions, assertions, nativeWarnings, allLogs;
    static int gc0, gc1, gc2;
    static long frameAllocatedBytes;
    static double started, lastProgress, lastSnapshot, frameMilliseconds, maxFrameMilliseconds;
    static string folder, previousProgress;

    [Serializable] sealed class Config
    {
        public int[] Seeds;
        public int WallSeconds, StallSeconds, RequiredOwnerTurns, ThreatLevel;
        public string Output, RunId, StartedUtc, OriginalCompany, OriginalProduct;
    }
    [Serializable] sealed class BatchReport
    {
        public string StartedUtc, FinishedUtc, Output;
        public bool ObservationRunFinished, ThreeResolvedMatchesReached, OwnerTurnTargetReached;
        public int RequestedMatches, ObservedMatches, ResolvedMatches, InterruptedMatches, FaultedMatches;
        public int CompletedOwnerTurns, MaximumSameCommanderOwnerTurns, MaximumMatchOwnerTurns;
        public List<MatchReport> Matches = new List<MatchReport>();
        public string MeasurementScope = "Ordinary SampleScene / production assets / title New Game / developer player AI vs ordinary enemy AI. Frame allocations, profiler memory, loaded Unity Objects and process memory include Editor, UI and observer. No forced GC. Managed diagnostic capture covers observation only; inspect the external Editor log for startup, domain unload, shutdown and native warnings.";
    }
    [Serializable] sealed class MatchReport
    {
        public int MatchIndex, Seed, Round, PlayerAISeed, EnemyAISeed, ThreatLevel;
        public float TerrainSeedX, TerrainSeedZ, InitialTurnTimeLimit, InitialPlayerTime, InitialEnemyTime, InitialTimeScale;
        public string StartedUtc, FinishedUtc, EndCondition = "running", Result = "unresolved", OutcomeReason = "";
        public string PersistentPath, PlayerPersonality, EnemyPersonality, AIConfigMode, State, Folder, Fault;
        public bool GameOver, PlayerReflectionAvailable, EnemyReflectionAvailable, GCAllocationRecorderValid;
        public int PlayerCompletedOwnerTurns, EnemyCompletedOwnerTurns, CompletedOwnerTurns;
        public int PlayerCommanderSkills, EnemyCommanderSkills, PlayerObservedSkills, EnemyObservedSkills;
        public int PlayerSuccessfulSpecialSkills, EnemySuccessfulSpecialSkills, SpecialSkillTypesUsed;
        public long PlayerActionRecordGaps, EnemyActionRecordGaps;
        public int Warnings, Errors, Exceptions, Assertions, NativeWarningMessages, AllLogMessages, MemorySamples;
        public double Seconds;
        public MemorySample FirstMemory, LastMemory;
        public List<SkillCount> Skills = new List<SkillCount>();
    }
    [Serializable] sealed class MemorySample
    {
        public double Seconds;
        public int CompletedOwnerTurns, LoadedUnityObjects, SceneGameObjects, GC0, GC1, GC2;
        public long TrackedAllocatedBytes, MonoUsedBytes, MonoReservedBytes, TrackedReservedBytes;
        public long TrackedUnusedReservedBytes, ManagedGCBytes, ProcessPrivateBytes, ProcessWorkingSetBytes;
    }
    [Serializable] sealed class SkillCount
    {
        public string Team, Ability;
        public int Attempts, Succeeded, Failed;
    }
    [Serializable] sealed class AssetRecord
    {
        public string Path, SHA256, DefinitionId, Kind, Ability, ConfigurationError;
        public bool Enabled, AvailableToPlayer, AvailableToEnemy;
        public int APCost, Cooldown;
    }
    [Serializable] sealed class AssetManifest { public List<AssetRecord> Assets = new List<AssetRecord>(); }

    sealed class SideObserver
    {
        public readonly Team Team;
        public readonly AICommander Commander;
        public readonly AIActionReflectionSystem Reflection;
        public readonly Dictionary<UnitSpecialSkill, SkillCount> Skills = new Dictionary<UnitSpecialSkill, SkillCount>();
        public long LastId, Gaps;
        public int SuccessfulSkills, SuccessfulSpecialSkills, ActionCount;
        public int OwnTurns => Reflection?.OwnTurns ?? 0;
        public SideObserver(Team team, AICommander commander)
        {
            Team = team; Commander = commander; Reflection = commander.Reflection;
            foreach (UnitSpecialSkill ability in Enum.GetValues(typeof(UnitSpecialSkill)))
                if (ability != UnitSpecialSkill.None)
                    Skills.Add(ability, new SkillCount { Team = team.ToString(), Ability = ability.ToString() });
            if (Reflection == null) return;
            // Player commander is created by its first ordinary Tick. Replay its bounded history so
            // first-slice actions are included, then deduplicate callbacks by monotonic action ID.
            Reflection.ActionCompleted += Observe;
            var replay = Reflection.CaptureBattleState()?.RecentRecords;
            if (replay != null)
            {
                replay.Sort((a, b) => (a?.ActionId ?? 0).CompareTo(b?.ActionId ?? 0));
                foreach (var record in replay) Observe(record);
            }
        }
        void Observe(AIActionRecord record)
        {
            if (record == null || record.ActionId <= LastId) return;
            if (record.ActionId > LastId + 1) Gaps += record.ActionId - LastId - 1;
            LastId = record.ActionId; ActionCount++;
            string abilityName = "";
            if (record.ActionType == AIActionType.SkillUse)
            {
                if (record.ExecutionSucceeded) SuccessfulSkills++;
                string key = record.ActionKey ?? "";
                int marker = key.IndexOf(":special", StringComparison.Ordinal);
                if (marker >= 0)
                {
                    string number = key.Substring(marker + 8).Split(':')[0];
                    if (int.TryParse(number, NumberStyles.Integer, Invariant, out int value)
                        && Skills.TryGetValue((UnitSpecialSkill)value, out var count))
                    {
                        abilityName = count.Ability; count.Attempts++;
                        if (record.ExecutionSucceeded) { count.Succeeded++; SuccessfulSpecialSkills++; }
                        else count.Failed++;
                    }
                }
            }
            try
            {
                WriteRow(actions, record.Turn, Team, record.ActionId, record.ActionType, record.ActorRole,
                    record.ActorLifeId, record.TargetLifeId, record.ActionKey, abilityName,
                    record.ExecutionSucceeded, record.FailureReason, record.Outcome?.DamageDealt ?? 0,
                    record.Outcome?.DamageTaken ?? 0);
            }
            catch (Exception error) { match.Fault = "Observer action writer: " + error.Message; }
        }
        public void Dispose() { if (Reflection != null) Reflection.ActionCompleted -= Observe; }
    }

    static R1ProductionPlaytest() { EditorApplication.update += Tick; }

    public static void Run()
    {
        if (Application.dataPath.IndexOf("UnityValidation", StringComparison.OrdinalIgnoreCase) < 0)
            throw new InvalidOperationException("Use an isolated UnityValidation project only.");
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Start the observer from Edit Mode.");
        string runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", Invariant) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        string selectedSeeds = Environment.GetEnvironmentVariable("FK_R1_SEEDS") ?? "161803,271828,314159";
        string[] parts = selectedSeeds.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) throw new ArgumentException("FK_R1_SEEDS must contain at least three seeds.");
        int[] seeds = Array.ConvertAll(parts, value => int.Parse(value, Invariant));
        config = new Config { Seeds = seeds, RunId = runId, StartedUtc = DateTime.UtcNow.ToString("o"),
            Output = Path.GetFullPath(Environment.GetEnvironmentVariable("FK_R1_OUTPUT")
                ?? Path.Combine(Application.dataPath, "../../r1-production-playtest-" + runId)),
            WallSeconds = Setting("FK_R1_WALL_SECONDS", 1200, 60, 21600),
            StallSeconds = Setting("FK_R1_STALL_SECONDS", 180, 30, 1800),
            RequiredOwnerTurns = Setting("FK_R1_MIN_OWNER_TURNS", 100, 1, 1000000),
            ThreatLevel = Setting("FK_R1_THREAT", 15, 1, 100),
            OriginalCompany = PlayerSettings.companyName, OriginalProduct = PlayerSettings.productName };
        Directory.CreateDirectory(config.Output);
        batch = new BatchReport { StartedUtc = config.StartedUtc, Output = config.Output, RequestedMatches = seeds.Length };
        SessionState.SetString(Key + ".Config", JsonUtility.ToJson(config));
        SessionState.SetString(Key + ".Batch", JsonUtility.ToJson(batch));
        SessionState.SetInt(Key + ".Index", 0);
        SessionState.SetString(Key + ".Phase", "prepare");
        SessionState.SetBool(Key, true);
        WriteJson(Path.Combine(config.Output, "config.json"), config);
        WriteJson(Path.Combine(config.Output, "batch.json"), batch);
        WriteManifest();
        Debug.Log(Prefix + " SCHEDULED seeds=" + selectedSeeds + " wallSecondsPerMatch=" + config.WallSeconds + " output=" + config.Output);
    }

    static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || EditorApplication.isCompiling) return;
        try
        {
            if (config == null) config = JsonUtility.FromJson<Config>(SessionState.GetString(Key + ".Config", ""));
            if (batch == null) batch = JsonUtility.FromJson<BatchReport>(SessionState.GetString(Key + ".Batch", ""));
            string phase = SessionState.GetString(Key + ".Phase", "");
            if (phase == "prepare" || phase == "advance")
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (phase == "advance")
                {
                    int index = SessionState.GetInt(Key + ".Index", 0) + 1;
                    SessionState.SetInt(Key + ".Index", index);
                    if (index >= config.Seeds.Length) { FinishBatch(); return; }
                }
                PrepareMatch(); return;
            }
            if (!EditorApplication.isPlaying || finishing) return;
            if (!ready) InitializeObservation();
            Observe();
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            try
            {
                if (ready && match != null && !finishing) { FinishMatch("observer_exception", error.ToString()); return; }
            }
            catch (Exception finalizationError) { error = new AggregateException(error, finalizationError); }
            // A fault in finalization itself must not leave the Editor waiting forever with
            // finishing=true. Keep partial CSVs and a distinct failure artifact.
            try { if (config != null) File.WriteAllText(Path.Combine(config.Output, "harness-failure.txt"), error.ToString()); }
            finally { Cleanup(); RestoreIdentity(); SessionState.SetBool(Key, false); EditorApplication.Exit(1); }
        }
    }

    static void PrepareMatch()
    {
        Cleanup(); ready = clicked = finishing = logWriteFailed = false;
        turn = null; player = enemy = null; previousProgress = null;
        lastFrame = -1; sampledOwnerTurns = -1; frameAllocatedBytes = 0; frameSamples = 0;
        frameMilliseconds = maxFrameMilliseconds = 0;
        int index = SessionState.GetInt(Key + ".Index", 0);
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "FantasyKingdom_R1Production_" + config.RunId + "_" + index;
        SessionState.SetString(Key + ".Phase", "observe");
        SessionState.SetString(Key + ".Started", EditorApplication.timeSinceStartup.ToString("R", Invariant));
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        EditorApplication.EnterPlaymode();
    }

    static void InitializeObservation()
    {
        int index = SessionState.GetInt(Key + ".Index", 0);
        folder = Path.Combine(config.Output, (index + 1).ToString("D2") + "-" + config.Seeds[index]);
        Directory.CreateDirectory(folder);
        started = double.Parse(SessionState.GetString(Key + ".Started", "0"), Invariant);
        lastProgress = lastSnapshot = EditorApplication.timeSinceStartup;
        match = new MatchReport { MatchIndex = index + 1, Seed = config.Seeds[index], Folder = folder,
            StartedUtc = DateTime.UtcNow.ToString("o"), PersistentPath = Application.persistentDataPath,
            ThreatLevel = config.ThreatLevel, InitialTimeScale = Time.timeScale };
        warnings = errors = exceptions = assertions = nativeWarnings = allLogs = 0;
        gc0 = GC.CollectionCount(0); gc1 = GC.CollectionCount(1); gc2 = GC.CollectionCount(2);
        snapshots = Writer("observations.csv"); actions = Writer("actions.csv"); diagnostics = Writer("diagnostics.tsv");
        WriteRow(actions, "round", "team", "actionId", "actionType", "actorRole", "actorLifeId", "targetLifeId", "actionKey", "specialAbility", "executionSucceeded", "failureReason", "damageDealt", "damageTaken");
        diagnostics.WriteLine("utc\ttype\tnativeWarningPattern\tmessage\tstack");
        Application.logMessageReceivedThreaded += Log;
        frameAllocation = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
        match.GCAllocationRecorderValid = frameAllocation.Valid;
        var header = new List<object> { "seconds", "round", "state", "reason", "playerCompletedOwnerTurns", "enemyCompletedOwnerTurns" };
        foreach (string side in new[] { "player", "enemy" })
            foreach (string metric in new[] { "Wood", "Stone", "Water", "Iron", "MagicOre", "Wheat", "Bread", "Citizen", "ResourceCap", "AP", "Units", "UnpaidUnits", "Buildings", "KingHP", "CrystalHP", "Moves", "Attacks", "CommanderSkills", "ObservedSuccessfulSkills", "SuccessfulSpecialSkills", "Builds", "Summons", "TotalTimeRemaining", "SharedMemory", "PositionHistoryEntries", "ReflectionRecords", "ReflectionKnowledge", "CompletedOperations", "LastSearchMaxSliceMs" })
                header.Add(side + metric);
        header.AddRange(new object[] { "trackedAllocatedBytes", "monoUsedBytes", "monoReservedBytes", "trackedReservedBytes", "trackedUnusedReservedBytes", "managedGCBytes", "processPrivateBytes", "processWorkingSetBytes", "loadedUnityObjects", "sceneGameObjects", "gc0SinceStart", "gc1SinceStart", "gc2SinceStart", "gcAllocatedInSampledFrames", "allocationFrameSamples", "meanFrameMs", "maxFrameMs", "warnings", "errors", "exceptions", "assertions", "nativeWarningMessages", "telemetryEnabled", "telemetryEntries" });
        WriteRow(snapshots, header.ToArray());
        ready = true;
        if (!SaveSystem.TrySaveProfile(new SaveSystem.ProfileData { ThreatLevel = config.ThreatLevel }, out string reason))
            throw new InvalidOperationException("Cannot create isolated difficulty profile: " + reason);
        WriteJson(Path.Combine(folder, "match.json"), match);
    }

    static void Observe()
    {
        double now = EditorApplication.timeSinceStartup;
        if (Time.frameCount != lastFrame)
        {
            lastFrame = Time.frameCount; frameSamples++;
            if (frameAllocation.Valid) frameAllocatedBytes += Math.Max(0, frameAllocation.LastValue);
            double duration = Time.unscaledDeltaTime * 1000d;
            frameMilliseconds += duration; maxFrameMilliseconds = Math.Max(maxFrameMilliseconds, duration);
        }
        if (File.Exists(Path.Combine(config.Output, "stop-request.txt"))) { FinishMatch("stop_requested"); return; }
        if (!clicked)
        {
            if (now - started >= config.WallSeconds) { FinishMatch("initialization_wall_limit", "Title initialization did not complete."); return; }
            if (TitleScreenUI.Instance == null || UIBuilder.ScreenCanvas == null) return;
            var button = UIBuilder.ScreenCanvas.transform.Find("TitleScreenOverlay/ButtonArea/はじめからBtn")?.GetComponent<Button>();
            if (button == null) throw new InvalidOperationException("Actual title New Game button was not found.");
            UnityEngine.Random.InitState(match.Seed); button.onClick.Invoke(); clicked = true;
            Debug.Log(Prefix + " NEW_GAME seed=" + match.Seed); return;
        }
        if (turn == null)
        {
            var candidate = UnityEngine.Object.FindFirstObjectByType<TurnGenerator>();
            if (candidate != null && candidate.Systems.AICommander != null && candidate.CurrentState is PlayerMove)
            {
                turn = candidate; turn.MarkDeveloperMatch();
                enemy = new SideObserver(Team.Enemy, turn.Systems.AICommander);
                match.InitialTurnTimeLimit = turn.Systems.TimerSystem.TurnTimeLimit;
                match.InitialPlayerTime = turn.Systems.TimerSystem.PlayerTimeRemaining;
                match.InitialEnemyTime = turn.Systems.TimerSystem.EnemyTimeRemaining;
                match.TerrainSeedX = turn.Systems.MapCreate.SeedX; match.TerrainSeedZ = turn.Systems.MapCreate.SeedZ;
                match.EnemyAISeed = enemy.Commander.RandomSeed; match.EnemyPersonality = enemy.Commander.Personality.Major.ToString();
                match.AIConfigMode = AIConfig.Mode.ToString();
                if (enemy.Commander.ThreatLevel.Level != config.ThreatLevel)
                    throw new InvalidOperationException("Enemy did not load the isolated requested difficulty.");
                turn.PlayerAI.SetEnabled(true, false);
                Debug.Log(Prefix + " START seed=" + match.Seed + " enemyAISeed=" + match.EnemyAISeed + " ordinaryBudgets=True ordinaryClock=True");
            }
        }
        if (turn != null && player == null && turn.PlayerAI.Commander != null)
        {
            player = new SideObserver(Team.Player, turn.PlayerAI.Commander);
            match.PlayerAISeed = player.Commander.RandomSeed; match.PlayerPersonality = player.Commander.Personality.Major.ToString();
        }
        if (turn != null)
        {
            if (turn.IsGameOver) { FinishMatch("game_over"); return; }
            if (!turn.PlayerAI.Enabled) { FinishMatch("player_ai_disabled", "Developer player AI unexpectedly stopped before game over."); return; }
            string progress = turn.Context.Turn + "|" + turn.CurrentState?.GetType().Name + "|" + player?.LastId + "|" + enemy?.LastId + "|" + player?.OwnTurns + "|" + enemy?.OwnTurns;
            if (progress != previousProgress) { previousProgress = progress; lastProgress = now; }
            int owners = (player?.OwnTurns ?? 0) + (enemy?.OwnTurns ?? 0);
            if (owners != sampledOwnerTurns || now - lastSnapshot >= 5)
            {
                Snapshot("progress"); sampledOwnerTurns = owners; lastSnapshot = now;
                if (owners > 0 && owners % 20 == 0)
                    Debug.Log(Prefix + " PROGRESS seed=" + match.Seed + " round=" + turn.Context.Turn + " completedOwnerTurns=" + owners + " specialSkills=" + (player?.SuccessfulSpecialSkills ?? 0) + "/" + (enemy?.SuccessfulSpecialSkills ?? 0));
            }
        }
        if (now - started >= config.WallSeconds) { FinishMatch("wall_limit"); return; }
        if (now - lastProgress >= config.StallSeconds) FinishMatch(turn == null ? "initialization_stalled" : "progress_stalled", "No observed phase, action or completed owner-turn progress for " + config.StallSeconds + " seconds.");
        if (logWriteFailed) throw new IOException("Diagnostic log writer failed.");
        if (!string.IsNullOrEmpty(match.Fault)) throw new IOException(match.Fault);
    }

    static void Snapshot(string reason)
    {
        if (turn == null) return;
        UpdateReport();
        var memory = ReadMemory();
        match.MemorySamples++; if (match.FirstMemory == null) match.FirstMemory = memory; match.LastMemory = memory;
        var row = new List<object> { memory.Seconds, turn.Context.Turn, turn.CurrentState?.GetType().Name, reason, player?.OwnTurns ?? 0, enemy?.OwnTurns ?? 0 };
        AppendSide(row, Team.Player, player); AppendSide(row, Team.Enemy, enemy);
        row.AddRange(new object[] { memory.TrackedAllocatedBytes, memory.MonoUsedBytes, memory.MonoReservedBytes, memory.TrackedReservedBytes, memory.TrackedUnusedReservedBytes, memory.ManagedGCBytes, memory.ProcessPrivateBytes, memory.ProcessWorkingSetBytes, memory.LoadedUnityObjects, memory.SceneGameObjects, memory.GC0, memory.GC1, memory.GC2, frameAllocation.Valid ? frameAllocatedBytes : -1, frameSamples, frameSamples == 0 ? 0 : frameMilliseconds / frameSamples, maxFrameMilliseconds, warnings, errors, exceptions, assertions, nativeWarnings, CombatRuleTelemetry.Enabled, CombatRuleTelemetry.Count });
        WriteRow(snapshots, row.ToArray()); snapshots.Flush(); actions.Flush();
        lock (LogLock) diagnostics.Flush();
        frameAllocatedBytes = 0; frameSamples = 0; frameMilliseconds = maxFrameMilliseconds = 0;
        WriteJson(Path.Combine(folder, "match.json"), match);
    }

    static void AppendSide(List<object> row, Team team, SideObserver observer)
    {
        var s = turn.Systems; var resource = s.FactionState.GetResources(team);
        int alive = 0, unpaid = 0, kingHP = 0, buildings = 0;
        var parent = team == Team.Player ? s.UnitSetting.PlayerUnit : s.UnitSetting.EnemyUnit;
        foreach (var unit in parent.GetComponentsInChildren<Status>(true))
        {
            if (unit.kind == Kind.King) kingHP += Math.Max(0, unit.HP);
            if (unit.IsAlive && unit.type == Type.Unit) { alive++; if (unit.UpkeepUnpaidTurns > 0) unpaid++; }
        }
        var buildingParent = s.BuildSystem.GetBuildingParent(team);
        if (buildingParent != null)
            foreach (var building in buildingParent.GetComponentsInChildren<Status>(true)) if (building.IsAlive) buildings++;
        int crystalHP = 0;
        var crystals = team == Team.Player ? s.CrystalSystem.Playercrystal : s.CrystalSystem.Enemycrystal;
        if (crystals != null)
            foreach (var crystal in crystals.GetComponentsInChildren<Status>(true)) if (crystal.kind == Kind.Crystal) crystalHP += Math.Max(0, crystal.HP);
        var commander = observer?.Commander;
        var counts = commander?.GetRuntimeCounts();
        row.AddRange(new object[] { resource.Wood, resource.Stone, resource.Water, resource.Iron, resource.MagicOre, resource.Wheat, resource.Bread, resource.Citizen, s.FactionState.GetResourceCap(team), s.FactionState.GetAP(team), alive, unpaid, buildings, kingHP, crystalHP, commander?.SaveTotalMoves ?? 0, commander?.SaveTotalAttacks ?? 0, commander?.SaveTotalSkills ?? 0, observer?.SuccessfulSkills ?? 0, observer?.SuccessfulSpecialSkills ?? 0, commander?.SaveTotalBuilds ?? 0, commander?.SaveTotalSummons ?? 0, team == Team.Player ? s.TimerSystem.PlayerTimeRemaining : s.TimerSystem.EnemyTimeRemaining, counts?.SharedMemory ?? 0, counts?.PositionHistoryEntries ?? 0, counts?.ReflectionActions ?? 0, counts?.ReflectionKnowledge ?? 0, counts?.CompletedOperations ?? 0, commander?.LastSearchMaxSliceMs ?? 0 });
    }

    static MemorySample ReadMemory()
    {
        long privateBytes = -1, workingSet = -1;
        using (var process = Process.GetCurrentProcess())
        {
            try { process.Refresh(); privateBytes = process.PrivateMemorySize64; workingSet = process.WorkingSet64; }
            catch (System.ComponentModel.Win32Exception) { }
        }
        return new MemorySample { Seconds = EditorApplication.timeSinceStartup - started,
            CompletedOwnerTurns = (player?.OwnTurns ?? 0) + (enemy?.OwnTurns ?? 0),
            TrackedAllocatedBytes = Profiler.GetTotalAllocatedMemoryLong(), MonoUsedBytes = Profiler.GetMonoUsedSizeLong(),
            MonoReservedBytes = Profiler.GetMonoHeapSizeLong(), TrackedReservedBytes = Profiler.GetTotalReservedMemoryLong(),
            TrackedUnusedReservedBytes = Profiler.GetTotalUnusedReservedMemoryLong(), ManagedGCBytes = GC.GetTotalMemory(false),
            ProcessPrivateBytes = privateBytes, ProcessWorkingSetBytes = workingSet,
            LoadedUnityObjects = Resources.FindObjectsOfTypeAll<UnityEngine.Object>().Length,
            SceneGameObjects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
            GC0 = GC.CollectionCount(0) - gc0, GC1 = GC.CollectionCount(1) - gc1, GC2 = GC.CollectionCount(2) - gc2 };
    }

    static void UpdateReport()
    {
        match.Round = turn?.Context.Turn ?? 0; match.State = turn?.CurrentState?.GetType().Name ?? "initializing";
        match.Seconds = EditorApplication.timeSinceStartup - started;
        match.PlayerReflectionAvailable = player?.Reflection != null; match.EnemyReflectionAvailable = enemy?.Reflection != null;
        match.PlayerCompletedOwnerTurns = player?.OwnTurns ?? 0; match.EnemyCompletedOwnerTurns = enemy?.OwnTurns ?? 0;
        match.CompletedOwnerTurns = match.PlayerCompletedOwnerTurns + match.EnemyCompletedOwnerTurns;
        match.PlayerCommanderSkills = player?.Commander.SaveTotalSkills ?? 0; match.EnemyCommanderSkills = enemy?.Commander.SaveTotalSkills ?? 0;
        match.PlayerObservedSkills = player?.SuccessfulSkills ?? 0; match.EnemyObservedSkills = enemy?.SuccessfulSkills ?? 0;
        match.PlayerSuccessfulSpecialSkills = player?.SuccessfulSpecialSkills ?? 0; match.EnemySuccessfulSpecialSkills = enemy?.SuccessfulSpecialSkills ?? 0;
        match.PlayerActionRecordGaps = player?.Gaps ?? 0; match.EnemyActionRecordGaps = enemy?.Gaps ?? 0;
        lock (LogLock) { match.Warnings = warnings; match.Errors = errors; match.Exceptions = exceptions; match.Assertions = assertions; match.NativeWarningMessages = nativeWarnings; match.AllLogMessages = allLogs; }
    }

    static void FinishMatch(string condition, string fault = null)
    {
        if (finishing) return; finishing = true;
        match.EndCondition = condition; match.Fault = fault ?? match.Fault;
        var terminal = turn?.CurrentState as GameEndState;
        match.GameOver = terminal != null;
        match.Result = terminal == null ? "unresolved" : typeof(GameEndState).GetField("_result", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(terminal).ToString();
        match.OutcomeReason = terminal?.OutcomeReason ?? "";
        match.FinishedUtc = DateTime.UtcNow.ToString("o");
        if (turn != null) Snapshot(terminal == null ? "interrupted" : "terminal");
        UpdateReport();
        var used = new HashSet<string>();
        foreach (var observer in new[] { player, enemy })
            if (observer != null) foreach (var count in observer.Skills.Values)
            { match.Skills.Add(count); if (count.Succeeded > 0) used.Add(count.Ability); }
        match.SpecialSkillTypesUsed = used.Count;
        using (var skillFile = Writer("skills.csv"))
        {
            WriteRow(skillFile, "team", "ability", "attempts", "succeeded", "failed");
            foreach (var count in match.Skills) WriteRow(skillFile, count.Team, count.Ability, count.Attempts, count.Succeeded, count.Failed);
        }
        Cleanup();
        WriteJson(Path.Combine(folder, "match.json"), match);
        batch.Matches.Add(match); batch.ObservedMatches = batch.Matches.Count;
        if (match.GameOver) batch.ResolvedMatches++; else batch.InterruptedMatches++;
        if (!string.IsNullOrEmpty(match.Fault) || match.Errors + match.Exceptions + match.Assertions > 0) batch.FaultedMatches++;
        batch.CompletedOwnerTurns += match.CompletedOwnerTurns;
        batch.MaximumMatchOwnerTurns = Math.Max(batch.MaximumMatchOwnerTurns, match.CompletedOwnerTurns);
        batch.MaximumSameCommanderOwnerTurns = Math.Max(batch.MaximumSameCommanderOwnerTurns, Math.Max(match.PlayerCompletedOwnerTurns, match.EnemyCompletedOwnerTurns));
        batch.ThreeResolvedMatchesReached = batch.ResolvedMatches >= 3;
        batch.OwnerTurnTargetReached = batch.CompletedOwnerTurns >= config.RequiredOwnerTurns;
        SessionState.SetString(Key + ".Batch", JsonUtility.ToJson(batch));
        WriteJson(Path.Combine(config.Output, "batch.json"), batch);
        Debug.Log(Prefix + " MATCH_END seed=" + match.Seed + " condition=" + condition + " gameOver=" + match.GameOver + " result=" + match.Result + " round=" + match.Round + " ownerTurns=" + match.CompletedOwnerTurns + " seconds=" + match.Seconds.ToString("F1", Invariant) + " specialSkillTypesUsed=" + match.SpecialSkillTypesUsed);
        SessionState.SetString(Key + ".Phase", "advance");
        EditorApplication.ExitPlaymode();
    }

    static void FinishBatch()
    {
        batch.ObservationRunFinished = true; batch.FinishedUtc = DateTime.UtcNow.ToString("o");
        WriteJson(Path.Combine(config.Output, "batch.json"), batch);
        RestoreIdentity(); SessionState.SetBool(Key, false);
        Debug.Log(Prefix + " OBSERVATION_FINISHED matches=" + batch.ObservedMatches + " resolved=" + batch.ResolvedMatches + " interrupted=" + batch.InterruptedMatches + " completedOwnerTurns=" + batch.CompletedOwnerTurns + " faulted=" + batch.FaultedMatches + " output=" + config.Output);
        // Exit zero means the requested observations finished; batch.json separately says whether
        // three real terminal results and the long-observation target were actually reached.
        EditorApplication.Exit(batch.FaultedMatches == 0 ? 0 : 1);
    }

    static void Log(string message, string stack, LogType type)
    {
        lock (LogLock)
        {
            allLogs++;
            bool native = IsNativeWarning(message);
            if (type == LogType.Warning) warnings++;
            if (type == LogType.Error) errors++;
            if (type == LogType.Exception) exceptions++;
            if (type == LogType.Assert) assertions++;
            if (native) nativeWarnings++;
            if (type == LogType.Log && !native) return;
            try { diagnostics?.WriteLine(DateTime.UtcNow.ToString("o") + "\t" + type + "\t" + native + "\t" + Tsv(message) + "\t" + Tsv(stack)); }
            catch { logWriteFailed = true; }
        }
    }
    static bool IsNativeWarning(string text)
    {
        foreach (string word in NativeWarningPatterns)
            if ((text ?? "").IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }
    static void Cleanup()
    {
        Application.logMessageReceivedThreaded -= Log;
        player?.Dispose(); enemy?.Dispose();
        if (frameAllocation.Valid) frameAllocation.Dispose();
        snapshots?.Dispose(); actions?.Dispose();
        lock (LogLock) { diagnostics?.Dispose(); diagnostics = null; }
        snapshots = actions = null;
    }
    static void RestoreIdentity()
    {
        if (config == null) return;
        PlayerSettings.companyName = config.OriginalCompany; PlayerSettings.productName = config.OriginalProduct;
    }
    static StreamWriter Writer(string name) => new StreamWriter(Path.Combine(folder, name), false, new UTF8Encoding(false));
    static int Setting(string name, int fallback, int minimum, int maximum)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out int value) ? Mathf.Clamp(value, minimum, maximum) : fallback;
    static string Tsv(string text) => (text ?? "").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");
    static void WriteJson(string path, object value) => File.WriteAllText(path, JsonUtility.ToJson(value, true), new UTF8Encoding(false));
    static void WriteRow(StreamWriter writer, params object[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (i != 0) writer.Write(',');
            string value = Convert.ToString(values[i], Invariant) ?? "";
            writer.Write('"'); writer.Write(value.Replace("\"", "\"\"")); writer.Write('"');
        }
        writer.WriteLine();
    }

    static void WriteManifest()
    {
        var manifest = new AssetManifest();
        var paths = new HashSet<string>();
        foreach (string filter in new[] { "t:UnitData", "t:SpecialSkillDefinition" })
            foreach (string guid in AssetDatabase.FindAssets(filter)) paths.Add(AssetDatabase.GUIDToAssetPath(guid));
        foreach (string path in Directory.GetFiles("Assets/Resources", "*.asset", SearchOption.AllDirectories)) paths.Add(path.Replace('\\', '/'));
        foreach (string path in paths)
        {
            var record = new AssetRecord { Path = path };
            using (var file = File.OpenRead(path)) using (var hash = SHA256.Create())
                record.SHA256 = BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
            var unit = AssetDatabase.LoadAssetAtPath<UnitData>(path);
            var skill = unit != null ? unit.specialSkill : AssetDatabase.LoadAssetAtPath<SpecialSkillDefinition>(path);
            if (unit != null) { record.DefinitionId = unit.definitionId; record.Kind = unit.kind.ToString(); record.AvailableToPlayer = unit.availableToPlayer; record.AvailableToEnemy = unit.availableToEnemy; }
            if (skill != null) { record.Ability = skill.ability.ToString(); record.Enabled = skill.enabledForPlay; record.APCost = skill.apCost; record.Cooldown = skill.cooldown; record.ConfigurationError = skill.ConfigurationError; }
            manifest.Assets.Add(record);
        }
        manifest.Assets.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        WriteJson(Path.Combine(config.Output, "production-assets.json"), manifest);
    }
}
#endif
