#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>Optional rendered fixtures; no asset definitions or ordinary balance matches are changed.</summary>
[InitializeOnLoad]
public static class SpecialSkillVisualSmoke
{
    const string Running = "FK.SpecialSkillVisualSmoke";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int stage;
    static double ready, started;
    static GameSystems systems;
    static Status mage, crossbow, targetA, targetB;
    static readonly List<Status> allies = new List<Status>();
    static SpecialSkillVisualSmoke() { EditorApplication.update += Tick; }

    public static void Run()
    {
        if (!Application.dataPath.Contains("UnityValidation")) throw new InvalidOperationException("Isolated validation project only");
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "FantasyKingdom_SpecialSkillVisual_" + Guid.NewGuid().ToString("N");
        TerrainEnvironmentEditor.InstallDefaults();
        SessionState.SetBool(Running, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity"); EditorApplication.EnterPlaymode();
    }

    static void Tick()
    {
        if (!SessionState.GetBool(Running, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (started == 0) started = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - started > 120) throw new TimeoutException("Special UI capture did not finish within two minutes");
            if (EditorApplication.timeSinceStartup < ready) return;
            if (stage == 0)
            {
                var button = UIBuilder.ScreenCanvas?.transform.Find("TitleScreenOverlay/ButtonArea/はじめからBtn")?.GetComponent<Button>();
                if (button == null) return;
                UnityEngine.Random.InitState(314159); button.onClick.Invoke(); stage++; return;
            }
            if (stage == 1)
            {
                var turn = UnityEngine.Object.FindFirstObjectByType<TurnGenerator>();
                if (turn == null || !(turn.CurrentState is PlayerMove) || turn.Systems.UnitPanelUI == null) return;
                systems = turn.Systems; turn.enabled = false;
                PrepareFixture();
                Camera.main.fieldOfView = 30; // Rendered fixture only: keep the effect cells readable.
                systems.TurnGenerator.Context.SelectUnit = mage; systems.UnitPanelUI.Show(mage);
                turn.GameAction.Enable();
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
                var keyboard = InputSystem.AddDevice<Keyboard>(); keyboard.MakeCurrent();
                turn.GameAction.Disable(); turn.GameAction.devices = new InputDevice[] { keyboard }; turn.GameAction.Enable();
                InputSystem.Update();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Digit2)); InputSystem.Update();
                Debug.Log("[SpecialSkillVisual] key2 pressed=" + turn.GameAction.GamePlay.SelectSkill.WasPressedThisFrame()
                    + " value=" + turn.GameAction.GamePlay.SelectSkill.ReadValue<float>()
                    + " raw=" + keyboard.digit2Key.isPressed + " controls=" + turn.GameAction.GamePlay.SelectSkill.controls.Count + " enabled=" + turn.GameAction.GamePlay.SelectSkill.enabled
                    + " state=" + turn.CurrentState.GetType().Name + " selection=" + (turn.Context.SelectUnit == mage)
                    + " canIssue=" + typeof(UnitPanelUI).GetMethod("CanIssueCommand", Private).Invoke(systems.UnitPanelUI, null));
                turn.GetComponent<TurnInputHandler>().Tick();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update(); InputSystem.RemoveDevice(keyboard);
                var targeting = turn.CurrentState as PlayerAttack;
                if (targeting == null || !targeting.IsFixedSkillTargeting || targeting.SpecialRangeCells.Count == 0 || turn.Context.SelectSkillDown)
                    throw new InvalidOperationException("Physical 2 did not enter map skill targeting exactly once");
                targeting.PreviewSpecialTarget(targetA);
                Debug.Log("[SpecialSkillVisual] Physical 2 opened map skill targeting with range and effect preview");
                stage = 101; ready = EditorApplication.timeSinceStartup + 1; return;
            }
            if (stage == 101)
            {
                Capture("skill-target-range.png");
                var targeting = (PlayerAttack)systems.TurnGenerator.CurrentState;
                if (!targeting.TryUseSpecialTarget(targetA)) throw new InvalidOperationException("Map target did not open sacrifice choices");
                var modal = (SpecialSkillSelectionUI)typeof(SpecialSkillSelectionUI).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                if (modal == null) throw new InvalidOperationException("Sacrifice choices did not open after map target click");
                Debug.Log("[SpecialSkillVisual] Physical 2 opened the unique skill without ordinary-skill dispatch");
                var targets = (Dictionary<Status, Image>)typeof(SpecialSkillSelectionUI).GetField("targetImages", Private).GetValue(modal);
                targets[targetA].GetComponent<Button>().onClick.Invoke();
                var sacrifices = (Dictionary<Status, TMPro.TextMeshProUGUI>)typeof(SpecialSkillSelectionUI).GetField("sacrificeLabels", Private).GetValue(modal);
                foreach (var actor in allies.Take(2)) sacrifices[actor].GetComponentInParent<Button>().onClick.Invoke();
                stage = 2; ready = EditorApplication.timeSinceStartup + 1; return;
            }
            if (stage == 2)
            {
                Capture("magic-sacrifice-modal.png");
                if (systems.TurnGenerator.CurrentState is PlayerAttack completed) { completed.Reset(); systems.TurnGenerator.ChangeState(completed.move); }
                SpecialSkillSelectionUI.CloseCurrent();
                mage.Special.ritualTargetId = targetA.ReflectionLifeId; mage.Special.storedSacrificeDamage = 45;
                mage.Special.ownerTurn = 1; mage.Special.ritualFireTurn = 2;
                mage.Special.damageNullTurns = 2; mage.Special.damageNullUnused = true;
                systems.UnitPanelUI.Show(mage); SpecialSkillSelectionUI.Show(mage, systems, true);
                stage++; ready = EditorApplication.timeSinceStartup + 1; return;
            }
            if (stage == 3)
            {
                Capture("magic-state-details.png"); SpecialSkillSelectionUI.CloseCurrent();
                systems.TurnGenerator.Context.SelectUnit = crossbow; systems.UnitPanelUI.Show(crossbow);
                stage++; ready = EditorApplication.timeSinceStartup + 1; return;
            }
            if (stage == 4)
            {
                Capture("crossbow-lightning-line.png");
                UnityEngine.Object.FindFirstObjectByType<SlidePanelUI>(FindObjectsInactive.Include).OpenUnitPanel();
                stage++; ready = EditorApplication.timeSinceStartup + 1; return;
            }
            if (stage == 5)
            {
                var row = UIBuilder.ScreenCanvas.GetComponentsInChildren<SummonUnitTooltip>().First();
                row.OnPointerEnter(new PointerEventData(EventSystem.current) { position = new Vector2(500, 500) });
                stage++; ready = EditorApplication.timeSinceStartup + .3; return;
            }
            if (stage == 6)
            {
                Capture("summon-hover-details.png"); SummonUnitTooltip.HideCurrent();
                UnityEngine.Object.FindFirstObjectByType<SlidePanelUI>(FindObjectsInactive.Include).ClosePanel();
                var berserker = allies[0]; berserker.kind = Kind.Berserker; berserker.Special = new SpecialState();
                berserker.GrowthData.displayName = "狂戦士（表示検証）";
                var rage = ScriptableObject.CreateInstance<SpecialSkillDefinition>(); rage.ability = UnitSpecialSkill.BerserkerRage;
                rage.ApplySpecifiedAPCost(); rage.cooldown = 0;
                rage.berserkerAttackCurve = AnimationCurve.Linear(0, 1, 5, 2.5f);
                rage.berserkerDefenceCurve = AnimationCurve.Linear(0, 1, 5, .5f);
                rage.berserkerAttackAPCurve = AnimationCurve.Linear(0, 1, 1, 5);
                berserker.GrowthData.specialSkill = rage; systems.FactionState.SetAP(Team.Player, 35);
                int before = systems.FactionState.GetAP(Team.Player);
                if (!SpecialSkillRuntime.TryExecute(new SpecialSkillCommand(berserker), systems, out _)
                    || !SpecialSkillRuntime.TryExecute(new SpecialSkillCommand(berserker), systems, out _)
                    || berserker.Special.berserkerStage != 2 || before - systems.FactionState.GetAP(Team.Player) != 13)
                    throw new InvalidOperationException("Berserker did not stack two real casts for five plus eight AP");
                Debug.Log("[SpecialSkillVisual] Berserker stacked 0 -> 1 -> 2 with real AP5 + AP8; next stage requires AP11");
                systems.TurnGenerator.Context.SelectUnit = berserker; systems.UnitPanelUI.Show(berserker);
                SpecialSkillSelectionUI.Show(berserker, systems);
                stage++; ready = EditorApplication.timeSinceStartup + .7; return;
            }
            Capture("berserker-stack-progress.png");
            File.WriteAllText(Path.Combine(OutputFolder(), "capture-notes.txt"),
                "通常シーンの実カメラ・実HUDをRenderTextureで描画した表示検証です。\n特殊能力のCD0/供儀上限8・強化曲線は表示検証専用で、本番の未確定値は変更していません。APは指定表を使用。\n仮想Keyboardの物理2入力を既存InputAction/TurnInputHandlerへ流し、マップ上の使用範囲と効果範囲が表示され、対象クリック後だけ供儀選択が開くことを確認。\n範囲表示・Magic供儀・予約一覧・Crossbow可視雷線・召喚hover・狂戦士連続2回使用後の6画面。狂戦士は実際にAP5+8を支払い0→1→2段階へ上昇。ゲームバランス試合の記録ではありません。\n");
            Debug.Log("[SpecialSkillVisual] ALL CAPTURED " + OutputFolder());
            SessionState.SetBool(Running, false); EditorApplication.Exit(0);
        }
        catch (Exception error)
        { Debug.LogException(error); SessionState.SetBool(Running, false); EditorApplication.Exit(1); }
    }

    static void PrepareFixture()
    {
        var positions = systems.MapCreate.SetPos.Where(position => GridHelper.ChebyshevDistance(position, systems.CrystalSystem.PCP) <= 3
            && !systems.MoveGenerator.IsOccupied(GridHelper.ToGridXZ(position))).OrderBy(position => Vector3.SqrMagnitude(position - systems.CrystalSystem.PCP)).Take(7).ToArray();
        if (positions.Length < 7) throw new InvalidOperationException("Not enough visible fixture cells near the player base");
        Status Spawn(Kind kind, string prefabName, Team team, int index)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Units/Player/" + prefabName + ".prefab");
            var go = UnityEngine.Object.Instantiate(prefab, positions[index], Quaternion.identity,
                team == Team.Player ? systems.UnitSetting.PlayerUnit : systems.UnitSetting.EnemyUnit);
            var actor = go.GetComponentInChildren<Status>(); actor.type = Type.Unit; actor.team = team; actor.kind = kind;
            actor.IsPrimaryCommander = false; actor.HP = actor.MaxHP = 150; actor.ATK = 25; actor.DEF = 10; actor.Special = new SpecialState();
            var data = UnitStaticData.CreateUnitData(kind); data.displayName = KindNameJP.Get(kind) + "（表示検証）"; actor.GrowthData = data;
            actor.unitDefinitionId = string.Empty; return actor;
        }
        mage = Spawn(Kind.Magic, "Magic", Team.Player, 0);
        allies.Add(Spawn(Kind.Knight, "Knight", Team.Player, 1));
        allies.Add(Spawn(Kind.Guardian, "Guardian", Team.Player, 2));
        crossbow = Spawn(Kind.Crossbow, "Crossbow", Team.Player, 3);
        targetA = Spawn(Kind.Knight, "Knight", Team.Enemy, 4);
        targetB = Spawn(Kind.Archer, "Archer", Team.Enemy, 5);
        allies.Add(Spawn(Kind.Scout, "Scout", Team.Player, 6));
        var magicDefinition = ScriptableObject.CreateInstance<SpecialSkillDefinition>();
        magicDefinition.ability = UnitSpecialSkill.MagicRitual; magicDefinition.ApplySpecifiedAPCost(); magicDefinition.cooldown = 0; magicDefinition.sacrificeLimit = 8;
        mage.GrowthData.specialSkill = magicDefinition;
        var crossbowDefinition = ScriptableObject.CreateInstance<SpecialSkillDefinition>();
        crossbowDefinition.ability = UnitSpecialSkill.CrossbowStakes; crossbowDefinition.ApplySpecifiedAPCost(); crossbowDefinition.cooldown = 0; crossbowDefinition.lightningFactionMask = SpecialFactionMask.Hostile; crossbowDefinition.crossbowInitialAmmo = 0;
        crossbow.GrowthData.specialSkill = crossbowDefinition;
        crossbow.Special.ownerTurn = 2; crossbow.Special.stakeAId = targetA.ReflectionLifeId; crossbow.Special.stakeBId = targetB.ReflectionLifeId;
        crossbow.Special.stakeAAppliedTurn = 1; crossbow.Special.stakeBAppliedTurn = 2;
        systems.FactionState.SetAP(Team.Player, 35); systems.MoveGenerator.UnitPointCore(); systems.RefreshVision();
        mage.VisionCell.Add(targetA.GridPosition); mage.VisionCell.Add(targetB.GridPosition);
        // All fixture cells are within the real player crystal's normal revealed region.
        if (!systems.VisionGenerator.IsInVisionXZ(Team.Player, targetA.transform.position)
            || !systems.VisionGenerator.IsInVisionXZ(Team.Player, targetB.transform.position))
            throw new InvalidOperationException("Fixture targets must be genuinely visible to the player HUD");
    }

    static string OutputFolder()
    {
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../r1-skill-captures-20261017"));
        Directory.CreateDirectory(folder); return folder;
    }
    static void Capture(string name)
    {
        var camera = Camera.main;
        var overlays = new List<(Canvas canvas, RenderMode mode, Camera camera, float plane)>();
        try
        {
            foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                overlays.Add((canvas, canvas.renderMode, canvas.worldCamera, canvas.planeDistance));
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            }
            Canvas.ForceUpdateCanvases();
            VisualRegressionTests.CaptureHUD("special-" + name);
        }
        finally
        {
            foreach (var entry in overlays)
            { entry.canvas.renderMode = entry.mode; entry.canvas.worldCamera = entry.camera; entry.canvas.planeDistance = entry.plane; }
        }
        string source = Path.GetFullPath(Path.Combine(Application.dataPath, "../../visual-captures/special-" + name));
        File.Copy(source, Path.Combine(OutputFolder(), name), true);
        Debug.Log("[SpecialSkillVisual] CAPTURED " + name);
    }
}
#endif
