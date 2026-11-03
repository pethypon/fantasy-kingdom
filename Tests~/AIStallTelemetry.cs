#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Reflection;
using UnityEngine;

/// <summary>Read-only decision evidence. Inspects existing choices and own actors; never regenerates a candidate.</summary>
public static class AIStallTelemetry
{
    static StreamWriter choices,scouts;
    static int choiceRows,scoutRows;
    static readonly StringBuilder buffer=new StringBuilder();
    const int Limit=24000;
    static readonly FieldInfo SquadGeneration=typeof(AIOperationManager).GetField("squadGeneration",BindingFlags.Instance|BindingFlags.NonPublic);
    static readonly FieldInfo SquadBoard=typeof(AIOperationManager).GetField("squadBoard",BindingFlags.Instance|BindingFlags.NonPublic);
    public static void Open(string folder)
    {
        Close();choiceRows=scoutRows=0;
        choices=new StreamWriter(Path.Combine(folder,"stall-choices.csv"));scouts=new StreamWriter(Path.Combine(folder,"stall-scouts.csv"));
        Row(choices,"team","round","generation","mode","AP","actor","kind","action","targetTeam","targetKind","score","priority","x","z","destX","destZ","hp","maxHP","hasMoved","goalId","goalState","goalReason","goalX","goalZ","beforeGoalCheby","afterGoalCheby","knownRoute","waypointX","waypointZ","bestSameActorCloserScore","bestSameActorCloserPriority","bestSameActorCloserX","bestSameActorCloserZ","visibleHostiles","visiblePrincipal","visibleThird","mainKnowledge","squadCount","squadPolicyEnabled","squadCacheGeneration","squadBoardMatches","reconSource","hypothesisConfidence","knownRouteRisks","ownRouteLosses");
        Row(scouts,"team","round","generation","lifeId","hp","x","z","stunned","movementBlocked","effects","goalId","goalState","goalReason","goalX","goalZ","route","waypointX","waypointZ");
        AIDecisionObserver.Observed+=Observe;
    }
    public static void Close(){AIDecisionObserver.Observed-=Observe;choices?.Dispose();scouts?.Dispose();choices=scouts=null;}
    static float Distance(Vector3 a,Vector3 b)=>Mathf.Max(Mathf.Abs(a.x-b.x),Mathf.Abs(a.z-b.z));
    static void Observe(AIBoardState board,List<AIAction> candidates,AIAction chosen,string stage)
    {
        if(stage!="selected"||board==null||choices==null)return;
        var exploration=board.Exploration;
        foreach(var unit in board.AliveEnemyUnits)
        {
            if(unit.kind!=Kind.Scout||scoutRows>=Limit)continue;
            var goal=exploration.GetObjective(unit.GetInstanceID());buffer.Clear();
            foreach(var effect in unit.ActiveEffects)buffer.Append(effect.debuffType).Append('/').Append(effect.buffType).Append(':').Append(effect.remainingTurns).Append(';');
            Row(scouts,board.ActorTeam,board.TurnCount,board.Generation,unit.ReflectionLifeId,unit.HP,unit.transform.position.x,unit.transform.position.z,
                StatusEffectSystem.IsStunned(unit),StatusEffectSystem.IsMovementBlocked(unit),buffer.ToString(),goal?.ObjectiveId,goal?.State,goal?.LastReason,goal?.TargetCell.x,goal?.TargetCell.z,goal?.UsesKnownRouteDistance,goal?.RouteWaypoint.x,goal?.RouteWaypoint.z);scoutRows++;
        }
        if(chosen==null||choiceRows>=Limit)return;
        var actor=chosen.Unit;var objective=actor!=null?exploration.GetObjective(actor.GetInstanceID()):null;
        Vector3 position=actor!=null?actor.transform.position:Vector3.zero;
        float before=objective!=null?Distance(position,objective.TargetCell):-1,after=objective!=null?Distance(chosen.TargetPos,objective.TargetCell):-1;
        AIAction closer=null;
        if(actor!=null&&objective!=null)
            foreach(var candidate in candidates)
            {
                if(candidate.Unit!=actor||candidate.ActionType!=AIActionType.Move||Distance(candidate.TargetPos,objective.TargetCell)>=before)continue;
                if(closer==null||AIAction.ComparePriorityThenScore(candidate,closer)<0)closer=candidate;
            }
        int principal=0,third=0;
        foreach(var enemy in board.AlivePlayerUnits)if(enemy.team==board.OpponentTeam)principal++;else third++;
        Row(choices,board.ActorTeam,board.TurnCount,board.Generation,board.Governor?.Mode,board.EnemyAP,actor?.ReflectionLifeId,actor?.kind,chosen.ActionType,
            chosen.TargetUnit?.team,chosen.TargetUnit?.kind,chosen.Score,chosen.StrategicPriority,position.x,position.z,chosen.TargetPos.x,chosen.TargetPos.z,actor?.HP,actor?.MaxHP,actor?.HasMovedThisTurn,
            objective?.ObjectiveId,objective?.State,objective?.LastReason,objective?.TargetCell.x,objective?.TargetCell.z,before,after,objective?.UsesKnownRouteDistance,objective?.RouteWaypoint.x,objective?.RouteWaypoint.z,
            closer?.Score,closer?.StrategicPriority,closer?.TargetPos.x,closer?.TargetPos.z,board.AlivePlayerUnits.Count,principal,third,board.MainCrystalKnowledge,
            board.Operations?.State.Squads?.Count,AIOperationSquadConfig.Active.Enabled,board.Operations!=null?SquadGeneration?.GetValue(board.Operations):null,
            board.Operations!=null&&ReferenceEquals(SquadBoard?.GetValue(board.Operations),board),board.TerritoryIntel.ReconSource,
            board.TerritoryIntel.SearchHypothesisConfidence,board.TerritoryIntel.RememberedRouteRisks,board.TerritoryIntel.RecordedRouteLosses);choiceRows++;
    }
    static void Row(StreamWriter writer,params object[] fields)
    {
        for(int i=0;i<fields.Length;i++){if(i>0)writer.Write(',');writer.Write('"');writer.Write((Convert.ToString(fields[i],CultureInfo.InvariantCulture)??"").Replace("\"","\"\""));writer.Write('"');}writer.WriteLine();
    }
}
#endif
