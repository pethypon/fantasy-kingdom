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
                var modal = (SpecialSkillSelectionUI)typeof(SpecialSkillSelectionUI).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                if (modal == null || turn.Context.SelectSkillDown) throw new InvalidOperationException("Physical 2 did not open the unique skill exactly once");
                Debug.Log("[SpecialSkillVisual] Physical 2 opened the unique skill without ordinary-skill dispatch");
                var targets = (Dictionary<Status, Image>)typeof(SpecialSkillSelectionUI).GetField("targetImages", Private).GetValue(modal);
                targets[targetA].GetComponent<Button>().onClick.Invoke();
                var sacrifices = (Dictionary<Status, TMPro.TextMeshProUGUI>)typeof(SpecialSkillSelectionUI).GetField("sacrificeLabels", Private).GetValue(modal);
                foreach (var actor in allies.Take(2)) sacrifices[actor].GetComponentInParent<Button>().onClick.Invoke();
                stage++; ready = EditorApplication.timeSinceStartup + 1; return;
            }
            if (stage == 2)
            {
                Capture("magic-sacrifice-modal.png");
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
            Capture("summon-hover-details.png");
            File.WriteAllText(Path.Combine(OutputFolder(), "capture-notes.txt"),
                "通常シーンの実カメラ・実HUDをRenderTextureで描画した表示検証です。\n特殊能力のCD0/供儀上限8は表示検証専用で、本番の未確定値は変更していません。APは指定表を使用。\n仮想Keyboardの物理2入力を既存InputAction/TurnInputHandlerへ流し、固有モーダルだけが開くことを確認。\nMagic供儀・予約一覧・Crossbow可視雷線・召喚hoverの4画面。ゲームバランス試合の記録ではありません。\n");
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
        crossbowDefinition.ability = UnitSpecialSkill.CrossbowStakes; crossbowDefinition.ApplySpecifiedAPCost(); crossbowDefinition.cooldown = 0; crossbowDefinition.lightningFactionMask = SpecialFactionMask.Hostile;
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
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../special-skill-captures-20261015"));
        Directory.CreateDirectory(folder); return folder;
    }
    static void Capture(string name)
    {
        Canvas.ForceUpdateCanvases();
        VisualRegressionTests.CaptureHUD("special-" + name);
        string source = Path.GetFullPath(Path.Combine(Application.dataPath, "../../visual-captures/special-" + name));
        File.Copy(source, Path.Combine(OutputFolder(), name), true);
        Debug.Log("[SpecialSkillVisual] CAPTURED " + name);
    }
}
#endif
