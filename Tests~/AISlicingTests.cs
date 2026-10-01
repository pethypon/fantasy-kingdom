#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;
using Debug = UnityEngine.Debug;
public static class AISlicingTests
{
    static void Check(string name, bool ok) { if (!ok) throw new Exception("[AISlicing] " + name); Debug.Log("[AISlicing] PASS " + name); }
    public static SimBoardState Fixture()
    {
        var b = new SimBoardState { Units = new List<SimUnit>(), MapTiles = new HashSet<Vector3Int>(),
            EnemyBuildingCounts = new Dictionary<FacilityKind,int>(), PlayerBuildingCounts = new Dictionary<FacilityKind,int>(),
            EnemyAP = 8, PlayerAP = 8, EnemyAPReset = 8, PlayerAPReset = 8,
            EnemyCrystalPos = new Vector3Int(1,0,1), PlayerCrystalPos = new Vector3Int(6,0,1) };
        for (int x=0;x<8;x++) for(int z=0;z<8;z++) b.MapTiles.Add(new Vector3Int(x,0,z));
        for(int i=0;i<8;i++) b.Units.Add(new SimUnit {Id=i, Team=i<4?Team.Enemy:Team.Player,
            Kind=i%4==0?Kind.Crystal:Kind.Knight, Type=i%4==0?Type.Building:Type.Unit,
            HP=100,MaxHP=100,ATK=20,DEF=8, AssignedSkillId=-1,
            Direction=i<4?Direction.N:Direction.S, Position=new Vector3Int(i<4?1:6,0,1+i%4)});
        b.RebuildOccupied();return b;
    }
    public static List<AIAction> Candidates() => new List<AIAction> {
        new AIAction {ActionType=AIActionType.Build,Facility=FacilityKind.Well,APCost=3,TargetPos=new Vector3(1,0,1)},
        new AIAction {ActionType=AIActionType.Summon,SummonKind=Kind.Knight,APCost=3,TargetPos=new Vector3(2,0,2)},
        new AIAction {ActionType=AIActionType.Wait}
    };
    static int PoolCount() => ((ICollection)typeof(SimBoardPool).GetField("_boardPool", BindingFlags.Static|BindingFlags.NonPublic).GetValue(null)).Count;
    public static void Run()
    {
        var b=Fixture();var candidates=Candidates();
        try
        {
            var legacy=new LegacyAIMinimaxEngine(2,4,4,10000);
            var before=legacy.Search(candidates,b,null);
            var engine=new AIMinimaxEngine(2,4,4,10000);
            var result=new Dictionary<AIAction,float>();
            int slices=0;
            using(var work=engine.BeginSearch(candidates,b,null,result))
            {
                while(work.Step(.25)) { slices++; CheckLimit(slices); }
                Debug.Log($"[AISlicing] depth2 maxSliceMs={work.MaxSliceMs:F3} slices={work.SliceCount}");
            }
            Check("search yields before completion",slices>1);
            Check("completed same depth",engine.CompletedDepth==legacy.CompletedDepth&&engine.CompletedDepth==2);
            bool same=result.Count==before.Count;
            foreach(var c in candidates) same &= Mathf.Abs(result[c]-before[c])<.001f;
            Check("scores match original synchronous search",same);
            Check("input snapshot unchanged",b.EnemyAP==8&&b.Units.Count==8&&b.EnemyBuildingCounts.Count==0);
            int pooled=PoolCount();
            for(int n=0;n<20;n++)
            {
                using(var work=engine.BeginSearch(candidates,b,null,result)) for(int j=0;j<40;j++) if(!work.Step(.00001)) break;
                Check("cancel returns boards "+n,PoolCount()==pooled);
            }
            using(var neverStarted=engine.BeginSearch(candidates,b,null,result)) { }
            using(var reusable=engine.BeginSearch(candidates,b,null,result)) while(reusable.Step(3)) { }
            Check("engine reusable after unstarted cancellation",engine.CompletedDepth==2);
            using(var timed=engine.BeginSearch(candidates,b,null,result,0)) while(timed.Step(3)) { }
            Check("zero budget returns neutral corrections",engine.CompletedDepth==0&&result.Count==candidates.Count&&result[candidates[0]]==0);
            var deep=new AIMinimaxEngine(12,14,10,250);
            using(var work=deep.BeginSearch(candidates,b,null,result))
            {
                while(work.Step(3)) { }
                Check("long search is split",work.SliceCount>5);
                Debug.Log($"[AISlicing] budget250 maxSliceMs={work.MaxSliceMs:F3} slices={work.SliceCount} depth={deep.CompletedDepth}");
                Check("bounded warmed slices under 30ms",work.MaxSliceMs<30);
            }
        }
        finally { SimBoardPool.ReturnBoard(b); }
    }
    static void CheckLimit(int steps) { if(steps>100000)throw new Exception("Search never finishes"); }
}
#endif

