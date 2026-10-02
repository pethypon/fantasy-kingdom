#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
public static class AIAdvancedStrategyTests
{
    static void Check(string name,bool ok){if(!ok)throw new Exception("[AdvancedAI] "+name);Debug.Log("[AdvancedAI] PASS "+name);}
    static void Set<T>(AIBoardState b,string name,T value)=>typeof(AIBoardState).GetProperty(name).SetValue(b,value);
    static void Changed(AIBoardState b)
    {
        Set(b,"Generation",b.Generation+1);
        typeof(AIBoardState).GetField("_visionCacheVersion",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(b,-1);
    }
    static Status Unit(List<GameObject> objects,Kind kind,Team team,Vector3 position,Type type=Type.Unit)
    {
        var go=new GameObject("Operation fixture "+kind);go.SetActive(false);objects.Add(go);
        var s=go.AddComponent<Status>();s.kind=kind;s.team=team;s.type=type;s.HP=s.MaxHP=100;s.ATK=20;s.direction=Direction.N;
        go.transform.position=position;go.SetActive(true);return s;
    }
    public static void Run(GameSystems s)
    {
        string settingsPath="Assets/Resources/AI/AITacticalPatternSettings.asset";
        if(!AssetDatabase.LoadAssetAtPath<AITacticalPatternSettings>(settingsPath))
        {
            if(!AssetDatabase.IsValidFolder("Assets/Resources/AI"))AssetDatabase.CreateFolder("Assets/Resources","AI");
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<AITacticalPatternSettings>(),settingsPath);AssetDatabase.SaveAssets();
        }
        var settings=AssetDatabase.LoadAssetAtPath<AITacticalPatternSettings>(settingsPath);
        var settingsScript=MonoScript.FromScriptableObject(settings);
        Check("Inspector settings have matching MonoScript",settingsScript!=null && settingsScript.GetClass()==typeof(AITacticalPatternSettings));
        // Repair the first draft's asset as well as checking newly created settings.
        var serializedSettings=new SerializedObject(settings);
        serializedSettings.FindProperty("m_Script").objectReferenceValue=settingsScript;
        serializedSettings.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(settings);AssetDatabase.SaveAssets();
        Check("settings serialize a persistent script reference",File.ReadAllText(settingsPath).Contains("guid: "+AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(settingsScript))));
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var seen=(HashSet<Vector3Int>)typeof(VisionGenerator).GetField("_enemyVisionBox",flags).GetValue(s.VisionGenerator);
        var explored=(HashSet<Vector3Int>)typeof(VisionGenerator).GetField("_enemyExplored",flags).GetValue(s.VisionGenerator);
        var savedSeen=seen.ToArray();var savedExplored=explored.ToArray();
        var objects=new List<GameObject>();string file=Path.Combine(Path.GetTempPath(),"FK-Response-"+Guid.NewGuid()+".json");
        try
        {
            seen.Clear();explored.Clear();
            var memory=AIBoardState.CreateSharedMemory();
            var b=new AIBoardState(s.MoveGenerator,s.AttackGenerator,s.APSystem,s.UnitSetting,s.CrystalSystem,s.VisionGenerator,s.BuildSystem,s.SummonSystem,s.FactionState,s.SubCrystalSystem,10,memory);
            Set(b,"EnemyCrystalPos",new Vector3(10,0,10));Set(b,"EnemyCrystalHP",100);Set(b,"EnemyCrystalMaxHP",100);
            b.ReconThreatLevel=10;b.AliveEnemyUnits.Clear();b.AlivePlayerUnits.Clear();memory.Clear();
            var origin=new Vector3Int(15,0,15);
            memory[999]=new AIBoardState.LastKnownInfo{Valid=true,Position=origin,Turn=7,Kind=Kind.Knight,Type=Type.Unit,Direction=Direction.N};Changed(b);
            var contact=b.Belief.Contacts[0];
            Check("missing Knight expands along legal movement",contact.Cells.Count>1);
            Check("probability plus unknown mass is normalized",Mathf.Abs(contact.Cells.Values.Sum()+contact.UnknownProbability-1)<.0001f);
            Check("probability stays bounded",contact.Cells.Values.All(p=>p>=0&&p<=1));
            var observedCell=contact.Cells.OrderByDescending(p=>p.Value).First().Key;
            seen.Add(observedCell);Changed(b);Check("visible empty cell removes belief",b.Belief.ProbabilityAt(observedCell)==0);
            int builds=b.Belief.Rebuilds;Changed(b);b.Belief.ProbabilityAt(origin);
            Check("unchanged evidence reuses distribution across refresh",b.Belief.Rebuilds==builds);
            memory[999]=new AIBoardState.LastKnownInfo{Valid=true,Position=origin,Turn=9,Kind=Kind.Knight,Type=Type.Unit,Direction=Direction.N};seen.Clear();Changed(b);
            int knightSpread=b.Belief.Contacts[0].Cells.Count;
            memory[999]=new AIBoardState.LastKnownInfo{Valid=true,Position=origin,Turn=9,Kind=Kind.Scout,Type=Type.Unit,Direction=Direction.N};Changed(b);
            Check("Scout spreads wider than Knight",b.Belief.Contacts[0].Cells.Count>knightSpread);
            memory[999]=new AIBoardState.LastKnownInfo{Valid=true,Position=origin,Turn=9,Kind=Kind.Assassin,Type=Type.Unit,Direction=Direction.N};Changed(b);
            Check("Assassin uncertainty exceeds Knight",b.Belief.Contacts[0].Cells.Count>knightSpread);
            var hidden=Unit(objects,Kind.Knight,Team.Player,new Vector3(20,0,20));float risk=b.Belief.RiskAt(origin,100);
            hidden.transform.position=new Vector3(1,0,1);hidden.HP=1;hidden.kind=Kind.Boss;Changed(b);
            Check("hidden object changes cannot alter beliefs",Mathf.Abs(risk-b.Belief.RiskAt(origin,100))<.0001f);
            b.ReconThreatLevel=9;Changed(b);Check("belief disabled before ten",b.Belief.Contacts.Count==0);b.ReconThreatLevel=10;Changed(b);
            Check("bounded probability storage",b.Belief.Contacts.Count<=AIBeliefMap.MaxContacts&&b.Belief.Contacts.All(c=>c.Cells.Count<=AIBeliefMap.MaxCellsPerContact));
            var watch=System.Diagnostics.Stopwatch.StartNew();float checksum=0;
            for(int i=0;i<10000;i++)checksum+=b.Belief.RiskAt(origin,100);
            watch.Stop();Check("cached risk query finishes within generous 100ms",watch.Elapsed.TotalMilliseconds<100);
            Debug.Log($"[AdvancedAI] cached risk 10000 queries {watch.Elapsed.TotalMilliseconds:F3}ms checksum={checksum}");
            memory.Clear();
            for(int i=0;i<40;i++)memory[1000+i]=new AIBoardState.LastKnownInfo{Valid=true,Position=origin,Turn=4,Kind=i%2==0?Kind.Scout:Kind.Assassin,Type=Type.Unit,Direction=Direction.N,ObservedAttackPower=25};
            Changed(b);watch.Restart();int retainedContacts=b.Belief.Contacts.Count;watch.Stop();
            Check("large missing army remains capped",retainedContacts==AIBeliefMap.MaxContacts&&b.Belief.Contacts.All(c=>c.Cells.Count<=AIBeliefMap.MaxCellsPerContact));
            Check("greater caution cannot lower overlapping contact risk",b.Belief.RiskAt(origin,100)>=b.Belief.RiskAt(origin,10));
            Debug.Log($"[AdvancedAI] maximum-contact belief rebuild {watch.Elapsed.TotalMilliseconds:F3}ms cells={b.Belief.Contacts.Sum(c=>c.Cells.Count)}");

            memory.Clear();b.AlivePlayerUnits.Clear();b.AliveEnemyUnits.Clear();seen.Clear();Changed(b);
            var scout=Unit(objects,Kind.Scout,Team.Enemy,new Vector3(12,0,10));
            var assassin=Unit(objects,Kind.Assassin,Team.Enemy,new Vector3(12,0,11));
            var knight=Unit(objects,Kind.Knight,Team.Enemy,new Vector3(12,0,12));
            b.AliveEnemyUnits.AddRange(new[]{scout,assassin,knight});
            var target=Unit(objects,Kind.SubCrystal,Team.Player,new Vector3(24,0,10),Type.Building);target.facilityKind=FacilityKind.SubCrystal;
            var p1=Unit(objects,Kind.Knight,Team.Player,new Vector3(24,0,14));var p2=Unit(objects,Kind.Knight,Team.Player,new Vector3(24,0,16));
            b.AlivePlayerUnits.AddRange(new[]{target,p1,p2});foreach(var u in b.AlivePlayerUnits)seen.Add(GridHelper.ToGridXZ(u.transform.position));
            var plans=new AIPlanManager();plans.Update(b);
            Check("observed outpost and available wing choose feint",plans.Current.Goal==AIPlanGoal.EasternFeint&&plans.Current.Step==AIPlanStep.Assemble);
            Changed(b);plans.Update(b);Check("required troops advance assemble",plans.Current.Step==AIPlanStep.Demonstrate);
            scout.transform.position=plans.Current.DemonstrationPoint;Changed(b);plans.Update(b);
            Check("demonstration enters response observation",plans.Current.Step==AIPlanStep.ObserveResponse);
            p1.transform.position=plans.Current.DemonstrationPoint+Vector3.right;p2.transform.position=plans.Current.DemonstrationPoint+Vector3.left;
            Changed(b);plans.Update(b);Check("hidden arrivals cannot trigger feint",plans.Current.Step==AIPlanStep.ObserveResponse);
            var revealedBuilding=Unit(objects,Kind.None,Team.Player,plans.Current.DemonstrationPoint,Type.Building);
            b.AlivePlayerUnits.Add(revealedBuilding);seen.Add(GridHelper.ToGridXZ(revealedBuilding.transform.position));Changed(b);plans.Update(b);
            Check("revealed stationary facility is not a responding troop",plans.Current.VisibleResponders==0&&plans.Current.Step==AIPlanStep.ObserveResponse);
            b.AlivePlayerUnits.Remove(revealedBuilding);
            seen.Add(GridHelper.ToGridXZ(p1.transform.position));seen.Add(GridHelper.ToGridXZ(p2.transform.position));Changed(b);plans.Update(b);
            Check("two observed arrivals trigger opposite flank",plans.Current.Step==AIPlanStep.Flank&&plans.Current.VisibleResponders==2);
            assassin.transform.position=plans.Current.FlankPoint;Changed(b);plans.Update(b);Check("flanking wing reaches strike",plans.Current.Step==AIPlanStep.Strike);
            target.HP=0;plans.RecordSuccess(new AIAction{ActionType=AIActionType.Attack,Unit=assassin,TargetUnit=target});Changed(b);plans.Update(b);
            Check("confirmed outpost kill orders withdrawal",plans.Current.Step==AIPlanStep.Withdraw);
            foreach(var u in b.AliveEnemyUnits)u.transform.position=plans.Current.RallyPoint;Changed(b);plans.Update(b);
            Check("returning troops complete operation",plans.Current.Step==AIPlanStep.Complete);
            var copy=plans.Snapshot();copy.Step=AIPlanStep.Strike;copy.TargetDestroyedConfirmed=false;copy.StartedTurn=10;copy.StepStartedTurn=10;copy.TargetId=target.GetInstanceID();copy.ExpectedTurns=8;
            plans.Restore(copy);Check("save reload discards process-specific target ID",plans.Current.TargetId==0);
            target.HP=100;Changed(b);plans.Update(b);Check("reload relinks observed target safely",plans.Current.TargetId==target.GetInstanceID());
            Set(b,"EnemyCrystalHP",20);Changed(b);plans.Update(b);Check("own crystal emergency aborts operation",plans.Current.Step==AIPlanStep.Aborted);Set(b,"EnemyCrystalHP",100);
            plans.Restore(copy);Set(b,"TurnCount",18);Changed(b);plans.Update(b);Check("operation deadline aborts",plans.Current.Step==AIPlanStep.Aborted);Set(b,"TurnCount",10);
            b.ReconThreatLevel=9;plans.Restore(copy);Changed(b);plans.Update(b);Check("operations disabled before ten",plans.Current==null);b.ReconThreatLevel=10;

            var raid=new AIPlan{Goal=AIPlanGoal.EconomicRaid,Step=AIPlanStep.Strike,StartedTurn=10,StepStartedTurn=10,LastObservedTurn=10,ExpectedTurns=9,
                Target=target.transform.position,TargetKind=target.kind,TargetFacility=target.facilityKind,TargetType=Type.Building,RallyPoint=new Vector3(12,0,10),
                RequiredUnits=new[]{Kind.Assassin,Kind.Knight},Steps=new[]{AIPlanStep.Strike,AIPlanStep.SupplyDelay,AIPlanStep.MainAssault},InitialArmy=3};
            plans.Restore(raid);Changed(b);plans.Update(b);target.HP=0;plans.RecordSuccess(new AIAction{ActionType=AIActionType.Attack,Unit=assassin,TargetUnit=target});Changed(b);plans.Update(b);
            Check("economic raid enters supply delay",plans.Current.Step==AIPlanStep.SupplyDelay);
            Set(b,"TurnCount",11);Changed(b);plans.Update(b);Check("supply delay persists next turn",plans.Current.Step==AIPlanStep.SupplyDelay);
            Set(b,"TurnCount",12);Changed(b);plans.Update(b);Check("estimated supply delay releases main assault",plans.Current.Step==AIPlanStep.MainAssault);
            var serialized=JsonUtility.FromJson<SaveSystem.AISaveData>(JsonUtility.ToJson(new SaveSystem.AISaveData{Operation=plans.Snapshot()}));
            Check("optional operation survives game-save serialization",serialized.Operation.Goal==AIPlanGoal.EconomicRaid&&serialized.Operation.Step==AIPlanStep.MainAssault);
            plans.Restore(JsonUtility.FromJson<SaveSystem.AISaveData>("{}").Operation);
            Check("old game saves safely omit operation",plans.Current==null);
            // A trap only proceeds after support is ready and an observed opponent actually arrives.
            Set(b,"TurnCount",10);b.AliveEnemyUnits.Clear();b.AlivePlayerUnits.Clear();seen.Clear();
            var bow=Unit(objects,Kind.Crossbow,Team.Enemy,new Vector3(12,0,11));
            var bomber=Unit(objects,Kind.Bomber,Team.Enemy,new Vector3(12,0,12));
            scout.HP=55;scout.transform.position=new Vector3(12,0,10);b.AliveEnemyUnits.AddRange(new[]{scout,bow,bomber});
            p1.HP=100;p1.transform.position=new Vector3(23,0,12);b.AlivePlayerUnits.Add(p1);seen.Add(GridHelper.ToGridXZ(p1.transform.position));
            plans=new AIPlanManager();Changed(b);plans.Update(b);Check("supported ranged force chooses ambush",plans.Current.Goal==AIPlanGoal.Ambush);
            Changed(b);plans.Update(b);scout.transform.position=plans.Current.DemonstrationPoint;Changed(b);plans.Update(b);
            Check("unprepared support blocks bait phase",plans.Current.Step==AIPlanStep.Demonstrate);
            bow.transform.position=plans.Current.RallyPoint;bomber.transform.position=plans.Current.RallyPoint;Changed(b);plans.Update(b);
            Check("ready support arms observed bait",plans.Current.Step==AIPlanStep.ObserveResponse);
            p1.transform.position=plans.Current.DemonstrationPoint+Vector3.right;seen.Add(GridHelper.ToGridXZ(p1.transform.position));Changed(b);plans.Update(b);
            Check("visible pursuer triggers ambush strike",plans.Current.Step==AIPlanStep.Strike);
            Check("trap targets the observed pursuer",plans.Current.TargetId==p1.GetInstanceID()&&GridHelper.MatchXZ(plans.Current.Target,GridHelper.ToGridXZ(p1.transform.position)));
            Set(b,"TurnCount",11);Changed(b);plans.Update(b);Check("ambush cannot complete without a strike",plans.Current.Step==AIPlanStep.Strike);
            plans.RecordSuccess(new AIAction{ActionType=AIActionType.Attack,Unit=bow,TargetUnit=p1});Changed(b);plans.Update(b);
            Check("verified trap strike orders withdrawal",plans.Current.Step==AIPlanStep.Withdraw);


            var sim=AISlicingTests.Fixture();
            try
            {
                var weak=sim.Units.First(u=>u.Team==Team.Player&&u.Type==Type.Unit);weak.HP=20;
                var toward=new SimAction{Type=SimActionType.Move,UnitId=weak.Id,ActorTeam=Team.Player,TargetPos=weak.Position+Vector3Int.left};
                var away=new SimAction{Type=SimActionType.Move,UnitId=weak.Id,ActorTeam=Team.Player,TargetPos=weak.Position+Vector3Int.right};
                var response=new PlayerResponseModel(0,0,0,1,1,100);
                Check("learned low-HP retreat is more probable",response.Likelihood(away,sim)>response.Likelihood(toward,sim));
                Check("uncertain model retains worst-case weight",response.Combine(10,-10)<10&&response.Combine(10,-10)>-10);
                Check("zero evidence keeps the worst-case fallback",new PlayerResponseModel(0,0,0,0,0,100).Combine(10,-10)==-10);
                Check("higher threat has higher worst-case caution",response.WorstCaseWeight>new PlayerResponseModel(0,0,0,1,1,10).WorstCaseWeight);
                var engine=new AIMinimaxEngine(3,4,4,10000){ResponseModel=response};var results=new Dictionary<AIAction,float>();
                using(var work=engine.BeginSearch(AISlicingTests.Candidates(),sim,null,results))while(work.Step(3)) { }
                Check("profile used inside sliced response search",engine.CompletedDepth==3&&engine.ResponseNodesEvaluated>0);
                Check("profile search leaves live snapshot untouched",sim.Units.Count==8&&weak.HP==20&&sim.PlayerAP==8);
            }finally{SimBoardPool.ReturnBoard(sim);}

            // A visible low-health retreat supplies the model; missing observations do not count as retreats.
            b.AlivePlayerUnits.Clear();b.AlivePlayerUnits.Add(p1);p1.HP=20;p1.transform.position=new Vector3(20,0,10);seen.Clear();seen.Add(GridHelper.ToGridXZ(p1.transform.position));Set(b,"TurnCount",20);Changed(b);
            var model=new AIPlayerModel(file);Check("empty history preserves standard minimax",model.CreateResponseModel(10)==null);model.Observe(b);
            p1.transform.position=new Vector3(22,0,10);seen.Clear();seen.Add(GridHelper.ToGridXZ(p1.transform.position));Set(b,"TurnCount",21);Changed(b);model.Observe(b);
            Check("visible retreat influences response model",model.CreateResponseModel(21).Retreat>0);
            float learned=model.CreateResponseModel(21).Retreat;seen.Clear();p1.transform.position=new Vector3(0,0,0);Set(b,"TurnCount",22);Changed(b);model.Observe(b);
            Check("unseen movement does not train retreat tendency",Mathf.Abs(model.CreateResponseModel(21).Retreat-learned)<.0001f);
            Check("response model disabled below ten",model.CreateResponseModel(9)==null);model.CompleteMatch();
            Check("retreat tendency survives next match",new AIPlayerModel(file).Snapshot.Retreat>0);
            var profiler=new PlayerProfiler.PlayerProfile{TotalObservations=200,AggressionScore=1,RushTendency=1,EconomyFocus=1,SkillReliance=1,FlankPreference=1,TurtleTendency=1,PreferredAttackRange=5};
            var merged=model.CreateResponseModel(21,profiler);
            Check("existing profiler directly supplies response tendencies",merged.Aggression==1&&merged.Economy==1&&merged.Rush==1&&merged.Skill==1&&merged.Flank==1&&merged.Turtle==1&&merged.Ranged==1);

            var investment=new AIInvestmentPlanner();var armyPlan=new AIPlan{Goal=AIPlanGoal.Ambush,RequiredUnits=new[]{Kind.Bomber,Kind.Bomber},Step=AIPlanStep.Assemble};
            var res=b.EnemyResources;string old=JsonUtility.ToJson(res);
            try
            {
                res.Iron=res.Bread=res.Water=0;investment.Prepare(b,armyPlan);
                Check("future army creates forecast resource requirements",investment.NeededIron>0||investment.NeededBread>0||investment.NeededWater>0);
                Check("payback estimate is positive or infinite",investment.PaybackTurns(FacilityKind.Mine)>0);
                float early=investment.Bonus(new AIAction{ActionType=AIActionType.Build,Facility=FacilityKind.Mine},b);
                Set(b,"EnemyCrystalHP",10);Changed(b);investment.Prepare(b,armyPlan);
                Check("emergency shortens investment horizon",investment.EstimatedRemainingTurns==4);
                Check("late-payback project loses appeal in short horizon",investment.Bonus(new AIAction{ActionType=AIActionType.Build,Facility=FacilityKind.Mine},b)<=early);
            }finally{JsonUtility.FromJsonOverwrite(old,res);Set(b,"EnemyCrystalHP",100);}
            Check("six configurable motifs supplied",AITacticalPatterns.DefaultRules().Length==6);
            b.AliveEnemyUnits.Clear();b.AlivePlayerUnits.Clear();memory.Clear();seen.Clear();explored.Clear();Set(b,"TurnCount",30);
            assassin.HP=100;knight.HP=100;assassin.transform.position=new Vector3(19,0,20);knight.transform.position=new Vector3(18,0,20);
            b.AliveEnemyUnits.AddRange(new[]{assassin,knight});
            var priest=Unit(objects,Kind.Priest,Team.Player,new Vector3(20,0,20));b.AlivePlayerUnits.Add(priest);seen.Add(GridHelper.ToGridXZ(priest.transform.position));Changed(b);
            var motifs=new AITacticalPatterns(settings);motifs.Prepare(b);
            var attack=new AIAction{ActionType=AIActionType.Attack,Unit=assassin,TargetUnit=priest};
            Check("isolated Priest triggers pincer and assassination motifs",motifs.Bonus(attack,b)==48);
            seen.Clear();Changed(b);motifs.Prepare(b);Check("unseen target cannot trigger a tactical motif",motifs.Bonus(attack,b)==0);
            var cluster=new AIAction{ActionType=AIActionType.SkillUse,Unit=bomber,TargetUnit=bomber,Skill=new SkillData{Multiplier=1},AreaTargets=new List<Status>{priest,p1,p2}};
            Check("hidden area targets cannot trigger cluster bonus",motifs.Bonus(cluster,b)==0);
            foreach(var u in cluster.AreaTargets)seen.Add(GridHelper.ToGridXZ(u.transform.position));Changed(b);motifs.Prepare(b);
            Check("three-target area attack receives cluster bonus",motifs.Bonus(cluster,b)==30);
            b.ReconThreatLevel=9;Check("motifs disabled before ten",motifs.Bonus(cluster,b)==0);b.ReconThreatLevel=10;
            // Missing troop roles and unobserved reactions cannot keep an operation alive indefinitely.
            var assembling=new AIPlan{Goal=AIPlanGoal.EconomicRaid,Step=AIPlanStep.Assemble,StartedTurn=30,StepStartedTurn=30,LastObservedTurn=30,ExpectedTurns=9,
                Target=priest.transform.position,TargetType=Type.Unit,TargetKind=Kind.Priest,RequiredUnits=new[]{Kind.Bomber},Steps=new[]{AIPlanStep.Assemble},InitialArmy=2};
            plans.Restore(assembling);seen.Add(GridHelper.ToGridXZ(priest.transform.position));Set(b,"TurnCount",32);Changed(b);plans.Update(b);
            Check("missing required troop aborts after assembly window",plans.Current.Step==AIPlanStep.Aborted);
            assembling.RequiredUnits=new[]{Kind.Assassin};assembling.Step=AIPlanStep.ObserveResponse;assembling.Goal=AIPlanGoal.EasternFeint;assembling.DemonstrationPoint=new Vector3(25,0,25);
            plans.Restore(assembling);plans.Current.Step=AIPlanStep.ObserveResponse;Changed(b);plans.Update(b);
            Check("no visible response aborts after observation window",plans.Current.Step==AIPlanStep.Aborted);
        }
        finally
        {
            foreach(var go in objects)UnityEngine.Object.DestroyImmediate(go);
            seen.Clear();seen.UnionWith(savedSeen);explored.Clear();explored.UnionWith(savedExplored);
            foreach(var path in new[]{file,file+".bak",file+".tmp"})if(File.Exists(path))File.Delete(path);
        }
    }
}
#endif
