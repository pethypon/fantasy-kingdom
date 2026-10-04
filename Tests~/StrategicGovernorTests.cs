using System;
using System.Collections.Generic;
using UnityEngine;

public static class StrategicGovernorTests
{
    sealed class LossProvider:ILossConditionProvider
    {public Status King;public void Collect(AIBoardState b,List<Status> targets){targets.Clear();targets.Add(King);}}
    static void Check(string name,bool value){if(!value)throw new Exception("[StrategicGovernor] FAIL "+name);Debug.Log("[StrategicGovernor] PASS "+name);}
    public static void PlayMode(GameSystems systems)
    {
        var objects=new List<UnityEngine.Object>();var state=systems.FactionState;
        var res=state.EnemyResources;string oldResources=JsonUtility.ToJson(res);int oldTurn=state.EnemyNation.TurnsAlive;
        try
        {
            Vector3 start=Vector3.zero;bool found=false;
            foreach(var p in systems.MapCreate.SetPos)
            {
                if(systems.MapCreate.TryGetHeight((int)p.x+1,(int)p.z,out float y1)&&systems.MapCreate.TryGetHeight((int)p.x+2,(int)p.z,out float y2)
                    &&Mathf.Abs(y1-p.y)<.01f&&Mathf.Abs(y2-p.y)<.01f){start=p;found=true;break;}
            }
            Check("fixture has adjacent traversable cells",found);
            Status Actor(string name,Vector3 at,Team team,Kind role)
            {var go=new GameObject(name);objects.Add(go);go.transform.position=at;var s=go.AddComponent<Status>();s.team=team;s.kind=role;s.type=Type.Unit;s.HP=s.MaxHP=10;s.ATK=50;return s;}
            var king=Actor("Governor king",start,Team.Enemy,Kind.King);
            var foe=Actor("Visible stationary threat",start+Vector3.right*2,Team.Player,Kind.Knight);
            var data=ScriptableObject.CreateInstance<UnitData>();objects.Add(data);data.baseHP=10;data.upkeepBread=1000;king.GrowthData=data;
            var foeData=ScriptableObject.CreateInstance<UnitData>();objects.Add(foeData);foe.GrowthData=foeData;
            var profile=ScriptableObject.CreateInstance<BoardActionProfile>();objects.Add(profile);foeData.actionProfile=profile;
            profile.actionType=ActorActionType.Stationary;profile.attack.useCustom=true;profile.attack.SetCell(2,3,true);
            res.Bread=100;res.Citizen=0;state.EnemyNation.TurnsAlive=100;
            AIBoardState Board()
            {
                var b=new AIBoardState(systems.MoveGenerator,systems.AttackGenerator,systems.APSystem,systems.UnitSetting,systems.CrystalSystem,systems.VisionGenerator,
                    systems.BuildSystem,systems.SummonSystem,state,systems.SubCrystalSystem,20);
                b.MapCreate=systems.MapCreate;b.ReconThreatLevel=15;b.AliveEnemyUnits.Clear();b.AliveEnemyUnits.Add(king);b.AlivePlayerUnits.Clear();b.AlivePlayerUnits.Add(foe);return b;
            }
            var provider=new LossProvider{King=king};var board=Board();var governor=new AIStrategicGovernor(provider);board.Governor=governor;
            var suicidal=new AIAction{ActionType=AIActionType.Move,Unit=king,TargetPos=start+Vector3.right,Score=100000};
            var safe=new AIAction{ActionType=AIActionType.Attack,Unit=king,TargetUnit=foe,TargetPos=foe.transform.position};
            var actions=new List<AIAction>{suicidal,safe};governor.Filter(actions,board);
            Check("known lethal king move is rejected before scoring",!actions.Contains(suicidal)&&suicidal.StrategicRejectReason=="king_known_lethal");
            Check("safe king attack remains eligible",actions.Contains(safe));
            Check("executor gate cannot revive a rejected fallback",!governor.AllowExecution(suicidal,board));
            Check("safe default cannot revive a rejected candidate",AISearchEngine.GetSafeDefault(new List<AIAction>{suicidal},board)==null);
            var strategic=new List<AIAction>{new AIAction{StrategicPriority=4,Score=100000},new AIAction{StrategicPriority=0,Score=-100}};
            strategic.Sort(AIAction.ComparePriorityThenScore);
            Check("lookahead candidate order preserves survival priority",strategic[0].StrategicPriority==0);
            var roles=new AIRoleAssigner();roles.AssignRoles(board,TurnStrategy.ScoutSearch,new AIPersonality((MajorPersonality)0,1234));
            Check("king never receives an exploration role",roles.GetRole(king)==UnitRole.Guardian);
            profile.attack.SetCell(1,3,true);king.ATK=0;foe.HP=100;
            board=Board();governor=new AIStrategicGovernor(provider);
            var doomed=new AIAction{ActionType=AIActionType.Move,Unit=king,TargetPos=start+Vector3.right};
            var rotate=new AIAction{ActionType=AIActionType.Rotate,Unit=king,TargetDirection=Direction.S};actions=new List<AIAction>{doomed,rotate};governor.Filter(actions,board);
            Check("fully threatened king retains least dangerous actions",actions.Count>0);
            Check("known attack triggers emergency defense",governor.Mode==StrategicMode.EmergencyDefense);
            king.ATK=100;foe.HP=1;var finisher=new AIAction{ActionType=AIActionType.Attack,Unit=king,TargetUnit=foe};
            actions=new List<AIAction>{finisher};governor.Filter(actions,board);
            Check("king may eliminate its threat safely",actions.Contains(finisher));
            var planner=new AIPlanManager();planner.Restore(new AIPlan{RequiredUnits=new[]{Kind.Scout},Steps=new[]{AIPlanStep.Assemble},StartedTurn=1,ExpectedTurns=8});
            board.Governor=governor;planner.Update(board);
            Check("emergency suspends an operation instead of deleting it",planner.Current!=null&&planner.Current.Active&&planner.Current.Suspended);
            king.Level=21;res.Bread=1;res.Citizen=5;board=Board();var forecast=new StrategicEconomyForecast();string before=JsonUtility.ToJson(res);
            var result=forecast.Simulate(board);
            Check("nonnegative stocks do not conceal unpaid upkeep",result.UpkeepPaymentFailed&&result.State==EconomicState.Collapse);
            Check("forecast never mutates live resources",JsonUtility.ToJson(res)==before);
            var summon=new AIAction{ActionType=AIActionType.Summon,SummonKind=Kind.Knight,SummonDefinition=data};
            governor=new AIStrategicGovernor(provider);actions=new List<AIAction>{summon};governor.Filter(actions,board);
            Check("economic collapse forbids unsustainable recruitment",actions.Count==0&&!string.IsNullOrEmpty(summon.StrategicRejectReason));
            var persistence=new PersistentStrategicObjective{Active=true,StartedTurn=4,Target=start+Vector3.right*10};
            governor.Restore(JsonUtility.FromJson<PersistentStrategicObjective>(JsonUtility.ToJson(persistence)));governor.Evaluate(board);
            Check("strategic target survives suspension and serialization",governor.Objective.Active&&governor.Objective.Suspended&&governor.Snapshot().Target==persistence.Target);
            board.AlivePlayerUnits.Clear();governor=new AIStrategicGovernor(provider);governor.Evaluate(board);
            Check("unobserved actors do not enter king threat forecast",governor.IncomingDamage(board,king,start,null)==0);
        }
        finally
        {JsonUtility.FromJsonOverwrite(oldResources,res);state.EnemyNation.TurnsAlive=oldTurn;foreach(var item in objects)if(item!=null)UnityEngine.Object.DestroyImmediate(item);systems.MoveGenerator.UnitPointCore();systems.RefreshVision();}
    }
}
