#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
public static class SelectionUXTests
{
 static void Check(string text,bool ok){if(!ok)throw new Exception("[SelectionUX] "+text);Debug.Log("[SelectionUX] PASS "+text);}
 public static void Run(GameSystems s,TurnGenerator turn)
 {
  var controller=turn.GetComponent<TurnInspectionController>();
  var actor=s.UnitSetting.PlayerUnit.GetComponentsInChildren<Status>().First(u=>s.MapCreate.SetPos.Any(p=>AttackPatterns.CanAttack(u.kind,u.direction,p.x-u.transform.position.x,p.z-u.transform.position.z)&&s.MapCreate.CanAttackAcrossTerrain(u,p)&&s.VisionGenerator.IsInVisionXZ(Team.Player,p)));
  s.UnitPanelUI.Show(actor);controller.Tick();
  var renderer=(MeshRenderer)typeof(TurnInspectionController).GetField("rangeRenderer",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller);
  Check("normal selection shows attack area without attack mode",renderer!=null&&renderer.enabled&&renderer.GetComponent<MeshFilter>().sharedMesh.vertexCount>0);
  var enemy=new GameObject("Enemy range fixture");var status=enemy.AddComponent<Status>();status.team=Team.Enemy;status.kind=actor.kind;status.direction=actor.direction;status.HP=status.MaxHP=10;enemy.transform.position=actor.transform.position;
  s.UnitPanelUI.Show(status);controller.Tick();Check("visible enemy selection shows attack area",renderer.enabled&&renderer.GetComponent<MeshFilter>().sharedMesh.vertexCount>0);
  s.UnitPanelUI.Hide();controller.Tick();Check("deselect hides range",!renderer.enabled);UnityEngine.Object.DestroyImmediate(enemy);
  var resource=s.FactionState.PlayerResources;string saved=JsonUtility.ToJson(resource);int ap=s.APSystem.GetAP(Team.Player);
  try {
   resource.Wood=resource.Stone=resource.Iron=resource.MagicOre=resource.Water=resource.Citizen=100000;
   s.FactionState.SetAP(Team.Player,99999);s.BuildSystem.StartBuildMode(FacilityKind.Field);
   resource.Wood=0;Check("wood shortage is specific",BuildValidator.CostFailure(FacilityKind.Field,s.FactionState)=="木材不足");resource.Wood=100000;
   s.FactionState.SetAP(Team.Player,0);Check("AP shortage is specific",BuildValidator.CostFailure(FacilityKind.Field,s.FactionState)=="AP不足");s.FactionState.SetAP(Team.Player,99999);
   var outside=s.MapCreate.SetPos.First(p=>!s.TerritorySystem.IsInTerritory(GridHelper.ToGrid(p),Team.Player));
   Check("outside territory gives reason",s.BuildSystem.GetPlacementFailure(GridHelper.ToGrid(outside)).StartsWith("領地外"));
   Check("off-map placement gives reason",s.BuildSystem.GetPlacementFailure(new Vector3Int(-999,0,-999)).StartsWith("配置不可"));
   s.BuildSystem.CancelBuildMode();
   var bar=UIBuilder.ScreenCanvas.GetComponentInChildren<ResourceBarUI>();
   resource.Wood=resource.Stone=resource.Iron=resource.MagicOre=resource.Wheat=resource.Bread=resource.Water=resource.Citizen=int.MaxValue;
   typeof(ResourceBarUI).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(bar,null);Canvas.ForceUpdateCanvases();
   var labels=bar.GetComponentsInChildren<TextMeshProUGUI>();
   foreach(var text in labels){text.ForceMeshUpdate();Check("large resource text fits "+text.name,!text.isTextOverflowing);}
   Check("exact values available on hover",labels.First(x=>x.name=="Wood").GetComponentInParent<ResourceValueTooltip>().Value.Contains("2,147,483,647"));
  } finally {JsonUtility.FromJsonOverwrite(saved,resource);s.FactionState.SetAP(Team.Player,ap);s.BuildSystem.CancelBuildMode();}
 }
}
#endif
