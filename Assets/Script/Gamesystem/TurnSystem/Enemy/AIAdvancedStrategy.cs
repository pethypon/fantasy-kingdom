using System.Collections.Generic;
using UnityEngine;

/// <summary>Level-ten boundary and the single integration point for operations, beliefs, motifs and investment.</summary>
public sealed class AIAdvancedStrategy
{
    public readonly AIPlanManager Plans=new AIPlanManager();
    readonly AIInvestmentPlanner investment=new AIInvestmentPlanner();
    readonly AITacticalPatterns tactics;
    public AIAdvancedStrategy(AITacticalPatternSettings settings=null){tactics=new AITacticalPatterns(settings);}
    public void Score(List<AIAction> actions,AIBoardState board)
    {
        var work=ScoreSteps(actions,board);
        try{while(work.MoveNext()) { }}finally{(work as System.IDisposable)?.Dispose();}
    }
    public System.Collections.IEnumerator ScoreSteps(List<AIAction> actions,AIBoardState board)
    {
        if(board.ReconThreatLevel<10)yield break;
        Plans.Update(board);investment.Prepare(board,Plans.Current);tactics.Prepare(board);
        for(int i=0;i<actions.Count;i++)
        {
            if((i&15)==0)yield return null;
            var action=actions[i];
            action.Score+=Plans.Bonus(action,board)+investment.Bonus(action,board)+tactics.Bonus(action,board);
            bool movement=action.ActionType==AIActionType.Move || action.ActionType==AIActionType.Retreat || action.ActionType==AIActionType.Support || action.ActionType==AIActionType.Surround;
            if(!movement || action.Unit==null)continue;
            if(action.Unit.kind==Kind.Scout)action.Score+=board.Belief.InformationGain(action.Unit,action.TargetPos);
            float expected=board.Belief.RiskAt(action.TargetPos,board.ReconThreatLevel);
            float exposure=action.Unit.kind==Kind.Priest || action.Unit.HP<action.Unit.MaxHP*.4f?1.8f:1;
            action.Score-=Mathf.Min(55,expected*exposure);
        }
    }
}
