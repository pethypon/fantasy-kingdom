#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

public static class SpecialSkillAITests
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static int passed;
    static void Check(string label,bool value)
    {if(!value)throw new InvalidOperationException("[SpecialAI] FAIL "+label);passed++;Debug.Log("[SpecialAI] PASS "+label);}
    static SpecialSkillDefinition Configure(Status actor,UnitSpecialSkill ability,List<UnityEngine.Object> owned)
    {
        var definition=ScriptableObject.CreateInstance<SpecialSkillDefinition>();owned.Add(definition);
        definition.ability=ability;definition.apCost=2;definition.cooldown=1;actor.GrowthData.specialSkill=definition;
        if (ability == UnitSpecialSkill.CrossbowStakes) definition.crossbowInitialAmmo = 0;
        return definition;
    }
    public static void PlayMode(GameSystems live)
    {
        passed=0;
        foreach(var team in new[]{Team.Player,Team.Enemy})
        {
            Faction(live,team);
            ConfiguredExecution(live,team,UnitSpecialSkill.WhiteMageHeal);
            ConfiguredExecution(live,team,UnitSpecialSkill.MagicRitual);
        }
        SpecialSkillR1AITests.PlayMode(live);
        Learning();Debug.Log("[SpecialAI] "+passed+" checks passed");
    }
    static void Faction(GameSystems live,Team team)
    {
        using(var f=new EconomyR2Tests.Fixture(live,team))
        {
            var owned=new List<UnityEngine.Object>();
            var go=new GameObject("Special AI observed-only vision");go.SetActive(false);owned.Add(go);
            var vision=go.AddComponent<VisionGenerator>();var visible=new HashSet<Vector3Int>();
            foreach(string name in new[]{"_playerVisionBox","_enemyVisionBox","_playerExplored","_enemyExplored"})
                typeof(VisionGenerator).GetField(name,Private).SetValue(vision,
                    name.StartsWith(team==Team.Player?"_player":"_enemy")?visible:new HashSet<Vector3Int>());
            var systems=new GameSystems{APSystem=f.AP,FactionState=f.State,MoveGenerator=live.MoveGenerator,
                UnitSetting=f.Units,CrystalSystem=f.Crystals,VisionGenerator=vision,MapCreate=live.MapCreate};
            AIBoardState Board()
            {
                var board=new AIBoardState(live.MoveGenerator,live.AttackGenerator,f.AP,f.Units,f.Crystals,
                    vision,f.Builder,null,f.State,null,20,null,team);
                board.SpecialSystems=systems;board.MapCreate=live.MapCreate;board.ReconThreatLevel=51;return board;
            }
            try
            {
                var mage=f.Unit();mage.kind=Kind.Magic;mage.transform.position=new Vector3(10,0,10);
                var ritual=Configure(mage,UnitSpecialSkill.MagicRitual,owned);ritual.sacrificeLimit=3;
                var foe=f.Opponent(new Vector3(11,0,10),0);foe.HP=25;
                var hidden=f.Opponent(new Vector3(12,0,10),9999);
                visible.Add(foe.GridPosition);mage.VisionCell.Add(foe.GridPosition);mage.VisionCell.Add(hidden.GridPosition);
                for(int i=0;i<3;i++){var ally=f.Unit();ally.transform.position=new Vector3(10,0,11+i%2);}
                var board=Board();var candidates=new List<AIAction>();
                AIActionGenerator.GenerateSpecialSkillCandidates(mage,board,candidates);
                Check(team+" ritual creates bounded representative branches",candidates.Count>0&&candidates.Count<=4);
                bool minimum=false,kill=false,maximum=false;
                foreach(var candidate in candidates)
                {
                    Check(team+" hidden hostile never becomes lock candidate",candidate.TargetUnit==foe);
                    var sacrifices=candidate.SpecialCommand.Sacrifices;
                    Check(team+" ritual respects authored count",sacrifices.Count>0&&sacrifices.Count<=ritual.sacrificeLimit);
                    minimum|=sacrifices.Count==1;maximum|=sacrifices.Count==ritual.sacrificeLimit;
                    int power=0;foreach(var actor in sacrifices)power+=Mathf.Min(actor.HP,(int)Math.Floor(actor.MaxHP*(double)SpecialSkillDefinition.RitualSacrifice));
                    kill|=sacrifices.Count==2&&power>=foe.HP;
                    Check(team+" special candidate keeps shared AP and static future score",candidate.APCost==SpecialSkillRuntime.GetAPCost(mage)
                        &&AISpecialSkillKnowledge.NeedsSpecialStateModel(candidate));
                }
                Check(team+" minimum, minimum kill and maximum remain distinct",minimum&&kill&&maximum);
                var before=new List<Status>();board.CollectSpecialTargets(mage,before);
                hidden.transform.position=new Vector3(1000,0,1000);hidden.HP=1;board.Refresh();var after=new List<Status>();board.CollectSpecialTargets(mage,after);
                Check(team+" hidden actor mutation cannot change legal target set",before.Count==after.Count&&after.Contains(foe)&&!after.Contains(hidden));
                ritual.cooldown=-1;candidates.Clear();AIActionGenerator.GenerateSpecialSkillCandidates(mage,board,candidates);
                Check(team+" undefined cooldown never becomes a free active skill",candidates.Count==0);ritual.cooldown=1;

                var attack=new AIAction{ActionType=AIActionType.Attack,Unit=mage,TargetUnit=foe,APCost=1};mage.ATK=100;
                foe.Special.damageNullUnused=true;foe.Special.damageNullTurns=2;board.Refresh();
                Check(team+" damage null prevents false finishing bonus",!AIActionEvaluator.CanFinishSpecialTarget(mage,foe,board));
                foe.Special.damageNullUnused=false;foe.Special.damageNullTurns=0;foe.Special.ritualTargetId=mage.ReflectionLifeId;foe.Special.storedSacrificeDamage=100;
                board.Refresh();float interruption=AIActionEvaluator.CalcSpecialStateAdjustment(attack,board);
                foe.Special.ritualTargetId=null;foe.Special.storedSacrificeDamage=0;board.Refresh();
                Check(team+" observed ritual caster earns interruption value",interruption>AIActionEvaluator.CalcSpecialStateAdjustment(attack,board));

                var archer=f.Unit();archer.kind=Kind.Archer;archer.ATK=20;archer.direction=Direction.N;archer.transform.position=new Vector3(20,0,20);
                var charge=Configure(archer,UnitSpecialSkill.ArcherStationary,owned);
                charge.stationaryAttackCurve=AnimationCurve.Linear(0,1,3,SpecialSkillDefinition.ArcherMaximum);archer.Special.stationaryTurns=3;
                foe.transform.position=new Vector3(20,0,22);foe.HP=foe.MaxHP=100;visible.Clear();visible.Add(foe.GridPosition);
                board.Refresh();
                var wait=new AIAction{ActionType=AIActionType.Wait,Unit=archer,Score=1};wait.Score+=AIActionEvaluator.CalcSpecialStateAdjustment(wait,board);
                var move=new AIAction{ActionType=AIActionType.Move,Unit=archer,TargetPos=archer.transform.position+Vector3.right,Score=5};
                Check(team+" charged safe archer preserves a firing position",AIActionEvaluator.ShouldPreserveStationary(move,new List<AIAction>{move,wait},board));
                foe.Special.ritualTargetId=archer.ReflectionLifeId;foe.Special.storedSacrificeDamage=100;foe.Special.ritualFireTurn=foe.Special.ownerTurn+1;board.Refresh();
                Check(team+" lethal reservation overrides stationary preservation",!AIActionEvaluator.ShouldPreserveStationary(move,new List<AIAction>{move,wait},board)
                    &&AIActionEvaluator.EstimateSpecialReservedThreatAt(board,archer,archer.transform.position)>0);
                foe.Special.ritualTargetId=null;

                var crossbow=f.Opponent(new Vector3(31,0,30),20);Configure(crossbow,UnitSpecialSkill.CrossbowStakes,owned).lightningFactionMask=SpecialFactionMask.Hostile;
                var a=f.Unit();a.transform.position=new Vector3(30,0,30);var b=f.Unit();b.transform.position=new Vector3(34,0,30);
                var walker=f.Unit();walker.transform.position=new Vector3(32,0,29);
                crossbow.Special.stakeAId=a.ReflectionLifeId;crossbow.Special.stakeBId=b.ReflectionLifeId;
                crossbow.Special.ownerTurn=1;crossbow.Special.stakeAAppliedTurn=crossbow.Special.stakeBAppliedTurn=1;
                visible.Clear();visible.Add(crossbow.GridPosition);board.Refresh();
                Vector3 crossing=new Vector3(32,0,31),safe=new Vector3(33,0,29);
                Check(team+" observed line is a movement hazard",board.EstimateSpecialHazardAt(walker,crossing)>0&&board.EstimateSpecialHazardAt(walker,safe)==0);
                visible.Clear();board.Refresh();
                Check(team+" hidden crossbow cannot reveal a current line",board.EstimateSpecialHazardAt(walker,crossing)==0);
                Check(team+" knowledge reads the same gameplay definition",ReferenceEquals(AISpecialSkillKnowledge.For(mage).Definition,ritual));
                var guardian=f.Unit(); Configure(guardian,UnitSpecialSkill.GuardianLifeLink,owned);
                var supportObject=new GameObject("Observed owned building for life link");owned.Add(supportObject);
                supportObject.transform.SetParent(f.Builder.GetBuildingParent(team),false);
                var support=supportObject.AddComponent<Status>();support.type=Type.Building;support.team=team;support.HP=support.MaxHP=200;
                board=Board();var supportTargets=new List<Status>();board.CollectSpecialTargets(guardian,supportTargets);
                Check(team+" guardian can choose an owned important building",supportTargets.Contains(support)
                    &&board.FindObservedSpecialActor(support.ReflectionLifeId)==support);
                var berserker=f.Unit();var rage=Configure(berserker,UnitSpecialSkill.BerserkerRage,owned);rage.ApplySpecifiedAPCost();
                rage.berserkerAttackCurve=AnimationCurve.Linear(0,1,5,2.5f);
                rage.berserkerDefenceCurve=AnimationCurve.Linear(0,1,5,.5f);
                rage.berserkerAttackAPCurve=AnimationCurve.Linear(0,1,1,5);
                f.State.SetAP(team,20);board=Board();candidates.Clear();
                AIActionGenerator.GenerateSpecialSkillCandidates(berserker,board,candidates);
                Check(team+" AI considers only the next Berserker stage",candidates.Count==1&&candidates[0].SpecialCommand.BerserkerStage==1&&candidates[0].APCost==5);
                berserker.Special.berserkerStage=2;f.State.SetAP(team,10);board=Board();candidates.Clear();
                AIActionGenerator.GenerateSpecialSkillCandidates(berserker,board,candidates);
                Check(team+" AI excludes an unaffordable next Berserker stage",candidates.Count==0);
                f.State.SetAP(team,11);board=Board();candidates.Clear();AIActionGenerator.GenerateSpecialSkillCandidates(berserker,board,candidates);
                Check(team+" AI pays eleven AP for the next third Berserker stage",candidates.Count==1&&candidates[0].SpecialCommand.BerserkerStage==3&&candidates[0].APCost==11);
                berserker.Special.berserkerStage=5;f.State.SetAP(team,20);board=Board();candidates.Clear();AIActionGenerator.GenerateSpecialSkillCandidates(berserker,board,candidates);
                Check(team+" AI never generates a sixth Berserker stage",candidates.Count==0);
                rage.stageAPCosts[4]=APSystem.MaximumSkillAP+1;
                Check(team+" malformed Berserker stage AP is rejected",rage.ConfigurationError!=null);
            }
            finally{foreach(var value in owned)if(value!=null)UnityEngine.Object.DestroyImmediate(value);}
        }
    }
    // Normal generator/evaluator -> commander selector -> executor, with exact requested AP.
    // Only the scene services and actors are isolated; no command/score is forced into the selector.
    static void ConfiguredExecution(GameSystems live,Team team,UnitSpecialSkill ability)
    {
        using(var f=new EconomyR2Tests.Fixture(live,team))
        {
            var owned=new List<UnityEngine.Object>();
            var originalSystems=live.TurnGenerator.Systems;
            AICommander commander=null;
            try
            {
                var visionObject=new GameObject("Configured skill AI vision");visionObject.SetActive(false);owned.Add(visionObject);
                var vision=visionObject.AddComponent<VisionGenerator>();var visible=new HashSet<Vector3Int>();
                foreach(string name in new[]{"_playerVisionBox","_enemyVisionBox","_playerExplored","_enemyExplored"})
                    typeof(VisionGenerator).GetField(name,Private).SetValue(vision,
                        name.StartsWith(team==Team.Player?"_player":"_enemy")?visible:new HashSet<Vector3Int>());
                var systems=new GameSystems{APSystem=f.AP,FactionState=f.State,MoveGenerator=live.MoveGenerator,
                    UnitSetting=f.Units,CrystalSystem=f.Crystals,VisionGenerator=vision};
                typeof(GameSystems).GetProperty("TurnGenerator").SetValue(systems,live.TurnGenerator);
                typeof(TurnGenerator).GetProperty("Systems").SetValue(live.TurnGenerator,systems);
                var caster=f.Unit();caster.transform.position=new Vector3(10,0,10);caster.ATK=100;
                var definition=Configure(caster,ability,owned);definition.ApplySpecifiedAPCost();
                definition.sacrificeLimit=3;definition.lightningFactionMask=SpecialFactionMask.Hostile;
                caster.kind=ability==UnitSpecialSkill.WhiteMageHeal?Kind.WhiteMage:Kind.Magic;
                void Passive(Status actor,bool skills)
                {
                    var profile=ScriptableObject.CreateInstance<BoardActionProfile>();owned.Add(profile);
                    profile.actionType=skills?ActorActionType.Stationary:ActorActionType.Passive;
                    profile.attack.useCustom=true;actor.GrowthData.actionProfile=profile;
                }
                Passive(caster,true);
                var patient=f.Unit();patient.transform.position=new Vector3(10,0,11);Passive(patient,false);
                Status first=null;
                if(ability==UnitSpecialSkill.WhiteMageHeal)
                { patient.HP=40;caster.VisionCell.Add(patient.GridPosition); }
                else
                {
                    first=f.Opponent(new Vector3(11,0,10),0);first.DEF=0;first.HP=ability==UnitSpecialSkill.MagicRitual?25:100;
                    Passive(first,false);visible.Add(first.GridPosition);caster.VisionCell.Add(first.GridPosition);
                    { var donor=f.Unit();donor.transform.position=new Vector3(10,0,12);Passive(donor,false); }
                }
                // No economic purchases in this tactical fixture; AP is still the real faction AP.
                var resources=f.Resources;resources.Wood=resources.Stone=resources.Iron=resources.MagicOre=resources.Water=resources.Wheat=resources.Bread=resources.Citizen=0;
                var board=new AIBoardState(live.MoveGenerator,live.AttackGenerator,f.AP,f.Units,f.Crystals,
                    vision,f.Builder,null,f.State,null,20,null,team){SpecialSystems=systems,MapCreate=live.MapCreate,ReconThreatLevel=15,Governor=new AIStrategicGovernor()};
                commander=new AICommander(live.TurnGenerator,live.MoveGenerator,live.AttackGenerator,live.BattleSystem,vision,
                    f.AP,f.Units,f.Crystals,live.MapCreate,MajorPersonality.Combat,f.Builder,null,f.State,null,null,15,1234,team);
                typeof(AICommander).GetField("_board",Private).SetValue(commander,board);
                var actions=new List<AIAction>();
                AIAction Decide()
                {
                    AIActionEvaluator.EvaluateAllInto(actions,commander.Personality,board,commander.Learning);
                    return (AIAction)typeof(AICommander).GetMethod("SelectBestAction",Private)
                        .Invoke(commander,new object[]{actions,new HashSet<string>(),new HashSet<string>()});
                }
                var action=Decide();
                Check(team+" exact AP "+ability+" is selected by the ordinary AI pipeline",action?.SpecialCommand?.Caster==caster);
                var extractor=new AIActionFeatureExtractor();
                var before=extractor.Capture(action,board,20,TurnStrategy.Balanced,15,0);
                extractor.GetSelectionKeys(action,board,20,TurnStrategy.Balanced,15,0,out _,out var firstKey);
                var configuredAbility=definition.ability;definition.ability=UnitSpecialSkill.NinjaSwap;
                extractor.GetSelectionKeys(action,board,20,TurnStrategy.Balanced,15,0,out _,out var differentKey);
                definition.ability=configuredAbility;
                Check(team+" special learning/cache key distinguishes fixed ability "+ability,firstKey!=differentKey
                    &&firstKey.Contains(":special"+(int)ability));
                int ap=f.State.GetAP(team),otherAP=f.State.GetAP(team==Team.Player?Team.Enemy:Team.Player),hp=patient.HP;
                var executor=(AIActionExecutor)typeof(AICommander).GetField("_actionExecutor",Private).GetValue(commander);
                Check(team+" executes selected "+ability,executor.Execute(action,board));
                Check(team+" pays exact authored "+ability+" AP only once",ap-f.State.GetAP(team)==definition.apCost
                    &&f.State.GetAP(team==Team.Player?Team.Enemy:Team.Player)==otherAP);
                Check(team+" cooldown prevents repeat "+ability,!SpecialSkillRuntime.CanUse(caster,systems,out _));
                var after=extractor.Capture(action,board,20,TurnStrategy.Balanced,15,0);
                var record=new AIActionRecord{ActionId=1,ActionType=AIActionType.SkillUse,ExecutionSucceeded=true,
                    Before=before,After=after,Outcome=AIActionOutcome.Difference(before,after),
                    ActorLifeId=caster.ReflectionLifeId,TargetLifeId=before.KnownTargetLifeId};
                record.MeaningfulProgress=AIFailureAnalyzer.HasMeaningfulProgress(record);
                Check(team+" applied "+ability+" counts as preparation rather than no-damage failure",record.MeaningfulProgress
                    &&AIFailureAnalyzer.Analyze(record)==AIFailureReason.None);
                var delayed=new AIDelayedCreditAssigner();delayed.Remember(record);
                Check(team+" applied "+ability+" remains eligible for delayed credit",delayed.Capture().RecentActions[0].Preparation);
                record.After=before.Copy();record.Outcome=AIActionOutcome.Difference(before,record.After);
                Check(team+" a successful no-change executor alone receives no special credit",!AIFailureAnalyzer.HasMeaningfulProgress(record));
                if(ability==UnitSpecialSkill.WhiteMageHeal)
                {
                    Check(team+" actual AI healing reaches ally",patient.HP==hp+30);
                    SpecialSkillRuntime.OnActorTurnStart(caster,systems,21);patient.HP=patient.MaxHP-1;board.Refresh();
                    var tiny=new AIAction{Unit=caster,TargetUnit=patient,ActionType=AIActionType.SkillUse,APCost=12,SpecialCommand=new SpecialSkillCommand(caster,patient)};
                    Check(team+" one HP heal does not justify AP12",AIActionEvaluator.CalcSpecialSkillScore(tiny,board)<=0);
                }
                else if(ability==UnitSpecialSkill.MagicRitual)
                {
                    Check(team+" actual AI ritual reserves sufficient donated damage",caster.Special.ritualActive&&caster.Special.storedSacrificeDamage>=first.HP);
                    SpecialSkillRuntime.OnActorTurnStart(caster,systems,21);
                    Check(team+" AI reservation fires on the next owner turn",!first.IsAlive&&!caster.Special.ritualActive);
                }
            }
            finally
            {
                typeof(TurnGenerator).GetProperty("Systems").SetValue(live.TurnGenerator,originalSystems);
                commander?.ReleaseRuntimeReferences();
                foreach(var value in owned)if(value!=null)UnityEngine.Object.DestroyImmediate(value);
            }
        }
    }
    static void Learning()
    {
        string directory=Path.Combine(Path.GetTempPath(),"FantasyKingdom-SpecialAI-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);string path=Path.Combine(directory,"profile.json");
        try
        {
            var model=new AIPlayerModel(path);
            model.RecordSpecialSample(UnitSpecialSkill.MagicRitual,true,float.NaN);
            model.RecordSpecialSample(UnitSpecialSkill.MagicRitual,true,.5f);
            Check("special-only observations save",model.CompleteMatch());
            var loaded=new AIPlayerModel(path).Snapshot;int index=(int)UnitSpecialSkill.MagicRitual;
            Check("missing observations do not enter detail denominator",loaded.SpecialSamples[index]==2&&loaded.SpecialDetailSamples[index]==1
                &&Mathf.Abs(loaded.SpecialDetail[index]-.1f)<.0001f);
            string json=File.ReadAllText(path);
            Check("special learning stores no actors, targets or positions",!json.Contains("Position")&&!json.Contains("TargetId")&&!json.Contains("Sacrifices")&&!json.Contains("HP"));
            var snapshot=model.Snapshot;snapshot.SpecialUse[index]=1;
            Check("profile snapshot does not alias live arrays",model.Snapshot.SpecialUse[index]<1);
            Check("completed match does not duplicate samples",model.CompleteMatch()&&new AIPlayerModel(path).Snapshot.SpecialSamples[index]==2);
        }
        finally{foreach(string file in Directory.GetFiles(directory))File.Delete(file);Directory.Delete(directory);}
    }
}
#endif
