#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bounded ordinary AI-mode observation; copy to Assets/Editor in UnityValidation only.
/// -executeMethod AIClosingPlaytest.Run, without -quit. No AP/resource/map/rules/search-budget override.
/// FK_CLOSING_SEEDS=161803,271828 FK_CLOSING_OWNER_TURNS=120 FK_CLOSING_WALL_SECONDS=300
/// FK_CLOSING_OUTPUT=absolute directory FK_CLOSING_LABEL=baseline|adjusted.
/// Unlike the production initializer's clock seed, this observer supplies a fixed enemy commander seed
/// before either AI acts. A bounded interruption is explicitly unresolved, never a fabricated victory.
/// </summary>
[InitializeOnLoad]
public static class AIClosingPlaytest
{
    const string Key = "FK.AIClosingPlaytest";
    const string Prefix = "[AIClosingPlaytest]";
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    [Serializable] sealed class Settings
    {
        public int[] Seeds;
        public int OwnerTurnLimit, WallSeconds, ThreatLevel;
        public string Output, Label, RunId, OriginalCompany, OriginalProduct, EnemyPersonality;
    }
    [Serializable] sealed class RunReport
    {
        public string Label, StartedUtc, FinishedUtc;
        public string MeasurementScope = "Ordinary SampleScene and production roster. Title New Game and developer player AI (fixed intelligence 15). Enemy threat/personality are recorded per match. Fixed map and commander seeds; no AP, resources, timer, timeScale or thinking-budget overrides. Frame scheduling/search limits can still affect exact trajectories. Observer reads authoritative HP/decisive identity for reporting only, never supplies hidden information to AI. CommanderHP follows MatchObjectiveRules.IsCommander, including Kind.Boss primary commanders. Legacy KingHP fields alias CommanderHP. VictoryObjectiveAttacks match actual opposing commander/main-crystal life IDs, not the Reflection role string Boss.";
        public int Resolved, Interrupted, Faulted;
        public List<MatchReport> Matches = new List<MatchReport>();
    }
    [Serializable] sealed class MatchReport
    {
        public int MapSeed, EnemyAISeed, PlayerAISeed, ThreatLevel = 15, Round, OwnerTurns;
        public string EnemyPersonality, PersistentPath, StartedUtc, FinishedUtc, EndCondition, Result = "unresolved", OutcomeReason, Fault;
        public bool ActualPlayerAIModeUsed, GameOver, BarrierEnabled;
        public int BarrierRevision, FrameSamples, HpDamage, BarrierDamage, BarrierOnlyHits, BypassHits, HeavySkills;
        public float P95FrameMs, WorstFrameMs, InitialMemoryMB, FinalMemoryMB, PeakMemoryMB;
        public string CombatRules;
        public SaveSystem.ResourceSaveData PlayerResources, EnemyResources;
        public int HeavyWarriorsSeen, DragonKnightsSeen, PlayerLivingUnits, EnemyLivingUnits;
        public int PlayerMaxUnitLevel, EnemyMaxUnitLevel;
        public int PlayerStarvationTurns, EnemyStarvationTurns;
        public int PlayerUnpaidUnits, EnemyUnpaidUnits, PlayerMaxUnpaidTurns, EnemyMaxUnpaidTurns;
        public int PlayerUnavailableBuildings, EnemyUnavailableBuildings;
        public int PlayerInputBlockedProducers, EnemyInputBlockedProducers, PlayerResourceCapacity, EnemyResourceCapacity;
        public double Seconds;
        public float InitialTimeScale, InitialTurnTimeLimit, TerrainSeedX, TerrainSeedZ;
        public int PlayerKingHP, PlayerCrystalHP, EnemyKingHP, EnemyCrystalHP, Errors, Exceptions, Assertions;
        public int PlayerCommanderHP, EnemyCommanderHP, PlayerCommandersFound, EnemyCommandersFound;
        public string PlayerCommanderKinds, EnemyCommanderKinds;
        public List<string> FirstErrors = new List<string>();
        public SideReport Player = new SideReport { Team = "Player" }, Enemy = new SideReport { Team = "Enemy" };
    }
    [Serializable] sealed class SideReport
    {
        public string Team;
        public int OwnerTurns, Actions, SuccessfulActions, FailedActions, Moves, Attacks, Skills, Builds, Summons;
        public int ExplorationMoves, ObjectiveApproachMoves, ObjectiveDistanceGain, RevealedCells, DamageDealt, EnemyKills;
        public int ObjectiveAttacks, VictoryObjectiveAttacks, CommanderTargetAttacks, MainCrystalTargetAttacks;
        public int RepeatedActions, Oscillations, FailedProgressActions, RecordGaps;
        public int FirstObservedEnemyRound = -1, FirstAttackRound = -1;
        public float AttackRatio, BuildRatio, ExplorationMoveRatio;
    }
    sealed class SideObserver : IDisposable
    {
        public readonly AICommander Commander;
        readonly AIActionReflectionSystem reflection;
        readonly AIActionExecutor observedExecutor;
        readonly SideReport report;
        long lastId;
        public SideObserver(AICommander commander, SideReport target)
        {
            Commander = commander; report = target; reflection = commander.Reflection;
            observedExecutor = typeof(AICommander).GetField("_actionExecutor", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(commander) as AIActionExecutor;
            if (reflection == null) throw new InvalidOperationException("Ordinary AI reflection is unavailable.");
            reflection.ActionCompleted += Observe;
            var records = reflection.CaptureBattleState()?.RecentRecords;
            if (records != null)
            {
                records.Sort((a, b) => (a?.ActionId ?? 0).CompareTo(b?.ActionId ?? 0));
                foreach (var record in records) Observe(record);
            }
        }
        public void Snapshot()
        {
            report.OwnerTurns = reflection.OwnTurns;
            report.AttackRatio = Ratio(report.Attacks, report.SuccessfulActions);
            report.BuildRatio = Ratio(report.Builds, report.SuccessfulActions);
            report.ExplorationMoveRatio = Ratio(report.ExplorationMoves, report.Moves);
        }
        void Observe(AIActionRecord record)
        {
            if (record == null || record.ActionId <= lastId) return;
            if (record.ActionId > lastId + 1) report.RecordGaps += (int)(record.ActionId - lastId - 1);
            lastId = record.ActionId; report.Actions++;
            bool moved = record.Before != null && record.After != null
                && (record.Before.ActorX != record.After.ActorX || record.Before.ActorY != record.After.ActorY);
            var outcome = record.Outcome;
            int gain = outcome?.DistanceToObjectiveDelta ?? 0;
            int revealed = outcome?.NewTilesRevealed ?? 0;
            if (record.ExecutionSucceeded)
            {
                report.SuccessfulActions++;
                if (moved)
                {
                    report.Moves++;
                    if (revealed > 0) report.ExplorationMoves++;
                    if (gain > 0) { report.ObjectiveApproachMoves++; report.ObjectiveDistanceGain += gain; }
                }
                if (record.ActionType == AIActionType.Attack)
                {
                    report.Attacks++;
                    if (report.FirstAttackRound < 0) report.FirstAttackRound = record.Turn;
                    string category = record.TargetCategory ?? "";
                    bool commanderTarget = IsCommanderTarget(report.Team, record.TargetLifeId);
                    bool crystalTarget = IsMainCrystalTarget(report.Team, record.TargetLifeId);
                    if (commanderTarget || crystalTarget || category == "SubCrystal") report.ObjectiveAttacks++;
                    if (commanderTarget || crystalTarget) report.VictoryObjectiveAttacks++;
                    if (commanderTarget) report.CommanderTargetAttacks++;
                    if (crystalTarget) report.MainCrystalTargetAttacks++;
                }
                if (record.ActionType == AIActionType.SkillUse) report.Skills++;
                if (record.ActionType == AIActionType.Build) report.Builds++;
                if (record.ActionType == AIActionType.Summon) report.Summons++;
                report.RevealedCells += revealed; report.DamageDealt += outcome?.DamageDealt ?? 0;
                report.EnemyKills += outcome?.EnemyKills ?? 0;
            }
            else report.FailedActions++;
            if (record.IsRepeatedAction) report.RepeatedActions++;
            if (record.IsOscillation) report.Oscillations++;
            if (!record.MeaningfulProgress) report.FailedProgressActions++;
            if (report.FirstObservedEnemyRound < 0 && (record.Before?.VisibleEnemyCount ?? 0) > 0)
                report.FirstObservedEnemyRound = record.Turn;
            string occupancy = "", destinationVisible = "";
            if (!record.ExecutionSucceeded && observedExecutor?.LastFailureReason == "move_destination_occupied"
                && record.Before != null && Environment.GetEnvironmentVariable("FK_TERRITORY_TELEMETRY") == "1")
            {
                // Reporting only, after execution. Authoritative occupants never flow back into AI decisions.
                var cell = GridHelper.ToGridXZ(observedExecutor.LastRequestedDestination);
                var board = typeof(AICommander).GetField("_board", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(Commander) as AIBoardState;
                destinationVisible = (board?.IsVisibleToEnemy(cell) ?? false).ToString();
                foreach (var actor in UnityEngine.Object.FindObjectsByType<Status>(FindObjectsSortMode.None))
                    if (actor != null && actor.IsAlive && GridHelper.MatchXZ(actor.transform.position, cell))
                        occupancy += actor.team + ":" + actor.type + ":" + actor.kind
                            + ":observed=" + (board?.IsSpecialActorObserved(actor) ?? false) + ";";
            }
            WriteRow(actions, report.Team, record.Turn, record.ActionId, record.ActionType, record.ActorRole,
                record.TargetCategory, record.ExecutionSucceeded, record.FailureReason, moved, revealed, gain,
                outcome?.DamageDealt ?? 0, outcome?.EnemyKills ?? 0, record.Before?.ActorX, record.Before?.ActorY,
                record.After?.ActorX, record.After?.ActorY, record.Before?.Strategy, record.ActionKey,
                IsCommanderTarget(report.Team, record.TargetLifeId), IsMainCrystalTarget(report.Team, record.TargetLifeId),
                record.Before?.ActorAp, record.After?.ActorAp, record.Before?.ActionApCost,
                observedExecutor?.LastFailureReason ?? "not_recorded", observedExecutor?.LastRequestedDestination.x, observedExecutor?.LastRequestedDestination.z,
                destinationVisible, occupancy);
        }
        public void Dispose() => reflection.ActionCompleted -= Observe;
    }
    static Settings settings;
    static RunReport run;
    static MatchReport match;
    static TurnGenerator turn;
    static SideObserver player, enemy;
    static StreamWriter actions, turns;
    static bool initialized, clicked, finishing;
    static readonly List<float> frameTimes = new List<float>(12000);
    static int sampledFrame = -1, lastCaptureRound = -1;
    static readonly HashSet<string> heavyLives = new HashSet<string>(), dragonLives = new HashSet<string>();
    static readonly List<Status> reportActors = new List<Status>(128);
    static SaveSystem.ResourceSaveData Resources(FactionState.ResourceData source) => new SaveSystem.ResourceSaveData
    { Wood=source.Wood,Stone=source.Stone,Iron=source.Iron,MagicOre=source.MagicOre,Water=source.Water,Bread=source.Bread,Wheat=source.Wheat,Citizen=source.Citizen };
    static void OnHpChange(HpChangeResult result)
    {
        if (match == null || finishing) return;
        match.HpDamage += result.ActualHpLoss; match.BarrierDamage += result.ActualBarrierLoss;
        if (result.ActualBarrierLoss > 0 && result.ActualHpLoss == 0) match.BarrierOnlyHits++;
        if (result.Kind == HpChangeKind.BarrierBypassDamage && result.ActualHpLoss > 0) match.BypassHits++;
        if (result.Source != null && result.Source.kind == Kind.HeavyWarrior && result.Cause == HpChangeCause.Skill) match.HeavySkills++;
    }
    static void SampleFrame()
    {
        if (Time.frameCount == sampledFrame) return;
        sampledFrame = Time.frameCount;
        if (frameTimes.Count < 20000) frameTimes.Add(Time.unscaledDeltaTime * 1000);
        float memory = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f);
        match.PeakMemoryMB = Mathf.Max(match.PeakMemoryMB, memory); match.FinalMemoryMB = memory;
    }
    static void CaptureNativeView(string suffix)
    {
        if (Environment.GetEnvironmentVariable("FK_CLOSING_CAPTURE_VISUALS") != "1") return;
        string name = "barrier-ai-" + match.MapSeed + "-" + suffix + ".png";
        VisualRegressionTests.CaptureHUD(name);
        var camera = Camera.main; var oldPosition=camera.transform.position; float oldFov=camera.fieldOfView,oldSize=camera.orthographicSize;
        try
        {
            Vector3 focus=turn.Systems.CrystalSystem.PCP; int found=0;Vector3 sum=Vector3.zero;
            foreach (var actor in turn.Systems.UnitSetting.PlayerUnit.GetComponentsInChildren<Status>())
                if(actor.IsAlive&&!MatchObjectiveRules.IsCommander(actor)){sum+=actor.transform.position;found++;}
            if(found>0)focus=sum/found;
            camera.fieldOfView=45;camera.orthographicSize=9;
            camera.transform.position=focus-camera.transform.forward*18;
            foreach(var head in UnityEngine.Object.FindObjectsByType<UnitHeadUI>(FindObjectsSortMode.None))head.SendMessage("LateUpdate",SendMessageOptions.DontRequireReceiver);
            string closeName="close-"+name; VisualRegressionTests.CaptureHUD(closeName);
            string closeFrom=Path.GetFullPath(Path.Combine(Application.dataPath,"../../visual-captures",closeName));
            if(File.Exists(closeFrom))File.Copy(closeFrom,Path.Combine(folder,closeName),true);
        }
        finally
        {
            camera.transform.position=oldPosition;camera.fieldOfView=oldFov;camera.orthographicSize=oldSize;
            foreach(var head in UnityEngine.Object.FindObjectsByType<UnitHeadUI>(FindObjectsSortMode.None))head.SendMessage("LateUpdate",SendMessageOptions.DontRequireReceiver);
        }
        string from = Path.GetFullPath(Path.Combine(Application.dataPath,"../../visual-captures",name));
        if (File.Exists(from)) File.Copy(from,Path.Combine(folder,name),true);
    }
    static int lastOwners = -1;
    static double started, lastProgress;
    static string progressKey, folder;
    static readonly HashSet<string> playerCommanderIds = new HashSet<string>();
    static readonly HashSet<string> enemyCommanderIds = new HashSet<string>();
    static readonly HashSet<string> playerMainCrystalIds = new HashSet<string>();
    static readonly HashSet<string> enemyMainCrystalIds = new HashSet<string>();

