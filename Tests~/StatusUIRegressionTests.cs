#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
public static class StatusUIRegressionTests
{
    static void Check(string label,bool ok) { if(!ok) throw new Exception("[StatusUI] "+label); Debug.Log("[StatusUI] PASS "+label); }
    static void CheckBarrierUI(GameSystems systems)
    {
        if (!BarrierCombatSettings.Active.enabledForPlay) return;
        var panel = systems.UnitPanelUI;
        var targetGo = new GameObject("Barrier UI target fixture");
        var attackerGo = new GameObject("Barrier UI attacker fixture");
        var definition = ScriptableObject.CreateInstance<UnitData>();
        definition.kind = Kind.Knight; definition.baseBarrier = 20;
        var target = targetGo.AddComponent<Status>(); target.kind = Kind.Knight; target.type = Type.Unit;
        target.team = Team.Enemy; target.GrowthData = definition; target.MaxHP = target.HP = 100; target.ResetBarrier();
        var attacker = attackerGo.AddComponent<Status>(); attacker.kind = Kind.Knight; attacker.type = Type.Unit;
        attacker.team = Team.Player; attacker.MaxHP = attacker.HP = 100; attacker.ATK = 1;
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var preview = systems.TurnGenerator.GetComponentInChildren<DamagePreviewUI>(true)
            ?? UnityEngine.Object.FindFirstObjectByType<DamagePreviewUI>();
        try
        {
            panel.Hide(); panel.Preview(target);
            var hp = panel.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true).First(t=>t.name=="HPText");
            var barrier = panel.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true).First(t=>t.name=="BarrierText");
            Canvas.ForceUpdateCanvases();
            Check("barrier follows HP in the same basic information column", barrier.transform.parent==hp.transform.parent
                && barrier.rectTransform.anchorMax.y<=hp.rectTransform.anchorMin.y);
            Check("the status panel no longer exposes a defence stat", !panel.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true)
                .Any(t=>t.name=="DEFText" || t.text?.Contains("防御力")==true));
            string originalHP = hp.text;
            SpecialSkillRuntime.ApplyHpChange(null, target, 4, HpChangeKind.NormalDamage);
            panel.Refresh();
            var fill = panel.GetComponentsInChildren<Image>(true).First(i=>i.name=="BarrierBarFill");
            Check("barrier-only damage preserves HP and HP label", target.HP==100&&target.Barrier==16&&hp.text==originalHP);
            Check("HUD exposes independent barrier value and gauge", barrier.text.Contains("バリア")&&barrier.text.Contains("16")
                &&fill.transform.parent.gameObject.activeSelf&&Mathf.Abs(fill.fillAmount-.8f)<.001f);
            var head = UnitHeadUI.Attach(targetGo);
            var headObject = (GameObject)typeof(UnitHeadUI).GetField("headUIObject",flags).GetValue(head);
            Check("world display has a separate barrier gauge", headObject.transform.Find("BarrierFrame/BarrierWell/BarrierFill")!=null);
            var worldHP=(RectTransform)headObject.transform.Find("HPFrame");
            var worldBarrier=(RectTransform)headObject.transform.Find("BarrierFrame");
            Check("world barrier is below HP without overlapping it", worldBarrier.anchoredPosition.y+worldBarrier.rect.height*.5f
                <worldHP.anchoredPosition.y-worldHP.rect.height*.5f);
            Check("crowded world gauges show numbers without repeating stat labels", worldHP.Find("HPText").GetComponent<TMPro.TextMeshProUGUI>().text=="100/100"
                && worldBarrier.Find("BarrierText").GetComponent<TMPro.TextMeshProUGUI>().text=="16/20");
            var worldRoot=(RectTransform)headObject.transform;
            Check("head layout bounds include both stacked gauges", worldBarrier.anchoredPosition.y-worldBarrier.rect.height*.5f>=worldRoot.rect.yMin
                && worldHP.anchoredPosition.y+worldHP.rect.height*.5f<=worldRoot.rect.yMax);
            if (preview != null)
            {
                var show = typeof(DamagePreviewUI).GetMethod("ShowPreview",flags);
                var label = (TMPro.TextMeshProUGUI)typeof(DamagePreviewUI).GetField("_hpText",flags).GetValue(preview);
                target.Barrier = 20;
                show.Invoke(preview,new object[]{attacker,target,Vector2.zero});
                Check("damage preview shows absorption without HP loss", label.text.Contains("HP: 100 → 100")&&label.text.Contains("バリア: 20 →"));
                target.Barrier = 1;
                int incoming = SkillSystem.CalcNormalDamage(attacker,target);
                int expectedHP = 100-SpecialSkillRuntime.PreviewHpDamage(target,incoming);
                show.Invoke(preview,new object[]{attacker,target,Vector2.zero});
                Check("damage preview sends only barrier overflow to HP", expectedHP<100&&label.text.Contains("HP: 100 → "+expectedHP)
                    &&label.text.Contains("バリア: 1 → 0"));
                typeof(DamagePreviewUI).GetMethod("ClearPreview",flags).Invoke(preview,null);
                var popup = (RectTransform)typeof(DamagePreviewUI).GetField("_panelRect",flags).GetValue(preview);
                Check("a missing hover reference still clears the damage popup",!popup.gameObject.activeSelf);
                preview.Hide();
            }
            else Check("normal damage preview is present",false);
            target.kind = Kind.King; target.ResetBarrier(); panel.Refresh();
            Check("HP-only roles do not show a legacy stat or configuration placeholder", !barrier.gameObject.activeSelf
                && !panel.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true)
                    .Any(t=>t.gameObject.activeInHierarchy && (t.text?.Contains("防御力")==true || t.text?.Contains("未設定")==true)));
            target.kind = Kind.Knight; target.ResetBarrier(); target.Barrier = 16; panel.Refresh();
            VisualRegressionTests.CaptureHUD("barrier-hud.png");
            targetGo.SetActive(false);
            Check("disabling an owner hides both world gauges",!headObject.activeSelf);
        }
        finally
        {
            panel.Hide(); if(preview!=null)preview.Hide();
            UnityEngine.Object.DestroyImmediate(targetGo); UnityEngine.Object.DestroyImmediate(attackerGo);
            UnityEngine.Object.DestroyImmediate(definition);
        }
    }
    public static void Run(GameSystems systems,TurnGenerator turn)
    {
        var actor=systems.UnitSetting.PlayerUnit.GetComponentsInChildren<Status>().First(s=>s.type==Type.Unit);
        var panel=systems.UnitPanelUI;
        panel.Hide();panel.Preview(actor);
        var group=panel.GetComponent<CanvasGroup>();
        Check("hover uses shared panel without action raycasts",group.alpha==1&&!group.blocksRaycasts&&!panel.HasSelection);
        CheckBarrierUI(systems);
        var move=new PlayerMove(turn){SelectedUnit=actor,SelectedUnitPosition=actor.transform.position,MenuSwitch=true};
        turn.ChangeState(move);turn.Context.SelectUnit=actor;panel.Show(actor);
        panel.Preview(null);
        Check("selection remains pinned after hover exit",panel.HasSelection&&group.blocksRaycasts&&group.alpha==1);
        var attack=panel.GetComponentsInChildren<Button>(true).First(b=>b.name=="AttackBtn");
        attack.onClick.Invoke();
        var input=turn.GetComponent<TurnInputHandler>();input.Tick();
        Check("button command survives input sampling",turn.Context.SelectNormalDown);
        turn.CurrentState.Update();
        Check("attack button enters attack state even without targets",turn.CurrentState is PlayerAttack);
        input.Tick();
        Check("button command consumed once",!turn.Context.SelectNormalDown);
        UnityEngine.Object.FindFirstObjectByType<TopBarUI>().OnEndTurn();input.Tick();
        Check("turn end button queues during attack mode",turn.Context.TurnEndDown);
        input.Tick();Check("turn end command consumed once",!turn.Context.TurnEndDown);
        panel.OnClickCancel();input.Tick();turn.CurrentState.Update();
        Check("cancel restores selected unit and move state",turn.CurrentState is PlayerMove&&turn.Context.SelectUnit==actor&&panel.HasSelection);
        panel.Hide();panel.Preview(actor);panel.OnClickAttack();input.Tick();
        Check("hover preview cannot issue attack command",!turn.Context.SelectNormalDown);
        var buildingGo=new GameObject("Status panel building fixture");
        var building=buildingGo.AddComponent<Status>();building.type=Type.Building;building.facilityKind=FacilityKind.Field;building.team=Team.Player;building.Level=1;building.HP=100;building.MaxHP=100;
        panel.Show(building);Canvas.ForceUpdateCanvases();
        var upgrade=panel.transform.Find("RightCommands/UpgradeArea").GetComponent<RectTransform>();
        var destroy=panel.transform.Find("RightCommands/DestroyArea").GetComponent<RectTransform>();
        Check("upgrade and destroy occupy separate rows",upgrade.anchorMin.y>destroy.anchorMax.y);
        var effect=panel.transform.Find("CenterStats/KindText").GetComponent<RectTransform>();
        Check("building effect does not overlap command column",effect.parent.GetComponent<RectTransform>().anchorMax.x<=upgrade.parent.GetComponent<RectTransform>().anchorMin.x);
        VisualRegressionTests.CaptureHUD("building-hud.png");
        panel.Hide();UnityEngine.Object.DestroyImmediate(buildingGo);move.Reset();systems.MoveGenerator.MoveReset();
        turn.Context.ConsumeUICommand();input.Tick();
    }
}
#endif
