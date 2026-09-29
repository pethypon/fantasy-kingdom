using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using UnityEditor;
public static class FogPerformanceValidation
{
 static void Check(bool ok,string text) { if(!ok)throw new Exception(text); UnityEngine.Debug.Log("[FogTest] PASS "+text); }
 static GameObject Load(string name) => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Map/"+name+".prefab");
 public static void Run()
 {
  var mapObject=new GameObject("Fog test map"); var oldObject=new GameObject("Baseline fog"); var newObject=new GameObject("Merged fog");
  try {
   var map=mapObject.AddComponent<MapCreate>(); map.maxX=35;map.maxZ=35;map.maxY=4;map.minY=0;
   var old=oldObject.AddComponent<FogChunkRendererBaseline>();var current=newObject.AddComponent<FogChunkRenderer>();
   old.Initialize(map,Load("Fog"),Load("FogExploard"),Load("FogBoard"),Load("FogExploardBoard"));
   current.Initialize(map,Load("Fog"),Load("FogExploard"),Load("FogBoard"),Load("FogExploardBoard"));
   var visible=new HashSet<Vector3Int>();var explored=new HashSet<Vector3Int>();
   old.Refresh(visible,explored);current.Refresh(visible,explored);
   Check(current.RenderObjectCount==18 && old.RenderObjectCount==54,"renderers 54 -> 18 (35x35)");
   Report(oldObject,newObject,"fully hidden"); ValidateMesh(newObject,1225,0);
   for(int x=9;x<24;x++)for(int z=9;z<24;z++)explored.Add(new Vector3Int(x,0,z));
   for(int x=13;x<20;x++)for(int z=13;z<20;z++)visible.Add(new Vector3Int(x,0,z));
   old.Refresh(visible,explored); current.Refresh(visible,explored);
   ValidateMesh(newObject,1000,176);
   Report(oldObject,newObject,"explored region and visible hole");
   for(int x=0;x<35;x++)for(int z=0;z<35;z++)CheckState(current,x,z,visible,explored);
   current.Refresh(visible,explored);Check(current.LastRebuiltChunks==0,"unchanged visibility does not rebuild");
   visible.Add(new Vector3Int(15,0,2));current.Refresh(visible,explored);Check(current.LastRebuiltChunks==2,"change at chunk seam rebuilds both neighbours");
   visible.Clear();explored.Clear();
   for(int x=0;x<35;x++)for(int z=0;z<35;z++){if((x+z)%3==0)visible.Add(new Vector3Int(x,0,z));else if((x+z)%3==1)explored.Add(new Vector3Int(x,0,z));}
   current.Refresh(visible,explored);ValidateMesh(newObject,408,409);
   Check(current.GetComponentsInChildren<MeshFilter>().All(f=>f.sharedMesh.vertexCount<65536),"fragmented mesh fits 16-bit indices");
   var timer=new Stopwatch();double oldMs=0,newMs=0;
   // Deterministic repeated border changes; excludes initialization and warm-up.
   for(int i=0;i<220;i++){
    var cell=new Vector3Int(i%35,0,(i*7)%35);if(!visible.Add(cell))visible.Remove(cell);
    timer.Restart();old.Refresh(visible,explored);timer.Stop();if(i>=20)oldMs+=timer.Elapsed.TotalMilliseconds;
    timer.Restart();current.Refresh(visible,explored);timer.Stop();if(i>=20)newMs+=timer.Elapsed.TotalMilliseconds;
   }
   UnityEngine.Debug.Log($"[FogBenchmark] 200 refreshes baseline={oldMs:F3}ms merged={newMs:F3}ms");
   foreach(string name in new[]{"UnknownMist","ExploredMist"})Check(!ShaderUtil.ShaderHasError(Resources.Load<Material>("Terrain/"+name).shader),"shader compiles "+name);
   Check(Resources.Load<Material>("Terrain/UnknownMist").renderQueue==2000,"opaque fog draws before transparent fog");
   visible.Clear();explored.Clear();for(int x=0;x<35;x++)for(int z=0;z<35;z++)visible.Add(new Vector3Int(x,0,z));
   current.Refresh(visible,explored);Check(current.GetComponentsInChildren<MeshRenderer>().All(r=>!r.enabled),"fully visible map has no fog draw");
   UnityEngine.Debug.Log("[FogPerformanceValidation] ALL PASSED");EditorApplication.Exit(0);
  }catch(Exception e){UnityEngine.Debug.LogException(e);EditorApplication.Exit(1);}
  finally {UnityEngine.Object.DestroyImmediate(oldObject);UnityEngine.Object.DestroyImmediate(newObject);UnityEngine.Object.DestroyImmediate(mapObject);}
 }
 static void CheckState(FogChunkRenderer fog,int x,int z,HashSet<Vector3Int> v,HashSet<Vector3Int> e){var p=new Vector3Int(x,0,z);if(fog.GetState(x,z)!=(v.Contains(p)?2:e.Contains(p)?1:0))throw new Exception("State changed");}
 static void Report(GameObject a,GameObject b,string label){int av=a.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.vertexCount),bv=b.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.vertexCount);UnityEngine.Debug.Log($"[FogBenchmark] {label}: vertices {av} -> {bv}");}
 static void ValidateMesh(GameObject root,float unknownArea,float exploredArea){
  float a=0,b=0;
  foreach(var f in root.GetComponentsInChildren<MeshFilter>()){
   var m=f.sharedMesh;var vs=m.vertices;var ns=m.normals;var ts=m.triangles;
   for(int t=0;t<ts.Length;t+=3){int i=ts[t];var cross=Vector3.Cross(vs[ts[t+1]]-vs[i],vs[ts[t+2]]-vs[i]);
    if(Vector3.Dot(cross,ns[i])<=0)throw new Exception("Reversed face");
    if(ns[i].y>.9f){if(f.name.EndsWith(" 0"))a+=cross.magnitude*.5f;else b+=cross.magnitude*.5f;}
   }
  }
  Check(Mathf.Approximately(a,unknownArea)&&Mathf.Approximately(b,exploredArea),$"exact top coverage {a}/{b}; holes and winding preserved");
 }
}
