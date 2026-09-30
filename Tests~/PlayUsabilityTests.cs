#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
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
        }
    }
}
#endif
