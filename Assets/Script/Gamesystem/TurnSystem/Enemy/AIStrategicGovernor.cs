using System;
using System.Collections.Generic;
using UnityEngine;

public enum StrategicMode { Normal, EmergencyDefense, EconomicRecovery, Exploration, StrategicAttack, Hold }
public interface ILossConditionProvider { void Collect(AIBoardState board,List<Status> targets); }
public sealed class DefaultLossConditionProvider : ILossConditionProvider
{
    public void Collect(AIBoardState board,List<Status> targets)
    {
        targets.Clear();
        foreach(var actor in board.AliveEnemyUnits)if(actor!=null&&actor.IsAlive&&actor.kind==Kind.King)targets.Add(actor);
        var crystal=board.OwnCrystalParent?.GetComponentInChildren<Status>();if(crystal!=null&&crystal.IsAlive&&!targets.Contains(crystal))targets.Add(crystal);
    }
}
[Serializable]
public sealed class PersistentStrategicObjective
{
    public int Version=1,StartedTurn,PausedTurn,PausedTurns;
    public Vector3 Target;
    public bool Active,Suspended;
}

/// <summary>Priority gates above unchanged tactical/economic scoring. Facts come only from the observed board.</summary>
public sealed class AIStrategicGovernor
{
    readonly ILossConditionProvider losses;
    readonly StrategicEconomyForecast forecast=new StrategicEconomyForecast();
    readonly List<Status> critical=new List<Status>(4),defenders=new List<Status>(8),army=new List<Status>(32);
    readonly Dictionary<Status,List<Vector3>> reachable=new Dictionary<Status,List<Vector3>>();
    readonly Dictionary<Status,float> safest=new Dictionary<Status,float>();
    readonly Dictionary<(Status,Vector3Int,Status),float> damageCache=new Dictionary<(Status,Vector3Int,Status),float>();
    readonly Dictionary<AIAction,string> rejected=new Dictionary<AIAction,string>();
    readonly Dictionary<(Status,Vector3Int),float> threatCache=new Dictionary<(Status,Vector3Int),float>();
    readonly Dictionary<(int,Kind,Direction),HashSet<Vector2Int>> skillReach=new Dictionary<(int,Kind,Direction),HashSet<Vector2Int>>();
    AIBoardState current;int generation=-1;
    Vector3 defensePoint;bool safeAlternativesPrepared;
    Status explorer;
    public StrategicMode Mode {get;private set;}
    public EconomyForecastResult Economy {get;private set;}
    public PersistentStrategicObjective Objective {get;private set;}=new PersistentStrategicObjective();
    public IReadOnlyDictionary<AIAction,string> Rejections=>rejected;
    public int BreadReserveTurns {get=>forecast.ReserveTurns;set=>forecast.ReserveTurns=Mathf.Clamp(value,1,8);}
    public AIStrategicGovernor(ILossConditionProvider provider=null){losses=provider??new DefaultLossConditionProvider();}
    public void Evaluate(AIBoardState board)
    {
        if(current==board&&generation==board.Generation)return;
        current=board;generation=board.Generation;reachable.Clear();damageCache.Clear();threatCache.Clear();safest.Clear();safeAlternativesPrepared=false;
        losses.Collect(board,critical);defenders.Clear();army.Clear();
        // A persistent scout objective does not commandeer every combat unit or consume all economic AP.
        explorer=null;
        foreach(var actor in board.AliveEnemyUnits)
        {
            if(actor==null||!actor.IsAlive||actor.type!=Type.Unit||actor.kind==Kind.King||actor.kind==Kind.Priest
                ||BoardActionProfile.For(actor)?.CanMove==false)continue;
            if(explorer==null||actor.kind==Kind.Scout&&explorer.kind!=Kind.Scout)explorer=actor;
        }
        forecast.Prepare(board);Economy=forecast.Simulate(board);
        bool emergency=false;float severity=0;
        foreach(var target in critical)
        {
            float incoming=IncomingDamage(board,target,target.transform.position,null);
            float risk=incoming/Mathf.Max(1,target.HP);
            if(incoming>0){emergency=true;if(risk>severity){severity=risk;defensePoint=target.transform.position;}}
        }
        // A remembered threat can guide scouting, but only observed legal attacks trigger emergency orders.
        Mode=emergency?StrategicMode.EmergencyDefense:Economy.State>=EconomicState.Crisis?StrategicMode.EconomicRecovery
            :board.AlivePlayerUnits.Count==0?StrategicMode.Exploration:StrategicMode.StrategicAttack;
        UpdateObjective(board,emergency||Mode==StrategicMode.EconomicRecovery);
        if(emergency)SelectDefenders(board);
    }
    void SelectDefenders(AIBoardState board)
    {
        foreach(var actor in board.AliveEnemyUnits)
            if(actor!=null&&actor.IsAlive&&actor.type==Type.Unit&&actor.kind!=Kind.King&&BoardActionProfile.For(actor)?.CanAttack!=false)army.Add(actor);
        army.Sort((a,b)=>DefenseFitness(b).CompareTo(DefenseFitness(a)));
        float need=0;
        foreach(var opponent in board.AlivePlayerUnits)if(opponent!=null&&opponent.IsAlive&&Distance(opponent.transform.position,defensePoint)<=8)need+=Mathf.Max(1,opponent.ATK);
        float available=0;
        foreach(var actor in army)
        {defenders.Add(actor);available+=Mathf.Max(1,actor.ATK)*(float)actor.HP/Mathf.Max(1,actor.MaxHP);if(available>=need*1.2f||defenders.Count>=4)break;}
    }
    float DefenseFitness(Status actor)=>Mathf.Max(1,actor.ATK)*(float)actor.HP/Mathf.Max(1,actor.MaxHP)/(1+Distance(actor.transform.position,defensePoint));
    void UpdateObjective(AIBoardState board,bool pause)
    {
        if(Objective.Active)
        {
            if(pause&&!Objective.Suspended){Objective.Suspended=true;Objective.PausedTurn=board.TurnCount;}
            else if(!pause&&Objective.Suspended){Objective.PausedTurns+=Mathf.Max(0,board.TurnCount-Objective.PausedTurn);Objective.Suspended=false;}
            if(pause)return;
            bool reached=board.IsVisibleToEnemy(Objective.Target);
            if(!reached&&board.TurnCount-Objective.StartedTurn-Objective.PausedTurns<12)return;
            Objective.Active=false;
        }
        if(pause||board.ReconMap==null)return;
        var map=board.ReconMap;Vector3 origin=board.EnemyCrystalPos;float best=float.NegativeInfinity;Vector3 choice=origin;
        // Sampling 128 terrain cells bounds planning work; stable order and persistence prevent local jitter.
        int step=Mathf.Max(1,map.SetPos.Count/128);
        for(int i=0;i<map.SetPos.Count;i+=step)
        {
            var cell=map.SetPos[i];if(board.IsTerrainKnown(cell))continue;
            float distance=Distance(origin,cell);if(distance<6)continue;
            float score=distance-board.Belief.RiskAt(cell,board.ReconThreatLevel)*.2f;
            if(score>best){best=score;choice=cell;}
        }
        if(float.IsNegativeInfinity(best))return;
        Objective=new PersistentStrategicObjective{Active=true,Target=choice,StartedTurn=board.TurnCount};
    }
    public void Filter(List<AIAction> actions,AIBoardState board)
    {
        Evaluate(board);rejected.Clear();safest.Clear();
        foreach(var target in critical)
        {
            if(target.kind!=Kind.King)continue;
            float minimum=float.PositiveInfinity;
            foreach(var action in actions)if(action.Unit==target&&action.ActionType!=AIActionType.Wait)
                minimum=Mathf.Min(minimum,Risk(action,board));
            // A safe hold is an alternative even when no move can escape the current attack geometry.
            if(IncomingDamage(board,target,target.transform.position,null)<target.HP)minimum=Mathf.Min(minimum,0);
            safest[target]=minimum;
        }
        safeAlternativesPrepared=true;
        int output=0;
        for(int i=0;i<actions.Count;i++)
        {
            var action=actions[i];action.StrategicPriority=Priority(action,board);
            string reason=RejectReason(action,board);
            action.StrategicRejectReason=reason;
            if(reason!=null){rejected[action]=reason;continue;}
            actions[output++]=action;
        }
        if(output<actions.Count)actions.RemoveRange(output,actions.Count-output);
    }
    public bool AllowExecution(AIAction action,AIBoardState board)
    {
        Evaluate(board);
        if(!string.IsNullOrEmpty(action.StrategicRejectReason))return false;
        string reason=RejectReason(action,board);
        if(reason==null)return true;
        action.StrategicRejectReason=reason;Telemetry(action,false);return false;
    }
    string RejectReason(AIAction action,AIBoardState board)
    {
        if(action.Unit!=null&&action.Unit.kind==Kind.King)
        {
            float risk=Risk(action,board);
            float minimum=safest.TryGetValue(action.Unit,out var safe)?safe:IncomingDamage(board,action.Unit,action.Unit.transform.position,null);
            if(risk>=action.Unit.HP&&(minimum<action.Unit.HP||safeAlternativesPrepared&&risk>minimum+.01f))return "king_known_lethal";
        }
        if(action.ActionType==AIActionType.Summon)
        {
            var projected=forecast.Simulate(board,action);
            if(projected.BreadPaymentFailed||projected.ImportantProductionStopped)return "bread_forecast_fail";
            if(projected.UpkeepPaymentFailed)return "upkeep_forecast_fail";
            if(projected.BreadCoverageTurns<BreadReserveTurns&&Mode!=StrategicMode.EmergencyDefense)return "bread_reserve_fail";
        }
        if(Mode==StrategicMode.EmergencyDefense)
        {
            if(action.ActionType==AIActionType.SubCrystal)return "emergency_expansion_suspended";
            if(IsMovement(action)&&defenders.Contains(action.Unit)&&Distance(action.TargetPos,defensePoint)>Distance(action.Unit.transform.position,defensePoint)+.01f)
                return "defender_pursuit_limit";
        }
        return null;
    }
    int Priority(AIAction a,AIBoardState board)
    {
        if(a.Unit!=null&&a.Unit.kind==Kind.King&&IsMovement(a)&&Risk(a,board)<IncomingDamage(board,a.Unit,a.Unit.transform.position,null))return 0;
        if(a.Unit!=null&&a.Unit.kind==Kind.King&&a.ActionType==AIActionType.Attack
            &&IncomingDamage(board,a.Unit,a.Unit.transform.position,null)>=a.Unit.HP&&Risk(a,board)<a.Unit.HP)return 0;
        if(Mode==StrategicMode.EmergencyDefense)
        {
            if(a.ActionType==AIActionType.Attack&&a.TargetUnit!=null&&ThreatensCritical(a.TargetUnit,board))return 1;
            if(a.ActionType==AIActionType.SkillUse&&a.Unit!=null&&defenders.Contains(a.Unit))return 1;
            if(IsMovement(a)&&defenders.Contains(a.Unit)&&Distance(a.TargetPos,defensePoint)<Distance(a.Unit.transform.position,defensePoint))return 1;
            if(a.ActionType==AIActionType.Summon&&Distance(a.TargetPos,defensePoint)<=4)return 1;
        }
        if(Mode==StrategicMode.EconomicRecovery&&forecast.ImprovesFoodSupply(a,board))return 2;
        if(Objective.Active&&!Objective.Suspended&&a.Unit==explorer&&IsMovement(a)
            &&Distance(a.TargetPos,Objective.Target)<Distance(a.Unit.transform.position,Objective.Target)
            &&IncomingDamage(board,a.Unit,a.TargetPos,null)<a.Unit.HP)return 3;
        return 4;
    }
    bool ThreatensCritical(Status opponent,AIBoardState board)
    {foreach(var target in critical)if(CanThreaten(board,opponent,target.transform.position))return true;return false;}
    float Risk(AIAction action,AIBoardState board)
    {
        var unit=action.Unit;if(unit==null)return 0;Status eliminated=null;
        if(action.ActionType==AIActionType.Attack&&action.TargetUnit!=null&&action.TargetUnit.ShieldTurns<=0
            &&AIEvalHelpers.EstimateDamage(unit,action.TargetUnit)>=action.TargetUnit.HP)eliminated=action.TargetUnit;
        return IncomingDamage(board,unit,IsMovement(action)?action.TargetPos:unit.transform.position,eliminated);
    }
    public float IncomingDamage(AIBoardState board,Status self,Vector3 position,Status eliminated)
    {
        var key=(self,GridHelper.ToGridXZ(position),eliminated);if(damageCache.TryGetValue(key,out var cached))return cached;
        double total=0;
        foreach(var opponent in board.AlivePlayerUnits)
        {
            if(opponent==null||!opponent.IsAlive||opponent==eliminated||StatusEffectSystem.IsStunned(opponent))continue;
            float attack=ThreatPower(board,opponent,position);
            if(attack>0)total+=DamageCalculator.EstimateBaseDamage(Mathf.CeilToInt(attack),self.DEF);
        }
        return damageCache[key]=(float)Math.Min(int.MaxValue,total);
    }
    bool CanThreaten(AIBoardState board,Status opponent,Vector3 destination)
        =>ThreatPower(board,opponent,destination)>0;
    float ThreatPower(AIBoardState board,Status opponent,Vector3 destination)
    {
        var key=(opponent,GridHelper.ToGridXZ(destination));if(threatCache.TryGetValue(key,out var cached))return cached;
        var profile=BoardActionProfile.For(opponent);
        float Direct(Vector3 from)
        {
            float dx=destination.x-from.x,dz=destination.z-from.z;
            bool line=board.ReconMap==null||MapCreate.IsArcingAttack(opponent.kind,opponent.facilityKind)&&Distance(from,destination)>=2
                ||board.ReconMap.HasClearTerrainLine(from,destination);
            if(opponent.type!=Type.Unit&&(opponent.facilityKind==FacilityKind.Cannon||opponent.facilityKind==FacilityKind.Mortar))line=true;
            if(!line)return 0;
            bool normal=AttackPatterns.CanAttack(opponent,opponent.direction,dx,dz);
            if(opponent.type!=Type.Unit&&opponent.BuildingOperationAvailable&&profile?.CanAttack!=false&&profile?.attack?.useCustom!=true)
                normal=opponent.facilityKind==FacilityKind.Cannon?Distance(from,destination)==1
                    :opponent.facilityKind==FacilityKind.Mortar&&(dx==0||dz==0)&&Distance(from,destination)>=1&&Distance(from,destination)<=2;
            float power=normal?Mathf.Max(1,opponent.ATK):0;
            if(AttackPatterns.CanUseSkills(opponent)&&opponent.SkillCooldown<=1&&SkillData.Table.TryGetValue(opponent.AssignedSkillId,out var skill)
                &&skill.Multiplier>0&&skill.Target!=SkillTarget.Self&&skill.Target!=SkillTarget.AllySingle)
            {
                if(SkillOffsets(opponent,skill).Contains(new Vector2Int(Mathf.RoundToInt(dx),Mathf.RoundToInt(dz))))
                    power=Mathf.Max(power,opponent.ATK*(skill.Multiplier+Mathf.Max(0,skill.SecondMultiplier))+Mathf.Max(0,skill.FixedDamage));
            }
            return power;
        }
        float best=Direct(opponent.transform.position);
        if(StatusEffectSystem.IsMovementBlocked(opponent)||opponent.type!=Type.Unit||profile?.CanMove==false)return threatCache[key]=best;
        if(!reachable.TryGetValue(opponent,out var moves))reachable[opponent]=moves=board.GetValidMoves(opponent);
        foreach(var move in moves)best=Mathf.Max(best,Direct(move));
        return threatCache[key]=best;
    }
    HashSet<Vector2Int> SkillOffsets(Status actor,SkillData skill)
    {
        var key=(skill.Id,actor.kind,actor.direction);if(skillReach.TryGetValue(key,out var cached))return cached;
        var set=new HashSet<Vector2Int>();skillReach[key]=set;
        Vector2Int[] centers=null;
        if(skill.Target==SkillTarget.SelfArea)centers=new[]{Vector2Int.zero};
        else if(!AttackPatterns.SkillFixedPositions.TryGetValue(skill.Id,out centers))AttackPatterns.SkillAttackPositions.TryGetValue(actor.kind,out centers);
        if(centers==null)return set;
        var area=SkillSystem.GetAreaPositions(skill.Area,Vector3Int.zero,actor.direction);
        foreach(var center in centers)foreach(var offset in area)set.Add(new Vector2Int(center.x+offset.x,center.y*MovePatterns.DirZ(actor.direction)+offset.z));
        return set;
    }
    public void Telemetry(AIAction action,bool success)
    {
        DevelopmentLog.Log($"[AI戦略統括] 方針={Mode} 経済={Economy.State} パン収支={Economy.NetBreadPerTurn:F1} 優先=P{action.StrategicPriority} 結果={(success?"実行":"禁止")} 理由={action.StrategicRejectReason??"安全条件を満たす"}");
    }
    public PersistentStrategicObjective Snapshot()=>JsonUtility.FromJson<PersistentStrategicObjective>(JsonUtility.ToJson(Objective));
    public void Restore(PersistentStrategicObjective data)
    {
        Objective=data!=null&&data.Version==1&&data.StartedTurn>=0&&!float.IsNaN(data.Target.sqrMagnitude)&&!float.IsInfinity(data.Target.sqrMagnitude)
            ?JsonUtility.FromJson<PersistentStrategicObjective>(JsonUtility.ToJson(data)):new PersistentStrategicObjective();
        current=null;generation=-1;
    }
    static float Distance(Vector3 a,Vector3 b)=>GridHelper.ChebyshevDistance(a,b);
    static bool IsMovement(AIAction a)=>a.ActionType==AIActionType.Move||a.ActionType==AIActionType.Retreat||a.ActionType==AIActionType.Support
        ||a.ActionType==AIActionType.Surround||a.ActionType==AIActionType.DefenseRepos;
}
