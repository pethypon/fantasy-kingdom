#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
public static class OutpostPlanningTests
{
 static void Check(string name,bool ok){if(!ok)throw new Exception("[Outposts] "+name);Debug.Log("[Outposts] PASS "+name);}
 public static void Run(GameSystems s,TurnGenerator turn)
 {
  var flags=BindingFlags.Instance|BindingFlags.NonPublic;
  var seen=(HashSet<Vector3Int>)typeof(VisionGenerator).GetField("_enemyVisionBox",flags).GetValue(s.VisionGenerator);
  var explored=(HashSet<Vector3Int>)typeof(VisionGenerator).GetField("_enemyExplored",flags).GetValue(s.VisionGenerator);
  var oldSeen=seen.ToArray();var oldExplored=explored.ToArray();int ap=s.APSystem.GetAP(Team.Enemy),stock=s.FactionState.EnemySubCrystals;
  string resources=JsonUtility.ToJson(s.FactionState.EnemyResources);var pending=s.FactionState.EnemyPendingReturns.ToArray();
  var dungeon=s.DungeonSystem.Dungeons[0];var dungeonPosition=dungeon.Position;
  var originalBuildings=new HashSet<Status>();var own=new List<Status>();
  var g=new GameObject("Outpost support fixture");g.SetActive(false);g.transform.SetParent(s.UnitSetting.EnemyUnit);
  var scout=g.AddComponent<Status>();scout.kind=Kind.Scout;scout.type=Type.Unit;scout.team=Team.Enemy;scout.HP=scout.MaxHP=100;
  try {
   s.FactionState.SetAP(Team.Enemy,35);s.FactionState.EnemySubCrystals=2;
   foreach(var field in typeof(FactionState.ResourceData).GetFields())if(field.FieldType==typeof(int))field.SetValue(s.FactionState.EnemyResources,999);
   seen.Clear();explored.Clear();foreach(var tile in s.MapCreate.SetPos)seen.Add(GridHelper.ToGridXZ(tile));
   var b=new AIBoardState(s.MoveGenerator,s.AttackGenerator,s.APSystem,s.UnitSetting,s.CrystalSystem,s.VisionGenerator,s.BuildSystem,s.SummonSystem,s.FactionState,s.SubCrystalSystem);
   b.DungeonSystem=s.DungeonSystem;b.ReconThreatLevel=100;b.CollectOwnBuildings(own);originalBuildings.UnionWith(own);
   Check("full legal site enumeration exceeds old five-site cap",b.SubCrystalPlaceable.Count>5);
   var legal=b.SubCrystalPlaceable.Where(p=>GridHelper.ChebyshevDistance(p,b.EnemyCrystalPos)>=5&&GridHelper.ChebyshevDistance(p,b.EnemyCrystalPos)<=10).ToArray();
   Check("fixture has forward site",legal.Length>0);var site=legal[legal.Length-1];dungeon.Position=site;
   g.transform.position=(Vector3)site+Vector3.right;g.SetActive(true);s.MoveGenerator.UnitPointCore();b.Refresh();b.AlivePlayerUnits.Clear();
   // Suppress other known dungeons, exposing only this objective when explicitly discovered.
   foreach(var d in s.DungeonSystem.Dungeons)seen.Remove(GridHelper.ToGridXZ(d.Position));b.Refresh();b.AlivePlayerUnits.Clear();
   Check("undiscovered dungeon cannot become objective",!b.Outposts.TryGetDungeonObjective(site,out _));
   explored.Add(GridHelper.ToGridXZ(site));seen.Add(GridHelper.ToGridXZ(site));b.Refresh();b.AlivePlayerUnits.Clear();
   Check("discovered dungeon becomes exploration objective",b.Outposts.TryGetDungeonObjective(site,out var objective)&&GridHelper.MatchXZ(objective,site));
   var candidates=new List<AIAction>();AIActionGenerator.GenerateSubCrystalCandidates(b,candidates);
   Check("best deployment controls discovered dungeon",candidates.Count>0&&GridHelper.ChebyshevDistance(candidates[0].TargetPos,site)<=1);
   Check("ranked shortlist bounded and legal",candidates.Count<=4&&candidates.All(a=>s.SubCrystalSystem.CanPlaceSubCrystal(GridHelper.ToGrid(a.TargetPos),Team.Enemy)));
   var chosen=candidates[0].TargetPos;int evaluated=b.Outposts.EvaluatedSites;
   b.ObservedDungeons();
   var watch=System.Diagnostics.Stopwatch.StartNew();long before=GC.GetAllocatedBytesForCurrentThread();
   for(int i=0;i<10000;i++){var top=b.Outposts.Candidates;float score=b.Outposts.Score(chosen);var observed=b.ObservedDungeons();}
   long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
   Check("cached 10000 queries do not allocate or re-evaluate",bytes==0&&evaluated==b.Outposts.EvaluatedSites);
   Debug.Log($"[Outposts] cachedQueries=10000 ms={watch.Elapsed.TotalMilliseconds:F3} bytes={bytes} sites={evaluated}");
   b.SubCrystalPlaceable.Reverse();b.Refresh();b.AlivePlayerUnits.Clear();b.SubCrystalPlaceable.Reverse();candidates.Clear();AIActionGenerator.GenerateSubCrystalCandidates(b,candidates);
   Check("ranking independent of map enumeration order",candidates[0].TargetPos==chosen);
   // A currently observed enemy at the proposed site makes that site unsafe.
   b.Refresh();b.AlivePlayerUnits.Clear();b.AlivePlayerUnits.Add(scout);
   Check("observed nearby threat rejects deployment",b.Outposts.Score(site)<40);
   // A distant scouting position must not create an unsupported foothold.
   g.transform.position=new Vector3(-100,0,-100);b.Refresh();b.AlivePlayerUnits.Clear();b.AliveEnemyUnits.Clear();b.AliveEnemyUnits.Add(scout);
   Check("isolated outpost is postponed",b.Outposts.Score(site)<40);
   g.transform.position=(Vector3)site+Vector3.right;b.Refresh();b.AlivePlayerUnits.Clear();
   candidates.Clear();AIActionGenerator.GenerateSubCrystalCandidates(b,candidates);
   var executor=new AIActionExecutor(turn,s.MoveGenerator,s.AttackGenerator,s.BattleSystem,s.APSystem,s.SkillSystem,s.SubCrystalSystem,s.BuildSystem,s.SummonSystem,new AILearning(false));
   int beforeStock=s.FactionState.EnemySubCrystals;
   Check("AI actually deploys one outpost",executor.Execute(candidates[0],b)&&b.ExpansionCommitted&&s.FactionState.EnemySubCrystals==beforeStock-1);
   Check("dungeon controlled after deployment",dungeon.ClaimingTeam==Team.Enemy);
   Check("stale proposal cannot deploy second outpost",!executor.Execute(candidates[0],b));
   b.Refresh();candidates.Clear();AIActionGenerator.GenerateSubCrystalCandidates(b,candidates);
   Check("one deployment per turn survives board refresh",candidates.Count==0);
   Check("secured dungeon no longer attracts scout",!b.Outposts.TryGetDungeonObjective(site,out _));
  } finally {
   var parent=s.BuildSystem.GetBuildingParent(Team.Enemy);
   if(parent!=null)foreach(var u in parent.GetComponentsInChildren<Status>())if(!originalBuildings.Contains(u)&&u.facilityKind==FacilityKind.SubCrystal)s.SubCrystalSystem.DestroyBuilding(u);
   UnityEngine.Object.DestroyImmediate(g);dungeon.Position=dungeonPosition;
   s.FactionState.EnemySubCrystals=stock;s.FactionState.EnemyPendingReturns.Clear();s.FactionState.EnemyPendingReturns.AddRange(pending);
   JsonUtility.FromJsonOverwrite(resources,s.FactionState.EnemyResources);s.FactionState.SetAP(Team.Enemy,ap);s.MoveGenerator.UnitPointCore();
   seen.Clear();seen.UnionWith(oldSeen);explored.Clear();explored.UnionWith(oldExplored);
  }
 }
}
#endif
