#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;

/// <summary>Controlled native regressions. These checks are not match wins or natural occurrence counts.</summary>
public static class AIStallContracts
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static int checks;
    static readonly Vector2Int[] Steps={Vector2Int.left,Vector2Int.right,Vector2Int.up,Vector2Int.down};
    static void Check(string name,bool ok){if(!ok)throw new InvalidOperationException("[AIStall] FAIL "+name);checks++;Debug.Log("[AIStall] PASS "+name);}
    public static void Core()
    {
        checks=0;var settings=ScriptableObject.CreateInstance<ExplorationAISettings>();settings.EnableJsonlTelemetry=false;
        try
        {
            var observation=new ExplorationObservation{Width=40,Depth=16,Turn=0,ActorTeam=Team.Enemy,ViewVersion=1,CellVersion=1,OwnBase=new Vector3Int(1,0,5)};
            for(int x=0;x<16;x++)for(int z=0;z<16;z++)observation.KnownCells.Add(new ExplorationObservedCell(new Vector3Int(x,0,z),true,0));
            var planner=new AIExplorationPlanner(settings);planner.Update(observation,false);
            var actor=new ExplorationActor(17,new Vector3Int(3,0,5),Kind.Scout,100,true,"blocked-scout"){MovementOffsets=Steps,MovementDirectionIndependent=true};
            observation.Turn=1;observation.Actors.Add(actor);planner.Update(observation,false);
            var first=planner.GetObjective(17);Check("initial scout has a persistent frontier",first!=null&&first.State==ObjectiveState.Active);
            int id=first.ObjectiveId,started=first.LastProgressTurn;
            actor.CanAct=false;observation.Actors[0]=actor;observation.Turn=2;planner.Update(observation,false);
            Check("temporary inability suspends without losing the actor or target",planner.GetObjective(17)?.ObjectiveId==id&&planner.GetObjective(17).State==ObjectiveState.Suspended
                &&planner.GetObjective(17).LastReason=="movement_unavailable"&&!planner.TryGetAssignedTarget(17,out _));
            observation.Turn=3;planner.Update(observation,true);
            var saved=JsonUtility.FromJson<AIExplorationState>(JsonUtility.ToJson(planner.CaptureState()));
            planner=new AIExplorationPlanner(settings);planner.RestoreState(saved);
            actor.CanAct=true;observation.Actors[0]=actor;observation.Turn=5;planner.Update(observation,false);
            Check("movement resumes after save and emergency with the same goal",planner.GetObjective(17)?.ObjectiveId==id&&planner.GetObjective(17).State==ObjectiveState.Active);
            Check("pause compensates the stall clock once",planner.GetObjective(17).LastProgressTurn>=started+3);
            observation.Turn=6;observation.Actors.Clear();planner.Update(observation,false);
            Check("an actually absent actor still releases its objective",planner.GetObjective(17)==null);
        }
        finally{UnityEngine.Object.DestroyImmediate(settings);}
    }
    public static void Live(GameSystems live)
    {
        using var effects=BoardActorAnimation.SuppressEffects();
        foreach(var side in new[]{Team.Player,Team.Enemy})
        {
            using var f=new R2ConstructionEconomyTests.Fixture(live,side);
            var actor=f.Engineer;actor.kind=actor.GrowthData.kind=Kind.Scout;actor.HP=actor.MaxHP=35;
            var board=f.Board();var planner=board.Exploration;planner.Update(board,false);
            var goal=planner.GetObjective(actor.GetInstanceID());Check(side+" live scout gets a goal",goal!=null);
            goal.TargetCell=f.Sites[1];goal.State=ObjectiveState.Active;
            int goalId=goal.ObjectiveId;
            actor.ActiveEffects.Add(new ActiveEffect(StatusEffectType.Freeze,1));board.Refresh();planner.Update(board,false);
            Check(side+" a live frozen scout is suspended rather than removed",planner.GetObjective(actor.GetInstanceID())?.ObjectiveId==goalId&&planner.GetObjective(actor.GetInstanceID()).State==ObjectiveState.Suspended);
            actor.ActiveEffects.Clear();board.Refresh();planner.Update(board,false);
            Check(side+" thawed scout retains its expedition",planner.GetObjective(actor.GetInstanceID())?.ObjectiveId==goalId&&planner.GetObjective(actor.GetInstanceID()).State==ObjectiveState.Active);
            actor.kind=actor.GrowthData.kind=Kind.Knight;actor.Special.visionZeroTurns=1;
            var blindMove=new AIAction{ActionType=AIActionType.Move,Unit=actor,TargetPos=f.Sites[1]};
            var explored=(HashSet<Vector3Int>)typeof(VisionGenerator).GetField(side==Team.Player?"_playerExplored":"_enemyExplored",Hidden).GetValue(f.Vision);
            explored.Add(GridHelper.ToGridXZ(f.Sites[1]));
            board.Refresh();
            Check(side+" null-observer scout mask would promise unseen cells",board.EstimateNewVisionCells(blindMove.TargetPos)>0);
            Check(side+" this actor with zero vision forecasts no reveal",board.EstimateNewVisionCells(blindMove.TargetPos,actor)==0);
            AIStrategyBonus.Apply(new List<AIAction>{blindMove},TurnStrategy.ScoutSearch,board);
            Check(side+" the search strategy respects the actor's zero vision",blindMove.Score<=0);
            actor.Special.visionZeroTurns=0;actor.kind=actor.GrowthData.kind=Kind.Scout;
            EnvironmentKnowledge(live,f,board,side);
        }
        Debug.Log("[AIStall] RESULT checks="+checks);
    }
    static void EnvironmentKnowledge(GameSystems live,R2ConstructionEconomyTests.Fixture f,AIBoardState board,Team side)
    {
        var map=live.MapCreate;var field=typeof(MapCreate).GetField("environment",Hidden);var original=field.GetValue(map);
        var runtime=new TerrainEnvironmentRuntime(map);var catalog=ScriptableObject.CreateInstance<TerrainEnvironmentCatalog>();
        var rock=ScriptableObject.CreateInstance<TerrainEnvironmentDefinition>();var forest=ScriptableObject.CreateInstance<TerrainEnvironmentDefinition>();
        try
        {
            rock.definitionId="test.stall.rock";rock.enabledForPlay=true;rock.hidden=true;rock.kind=TerrainEnvironmentKind.Rock;rock.blocksMovement=true;rock.blocksVision=false;
            forest.definitionId="test.stall.forest";forest.enabledForPlay=true;forest.hidden=true;forest.kind=TerrainEnvironmentKind.Forest;forest.blocksMovement=false;forest.blocksVision=true;forest.attackRevealRounds=1;
            catalog.enabledForPlay=true;catalog.definitions=new List<TerrainEnvironmentDefinition>{rock,forest};
            catalog.placements=new List<TerrainEnvironmentPlacement>{
                new TerrainEnvironmentPlacement{cell=new Vector2Int(f.Sites[1].x,f.Sites[1].z),definition=rock},
                new TerrainEnvironmentPlacement{cell=new Vector2Int(f.Sites[3].x,f.Sites[3].z),definition=forest}};
            field.SetValue(map,runtime);runtime.Initialize(catalog);
            board.Refresh();var planner=board.Exploration;planner.Update(board,false);
            var unknown=planner.CaptureState().Cells.Find(c=>GridHelper.MatchXZ(c.Cell,f.Sites[1]));
            Check(side+" unobserved environment is not read through terrain knowledge",unknown!=null&&unknown.Passable);
            runtime.RevealTo(side,f.Sites[1]);runtime.RevealTo(side,f.Sites[3]);board.Refresh();planner.Update(board,false);
            var state=planner.CaptureState();
            Check(side+" observed rocks cannot be a known walking route",state.Cells.Exists(c=>GridHelper.MatchXZ(c.Cell,f.Sites[1])&&!c.Passable)
                &&!map.CanTraverse(f.Sites[0],f.Sites[1]));
            Check(side+" forest vision obstruction remains walkable",state.Cells.Exists(c=>GridHelper.MatchXZ(c.Cell,f.Sites[3])&&c.Passable));
            var beyond=f.Sites[3]+(f.Sites[3]-f.Sites[0]);
            bool predictedClear=(bool)typeof(AIExplorationPlanner).GetMethod("KnownVisionLineClear",Hidden).Invoke(planner,new object[]{GridHelper.ToGridXZ(f.Sites[0]),GridHelper.ToGridXZ(beyond)});
            Check(side+" observed forest blocks repeated hypothetical reveal",!predictedClear&&!runtime.HasClearVision(f.Sites[0],beyond));
        }
        finally
        {
            runtime.Initialize(null);field.SetValue(map,original);
            UnityEngine.Object.DestroyImmediate(catalog);UnityEngine.Object.DestroyImmediate(rock);UnityEngine.Object.DestroyImmediate(forest);
        }
    }
}
#endif
