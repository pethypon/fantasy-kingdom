#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;

/// <summary>Native controlled scenarios, distinct from ordinary-match evidence.</summary>
public static class AICampaignContracts
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static int passed;
    static void Check(string name, bool ok) { if (!ok) throw new InvalidOperationException("[Campaign] FAIL " + name); passed++; Debug.Log("[Campaign] PASS " + name); }
    static void Set(object target, string field, object value) => target.GetType().GetField(field, Hidden).SetValue(target,value);
    public static void Run(GameSystems live)
    {
        passed=0; using var effects=BoardActorAnimation.SuppressEffects();
        Reconfirmation();
        foreach (var side in new[]{Team.Player,Team.Enemy}) { Crystal(live,side); TerritoryItinerary(live,side); Overwatch(live,side); Campaign(live,side); }
        Debug.Log("[Campaign] RESULT checks="+passed);
    }
    static void TerritoryItinerary(GameSystems live,Team side)
    {
        using var f=new R2ConstructionEconomyTests.Fixture(live,side);
        f.Engineer.kind=f.Engineer.GrowthData.kind=Kind.Scout;
        var movement=f.Engineer.GrowthData.actionProfile.movement;movement.useCustom=true;movement.directionIndependent=true;
        movement.SetCell(4,3,true);movement.SetCell(2,3,true);movement.SetCell(3,4,true);movement.SetCell(3,2,true);
        f.Territory.GetTerritory(side==Team.Player?Team.Enemy:Team.Player).Add(f.Sites[1]);
        var board=f.Board();board.TerritorySystem=f.Territory;
        var seen=(HashSet<Vector3Int>)typeof(VisionGenerator).GetField(side==Team.Player?"_playerVisionBox":"_enemyVisionBox",Hidden).GetValue(f.Vision);
        var explored=(HashSet<Vector3Int>)typeof(VisionGenerator).GetField(side==Team.Player?"_playerExplored":"_enemyExplored",Hidden).GetValue(f.Vision);
        foreach(var cell in f.Sites){seen.Add(GridHelper.ToGridXZ(cell));explored.Add(GridHelper.ToGridXZ(cell));}
        board.Refresh();var intel=board.TerritoryIntel;intel.Observe(board);
        Check(side+" territory-only evidence creates a frontier itinerary",intel.TryGetReconTarget(board,out var target)&&!board.GetLastKnownPlayerCrystal().Valid);
        Check(side+" cached request keeps its selected frontier",intel.TryGetReconTarget(board,out var same)&&same==target);
        board.Exploration.Update(board,false);
        var moves=new List<AIAction>();AIActionGenerator.GenerateMoveCandidates(f.Engineer,board,moves);
        var legal=new List<(AIAction action,float score)>();foreach(var move in moves)legal.Add((move,1));
        var commander=(AICommander)FormatterServices.GetUninitializedObject(typeof(AICommander));Set(commander,"_board",board);Set(commander,"mainBaseReconMoveRound",-1);
        var reserve=typeof(AICommander).GetMethod("ReservedMainBaseReconMove",Hidden);
        var selected=(AIAction)reserve.Invoke(commander,new object[]{legal});
        Check(side+" a generated safe scout advance receives one AP opportunity",selected!=null&&board.Exploration.IsObjectiveProgress(selected.Unit,selected.TargetPos,board));
        typeof(AICommander).GetMethod("RecordCampaignAction",Hidden).Invoke(commander,new object[]{selected});
        Check(side+" reconnaissance reservation is once per own round",reserve.Invoke(commander,new object[]{legal})==null);
        f.Engineer.Special.visionZeroTurns=2;board.Refresh();
        Check(side+" vision-zero actor cannot falsely finish a survey",intel.TryGetReconTarget(board,out same)&&same==target);
        f.Engineer.Special.visionZeroTurns=0;f.Engineer.transform.position=target;board.Refresh();
        bool next=intel.TryGetReconTarget(board,out var other);
        Check(side+" reached frontier is not immediately selected again",!next||other!=target);
        var saved=intel.CaptureState();
        Check(side+" survey retry cooldown is bounded and saved",saved.ReconVisits.Count>0&&saved.ReconVisits.Count<=128
            &&saved.ReconVisits.Exists(v=>v.Cell==GridHelper.ToGridXZ(target)&&v.UntilTurn>board.TurnCount)
            &&EnemyTerritoryIntel.IsValidState(saved,live.MapCreate.maxX,live.MapCreate.maxZ,side));
        var restored=new EnemyTerritoryIntel();restored.RestoreState(JsonUtility.FromJson<EnemyTerritoryIntelState>(JsonUtility.ToJson(saved)),side);
        Check(side+" survey cooldown survives save",restored.CaptureState().ReconVisits.Count==saved.ReconVisits.Count);
    }
    static void Crystal(GameSystems live,Team side)
    {
        using var f=new EconomyR2Tests.Fixture(live,side);
        var crystal=f.Crystal; crystal.HP=crystal.MaxHP=1000;
        Check(side+" first shard grants 2000",MainCrystalRules.ApplyMaximumHPBonus(crystal,1000)==1000&&crystal.HP==2000&&crystal.MaxHP==2000);
        Check(side+" second shard stays capped at 2000",MainCrystalRules.ApplyMaximumHPBonus(crystal,1000)==0&&crystal.MaxHP==2000);
        crystal.HP=1500;
        Check(side+" capped reward cannot heal for free",MainCrystalRules.ApplyMaximumHPBonus(crystal,1000)==0&&crystal.HP==1500);
        var host=new GameObject("campaign reward fixture");host.SetActive(false);var dungeon=host.AddComponent<DungeonSystem>();
        int rewards=UniqueRewardSystem.Records.Count;
        try
        {
            Set(dungeon,"factionState",f.State);Set(dungeon,"crystalsystem",f.Crystals);f.Resources.MagicOre=0;
            typeof(DungeonSystem).GetMethod("GrantArtifact",Hidden).Invoke(dungeon,new object[]{side,DungeonSystem.Artifact.CrystalShard});
            Check(side+" actual capped dungeon reward converts to 30 magic",f.Resources.MagicOre==30&&crystal.MaxHP==2000&&crystal.HP==1500);
            foreach(int version in new[]{0,1,2,3})
            {
                int old=version==0?23000:version==1?13000:11000;
                var save=new SaveSystem.UnitSaveData{Kind="Crystal",Type="Building",Team=side.ToString(),HP=old/2,MaxHP=old,MainCrystalRuleVersion=version};
                SaveGameApplier.ApplyStatusFields(crystal,save);
                Check(side+" old version "+version+" preserves half HP at capped maximum",crystal.MaxHP==2000&&crystal.HP==1000);
                var roundTrip=JsonUtility.FromJson<SaveSystem.UnitSaveData>(JsonUtility.ToJson(SaveSystem.CaptureUnit(crystal)));
                SaveGameApplier.ApplyStatusFields(crystal,roundTrip);
                Check(side+" capped save version "+version+" is idempotent",crystal.MaxHP==2000&&crystal.HP==1000);
                save.HP=0;SaveGameApplier.ApplyStatusFields(crystal,save);
                Check(side+" capped dead version "+version+" stays dead",crystal.MaxHP==2000&&crystal.HP==0);
            }
            crystal.kind=Kind.SubCrystal;crystal.HP=crystal.MaxHP=500;
            Check(side+" shard rule does not alter subcrystals",MainCrystalRules.ApplyMaximumHPBonus(crystal,1000)==0&&crystal.MaxHP==500);
        }
        finally { UniqueRewardSystem.Records.RemoveRange(rewards,UniqueRewardSystem.Records.Count-rewards);UnityEngine.Object.DestroyImmediate(host); }
    }
    static void Reconfirmation()
    {
        var settings=ScriptableObject.CreateInstance<ExplorationAISettings>();settings.EnableJsonlTelemetry=false;
        try
        {
            var o=new ExplorationObservation{Width=24,Depth=12,Turn=1,ActorTeam=Team.Enemy,ViewVersion=1,CellVersion=1,
                HasTerritoryReconDirection=true,TerritoryReconIsRevisit=true,TerritoryReconDirection=new Vector3Int(16,0,6),OwnBase=new Vector3Int(1,0,6)};
            for(int x=0;x<24;x++)for(int z=0;z<12;z++)o.KnownCells.Add(new ExplorationObservedCell(new Vector3Int(x,0,z),true,0));
            var actor=new ExplorationActor(17,new Vector3Int(3,0,6),Kind.Scout,100,true,"campaign-scout")
            {MovementOffsets=new[]{Vector2Int.left,Vector2Int.right,Vector2Int.up,Vector2Int.down},MovementDirectionIndependent=true};
            o.Actors.Add(actor);o.VisibleCells.Add(new Vector3Int(3,0,6));var planner=new AIExplorationPlanner(settings);planner.Update(o,false);
            var goal=planner.GetObjective(17);
            Check("a fully explored but hidden base gets a reconfirmation goal",goal!=null&&goal.HasRequiredObservation&&goal.RequiredObservationCell==o.TerritoryReconDirection);
            int id=goal.ObjectiveId;var destination=goal.TargetCell;
            o.Turn=2;o.VisibleCells.Add(GridHelper.ToGridXZ(destination));o.ViewVersion++;planner.Update(o,false);
            Check("seeing a staging tile does not complete base reconfirmation",planner.GetObjective(17)?.ObjectiveId==id&&planner.GetObjective(17).State==ObjectiveState.Active);
            var state=JsonUtility.FromJson<AIExplorationState>(JsonUtility.ToJson(planner.CaptureState()));
            planner=new AIExplorationPlanner(settings);planner.RestoreState(state);o.Turn=3;planner.Update(o,false);
            Check("required observation survives save",planner.GetObjective(17)?.HasRequiredObservation==true);
            actor.Cell=destination;o.Actors[0]=actor;o.Turn=4;planner.Update(o,false);
            Check("an unsuccessful arrival selects another viewpoint",planner.GetObjective(17)!=null&&planner.GetObjective(17).TargetCell!=destination);
            o.Turn=5;o.ViewVersion++;o.VisibleCells.Add(o.TerritoryReconDirection);planner.Update(o,false);
            Check("actual base observation completes the reconfirmation task",!planner.TryGetAssignedTarget(17,out _)
                &&planner.GetObjective(17)?.LastReason=="main_crystal_reconfirmed");
            int routes=planner.KnownRouteBuildCount;o.Turn=6;planner.Update(o,false);
            Check("visible-base overwatch does not rebuild routes every turn",planner.KnownRouteBuildCount==routes&&!planner.TryGetAssignedTarget(17,out _));
        }
        finally { UnityEngine.Object.DestroyImmediate(settings); }
    }
    static void Overwatch(GameSystems live,Team side)
    {
        using var f=new R2ConstructionEconomyTests.Fixture(live,side);
        f.Engineer.kind=f.Engineer.GrowthData.kind=Kind.Scout;
        f.Engineer.GrowthData.actionProfile.vision.SetCell(4,3,true);
        var host=new GameObject("observed main base");host.transform.SetParent(side==Team.Player?f.Crystals.Enemycrystal:f.Crystals.Playercrystal);
        var main=host.AddComponent<Status>();main.kind=Kind.Crystal;main.type=Type.Building;main.team=side==Team.Player?Team.Enemy:Team.Player;
        main.HP=main.MaxHP=3000;host.transform.position=f.Sites[1];
        var board=f.Board();
        Check(side+" overwatch scenario actually observes the main base",board.PlayerCrystalVisible);
        var move=new AIAction{ActionType=AIActionType.Move,Unit=f.Engineer,TargetPos=f.Sites[3],StrategicPriority=3};
        Check(side+" healthy last scout does not abandon base sight",board.Recon.WouldAbandonObservedMainCrystal(move));
        move.StrategicPriority=1;
        Check(side+" urgent survival order can leave overwatch",!board.Recon.WouldAbandonObservedMainCrystal(move));
        move.StrategicPriority=3;move.ActionType=AIActionType.Retreat;
        Check(side+" an actual retreat can leave overwatch",!board.Recon.WouldAbandonObservedMainCrystal(move));
    }
    static void Campaign(GameSystems live,Team side)
    {
        using var f=new EconomyR2Tests.Fixture(live,side);var host=new GameObject("campaign observation fixture");host.SetActive(false);
        var vision=host.AddComponent<VisionGenerator>();var config=ScriptableObject.CreateInstance<AIOperationConfig>();var profile=ScriptableObject.CreateInstance<BoardActionProfile>();
        config.EnablePersistentLearning=false;config.DefaultMaximumDuration=4;config.DefaultExpectedDuration=2;config.MaxRevisions=3;
        try
        {
            Set(vision,"PlayerUnit",f.Units.PlayerUnit);Set(vision,"EnemyUnit",f.Units.EnemyUnit);Set(vision,"territorysystem",live.TerritorySystem);
            var seen=new HashSet<Vector3Int>();var explored=new HashSet<Vector3Int>();
            for(int x=0;x<live.MapCreate.maxX;x++)for(int z=0;z<live.MapCreate.maxZ;z++) {seen.Add(new Vector3Int(x,0,z));explored.Add(new Vector3Int(x,0,z));}
            foreach(var field in new[]{"_playerVisionBox","_enemyVisionBox","_playerExplored","_enemyExplored"})
                Set(vision,field,field==(side==Team.Player?"_playerVisionBox":"_enemyVisionBox")?seen:field==(side==Team.Player?"_playerExplored":"_enemyExplored")?explored:new HashSet<Vector3Int>());
            var cells=f.AdjacentCells();f.Crystal.transform.position=cells[0];f.SetOwnCrystalPosition(cells[0]);var army=new List<Status>();
            profile.attack.useCustom=true;profile.attack.directionIndependent=true;profile.attack.SetCell(4,3,true);
            for(int i=0;i<5;i++){var unit=f.Unit();unit.transform.position=cells[1];unit.ATK=40;unit.HP=unit.MaxHP=100;unit.GrowthData.actionProfile=profile;army.Add(unit);}
            var target=f.Opponent(cells[2],0);target.kind=Kind.Crystal;target.type=Type.Building;target.HP=target.MaxHP=3000;
            target.transform.SetParent(side==Team.Player?f.Crystals.Enemycrystal:f.Crystals.Playercrystal);
            f.Building(f.Definition(FacilityKind.LoggingCamp,wood:20));f.Building(f.Definition(FacilityKind.Quarry,stone:20));f.Building(f.Definition(FacilityKind.Well,water:20));
            f.Building(f.Definition(FacilityKind.Field,wheat:20));f.Building(f.Definition(FacilityKind.Bakery,bread:20));f.Building(f.Definition(FacilityKind.Mine,iron:20,magic:20));
            var board=new AIBoardState(live.MoveGenerator,live.AttackGenerator,f.AP,f.Units,f.Crystals,vision,f.Builder,null,f.State,null,20,null,side){MapCreate=live.MapCreate,ReconThreatLevel=30};
            board.Governor=new AIStrategicGovernor();board.Governor.Evaluate(board);
            var manager=new AIOperationManager(config);board.Operations=manager;manager.BeginBattle(side,"campaign-test");manager.BeginTurn(board,TurnStrategy.Balanced,1);
            var plan=manager.PrimaryOperation;Check(side+" observed main base starts the primary campaign",plan?.PrimaryGoal==AIOperationGoal.DestroyEnemyCrystal&&plan.IsBaseAssault);
            var attacks=new List<AIAction>();AIActionGenerator.GenerateAttackCandidates(army[0],board,attacks);var attack=attacks.Find(a=>a.TargetUnit==target);
            Check(side+" siege reservation uses a generated legal attack",attack!=null&&manager.IsMainCrystalCampaignAttack(attack,board));
            var commander=(AICommander)FormatterServices.GetUninitializedObject(typeof(AICommander));Set(commander,"_board",board);Set(commander,"mainCrystalStrikeRound",-1);
            var choices=new List<(AIAction action,float score)>{(new AIAction{ActionType=AIActionType.Move,Unit=army[1],TargetPos=cells[0]},10000),(attack,1)};
            var reserve=typeof(AICommander).GetMethod("ReservedMainCrystalStrike",Hidden);
            Check(side+" one siege strike wins before unrelated spending",ReferenceEquals(reserve.Invoke(commander,new object[]{choices}),attack));
            var mode=board.Governor.Mode;
            Set(board.Governor,"<Mode>k__BackingField",StrategicMode.EmergencyDefense);
            Check(side+" emergency defense releases siege AP",reserve.Invoke(commander,new object[]{choices})==null);
            Set(board.Governor,"<Mode>k__BackingField",StrategicMode.EconomicRecovery);
            Check(side+" necessary supply recovery releases siege AP",reserve.Invoke(commander,new object[]{choices})==null);
            Set(board.Governor,"<Mode>k__BackingField",mode);
            typeof(AICommander).GetMethod("RecordCampaignAction",Hidden).Invoke(commander,new object[]{attack});
            Check(side+" reservation is consumed only after success and once per own round",reserve.Invoke(commander,new object[]{choices})==null);
            long operationId=plan.OperationId;int oldX=plan.TargetX,oldZ=plan.TargetZ;
            // The objective itself is observed, while its defense-radius square is not.
            // Remove immediate-fire capability so the old attack-in-range bypass cannot pass this test.
            profile.actionType=ActorActionType.Support;
            plan.Steps=new List<AIOperationStep>{new AIOperationStep{StepId=1,Type=AIOperationStepType.Scout,Required=true},
                new AIOperationStep{StepId=2,Type=AIOperationStepType.Regroup,Required=true},new AIOperationStep{StepId=3,Type=AIOperationStepType.Approach,Required=true},
                new AIOperationStep{StepId=4,Type=AIOperationStepType.DestroyObjective,Required=true}};
            plan.CurrentStep=0;seen.Clear();seen.Add(GridHelper.ToGridXZ(target.transform.position));board.Refresh();manager.Refresh(board,TurnStrategy.Balanced);
            Check(side+" partial base sight does not falsely certify full defense intelligence",!plan.BaseDefenseKnown);
            Check(side+" actual main-base reconfirmation advances beyond Scout without a legal immediate attack",plan.CurrentStep>0&&Find(manager,operationId)!=null);
            profile.actionType=ActorActionType.All;
            foreach(var unit in army)unit.HP=0;
            seen.Clear();board.Refresh();target.transform.position+=new Vector3(17,0,19);target.HP=0;
            for(int t=2;t<=14;t++){manager.EndTurn(board,t-1);manager.BeginTurn(board,TurnStrategy.Balanced,t);}
            Check(side+" squad loss and timeout preserve the same main objective",manager.ActiveOperations.Count>0&&Find(manager,operationId)?.TargetLifeId==target.ReflectionLifeId);
            plan=Find(manager,operationId);Check(side+" hidden live state cannot rewrite remembered coordinates or confirm destruction",plan.TargetX==oldX&&plan.TargetZ==oldZ&&!manager.KnownEnemyBases[0].ConfirmedDestroyed);
            Check(side+" repeated recovery retains DestroyObjective within the step bound",plan.Steps.Count<=config.StepLimit&&plan.Steps.Exists(s=>s.Type==AIOperationStepType.DestroyObjective));
            plan.Status=AIOperationStatus.Active;
            Check(side+" active retained siege still requests military reinforcements",manager.ActionBonus(new AIAction{ActionType=AIActionType.Summon,SummonKind=Kind.Knight},board)>0);
            for(int i=0;i<5;i++){var unit=f.Unit();unit.transform.position=cells[1];unit.ATK=40;unit.HP=unit.MaxHP=100;unit.GrowthData.actionProfile=profile;}
            board.Refresh();manager.EndTurn(board,14);manager.BeginTurn(board,TurnStrategy.Balanced,15);
            Check(side+" reinforcements join the retained mission",Find(manager,operationId)?.AssignedUnitLifeIds.Count>=3);
            bool four=false;foreach(var squad in manager.Squads)four|=squad.OperationId==operationId&&squad.Members.Count==4;
            Check(side+" the normal four-member squad remains supported",four);
            plan=Find(manager,operationId);plan.CurrentContext.OwnCrystalThreatened=true;
            typeof(AIOperationManager).GetMethod("Reevaluate",Hidden).Invoke(manager,new object[]{plan,board,16});
            Check(side+" danger suspends without deleting the objective",plan.Status==AIOperationStatus.Suspended&&Find(manager,operationId)!=null);
            plan.CurrentContext.OwnCrystalThreatened=false;plan.CurrentContext.EconomyState=EconomicState.Healthy;
            typeof(AIOperationManager).GetMethod("Reevaluate",Hidden).Invoke(manager,new object[]{plan,board,17});
            Check(side+" recovery resumes the same objective",plan.Status!=AIOperationStatus.Suspended&&Find(manager,operationId)!=null);
            var snapshot=JsonUtility.FromJson<AIOperationBattleState>(JsonUtility.ToJson(manager.Snapshot(false)));
            var restored=new AIOperationManager(config);restored.BeginBattle(side,"campaign-test",snapshot);
            Check(side+" campaign identity and memory survive save",Find(restored,operationId)?.TargetLifeId==target.ReflectionLifeId);
            restored.ConfirmKnownBaseDestroyed(target.ReflectionLifeId,false,false,board);
            Check(side+" an unobserved death event cannot finish the mission",Find(restored,operationId)!=null);
            restored.ConfirmKnownBaseDestroyed(target.ReflectionLifeId,true,false,board);
            Check(side+" observed destruction finally releases the mission",Find(restored,operationId)==null);
        }
        finally { UnityEngine.Object.DestroyImmediate(profile);UnityEngine.Object.DestroyImmediate(config);UnityEngine.Object.DestroyImmediate(host); }
    }
    static AIOperationPlan Find(AIOperationManager manager,long id)
    { foreach(var plan in manager.ActiveOperations)if(plan.OperationId==id)return plan;return null; }
}
#endif
