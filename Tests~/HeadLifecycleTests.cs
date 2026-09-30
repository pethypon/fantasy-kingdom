#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;
public static class HeadLifecycleTests
{
    static void Check(string label, bool value) { if (!value) throw new Exception(label); Debug.Log("[HeadLifecycle] PASS " + label); }
    public static void Run()
    {
        var owner = new GameObject("Head lifecycle fixture");
        var status = owner.AddComponent<Status>();
        status.type = Type.Unit; status.kind = Kind.Scout; status.HP = status.MaxHP = 20;
        var head = UnitHeadUI.Attach(owner);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var graphic = (GameObject)typeof(UnitHeadUI).GetField("headUIObject", flags).GetValue(head);
        try
        {
            Check("head created", graphic != null);
            Check("attach is idempotent", UnitHeadUI.Attach(owner) == head && owner.GetComponents<UnitHeadUI>().Length == 1);
            owner.SetActive(false);
            Check("disabled owner immediately hides external UI", !graphic.activeSelf);
            owner.SetActive(true);
            graphic.SetActive(true);
            head.enabled = false;
            Check("disabled component hides external UI", !graphic.activeSelf);
            head.enabled = true;
            status.HP = 0;
            graphic.SetActive(true);
            typeof(UnitHeadUI).GetMethod("LateUpdate", flags).Invoke(head, null);
            Check("zero HP hides UI", !graphic.activeSelf);
            graphic.SetActive(true);
            status.HandleDeathIfDead();
            Check("real death cleanup hides UI before next frame", !owner.activeSelf && !graphic.activeSelf);
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); if (graphic != null) UnityEngine.Object.DestroyImmediate(graphic); }

        int oldRate = Application.targetFrameRate, oldSync = QualitySettings.vSyncCount;
        var budgetOwner = new GameObject("Frame budget fixture");
        var budget = budgetOwner.AddComponent<RuntimeFrameBudget>();
        try
        {
            var focus = typeof(RuntimeFrameBudget).GetMethod("OnApplicationFocus", flags);
            focus.Invoke(budget, new object[] { true });
            Check("active rendering capped at 60", Application.targetFrameRate == 60 && QualitySettings.vSyncCount == 0);
            focus.Invoke(budget, new object[] { false });
            Check("inactive rendering capped at 15", Application.targetFrameRate == 15);
        }
        finally { UnityEngine.Object.DestroyImmediate(budgetOwner); }
        Check("frame settings restored on teardown", Application.targetFrameRate == oldRate && QualitySettings.vSyncCount == oldSync);
    }
}
#endif
