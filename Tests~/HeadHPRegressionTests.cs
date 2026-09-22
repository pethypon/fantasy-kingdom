#if UNITY_EDITOR
using System;
using TMPro;
using UnityEngine;
public static class HeadHPRegressionTests
{
    static void Check(string label,bool ok) {if(!ok)throw new Exception("[HeadHP] "+label);Debug.Log("[HeadHP] PASS "+label);}
    public static void Run()
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Sphere);
        try
        {
            var status=go.AddComponent<Status>();status.type=Type.Unit;status.kind=Kind.Knight;status.team=Team.Enemy;
            status.Level=1;status.MaxHP=40;status.HP=10;
            var ui=UnitHeadUI.Attach(go);
            var fill=go.transform.Find("HeadUI/Root/HPBar/HPFill").GetComponent<RectTransform>();
            var label=go.transform.Find("HeadUI/Root/HPBar/HPText").GetComponent<TextMeshProUGUI>();
            var level=go.transform.Find("HeadUI/Root/LvCircle/LvText").GetComponent<TextMeshProUGUI>();
            Check("injured spawn uses actual max HP",Mathf.Approximately(fill.anchorMax.x,.25f)&&label.text=="10/40");
            status.GainExperience(Status.XPRequiredForLevel(2));typeof(UnitHeadUI).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(ui, null);
            Check("level gain changes actual maximum",status.Level==2&&status.MaxHP>40);
            Check("level up refreshes ratio and both numbers",Mathf.Approximately(fill.anchorMax.x,(float)status.HP/status.MaxHP)&&label.text==$"{status.HP}/{status.MaxHP}"&&level.text=="2");
            status.MaxHP+=20;typeof(UnitHeadUI).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(ui, null);
            Check("max-only change refreshes bar",Mathf.Approximately(fill.anchorMax.x,(float)status.HP/status.MaxHP)&&label.text==$"{status.HP}/{status.MaxHP}");
            status.HP=status.MaxHP;typeof(UnitHeadUI).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(ui, null);
            Check("healing reaches full bar",Mathf.Approximately(fill.anchorMax.x,1));
            status.HP=1;typeof(UnitHeadUI).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(ui, null);
            Check("damage after leveling uses grown maximum",Mathf.Approximately(fill.anchorMax.x,1f/status.MaxHP));
            status.HP=0;typeof(UnitHeadUI).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(ui, null);
            Check("dead unit head bar stays hidden",!go.transform.Find("HeadUI").gameObject.activeSelf);
        }
        finally {UnityEngine.Object.DestroyImmediate(go);}
    }
}
#endif
