#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class SceneSmoke
{
    const string Running = "FantasyKingdomR1Smoke";
    static bool executing;
    static SceneSmoke() { EditorApplication.update += Tick; }

    public static void Run()
    {
        if (!Application.dataPath.Contains("UnityValidation")) throw new Exception("Smoke test requires isolated project");
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "FantasyKingdomR1Validation";
        CoreLogicTests.RunAll();
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        var catalog = AssetDatabase.LoadAssetAtPath<R1ContentCatalog>("Assets/Resources/R1ContentCatalog.asset");
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<R1ContentCatalog>();
            AssetDatabase.CreateAsset(catalog, "Assets/Resources/R1ContentCatalog.asset");
        }
        var stats = AssetDatabase.LoadAssetAtPath<UnitData>("Assets/Resources/FixtureUnit.asset");
        if (stats == null)
        {
            stats = UnitStaticData.CreateUnitData(Kind.Knight);
            AssetDatabase.CreateAsset(stats, "Assets/Resources/FixtureUnit.asset");
        }
        catalog.Monsters.Clear(); catalog.Intruders.Clear();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Units/Player/Knight.prefab");
        catalog.Monsters.Add(new R1ContentCatalog.Encounter { Id = "fixture-monster", Prefab = prefab, Stats = stats, Count = 2 });
        catalog.Intruders.Add(new R1ContentCatalog.Encounter { Id = "fixture-intruder", Prefab = prefab, Stats = stats,
            Relic = new R1ContentCatalog.UniqueReward { Id = "fixture-relic", DisplayName = "検証用報酬" } });
        EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        SessionState.SetBool(Running, true);
        EditorApplication.EnterPlaymode();
    }

    static void Check(string name, bool value)
    {
        if (!value) throw new Exception("[SceneSmoke] FAIL " + name);
        Debug.Log("[SceneSmoke] PASS " + name);
    }

    static void Tick()
    {
        if (executing || !SessionState.GetBool(Running, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        var game = UnityEngine.Object.FindFirstObjectByType<GameGenerator>();
        if (game == null || TitleScreenUI.Instance == null) return;
        executing = true;
        try
        {
            typeof(GameGenerator).GetField("_waitingForTitle", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(game, false);
            UnityEngine.Object.DestroyImmediate(TitleScreenUI.Instance.gameObject);
            typeof(GameGenerator).GetMethod("StartGameInit", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, new object[] { -1 });
            var turn = UnityEngine.Object.FindFirstObjectByType<TurnGenerator>();
            var systems = turn.Systems;
            Check("game starts on player turn", turn.CurrentState is PlayerMove && turn.Context.Turn == 1);
            Check("player starts with 35 AP", systems.APSystem.GetAP(Team.Player) == 35);
            Check("initial player squad", systems.UnitSetting.PlayerUnit.GetComponentsInChildren<Status>().Count(s => s.IsAlive) == 4);
            Check("initial enemy squad", systems.UnitSetting.EnemyUnit.GetComponentsInChildren<Status>().Count(s => s.IsAlive) == 4);
            Check("two dungeons", systems.DungeonSystem.Dungeons.Count == 2);
            Check("new map uses passable heights 1/2", systems.MapCreate.SetPos.All(p => p.y == 1 || p.y == 2));
            Check("crystal HP 15000", systems.CrystalSystem.Playercrystal.GetComponentInChildren<Status>().MaxHP == 15000);
            Check("territory exact boundary hidden", systems.WildBossSystem.TerritoryParent.childCount == 0);
            var viewport = Camera.main.WorldToViewportPoint(systems.CrystalSystem.PCP);
            Check("player base centered on screen", viewport.z > 0 && Mathf.Abs(viewport.x - 0.5f) < 0.01f && Mathf.Abs(viewport.y - 0.5f) < 0.01f);
            BenchmarkHeightLookup(systems.MapCreate);
            TestMoveUndo(systems, turn);
            TestObservations(systems, turn);
            TestSubCrystalAndDungeon(systems);
            systems.FactionState.SetAP(Team.Player, 12);
            systems.TimerSystem.RestoreTurnTimeRemaining(43f);
            var menu = GameMenuUI.Instance;
            menu.Open();
            float totalBefore = systems.TimerSystem.PlayerTotalTime;
            typeof(TimerSystem).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(systems.TimerSystem, null);
            Check("menu pauses both timers", menu.IsOpen && systems.TimerSystem.TurnTimeRemaining == 43f && systems.TimerSystem.PlayerTotalTime == totalBefore);
            menu.Close();
            Check("closing menu releases pause", !menu.IsOpen);
            var saved = SaveSystem.CollectGameState(turn, systems.FactionState, systems.TimerSystem, systems.VisionGenerator, systems.AICommander);
            var roundTrip = JsonUtility.FromJson<SaveSystem.GameSaveData>(JsonUtility.ToJson(saved));
            Check("save includes both crystals", roundTrip.Units.Count(u => u.Kind == Kind.Crystal.ToString()) == 2);
            Check("save includes both dungeons", roundTrip.Dungeons.Count == 2);
            SaveGameApplier.Apply(roundTrip, systems.FactionState, turn, systems.UnitSetting, systems.CrystalSystem,
                systems.BuildSystem, systems.MoveGenerator, systems.VisionGenerator, systems.MapCreate, systems.SummonSystem);
            turn.StartFirstTurn();
            Check("loading does not refill AP", systems.APSystem.GetAP(Team.Player) == 12);
            Check("loading does not reset turn time", Mathf.Abs(systems.TimerSystem.TurnTimeRemaining - 43f) < 0.01f);
            Check("loading does not increment round", turn.Context.Turn == 1);
            Check("boss state survives save", systems.WildBossSystem.SpawnedBoss.HP == roundTrip.WildBoss.Unit.HP
                && systems.WildBossSystem.SpawnedBoss.wildBossArchetype == roundTrip.WildBoss.Archetype);
            turn.ChangeState(new EnemyStart(turn));
            Check("enemy and independent turns return to player", turn.CurrentState is PlayerMove && turn.Context.Turn == 2);
            var neutrals = systems.NeutralFactionSystem.UnitParent.GetComponentsInChildren<Status>();
            Check("configured monsters spawn independently", neutrals.Count(s => s.team == Team.Monster) == 2);
            var intruder = neutrals.First(s => s.team == Team.Intruder);
            Check("intruder excludes strong enemy", !NeutralFactionSystem.AreHostile(intruder, systems.WildBossSystem.SpawnedBoss));
            var player = systems.UnitSetting.PlayerUnit.GetComponentInChildren<Status>();
            Check("intruder attacks both principal factions", NeutralFactionSystem.AreHostile(intruder, player)
                && NeutralFactionSystem.AreHostile(intruder, systems.UnitSetting.EnemyUnit.GetComponentInChildren<Status>()));
            intruder.HP = 1;
            int actual = intruder.ApplyDamage(5);
            Status.AwardDamageExperience(player, intruder, actual, systems.FactionState);
            Status.AwardDamageExperience(player, intruder, actual, systems.FactionState);
            Check("unique relic awarded once", UniqueRewardSystem.Records.Count(r => r.Id == "fixture-relic") == 1);
            intruder.HandleDeathIfDead();
            var neutralSave = systems.NeutralFactionSystem.Capture();
            systems.NeutralFactionSystem.Restore(neutralSave, 2, 1, systems.NeutralFactionSystem.SpawnedIntruders.ToList());
            Check("neutral restore does not duplicate units", systems.NeutralFactionSystem.UnitParent.GetComponentsInChildren<Status>().Length == 2);
            Debug.Log("[SceneSmoke] ALL PASSED");
            SessionState.SetBool(Running, false);
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            SessionState.SetBool(Running, false);
            EditorApplication.Exit(1);
        }
    }

    static void TestMoveUndo(GameSystems s, TurnGenerator turn)
    {
        var unit = s.UnitSetting.PlayerUnit.GetComponentsInChildren<Status>().First(u => u.kind == Kind.Knight);
        var from = unit.transform.position;
        var to = s.MapCreate.SetPos.First(p => !s.MoveGenerator.IsOccupied(s.MoveGenerator.Cell(p)));
        int beforeAP = s.APSystem.GetAP(Team.Player);
        unit.Fatigue = 3; unit.HasMovedThisTurn = false;
        int cost = s.APSystem.CalcCost(APSystem.ActionType.Move, unit, from, to);
        s.MoveUndoSystem.Record(unit, from, to, cost);
        unit.transform.position = to;
        s.APSystem.Consume(Team.Player, APSystem.ActionType.Move, unit, from, to);
        unit.HasMovedThisTurn = true;
        Check("undo restores move", s.MoveUndoSystem.Undo(turn, (PlayerMove)turn.CurrentState, s.VisionGenerator, s.MapCreate, s.CrystalSystem));
        Check("undo restores AP fatigue and first-move state", s.APSystem.GetAP(Team.Player) == beforeAP && unit.Fatigue == 3 && !unit.HasMovedThisTurn && unit.transform.position == from);
        s.MoveUndoSystem.Record(unit, from, to, cost);
        s.APSystem.Consume(Team.Player, APSystem.ActionType.Attack, unit);
        Check("attack invalidates old movement undo", !s.MoveUndoSystem.CanUndo);
        unit.Fatigue = 0;
        s.FactionState.SetAP(Team.Player, beforeAP);
    }

    static void BenchmarkHeightLookup(MapCreate map)
    {
        bool equivalent = true;
        for (int x = -1; x <= map.maxX; x++)
        for (int z = -1; z <= map.maxZ; z++)
        {
            bool oldResult = GridHelper.TryGetHeight(map.SetPos, x, z, out float oldY);
            bool newResult = map.TryGetHeight(x, z, out float newY);
            if (oldResult != newResult || (oldResult && oldY != newY)) equivalent = false;
        }
        Check("indexed terrain lookup matches original on every cell", equivalent);
        const int iterations = 20000;
        float checksumOld = 0, checksumNew = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
            if (GridHelper.TryGetHeight(map.SetPos, i % map.maxX, (i / map.maxX) % map.maxZ, out float y)) checksumOld += y;
        double oldMs = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        for (int i = 0; i < iterations; i++)
            if (map.TryGetHeight(i % map.maxX, (i / map.maxX) % map.maxZ, out float y)) checksumNew += y;
        double newMs = watch.Elapsed.TotalMilliseconds;
        Check("terrain benchmark checksums match", checksumOld == checksumNew);
        Debug.Log($"[Performance] Height lookup {iterations} calls: list={oldMs:F3}ms indexed={newMs:F3}ms; map={map.maxX}x{map.maxZ}");
    }

    static void TestObservations(GameSystems s, TurnGenerator turn)
    {
        var sight = (System.Collections.Generic.HashSet<Vector3Int>)typeof(VisionGenerator)
            .GetField("_enemyVisionBox", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(s.VisionGenerator);
        var original = sight.ToArray();
        var players = s.UnitSetting.PlayerUnit.GetComponentsInChildren<Status>();
        sight.Clear(); sight.Add(players[0].GridPosition);
        var board = new AIBoardState(s.MoveGenerator, s.AttackGenerator, s.APSystem, s.UnitSetting,
            s.CrystalSystem, s.VisionGenerator, s.BuildSystem, s.SummonSystem, s.FactionState, s.SubCrystalSystem);
        Check("undiscovered crystal data withheld", !board.PlayerCrystalVisible && board.PlayerCrystalHP == -1 && board.PlayerCrystalPos == Vector3.zero);
        Check("observed player appears", board.AlivePlayerUnits.Contains(players[0]));
        sight.Clear(); sight.Add(players[1].GridPosition); board.Refresh();
        Check("same-count visibility updates", !board.AlivePlayerUnits.Contains(players[0]) && board.AlivePlayerUnits.Contains(players[1]));
        var previous = players[0].transform.position;
        players[0].transform.position += new Vector3(5, 0, 5); board.Refresh();
        Check("hidden movement does not update memory", board.GetLastKnownPlayerPositions().Any(p => p.pos == GridHelper.ToGrid(previous)));
        players[0].transform.position = previous;
        sight.Clear(); sight.UnionWith(original); s.RefreshVision();
    }

    static void TestSubCrystalAndDungeon(GameSystems s)
    {
        var dungeon = s.DungeonSystem.Dungeons[0];
        var pos = dungeon.Position;
        var sub = s.BuildSystem.PlaceBuildingForLoad(pos, FacilityKind.SubCrystal, Team.Player);
        var neighbor = s.TerritorySystem.GetTerritory(Team.Player).First(p => GridHelper.ChebyshevDistance(GridHelper.ToGrid(p), pos) == 1);
        var wall = s.BuildSystem.PlaceBuildingForLoad(GridHelper.ToGrid(neighbor), FacilityKind.WoodWall, Team.Player);
        s.DungeonSystem.ProcessTurn(Team.Player, 100);
        Check("subcrystal controls dungeon", dungeon.ClaimingTeam == Team.Player && dungeon.ClaimProgress == 1);
        s.DungeonSystem.ProcessTurn(Team.Enemy, 100); s.DungeonSystem.ProcessTurn(Team.Player, 100);
        Check("dungeon advances only once per round", dungeon.ClaimProgress == 1);
        int wood = s.FactionState.PlayerResources.Wood;
        var enemyResources = s.FactionState.EnemyResources;
        int enemyTotal = enemyResources.Wood + enemyResources.Bread + enemyResources.Stone + enemyResources.Iron;
        s.SubCrystalSystem.DestroyBuilding(sub);
        Check("own dismantle grants no enemy reward", enemyTotal == enemyResources.Wood + enemyResources.Bread + enemyResources.Stone + enemyResources.Iron);
        Check("dependent building destroyed without refund", !wall.gameObject.activeSelf && s.FactionState.PlayerResources.Wood == wood);
        Check("destroyed wall releases occupancy", !s.MoveGenerator.IsOccupied(s.MoveGenerator.Cell(neighbor)));
        Check("one delayed return scheduled", s.FactionState.PlayerPendingReturns.Count == 1 && s.FactionState.PlayerPendingReturns[0] == 5);
        s.SubCrystalSystem.DestroyBuilding(sub);
        Check("duplicate destruction is harmless", s.FactionState.PlayerPendingReturns.Count == 1);
        s.DungeonSystem.ProcessTurn(Team.Player, 101);
        Check("lost control pauses dungeon", dungeon.ClaimingTeam == Team.None && dungeon.ClaimProgress == 1);
    }
}
#endif
