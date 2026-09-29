#if UNITY_EDITOR
using System;
using TMPro;
using UnityEngine;
public static class HeadHPRegressionTests
{
    static void Check(string label,bool ok) {if(!ok)throw new Exception("[HeadHP] "+label);Debug.Log("[HeadHP] PASS "+label);}
    public static void Run()
    {
        var canvasObject = new GameObject("Head test canvas",typeof(Canvas));
        var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay;
        typeof(UIBuilder).GetProperty("ScreenCanvas").SetValue(null,canvas);
        var cameraObject=new GameObject("Head test camera",typeof(Camera)); cameraObject.tag="MainCamera";
        cameraObject.transform.position=new Vector3(0,1,-10);
        var go=GameObject.CreatePrimitive(PrimitiveType.Sphere);
        try
        {
            var status=go.AddComponent<Status>();status.type=Type.Unit;status.kind=Kind.Knight;status.team=Team.Enemy;
            status.Level=1;status.MaxHP=40;status.HP=10;
            var ui=UnitHeadUI.Attach(go);
            Check("theme has head sprites", GameUITheme.Current != null && GameUITheme.Current.healthFrame != null && GameUITheme.Current.healthFill != null && GameUITheme.Current.levelBadge != null);
            var fill=canvas.transform.Find("UnitHeads/HeadUI/HPFrame/HPWell/HPFill").GetComponent<UnityEngine.UI.Image>();
            var label=canvas.transform.Find("UnitHeads/HeadUI/HPFrame/HPText").GetComponent<TextMeshProUGUI>();
            var level=canvas.transform.Find("UnitHeads/HeadUI/LevelBadge/LvText").GetComponent<TextMeshProUGUI>();
            Check("injured spawn uses actual max HP",Mathf.Approximately(fill.fillAmount,.25f)&&label.text=="10/40");
            status.GainExperience(Status.XPRequiredForLevel(2));typeof(UnitHeadUI).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(ui, null);
            Check("level gain changes actual maximum",status.Level==2&&status.MaxHP>40);
            Check("level up refreshes ratio and both numbers",Mathf.Approximately(fill.fillAmount,(float)status.HP/status.MaxHP)&&label.text==$"{status.HP}/{status.MaxHP}"&&level.text=="2");
            status.MaxHP+=20;typeof(UnitHeadUI).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(ui, null);
            Check("max-only change refreshes bar",Mathf.Approximately(fill.fillAmount,(float)status.HP/status.MaxHP)&&label.text==$"{status.HP}/{status.MaxHP}");
            status.HP=status.MaxHP;typeof(UnitHeadUI).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(ui, null);
            Check("healing reaches full bar",Mathf.Approximately(fill.fillAmount,1));
            status.HP=1;typeof(UnitHeadUI).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(ui, null);
            Check("damage after leveling uses grown maximum",Mathf.Approximately(fill.fillAmount,1f/status.MaxHP));
            var other=GameObject.CreatePrimitive(PrimitiveType.Sphere);var otherStatus=other.AddComponent<Status>();otherStatus.kind=Kind.Knight;otherStatus.type=Type.Unit;otherStatus.HP=otherStatus.MaxHP=40;
            UnitHeadUI.Attach(other);var heads=canvas.transform.Find("UnitHeads");
            var head=fill.transform.parent.parent.parent;head.SetAsFirstSibling();
            typeof(UnitPanelUI).GetProperty("SelectedStatus").SetValue(null,status);
            typeof(UnitHeadUI).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(ui,null);
            Check("selected head draws above crowded heads",head.GetSiblingIndex()==heads.childCount-1);
            UnityEngine.Object.DestroyImmediate((GameObject)typeof(UnitHeadUI).GetField("headUIObject",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(other.GetComponent<UnitHeadUI>()));
            UnityEngine.Object.DestroyImmediate(other);
            typeof(UnitPanelUI).GetProperty("SelectedStatus").SetValue(null,null);
            status.HP=0;typeof(UnitHeadUI).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(ui, null);
            Check("dead unit head bar stays hidden",!canvas.transform.Find("UnitHeads/HeadUI").gameObject.activeSelf);
        }
        finally {
            UnityEngine.Object.DestroyImmediate(canvasObject);
            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(cameraObject);
            typeof(UIBuilder).GetProperty("ScreenCanvas").SetValue(null,null);
        }
    }
}
#endif
