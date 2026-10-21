#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Loss-condition movement gates use the acting faction's observed board at selection and execution.</summary>
public static class AICommanderSafetyTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly AIActionType[] Movements = { AIActionType.Move, AIActionType.Retreat, AIActionType.Support,
        AIActionType.Surround, AIActionType.DefenseRepos };
    static int passed;

    static void Check(string label, bool condition)
    {
        if (!condition) throw new InvalidOperationException("[AICommanderSafety] FAIL " + label);
        passed++; Debug.Log("[AICommanderSafety] PASS " + label);
    }

    public static void PlayMode(GameSystems systems)
    {
        if (!Application.isPlaying || systems?.FactionState == null)
            throw new InvalidOperationException("AI commander safety tests require a started Play Mode game.");
        passed = 0;
        using (var snapshot = new LiveSnapshot(systems))
        using (BoardActorAnimation.SuppressEffects())
        {
            foreach (var team in new[] { Team.Player, Team.Enemy })
            foreach (var kind in new[] { Kind.King, Kind.Boss })
            {
                using (var f = new Fixture(systems, team, kind))
                {
                    UnescortedAndOrdinary(f);
                    HoldAndReturn(f);
                    Observation(f);
                    ExposureAndEscape(f);
                    Escorts(f);
                    HiddenHostile(f);
                    RetreatTerrain(f);
                }
            }
            Check("live AP, resources, random state and UI survive isolated fixtures", snapshot.Unchanged());
        }
        Debug.Log("[AICommanderSafety] " + passed + " commander/faction safety checks passed");
    }

    static AIAction Move(Fixture f, Vector3 destination, AIActionType type = AIActionType.Move)
        => new AIAction { ActionType = type, Unit = f.Commander, TargetPos = destination, APCost = 3, Score = 100000 };

    static void Gate(Fixture f, string label, Vector3 destination, string expected,
        AIActionType type = AIActionType.Move)
    {
        string stocks = JsonUtility.ToJson(f.Economy.Resources);
        string ap = JsonUtility.ToJson(f.Economy.State.GetAPData(f.Economy.Team));
        var board = f.Board(); var selected = Move(f, destination, type); var actions = new List<AIAction> { selected };
        board.Governor.Filter(actions, board);
        Check(f.Label + " " + label + " Filter", expected == null
            ? actions.Contains(selected) && selected.StrategicRejectReason == null
            : !actions.Contains(selected) && selected.StrategicRejectReason == expected);
        // A fresh action and governor prove execution independently rechecks the board,
        // rather than merely honoring a reason stamped by Filter.
        board = f.Board(); var execution = Move(f, destination, type);
        bool allowed = board.Governor.AllowExecution(execution, board);
        Check(f.Label + " " + label + " AllowExecution", allowed == (expected == null)
            && execution.StrategicRejectReason == expected);
        Check(f.Label + " " + label + " inspection preserves faction AP/stocks", stocks == JsonUtility.ToJson(f.Economy.Resources)
            && ap == JsonUtility.ToJson(f.Economy.State.GetAPData(f.Economy.Team)));
    }

    static void UnescortedAndOrdinary(Fixture f)
    {
        f.Reset();
        Check(f.Label + " fixture uses the shared commander classification", MatchObjectiveRules.IsCommander(f.Commander));
        foreach (var movement in Movements)
            Gate(f, movement + " far advance requires escort", f.Cells[5], "king_unescorted_advance", movement);
        f.Commander.IsPrimaryCommander = false; f.Commander.kind = Kind.Knight;
        foreach (var movement in Movements) Gate(f, movement + " ordinary unit keeps movement eligibility", f.Cells[5], null, movement);
        f.Commander.kind = Kind.Boss;
        Gate(f, "ordinary non-primary Boss keeps movement eligibility", f.Cells[5], null);
    }

    static void HoldAndReturn(Fixture f)
    {
        f.Reset(); f.Commander.transform.position = f.Cells[2];
        Gate(f, "safe home commander holds", f.Cells[3], "king_hold_safe_home");
        var board = f.Board(); var action = Move(f, f.Cells[3]);
        var turn = UnityEngine.Object.FindFirstObjectByType<TurnGenerator>();
        Check(f.Label + " executor fixture has an active match", turn != null && !turn.IsGameOver);
        var executor = new AIActionExecutor(turn, f.Live.MoveGenerator, f.Live.AttackGenerator,
            f.Live.BattleSystem, f.Economy.AP, f.Live.SkillSystem, null, f.Economy.Builder, null, null, f.Economy.Team);
        string ap = JsonUtility.ToJson(f.Economy.State.GetAPData(f.Economy.Team));
        string resources = JsonUtility.ToJson(f.Economy.Resources);
        Vector3 position = f.Commander.transform.position; int fatigue = f.Commander.Fatigue;
        Check(f.Label + " safe-home executor spends no AP, stock or movement", !executor.Execute(action, board)
            && action.StrategicRejectReason == "king_hold_safe_home" && f.Commander.transform.position == position
            && !f.Commander.HasMovedThisTurn && fatigue == f.Commander.Fatigue
            && ap == JsonUtility.ToJson(f.Economy.State.GetAPData(f.Economy.Team))
            && resources == JsonUtility.ToJson(f.Economy.Resources));
        f.Reset(); f.Commander.HP = 50;
        Gate(f, "wounded unescorted homeward return remains available", f.Cells[3], null);
        Gate(f, "wounded outward advance is rejected", f.Cells[5], "king_wounded_advance");
    }

    static void Observation(Fixture f)
    {
        f.Reset(); f.Hide(f.Cells[5]);
        Gate(f, "hidden destination is not an advance target", f.Cells[5], "king_unobserved_advance");
        f.Reset(); f.Commander.transform.position = f.Cells[2]; f.Hide(f.Cells[3]);
        Gate(f, "visible destination cannot bypass a hidden intermediate cell", f.Cells[5], "king_unobserved_advance");
    }

    static void ExposureAndEscape(Fixture f)
    {
        f.Reset(); f.Commander.transform.position = f.Cells[3]; f.Threat(30, -1);
        var board = f.Board();
        Check(f.Label + " exposure fixture has zero current risk and positive destination risk",
            board.Governor.IncomingDamage(board, f.Commander, f.Cells[3], null) == 0
            && board.Governor.IncomingDamage(board, f.Commander, f.Cells[4], null) > 0);
        Gate(f, "increasing actual incoming risk is rejected", f.Cells[4], "king_frontline_exposure");

        f.Reset(); f.Commander.transform.position = f.Cells[3]; f.Threat(60, -2, -1);
        board = f.Board(); float before = board.Governor.IncomingDamage(board, f.Commander, f.Cells[3], null);
        float after = board.Governor.IncomingDamage(board, f.Commander, f.Cells[4], null);
        Check(f.Label + " exposure fixture holds nonlethal risk above one fifth HP",
            before == after && after >= f.Commander.HP * .2f && after < f.Commander.HP);
        Gate(f, "unchanged exposure at or above twenty percent HP is rejected", f.Cells[4], "king_frontline_exposure");

        f.Reset(); f.Commander.transform.position = f.Cells[3]; f.Threat(60, -2); f.Hide(f.Cells[4]);
        Gate(f, "lower-risk escape bypasses fog and escort requirements", f.Cells[4], null);
        board = f.Board(); var escape = Move(f, f.Cells[4]); var actions = new List<AIAction> { escape };
        board.Governor.Filter(actions, board);
        Check(f.Label + " risk-reducing escape retains survival priority", actions.Contains(escape) && escape.StrategicPriority == 0);

        f.Reset(); f.Commander.transform.position = f.Cells[3]; f.Commander.HP = 1; f.Threat(60, -2, -1);
        board = f.Board(); var doomed = Move(f, f.Cells[4]); escape = Move(f, f.Cells[2]);
        actions = new List<AIAction> { doomed, escape }; board.Governor.Filter(actions, board);
        Check(f.Label + " existing least-lethal guard precedes other commander rules", !actions.Contains(doomed)
            && doomed.StrategicRejectReason == "king_known_lethal" && actions.Contains(escape) && escape.StrategicPriority == 0);
    }

    static void Escorts(Fixture f)
    {
        f.Reset(); f.Guard.HP = f.Guard.MaxHP; f.Guard.transform.position = f.Cells[3];
        f.Foe.HP = f.Foe.MaxHP; f.Foe.transform.position = f.Cells[1];
        var board = f.Board();
        Check(f.Label + " escort fixture sees the nearby hostile without an attack lane", board.IsSpecialActorObserved(f.Foe)
            && GridHelper.ChebyshevDistance(f.Cells[5], f.Foe.transform.position) <= 4
            && board.Governor.IncomingDamage(board, f.Commander, f.Cells[5], null) == 0);
        Gate(f, "healthy guard ahead permits a safe far advance", f.Cells[5], null);
        f.Guard.transform.position = f.FlankCell(); f.Show(f.Guard.transform.position);
        Gate(f, "commander cannot lead a nearby hostile ahead of its guard", f.Cells[5], "king_ahead_of_escort");
        f.Guard.transform.position = f.Cells[3]; f.Commander.HP = 75;
        Gate(f, "seventy-five percent HP is healthy enough with escort", f.Cells[5], null);
        f.Commander.HP = 74;
        Gate(f, "escort cannot authorize wounded advance below seventy-five percent", f.Cells[5], "king_wounded_advance");
        f.Commander.HP = f.Commander.MaxHP;
        foreach (var kind in new[] { Kind.Scout, Kind.Priest, Kind.WhiteMage })
        {
            f.Guard.kind = kind;
            Gate(f, kind + " does not count as a combat escort", f.Cells[5], "king_unescorted_advance");
        }
        f.Guard.kind = Kind.Knight; f.Guard.HP = 49;
        Gate(f, "guard below half HP does not count as escort", f.Cells[5], "king_unescorted_advance");
        f.Guard.HP = f.Guard.MaxHP; f.Guard.ActiveEffects.Add(new ActiveEffect(StatusEffectType.Stun, 1));
        Gate(f, "stunned guard does not count as escort", f.Cells[5], "king_unescorted_advance");
        f.Guard.ActiveEffects.Clear(); f.Guard.GrowthData.actionProfile = f.SupportProfile;
        Gate(f, "noncombat support profile does not count as escort", f.Cells[5], "king_unescorted_advance");
    }

    static void HiddenHostile(Fixture f)
    {
        f.Reset(); f.Guard.HP = f.Guard.MaxHP; f.Guard.transform.position = f.Cells[3];
        f.Foe.HP = f.Foe.MaxHP; f.Foe.ATK = 9999;
        f.Foe.transform.position = f.Cells[5] + Vector3.forward * 1000;
        Gate(f, "hidden remote hostile does not prevent escorted advance", f.Cells[5], null);
        var board = f.Board();
        f.Foe.transform.position = f.HiddenThreatCell();
        Check(f.Label + " hidden live transform mutation remains unobserved", !board.AlivePlayerUnits.Contains(f.Foe)
            && !board.IsSpecialActorObserved(f.Foe) && !board.IsVisibleToEnemy(f.Foe.transform.position));
        var action = Move(f, f.Cells[5]);
        Check(f.Label + " existing snapshot ignores nearby hidden live transform", board.Governor.AllowExecution(action, board));
        Gate(f, "refreshed observation also ignores nearby hidden live transform", f.Cells[5], null);
    }

    static void RetreatTerrain(Fixture f)
    {
        f.Reset(); f.Guard.HP = f.Guard.MaxHP; f.Guard.transform.position = f.Cells[3];
        f.MovementMap = f.NoReturnMap();
        Check(f.Label + " terrain fixture allows departure but blocks return", f.MovementMap.CanTraverse(f.Cells[4], f.Cells[5])
            && !f.MovementMap.CanTraverse(f.Cells[5], f.Cells[4]));
        Gate(f, "safe escorted advance must retain traversable retreat", f.Cells[5], "king_no_retreat");
    }

    sealed class Fixture : IDisposable
    {
        public readonly GameSystems Live;
        public readonly EconomyR2Tests.Fixture Economy;
        public readonly Status Commander, Guard, Foe;
        public readonly Vector3[] Cells;
        public readonly BoardActionProfile SupportProfile;
        public MapCreate MovementMap;
        public string Label => Economy.Team + " " + commanderKind + (commanderKind == Kind.Boss ? " primary commander" : "");
        readonly Kind commanderKind;
        readonly VisionGenerator vision;
        readonly BoardActionProfile foeProfile;
        readonly HashSet<Vector3Int> seen = new HashSet<Vector3Int>();
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();

        public Fixture(GameSystems live, Team team, Kind kind)
        {
            Live = live; commanderKind = kind; Economy = new EconomyR2Tests.Fixture(live, team);
            try
            {
                Economy.Resources.Citizen = 0; Economy.Rules.aiEconomy.plannedDemandWeight = 0;
                Economy.Food(100); Economy.Building(Economy.Definition(FacilityKind.LoggingCamp, wood: 20));
                Economy.Building(Economy.Definition(FacilityKind.Quarry, stone: 20));
                Economy.Building(Economy.Definition(FacilityKind.Mine, iron: 20, magic: 20));
                Cells = FlatCells(live.MapCreate); MovementMap = live.MapCreate;
                Economy.Crystal.transform.position = Cells[0]; Economy.SetOwnCrystalPosition(Cells[0]);
                Commander = Economy.Unit(); Commander.DEF = 0;
                Guard = Economy.Unit();
                Foe = Economy.Opponent(Cells[5], 60); foeProfile = Foe.GrowthData.actionProfile;
                foeProfile.attack.directionIndependent = true;
                SupportProfile = ScriptableObject.CreateInstance<BoardActionProfile>(); owned.Add(SupportProfile);
                SupportProfile.actionType = ActorActionType.Support;
                var go = new GameObject("Commander safety isolated vision"); owned.Add(go); go.SetActive(false);
                vision = go.AddComponent<VisionGenerator>();
                foreach (string name in new[] { "_playerVisionBox", "_enemyVisionBox", "_playerExplored", "_enemyExplored" })
                    typeof(VisionGenerator).GetField(name, Hidden).SetValue(vision, seen);
                Reset();
            }
            catch { Dispose(); throw; }
        }

        public void Reset()
        {
            Commander.kind = commanderKind; Commander.IsPrimaryCommander = commanderKind == Kind.Boss;
            Commander.HP = Commander.MaxHP = 100; Commander.transform.position = Cells[4]; Commander.HasMovedThisTurn = false;
            Guard.kind = Kind.Knight; Guard.HP = 0; Guard.MaxHP = 100; Guard.ActiveEffects.Clear();
            Guard.GrowthData.actionProfile = null; Guard.transform.position = Cells[3];
            Foe.HP = 0; Foe.MaxHP = 100; Foe.ATK = 60; Foe.transform.position = Cells[5];
            foeProfile.attack.cells = 0; MovementMap = Live.MapCreate;
            seen.Clear(); foreach (var cell in Cells) Show(cell);
        }
        public void Hide(Vector3 cell) => seen.Remove(GridHelper.ToGridXZ(cell));
        public void Show(Vector3 cell) => seen.Add(GridHelper.ToGridXZ(cell));
        public void Threat(int atk, params int[] offsets)
        {
            Foe.HP = Foe.MaxHP; Foe.ATK = atk; Foe.transform.position = Cells[5];
            foeProfile.attack.cells = 0;
            foreach (int offset in offsets) foeProfile.attack.SetCell(3 + offset, 3, true);
        }
        public AIBoardState Board()
        {
            var board = new AIBoardState(Live.MoveGenerator, Live.AttackGenerator, Economy.AP, Economy.Units,
                Economy.Crystals, vision, Economy.Builder, null, Economy.State, null, 60, null, Economy.Team)
                { MapCreate = MovementMap, ReconThreatLevel = 15, Governor = new AIStrategicGovernor() };
            // Shared map geometry must not import unrelated scene neutrals into this in-memory faction fixture.
            board.AlivePlayerUnits.Clear();
            if (Foe.IsAlive && vision.IsActorVisibleTo(Economy.Team, Foe)) board.AlivePlayerUnits.Add(Foe);
            board.Enemies.Rebuild(board.AlivePlayerUnits);
            board.Governor.Evaluate(board); return board;
        }
        public Vector3 FlankCell()
        {
            foreach (var cell in Live.MapCreate.SetPos)
                if (cell != Cells[4] && cell != Cells[5] && GridHelper.ChebyshevDistance(cell, Cells[5]) <= 2
                    && GridHelper.ChebyshevDistance(cell, Cells[1]) >= 4
                    && Live.MapCreate.Environment.DefinitionAt(cell) == null) return cell;
            throw new InvalidOperationException("Commander safety fixture requires a real nearby flank cell.");
        }
        public Vector3 HiddenThreatCell()
        {
            foreach (var cell in Live.MapCreate.SetPos)
                if (!seen.Contains(GridHelper.ToGridXZ(cell)) && GridHelper.ChebyshevDistance(cell, Cells[5]) <= 2
                    && GridHelper.ChebyshevDistance(cell, Cells[5]) < GridHelper.ChebyshevDistance(cell, Guard.transform.position)) return cell;
            throw new InvalidOperationException("Commander safety fixture requires an unobserved cell near the advance.");
        }
        public MapCreate NoReturnMap()
        {
            var go = new GameObject("Commander safety isolated return geometry"); owned.Add(go);
            var map = go.AddComponent<MapCreate>(); map.UseR1Terrain = false; map.SetPos.AddRange(Cells);
            var catalog = ScriptableObject.CreateInstance<TerrainEnvironmentCatalog>(); owned.Add(catalog); catalog.enabledForPlay = true;
            var rock = ScriptableObject.CreateInstance<TerrainEnvironmentDefinition>(); owned.Add(rock);
            rock.definitionId = "commander.safety.return.blocker"; rock.kind = TerrainEnvironmentKind.Rock;
            rock.enabledForPlay = true; rock.blocksMovement = true; catalog.definitions.Add(rock);
            catalog.placements.Add(new TerrainEnvironmentPlacement { definition = rock,
                cell = new Vector2Int(Mathf.RoundToInt(Cells[4].x), Mathf.RoundToInt(Cells[4].z)) });
            map.InitializeEnvironment(catalog); return map;
        }
        public void Dispose()
        {
            try { for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]); }
            finally { Economy?.Dispose(); }
        }
        static Vector3[] FlatCells(MapCreate map)
        {
            foreach (var start in map.SetPos)
            {
                var cells = new Vector3[6]; bool valid = true;
                for (int i = 0; i < cells.Length; i++)
                {
                    int x = Mathf.RoundToInt(start.x) + i, z = Mathf.RoundToInt(start.z);
                    if (!map.TryGetHeight(x, z, out float y) || Mathf.Abs(y - start.y) > .01f)
                    { valid = false; break; }
                    cells[i] = new Vector3(x, y, z);
                    if (map.Environment.DefinitionAt(cells[i]) != null || !map.CanTraverse(start, cells[i])
                        || !map.CanTraverse(cells[i], start)) { valid = false; break; }
                }
                if (!valid) continue;
                // Reserve a real off-corridor cell for both the flank and hidden-actor
                // regressions instead of relying on whichever corridor happens to appear first.
                foreach (var cell in map.SetPos)
                    if (cell != cells[4] && cell != cells[5] && GridHelper.ChebyshevDistance(cell, cells[5]) <= 2
                        && GridHelper.ChebyshevDistance(cell, cells[1]) >= 4
                        && GridHelper.ChebyshevDistance(cell, cells[5]) < GridHelper.ChebyshevDistance(cell, cells[3])
                        && map.Environment.DefinitionAt(cell) == null) return cells;
            }
            throw new InvalidOperationException("Commander safety fixture needs six adjacent level, clear board cells.");
        }
    }

    sealed class LiveSnapshot : IDisposable
    {
        readonly GameSystems live;
        readonly UnityEngine.Random.State random = UnityEngine.Random.state;
        readonly string playerAP, enemyAP, playerStock, enemyStock;
        readonly Canvas canvas;
        readonly List<(GameObject Object, bool Active)> uiObjects = new List<(GameObject, bool)>();
        readonly List<(TMPro.TMP_Text Label, string Text)> labels = new List<(TMPro.TMP_Text, string)>();

        public LiveSnapshot(GameSystems systems)
        {
            live = systems; canvas = UIBuilder.ScreenCanvas;
            playerAP = JsonUtility.ToJson(live.FactionState.PlayerAP); enemyAP = JsonUtility.ToJson(live.FactionState.EnemyAP);
            playerStock = JsonUtility.ToJson(live.FactionState.PlayerResources); enemyStock = JsonUtility.ToJson(live.FactionState.EnemyResources);
            if (canvas != null)
            {
                foreach (var item in canvas.GetComponentsInChildren<Transform>(true)) uiObjects.Add((item.gameObject, item.gameObject.activeSelf));
                foreach (var label in canvas.GetComponentsInChildren<TMPro.TMP_Text>(true)) labels.Add((label, label.text));
            }
        }
        public bool Unchanged()
        {
            if (playerAP != JsonUtility.ToJson(live.FactionState.PlayerAP) || enemyAP != JsonUtility.ToJson(live.FactionState.EnemyAP)
                || playerStock != JsonUtility.ToJson(live.FactionState.PlayerResources) || enemyStock != JsonUtility.ToJson(live.FactionState.EnemyResources)
                || !random.Equals(UnityEngine.Random.state) || canvas != UIBuilder.ScreenCanvas) return false;
            foreach (var item in uiObjects) if (item.Object == null || item.Object.activeSelf != item.Active) return false;
            foreach (var label in labels) if (label.Label == null || label.Label.text != label.Text) return false;
            return true;
        }
        public void Dispose()
        {
            try
            {
                JsonUtility.FromJsonOverwrite(playerAP, live.FactionState.PlayerAP); JsonUtility.FromJsonOverwrite(enemyAP, live.FactionState.EnemyAP);
                JsonUtility.FromJsonOverwrite(playerStock, live.FactionState.PlayerResources); JsonUtility.FromJsonOverwrite(enemyStock, live.FactionState.EnemyResources);
                typeof(UIBuilder).GetProperty("ScreenCanvas").SetValue(null, canvas);
                foreach (var label in labels) if (label.Label != null && label.Label.text != label.Text) label.Label.text = label.Text;
                foreach (var item in uiObjects) if (item.Object != null && item.Object.activeSelf != item.Active) item.Object.SetActive(item.Active);
            }
            finally { UnityEngine.Random.state = random; live.MoveGenerator.UnitPointCore(); }
        }
    }
}
#endif
