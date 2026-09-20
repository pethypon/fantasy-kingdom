#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using Unity.Profiling;
public static class TerrainOptimizationTests
{
    static void Check(string name, bool ok) { if(!ok) throw new Exception(name); Debug.Log("[Optimization] PASS " + name); }
    static void Set(object o,string name,object value) => o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(o,value);
    static GameObject Prefab(MapCreate map,string name) => (GameObject) typeof(MapCreate).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(map);
    static long NativeObjectBytes(GameObject root)
    {
        long bytes=0;foreach(var t in root.GetComponentsInChildren<Transform>(true)) {
            bytes+=UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t.gameObject);
            foreach(var c in t.GetComponents<Component>()) bytes+=UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(c);
        }
        return bytes;
    }
    public static void Run(MapCreate live)
    {
        using(var sanity=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"GC.Alloc",1000,ProfilerRecorderOptions.CollectOnlyOnCurrentThread)) {
            var allocation=new byte[4096];GC.KeepAlive(allocation);sanity.Stop();
            Check("Unity allocation recorder detects known allocation",sanity.Valid&&sanity.Count>0);
        }
        var root=new GameObject("Terrain rule fixture");
        var map=root.AddComponent<MapCreate>(); map.maxX=5;map.maxZ=5;map.maxY=4;
        var heights=new int[5,5]; var water=new bool[5,5]; heights[2,2]=2; water[2,1]=true;
        Set(map,"topY",heights);Set(map,"rivers",water);
        var actor=new GameObject("Attacker").AddComponent<Status>(); actor.transform.position=new Vector3(1,1,2);
        Check("water and mountains reject placement",!map.TryGetHeight(2,1,out _)&&!map.TryGetHeight(2,2,out _));
        Check("water blocks traversal",!map.CanTraverse(new Vector3(1,1,1),new Vector3(3,1,1)));
        Check("water corner cannot be cut",!map.CanTraverse(new Vector3(1,1,1),new Vector3(2,1,0)));
        Check("mountain blocks vision",!map.HasClearTerrainLine(actor.transform.position,new Vector3(3,1,2)));
        foreach(Kind kind in new[]{Kind.Knight,Kind.Crossbow,Kind.Magicsniper,Kind.Archer,Kind.Bomber}) {
            actor.kind=kind;
            Check("mountain attack rule "+kind,map.CanAttackAcrossTerrain(actor,new Vector3(3,1,2))==(kind==Kind.Archer||kind==Kind.Bomber));
        }
        actor.kind=Kind.Knight;actor.facilityKind=FacilityKind.Mortar;
        Check("mortar arcs across mountain",map.CanAttackAcrossTerrain(actor,new Vector3(3,1,2)));
        var sim=new SimBoardState{ MapTiles=new HashSet<Vector3Int>() };
        for(int x=0;x<5;x++) for(int z=0;z<5;z++) if(map.TryGetHeight(x,z,out _)) sim.MapTiles.Add(new Vector3Int(x,0,z));
        sim.Mountains.Add(new Vector3Int(2,0,2));sim.Water.Add(new Vector3Int(2,0,1));
        bool parity=true;
        foreach(var from in sim.MapTiles) foreach(var to in sim.MapTiles) parity &= sim.CanTraverse(from,to)==map.CanTraverse(from,to);
        Check("simulation movement agrees for every fixture pair",parity);
        var su=new SimUnit{Position=new Vector3Int(1,0,2),Kind=Kind.Crossbow};
        Check("simulation rejects straight shot",!sim.CanAttack(su,new Vector3Int(3,0,2)));
        su.Kind=Kind.Archer;Check("simulation accepts arc",sim.CanAttack(su,new Vector3Int(3,0,2)));
        bool offsetsMatch=true;
        foreach(var entry in MovePatterns.Map) foreach(var direction in new[]{Direction.N,Direction.S}) {
            var actual=new HashSet<Vector2Int>(); int dir=MovePatterns.DirectionIndependent.Contains(entry.Key)?1:MovePatterns.DirZ(direction);
            foreach(var offset in MovePatterns.Offsets(entry.Key)) actual.Add(new Vector2Int(offset.x,offset.y*dir));
            for(int x=-8;x<=8;x++) for(int z=-8;z<=8;z++) offsetsMatch &= actual.Contains(new Vector2Int(x,z))==((x!=0||z!=0)&&MovePatterns.CanMove(entry.Key,direction,x,z));
        }
        Check("offsets equal full scan for all kinds and directions",offsetsMatch);
        var actors=new List<Status>();actor.HP=100;actor.type=Type.Unit;CombatRegistry.Collect(actors);
        Check("registry tracks enabled actors",actors.Contains(actor));actor.gameObject.SetActive(false);CombatRegistry.Collect(actors);
        Check("registry removes disabled actors",!actors.Contains(actor));actor.gameObject.SetActive(true);CombatRegistry.Collect(actors);
        Check("registry restores reenabled actors",actors.Contains(actor));
        map.SetPos.Add(new Vector3Int(0,1,0));actor.transform.position=new Vector3(2,1,1);
        SaveGameApplier.RelocateBlockedUnits(map);
        Check("legacy water unit relocates to free land",actor.transform.position==new Vector3(0,1,0));
        var legacy=new SaveSystem.GameSaveData { PCPx=2, PCPz=1 };
        map.RestoreSavedLand(legacy);
        Check("legacy crystal footprint becomes land",!map.IsRiver(2,1)&&map.SavedLandOverrides.Count==1);
        water[2,1]=true;legacy.LandOverrides=new List<Vector3Int>(map.SavedLandOverrides);legacy.PCPx=0;legacy.PCPz=0;
        map.RestoreSavedLand(legacy);Check("land override persists after structure removal",!map.IsRiver(2,1));
        UnityEngine.Object.DestroyImmediate(actor.gameObject);
        foreach(string name in new[]{"WaterBlock","HighMountainBlock"}) {
            var prefab=Resources.Load<GameObject>("Terrain/"+name);
            Check("dedicated prefab "+name,prefab!=null&&prefab.GetComponent<Collider>()!=null&&prefab.GetComponent<Status>()!=null);
        }
        foreach(int size in new[]{35,40}) {
            map.maxX=size;map.maxZ=size;
            var go=new GameObject("Fog benchmark");var fog=go.AddComponent<FogChunkRenderer>();
            var watch=System.Diagnostics.Stopwatch.StartNew();
            fog.Initialize(map,Prefab(live,"Fog"),Prefab(live,"FogExploard"),Prefab(live,"FogBoard"),Prefab(live,"FogExploardBoard"));
            var visible=new HashSet<Vector3Int>();var explored=new HashSet<Vector3Int>();fog.Refresh(visible,explored);
            watch.Stop();double initial=watch.Elapsed.TotalMilliseconds;
            Check(size+" fog has 54 render objects",fog.RenderObjectCount==54);
            explored.Add(new Vector3Int(1,0,1));visible.Add(new Vector3Int(2,0,2));fog.Refresh(visible,explored);
            Check(size+" fog three states",fog.GetState(0,0)==0&&fog.GetState(1,1)==1&&fog.GetState(2,2)==2);
            Check(size+" fog only dirty chunk rebuilt",fog.LastRebuiltChunks==1);
            fog.Refresh(visible,explored);
            var gcRecorder=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"GC.Alloc",100000,ProfilerRecorderOptions.CollectOnlyOnCurrentThread);watch.Restart();
            for(int i=0;i<100;i++)fog.Refresh(visible,explored);
            watch.Stop();gcRecorder.Stop();long bytes=0;int allocationCount=gcRecorder.Count;
            for(int sample=0;sample<gcRecorder.Count;sample++) bytes+=gcRecorder.GetSample(sample).Value;gcRecorder.Dispose();
            Check(size+" unchanged fog skips mesh rebuild",fog.LastRebuiltChunks==0);
            Check(size+" unchanged fog no managed allocations",allocationCount==0);
            long meshBytes=0;foreach(var filter in go.GetComponentsInChildren<MeshFilter>())meshBytes+=UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(filter.sharedMesh);
            Debug.Log($"[Optimization] BENCH size={size} oldRenderObjects={size*size*6} newRenderObjects={fog.RenderObjectCount} initialMs={initial:F3} unchanged100Ms={watch.Elapsed.TotalMilliseconds:F3} managedBytes={bytes} allocationCount={allocationCount} meshBytes={meshBytes}");
            long chunkObjects=NativeObjectBytes(go)+meshBytes;
            UnityEngine.Object.DestroyImmediate(go);
            var oldRoot=new GameObject("Legacy fog benchmark");watch.Restart();
            var prefabs=new[]{Prefab(live,"Fog"),Prefab(live,"FogExploard"),Prefab(live,"Fog"),Prefab(live,"FogExploard"),Prefab(live,"FogBoard"),Prefab(live,"FogExploardBoard")};
            for(int x=0;x<size;x++)for(int z=0;z<size;z++)for(int layer=0;layer<6;layer++) {
                var old=UnityEngine.Object.Instantiate(prefabs[layer],new Vector3(x,layer<2?3:layer<4?2:3.6f,z),Quaternion.identity,oldRoot.transform);
                old.SetActive(layer%2==0);
            }
            watch.Stop();double legacyMs=watch.Elapsed.TotalMilliseconds;
            long legacyObjects=NativeObjectBytes(oldRoot);
            Debug.Log($"[Optimization] COMPARE size={size} legacyInitialMs={legacyMs:F3} chunkInitialMs={initial:F3} legacyNativeObjectBytes={legacyObjects} chunkNativeObjectsAndMeshesBytes={chunkObjects} (shared materials excluded)");
            UnityEngine.Object.DestroyImmediate(oldRoot);
        }
        var candidates=new SimActionBuffer();var first=candidates.Next();first.UnitId=7;first.SkillId=9;first.TargetPos=new Vector3Int(2,0,3);
        var remembered=new SimAction();remembered.CopyFrom(first);candidates.Clear();var reused=candidates.Next();
        Check("candidate buffer reuses and resets entries",ReferenceEquals(first,reused)&&reused.SkillId==-1&&reused.TargetPos==Vector3Int.zero);
        Check("remembered killer move survives buffer reuse",remembered.UnitId==7&&remembered.SkillId==9&&remembered.TargetPos==new Vector3Int(2,0,3));
        using(var recorder=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"GC.Alloc",1000,ProfilerRecorderOptions.CollectOnlyOnCurrentThread)) {
            for(int i=0;i<1000;i++){candidates.Clear();candidates.Next();}recorder.Stop();
            Check("warmed candidate buffer allocates nothing",recorder.Count==0);
        }
        AITurnBudget.Begin(0);Check("expired thinking budget is enforced",AITurnBudget.Expired);AITurnBudget.Begin(3000);
        UnityEngine.Object.DestroyImmediate(root);
    }
}
#endif
