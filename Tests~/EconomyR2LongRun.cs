#if UNITY_EDITOR
using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Execute with -executeMethod EconomyR2LongRun.Run in the isolated UnityValidation project.
/// Runs ordinary, live AI-versus-AI battles plus a separate 60-turn real-economy endurance fixture.
/// An early battle result is reported as such; endurance turns are never counted as battle turns.
/// </summary>
[InitializeOnLoad]
public static class EconomyR2LongRun
{
    const string Running = "EconomyR2LongRun.Running", Index = "EconomyR2LongRun.Case", Next = "EconomyR2LongRun.Next";
    const string Started = "EconomyR2LongRun.Started", PassedBattles = "EconomyR2LongRun.Battles", PassedEndurance = "EconomyR2LongRun.Endurance";
    const string IgnoredEditorIndexing = "EconomyR2LongRun.IgnoredEditorIndexing";
    const int TargetRounds = 50, EnduranceTurns = 60;
    const double CaseSeconds = 240, TotalSeconds = 780;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly int[] Seeds = { 1701, 2718, 3141 };
    static readonly MajorPersonality[] Majors = { MajorPersonality.Growth, MajorPersonality.Intellect, MajorPersonality.Combat };
    static TurnGenerator turn;
    static GameSystems systems;
    static AICommander player;
    static IEnumerator playerSteps;
    static PlayerMove playerOwner;
    static bool initialized, originalEnabled;
    static AIMode originalMode;
    static int lastFrame = -1, startRound, lastRound;
    static double caseStarted, lastProgress;
    static string runtimeFailure;
    static readonly int[,] stagnant = new int[2, 8];
    static readonly float[,] lastProduction = new float[2, 8];
    static readonly float[,] minimumStock = new float[2, 8];
    static int maxStagnant, battleRounds, caseEditorIndexExceptions;

    static EconomyR2LongRun() { EditorApplication.update += Tick; }

    public static void Run()
    {
        if (Application.dataPath.IndexOf("UnityValidation", StringComparison.OrdinalIgnoreCase) < 0)
            throw new InvalidOperationException("Economy R2 long-run validation requires an isolated UnityValidation project");
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start long-run validation from edit mode");
        SessionState.SetBool(Running, true); SessionState.SetInt(Index, 0); SessionState.SetBool(Next, false);
        SessionState.SetInt(PassedBattles, 0); SessionState.SetInt(PassedEndurance, 0);
        SessionState.SetInt(IgnoredEditorIndexing, 0);
        SessionState.SetFloat(Started, (float)EditorApplication.timeSinceStartup);
        OpenCase();
    }

    static void OpenCase()
    {
        initialized = false; turn = null; systems = null; player = null; playerSteps = null; runtimeFailure = null; lastFrame = -1;
        caseEditorIndexExceptions = 0;
        int i = SessionState.GetInt(Index, 0);
        Debug.Log($"[EconomyR2LongRun] BEGIN case={i + 1}/{Seeds.Length} seed={Seeds[i]} personality={Majors[i]} battleTargetRounds={TargetRounds} enduranceTurns={EnduranceTurns}");
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        EditorApplication.EnterPlaymode();
    }