    static AIClosingPlaytest() => EditorApplication.update += Tick;
    public static void Run()
    {
        if (new DirectoryInfo(Path.GetDirectoryName(Application.dataPath)).Name != "UnityValidation")
            throw new InvalidOperationException("Use the isolated UnityValidation project only.");
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start in Edit Mode.");
        string id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", Invariant) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        settings = new Settings { RunId = id,
            Seeds = Array.ConvertAll((Environment.GetEnvironmentVariable("FK_CLOSING_SEEDS") ?? "161803,271828")
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries), x => int.Parse(x, Invariant)),
            OwnerTurnLimit = Setting("FK_CLOSING_OWNER_TURNS", 120, 2, 1000),
            WallSeconds = Setting("FK_CLOSING_WALL_SECONDS", 300, 30, 3600),
            ThreatLevel = Setting("FK_CLOSING_THREAT", 15, 1, 100),
            EnemyPersonality = Environment.GetEnvironmentVariable("FK_CLOSING_PERSONALITY") ?? "",
            Label = Environment.GetEnvironmentVariable("FK_CLOSING_LABEL") ?? "observation",
            Output = Path.GetFullPath(Environment.GetEnvironmentVariable("FK_CLOSING_OUTPUT")
                ?? Path.Combine(Application.dataPath, "../../ai-closing-" + id)),
            OriginalCompany = PlayerSettings.companyName, OriginalProduct = PlayerSettings.productName };
        if (settings.Seeds.Length == 0) throw new ArgumentException("At least one fixed seed is required.");
        Directory.CreateDirectory(settings.Output);
        if (Environment.GetEnvironmentVariable("FK_CLOSING_CAPTURE_VISUALS") == "1") BoardVisualThemeInstaller.RenderPreview();
        run = new RunReport { Label = settings.Label, StartedUtc = DateTime.UtcNow.ToString("o") };
        SessionState.SetString(Key + ".Settings", JsonUtility.ToJson(settings));
        SessionState.SetString(Key + ".Run", JsonUtility.ToJson(run));
        SessionState.SetInt(Key + ".Index", 0); SessionState.SetString(Key + ".Phase", "prepare");
        SessionState.SetBool(Key, true); WriteJson("settings.json", settings); WriteJson("run.json", run);
        Debug.Log(Prefix + " SCHEDULED label=" + settings.Label + " fixedSeeds=" + string.Join(",", settings.Seeds));
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || EditorApplication.isCompiling) return;
        try
        {
            if (settings == null) settings = JsonUtility.FromJson<Settings>(SessionState.GetString(Key + ".Settings", ""));
            if (run == null) run = JsonUtility.FromJson<RunReport>(SessionState.GetString(Key + ".Run", ""));
            string phase = SessionState.GetString(Key + ".Phase", "");
            if (phase == "prepare" || phase == "advance")
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                int index = SessionState.GetInt(Key + ".Index", 0);
                if (phase == "advance") SessionState.SetInt(Key + ".Index", ++index);
                if (index >= settings.Seeds.Length) { Complete(); return; }
                initialized = clicked = finishing = false; turn = null; player = enemy = null; lastOwners = -1;
                PlayerSettings.companyName = "CodexValidation";
                PlayerSettings.productName = "FantasyKingdom_AIClosing_" + settings.RunId + "_" + index;
                SessionState.SetString(Key + ".Phase", "observe");
                SessionState.SetString(Key + ".Started", EditorApplication.timeSinceStartup.ToString("R", Invariant));
                EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity"); EditorApplication.EnterPlaymode(); return;
            }
            if (!EditorApplication.isPlaying || finishing) return;
            if (!initialized) Initialize();
            Observe();
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            if (initialized && !finishing) { Finish("observer_failure", error.ToString()); return; }
            if (settings != null) File.WriteAllText(Path.Combine(settings.Output, "harness-failure.txt"), error.ToString());
            Cleanup(); RestoreIdentity(); SessionState.SetBool(Key, false); EditorApplication.Exit(1);
        }
    }
    static void Initialize()
    {
        int index = SessionState.GetInt(Key + ".Index", 0), seed = settings.Seeds[index];
        folder = Path.Combine(settings.Output, (index + 1).ToString("D2") + "-" + seed); Directory.CreateDirectory(folder);
        started = double.Parse(SessionState.GetString(Key + ".Started", "0"), Invariant); lastProgress = started;
        match = new MatchReport { MapSeed = seed, EnemyAISeed = seed ^ 476393, ThreatLevel = settings.ThreatLevel, StartedUtc = DateTime.UtcNow.ToString("o"),
            PersistentPath = Application.persistentDataPath, InitialTimeScale = Time.timeScale };
        playerCommanderIds.Clear(); enemyCommanderIds.Clear(); playerMainCrystalIds.Clear(); enemyMainCrystalIds.Clear();
        actions = Writer("actions.csv"); turns = Writer("turns.csv");
        WriteRow(actions, "team", "round", "id", "type", "role", "targetCategory", "succeeded", "failureReason", "moved",
            "newTiles", "objectiveDistanceGain", "damageDealt", "kills", "beforeX", "beforeZ", "afterX", "afterZ", "strategy", "actionKey",
            "targetsOpposingCommander", "targetsOpposingMainCrystal", "APBefore", "APAfter", "ActionAPCost", "executorFailure", "requestedX", "requestedZ", "failedDestinationVisible", "reportedPhysicalOccupants");
        WriteRow(turns, "seconds", "round", "state", "ownerTurns", "playerMoves", "enemyMoves", "playerAttacks", "enemyAttacks",
            "playerBuilds", "enemyBuilds", "playerSummons", "enemySummons", "playerKingHP", "playerCrystalHP", "enemyKingHP", "enemyCrystalHP",
            "playerCommanderHP", "enemyCommanderHP", "playerCommandersFound", "enemyCommandersFound",
            "playerWood", "playerStone", "playerIron", "playerMagic", "playerWater", "playerBread", "playerWheat", "playerCitizen",
            "enemyWood", "enemyStone", "enemyIron", "enemyMagic", "enemyWater", "enemyBread", "enemyWheat", "enemyCitizen",
            "playerStarvation", "enemyStarvation", "playerLivingUnits", "enemyLivingUnits",
            "playerUnpaidUnits", "enemyUnpaidUnits", "playerMaxUnpaidTurns", "enemyMaxUnpaidTurns",
            "playerUnavailableBuildings", "enemyUnavailableBuildings", "playerInputBlockedProducers", "enemyInputBlockedProducers",
            "playerResourceCapacity", "enemyResourceCapacity", "playerMaxUnitLevel", "enemyMaxUnitLevel");
        Application.logMessageReceived += Log;
        if (!SaveSystem.TrySaveProfile(new SaveSystem.ProfileData { ThreatLevel = settings.ThreatLevel }, out string reason))
            throw new IOException("Cannot initialize isolated threat profile: " + reason);
        frameTimes.Clear(); heavyLives.Clear(); dragonLives.Clear(); sampledFrame = lastCaptureRound = -1;
        match.InitialMemoryMB = match.FinalMemoryMB = match.PeakMemoryMB = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong()/(1024f*1024f);
        match.BarrierEnabled = BarrierCombatSettings.Active.enabledForPlay; match.BarrierRevision = BarrierCombatSettings.Active.specificationRevision;
        match.CombatRules = "R1 confirmed table; +"+BarrierSystem.GrowthPerLevel+" per level. "
            + SpecialSkillPresentation.BarrierRecoveryDescription();
        SpecialSkillRuntime.HpChangeCommitted += OnHpChange;
        initialized = true;
    }
    static void Observe()
    {
        double now = EditorApplication.timeSinceStartup;
        if (!clicked)
        {
            if (now - started >= settings.WallSeconds) { Finish("initialization_wall_limit"); return; }
            if (TitleScreenUI.Instance == null || UIBuilder.ScreenCanvas == null) return;
            var button = UIBuilder.ScreenCanvas.transform.Find("TitleScreenOverlay/ButtonArea/はじめからBtn")?.GetComponent<Button>();
            if (button == null) throw new InvalidOperationException("Actual title New Game button is unavailable.");
            UnityEngine.Random.InitState(match.MapSeed); button.onClick.Invoke(); clicked = true; return;
        }
        if (turn == null)
        {
            var candidate = UnityEngine.Object.FindFirstObjectByType<TurnGenerator>();
            if (candidate != null && candidate.CurrentState is PlayerMove && candidate.Systems.AICommander != null)
            {
                turn = candidate; turn.MarkDeveloperMatch(); var s = turn.Systems;
                SpecialSkillRuntime.HpChangeCommitted -= OnHpChange; SpecialSkillRuntime.HpChangeCommitted += OnHpChange;
                // The original enemy has not acted: preserve all normal systems and rules, fix only random personality/seed.
                var major = AIPersonality.RandomMajor(match.EnemyAISeed);
                if (!string.IsNullOrEmpty(settings.EnemyPersonality)
                    && !Enum.TryParse(settings.EnemyPersonality, true, out major)) throw new ArgumentException("Unknown enemy personality");
                s.AICommander = new AICommander(turn, s.MoveGenerator, s.AttackGenerator, s.BattleSystem, s.VisionGenerator,
                    s.APSystem, s.UnitSetting, s.CrystalSystem, s.MapCreate, major, s.BuildSystem, s.SummonSystem,
                    s.FactionState, s.SkillSystem, s.SubCrystalSystem, match.ThreatLevel, match.EnemyAISeed, Team.Enemy);
                enemy = new SideObserver(s.AICommander, match.Enemy);
                match.EnemyPersonality = major.ToString(); match.InitialTurnTimeLimit = s.TimerSystem.TurnTimeLimit;
                match.TerrainSeedX = s.MapCreate.SeedX; match.TerrainSeedZ = s.MapCreate.SeedZ;
                ObserveDecisiveActors();
                turn.PlayerAI.SetEnabled(true, false);
                match.ActualPlayerAIModeUsed = turn.PlayerAI.Enabled;
                Debug.Log(Prefix + " ACTUAL_AI_MODE_ENABLED mapSeed=" + match.MapSeed + " enemySeed=" + match.EnemyAISeed
                    + " enemyPersonality=" + major + " threat=" + match.ThreatLevel + " normalRules=True");
            }
        }
        if (turn != null && player == null && turn.PlayerAI.Commander != null)
        {
            player = new SideObserver(turn.PlayerAI.Commander, match.Player); match.PlayerAISeed = player.Commander.RandomSeed;
        }
        if (turn != null)
        {
            SampleFrame();
            if (turn.IsGameOver) { Finish("game_over"); return; }
            if (!turn.PlayerAI.Enabled) { Finish("player_ai_disabled", "Actual player AI stopped before a terminal result."); return; }
            player?.Snapshot(); enemy?.Snapshot();
            int owners = match.Player.OwnerTurns + match.Enemy.OwnerTurns;
            string key = turn.Context.Turn + "|" + turn.CurrentState?.GetType().Name + "|" + match.Player.Actions + "|" + match.Enemy.Actions;
            if (key != progressKey) { progressKey = key; lastProgress = now; }
            if (owners != lastOwners)
            {
                lastOwners = owners; Snapshot();
                if ((turn.Context.Turn == 1 || turn.Context.Turn % 40 == 0) && lastCaptureRound != turn.Context.Turn)
                { lastCaptureRound = turn.Context.Turn; CaptureNativeView("round-"+turn.Context.Turn); }
                if (owners > 0 && owners % 20 == 0) Debug.Log(Prefix + " PROGRESS seed=" + match.MapSeed + " ownerTurns=" + owners
                    + " attacks=" + match.Player.Attacks + "/" + match.Enemy.Attacks + " builds=" + match.Player.Builds + "/" + match.Enemy.Builds);
            }
            if (owners >= settings.OwnerTurnLimit) { Finish("owner_turn_limit"); return; }
        }
        if (now - started >= settings.WallSeconds) { Finish("wall_limit"); return; }
        if (now - lastProgress >= 90) Finish("progress_stalled", "No phase/action progress for 90 seconds.");
    }
    static void Snapshot()
    {
        AITerritoryPlaytestTelemetry.Capture(player?.Commander, folder, turn?.Context.Turn ?? 0);
        AITerritoryPlaytestTelemetry.Capture(enemy?.Commander, folder, turn?.Context.Turn ?? 0);
        player?.Snapshot(); enemy?.Snapshot(); match.OwnerTurns = match.Player.OwnerTurns + match.Enemy.OwnerTurns;
        match.Seconds = EditorApplication.timeSinceStartup - started; match.Round = turn?.Context.Turn ?? 0;
        if (turn != null)
        {
            var s = turn.Systems; ObserveDecisiveActors();
            match.PlayerResources=Resources(s.FactionState.PlayerResources);match.EnemyResources=Resources(s.FactionState.EnemyResources);
            match.PlayerStarvationTurns=s.FactionState.PlayerNation.StarvationCounter;match.EnemyStarvationTurns=s.FactionState.EnemyNation.StarvationCounter;
            match.PlayerLivingUnits=match.EnemyLivingUnits=0;
            match.PlayerMaxUnitLevel=match.EnemyMaxUnitLevel=0;
            match.PlayerUnpaidUnits=match.EnemyUnpaidUnits=match.PlayerMaxUnpaidTurns=match.EnemyMaxUnpaidTurns=0;
            match.PlayerUnavailableBuildings=match.EnemyUnavailableBuildings=0;
            match.PlayerInputBlockedProducers=match.EnemyInputBlockedProducers=0;
            match.PlayerResourceCapacity=s.FactionState.GetResourceCap(Team.Player);
            match.EnemyResourceCapacity=s.FactionState.GetResourceCap(Team.Enemy);
            CombatRegistry.Collect(reportActors);
            foreach(var actor in reportActors)
            {
                if(actor==null||!actor.IsAlive)continue;
                if(actor.type==Type.Building)
                {
                    ObserveBuildingEconomy(actor,s.FactionState);
                    continue;
                }
                if(actor.type!=Type.Unit)continue;
                if(actor.team==Team.Player)match.PlayerLivingUnits++;if(actor.team==Team.Enemy)match.EnemyLivingUnits++;
                if(!MatchObjectiveRules.IsCommander(actor))
                {
                    if(actor.team==Team.Player)match.PlayerMaxUnitLevel=Mathf.Max(match.PlayerMaxUnitLevel,actor.Level);
                    if(actor.team==Team.Enemy)match.EnemyMaxUnitLevel=Mathf.Max(match.EnemyMaxUnitLevel,actor.Level);
                }
                if(actor.UpkeepUnpaidTurns>0)
                {
                    if(actor.team==Team.Player){match.PlayerUnpaidUnits++;match.PlayerMaxUnpaidTurns=Mathf.Max(match.PlayerMaxUnpaidTurns,actor.UpkeepUnpaidTurns);}
                    if(actor.team==Team.Enemy){match.EnemyUnpaidUnits++;match.EnemyMaxUnpaidTurns=Mathf.Max(match.EnemyMaxUnpaidTurns,actor.UpkeepUnpaidTurns);}
                }
                if(actor.kind==Kind.HeavyWarrior&&heavyLives.Count<512)heavyLives.Add(actor.ReflectionLifeId);
                if(actor.kind==Kind.DragonKnight&&dragonLives.Count<512)dragonLives.Add(actor.ReflectionLifeId);
            }
            reportActors.Clear();match.HeavyWarriorsSeen=heavyLives.Count;match.DragonKnightsSeen=dragonLives.Count;
            match.PlayerCommanderHP = CommanderHP(s.UnitSetting.PlayerUnit, Team.Player, out match.PlayerCommandersFound, out match.PlayerCommanderKinds);
            match.EnemyCommanderHP = CommanderHP(s.UnitSetting.EnemyUnit, Team.Enemy, out match.EnemyCommandersFound, out match.EnemyCommanderKinds);
            match.PlayerKingHP = match.PlayerCommanderHP; match.EnemyKingHP = match.EnemyCommanderHP;
            match.PlayerCrystalHP = HP(s.CrystalSystem.Playercrystal, Kind.Crystal); match.EnemyCrystalHP = HP(s.CrystalSystem.Enemycrystal, Kind.Crystal);
        }
        WriteRow(turns, match.Seconds, match.Round, turn?.CurrentState?.GetType().Name, match.OwnerTurns,
            match.Player.Moves, match.Enemy.Moves, match.Player.Attacks, match.Enemy.Attacks, match.Player.Builds, match.Enemy.Builds,
            match.Player.Summons, match.Enemy.Summons, match.PlayerKingHP, match.PlayerCrystalHP, match.EnemyKingHP, match.EnemyCrystalHP,
            match.PlayerCommanderHP, match.EnemyCommanderHP, match.PlayerCommandersFound, match.EnemyCommandersFound,
            match.PlayerResources?.Wood, match.PlayerResources?.Stone, match.PlayerResources?.Iron, match.PlayerResources?.MagicOre,
            match.PlayerResources?.Water, match.PlayerResources?.Bread, match.PlayerResources?.Wheat, match.PlayerResources?.Citizen,
            match.EnemyResources?.Wood, match.EnemyResources?.Stone, match.EnemyResources?.Iron, match.EnemyResources?.MagicOre,
            match.EnemyResources?.Water, match.EnemyResources?.Bread, match.EnemyResources?.Wheat, match.EnemyResources?.Citizen,
            match.PlayerStarvationTurns, match.EnemyStarvationTurns, match.PlayerLivingUnits, match.EnemyLivingUnits,
            match.PlayerUnpaidUnits, match.EnemyUnpaidUnits, match.PlayerMaxUnpaidTurns, match.EnemyMaxUnpaidTurns,
            match.PlayerUnavailableBuildings, match.EnemyUnavailableBuildings, match.PlayerInputBlockedProducers, match.EnemyInputBlockedProducers,
            match.PlayerResourceCapacity, match.EnemyResourceCapacity, match.PlayerMaxUnitLevel, match.EnemyMaxUnitLevel);
        turns.Flush(); actions.Flush(); File.WriteAllText(Path.Combine(folder, "match.json"), JsonUtility.ToJson(match, true), new UTF8Encoding(false));
    }
    static void ObserveBuildingEconomy(Status actor,FactionState factions)
    {
        if(actor.team!=Team.Player&&actor.team!=Team.Enemy)return;
        bool playerSide=actor.team==Team.Player;
        if(!actor.BuildingOperationAvailable)
        {
            if(playerSide)match.PlayerUnavailableBuildings++;else match.EnemyUnavailableBuildings++;
        }
        var recipe=FacilityData.GetLevel(actor,actor.Level);
        var stock=playerSide?factions.PlayerResources:factions.EnemyResources;
        // Current-stock diagnostic only: do not infer that a past production payment failed.
        if(recipe.HasProduction&&actor.BuildingOperationAvailable&&!FacilityData.CanAffordProduction(stock,recipe.Input))
        {
            if(playerSide)match.PlayerInputBlockedProducers++;else match.EnemyInputBlockedProducers++;
        }
    }
    static int HP(Transform parent, Kind kind)
    {
        int result = 0;
        if (parent != null) foreach (var actor in parent.GetComponentsInChildren<Status>(true))
            if (actor != null && actor.kind == kind) result += Math.Max(0, actor.HP);
        return result;
    }
    static int CommanderHP(Transform parent, Team team, out int found, out string kinds)
    {
        int result = 0; found = 0;
        var names = new HashSet<string>();
        if (parent != null) foreach (var actor in parent.GetComponentsInChildren<Status>(true))
            if (actor != null && actor.team == team && MatchObjectiveRules.IsCommander(actor))
            {
                result += Math.Max(0, actor.HP); found++; names.Add(actor.kind.ToString());
            }
        var ordered = new List<string>(names); ordered.Sort(StringComparer.Ordinal); kinds = string.Join("/", ordered);
        return result;
    }
    static void ObserveDecisiveActors()
    {
        if (turn == null) return;
        var s = turn.Systems;
        ObserveIds(s.UnitSetting.PlayerUnit, Team.Player, playerCommanderIds, true);
        ObserveIds(s.UnitSetting.EnemyUnit, Team.Enemy, enemyCommanderIds, true);
        ObserveIds(s.CrystalSystem.Playercrystal, Team.Player, playerMainCrystalIds, false);
        ObserveIds(s.CrystalSystem.Enemycrystal, Team.Enemy, enemyMainCrystalIds, false);
    }
    static void ObserveIds(Transform parent, Team team, HashSet<string> ids, bool commanders)
    {
        if (parent == null) return;
        foreach (var actor in parent.GetComponentsInChildren<Status>(true))
            if (actor != null && actor.team == team && (commanders ? MatchObjectiveRules.IsCommander(actor) : actor.kind == Kind.Crystal))
                ids.Add(actor.ReflectionLifeId);
    }
    static bool IsCommanderTarget(string observingTeam, string targetLifeId)
        => !string.IsNullOrEmpty(targetLifeId)
            && (observingTeam == "Player" ? enemyCommanderIds : playerCommanderIds).Contains(targetLifeId);
    static bool IsMainCrystalTarget(string observingTeam, string targetLifeId)
        => !string.IsNullOrEmpty(targetLifeId)
            && (observingTeam == "Player" ? enemyMainCrystalIds : playerMainCrystalIds).Contains(targetLifeId);
    static void Finish(string condition, string fault = null)
    {
        if (finishing) return; finishing = true;
        var terminal = turn?.CurrentState as GameEndState;
        match.EndCondition = condition; match.Fault = fault; match.GameOver = terminal != null;
        match.Result = terminal == null ? "unresolved" : typeof(GameEndState)
            .GetField("_result", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(terminal).ToString();
        match.OutcomeReason = terminal?.OutcomeReason ?? ""; match.FinishedUtc = DateTime.UtcNow.ToString("o");
        if (turn != null) CaptureNativeView("final");
        frameTimes.Sort(); match.FrameSamples = frameTimes.Count;
        if (frameTimes.Count > 0) { match.P95FrameMs = frameTimes[Mathf.Clamp(Mathf.CeilToInt(frameTimes.Count*.95f)-1,0,frameTimes.Count-1)]; match.WorstFrameMs=frameTimes[frameTimes.Count-1]; }
        Snapshot(); Cleanup(); run.Matches.Add(match);
        if (match.GameOver) run.Resolved++; else run.Interrupted++;
        if (!string.IsNullOrEmpty(fault) || match.Errors + match.Exceptions + match.Assertions > 0) run.Faulted++;
        SessionState.SetString(Key + ".Run", JsonUtility.ToJson(run)); WriteJson("run.json", run);
        Debug.Log(Prefix + " MATCH_END seed=" + match.MapSeed + " condition=" + condition + " result=" + match.Result
            + " ownerTurns=" + match.OwnerTurns + " attacks=" + match.Player.Attacks + "/" + match.Enemy.Attacks);
        SessionState.SetString(Key + ".Phase", "advance"); EditorApplication.ExitPlaymode();
    }
    static void Log(string message, string stack, LogType type)
    {
        if (type == LogType.Error) match.Errors++;
        if (type == LogType.Exception) match.Exceptions++;
        if (type == LogType.Assert) match.Assertions++;
        if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && match.FirstErrors.Count < 12)
            match.FirstErrors.Add(type + ": " + message + "\n" + stack);
    }
    static void Cleanup()
    {
        SpecialSkillRuntime.HpChangeCommitted -= OnHpChange;
        Application.logMessageReceived -= Log; player?.Dispose(); enemy?.Dispose();
        actions?.Dispose(); turns?.Dispose(); actions = turns = null;
    }
    static void Complete()
    {
        run.FinishedUtc = DateTime.UtcNow.ToString("o"); WriteJson("run.json", run); RestoreIdentity(); SessionState.SetBool(Key, false);
        Debug.Log(Prefix + " COMPLETE label=" + run.Label + " resolved=" + run.Resolved + " interrupted=" + run.Interrupted + " faulted=" + run.Faulted);
        EditorApplication.Exit(run.Faulted == 0 ? 0 : 1);
    }
    static void RestoreIdentity()
    {
        if (settings == null) return;
        PlayerSettings.companyName = settings.OriginalCompany; PlayerSettings.productName = settings.OriginalProduct;
    }
    static int Setting(string name, int fallback, int min, int max)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out int value) ? Mathf.Clamp(value, min, max) : fallback;
    static float Ratio(int numerator, int denominator) => denominator > 0 ? (float)numerator / denominator : 0;
    static StreamWriter Writer(string name) => new StreamWriter(Path.Combine(folder, name), false, new UTF8Encoding(false));
    static void WriteJson(string name, object value) => File.WriteAllText(Path.Combine(settings.Output, name), JsonUtility.ToJson(value, true), new UTF8Encoding(false));
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
}
#endif
