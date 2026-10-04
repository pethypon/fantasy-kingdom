using System.Collections.Generic;
using UnityEngine;

public static partial class AIActionGenerator
{
    static void GenerateAuthoredSummonCandidates(AIBoardState board,List<AIAction> results)
    {
        if(board.SummonablePositions==null||board.SummonablePositions.Count==0)return;
        var catalog=UnitAuthoringCatalog.Load();if(catalog==null)return;
        int accepted=0;
        foreach(var definition in catalog.EnumerateStandaloneDefinitions(board.ActorTeam))
        {
            if(accepted>=32)break;
            if(!board.CanSummonDefinition(definition))continue;
            int count=0;
            foreach(var actor in board.AliveEnemyUnits)if(actor!=null&&actor.IsAlive&&actor.unitDefinitionId==definition.definitionId)count++;
            if(count>=GetMaxUnitCount(definition.kind))continue;
            var target=board.Recon.TryGetContactTarget(out var contact)?contact:board.EnemyCrystalPos+board.GetUnexploredDirection()*8;
            foreach(var pos in TakeNClosest(board.SummonablePositions,target,2))
            {
                results.Add(new AIAction{ActionType=AIActionType.Summon,SummonKind=definition.kind,SummonDefinition=definition,
                    TargetPos=pos,APCost=definition.costAP});
            }
            accepted++;
        }
    }
}
