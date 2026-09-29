#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
public static class InteractionRegressionTests
{
    static void Check(string label,bool ok) { if(!ok) throw new Exception("[Interaction] "+label); Debug.Log("[Interaction] PASS "+label); }
    public static void Run(GameSystems s, TurnGenerator turn)
    {
        Check("fog shell stays aligned when generator is translated",s.MapCreate.FogChunks.transform.position.sqrMagnitude<.00001f);
        var actor=s.UnitSetting.PlayerUnit.GetComponentsInChildren<Status>().First(x=>x.kind!=Kind.Crystal);
        var controller=turn.GetComponent<TurnInspectionController>();
        var inspect=typeof(TurnInspectionController).GetMethod("Inspect",BindingFlags.Instance|BindingFlags.NonPublic);
        var previous=turn.CurrentState;
        // Assign only the state in this isolated test: do not start an actual AI turn.
        var field=typeof(TurnGenerator).GetField("_stateManager",BindingFlags.Instance|BindingFlags.NonPublic);
        field.SetValue(turn,new EnemyMove(turn));
        int ap=s.APSystem.GetAP(Team.Player);var position=actor.transform.position;var selected=turn.Context.SelectUnit;
        var moves=s.MoveGenerator.MovePositions.ToArray();var attacks=s.AttackGenerator.AttackP.ToArray();
        Check("opponent turn accepts visible selection",(bool)inspect.Invoke(controller,new object[]{actor}));
        Check("selected HP priority follows inspection",UnitPanelUI.SelectedStatus==actor);
        s.UnitPanelUI.OnClickAttack();s.UnitPanelUI.OnClickWait();turn.GetComponent<TurnInputHandler>().Tick();
        Check("inspection cannot queue attacks",!turn.Context.SelectNormalDown);
        controller.Tick();
        VisualRegressionTests.CaptureHUD("inspection-hud.png");
        Check("inspection preserves AP and unit position",s.APSystem.GetAP(Team.Player)==ap&&actor.transform.position==position);
        Check("inspection preserves AI action buffers",moves.SequenceEqual(s.MoveGenerator.MovePositions)&&attacks.SequenceEqual(s.AttackGenerator.AttackP)&&turn.Context.SelectUnit==selected);
        var hidden=new GameObject("Hidden inspection fixture");var status=hidden.AddComponent<Status>();status.HP=10;status.team=Team.Enemy;hidden.transform.position=new Vector3(-100,0,-100);
        Check("unseen enemies cannot be inspected",!(bool)inspect.Invoke(controller,new object[]{status}));UnityEngine.Object.DestroyImmediate(hidden);
        field.SetValue(turn,previous);controller.Tick();Check("inspection clears on player turn",UnitPanelUI.SelectedStatus==null);
        foreach(var parent in new[]{s.CrystalSystem.Playercrystal,s.CrystalSystem.Enemycrystal}) {
            var crystal=parent.GetComponentInChildren<Status>();
            Check("custom crystal has identity and full HP",crystal!=null&&crystal.kind==Kind.Crystal&&crystal.MaxHP==CrystalSystem.CrystalHP);
            Check("custom crystal has click collider",parent.GetComponentInChildren<Collider>()!=null);
        }
        s.UnitPanelUI.Hide();turn.Context.ConsumeUICommand();
    }
}
#endif
