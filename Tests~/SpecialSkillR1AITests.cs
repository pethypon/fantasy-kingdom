#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>R1 forecasts run on real observed boards. Scoring cannot mutate or discover live state.</summary>
public static class SpecialSkillR1AITests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const int Origin = 21500;
    static int checks;
    static Vector3 At(int x, int z) => new Vector3(Origin + x, 1, Origin + z);
    static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException("[R1AI] " + message); checks++; }
    public static void PlayMode(GameSystems live)
    {
        checks = 0;
        foreach (var team in new[] { Team.Player, Team.Enemy })
        { Faction(live, team); CrossbowChoice(live, team); }
        Debug.Log("[SpecialSkillR1AI] " + checks + " observed skill/environment/recovery checks passed");
    }
    static void Faction(GameSystems live, Team team)
    {
        using (var fixture = new EconomyR2Tests.Fixture(live, team))
        {
            var owned = new List<UnityEngine.Object>();
            var originalSystems = live.TurnGenerator.Systems;
            try
            {
                var go = new GameObject("R1 AI isolated board"); owned.Add(go); go.SetActive(false);
                var map = go.AddComponent<MapCreate>(); map.UseR1Terrain = false; map.SetPos = new List<Vector3>();
                for (int x=0;x<12;x++) for(int z=0;z<12;z++) map.SetPos.Add(At(x,z));
                var moves = go.AddComponent<MoveGenerator>(); moves.mapcreate = map;
                typeof(MoveGenerator).GetField("crystalsystem",Private).SetValue(moves,fixture.Crystals);
                typeof(MoveGenerator).GetField("PlayerUnit",Private).SetValue(moves,fixture.Units.PlayerUnit);
                typeof(MoveGenerator).GetField("EnemyUnit",Private).SetValue(moves,fixture.Units.EnemyUnit);
                var attacks = go.AddComponent<AttackGenerator>(); attacks.mapcreate=map; attacks.moveGenerator=moves;
                var vision = go.AddComponent<VisionGenerator>(); var visible = new HashSet<Vector3Int>();
                foreach(string name in new[]{"_playerVisionBox","_enemyVisionBox","_playerExplored","_enemyExplored"})
                    typeof(VisionGenerator).GetField(name,Private).SetValue(vision,
                        name.StartsWith(team==Team.Player?"_player":"_enemy") ? visible : new HashSet<Vector3Int>());
                foreach(var point in map.SetPos) visible.Add(GridHelper.ToGridXZ(point));
                var systems = new GameSystems { APSystem=fixture.AP,FactionState=fixture.State,MapCreate=map,
                    MoveGenerator=moves,VisionGenerator=null,UnitSetting=fixture.Units,CrystalSystem=fixture.Crystals };
                // This fixture authors the observation snapshot explicitly. Actual vision recomputation
                // is covered by live-scene execution tests, rather than replacing this controlled snapshot.
                typeof(GameSystems).GetProperty("TurnGenerator").SetValue(systems,live.TurnGenerator);
                typeof(TurnGenerator).GetProperty("Systems").SetValue(live.TurnGenerator,systems);
                AIBoardState Board() => new AIBoardState(moves,attacks,fixture.AP,fixture.Units,fixture.Crystals,
                    vision,fixture.Builder,null,fixture.State,null,20,null,team)
                    { MapCreate=map,SpecialSystems=systems,ReconThreatLevel=31 };
                SpecialSkillDefinition Special(Status actor,UnitSpecialSkill ability)
                {
                    var definition=ScriptableObject.CreateInstance<SpecialSkillDefinition>();owned.Add(definition);
                    definition.ability=ability;definition.apCost=2;definition.cooldown=0;actor.GrowthData.specialSkill=definition;
                    SpecialSkillRuntime.InvalidateRules();
                    return definition;
                }
                Status Own(int x,int z) {var actor=fixture.Unit();actor.transform.position=At(x,z);actor.ATK=20;return actor;}
                Status Foe(int x,int z,int attack=20) {var actor=fixture.Opponent(At(x,z),attack);actor.DEF=0;return actor;}
                AIAction Skill(Status actor,Status target=null) => new AIAction {ActionType=AIActionType.SkillUse,Unit=actor,
                    TargetUnit=target??actor,TargetPos=(target??actor).transform.position,APCost=2,SpecialCommand=new SpecialSkillCommand(actor,target??actor)};

                var catalog=ScriptableObject.CreateInstance<TerrainEnvironmentCatalog>();owned.Add(catalog);catalog.enabledForPlay=true;
                var swamp=ScriptableObject.CreateInstance<TerrainEnvironmentDefinition>();owned.Add(swamp);
                swamp.definitionId="r1.ai.swamp";swamp.kind=TerrainEnvironmentKind.PoisonSwamp;swamp.enabledForPlay=true;
                swamp.damageOnEntry=true;swamp.damageOnOwnerTurn=true;swamp.flatDamage=20;
                var geyser=ScriptableObject.CreateInstance<TerrainEnvironmentDefinition>();owned.Add(geyser);
                geyser.definitionId="r1.ai.geyser";geyser.kind=TerrainEnvironmentKind.Geyser;geyser.enabledForPlay=true;
                catalog.definitions.Add(swamp);catalog.definitions.Add(geyser);
                catalog.placements.Add(new TerrainEnvironmentPlacement{definition=swamp,cell=new Vector2Int(Origin+3,Origin+2)});
                catalog.placements.Add(new TerrainEnvironmentPlacement{definition=geyser,cell=new Vector2Int(Origin+4,Origin+2)});
                map.InitializeEnvironment(catalog);
                var walker=Own(2,2);var victim=Foe(2,3);
                var board=Board();
                float hazard=AIActionEvaluator.KnownForcedEntryValue(board,victim,victim.transform.position,At(3,2));
                Check(hazard>0,"forced enemy entry values an authored known hazard");
                Check(AIActionEvaluator.KnownForcedEntryValue(board,walker,walker.transform.position,At(3,2))<0,
                    "same common hazard evaluator rejects friendly forced entry");
                Check(board.KnownEnvironmentDamage(victim,At(4,2),true)==0 && board.KnownEnvironmentDefinition(At(4,2))==null,
                    "an explored surface does not reveal an untriggered geyser");
                float undiscovered=AIActionEvaluator.KnownForcedEntryValue(board,victim,victim.transform.position,At(4,2));
                map.Environment.RevealTo(team,At(4,2));board.Refresh();
                Check(AIActionEvaluator.KnownForcedEntryValue(board,victim,victim.transform.position,At(4,2))>undiscovered,
                    "own geyser discovery adds hazard combination value");

                var knight=Own(3,0);knight.kind=Kind.Knight;knight.direction=Direction.N;Special(knight,UnitSpecialSkill.KnightBreakthrough);
                knight.HP = knight.MaxHP = 10000; // Isolate hostile entry value from the caster following into the same swamp.
                victim.transform.position=At(3,1);board.Refresh();var charge=Skill(knight);
                float hazardousCharge=AIActionEvaluator.CalcSpecialSkillScore(charge,board);
                var placements=new List<TerrainEnvironmentPlacement>(catalog.placements);catalog.placements.Clear();map.InitializeEnvironment(catalog);board.Refresh();
                Check(hazardousCharge>AIActionEvaluator.CalcSpecialSkillScore(charge,board),"knight push values intermediate known poison entry even when its final cell is safe");
                catalog.placements.AddRange(placements);map.InitializeEnvironment(catalog);victim.transform.position=At(2,3);board.Refresh();

                var navigator=Own(3,1);Special(navigator,UnitSpecialSkill.NavigatorPull).navigatorAPDamageCurve=AnimationCurve.Linear(0,1,60,1.5f);
                navigator.VisionCell.Add(victim.GridPosition);var pull=Skill(navigator,victim);board.Refresh();
                float hazardousPull=AIActionEvaluator.CalcSpecialSkillScore(pull,board);
                catalog.placements.Clear();map.InitializeEnvironment(catalog);board.Refresh();
                Check(hazardousPull>AIActionEvaluator.CalcSpecialSkillScore(pull,board),"navigator values pulling into known poison without a scripted combo");

                var archer=Own(6,6);archer.kind=Kind.Archer;archer.direction=Direction.N;
                var archerRule=Special(archer,UnitSpecialSkill.ArcherStationary);archerRule.stationaryAttackCurve=AnimationCurve.Linear(0,1,3,1.75f);
                var poisonTarget=Foe(6,8);archer.VisionCell.Add(poisonTarget.GridPosition);
                var protectedAlly=Own(5,8);var enemyProfile=poisonTarget.GrowthData.actionProfile;
                enemyProfile.attack.useCustom=true;enemyProfile.attack.SetCell(2,3,true);
                board.Refresh();var escape=Skill(archer);float allyExposed=AIActionEvaluator.CalcSpecialSkillScore(escape,board);
                protectedAlly.transform.position=At(0,11);board.Refresh();
                Check(AIActionEvaluator.CalcSpecialSkillScore(escape,board)>allyExposed,"archer escape subtracts enemy ATK buff against another ally");
                var stationary=archer.GrowthData.actionProfile;
                var noAttack=ScriptableObject.CreateInstance<BoardActionProfile>();owned.Add(noAttack);noAttack.attack.useCustom=true;archer.GrowthData.actionProfile=noAttack;board.Refresh();
                float noShotOpportunity=AIActionEvaluator.CalcSpecialSkillScore(escape,board);
                archer.GrowthData.actionProfile=stationary;board.Refresh();
                Check(noShotOpportunity>AIActionEvaluator.CalcSpecialSkillScore(escape,board),"archer escape counts the two-turn attack lock opportunity");

                var recovery=Own(0,0);recovery.HP=40;recovery.NaturalRecovery=new NaturalRecoveryState
                    {HasTurnBaseline=true,ConsecutiveSafeTurns=4};board.Refresh();
                var wait=new AIAction{ActionType=AIActionType.Wait,Unit=recovery};
                float safeRecovery=AIActionEvaluator.CalcNaturalRecoveryAdjustment(wait,board);
                Check(safeRecovery>0&&wait.PreserveSpecialEvaluation,"wounded safe unit can value imminent natural recovery");
                recovery.NaturalRecovery.ConsecutiveSafeTurns=0;
                Check(AIActionEvaluator.CalcNaturalRecoveryAdjustment(wait,board)==0,"freshly damaged unit does not claim imminent recovery");
                recovery.NaturalRecovery.ConsecutiveSafeTurns=4;recovery.ActiveEffects.Add(new ActiveEffect(StatusEffectType.Poison,2));
                Check(AIActionEvaluator.CalcNaturalRecoveryAdjustment(wait,board)==0,"known poison prevents a false natural recovery plan");

                var magician=Own(8,2);Special(magician,UnitSpecialSkill.MagicRitual).sacrificeLimit=2;
                var shot=Foe(8,4);var hidden=Foe(8,3);visible.Remove(hidden.GridPosition);board.Refresh();
                int forecast=AIActionEvaluator.EstimateSpecialAwareAttack(magician,shot,board);
                hidden.HP=1;board.Refresh();
                Check(AIActionEvaluator.EstimateSpecialAwareAttack(magician,shot,board)==forecast,
                    "unseen wounded nearby enemy cannot alter magician ability forecast");

                var assassin=Own(1,8);assassin.kind=Kind.Assassin;assassin.direction=Direction.N;
                var assassinRule=Special(assassin,UnitSpecialSkill.AssassinVisionSeal);assassinRule.assassinBoostDuration=2;
                assassinRule.assassinBoostStackRule=AbilityStackRule.Strongest;
                var expendable=Own(2,9);expendable.HP=10;board.Refresh();
                var attackCandidates=new List<AIAction>();AIActionGenerator.GenerateAttackCandidates(assassin,board,attackCandidates);
                Check(!attackCandidates.Exists(candidate=>candidate.TargetUnit==expendable),"replacement assassin never generates allied execution candidates");
                var evaluated=AIActionEvaluator.EvaluateAll(new AIPersonality(MajorPersonality.Combat,123),board,null);
                var friendly=evaluated.Find(candidate=>candidate.Unit==assassin&&candidate.TargetUnit==expendable&&candidate.ActionType==AIActionType.Attack);
                Check(friendly==null,"replacement assassin cannot evaluate removed allied execution");

                var crossbow=Own(9,8);crossbow.kind=Kind.Crossbow;crossbow.direction=Direction.N;crossbow.ATK=10;
                var crossbowRule=Special(crossbow,UnitSpecialSkill.CrossbowStakes);crossbowRule.crossbowInitialAmmo=0;
                crossbowRule.lightningFactionMask=SpecialFactionMask.Hostile;
                Status endpointA=Foe(9,9), endpointB=Foe(9,10);crossbow.VisionCell.Add(endpointA.GridPosition);crossbow.VisionCell.Add(endpointB.GridPosition);
                board.Refresh();_ = board.SpecialObservedActors;
                string state=JsonUtility.ToJson(crossbow.Special);var skills=new List<AIAction>();
                AIActionGenerator.GenerateSpecialSkillCandidates(crossbow,board,skills);
                Check(skills.Count==1&&skills[0].TargetUnit==crossbow,"crossbow skill candidate loads its own magazine rather than attaching to an enemy");
                float reload=AIActionEvaluator.CalcSpecialSkillScore(skills[0],board);
                Check(JsonUtility.ToJson(crossbow.Special)==state,"crossbow candidate generation and future line evaluation cannot initialize or mutate live ammo");
                float again=AIActionEvaluator.CalcSpecialSkillScore(skills[0],board);
                Check(reload==again,"identical observed state produces identical skill value");
                var features=new AIActionFeatureExtractor();var before=features.Capture(skills[0],board,20,TurnStrategy.Balanced,31,0);
                Check(SpecialSkillRuntime.TryExecute(skills[0].SpecialCommand,systems,out _),"AI reload command executes through shared paid validation");
                board.Refresh();var after=features.Capture(skills[0],board,20,TurnStrategy.Balanced,31,0);
                var record=new AIActionRecord{ActionType=AIActionType.SkillUse,ExecutionSucceeded=true,Before=before,After=after,
                    Outcome=AIActionOutcome.Difference(before,after),ActorLifeId=crossbow.ReflectionLifeId,TargetLifeId=crossbow.ReflectionLifeId};
                Check(AIFailureAnalyzer.HasMeaningfulProgress(record),"real magazine loading is preparation progress rather than a no-damage failure");
                Check(SpecialSkillRuntime.CrossbowPeekRound(crossbow)==CrossbowRoundType.Stake,"AI uses the same oldest-round magazine model as gameplay");

                var defender=Own(4,7);defender.kind=Kind.Samurai;var taunt=Special(defender,UnitSpecialSkill.SamuraiCounter);
                taunt.samuraiGaugeMax=5;taunt.samuraiGaugeGain=1;defender.VisionCell.Add(endpointA.GridPosition);board.Refresh();skills.Clear();
                AIActionGenerator.GenerateSpecialSkillCandidates(defender,board,skills);
                Check(skills.Exists(candidate=>candidate.TargetUnit==endpointA),"samurai taunt participates in ordinary active-skill candidates");
                CombatRuleTelemetry.Enabled=true;AIActionEvaluator.CalcSpecialSkillScore(Skill(defender,endpointA),board);
                Check(CombatRuleTelemetry.Count>0,"AI diagnostics expose bounded skill evaluation records");CombatRuleTelemetry.Reset();

                var scout=Own(0,4);var scoutRule=Special(scout,UnitSpecialSkill.ScoutVisionGuard);
                scoutRule.scoutFreeMoveScope=AbilityResetScope.OwnerTurn;
                var borrower=Own(0,5);scout.VisionCell.Add(borrower.GridPosition);fixture.State.SetAP(team,0);board.Refresh();
                Check(AIActionGenerator.HasFreeBattleAction(board),"Scout free movement remains available at national AP0");
                fixture.AP.Consume(team,APSystem.ActionType.Move,borrower,borrower.transform.position,At(0,6));board.Refresh();
                Check(fixture.State.GetAP(team)==0 && borrower.Special.scoutFreeMoveUsed
                    && !AIActionGenerator.HasFreeBattleAction(board),"consuming the one free token terminates the zero AP action path");
                Check(!AIActionGenerator.IsFreeBattleAction(new AIAction{ActionType=AIActionType.Wait,APCost=0}),
                    "zero AP waiting cannot keep the AI loop alive");
            }
            finally
            {
                typeof(TurnGenerator).GetProperty("Systems").SetValue(live.TurnGenerator,originalSystems);
                CombatRuleTelemetry.Reset();
                for(int i=owned.Count-1;i>=0;i--)if(owned[i]!=null)UnityEngine.Object.DestroyImmediate(owned[i]);
            }
        }
    }
    static void CrossbowChoice(GameSystems live, Team team)
    {
        using (var fixture = new EconomyR2Tests.Fixture(live, team))
        {
            var owned = new List<UnityEngine.Object>();
            try
            {
                var go = new GameObject("R1 AI reload choice");go.SetActive(false);owned.Add(go);
                var vision = go.AddComponent<VisionGenerator>();var visible = new HashSet<Vector3Int>();
                foreach(string name in new[]{"_playerVisionBox","_enemyVisionBox","_playerExplored","_enemyExplored"})
                    typeof(VisionGenerator).GetField(name,Private).SetValue(vision,
                        name.StartsWith(team==Team.Player?"_player":"_enemy")?visible:new HashSet<Vector3Int>());
                var caster=fixture.Unit();caster.kind=Kind.Crossbow;caster.direction=Direction.N;caster.transform.position=new Vector3(10,0,10);caster.ATK=100;
                var profile=ScriptableObject.CreateInstance<BoardActionProfile>();owned.Add(profile);profile.actionType=ActorActionType.Stationary;caster.GrowthData.actionProfile=profile;
                var definition=ScriptableObject.CreateInstance<SpecialSkillDefinition>();owned.Add(definition);definition.ability=UnitSpecialSkill.CrossbowStakes;
                definition.ApplySpecifiedAPCost();definition.cooldown=0;definition.crossbowInitialAmmo=0;definition.lightningFactionMask=SpecialFactionMask.Hostile;caster.GrowthData.specialSkill=definition;
                var target=fixture.Opponent(new Vector3(10,0,11),0);target.HP=50;target.DEF=0;target.GrowthData.actionProfile.actionType=ActorActionType.Passive;
                visible.Add(target.GridPosition);caster.VisionCell.Add(target.GridPosition);
                var resources=fixture.Resources;resources.Wood=resources.Stone=resources.Iron=resources.MagicOre=resources.Water=resources.Wheat=resources.Bread=resources.Citizen=0;
                var systems=new GameSystems{APSystem=fixture.AP,FactionState=fixture.State,VisionGenerator=vision,MapCreate=live.MapCreate};
                var board=new AIBoardState(live.MoveGenerator,live.AttackGenerator,fixture.AP,fixture.Units,fixture.Crystals,vision,
                    fixture.Builder,null,fixture.State,null,20,null,team){MapCreate=live.MapCreate,SpecialSystems=systems,ReconThreatLevel=15};
                var actions=AIActionEvaluator.EvaluateAll(new AIPersonality(MajorPersonality.Combat,123),board,null);
                Check(actions.Count>0&&actions[0].SpecialCommand?.Caster==caster&&actions[0].TargetUnit==caster&&actions[0].APCost==10,
                    team+" ordinary AI chooses authored AP10 reload to enable a finishing normal shot");
            }
            finally{for(int i=owned.Count-1;i>=0;i--)if(owned[i]!=null)UnityEngine.Object.DestroyImmediate(owned[i]);}
        }
    }
}
#endif
