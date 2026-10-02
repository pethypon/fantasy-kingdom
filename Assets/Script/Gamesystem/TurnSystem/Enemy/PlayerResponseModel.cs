using UnityEngine;

/// <summary>Immutable, probabilistic player response preferences; contains no real units or hidden state.</summary>
public sealed class PlayerResponseModel
{
    public readonly float Aggression, Economy, Ranged, Retreat, Confidence, Skill, Flank, Turtle, Rush;
    public readonly int ThreatLevel;
    public float WorstCaseWeight => Mathf.Lerp(1,Mathf.Lerp(.20f,.55f,Mathf.InverseLerp(10,100,ThreatLevel)),Confidence);
    public PlayerResponseModel(float aggression,float economy,float ranged,float retreat,float confidence,int threatLevel,
        float skill=0,float flank=0,float turtle=0,float rush=0)
    {
        Aggression=Safe(aggression);Economy=Safe(economy);Ranged=Safe(ranged);Retreat=Safe(retreat);
        Confidence=Safe(confidence);ThreatLevel=Mathf.Clamp(threatLevel,10,100);
        Skill=Safe(skill);Flank=Safe(flank);Turtle=Safe(turtle);Rush=Safe(rush);
    }
    static float Safe(float f)=>float.IsNaN(f)||float.IsInfinity(f)?0:Mathf.Clamp01(f);
    // Adapter for callers with an existing profiler; only aggregate tendencies cross this boundary.
    public static PlayerResponseModel FromProfiler(PlayerProfiler.PlayerProfile p,int threatLevel,float retreat=.5f)
        => p==null?null:new PlayerResponseModel(p.AggressionScore,p.EconomyFocus,p.PreferredAttackRange/5,retreat,p.Confidence,threatLevel,p.SkillReliance,p.FlankPreference,p.TurtleTendency,p.RushTendency);

    public float Preference(SimAction action,SimBoardState board)
    {
        if(action.ActorTeam!=Team.Player)return 0;
        float preference=0;
        float earlyRush = board.TurnCount <= 6 ? Rush : 0;
        if(action.Type==SimActionType.Attack)preference+=Aggression*2+earlyRush;
        if(action.Type==SimActionType.SkillUse)preference+=Skill*2;
        if(action.Type==SimActionType.Build)preference+=Economy*2;
        if(action.Type==SimActionType.Summon && AIPlayerModel.IsRanged(action.SummonKind))preference+=Ranged*1.5f;
        var unit=board.GetUnit(action.UnitId);
        if(action.Type==SimActionType.Move && unit!=null)
        {
            float before=NearestOpponent(board,unit.Position);
            float after=NearestOpponent(board,action.TargetPos);
            if(unit.HP<=unit.MaxHP*.3f)preference+=(after-before)*Retreat*2;
            else preference+=(before-after)*(Aggression+earlyRush);
            // A zero/sentinel crystal position is not an observed home location.
            bool knownHome = false;
            foreach(var own in board.Units)
                if(own.IsAlive && own.Team==Team.Player && own.Kind==Kind.Crystal){knownHome=true;break;}
            if(knownHome)
            {
                float homeBefore=GridHelper.ChebyshevDistance(unit.Position,board.PlayerCrystalPos);
                float homeAfter=GridHelper.ChebyshevDistance(action.TargetPos,board.PlayerCrystalPos);
                preference+=(homeBefore-homeAfter)*Turtle*.5f;
                if(Mathf.Abs(action.TargetPos.x-board.PlayerCrystalPos.x)>Mathf.Abs(unit.Position.x-board.PlayerCrystalPos.x))preference+=Flank;
            }
        }
        return Mathf.Clamp(preference,-4,4)*Confidence;
    }
    public float Likelihood(SimAction action,SimBoardState board)
        => Mathf.Exp(Preference(action,board)); // Bounded positive weights, normalized over the evaluated responses.
    static float NearestOpponent(SimBoardState board,Vector3Int position)
    {
        float distance=20;
        foreach(var other in board.Units)
            if(other.IsAlive && other.Team==Team.Enemy)distance=Mathf.Min(distance,GridHelper.ChebyshevDistance(position,other.Position));
        return distance;
    }
    public float Combine(float mean,float worst)=>Mathf.Lerp(mean,worst,WorstCaseWeight);
}
