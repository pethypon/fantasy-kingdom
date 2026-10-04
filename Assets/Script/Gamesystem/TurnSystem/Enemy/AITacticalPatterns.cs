using System.Collections.Generic;
using UnityEngine;

public enum AITacticalMotif { IsolatedTarget, ExposedPriest, ClusteredEnemies, WoundedKing, WeakCrystal, SupportedRetreat }

[System.Serializable]
public struct AITacticalRule
{
    public AITacticalMotif Motif;
    [Range(10,100)] public int MinimumThreat;
    [Range(0,60)] public float Weight;
}

/// <summary>Small, configurable motifs recognized once per observation generation, rather than searched repeatedly.</summary>
public sealed class AITacticalPatterns
{
    struct Situation { public int EnemiesNearby, AlliesNearby; public bool PriestExposed, KingReachable; }
    readonly Dictionary<int,Situation> situations = new Dictionary<int,Situation>();
    readonly AITacticalRule[] rules;
    int generation=-1,turn=-1;
    public static AITacticalRule[] DefaultRules() => new[] {
        new AITacticalRule{Motif=AITacticalMotif.IsolatedTarget,MinimumThreat=10,Weight=20},
        new AITacticalRule{Motif=AITacticalMotif.ExposedPriest,MinimumThreat=10,Weight=28},
        new AITacticalRule{Motif=AITacticalMotif.ClusteredEnemies,MinimumThreat=10,Weight=30},
        new AITacticalRule{Motif=AITacticalMotif.WoundedKing,MinimumThreat=10,Weight=35},
        new AITacticalRule{Motif=AITacticalMotif.WeakCrystal,MinimumThreat=10,Weight=25},
        new AITacticalRule{Motif=AITacticalMotif.SupportedRetreat,MinimumThreat=10,Weight=12}
    };
    public AITacticalPatterns(AITacticalPatternSettings settings=null)
    { rules=settings!=null && settings.Rules!=null ? settings.Rules : DefaultRules(); }
    public void Prepare(AIBoardState board)
    {
        if(generation==board.Generation && turn==board.TurnCount)return;
        generation=board.Generation;turn=board.TurnCount;situations.Clear();
        foreach(var target in board.AlivePlayerUnits)
        {
            if(target==null || !target.IsAlive || !board.IsVisibleToEnemy(target.transform.position))continue;
            var info=new Situation();
            foreach(var other in board.AlivePlayerUnits)
                if(other!=null && other!=target && other.IsAlive && board.IsVisibleToEnemy(other.transform.position)
                    && GridHelper.ChebyshevDistance(other.transform.position,target.transform.position)<=2)info.EnemiesNearby++;
            foreach(var ally in board.AliveEnemyUnits)
            {
                if(ally==null || !ally.IsAlive || ally.type!=Type.Unit)continue;
                if(GridHelper.ChebyshevDistance(ally.transform.position,target.transform.position)<=3)info.AlliesNearby++;
                if(target.kind==Kind.King && target.HP<target.MaxHP*.4f && GridHelper.ChebyshevDistance(ally.transform.position,target.transform.position)<=8 && WithinTwoMoves(board,ally,target.transform.position))info.KingReachable=true;
            }
            info.PriestExposed=target.kind==Kind.Priest && info.EnemiesNearby==0;
            situations[target.GetInstanceID()]=info;
        }
    }
    public static bool WithinTwoMoves(AIBoardState board,Status unit,Vector3 target)
    {
        if(GridHelper.ChebyshevDistance(unit.transform.position,target)<=1)return true;
        var offsets=MovePatterns.Offsets(unit);
        int sign=MovePatterns.IsDirectionIndependent(unit)?1:MovePatterns.DirZ(unit.direction);
        foreach(var first in offsets)
        {
            Vector3 p=unit.transform.position+new Vector3(first.x,0,first.y*sign);
            if(!board.IsTerrainKnown(p) || !board.ReconMap.CanTraverse(unit.transform.position,p))continue;
            if(GridHelper.ChebyshevDistance(p,target)<=1)return true;
            foreach(var second in offsets)
            {
                Vector3 q=p+new Vector3(second.x,0,second.y*sign);
                if(GridHelper.ChebyshevDistance(q,target)<=1 && board.IsTerrainKnown(q) && board.ReconMap.CanTraverse(p,q))return true;
            }
        }
        return false;
    }
    public float Bonus(AIAction a,AIBoardState b)
    {
        if(b.ReconThreatLevel<10)return 0;
        if(a.TargetUnit!=null && a.TargetUnit.team==b.OpponentTeam && !situations.ContainsKey(a.TargetUnit.GetInstanceID()))return 0;
        situations.TryGetValue(a.TargetUnit!=null?a.TargetUnit.GetInstanceID():0,out var s);
        bool attacking=a.ActionType==AIActionType.Attack || a.ActionType==AIActionType.SkillUse && a.Skill!=null && a.Skill.Multiplier>0;
        float result=0;
        for(int i=0;i<rules.Length && i<300;i++)
        {
            var r=rules[i];if(b.ReconThreatLevel<Mathf.Max(10,r.MinimumThreat))continue;
            bool match=false;
            switch(r.Motif)
            {
                case AITacticalMotif.IsolatedTarget:match=attacking && a.TargetUnit!=null && s.EnemiesNearby==0 && s.AlliesNearby>=2;break;
                case AITacticalMotif.ExposedPriest:match=attacking && s.PriestExposed && a.Unit!=null && a.Unit.kind==Kind.Assassin;break;
                case AITacticalMotif.ClusteredEnemies:match=a.ActionType==AIActionType.SkillUse && VisibleAreaTargets(a,b)>=3;break;
                case AITacticalMotif.WoundedKing:match=attacking && a.TargetUnit!=null && a.TargetUnit.kind==Kind.King && s.KingReachable;break;
                case AITacticalMotif.WeakCrystal:
                    match=attacking && a.TargetUnit!=null && a.TargetUnit.team==b.OpponentTeam && a.TargetUnit.kind==Kind.Crystal && a.TargetUnit.HP<a.TargetUnit.MaxHP*.4f && s.EnemiesNearby==0;break;
                case AITacticalMotif.SupportedRetreat:match=a.ActionType==AIActionType.Retreat && a.Unit!=null && b.CountAlliesNear(a.TargetPos,a.Unit,3)>=2;break;
            }
            if(match)result+=Mathf.Clamp(r.Weight,0,60);
        }
        return Mathf.Min(65,result);
    }
    static int VisibleAreaTargets(AIAction action,AIBoardState board)
    {
        int count=0;
        if(action.AreaTargets!=null)
            foreach(var target in action.AreaTargets)
                if(target!=null && target.IsAlive && target.team==board.OpponentTeam && board.IsVisibleToEnemy(target.transform.position)
                    && ++count>=3)break;
        return count;
    }
}
