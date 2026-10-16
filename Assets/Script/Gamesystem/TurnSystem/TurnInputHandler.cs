using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Gameplay inputs, hidden hotkeys, and camera pointer input share one scene-owned router.
/// </summary>
// Owner: TurnGenerator. Lifetime: scene. Maximum retained items: four instance event subscriptions and one owned MenuBack action.
// Cleanup timing: TurnGenerator destruction; no static input events. Failure: absent devices produce no command.
[DisallowMultipleComponent]
public class TurnInputHandler : MonoBehaviour
{
    private TurnGenerator _turn;
    private InputAction menuBackAction;
    [Flags] public enum HiddenCommand { None = 0, ToggleDiary = 1, ToggleTopDown = 2 }
    public event Action ToggleAIDiaryRequested;
    public event Action ToggleTopDownRequested;
    public event Action DiaryCloseRequested;
    public event Action DeveloperAIToggleRequested;
    public Key DeveloperAIToggleKey { get; set; } = Key.F8;
    public Key FocusSelectedKey { get; set; } = Key.C;
    public Key CycleUnitKey { get; set; } = Key.Tab;
    public Key UndoMoveKey { get; set; } = Key.Z;
    public Vector2 PointerPosition { get; private set; }
    public bool HasPointer { get; private set; }
    public bool MiddleButtonDown { get; private set; }
    public bool MiddleButtonHeld { get; private set; }
    public bool PointerOverUI { get; private set; }
    public bool SpaceConsumedThisFrame { get; private set; }
    /// <summary>The existing MenuBack action retains its name and binding and can be remapped by input tooling.</summary>
    public InputAction MenuBackAction => menuBackAction;

    public void Bind(TurnGenerator turn)
    {
        _turn = turn;
        EnsureMenuBackAction();
    }

    private void EnsureMenuBackAction()
    {
        if (menuBackAction == null)
            menuBackAction = new InputAction("MenuBack", InputActionType.Button, "<Keyboard>/escape");
        if (isActiveAndEnabled) menuBackAction.Enable();
    }
    private void OnEnable() => EnsureMenuBackAction();
    private void OnDisable() => menuBackAction?.Disable();
    private void OnDestroy() { menuBackAction?.Dispose(); menuBackAction = null; }

    /// <summary>Pure chord routing. Down edges, not held keys, determine one toggle per press.</summary>
    public static HiddenCommand EvaluateHiddenCommands(bool leftShift, bool rightShift, bool f7Down,
        bool leftCtrl, bool rightCtrl, bool spaceDown)
    {
        HiddenCommand result = HiddenCommand.None;
        if ((leftShift || rightShift) && f7Down) result |= HiddenCommand.ToggleDiary;
        if ((leftCtrl || rightCtrl) && spaceDown) result |= HiddenCommand.ToggleTopDown;
        return result;
    }

    /// <summary>Read once per TurnGenerator frame; hidden keys also work while the menu is open.</summary>
    public void Tick()
    {
        if (_turn == null) return;
        var keyboard = Keyboard.current;
        HiddenCommand hidden = keyboard == null ? HiddenCommand.None : EvaluateHiddenCommands(
            keyboard.leftShiftKey.isPressed, keyboard.rightShiftKey.isPressed, keyboard.f7Key.wasPressedThisFrame,
            keyboard.leftCtrlKey.isPressed, keyboard.rightCtrlKey.isPressed, keyboard.spaceKey.wasPressedThisFrame);
        SpaceConsumedThisFrame = (hidden & HiddenCommand.ToggleTopDown) != 0;
        if ((hidden & HiddenCommand.ToggleDiary) != 0) ToggleAIDiaryRequested?.Invoke();
        if (SpaceConsumedThisFrame) ToggleTopDownRequested?.Invoke();

        var menu = GameMenuUI.Instance;
        if (menuBackAction != null && menuBackAction.WasPressedThisFrame())
        {
            if (menu != null && menu.IsOpen) menu.Back();
            else DiaryCloseRequested?.Invoke();
        }
        bool menuOpen = menu != null && menu.IsOpen;
        if (!menuOpen && keyboard != null && DeveloperAIToggleKey != Key.None
            && keyboard[DeveloperAIToggleKey].wasPressedThisFrame)
            DeveloperAIToggleRequested?.Invoke();

        var mouse = Mouse.current;
        HasPointer = mouse != null;
        PointerPosition = mouse == null ? default : mouse.position.ReadValue();
        PointerOverUI = UnityEngine.EventSystems.EventSystem.current != null
            && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
        MiddleButtonDown = !menuOpen && !PointerOverUI && mouse != null && mouse.middleButton.wasPressedThisFrame;
        MiddleButtonHeld = !menuOpen && mouse != null && mouse.middleButton.isPressed;
        var gameaction = _turn.GameAction;
        if (gameaction == null) return;

        var ctx = _turn.Context;
        var command = ctx.ConsumeUICommand();
        if (menuOpen) command = GameContext.UICommand.None;
        ctx.MoveInput = menuOpen ? Vector2.zero : gameaction.GamePlay.Move.ReadValue<Vector2>();
        ctx.ScrollInput = menuOpen || PointerOverUI ? 0f : gameaction.GamePlay.Scroll.ReadValue<float>();
        // Consuming this chord also protects future Space bindings in the existing GameAction asset.
        bool blocked = menuOpen || SpaceConsumedThisFrame;
        ctx.LeftClickDown = !blocked && !PointerOverUI && gameaction.GamePlay.LeftClick.WasPressedThisFrame();
        ctx.RightClickDown = !blocked && (command == GameContext.UICommand.Cancel || gameaction.GamePlay.RightClick.WasPressedThisFrame());
        ctx.TurnEndDown = !blocked && (command == GameContext.UICommand.EndTurn || gameaction.GamePlay.TurnEnd.WasPressedThisFrame());
        ctx.SelectNormalDown = !blocked && (command == GameContext.UICommand.Attack || gameaction.GamePlay.SelectNormal.WasPressedThisFrame());
        bool typing = IsTypingInField();
        bool skillKeyDown = gameaction.GamePlay.SelectSkill.WasPressedThisFrame();
        ctx.SelectSkillDown = !blocked && !typing && (command == GameContext.UICommand.Skill || skillKeyDown);
        if (ctx.SelectSkillDown && command != GameContext.UICommand.Skill && skillKeyDown
            && _turn.Systems.UnitPanelUI != null && _turn.Systems.UnitPanelUI.TryOpenSpecialShortcut())
            ctx.SelectSkillDown = false; // The physical 2 key is handled once; explicit ordinary-skill buttons retain their command.
        ctx.ToggleNSDown = !blocked && gameaction.GamePlay.ToggleNS.WasPressedThisFrame();
        ctx.FocusSelectedDown = !blocked && Pressed(keyboard, FocusSelectedKey);
        ctx.CycleUnitDown = !blocked && Pressed(keyboard, CycleUnitKey);
        ctx.CycleUnitBackwards = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
        ctx.UndoMoveDown = !blocked && Pressed(keyboard, UndoMoveKey);
    }
    static bool IsTypingInField()
    {
        var selected = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
        return selected != null && (selected.GetComponentInParent<TMPro.TMP_InputField>() != null
            || selected.GetComponentInParent<UnityEngine.UI.InputField>() != null);
    }
    static bool Pressed(Keyboard keyboard, Key key) => keyboard != null && key != Key.None && keyboard[key].wasPressedThisFrame;
}
