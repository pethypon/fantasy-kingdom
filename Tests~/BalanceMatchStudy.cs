#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Observed real matches: normal title button, fresh difficulty profile, untouched gameplay budgets.</summary>
[InitializeOnLoad]
public static partial class BalanceMatchStudy
{
    const string Running = "FK.BalanceMatchStudy";
    static TurnGenerator turn;
    static bool profileReady, clicked, observing;
    static int seed, maximumTurns, recorded;
    static double started, lastProgress;
    static string folder;
    static string batch;
    static readonly StringBuilder rows = new StringBuilder();
    static BalanceMatchStudy() { EditorApplication.update += Tick; }

    public static void Run() => RunBatch("balance-study-20261014");
    public static void RunAdditional() => RunBatch("balance-study-20261014-repeat");
    public static void RunImproved() => RunBatch("balance-study-20261015-improved");
    public static void RunConfirmed() => RunBatch("balance-study-20261015-confirmed");
    static void RunBatch(string selectedBatch)
    {
        if (!Application.dataPath.Contains("UnityValidation")) throw new InvalidOperationException("Isolated validation project only");
        int value;
        seed = int.TryParse(Environment.GetEnvironmentVariable("FK_BALANCE_SEED"), out value) ? value : 56192;
        maximumTurns = int.TryParse(Environment.GetEnvironmentVariable("FK_BALANCE_TURNS"), out value) ? Mathf.Clamp(value, 60, 300) : 180;
        batch = selectedBatch;
        PlayerSettings.companyName = "CodexValidation";
        // Isolate every persistent subsystem, including the response model and ML weights, not just saves.
        PlayerSettings.productName = "FantasyKingdom_" + batch + "_" + seed + "_" + Guid.NewGuid().ToString("N");
        SessionState.SetInt(Running + ".Seed", seed); SessionState.SetInt(Running + ".Turns", maximumTurns);
        SessionState.SetString(Running + ".Batch", batch);
        SessionState.SetBool(Running, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity"); EditorApplication.EnterPlaymode();
    }

    static void Tick()
    {
        if (!SessionState.GetBool(Running, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (!profileReady)
            {
                seed = SessionState.GetInt(Running + ".Seed", 56192); maximumTurns = SessionState.GetInt(Running + ".Turns", 180);
                batch = SessionState.GetString(Running + ".Batch", "balance-study-20261014");
                folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../" + batch + "/" + seed));
                Directory.CreateDirectory(folder); started = lastProgress = EditorApplication.timeSinceStartup;
                if (!SaveSystem.TrySaveProfile(new SaveSystem.ProfileData { ThreatLevel = 15 }, out var reason)) throw new InvalidOperationException(reason);
                profileReady = true;
            }
            if (EditorApplication.timeSinceStartup - started > 1800) { Finish("wall_limit_30_minutes"); return; }
            if (File.Exists(Path.Combine(folder, "stop-request.txt"))) { Finish("investigation_stop_requested"); return; }
            if (EditorApplication.timeSinceStartup - lastProgress > 120 && turn != null) { Finish("turn_progress_stalled_120_seconds"); return; }
            if (!clicked)
            {
                if (TitleScreenUI.Instance == null || UIBuilder.ScreenCanvas == null) return;
                var button = UIBuilder.ScreenCanvas.transform.Find("TitleScreenOverlay/ButtonArea/はじめからBtn")?.GetComponent<Button>();
                if (button == null) throw new InvalidOperationException("Actual new-game button missing");
                UnityEngine.Random.InitState(seed); button.onClick.Invoke(); clicked = true;
                Debug.Log("[BalanceStudy] CLICKED actual はじめから button seed=" + seed); return;
            }
            if (turn == null)
            {
                var candidate = UnityEngine.Object.FindFirstObjectByType<TurnGenerator>();
                if (candidate == null || candidate.Systems.AICommander == null || !(candidate.CurrentState is PlayerMove)) return;
                turn = candidate; turn.MarkDeveloperMatch(); turn.PlayerAI.SetEnabled(true, true);
            }
            // The normal controller creates its commander on its first gameplay Tick.
            if (!observing)
            {
                if (turn.PlayerAI.Commander == null) return;
                if (turn.Systems.AICommander.ThreatLevel.Level != 15 || turn.PlayerAI.Commander.ThreatLevel.Level != 15)
                    throw new InvalidOperationException("Both commanders must start at threat 15");
                var overlay = UIBuilder.ScreenCanvas.transform.Find("TitleScreenOverlay");
                if (overlay != null && overlay.gameObject.activeInHierarchy) throw new InvalidOperationException("Title overlay still covers gameplay");
                rows.Append("turn,phase");
                foreach (string side in new[] { "player", "enemy" })
                    foreach (string metric in Metrics) rows.Append(',').Append(side).Append(metric);
                rows.Append(",thirdUnits,thirdIP,pressureDamageEventsSinceSnapshot,pressureObjectiveDamageSinceSnapshot");
                rows.AppendLine();
                observing = true;
                WriteMetadata(); Debug.Log("[BalanceStudy] START seed=" + seed + " maxTurns=" + maximumTurns + " enemyAISeed=" + turn.Systems.AICommander.RandomSeed);
            }
            if (turn.IsGameOver) { Record("terminal"); WriteDiagnosis("terminal"); Capture("terminal"); Finish("game_over"); return; }
            if (turn.CurrentState is PlayerMove && turn.Context.Turn != recorded)
            {
                recorded = turn.Context.Turn; lastProgress = EditorApplication.timeSinceStartup;
                Record("player_start");
                if (recorded <= 10 || recorded % 10 == 0) WriteDiagnosis("player_start");
                if (recorded == 1 || recorded == 5 || recorded == 20 || recorded % 40 == 0) Capture("turn-" + recorded);
                if (recorded % 10 == 0) Debug.Log("[BalanceStudy] PROGRESS seed=" + seed + " turn=" + recorded
                    + " attacks=" + turn.PlayerAI.Commander.SaveTotalAttacks + "/" + turn.Systems.AICommander.SaveTotalAttacks
                    + " crystalHP=" + HP(turn.Systems.CrystalSystem.Playercrystal) + "/" + HP(turn.Systems.CrystalSystem.Enemycrystal));
                if (recorded >= maximumTurns) Finish("turn_limit");
            }
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            if (folder != null) File.WriteAllText(Path.Combine(folder, "failure.txt"), error.ToString());
            SessionState.SetBool(Running, false); EditorApplication.Exit(1);
        }
    }

    static readonly string[] Metrics = { "Wood", "Stone", "Water", "Iron", "Magic", "Wheat", "Bread", "Citizen", "Storage", "AP",
        "Units", "Scouts", "Unpaid", "Levels", "Buildings", "BuildingLevels", "KingHP", "CrystalHP", "Explored", "VisibleOpponents",
        "Moves", "Attacks", "Skills", "Builds", "Summons", "Damage", "Losses", "Mode", "Economy", "Foundation", "Bottleneck", "BossHP", "DamageTaken", "BreadCoverage", "BreadNet" };
    static void Record(string phase)
    {
        rows.Append(turn.Context.Turn).Append(',').Append(phase);
        AppendSide(Team.Player, turn.PlayerAI.Commander); AppendSide(Team.Enemy, turn.Systems.AICommander);
        var third = turn.Systems.ThirdFactionSystem;
        rows.Append(',').Append(turn.Systems.VisionGenerator.NeutralParent?.GetComponentsInChildren<Status>().Count(u => u.IsAlive && u.type == Type.Unit) ?? 0)
            .Append(',').Append(third?.IP?.Current ?? 0).Append(',').Append(third?.State?.BattleCount ?? 0).Append(',').Append(third?.State?.ObjectiveDamage ?? 0);
        rows.AppendLine();
        File.WriteAllText(Path.Combine(folder, "turns.csv"), rows.ToString());
    }
    static void AppendSide(Team team, AICommander commander)
    {
        var s = turn.Systems; var res = s.FactionState.GetResources(team); bool player = team == Team.Player;
        var units = (player ? s.UnitSetting.PlayerUnit : s.UnitSetting.EnemyUnit).GetComponentsInChildren<Status>().Where(u => u.IsAlive && u.type == Type.Unit).ToArray();
        var opponents = (player ? s.UnitSetting.EnemyUnit : s.UnitSetting.PlayerUnit).GetComponentsInChildren<Status>();
        var buildings = s.BuildSystem.GetBuildingParent(team).GetComponentsInChildren<Status>().Where(u => u.IsAlive).ToArray();
        var king = (player ? s.UnitSetting.PlayerUnit : s.UnitSetting.EnemyUnit).GetComponentsInChildren<Status>(true).FirstOrDefault(u => u.kind == Kind.King);
        var governor = commander.StrategicGovernor; var stats = MatchStats.Instance;
        object[] values = { res.Wood, res.Stone, res.Water, res.Iron, res.MagicOre, res.Wheat, res.Bread, res.Citizen, s.FactionState.GetResourceCap(team), s.FactionState.GetAP(team),
            units.Length, units.Count(u => u.kind == Kind.Scout), units.Count(u => u.UpkeepUnpaidTurns > 0), units.Sum(u => (long)u.Level),
            buildings.Length, buildings.Sum(u => (long)u.Level), king == null ? -1 : king.HP, HP(player ? s.CrystalSystem.Playercrystal : s.CrystalSystem.Enemycrystal),
            (player ? s.VisionGenerator.PlayerExplored : s.VisionGenerator.EnemyExplored).Count,
            opponents.Count(u => u.IsAlive && u.type == Type.Unit && s.VisionGenerator.IsInVisionXZ(team, u.transform.position)),
            commander.SaveTotalMoves, commander.SaveTotalAttacks, commander.SaveTotalSkills, commander.SaveTotalBuilds, commander.SaveTotalSummons,
            stats == null ? 0 : player ? stats.PlayerDamageDealt : stats.EnemyDamageDealt, stats == null ? 0 : player ? stats.PlayerLosses : stats.EnemyLosses,
            governor.Mode, governor.Economy.State, governor.BasicResources.FoundationState, governor.BasicResources.PrimaryBottleneck,
            units.Where(u => u.kind == Kind.Boss).Select(u => u.HP).DefaultIfEmpty(-1).First(),
            stats == null ? 0 : player ? stats.PlayerDamageTaken : stats.EnemyDamageTaken, governor.Economy.BreadCoverageTurns, governor.Economy.NetBreadPerTurn };
        foreach (object value in values) rows.Append(',').Append(Convert.ToString(value, CultureInfo.InvariantCulture));
    }
    static int HP(Transform parent) => parent == null ? 0 : parent.GetComponentsInChildren<Status>(true).Where(u => u.kind == Kind.Crystal).Select(u => u.HP).FirstOrDefault();
    static void Capture(string label)
    {
        string name = batch + "-" + seed + "-" + label + ".png";
        VisualRegressionTests.CaptureHUD(name);
        string source = Path.GetFullPath(Path.Combine(Application.dataPath, "../../visual-captures/" + name));
        File.Copy(source, Path.Combine(folder, label + ".png"), true);
    }
    [Serializable] sealed class Metadata
    {
        public int MapRandomInput, MaxTurns, EnemyAISeed, PlayerAISeed, ThreatLevel;
        public float TerrainSeedX, TerrainSeedZ;
        public int MinimumChanceReserveSources;
        public float ChanceReserveCredit;
        public string EnemyPersonality, PlayerPersonality, PersistentPath, EndCondition, Result, OutcomeReason;
        public string LastNormalAttackerTeam, LastNormalAttackerKind, LastNormalAttackerName;
        public int LastRound; public bool GameOver; public double Seconds;
    }
    static void WriteMetadata(string end = "running")
    {
        var enemy = turn.Systems.AICommander; var player = turn.PlayerAI.Commander; var terminal = turn.CurrentState as GameEndState;
        var attacker = turn.Systems.BattleSystem.Attacker;
        var meta = new Metadata { MapRandomInput = seed, MaxTurns = maximumTurns, EnemyAISeed = enemy.RandomSeed, PlayerAISeed = player.RandomSeed, ThreatLevel = enemy.ThreatLevel.Level,
            TerrainSeedX = turn.Systems.MapCreate.SeedX, TerrainSeedZ = turn.Systems.MapCreate.SeedZ, EnemyPersonality = enemy.Personality.Major.ToString(), PlayerPersonality = player.Personality.Major.ToString(),
            MinimumChanceReserveSources = AIEconomySettings.Active.minimumChanceReserveSources,
            ChanceReserveCredit = AIEconomySettings.Active.chanceReserveCredit,
            PersistentPath = Application.persistentDataPath, EndCondition = end, Result = terminal == null ? "unresolved" : typeof(GameEndState).GetField("_result", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(terminal).ToString(),
            OutcomeReason = terminal?.OutcomeReason ?? "", LastRound = turn.Context.Turn, GameOver = turn.IsGameOver, Seconds = EditorApplication.timeSinceStartup - started,
            LastNormalAttackerTeam = terminal == null || attacker == null ? "" : attacker.team.ToString(),
            LastNormalAttackerKind = terminal == null || attacker == null ? "" : attacker.kind.ToString(),
            LastNormalAttackerName = terminal == null || attacker == null ? "" : attacker.name };
        File.WriteAllText(Path.Combine(folder, "match.json"), JsonUtility.ToJson(meta, true));
    }
    static void Finish(string condition)
    {
        if (turn == null) throw new TimeoutException("Game initialization did not complete");
        if (condition != "game_over" && observing) { WriteDiagnosis(condition); Capture("last-frame"); }
        WriteMetadata(condition);
        Debug.Log("[BalanceStudy] FINISHED seed=" + seed + " condition=" + condition + " round=" + turn.Context.Turn + " seconds=" + (EditorApplication.timeSinceStartup - started).ToString("F1"));
        SessionState.SetBool(Running, false); EditorApplication.Exit(0);
    }
}
#endif
