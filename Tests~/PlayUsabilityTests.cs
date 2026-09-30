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
        var oldBackground = InputSystem.settings.backgroundBehavior;
        var oldEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.EnableDevice(keyboard);
        try
        {
            menu.Open();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
            // SceneSmoke runs from EditorApplication.update; explicitly process the
            // player loop, rather than updating only the editor's input state.
            typeof(InputSystem).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Static,
                null, new[] { typeof(InputUpdateType) }, null).Invoke(null, new object[] { InputUpdateType.Dynamic });
            Check("Escape input closes menu", !menu.IsOpen);
        }
        finally { InputSystem.RemoveDevice(keyboard); InputSystem.settings.backgroundBehavior = oldBackground; InputSystem.settings.editorInputBehaviorInPlayMode = oldEditorInput; menu.Close(); }
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
            ((System.Collections.IList)typeof(ActionLogUI).GetField("_entries", Flags).GetValue(log)).Clear();
            ActionLogUI.Log("短いログ");
            typeof(ActionLogUI).GetMethod("RefreshText", Flags).Invoke(log, null);
            var panel = (RectTransform)typeof(ActionLogUI).GetField("_panelRect", Flags).GetValue(log);
            Check("short log shrinks background", panel.rect.height >= 36f && panel.rect.height < 100f);
        }
    }
}
#endif
