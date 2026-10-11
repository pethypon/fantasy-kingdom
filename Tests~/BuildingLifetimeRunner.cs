#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;

/// <summary>Actual building creation/demolition over separate frames; no gameplay/resource rules are modified.</summary>
[InitializeOnLoad]
public static class BuildingLifetimeRunner
{
    const string Running = "FK.BuildingLifetime.Running";
    const int Cycles = 500;
    static TurnGenerator turn;
    static Status retiring;
    static int completed, frame = -1, initialChildren, initialRegistry, initialUnitChildren, initialUnitRegistry;
    static bool retiredUnit;
    static Vector3Int cell;
    static double started;
    static long initialMemory;
    static BuildingLifetimeRunner() { EditorApplication.update += Tick; }
    public static void Run()
    {
        if (!Application.dataPath.Contains("UnityValidation")) throw new InvalidOperationException("Isolated project required");
        PlayerSettings.companyName = "CodexValidation"; PlayerSettings.productName = "FantasyKingdomR1Validation";
        SessionState.SetBool(Running, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity"); EditorApplication.EnterPlaymode();
    }
    static void Require(bool ok, string reason) { if (!ok) throw new InvalidOperationException("[BuildingLifetime] " + reason); }
    static void Tick()
    {
        if (!SessionState.GetBool(Running, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (turn == null)
            {
                var game = UnityEngine.Object.FindFirstObjectByType<GameGenerator>();
                if (game == null || TitleScreenUI.Instance == null) return;
                typeof(GameGenerator).GetField("_waitingForTitle", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(game, false);
                UnityEngine.Object.DestroyImmediate(TitleScreenUI.Instance.gameObject);
                UnityEngine.Random.InitState(56192);
                typeof(GameGenerator).GetMethod("StartGameInit", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, new object[] { -1 });
                turn = UnityEngine.Object.FindFirstObjectByType<TurnGenerator>(); turn.enabled = false; turn.MarkDeveloperMatch();
                turn.Systems.TimerSystem.StopTurn();
                var s = turn.Systems;
                cell = GridHelper.ToGrid(s.MapCreate.SetPos.First(p => !s.MoveGenerator.IsOccupied(s.MoveGenerator.Cell(p))));
                initialChildren = s.BuildSystem.PlayerBuildingParent.childCount;
                initialRegistry = UnitRegistry.Instance.PlayerBuildings.Count;
                initialUnitChildren = s.UnitSetting.PlayerUnit.childCount;
                initialUnitRegistry = UnitRegistry.Instance.PlayerUnits.Count;
                initialMemory = Profiler.GetTotalAllocatedMemoryLong(); started = EditorApplication.timeSinceStartup;
            }
            if (EditorApplication.timeSinceStartup - started > 180) throw new TimeoutException("Building lifetime test stalled");
            if (frame == Time.frameCount) return; frame = Time.frameCount;
            var systems = turn.Systems;
            if (completed > 0)
            {
                Require(retiring == null, "demolished object survived end of frame");
                Require(systems.BuildSystem.PlayerBuildingParent.childCount == initialChildren, "building child count grew");
                Require(UnitRegistry.Instance.PlayerBuildings.Count == initialRegistry, "registry retained a retired building");
                Require(systems.UnitSetting.PlayerUnit.childCount == initialUnitChildren, "summoned unit root count grew");
                Require(UnitRegistry.Instance.PlayerUnits.Count == initialUnitRegistry, "registry retained a retired summoned unit");
                Require(!systems.BuildSystem.HasBuildingAt(cell), "demolished cell stayed occupied");
            }
            if (completed == Cycles * 2)
            {
                var data = SaveSystem.CollectGameState(turn, systems.FactionState, systems.TimerSystem, systems.VisionGenerator, systems.AICommander);
                Require(SaveSystem.ValidateGameData(data, out var reason), "valid game snapshot rejected: " + reason);
                Require(!data.Units.Any(u => u.PosX < 0 || u.PosZ < 0), "demolition shells escaped into persistent data");
                Debug.Log("[BuildingLifetime] ALL PASSED buildingCycles=" + Cycles + " unitCycles=" + Cycles + " retiredReferences=0 beforeMemory=" + initialMemory
                    + " afterMemory=" + Profiler.GetTotalAllocatedMemoryLong() + " (Editor total, not a leak guarantee)");
                SessionState.SetBool(Running, false); EditorApplication.Exit(0); return;
            }
            retiredUnit = completed >= Cycles;
            retiring = retiredUnit ? systems.SummonSystem.SpawnUnitForLoad(Kind.Knight, Team.Player, cell)
                : systems.BuildSystem.PlaceBuildingForLoad(cell, FacilityKind.LoggingCamp, Team.Player);
            Require(retiring != null && retiring.gameObject.activeSelf, "real building placement failed");
            if (retiredUnit) { retiring.HP = 0; retiring.HandleDeathIfDead(); }
            else systems.SubCrystalSystem.DestroyBuilding(retiring);
            completed++;
            Require(!retiring.gameObject.activeSelf, "demolition did not disable interaction immediately");
            if (completed % 100 == 0) Debug.Log("[BuildingLifetime] PROGRESS cycles=" + completed);
        }
        catch (Exception error) { Debug.LogException(error); SessionState.SetBool(Running, false); EditorApplication.Exit(1); }
    }
}
#endif