    static void Tick()
    {
        if (!SessionState.GetBool(Running, false) || EditorApplication.isCompiling) return;
        try
        {
            if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Started, 0) > TotalSeconds)
                throw new TimeoutException("Economy R2 long-run total time budget exceeded");
            if (!EditorApplication.isPlaying)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || !SessionState.GetBool(Next, false)) return;
                SessionState.SetBool(Next, false); int next = SessionState.GetInt(Index, 0) + 1; SessionState.SetInt(Index, next);
                if (next == Seeds.Length)
                {
                    Debug.Log($"[EconomyR2LongRun] ALL PASSED cases={Seeds.Length} actualBattleCases={SessionState.GetInt(PassedBattles, 0)} enduranceCases={SessionState.GetInt(PassedEndurance, 0)} enduranceTurnsPerCase={EnduranceTurns} knownEditorStartupIndexExceptions={SessionState.GetInt(IgnoredEditorIndexing, 0)}");
                    SessionState.SetBool(Running, false); EditorApplication.Exit(0); return;
                }
                OpenCase(); return;
            }
            if (!initialized) { InitializeCase(); return; }
            if (lastFrame == Time.frameCount) return; lastFrame = Time.frameCount;
            if (!string.IsNullOrEmpty(runtimeFailure)) throw new InvalidOperationException("Live battle logged an error: " + runtimeFailure);
            if (EditorApplication.timeSinceStartup - caseStarted > CaseSeconds)
                throw new TimeoutException("Economy R2 battle stalled in " + turn.CurrentState.GetType().Name);
            if (turn.IsGameOver || turn.Context.Turn >= startRound + TargetRounds && turn.CurrentState is PlayerMove)
            {
                FinishCase(); return;
            }
            // Drive the ordinary turn states once per rendered frame. Both commanders execute legal live actions.
            if (turn.CurrentState is PlayerMove move)
            {
                if (playerSteps == null) { playerOwner = move; playerSteps = player.ExecuteTurnSteps(); }
                bool more;
                try { AITurnBudget.Resume(); more = playerSteps.MoveNext(); }
                finally { AITurnBudget.Pause(); }
                if (!more && !turn.IsGameOver && turn.CurrentState == playerOwner)
                {
                    DisposePlayerSteps(); move.ExecuteTurnEnd();
                }
            }
            else turn.CurrentState.Update();
            systems.TimerSystem?.StopTurn();
            if (turn.Context.Turn != lastRound && turn.CurrentState is PlayerMove)
            {
                lastRound = turn.Context.Turn; battleRounds = lastRound - startRound;
                RecordBattleEconomy();
            }
            if (EditorApplication.timeSinceStartup - lastProgress >= 10)
            {
                lastProgress = EditorApplication.timeSinceStartup;
                Debug.Log($"[EconomyR2LongRun] PROGRESS case={SessionState.GetInt(Index, 0) + 1} seed={Seeds[SessionState.GetInt(Index, 0)]} actualBattleRounds={battleRounds} state={turn.CurrentState.GetType().Name} elapsedSeconds={lastProgress - caseStarted:F1} maxStagnantDeficitTurns={maxStagnant}");
            }
        }
        catch (Exception error) { Fail(error); }
    }

    static void InitializeCase()
    {
        var game = UnityEngine.Object.FindFirstObjectByType<GameGenerator>();
        if (game == null || TitleScreenUI.Instance == null) return;
        int i = SessionState.GetInt(Index, 0); UnityEngine.Random.InitState(Seeds[i]);
        typeof(GameGenerator).GetField("_waitingForTitle", Private).SetValue(game, false);
        UnityEngine.Object.DestroyImmediate(TitleScreenUI.Instance.gameObject);
        typeof(GameGenerator).GetMethod("StartGameInit", Private).Invoke(game, new object[] { -1 });
        turn = UnityEngine.Object.FindFirstObjectByType<TurnGenerator>(); systems = turn.Systems;
        systems.TimerSystem.StopTurn(); originalEnabled = turn.enabled; turn.enabled = false; turn.MarkDeveloperMatch();
        originalMode = AIConfig.Mode; AIConfig.Mode = AIMode.PureRule;
        // No catalog or rule assets are edited. The endurance fixture restores all its transient state in finally.
        RunEndurance(systems, Seeds[i], Majors[i], Traits(i));
        SessionState.SetInt(PassedEndurance, SessionState.GetInt(PassedEndurance, 0) + 1);
        systems.AICommander = Commander(Team.Enemy, Majors[i], Seeds[i], Traits(i));
        player = Commander(Team.Player, Majors[(i + 1) % Majors.Length], Seeds[i] + 1, Traits((i + 1) % Majors.Length));
        startRound = lastRound = turn.Context.Turn; battleRounds = maxStagnant = 0;
        Array.Clear(stagnant, 0, stagnant.Length); Array.Clear(lastProduction, 0, lastProduction.Length);
        for (int side = 0; side < 2; side++) for (int resource = 0; resource < 8; resource++) minimumStock[side, resource] = float.PositiveInfinity;
        RecordBattleEconomy(); caseStarted = lastProgress = EditorApplication.timeSinceStartup;
        Application.logMessageReceived += OnLog; initialized = true;
    }

    static AICommander Commander(Team team, MajorPersonality major, int seed, PersonalityTraits traits)
    {
        var ai = new AICommander(turn, systems.MoveGenerator, systems.AttackGenerator, systems.BattleSystem,
            systems.VisionGenerator, systems.APSystem, systems.UnitSetting, systems.CrystalSystem, systems.MapCreate,
            major, systems.BuildSystem, systems.SummonSystem, systems.FactionState, systems.SkillSystem,
            systems.SubCrystalSystem, 15, seed, team);
        typeof(AIPersonality).GetProperty("Traits").SetValue(ai.Personality, traits);
        ai.TurnThinkingBudgetMs = 350; ai.SearchSliceBudgetMs = 3;
        return ai;
    }

    static PersonalityTraits Traits(int index)
    {
        if (index == 0) return new PersonalityTraits { Caution = 20, Command = 20, Obsession = 20, Defense = 20, Tactics = 20, Development = 200 };
        if (index == 1) return new PersonalityTraits { Caution = 20, Command = 20, Obsession = 20, Defense = 200, Tactics = 20, Development = 20 };
        return new PersonalityTraits { Caution = 20, Command = 20, Obsession = 200, Defense = 20, Tactics = 20, Development = 20 };
    }

    /// <summary>60 real economy turns; no SimBoardState or copied resource arithmetic is used.</summary>
    public static void RunEndurance(GameSystems live, int seed, MajorPersonality major, PersonalityTraits traits)
    {
        using (var f = new EconomyR2Tests.Fixture(live))
        {
            UnityEngine.Random.InitState(seed); f.Personality = new AIPersonality(major, traits);
            // A small AP budget keeps the authored five-unit recipes as the legal expansion choices.
            // This fixture measures mandatory growth; ordinary battle cases retain normal growth reservations.
            f.Rules.citizenAP = 0; f.Rules.aiEconomy.plannedDemandWeight = 0; f.State.GetAPData(f.Team).Reset = 3;
            f.Resources.Wood = f.Resources.Bread = 80;
            f.Food(5, 5); var camp = f.Definition(FacilityKind.LoggingCamp, wood: 5); f.Building(camp);
            f.Building(f.Definition(FacilityKind.Quarry, stone: 20)); f.Building(f.Definition(FacilityKind.Mine, iron: 20, magic: 20));
            // Five actual Lv1 units cost Bread/Iron 5. Extra authored wood costs preserve the existing Wood 10 demand.
            var upkeepUnits = new System.Collections.Generic.List<Status>();
            for (int i = 0; i < 5; i++) { var unit = f.Unit(upkeepWood: 2); unit.Level = 1; upkeepUnits.Add(unit); }
            var planner = f.Planner();
            int initialCamps = f.Builder.GetBuildingCount(f.Team, FacilityKind.LoggingCamp), initialBakeries = f.Builder.GetBuildingCount(f.Team, FacilityKind.Bakery);
            int stagnantWood = 0, stagnantBread = 0, maxDeficit = 0, minBread = f.Resources.Bread, minWood = f.Resources.Wood;
            double maxBuildMs = 0; var watch = Stopwatch.StartNew();
            for (int t = 1; t <= EnduranceTurns; t++)
            {
                if (watch.Elapsed.TotalSeconds > 30) throw new TimeoutException("60-turn real-economy endurance exceeded 30 seconds");
                // Army upkeep increases twice; four camps/bakeries are eventually necessary, exercising the old cap too.
                if (t == 20 || t == 40)
                    for (int i = 0; i < 5; i++) { var unit = f.Unit(upkeepWood: 1); unit.Level = 1; upkeepUnits.Add(unit); }
                f.State.GetNation(f.Team).TurnsAlive = 20 + t; f.State.ResetAPForTurn(f.Team);
                var board = f.Board(20 + t); AITurnBudget.Begin(1000); var buildWatch = Stopwatch.StartNew();
                int builds = planner.TryEarlyBuildPhase(board, TurnStrategy.EconomyBuild, 20 + t);
                planner.TryLateBuildPhase(board, builds, 20 + t); buildWatch.Stop(); maxBuildMs = Math.Max(maxBuildMs, buildWatch.Elapsed.TotalMilliseconds);
                f.Economy.ProcessTurn(f.Team); board.Refresh(); board.Governor.Evaluate(board);
                var demand = board.Governor.ProductionDemand; var wood = demand.Get(ResourceKind.Wood); var bread = demand.Get(ResourceKind.Bread);
                stagnantWood = wood.NetPerTurn < -.01f ? stagnantWood + 1 : 0;
                stagnantBread = bread.NetPerTurn < -.01f ? stagnantBread + 1 : 0; maxDeficit = Math.Max(maxDeficit, Math.Max(stagnantWood, stagnantBread));
                minBread = Math.Min(minBread, f.Resources.Bread); minWood = Math.Min(minWood, f.Resources.Wood);
                if (stagnantWood > 5 || stagnantBread > 5 || demand.HasMandatoryPaymentFailure)
                    throw new InvalidOperationException($"ECON-R2-12 seed={seed} endurance T{t} prolonged/unfunded mandatory demand: wood={wood} bread={bread}");
                foreach (var unit in upkeepUnits)
                    if (unit == null || !unit.IsAlive || unit.UpkeepUnpaidTurns != 0)
                        throw new InvalidOperationException($"ECON-R2-12 seed={seed} endurance T{t} upkeep failed for {(unit != null ? unit.kind.ToString() : "destroyed unit")}");
                if (t % 10 == 0)
                    Debug.Log($"[EconomyR2LongRun] ENDURANCE seed={seed} personality={major} turns={t} woodStock={wood.Stock:F1} woodProd={wood.ProductionPerTurn:F1} woodMandatory={wood.MandatoryDemandPerTurn:F1} breadStock={bread.Stock:F1} breadProd={bread.ProductionPerTurn:F1} breadMandatory={bread.MandatoryDemandPerTurn:F1} camps={board.GetBuildingCount(FacilityKind.LoggingCamp)} bakeries={board.GetBuildingCount(FacilityKind.Bakery)}");
            }
            var finalBoard = f.Board(20 + EnduranceTurns);
            EconomyR2Tests.Check($"ECON-R2-12 seed={seed} facility-one-stop regression is absent over 60 turns", initialCamps == 1 && initialBakeries == 1
                && finalBoard.GetBuildingCount(FacilityKind.LoggingCamp) >= 4 && finalBoard.GetBuildingCount(FacilityKind.Bakery) >= 4
                && finalBoard.Governor.ProductionDemand.Get(ResourceKind.Wood).NetPerTurn >= 0 && finalBoard.Governor.ProductionDemand.Get(ResourceKind.Bread).NetPerTurn >= 0);
            EconomyR2Tests.Check($"ECON-R2-12 seed={seed} no nonemergency military diversion", f.MilitaryCount == 0);
            Debug.Log($"[EconomyR2LongRun] ENDURANCE PASSED seed={seed} personality={major} turns={EnduranceTurns} minWood={minWood} minBread={minBread} maxDeficitTurns={maxDeficit} maxBuildMs={maxBuildMs:F2} wallMs={watch.Elapsed.TotalMilliseconds:F2}");
        }
    }

    static void RecordBattleEconomy()
    {
        for (int side = 0; side < 2; side++)
        {
            Team team = side == 0 ? Team.Player : Team.Enemy;
            var b = new AIBoardState(systems.MoveGenerator, systems.AttackGenerator, systems.APSystem, systems.UnitSetting,
                systems.CrystalSystem, systems.VisionGenerator, systems.BuildSystem, systems.SummonSystem, systems.FactionState,
                systems.SubCrystalSystem, turn.Context.Turn, null, team);
            b.MapCreate = systems.MapCreate; b.ReconThreatLevel = 15; b.Governor = new AIStrategicGovernor(); b.Governor.Evaluate(b);
            var demand = b.Governor.ProductionDemand;
            for (int resource = 0; resource < demand.Resources.Length; resource++)
            {
                var state = demand.Resources[resource]; minimumStock[side, resource] = Math.Min(minimumStock[side, resource], state.Stock);
                bool deficit = state.NetPerTurn < -.01f && state.MandatoryDemandPerTurn > 0;
                bool supplyImproved = state.ProductionPerTurn > lastProduction[side, resource] + .01f;
                stagnant[side, resource] = deficit && !supplyImproved ? stagnant[side, resource] + 1 : 0;
                maxStagnant = Math.Max(maxStagnant, stagnant[side, resource]); lastProduction[side, resource] = state.ProductionPerTurn;
                // Unaffordable recovery under enemy pressure is not misreported as a free economic opportunity.
                if (stagnant[side, resource] > 12 && b.AlivePlayerUnits.Count == 0 && b.BuildablePositions.Count > 0
                    && RecoveryAffordable(b, demand, state.Resource))
                    throw new InvalidOperationException($"ECON-R2-12 unaddressed affordable peaceful deficit team={team} resource={state.Resource} turns={stagnant[side, resource]} net={state.NetPerTurn:F1}");
            }
            if (battleRounds % 10 == 0)
                Debug.Log($"[EconomyR2LongRun] BATTLE seed={Seeds[SessionState.GetInt(Index, 0)]} actualBattleRounds={battleRounds} team={team} economicState={demand.State} wood={demand.Get(ResourceKind.Wood).Stock:F1}/{demand.Get(ResourceKind.Wood).NetPerTurn:F1} bread={demand.Get(ResourceKind.Bread).Stock:F1}/{demand.Get(ResourceKind.Bread).NetPerTurn:F1} camps={b.GetBuildingCount(FacilityKind.LoggingCamp)} bakeries={b.GetBuildingCount(FacilityKind.Bakery)}");
        }
    }

    static bool RecoveryAffordable(AIBoardState board, StrategicProductionDemand demand, ResourceKind resource)
    {
        foreach (var kind in demand.RecommendedFacilities)
        {
            if (!board.AffordableBuildings.Contains(kind) || !board.HasUpstreamProducer(kind)) continue;
            var recipe = FacilityData.GetLevel(kind, 1); var output = recipe.Output;
            if (resource == ResourceKind.Wood && output.Wood > recipe.Input.Wood || resource == ResourceKind.Bread && output.Bread > recipe.Input.Bread
                || resource == ResourceKind.Water && output.Water > recipe.Input.Water || resource == ResourceKind.Stone && output.Stone > recipe.Input.Stone
                || resource == ResourceKind.Iron && output.Iron > recipe.Input.Iron || resource == ResourceKind.MagicOre && output.MagicOre > recipe.Input.MagicOre
                || resource == ResourceKind.Wheat && output.Wheat > recipe.Input.Wheat) return true;
        }
        return false;
    }

    static void FinishCase()
    {
        int i = SessionState.GetInt(Index, 0); battleRounds = turn.Context.Turn - startRound;
        EconomyR2Tests.Check("ECON-R2-12 live commanders performed battle actions", systems.AICommander.SaveTurnCount > 0 && player.SaveTurnCount > 0);
        EconomyR2Tests.Check("ECON-R2-12 battle either completed fifty rounds or reached a real terminal state", battleRounds >= TargetRounds || turn.IsGameOver);
        Debug.Log($"[EconomyR2LongRun] BATTLE PASSED seed={Seeds[i]} personality={Majors[i]} actualBattleRounds={battleRounds} earlyTerminal={turn.IsGameOver && battleRounds < TargetRounds} playerAttacks={player.SaveTotalAttacks} enemyAttacks={systems.AICommander.SaveTotalAttacks} playerBuilds={player.SaveTotalBuilds} enemyBuilds={systems.AICommander.SaveTotalBuilds} maxStagnantDeficitTurns={maxStagnant} elapsedSeconds={EditorApplication.timeSinceStartup - caseStarted:F1} knownEditorStartupIndexExceptions={caseEditorIndexExceptions}");
        SessionState.SetInt(PassedBattles, SessionState.GetInt(PassedBattles, 0) + 1);
        Cleanup(); SessionState.SetBool(Next, true); EditorApplication.ExitPlaymode();
    }

    static void OnLog(string message, string stack, LogType type)
    {
        if (type != LogType.Exception && type != LogType.Error) return;
        // Unity's editor search index can throw while enumerating databases at startup.
        // This recorded editor-only stack cannot fail a gameplay run; all gameplay/other editor errors still do.
        string normalizedStack = stack?.Replace('\\', '/') ?? string.Empty;
        bool knownEditorStartupIndexFailure = type == LogType.Exception
            && (message?.StartsWith("ArgumentOutOfRangeException:", StringComparison.Ordinal) == true
                || message?.StartsWith("IndexOutOfRangeException:", StringComparison.Ordinal) == true)
            && normalizedStack.IndexOf("UnityEditor.Search.SearchDatabase+<EnumerateAll>", StringComparison.Ordinal) >= 0
            && normalizedStack.IndexOf("UnityEditor.Search.SearchInit.IndexationOnStartup", StringComparison.Ordinal) >= 0
            && normalizedStack.IndexOf("Assets/Script", StringComparison.OrdinalIgnoreCase) < 0;
        if (knownEditorStartupIndexFailure)
        {
            caseEditorIndexExceptions++;
            int total = SessionState.GetInt(IgnoredEditorIndexing, 0) + 1;
            SessionState.SetInt(IgnoredEditorIndexing, total);
            Debug.LogWarning($"[EconomyR2LongRun] KNOWN EDITOR STARTUP INDEX EXCEPTION case={SessionState.GetInt(Index, 0) + 1} count={caseEditorIndexExceptions} total={total} gameplayRunContinues=True exception={message}");
            return;
        }
        runtimeFailure = message;
    }
    static void DisposePlayerSteps()
    { var iterator = playerSteps; playerSteps = null; playerOwner = null; (iterator as IDisposable)?.Dispose(); }
    static void Cleanup()
    {
        Application.logMessageReceived -= OnLog;
        try { DisposePlayerSteps(); }
        finally { if (turn != null) turn.enabled = originalEnabled; if (initialized || systems != null) AIConfig.Mode = originalMode; initialized = false; }
    }
    static void Fail(Exception error)
    {
        Cleanup(); SessionState.SetBool(Running, false); SessionState.SetBool(Next, false);
        Debug.LogException(error); EditorApplication.Exit(1);
    }
}
#endif
