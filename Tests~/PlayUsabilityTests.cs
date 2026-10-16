#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
public static class PlayUsabilityTests
{
    static readonly BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    static void Check(string name, bool ok) { if (!ok) throw new Exception(name); Debug.Log("[PlayUsability] PASS " + name); }
    public static void Run()
    {
        var menu = GameMenuUI.Instance;
        menu.Open();
        var overlay = typeof(GameMenuUI).GetField("overlay", Flags).GetValue(menu);
        menu.Open();
        Check("opening open menu preserves its hierarchy", ReferenceEquals(overlay, typeof(GameMenuUI).GetField("overlay", Flags).GetValue(menu)));
        GameManualUI.ShowOrToggle();
        menu.Back();
        Check("back closes manual only", !GameManualUI.Instance.IsOpen && menu.IsOpen);
        typeof(GameMenuUI).GetMethod("ShowSlotPanel", Flags).Invoke(menu, new object[] { false });
        menu.Back();
        Check("back from slots keeps menu open", menu.IsOpen && typeof(GameMenuUI).GetField("slotPanel", Flags).GetValue(menu) == null);
        menu.Back();
        Check("back closes main menu immediately", !menu.IsOpen);
        var keyboard = InputSystem.AddDevice<Keyboard>();
        var input = UnityEngine.Object.FindFirstObjectByType<TurnGenerator>().GetComponent<TurnInputHandler>();
        var oldBackground = InputSystem.settings.backgroundBehavior;
        var oldEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
        string oldBackOverride = input.MenuBackAction.bindings[0].overridePath;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.EnableDevice(keyboard);
        try
        {
            menu.Open();
            ReadInput(input, keyboard, Key.Escape);
            Check("Escape input closes menu", !menu.IsOpen);
            ReadInput(input, keyboard);
            input.MenuBackAction.ApplyBindingOverride(0, new InputBinding { overridePath = "<Keyboard>/backspace" });
            menu.Open();
            ReadInput(input, keyboard, Key.Escape);
            Check("central menu routing respects a remapped Back action", menu.IsOpen);
            ReadInput(input, keyboard, Key.Backspace);
            Check("remapped Back key closes the existing menu", !menu.IsOpen);
        }
        finally
        {
            ReadInput(input, keyboard);
            if (oldBackOverride == null) input.MenuBackAction.RemoveBindingOverride(0);
            else input.MenuBackAction.ApplyBindingOverride(0, new InputBinding { overridePath = oldBackOverride });
            InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.backgroundBehavior = oldBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = oldEditorInput;
            menu.Close();
        }
        var log = ActionLogUI.Instance;
        if (log != null)
        {
            log.enabled = false;
            ActionLogUI.LogAttack("テスト", "対象", 10, true);
            Check("new log wakes suspended updater", log.enabled);
            Check("logs have no raycaster", log.GetComponentInChildren<GraphicRaycaster>() == null);
            bool passive = true;
            foreach (var g in log.GetComponentsInChildren<Graphic>()) passive &= !g.raycastTarget;
            Check("logs cannot block board input", passive);
            typeof(ActionLogUI).GetMethod("Update", Flags).Invoke(log, null); // Deliver the preceding critical display before the next fixture.
            // Earlier fixtures can enqueue more than one frame's delivery budget.
            // Isolate this sizing fixture from both displayed and still-pending events.
            ((AIPresentationQueue)typeof(ActionLogUI).GetField("presentation", Flags).GetValue(log)).Clear();
            ((System.Collections.IList)typeof(ActionLogUI).GetField("_entries", Flags).GetValue(log)).Clear();
            ActionLogUI.Log("短いログ");
            typeof(ActionLogUI).GetMethod("Update", Flags).Invoke(log, null); // Display events are intentionally drained on a render frame.
            typeof(ActionLogUI).GetMethod("RefreshText", Flags).Invoke(log, null);
            var panel = (RectTransform)typeof(ActionLogUI).GetField("_panelRect", Flags).GetValue(log);
            Check("short log shrinks background", panel.rect.height >= 36f && panel.rect.height < 100f);
        }
    }

    static void ReadInput(TurnInputHandler input, Keyboard keyboard, params Key[] keys)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        // Synthetic events need the same gameplay update followed by the central router as a real frame.
        typeof(InputSystem).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Static,
            null, new[] { typeof(InputUpdateType) }, null).Invoke(null, new object[] { InputUpdateType.Dynamic });
        input.Tick();
    }
}
#endif
