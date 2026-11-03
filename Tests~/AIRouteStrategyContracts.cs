#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class AIRouteStrategyContracts
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static int passed;
    static void Check(string name,bool ok){if(!ok)throw new InvalidOperationException("[RouteStrategy] FAIL "+name);passed++;Debug.Log("[RouteStrategy] PASS "+name);}
    static void Set(object target,string field,object value)=>target.GetType().GetField(field,Hidden).SetValue(target,value);
    public static void Run(GameSystems live)
    {
        passed=0;AlternativeRoute();
        foreach(var side in new[]{Team.Player,Team.Enemy}){Risk(live,side);Hypothesis(live,side);Outpost(live,side);}
        Debug.Log("[RouteStrategy] RESULT checks="+passed);
    }
    static void AlternativeRoute()
    {
        var settings=ScriptableObject.CreateInstance<ExplorationAISettings>();settings.EnableJsonlTelemetry=false;
        try
        {
            var saved=new EnemyTerritoryIntelState{ActorTeam=Team.Enemy,OpponentTeam=Team.Player,Width=20,Depth=12,LastObservationTurn=1,
                RouteRisks=new List<AIRouteRiskRecord>{new AIRouteRiskRecord{Cell=new Vector3Int(8,0,5),Losses=2,LastLossTurn=1}},RecordedRouteLosses=2};
            Check("bounded loss-memory payload is valid",EnemyTerritoryIntel.IsValidState(saved,20,12,Team.Enemy));
            var intel=new EnemyTerritoryIntel();intel.RestoreState(saved,Team.Enemy);
            var terrain=new Dictionary<Vector3Int,Vector3Int>();for(int x=2;x<=14;x++)for(int z=1;z<=9;z++){var p=new Vector3Int(x,0,z);terrain[p]=p;}
            var actor=new ExplorationActor(1,new Vector3Int(2,0,5),Kind.Scout,100,true,"alternative-scout")
            {MovementOffsets=new[]{Vector2Int.left,Vector2Int.right,Vector2Int.up,Vector2Int.down},MovementDirectionIndependent=true};
            var routes=new ExplorationKnownRoute(settings);
            var blocked=new HashSet<Vector3Int>();
            Check("known alternate route can be proved",routes.TryMeasure(actor,new Vector3Int(14,0,5),terrain,blocked,actor.MovementOffsets,false,1,1,1,1,
                (id,cell)=>intel.RoutePlanningRisk(actor.HP,cell),out _,out _,20,12));
            var current=actor.Cell;bool detoured=false;int steps=0;
            while(steps++<64&&current!=new Vector3Int(14,0,5))
            {
                actor.Cell=current;
                if(!routes.TryMeasure(actor,new Vector3Int(14,0,5),terrain,blocked,actor.MovementOffsets,false,1,1,1,1,
                    (id,cell)=>intel.RoutePlanningRisk(actor.HP,cell),out _,out var next,20,12))break;
                Check("proved path avoids the repeated-loss corridor "+steps,intel.RoutePlanningRisk(actor.HP,next)<actor.HP);
                detoured|=Mathf.Abs(next.z-5)>=2;current=next;
            }
            Check("scout reaches the goal using another corridor",current==new Vector3Int(14,0,5)&&detoured);
        }
        finally{UnityEngine.Object.DestroyImmediate(settings);}
    }
    static void Risk(GameSystems live,Team side)
    {
        using var f=new R2ConstructionEconomyTests.Fixture(live,side);f.Engineer.kind=f.Engineer.GrowthData.kind=Kind.Scout;
        var board=f.Board();var seen=(HashSet<Vector3Int>)typeof(VisionGenerator).GetField(side==Team.Player?"_playerVisionBox":"_enemyVisionBox",Hidden).GetValue(f.Vision);
        var explored=(HashSet<Vector3Int>)typeof(VisionGenerator).GetField(side==Team.Player?"_playerExplored":"_enemyExplored",Hidden).GetValue(f.Vision);
        foreach(var p in f.Sites){seen.Add(GridHelper.ToGridXZ(p));explored.Add(GridHelper.ToGridXZ(p));}board.Refresh();var intel=board.TerritoryIntel;intel.Observe(board);
        var point=f.Sites[1];intel.RecordOwnCombatLoss("own-a",point,board.TurnCount);intel.RecordOwnCombatLoss("own-b",point,board.TurnCount);intel.RecordOwnCombatLoss("own-b",point,board.TurnCount);
        Check(side+" own deaths are counted once per life",intel.RecordedRouteLosses==2);
        var move=new AIAction{ActionType=AIActionType.Move,Unit=f.Engineer,TargetPos=point,StrategicPriority=3};
        Check(side+" repeating a lethal corridor is rejected",intel.WouldRepeatDangerousRoute(move));
        move.StrategicPriority=1;Check(side+" emergency orders retain access",!intel.WouldRepeatDangerousRoute(move));move.StrategicPriority=3;
        var saved=JsonUtility.FromJson<EnemyTerritoryIntelState>(JsonUtility.ToJson(intel.CaptureState()));var restored=new EnemyTerritoryIntel();restored.RestoreState(saved,side);
        restored.RecordOwnCombatLoss("own-b",point,board.TurnCount);
        Check(side+" loss risk and deduplication survive save",restored.RecordedRouteLosses==2&&restored.RouteRiskAt(point)>=24);
        var boss=new GameObject("observed strong unit");boss.transform.SetParent(side==Team.Player?f.Units.EnemyUnit:f.Units.PlayerUnit);
        var hostile=boss.AddComponent<Status>();hostile.team=side==Team.Player?Team.Enemy:Team.Player;hostile.type=Type.Unit;hostile.kind=Kind.Boss;hostile.HP=hostile.MaxHP=500;hostile.ATK=200;boss.transform.position=f.Sites[3];
        board.Refresh();intel.Observe(board);float before=intel.RouteRiskAt(f.Sites[3]);
        Check(side+" a visible strong enemy creates route risk",before>=200);
        seen.Clear();seen.Add(GridHelper.ToGridXZ(point));board.Refresh();boss.transform.position+=new Vector3(11,0,13);hostile.ATK=900;intel.Observe(board);
        Check(side+" hidden movement cannot relocate the remembered danger",Mathf.Abs(intel.RouteRiskAt(f.Sites[3])-before)<.001f);
        Check(side+" one visible cell is not a safe survey",intel.RouteRiskAt(point)>=24);
        for(int dx=-4;dx<=4;dx++)for(int dz=-4;dz<=4;dz++){var p=GridHelper.ToGridXZ(point)+new Vector3Int(dx,0,dz);if(p.x>=0&&p.z>=0&&p.x<live.MapCreate.maxX&&p.z<live.MapCreate.maxZ)seen.Add(p);}
        board.Refresh();intel.Observe(board);
        Check(side+" scout survey of the empty surroundings clears the old corridor",intel.RouteRiskAt(point)<24&&!intel.WouldRepeatDangerousRoute(move));
        intel.RecordOwnCombatLoss("new-loss",point,board.TurnCount);
        Check(side+" a new loss after a safe survey restores caution",intel.RouteRiskAt(point)>0&&intel.RecordedRouteLosses==3);
    }
    static void Hypothesis(GameSystems live,Team side)
    {
        using var f=new R2ConstructionEconomyTests.Fixture(live,side);var board=f.Board();board.TerritorySystem=f.Territory;
        var explored=(HashSet<Vector3Int>)typeof(VisionGenerator).GetField(side==Team.Player?"_playerExplored":"_enemyExplored",Hidden).GetValue(f.Vision);
        for(int x=0;x<live.MapCreate.maxX/2;x++)for(int z=0;z<live.MapCreate.maxZ/2;z++)explored.Add(new Vector3Int(x,0,z));
        board.Refresh();var intel=board.TerritoryIntel;
        Check(side+" exploration coverage can generate a search hypothesis",intel.TryGetReconTarget(board,out var frontier)&&intel.HasSearchHypothesis);
        Check(side+" the hypothesis has bounded uncertainty and no discovered main coordinate",intel.SearchHypothesisConfidence>0&&intel.SearchHypothesisConfidence<=.55f&&!board.GetLastKnownPlayerCrystal().Valid);
        Check(side+" a hypothesis assigns known ground, never the guessed coordinate as an attack target",board.IsTerrainKnown(frontier)&&!board.PlayerCrystalVisible);
    }
    static void Outpost(GameSystems live,Team side)
    {
        using var f=new EconomyR2Tests.Fixture(live,side);var host=new GameObject("outpost vision fixture");host.SetActive(false);var vision=host.AddComponent<VisionGenerator>();
        try
        {
            Set(vision,"PlayerUnit",f.Units.PlayerUnit);Set(vision,"EnemyUnit",f.Units.EnemyUnit);Set(vision,"territorysystem",live.TerritorySystem);
            var all=new HashSet<Vector3Int>();for(int x=0;x<live.MapCreate.maxX;x++)for(int z=0;z<live.MapCreate.maxZ;z++)all.Add(new Vector3Int(x,0,z));
            foreach(var field in new[]{"_playerVisionBox","_enemyVisionBox","_playerExplored","_enemyExplored"})Set(vision,field,field.StartsWith(side==Team.Player?"_player":"_enemy")?new HashSet<Vector3Int>(all):new HashSet<Vector3Int>());
            var home=live.MapCreate.SetPos[0];Vector3 site=default;bool found=false;
            foreach(var p in live.MapCreate.SetPos)if(p.x+3<live.MapCreate.maxX&&GridHelper.ChebyshevDistance(home,p)>=6&&GridHelper.ChebyshevDistance(home,p)<=10&&!live.MapCreate.IsRiver((int)p.x,(int)p.z)&&!live.MapCreate.IsHighMountain((int)p.x,(int)p.z)){site=p;found=true;break;}
            Check(side+" staging fixture has a real known supported site",found);f.Crystal.transform.position=home;f.SetOwnCrystalPosition(home);f.Unit().transform.position=site;
            f.Building(f.Definition(FacilityKind.LoggingCamp,wood:20));f.Building(f.Definition(FacilityKind.Quarry,stone:20));
            var board=new AIBoardState(live.MoveGenerator,live.AttackGenerator,f.AP,f.Units,f.Crystals,vision,f.Builder,null,f.State,null,20,null,side){MapCreate=live.MapCreate};
            board.SubCrystalPlaceable.Add(GridHelper.ToGrid(site));
            var planner=new AIOutpostPlanner(board);float safe=planner.Score(site);Check(side+" a supported site receives a finite staging score",safe>-1000);
            f.State.GetNation(side).SubCrystals=1;board.Refresh();board.SubCrystalPlaceable.Add(GridHelper.ToGrid(site));
            Check(side+" final outpost is reserved when no strategic lead supports this site",planner.Score(site)<=-1000);
            f.State.GetNation(side).SubCrystals=2;
            var intel=board.TerritoryIntel;intel.RecordOwnCombatLoss("site-a",site,20);intel.RecordOwnCombatLoss("site-b",site,20);board.Refresh();board.SubCrystalPlaceable.Add(GridHelper.ToGrid(site));
            Check(side+" a remembered lethal site is not selected for a new base",planner.Score(site)<=-1000);
            var empty=new EnemyTerritoryIntelState{ActorTeam=side,OpponentTeam=side==Team.Player?Team.Enemy:Team.Player,Width=live.MapCreate.maxX,Depth=live.MapCreate.maxZ};intel.RestoreState(empty,side);
            var hostile=f.Opponent(site+Vector3.right*3,100);hostile.kind=Kind.Boss;
            var attack=hostile.GrowthData.actionProfile.attack;attack.useCustom=true;attack.directionIndependent=true;attack.SetCell(0,3,true);
            board.Refresh();board.SubCrystalPlaceable.Add(GridHelper.ToGrid(site));Check(side+" a long-range visible defender vetoes exposed construction",planner.Score(site)<=-1000);
            hostile.HP=0;
            var delta=site-home;var step=Mathf.Abs(delta.x)>=Mathf.Abs(delta.z)?new Vector3(Mathf.Sign(delta.x),0,0):new Vector3(0,0,Mathf.Sign(delta.z));
            var main=f.Opponent(site+step*5,0);main.kind=Kind.Crystal;main.type=Type.Building;main.HP=main.MaxHP=3000;
            main.transform.SetParent(side==Team.Player?f.Crystals.Enemycrystal:f.Crystals.Playercrystal);
            f.State.GetNation(side).SubCrystals=1;board.Refresh();board.SubCrystalPlaceable.Add(GridHelper.ToGrid(site));
            Check(side+" a supported assault foothold may spend the final crystal",planner.Score(site)>-1000&&planner.Describe(site).Contains("本拠点"));
        }
        finally{UnityEngine.Object.DestroyImmediate(host);}
    }
}
#endif
