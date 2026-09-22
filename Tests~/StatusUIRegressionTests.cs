#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
public static class StatusUIRegressionTests
{
    static void Check(string label,bool ok) { if(!ok) throw new Exception("[StatusUI] "+label); Debug.Log("[StatusUI] PASS "+label); }
    public static void Run(GameSystems systems,TurnGenerator turn)
    {
        var actor=systems.UnitSetting.PlayerUnit.GetComponentsInChildren<Status>().First(s=>s.type==Type.Unit);
        var panel=systems.UnitPanelUI;
        panel.Hide();panel.Preview(actor);
        var group=panel.GetComponent<CanvasGroup>();
        Check("hover uses shared panel without action raycasts",group.alpha==1&&!group.blocksRaycasts&&!panel.HasSelection);
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
        var upgrade=panel.transform.Find("UpgradeArea").GetComponent<RectTransform>();
        var destroy=panel.transform.Find("DestroyArea").GetComponent<RectTransform>();
        Check("upgrade and destroy occupy separate rows",upgrade.anchorMin.y>destroy.anchorMax.y);
        var effect=panel.transform.Find("CenterStats/KindText").GetComponent<RectTransform>();
        Check("building effect does not overlap command column",effect.parent.GetComponent<RectTransform>().anchorMax.x<=upgrade.anchorMin.x);
        panel.Hide();UnityEngine.Object.DestroyImmediate(buildingGo);move.Reset();systems.MoveGenerator.MoveReset();
        turn.Context.ConsumeUICommand();input.Tick();
    }
}
#endif
