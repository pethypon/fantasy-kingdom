#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
public static class LongevityTests
{
 static void Check(string name,bool value){if(!value)throw new Exception("[Longevity] "+name);Debug.Log("[Longevity] PASS "+name);}
 static void Advance(TimerSystem timer,float dt)=>typeof(TimerSystem).GetMethod("Advance",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(timer,new object[]{dt});
 public static void Run(GameSystems s,TurnGenerator turn)
 {
  var timer=s.TimerSystem; timer.PlayerTotalTime=timer.EnemyTotalTime=36000;timer.TurnTimeLimit=180;
  foreach(var status in UnityEngine.Object.FindObjectsByType<Status>(FindObjectsSortMode.None)) {status.HP=status.MaxHP=1000000;}
  s.FactionState.PlayerResources.Bread=s.FactionState.EnemyResources.Bread=1000000;
  foreach(var status in s.UnitSetting.PlayerUnit.GetComponentsInChildren<Status>()) status.transform.position=s.CrystalSystem.PCP;
  foreach(var status in s.UnitSetting.EnemyUnit.GetComponentsInChildren<Status>()) status.transform.position=s.CrystalSystem.ECP;
  for(int scenario=0;scenario<3;scenario++) {
   turn.ChangeState(new PlayerStart(turn));var move=(PlayerMove)turn.CurrentState;
   if(scenario==1){var unit=s.UnitSetting.PlayerUnit.GetComponentsInChildren<Status>().First(u=>u.IsAlive);move.SelectedUnit=unit;move.SelectedUnitPosition=unit.transform.position;turn.Context.SelectUnit=unit;turn.ChangeState(new PlayerAttack(turn,move,PlayerMove.AttackMode.Normal));}
   if(scenario==2)s.BuildSystem.StartBuildMode(FacilityKind.Field);
   timer.RestoreTurnTimeRemaining(0);Advance(timer,.02f);
   Check("timeout reaches enemy state "+scenario,turn.CurrentState is EnemyMove);
   Check("timeout clears build mode "+scenario,!s.BuildSystem.IsActive);
   var state=turn.CurrentState;var wood=s.FactionState.PlayerResources.Wood;
   move.ExecuteTurnEnd();Check("duplicate end is ignored "+scenario,turn.CurrentState==state&&s.FactionState.PlayerResources.Wood==wood);
   timer.RestoreTurnTimeRemaining(0);Advance(timer,.02f);state.Update();
   Check("enemy timeout does not stall "+scenario,turn.CurrentState!=state);
  }
  var go=new GameObject("Timer edge fixture");var t=go.AddComponent<TimerSystem>();int expired=0;t.OnTurnTimeExpired=()=>expired++;
  t.StartTurn(Team.Player);t.RestoreTurnTimeRemaining(0);Advance(t,.1f);Advance(t,.1f);
  Check("expiry fires once",expired==1&&t.TurnTimeRemaining==0&&!t.IsRunning);
  t.StartTurn(Team.Player);Advance(t,float.NaN);Check("NaN delta ignored",t.TurnTimeRemaining==180);
  t.RestoreTurnTimeRemaining(float.NaN);Check("corrupt saved time sanitized",t.TurnTimeRemaining==0);
  UnityEngine.Object.DestroyImmediate(go);
  var damage=FloatingDamageUI.Instance;Check("damage pool exists",damage!=null);
  typeof(FloatingDamageUI).GetField("_pool",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(damage,null);
  typeof(FloatingDamageUI).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(damage,null);
  Check("damage pool recovers without duplicate children",damage.transform.Cast<Transform>().Count(c=>c.name.StartsWith("DmgPopup_"))==10);
  var data=UnitStaticData.CreateUnitData(Kind.Knight);data.baseHP=777;
  var prefab=UnitWorkshopWindow.CreateUnit("WorkshopValidation",null,Kind.Knight,data,true);
  var catalog=UnitWorkshopWindow.GetCatalog();var stats=new System.Collections.Generic.Dictionary<Kind,UnitData>();var models=new System.Collections.Generic.Dictionary<Kind,GameObject>();catalog.Apply(stats,models);
  Check("workshop saves registered data and prefab",stats[Kind.Knight].baseHP==777&&models[Kind.Knight]==prefab&&prefab.GetComponent<Collider>()!=null);
  var owner=new GameObject("Authored setting fixture");var setting=owner.AddComponent<UnitSetting>();
  var spawned=setting.SpawnUnit(prefab,Vector3.zero,owner.transform,initialKind:Kind.Knight,initialTeam:Team.Player);
  Check("runtime uses authored prefab and stats",spawned.GetComponent<Status>().MaxHP==777&&setting.UnitDataMap[Kind.Knight].baseHP==777);
  var invalid=ScriptableObject.CreateInstance<UnitData>();invalid.kind=Kind.Archer;invalid.baseHP=10;invalid.costAP=-1;
  catalog.units.Add(new UnitAuthoringCatalog.Entry{kind=Kind.Archer,prefab=prefab,data=invalid});catalog.Apply(stats,models);
  Check("negative costs cannot override runtime data",!stats.ContainsKey(Kind.Archer));
  UnityEngine.Object.DestroyImmediate(invalid);UnityEngine.Object.DestroyImmediate(owner);
  catalog.units.Clear();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();UnityEngine.Object.DestroyImmediate(data);
  turn.ChangeState(new PlayerStart(turn));timer.PlayerTotalTime=0;Advance(timer,.1f);Check("total timeout reaches terminal state",turn.IsGameOver&&!timer.IsRunning);
 }
}
#endif
