#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

/// <summary>
/// Regression boundaries for save continuation, centralized input and battle-owned references.
/// Fixtures use developer matches and in-memory factions; they never save or train user profiles.
/// </summary>
public static class LongTermRuntimeTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int passed;
    static void Check(string label, bool condition)
    {
        if (!condition) throw new InvalidOperationException("[LongTermRuntime] FAIL " + label);
        passed++;
        Debug.Log("[LongTermRuntime] PASS " + label);
    }
    static FieldInfo Field(System.Type type, string name)
        => type.GetField(name, Private) ?? throw new MissingFieldException(type.FullName, name);
    static string Traits(PersonalityTraits value)
        => value.Caution + "," + value.Command + "," + value.Obsession + "," + value.Defense + "," + value.Tactics + "," + value.Development;

    public static void Run(GameSystems systems, TurnGenerator turn)
    {
        passed = 0;
        AIMode mode = AIConfig.Mode;
        long writes = AIPerformanceCounters.FileWriteCount;
        try
        {
            SavedAIConstruction(systems);
            UnitActionContinuation(systems);
            PerTurnAbilityContinuation(systems);
            ResumePlayerCheckpoint(systems);
            ExplicitMapZero();
            Checkpoints(systems);
            CentralInput();
            UndoLifetime(systems);
            BattleHubLifetime(systems);
            Check("fixtures never write user AI profiles", AIPerformanceCounters.FileWriteCount == writes);
        }
        finally { AIConfig.Mode = mode; }
        Debug.Log("[LongTermRuntime] " + passed + " save/input/lifetime checks passed");
    }

    static SaveSystem.GameSaveData Saved(int seed, int ownTurn = 11) => new SaveSystem.GameSaveData
    {
        Turn = 12, ThreatLevel = 15,
        AI = new SaveSystem.AISaveData
        {
            HasDeterministicSeed = true, RngSeed = seed, AITurnCount = ownTurn,
            MajorPersonality = MajorPersonality.Intellect.ToString(),
            TraitCaution = 80, TraitCommand = 40, TraitObsession = 20,
            TraitDefense = 60, TraitTactics = 70, TraitDevelopment = 30,
            CurrentStrategy = TurnStrategy.Balanced.ToString()
        }
    };

    static AICommander InitSaved(HubFixture fixture, GameSystems live, SaveSystem.GameSaveData data)
    {
        var hub = fixture.Hub;
        var systems = hub.Systems;
        SystemInitializer.InitAI(hub, live.MoveGenerator, null, fixture.Factions.AP,
            fixture.Factions.Units, fixture.Factions.Crystals, live.MapCreate,
            fixture.Factions.Builder, systems.SummonSystem, fixture.Factions.State, null, null, data);
        return systems.AICommander;
    }

    static void SavedAIConstruction(GameSystems live)
    {
        using (var fixture = new HubFixture(live))
        {
            var saved = Saved(0);
            var commander = InitSaved(fixture, live, saved);
            Check("saved explicit zero AI seed is honored by SystemInitializer", commander.RandomSeed == 0);
            Check("saved threat is used before constructing AI", commander.ThreatLevel.Level == 15);
            Check("saved major and all six traits survive construction", commander.Personality.Major == MajorPersonality.Intellect
                && Traits(commander.Personality.Traits) == "80,40,20,60,70,30");

            var copy = JsonUtility.FromJson<SaveSystem.GameSaveData>(JsonUtility.ToJson(saved));
            var reconstructed = InitSaved(fixture, live, copy);
            Check("JSON continuation preserves actual seed/personality/threat", reconstructed.RandomSeed == commander.RandomSeed
                && reconstructed.ThreatLevel.Level == commander.ThreatLevel.Level
                && Traits(reconstructed.Personality.Traits) == Traits(commander.Personality.Traits));
            commander.ReleaseRuntimeReferences();

            SaveSystem.RestoreAICommander(copy.AI, reconstructed);
            Check("restored own-turn count survives before next turn", reconstructed.SaveTurnCount == 11);
            fixture.Factions.State.SetAP(Team.Enemy, 0);
            IEnumerator steps = reconstructed.ExecuteTurnSteps();
            try
            {
                AITurnBudget.Resume();
                Check("loaded commander reaches its ordinary cooperative turn boundary", steps.MoveNext());
                AITurnBudget.Pause();
                Check("loaded next own-turn increments exactly once", reconstructed.SaveTurnCount == 12);
                var rng = (AIDeterministicRandom)Field(typeof(AICommander), "_rng").GetValue(reconstructed);
                // Independent system-random-v1 oracle; no production helper computes the expected value.
                var expected = new System.Random(0 ^ (12 * 31));
                bool same = true;
                for (int i = 0; i < 32; i++) same &= rng.Next() == expected.Next();
                Check("loaded next turn uses the saved match seed and restored own-turn subseed", same);
            }
            finally { AITurnBudget.Pause(); (steps as IDisposable)?.Dispose(); }

            var directA = Construct(fixture, live, 56192);
            var directB = Construct(fixture, live, 56192);
            Check("same explicit constructor seed produces the same personality traits", Traits(directA.Personality.Traits) == Traits(directB.Personality.Traits)
                && directA.Personality.Traits.Total == 300);
            var supplied = new PersonalityTraits { Caution = 80, Command = 40, Obsession = 20, Defense = 60, Tactics = 70, Development = 30 };
            var isolated = Construct(fixture, live, 56192, supplied);
            supplied.Caution = 1;
            Check("constructor copies saved traits instead of retaining mutable save objects", isolated.Personality.Traits.Caution == 80);
            directA.ReleaseRuntimeReferences(); directB.ReleaseRuntimeReferences(); isolated.ReleaseRuntimeReferences();

            var missing = Saved(1701);
            missing.AI.TraitCaution = 79; // Legacy/invalid total: regenerate from the preserved seed.
            var regenerated = InitSaved(fixture, live, missing);
            Check("unavailable legacy trait block has deterministic fallback", Traits(regenerated.Personality.Traits)
                == Traits(new AIPersonality(MajorPersonality.Intellect, 1701).Traits));
        }
    }

    static AICommander Construct(HubFixture fixture, GameSystems live, int seed, PersonalityTraits traits = null)
        => new AICommander(fixture.Hub, live.MoveGenerator, live.AttackGenerator, live.BattleSystem, null,
            fixture.Factions.AP, fixture.Factions.Units, fixture.Factions.Crystals, live.MapCreate,
            MajorPersonality.Intellect, fixture.Factions.Builder, fixture.Hub.Systems.SummonSystem,
            fixture.Factions.State, null, null, 15, seed, Team.Enemy, traits);

    static void UnitActionContinuation(GameSystems live)
    {
        using (var fixture = new EconomyR2Tests.Fixture(live))
        {
            var actor = fixture.Unit();
            // This is a fixed authored discount, not a historical random ability.
            actor.GrowthData.specialSkill = null;
            actor.GrowthData.useAuthoredAbilities = true;
            actor.GrowthData.authoredSpecialAbility = SpecialAbility.SwiftPosture;
            actor.HasMovedThisTurn = true; actor.Fatigue = 3; actor.SkillCooldown = 2;
            actor.UpkeepUnpaidTurns = 3; actor.BuildingOperationAvailable = false;
            actor.direction = Direction.S; actor.specialAbility = SpecialAbility.SwiftPosture;
            string life = actor.ReflectionLifeId;
            var data = JsonUtility.FromJson<SaveSystem.UnitSaveData>(JsonUtility.ToJson(SaveSystem.CaptureUnit(actor)));
            actor.HasMovedThisTurn = false; actor.Fatigue = 0; actor.SkillCooldown = 0; actor.direction = Direction.N;
            actor.UpkeepUnpaidTurns = 0; actor.BuildingOperationAvailable = true;
            SaveGameApplier.ApplyStatusFields(actor, data);
            Check("moved flag/fatigue/cooldown/direction survive save roundtrip", actor.HasMovedThisTurn && actor.Fatigue == 3
                && actor.SkillCooldown == 2 && actor.direction == Direction.S);
            Check("restored unit keeps its stable life ID", actor.ReflectionLifeId == life);
            Check("upkeep debt and paid-building operation state survive the checkpoint", actor.UpkeepUnpaidTurns == 3
                && !actor.BuildingOperationAvailable);
            Check("loaded moved unit cannot regain first-move AP discount", SpecialAbilitySystem.GetMoveAPReduction(actor) == 0);
            actor.HasMovedThisTurn = false;
            var unmoved = JsonUtility.FromJson<SaveSystem.UnitSaveData>(JsonUtility.ToJson(SaveSystem.CaptureUnit(actor)));
            actor.HasMovedThisTurn = true;
            SaveGameApplier.ApplyStatusFields(actor, unmoved);
            Check("unmoved unit also restores its legitimate first-move discount", !actor.HasMovedThisTurn
                && SpecialAbilitySystem.GetMoveAPReduction(actor) > 0);
        }
    }

    static void ExplicitMapZero()
    {
        var saved = new SaveSystem.GameSaveData { HasExplicitMapSnapshot = true };
        Check("explicit map seed zero is a saved seed", MapInitializer.HasSavedMapSeed(saved));
        Check("explicit crystal position at origin is saved placement", MapInitializer.HasSavedCrystalPositions(saved));
        saved.HasExplicitMapSnapshot = false;
        Check("legacy missing map seed retains historical fallback", !MapInitializer.HasSavedMapSeed(saved));
        Check("legacy missing crystal positions retain historical fallback", !MapInitializer.HasSavedCrystalPositions(saved));
        saved.MapSeedZ = 25;
        Check("legacy nonzero map seed remains supported", MapInitializer.HasSavedMapSeed(saved));
        saved.PCPx = 3;
        Check("legacy nonzero crystal placement remains supported", MapInitializer.HasSavedCrystalPositions(saved));
        Check("new game has no saved map or crystal placement", !MapInitializer.HasSavedMapSeed(null)
            && !MapInitializer.HasSavedCrystalPositions(null));
    }

    static SaveSystem.UnitSaveData RoundtripAbilityFlags(Status actor)
    {
        actor.GrowthData.specialSkill = null;
        actor.GrowthData.useAuthoredAbilities = true;
        actor.GrowthData.authoredSpecialAbility = actor.specialAbility;
        var saved = JsonUtility.FromJson<SaveSystem.UnitSaveData>(JsonUtility.ToJson(SaveSystem.CaptureUnit(actor)));
        actor.FirstSkillUsedThisTurn = !actor.FirstSkillUsedThisTurn;
        actor.DebuffNullifiedThisTurn = !actor.DebuffNullifiedThisTurn;
        actor.InterceptUsedThisTurn = !actor.InterceptUsedThisTurn;
        actor.specialAbility = SpecialAbility.None;
        SaveGameApplier.ApplyStatusFields(actor, saved);
        return saved;
    }

    static void PerTurnAbilityContinuation(GameSystems live)
    {
        using (var fixture = new EconomyR2Tests.Fixture(live))
        {
            var actor = fixture.Unit();
            var attacker = fixture.Opponent(actor.transform.position + Vector3.right, 10);
            attacker.specialAbility = SpecialAbility.None;
            actor.FirstSkillUsedThisTurn = actor.DebuffNullifiedThisTurn = actor.InterceptUsedThisTurn = true;
            actor.specialAbility = SpecialAbility.Efficiency;
            var spent = RoundtripAbilityFlags(actor);
            Check("all spent per-turn ability flags are captured in JSON", spent.FirstSkillUsedThisTurn
                && spent.DebuffNullifiedThisTurn && spent.InterceptUsedThisTurn);
            Check("all spent per-turn ability flags survive application", actor.FirstSkillUsedThisTurn
                && actor.DebuffNullifiedThisTurn && actor.InterceptUsedThisTurn);
            Check("loading cannot grant a second first-skill discount", SpecialAbilitySystem.GetSkillAPReduction(actor) == 0
                && fixture.AP.CalcSkillCost(4, actor) == 4);

            actor.specialAbility = SpecialAbility.IndomitableWill;
            actor.ActiveEffects.Clear(); RoundtripAbilityFlags(actor);
            Check("loading cannot grant a second debuff nullification", StatusEffectSystem.ApplyDebuff(actor, StatusEffectType.Poison)
                && StatusEffectSystem.HasDebuff(actor, StatusEffectType.Poison));

            actor.specialAbility = SpecialAbility.InterceptPosture;
            actor.HP = actor.MaxHP; RoundtripAbilityFlags(actor);
            Check("loading cannot grant a second intercept reduction", Mathf.Approximately(SpecialAbilitySystem.GetDefenderModifier(attacker, actor), 0));

            actor.FirstSkillUsedThisTurn = actor.DebuffNullifiedThisTurn = actor.InterceptUsedThisTurn = false;
            actor.specialAbility = SpecialAbility.Efficiency;
            var unused = RoundtripAbilityFlags(actor);
            Check("all unused per-turn ability flags also roundtrip", !unused.FirstSkillUsedThisTurn
                && !unused.DebuffNullifiedThisTurn && !unused.InterceptUsedThisTurn
                && !actor.FirstSkillUsedThisTurn && !actor.DebuffNullifiedThisTurn && !actor.InterceptUsedThisTurn);
            Check("legitimate unused first-skill discount survives loading", SpecialAbilitySystem.GetSkillAPReduction(actor) == 1
                && fixture.AP.CalcSkillCost(4, actor) == 3);

            actor.specialAbility = SpecialAbility.IndomitableWill;
            actor.ActiveEffects.Clear(); RoundtripAbilityFlags(actor);
            Check("legitimate unused debuff nullification survives loading", !StatusEffectSystem.ApplyDebuff(actor, StatusEffectType.Poison)
                && actor.DebuffNullifiedThisTurn && !StatusEffectSystem.HasDebuff(actor, StatusEffectType.Poison));

            actor.specialAbility = SpecialAbility.InterceptPosture;
            actor.HP = actor.MaxHP; RoundtripAbilityFlags(actor);
            Check("legitimate unused intercept reduction survives loading", Mathf.Approximately(
                SpecialAbilitySystem.GetDefenderModifier(attacker, actor), -.10f));
            SpecialAbilitySystem.OnAttackHit(attacker, actor, 10, true);
            Check("intercept is consumed by the ordinary hit rule", actor.InterceptUsedThisTurn
                && Mathf.Approximately(SpecialAbilitySystem.GetDefenderModifier(attacker, actor), 0));

            // Foresight uses current visibility rather than a once-per-turn flag; preserve that distinction.
            actor.specialAbility = SpecialAbility.Foresight;
            actor.VisionCell.Clear(); actor.VisionCell.Add(GridHelper.ToGrid(attacker.transform.position));
            RoundtripAbilityFlags(actor);
            Check("loaded foresight still reduces damage from a currently visible attacker", actor.specialAbility == SpecialAbility.Foresight
                && Mathf.Approximately(SpecialAbilitySystem.GetDefenderModifier(attacker, actor), -.05f));
            actor.VisionCell.Clear();
            Check("loaded foresight does not expose or protect against an unseen attacker", Mathf.Approximately(
                SpecialAbilitySystem.GetDefenderModifier(attacker, actor), 0));
        }
    }

    static void ResumePlayerCheckpoint(GameSystems live)
    {
        using (var fixture = new HubFixture(live))
        {
            var hub = fixture.Hub;
            var actor = fixture.Factions.Opponent(new Vector3(5, 1, 5), 10);
            actor.Fatigue = 3; actor.HasMovedThisTurn = true; actor.SkillCooldown = 2;
            actor.FirstSkillUsedThisTurn = actor.DebuffNullifiedThisTurn = actor.InterceptUsedThisTurn = true;
            fixture.Factions.State.SetAP(Team.Player, 5);
            var resources = JsonUtility.ToJson(fixture.Factions.State.PlayerResources);
            int turnsAlive = fixture.Factions.State.PlayerNation.TurnsAlive;
            var timer = hub.gameObject.AddComponent<TimerSystem>();
            timer.Init(hub, fixture.Factions.Crystals);
            timer.RestoreTurnTimeRemaining(113f);
            timer.PlayerTotalTime = 1200f; timer.EnemyTotalTime = 900f;
            hub.Systems.TimerSystem = timer;
            hub.Systems.UnitClick = hub.gameObject.AddComponent<UnitClick>();
            hub.Context.Turn = 7; hub.Context.ResumeSavedPlayerTurn = true;
            hub.ChangeState(new PlayerStart(hub));
            Check("saved player checkpoint resumes at its original visible turn", hub.Context.Turn == 8
                && !hub.Context.ResumeSavedPlayerTurn && hub.CurrentState is PlayerMove);
            Check("saved player checkpoint does not refill spent AP", fixture.Factions.State.GetAP(Team.Player) == 5);
            Check("saved player checkpoint does not grant production or charge upkeep twice", resources
                == JsonUtility.ToJson(fixture.Factions.State.PlayerResources)
                && fixture.Factions.State.PlayerNation.TurnsAlive == turnsAlive);
            Check("saved player checkpoint preserves spent movement/fatigue/cooldown", actor.HasMovedThisTurn
                && actor.Fatigue == 3 && actor.SkillCooldown == 2);
            Check("saved player checkpoint preserves all spent per-turn ability flags", actor.FirstSkillUsedThisTurn
                && actor.DebuffNullifiedThisTurn && actor.InterceptUsedThisTurn);
            Check("saved player checkpoint resumes the remaining timer without granting time", timer.IsRunning
                && timer.CurrentTeam == Team.Player && timer.TurnTimeRemaining == 113f
                && timer.PlayerTotalTime == 1200f && timer.EnemyTotalTime == 900f);
        }
    }

    static void Checkpoints(GameSystems live)
    {
        using (var fixture = new HubFixture(live))
        {
            var hub = fixture.Hub;
            var state = Field(typeof(TurnGenerator), "_stateManager");
            state.SetValue(hub, new PlayerMove(hub));
            Check("stable manual player phase allows checkpoint", SaveCheckpoint.CanCapture(hub, out string reason) && reason == null);
            state.SetValue(hub, new EnemyMove(hub));
            Check("enemy iterator phase refuses checkpoint with a visible reason", !SaveCheckpoint.CanCapture(hub, out reason) && !string.IsNullOrEmpty(reason));
            state.SetValue(hub, new PlayerMove(hub));
            hub.PlayerAI.SetEnabled(true, false);
            Check("developer autoplay refuses checkpoint even during PlayerMove", hub.DeveloperPlayerAIEnabled
                && !SaveCheckpoint.CanCapture(hub, out reason) && !string.IsNullOrEmpty(reason));
            hub.PlayerAI.SetEnabled(false, false);
            SetActive(fixture.Factions.Builder, true);
            Check("active building placement refuses checkpoint", !SaveCheckpoint.CanCapture(hub, out reason)
                && reason.Contains("配置"));
            SetActive(fixture.Factions.Builder, false);
            SetActive(hub.Systems.SummonSystem, true);
            Check("active summon placement refuses checkpoint", !SaveCheckpoint.CanCapture(hub, out reason)
                && reason.Contains("配置"));
            SetActive(hub.Systems.SummonSystem, false);
            Check("checkpoint becomes valid after interaction finishes", SaveCheckpoint.CanCapture(hub, out reason) && reason == null);
            Check("absent turn hub refuses checkpoint without throwing", !SaveCheckpoint.CanCapture(null, out reason) && !string.IsNullOrEmpty(reason));
        }
    }
    static void SetActive(MonoBehaviour component, bool active)
        => Field(component.GetType(), "<IsActive>k__BackingField").SetValue(component, active);

    static void CentralInput()
    {
        var go = new GameObject("Long-term input-only hub");
        var hub = go.AddComponent<TurnGenerator>(); hub.enabled = false; hub.MarkDeveloperMatch();
        var input = go.GetComponent<TurnInputHandler>();
        var settings = InputSystem.settings;
        var background = settings.backgroundBehavior;
        var routing = settings.editorInputBehaviorInPlayMode;
        bool runInBackground = Application.runInBackground;
        var previous = Keyboard.current;
        Keyboard keyboard = null;
        try
        {
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            Application.runInBackground = true;
            keyboard = InputSystem.AddDevice<Keyboard>(); InputSystem.EnableDevice(keyboard); keyboard.MakeCurrent();
            Send(input, keyboard, Key.C);
            Check("C routes focus through the central input context", hub.Context.FocusSelectedDown);
            Send(input, keyboard, Key.C);
            Check("held focus key is not repeated", !hub.Context.FocusSelectedDown);
            Send(input, keyboard); Send(input, keyboard, Key.Tab);
            Check("Tab routes forward unit cycling", hub.Context.CycleUnitDown && !hub.Context.CycleUnitBackwards);
            Send(input, keyboard); Send(input, keyboard, Key.LeftShift, Key.Tab);
            Check("left Shift+Tab routes backward cycling", hub.Context.CycleUnitDown && hub.Context.CycleUnitBackwards);
            Send(input, keyboard); Send(input, keyboard, Key.RightShift, Key.Tab);
            Check("right Shift+Tab routes backward cycling", hub.Context.CycleUnitDown && hub.Context.CycleUnitBackwards);
            Send(input, keyboard); Send(input, keyboard, Key.Z);
            Check("Z routes undo through the central input context", hub.Context.UndoMoveDown);
            input.FocusSelectedKey = Key.Home; input.CycleUnitKey = Key.PageDown; input.UndoMoveKey = Key.Backspace;
            Send(input, keyboard); Send(input, keyboard, Key.C, Key.Tab, Key.Z);
            Check("remapped actions no longer react to the previous keys", !hub.Context.FocusSelectedDown && !hub.Context.CycleUnitDown && !hub.Context.UndoMoveDown);
            Send(input, keyboard); Send(input, keyboard, Key.Home, Key.PageDown, Key.Backspace);
            Check("remapped keys reach all three central actions", hub.Context.FocusSelectedDown && hub.Context.CycleUnitDown && hub.Context.UndoMoveDown);
            input.FocusSelectedKey = input.CycleUnitKey = input.UndoMoveKey = Key.None;
            Send(input, keyboard); Send(input, keyboard, Key.Home, Key.PageDown, Key.Backspace);
            Check("unbound keys safely disable all three actions", !hub.Context.FocusSelectedDown && !hub.Context.CycleUnitDown && !hub.Context.UndoMoveDown);
        }
        finally
        {
            if (keyboard != null) { InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update(); InputSystem.RemoveDevice(keyboard); }
            if (previous != null && previous.added) previous.MakeCurrent();
            settings.backgroundBehavior = background; settings.editorInputBehaviorInPlayMode = routing;
            Application.runInBackground = runInBackground;
            UnityEngine.Object.DestroyImmediate(go);
        }
    }
    static void Send(TurnInputHandler input, Keyboard keyboard, params Key[] keys)
    { InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); InputSystem.Update(); input.Tick(); }

    static int APSubscribers(APSystem ap)
        => (Field(typeof(APSystem), "OnNonMoveAction").GetValue(ap) as Delegate)?.GetInvocationList().Length ?? 0;
    static int SceneSubscribers()
    {
        var field = typeof(SceneManager).GetField("activeSceneChanged", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingFieldException("SceneManager.activeSceneChanged");
        return (field.GetValue(null) as Delegate)?.GetInvocationList().Length ?? 0;
    }

    static void UndoLifetime(GameSystems live)
    {
        using (var fixture = new EconomyR2Tests.Fixture(live))
        {
            var actor = fixture.Unit(); int subscribers = APSubscribers(fixture.AP);
            for (int i = 0; i < 10; i++)
            {
                var undo = new MoveUndoSystem(fixture.AP, live.MoveGenerator);
                try
                {
                    Check("undo subscribes once on battle " + i, APSubscribers(fixture.AP) == subscribers + 1);
                    undo.Record(actor, Vector3.zero, Vector3.right, 3);
                    actor.Fatigue = 0; fixture.State.SetAP(Team.Enemy, 40);
                    fixture.AP.Consume(Team.Enemy, APSystem.ActionType.Attack, actor);
                    Check("enemy action preserves player undo on battle " + i, undo.CanUndo);
                    actor.Fatigue = 0; fixture.State.SetAP(Team.Player, 40);
                    fixture.AP.Consume(Team.Player, APSystem.ActionType.Attack, actor);
                    Check("irreversible player action clears undo on battle " + i, !undo.CanUndo);
                    undo.Record(actor, Vector3.zero, Vector3.right, 3);
                }
                finally { undo.Dispose(); undo.Dispose(); }
                Check("idempotent undo disposal clears references and subscription " + i, undo.Count == 0 && APSubscribers(fixture.AP) == subscribers);
            }
        }
    }

    static void BattleHubLifetime(GameSystems live)
    {
        int sceneSubscribers = SceneSubscribers();
        for (int battle = 0; battle < 10; battle++)
        {
            using (var fixture = new HubFixture(live))
            {
                var actor = fixture.Factions.Unit();
                var commander = InitSaved(fixture, live, Saved(56192));
                var history = (Dictionary<Status, List<Vector3Int>>)Field(typeof(AICommander), "_unitPositionHistory").GetValue(commander);
                var acted = (HashSet<Status>)Field(typeof(AICommander), "_actedUnits").GetValue(commander);
                var selected = (List<(AIAction action, float score)>)Field(typeof(AICommander), "selectionBuffer").GetValue(commander);
                AIRuntimeMaintenance.RecordPosition(history, actor, Vector3Int.zero);
                acted.Add(actor); selected.Add((new AIAction { Unit = actor, ActionType = AIActionType.Move }, 20));
                fixture.Hub.Systems.MoveUndoSystem = new MoveUndoSystem(fixture.Factions.AP, live.MoveGenerator);
                fixture.Hub.Systems.MoveUndoSystem.Record(actor, Vector3.zero, Vector3.right, 3);
                Check("battle hub owns one additional camera scene subscription " + battle, SceneSubscribers() == sceneSubscribers + 1);
                Check("battle commander holds real unit references before release " + battle, commander.GetRuntimeCounts().PositionHistoryUnits == 1
                    && commander.GetRuntimeCounts().PositionHistoryEntries == 1 && acted.Count == 1 && selected.Count == 1);
                if ((battle & 1) == 0) commander.ReleaseRuntimeReferences();
                fixture.DestroyHub(); // Also exercises TurnGenerator's OnDestroy release and Undo.Dispose.
                var counts = commander.GetRuntimeCounts();
                Check("battle destruction releases commander runtime unit references " + battle, counts.PositionHistoryUnits == 0
                    && counts.PositionHistoryEntries == 0 && acted.Count == 0 && selected.Count == 0);
                Check("battle destruction releases AP subscription " + battle, APSubscribers(fixture.Factions.AP) == 0);
                Check("battle destruction returns scene subscribers to baseline " + battle, SceneSubscribers() == sceneSubscribers);
            }
        }
    }

    sealed class HubFixture : IDisposable
    {
        readonly GameObject go;
        readonly AIMode oldMode;
        public readonly EconomyR2Tests.Fixture Factions;
        public readonly TurnGenerator Hub;
        public HubFixture(GameSystems live)
        {
            oldMode = AIConfig.Mode;
            Factions = new EconomyR2Tests.Fixture(live);
            go = new GameObject("Long-term isolated battle hub");
            Hub = go.AddComponent<TurnGenerator>(); Hub.enabled = false; Hub.MarkDeveloperMatch();
            AIConfig.Mode = AIMode.PureRule;
            var systems = Hub.Systems;
            systems.MapCreate = live.MapCreate; systems.MoveGenerator = live.MoveGenerator;
            systems.AttackGenerator = live.AttackGenerator; systems.BattleSystem = live.BattleSystem;
            systems.UnitSetting = Factions.Units; systems.FactionState = Factions.State;
            systems.CrystalSystem = Factions.Crystals; systems.BuildSystem = Factions.Builder;
            systems.APSystem = Factions.AP; systems.TerritorySystem = live.TerritorySystem;
            systems.SummonSystem = go.AddComponent<SummonSystem>();
            systems.SummonSystem.Init(Hub, live.TerritorySystem, Factions.AP, Factions.State,
                live.MoveGenerator, live.MapCreate, Factions.Units, null);
        }
        public void DestroyHub() { if (go != null) UnityEngine.Object.DestroyImmediate(go); }
        public void Dispose()
        {
            try { DestroyHub(); }
            finally { Factions.Dispose(); AIConfig.Mode = oldMode; }
        }
    }
}
#endif
