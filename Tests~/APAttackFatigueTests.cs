#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Actual shared AP transactions and the AI forward model must quote the same attack fatigue.</summary>
public static class APAttackFatigueTests
{
    static int passed;
    static void Check(string name,bool ok){if(!ok)throw new InvalidOperationException("[AttackFatigue] FAIL "+name);passed++;Debug.Log("[AttackFatigue] PASS "+name);}
    public static void Run(GameSystems live)
    {
        passed=0;
        foreach(var side in new[]{Team.Player,Team.Enemy})
        {
            using var f=new R2ConstructionEconomyTests.Fixture(live,side);var actor=f.Engineer;
            actor.kind=actor.GrowthData.kind=Kind.Knight;actor.Fatigue=0;actor.specialAbility=SpecialAbility.None;
            f.State.SetAP(side,60);int original=f.State.GetAP(side),first=f.AP.CalcCost(APSystem.ActionType.Attack,actor);
            f.AP.Consume(side,APSystem.ActionType.Attack,actor);
            Check(side+" first attack pays quoted AP and adds exactly one fatigue",f.State.GetAP(side)==original-first&&actor.Fatigue==1);
            int second=f.AP.CalcCost(APSystem.ActionType.Attack,actor);
            Check(side+" repeated attack costs one additional AP",second==first+1);
            f.AP.Consume(side,APSystem.ActionType.Attack,actor);
            var board=f.Board();int quote=board.CalcAttackCost(actor);
            Check(side+" AI board uses the same accumulated attack cost",actor.Fatigue==2&&quote==first+2&&quote==f.AP.CalcCost(APSystem.ActionType.Attack,actor));
            int unchanged=actor.Fatigue;f.AP.CalcAttackCostPreview(actor,actor.Fatigue);
            Check(side+" preview never adds fatigue",actor.Fatigue==unchanged);
            var profile=actor.GrowthData.actionProfile;profile.attack.useCustom=true;profile.attack.directionIndependent=true;profile.attack.SetCell(4,3,true);
            var simulated=new SimUnit{Id=1,Team=Team.Enemy,Kind=Kind.Knight,Type=Type.Unit,HP=100,MaxHP=100,ATK=10,Fatigue=actor.Fatigue,
                Position=new Vector3Int(2,0,2),ActionProfile=profile};
            var target=new SimUnit{Id=2,Team=Team.Player,Kind=Kind.Knight,Type=Type.Unit,HP=1000,MaxHP=1000,Position=new Vector3Int(3,0,2)};
            var sim=new SimBoardState{Units=new List<SimUnit>{simulated,target},MapTiles=new HashSet<Vector3Int>(),EnemyAP=60,PlayerAP=60,
                EnemyCrystalPos=Vector3Int.zero,PlayerCrystalPos=new Vector3Int(7,0,7),EnemyBuildingCounts=new Dictionary<FacilityKind,int>(),PlayerBuildingCounts=new Dictionary<FacilityKind,int>()};
            for(int x=0;x<8;x++)for(int z=0;z<8;z++)sim.MapTiles.Add(new Vector3Int(x,0,z));sim.RebuildOccupied();
            var attack=SimActionGenerator.GenerateAllActions(sim,Team.Enemy).FirstOrDefault(a=>a.Type==SimActionType.Attack&&a.UnitId==1&&a.TargetUnitId==2);
            Check(side+" generated AI attack includes actual fatigue",attack!=null&&attack.APCost==quote);
            Check(side+" real AI simulation increments attack fatigue",sim.ApplyAction(attack)&&simulated.Fatigue==3);
            int before=f.State.GetAP(side);f.AP.Consume(side,APSystem.ActionType.Attack,actor);
            Check(side+" transaction and simulation stay in step",actor.Fatigue==simulated.Fatigue&&f.State.GetAP(side)==before-quote);
            f.AP.ResetFatigue(actor.transform.parent);
            Check(side+" owner-turn fatigue reset restores base attack AP",actor.Fatigue==0&&f.AP.CalcCost(APSystem.ActionType.Attack,actor)==first);
        }
        Debug.Log("[AttackFatigue] RESULT checks="+passed);
    }
}
#endif
