#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Isolated R1 validation. Temporary combat settings and actor state are restored.</summary>
public static class BarrierAITests
{
    const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    static int checks;
    static void Check(bool value,string label)
    { if (!value) throw new InvalidOperationException("[BarrierAI] FAIL " + label); checks++; }
    static long Hash(SimBoardState board) => (long)typeof(AIMinimaxEngine).GetMethod("BoardHash",PrivateStatic).Invoke(null,new object[]{board});
    static float BuildScore(string method,params object[] arguments)
        => (float)typeof(AIActionEvaluator).Assembly.GetType("AIBuildEvaluator",true)
            .GetMethod(method,PrivateStatic).Invoke(null,arguments);
    static SimBoardState Board(SimUnit unit) => new SimBoardState
    {
        Units=new List<SimUnit>{unit}, MapTiles=new HashSet<Vector3Int>{Vector3Int.zero},
        EnemyBuildingCounts=new Dictionary<FacilityKind,int>(),PlayerBuildingCounts=new Dictionary<FacilityKind,int>()
    };

    public static void Run(GameSystems live)
    {
        checks=0;
        var settings=ScriptableObject.CreateInstance<BarrierCombatSettings>();
        settings.PopulateTrialValues();settings.enabledForPlay=true;
        try
        {
            using(BarrierCombatSettings.UseForValidation(settings))
            {
                ConfirmedTable();
                Simulation();
                RecoveryOptions();
                Forecasts(live);
            }
            Debug.Log("[BarrierAI] " + checks + " opt-in barrier/search/forecast checks passed");
        }
        finally { UnityEngine.Object.DestroyImmediate(settings); }
    }

    static void ConfirmedTable()
    {
        Kind[] kinds={Kind.Knight,Kind.Archer,Kind.Magic,Kind.Assassin,Kind.Scout,Kind.Priest,Kind.Guardian,
            Kind.Crossbow,Kind.Magicsniper,Kind.Bomber,Kind.WhiteMage,Kind.HolyKnight,Kind.Samurai,Kind.Ninja,
            Kind.Berserker,Kind.Navigator,Kind.HeavyWarrior,Kind.DragonKnight};
        int[] barriers={70,25,30,5,20,20,15,40,20,40,15,50,30,10,30,30,50,30};
        for(int i=0;i<kinds.Length;i++)
            Check(BarrierSystem.Initial(kinds[i])==barriers[i]&&BarrierSystem.Maximum(barriers[i],2)==barriers[i]+2,
                "confirmed initial barrier and common +2 growth "+kinds[i]);
        Check(BarrierSystem.Maximum(70,100)==268&&BarrierSystem.Maximum(5,100)==203,
            "Lv100 Knight and Assassin use the final +2 growth table");
        Check(!BarrierSystem.UsesBarrier(Kind.None,Type.Unit)&&!BarrierSystem.UsesBarrier(Kind.Boss,Type.Unit)
            &&!BarrierSystem.UsesBarrier(Kind.King,Type.Unit),"unconfigured roles receive no invented barrier");
    }

    static void Simulation()
    {
        var actor=new SimUnit{Id=1,Kind=Kind.Knight,Type=Type.Unit,Team=Team.Enemy,HP=70,MaxHP=100,
            MaxBarrier=32,Barrier=32,ATK=20,DEF=100000};
        var attacker=new SimUnit{Id=2,Kind=Kind.Archer,Type=Type.Unit,Team=Team.Player,HP=100,MaxHP=100,ATK=20};
        int damage=SimBoardState.CalcDamage(attacker,actor);actor.DEF=0;
        Check(SimBoardState.CalcDamage(attacker,actor)==damage,"ordinary search damage ignores historical DEF");
        actor.Effects.Add(new SimEffect(BuffType.Defensive,2));
        Check(SimBoardState.CalcDamage(attacker,actor)==damage,"legacy DEF buff is not silently converted into barrier resistance");
        actor.Effects.Clear();actor.Effects.Add(new SimEffect(StatusEffectType.ArmorBreak,2));
        Check(SimBoardState.CalcDamage(attacker,actor)==damage,"legacy DEF debuff is inert for normal roles until configured");
        actor.Effects.Clear();
        foreach(var kind in new[]{Kind.King,Kind.Crystal})
        {
            var target=new SimUnit{Kind=kind,Type=kind==Kind.Crystal?Type.Building:Type.Unit,
                IsPrimaryCommander=kind==Kind.King,HP=100,MaxHP=100,DEF=100000};
            int withoutLegacyDefence=SimBoardState.CalcDamage(attacker,target);target.DEF=0;
            Check(!target.UsesBarrier&&withoutLegacyDefence==SimBoardState.CalcDamage(attacker,target),
                "R1 abolishes DEF without inventing a barrier for "+kind);
            float value=SimCombatEval.GetAdjustedValue(target);target.Effects.Add(new SimEffect(BuffType.Defensive,2));
            Check(SimBoardState.CalcDamage(attacker,target)==withoutLegacyDefence&&SimCombatEval.GetAdjustedValue(target)==value,
                "historical defensive buff gives no damage or search-value bonus to "+kind);
        }
        Check(actor.ApplyDamage(31)==31&&actor.Barrier==1&&actor.HP==70,"one barrier point remains without early HP damage");
        Check(actor.BarrierDamaged&&actor.BarrierSafeTurns==0,"barrier-only loss resets recovery");
        actor.BarrierDamaged=false;actor.BarrierSafeTurns=2;actor.BarrierLastRound=12;actor.BarrierLastDamagedRound=17;
        var board=Board(actor);board.TurnCount=20;
        var copy=board.Clone();
        try
        {
            var cloned=copy.Units[0];
            Check(cloned.Barrier==1&&cloned.MaxBarrier==32&&cloned.BarrierSafeTurns==2&&!cloned.BarrierDamaged
                &&cloned.BarrierLastRound==12&&cloned.BarrierLastDamagedRound==17
                &&!cloned.BarrierHasRecovered&&!cloned.BarrierHasOwnTurnBaseline,"pooled clone preserves barrier and complete recovery clock");
            long original=Hash(board);cloned.Barrier=0;
            Check(Hash(copy)!=original&&actor.Barrier==1,"barrier value changes search hash without changing source");
            cloned.Barrier=1;cloned.BarrierDamaged=false;cloned.BarrierLastDamagedRound=17;cloned.BarrierSafeTurns=1;
            Check(Hash(copy)!=original,"recovery clock changes search hash");
            cloned.BarrierSafeTurns=2;cloned.BarrierHasRecovered=true;
            Check(Hash(copy)!=original,"one-shot recovery eligibility changes search hash");
            cloned.BarrierHasRecovered=false;cloned.BarrierHasOwnTurnBaseline=true;
            Check(Hash(copy)!=original,"first start baseline changes search hash");
            cloned.BarrierHasOwnTurnBaseline=false;cloned.BarrierLastDamagedRound=18;
            Check(Hash(copy)!=original,"recent damage boundary changes search hash");
            cloned.BarrierLastDamagedRound=17;cloned.Kind=Kind.HeavyWarrior;long heavy=Hash(copy);
            cloned.Kind=Kind.DragonKnight;
            Check(Hash(copy)!=heavy,"new role kinds retain distinct search hashes");
            board.SimulateTurnTransition(Team.Player);
            Check(actor.Barrier==8&&actor.BarrierSafeTurns==3,"third safe own end restores ceiling twenty percent at exactly seventy percent HP");
            typeof(SimBoardState).GetMethod("ProcessBarrierRecovery",PrivateStatic).Invoke(null,new object[]{actor,board.TurnCount});
            Check(actor.Barrier==8,"same own-turn recovery boundary is idempotent");
            actor.HP=69;actor.BarrierDamaged=false;actor.BarrierSafeTurns=3;
            board.SimulateTurnTransition(Team.Player);
            Check(actor.Barrier==8,"HP below seventy percent blocks barrier regeneration");
            actor.HP=70;actor.BarrierDamaged=false;actor.BarrierSafeTurns=3;actor.ApplyDamage(1);
            board.SimulateTurnTransition(Team.Player);
            Check(actor.Barrier==7&&actor.BarrierSafeTurns==0&&!actor.BarrierDamaged,"damaged owner end clears damage flag without crediting a safe turn");
            actor.ShieldTurns=3;actor.Barrier=32;
            Check(actor.ApplyDamage(1000)==0&&actor.Barrier==32&&actor.HP==70,"perfect shield is distinct from finite barrier");
            actor.ApplyHpDirectDamage(10);
            Check(actor.HP==60&&actor.Barrier==32&&actor.BarrierDamaged,"HP-direct loss preserves barrier and resets its recovery");
            actor.ShieldTurns=0;actor.Barrier=1;
            Check(actor.ApplyDamage(2)==2&&actor.Barrier==0&&actor.HP==59,"last barrier point overflows exactly one HP");
        }
        finally { SimBoardPool.ReturnBoard(copy);SimBoardPool.ReturnBoard(board); }
    }

    static void RecoveryOptions()
    {
        var settings=BarrierCombatSettings.Active;
        bool atStart=settings.recoveryAtOwnerTurnStart,onThreshold=settings.recoverOnThresholdTurn,
            repeat=settings.repeatRecoveryEveryTurn,negated=settings.fullyNegatedHitsResetRecovery;
        var rounding=settings.recoveryRounding;
        float damageBase=settings.provisionalDamageBase,attackCoefficient=settings.provisionalAttackCoefficient;
        var actor=new SimUnit{Id=1,Kind=Kind.Knight,Type=Type.Unit,Team=Team.Enemy,HP=100,MaxHP=100,MaxBarrier=25,Barrier=0};
        var board=Board(actor);board.RebuildOccupied();
        void EndOwner(){board.SimulateTurnTransition(Team.Player);board.SimulateTurnTransition(Team.Enemy);}
        try
        {
            settings.recoveryAtOwnerTurnStart=false;settings.recoverOnThresholdTurn=false;
            actor.BarrierSafeTurns=2;actor.BarrierHasOwnTurnBaseline=true;
            EndOwner();Check(actor.Barrier==0&&actor.BarrierSafeTurns==3,"optional delayed recovery does not heal at third safe end");
            EndOwner();Check(actor.Barrier==5&&actor.BarrierSafeTurns==4,"optional delayed recovery starts at fourth safe end");
            settings.repeatRecoveryEveryTurn=false;
            EndOwner();Check(actor.Barrier==5,"optional one-shot recovery does not repeat while undamaged");
            actor.ApplyDamage(1);EndOwner();
            Check(!actor.BarrierHasRecovered&&actor.BarrierSafeTurns==0,"new actual hit re-arms one-shot recovery after its reset boundary");

            settings.recoveryAtOwnerTurnStart=true;settings.recoverOnThresholdTurn=true;settings.repeatRecoveryEveryTurn=true;
            actor.Barrier=0;actor.BarrierDamaged=false;actor.BarrierSafeTurns=0;actor.BarrierHasRecovered=false;
            actor.BarrierHasOwnTurnBaseline=false;actor.BarrierLastRound=-1;
            board.SimulateTurnTransition(Team.Enemy);
            Check(actor.BarrierHasOwnTurnBaseline&&actor.BarrierSafeTurns==0&&actor.Barrier==0,
                "first own start establishes a baseline without crediting a completed safe turn");
            for(int i=0;i<3;i++){board.SimulateTurnTransition(Team.Player);board.SimulateTurnTransition(Team.Enemy);}
            Check(actor.Barrier==5&&actor.BarrierSafeTurns==3,"configured own-start recovery waits for three complete safe intervals");
            actor.Barrier=1;actor.BarrierDamaged=false;actor.BarrierSafeTurns=2;actor.BarrierHasRecovered=false;
            actor.Effects.Add(new SimEffect(StatusEffectType.Poison,2));
            board.SimulateTurnTransition(Team.Player);board.SimulateTurnTransition(Team.Enemy);
            Check(actor.Barrier==0&&actor.BarrierSafeTurns==0,"own-start damage runs before the recovery decision");
            actor.Effects.Clear();

            foreach(var mode in new[]{BarrierRecoveryRounding.Ceiling,BarrierRecoveryRounding.Floor,BarrierRecoveryRounding.Nearest})
            {
                settings.recoveryRounding=mode;
                int safe=2;bool damaged=false,recovered=false;
                int amount=BarrierSystem.AdvanceRecovery(ref safe,ref damaged,ref recovered,100,100,32);
                Check(amount==(mode==BarrierRecoveryRounding.Ceiling?7:6),"shared recovery rounding "+mode);
            }
            actor.ShieldTurns=2;actor.BarrierSafeTurns=3;actor.BarrierHasRecovered=true;actor.BarrierDamaged=false;
            settings.fullyNegatedHitsResetRecovery=false;actor.ApplyDamage(10);
            Check(actor.BarrierSafeTurns==3&&!actor.BarrierDamaged,"fully negated hit preserves recovery under the default option");
            settings.fullyNegatedHitsResetRecovery=true;actor.SimulationRound=42;actor.ApplyDamage(10);
            Check(actor.BarrierSafeTurns==0&&actor.BarrierDamaged&&!actor.BarrierHasRecovered&&actor.BarrierLastDamagedRound==42,
                "configured negated hit resets recovery and records its boundary");
            actor.ShieldTurns=0;

            settings.provisionalDamageBase=2;settings.provisionalAttackCoefficient=.25f;
            var attacker=new SimUnit{Kind=Kind.Archer,Type=Type.Unit,HP=100,MaxHP=100,ATK=20};
            Check(SimBoardState.CalcDamage(attacker,actor)==7,"search reads the adjustable provisional damage formula");
        }
        finally
        {
            settings.recoveryAtOwnerTurnStart=atStart;settings.recoverOnThresholdTurn=onThreshold;
            settings.repeatRecoveryEveryTurn=repeat;settings.fullyNegatedHitsResetRecovery=negated;settings.recoveryRounding=rounding;
            settings.provisionalDamageBase=damageBase;settings.provisionalAttackCoefficient=attackCoefficient;
            SimBoardPool.ReturnBoard(board);
        }
    }

    static void Forecasts(GameSystems live)
    {
        var owned=new List<UnityEngine.Object>();
        using(var fixture=new EconomyR2Tests.Fixture(live))
        try
        {
            var cells=fixture.AdjacentCells();
            var actor=fixture.Unit();actor.Level=1;actor.transform.position=cells[0];actor.ATK=20;
            actor.GrowthData.baseBarrier=30;actor.ResetBarrier();actor.Barrier=20;
            var first=fixture.Opponent(cells[1],20);first.GrowthData.baseBarrier=100;first.ResetBarrier();
            var second=fixture.Opponent(cells[2],20);
            var visionObject=new GameObject("Barrier AI controlled observation snapshot");owned.Add(visionObject);visionObject.SetActive(false);
            var vision=visionObject.AddComponent<VisionGenerator>();var visible=new HashSet<Vector3Int>();
            foreach(var cell in cells)visible.Add(GridHelper.ToGridXZ(cell));
            foreach(string field in new[]{"_enemyVisionBox","_enemyExplored"})
                typeof(VisionGenerator).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(vision,visible);
            // Build the observed board before any governor can warm its query caches.
            // Mutating AlivePlayerUnits after Fixture.Board() left a cached zero-threat result.
            var board=new AIBoardState(live.MoveGenerator,live.AttackGenerator,fixture.AP,fixture.Units,fixture.Crystals,
                vision,fixture.Builder,null,fixture.State,null,20,null,Team.Enemy){MapCreate=live.MapCreate,ReconThreatLevel=15};
            Check(board.AlivePlayerUnits.Count==2,"controlled vision snapshot observes both threats before queries");
            int raw=AIEvalHelpers.EstimateRawDamage(first,actor),secondRaw=AIEvalHelpers.EstimateRawDamage(second,actor);
            int expected=Mathf.Max(0,raw+secondRaw-actor.Barrier);
            int barrier=actor.Barrier;
            int cumulative=AIActionEvaluator.EstimateObservedHitLoss(actor,board,raw,HpChangeKind.NormalDamage,ref barrier)
                +AIActionEvaluator.EstimateObservedHitLoss(actor,board,secondRaw,HpChangeKind.NormalDamage,ref barrier);
            Check(cumulative==expected&&expected>0,"several hits share one remaining barrier budget");
            int counter=board.EstimateCounterDamageAt(actor.transform.position,actor);
            Check(counter==expected,"live counter query consumes barrier once across observed attackers actual="+counter
                +" expected="+expected+" raw="+raw+"/"+secondRaw+" barrier="+actor.Barrier+" visible="+board.AlivePlayerUnits.Count);
            var governor=new AIStrategicGovernor();
            float incoming=governor.IncomingDamage(board,actor,actor.transform.position,null);
            Check(Mathf.Abs(incoming-expected)<.01f,"strategic danger uses the same cumulative barrier budget actual="+incoming+" expected="+expected);
            ProtectionBudgets(actor,board,raw,secondRaw);
            first.HP=8;
            Check(!AIActionEvaluator.CanFinishSpecialTarget(actor,first,board)&&AIEvalHelpers.EstimateDamage(actor,first)==0,
                "low HP behind barrier is not an early ordinary kill");
            first.Barrier=0;
            Check(AIActionEvaluator.CanFinishSpecialTarget(actor,first,board),"same low HP is a finishing attack after barrier depletion");
            var settings=BarrierCombatSettings.Active;
            float oldBase=settings.provisionalDamageBase,oldCoefficient=settings.provisionalAttackCoefficient;
            try
            {
                settings.provisionalDamageBase=2;settings.provisionalAttackCoefficient=.25f;
                Check(AIEvalHelpers.EstimateRawDamage(actor,first)==7,"live AI reads the adjustable provisional damage formula");
            }
            finally { settings.provisionalDamageBase=oldBase;settings.provisionalAttackCoefficient=oldCoefficient; }

            actor.kind=Kind.DragonKnight;actor.GrowthData.kind=Kind.DragonKnight;actor.ATK=100;
            var definition=ScriptableObject.CreateInstance<SpecialSkillDefinition>();owned.Add(definition);
            definition.enabledForPlay=true;definition.ability=UnitSpecialSkill.DragonKnightPierce;definition.apCost=2;definition.cooldown=0;
            actor.GrowthData.specialSkill=definition;actor.VisionCell.Add(first.GridPosition);
            first.HP=40;first.ResetBarrier();
            board.Refresh();board.SpecialSystems=new GameSystems{APSystem=fixture.AP,FactionState=fixture.State};
            var action=new AIAction{ActionType=AIActionType.SkillUse,Unit=actor,TargetUnit=first,TargetPos=first.transform.position,
                APCost=2,SpecialCommand=new SpecialSkillCommand(actor,first)};
            actor.Special.dragonKnightGauge=1;
            Check(!SpecialSkillRuntime.IsLegalTarget(actor,first,out _)&&AIActionEvaluator.CalcSpecialSkillScore(action,board)<0,
                "one Dragon Knight gauge cannot enable or reward the dedicated skill");
            actor.Special.dragonKnightGauge=2;
            Check(SpecialSkillRuntime.IsLegalTarget(actor,first,out _)&&SpecialSkillRuntime.CanUse(actor,board.SpecialSystems,out _),
                "two gauge enables the legal dedicated skill");
            int oldHP=first.HP,oldBarrier=first.Barrier;
            float score=AIActionEvaluator.CalcSpecialSkillScore(action,board);
            int dedicated=SpecialSkillRuntime.PreviewDedicatedSkillDamage(actor,first,AIActionEvaluator.EstimateSpecialAwareAttack(actor,first,board));
            Check(score>0&&SpecialSkillRuntime.PreviewHpDamage(first,dedicated,HpChangeKind.BarrierBypassDamage)==oldHP
                &&AIActionEvaluator.EstimateEffectiveSpecialDamage(actor,first,board,out _)==0,
                "only dedicated Dragon Knight skill can finish through the intact barrier");
            Check(first.HP==oldHP&&first.Barrier==oldBarrier&&actor.Special.dragonKnightGauge==2,
                "skill scoring preserves live HP, barrier and gauge");
            OffensiveIntegration(actor,first,definition,board);
            MovementReturns(actor,first,board,vision,visible,owned,cells);
            first.ShieldTurns=1;
            Check(SpecialSkillRuntime.PreviewHpDamage(first,dedicated,HpChangeKind.BarrierBypassDamage)==0,
                "Dragon Knight barrier bypass still respects perfect shield");
            first.ShieldTurns=0;

            var heavy=ScriptableObject.CreateInstance<UnitData>();owned.Add(heavy);heavy.kind=Kind.HeavyWarrior;
            heavy.baseHP=100;heavy.baseATK=20;heavy.costIron=37;
            fixture.Units.UnitDataMap[Kind.HeavyWarrior]=heavy;fixture.Resources.Iron=0;
            var planner=new AIInvestmentPlanner();planner.Prepare(board,new AIPlan{RequiredUnits=new[]{Kind.HeavyWarrior}});
            Check(Mathf.Abs(planner.NeededIron-37)<.01f,"investment planner uses actual new-role recipe rather than absent static entry");
            HeavyRecipient(fixture,owned);
        }
        finally
        {
            for(int i=owned.Count-1;i>=0;i--)if(owned[i]!=null)UnityEngine.Object.DestroyImmediate(owned[i]);
            SpecialSkillRuntime.InvalidateRules();
        }
    }

    static void ProtectionBudgets(Status actor,AIBoardState board,int firstRaw,int secondRaw)
    {
        var state=SpecialSkillRuntime.State(actor);
        bool oldNull=state.damageNullUnused,oldDouble=state.nextDamageDouble,oldLock=state.holyKnightRecoveryLock;
        int oldNullTurns=state.damageNullTurns,oldHolyTurns=state.holyKnightTurns,oldBarrier=actor.Barrier,oldShield=actor.ShieldTurns;
        int hp=actor.HP;
        try
        {
            actor.ShieldTurns=0;actor.Barrier=20;state.damageNullUnused=true;state.damageNullTurns=2;
            state.nextDamageDouble=true;state.holyKnightTurns=0;state.holyKnightRecoveryLock=false;
            var budget=AIObservedDamageBudget.Capture(actor);
            Check(AIActionEvaluator.EstimateObservedHitLoss(actor,board,0,HpChangeKind.NormalDamage,ref budget)==0,
                "zero packet does not consume a nullification");
            Check(AIActionEvaluator.EstimateObservedHitLoss(actor,board,10,HpChangeKind.NormalDamage,ref budget)==0&&budget.Barrier==20,
                "first observed packet uses nullification before vulnerability and finite barrier");
            Check(AIActionEvaluator.EstimateObservedHitLoss(actor,board,10,HpChangeKind.IgnoreDefenceDamage,ref budget)==0&&budget.Barrier==0,
                "second packet consumes one vulnerability and the finite barrier");
            Check(AIActionEvaluator.EstimateObservedHitLoss(actor,board,10,HpChangeKind.NormalDamage,ref budget)==10,
                "third packet cannot reuse consumed vulnerability or nullification");
            Check(actor.HP==hp&&actor.Barrier==20&&state.damageNullUnused&&state.nextDamageDouble,
                "forecast consumes only its local budget, never live combat state");

            budget=AIObservedDamageBudget.Capture(actor);
            Check(AIActionEvaluator.EstimateObservedHitLoss(actor,board,3,HpChangeKind.DirectHpReduction,ref budget)==3&&budget.Barrier==20,
                "direct HP costs bypass protection without spending it");
            Check(AIActionEvaluator.EstimateObservedHitLoss(actor,board,10,HpChangeKind.BarrierBypassDamage,ref budget)==0&&budget.Barrier==20,
                "Dragon Knight bypass is still blocked by one nullification");
            Check(AIActionEvaluator.EstimateObservedHitLoss(actor,board,10,HpChangeKind.BarrierBypassDamage,ref budget)==Mathf.Min(hp,20)&&budget.Barrier==20,
                "next bypass packet consumes vulnerability while preserving finite barrier");

            actor.ShieldTurns=1;budget=AIObservedDamageBudget.Capture(actor);
            Check(AIActionEvaluator.EstimateObservedHitLoss(actor,board,100,HpChangeKind.NormalDamage,ref budget)==0&&budget.Barrier==20,
                "persistent perfect shield protects the forecast");
            actor.ShieldTurns=0;
            Check(AIActionEvaluator.EstimateObservedHitLoss(actor,board,10,HpChangeKind.NormalDamage,ref budget)==0&&budget.Barrier==20,
                "perfect shield does not prematurely consume one-use nullification");

            actor.Barrier=0;state.nextDamageDouble=false;
            board.Refresh();
            int counter=board.EstimateCounterDamageAt(actor.transform.position,actor);
            float incoming=new AIStrategicGovernor().IncomingDamage(board,actor,actor.transform.position,null);
            Check(counter==secondRaw&&Mathf.Abs(incoming-secondRaw)<.01f,
                "counter and governor cannot reuse one nullification for two observed attackers");

            actor.Barrier=20;state.damageNullUnused=false;state.nextDamageDouble=true;
            board.Refresh();
            int expected=Mathf.Max(0,firstRaw*2+secondRaw-20);
            counter=board.EstimateCounterDamageAt(actor.transform.position,actor);
            incoming=new AIStrategicGovernor().IncomingDamage(board,actor,actor.transform.position,null);
            Check(counter==expected&&Mathf.Abs(incoming-expected)<.01f,
                "counter and governor multiply only the first actual damage packet");
            actor.Barrier=20;state.nextDamageDouble=false;
            Check(AIActionEvaluator.ObservedSniperRecoilLoss(actor,board,null,0,false)==0,
                "ordinary Magic Sniper recoil spends finite barrier before HP in the AI model");
            actor.Barrier=0;state.damageNullUnused=true;state.damageNullTurns=2;
            Check(AIActionEvaluator.ObservedSniperRecoilLoss(actor,board,null,0,false)==0,
                "ordinary Magic Sniper recoil respects one-use nullification in the AI model");
        }
        finally
        {
            actor.Barrier=oldBarrier;actor.ShieldTurns=oldShield;state.damageNullUnused=oldNull;state.nextDamageDouble=oldDouble;
            state.damageNullTurns=oldNullTurns;state.holyKnightTurns=oldHolyTurns;state.holyKnightRecoveryLock=oldLock;
            board.Refresh();
        }
    }

    static void HeavyRecipient(EconomyR2Tests.Fixture fixture,List<UnityEngine.Object> owned)
    {
        var heavy=fixture.Unit();heavy.kind=Kind.HeavyWarrior;heavy.Level=1;heavy.ATK=20;
        var rule=ScriptableObject.CreateInstance<SpecialSkillDefinition>();owned.Add(rule);
        rule.ability=UnitSpecialSkill.HeavyWarriorBreak;rule.enabledForPlay=true;rule.apCost=2;rule.cooldown=0;
        rule.heavyBarrierMultiplier=2;heavy.GrowthData.specialSkill=rule;
        var intended=fixture.Unit();intended.Level=1;intended.GrowthData.baseBarrier=100;intended.ResetBarrier();intended.Barrier=0;
        var recipient=fixture.Unit();recipient.Level=1;recipient.GrowthData.baseBarrier=100;recipient.ResetBarrier();
        SpecialSkillRuntime.State(intended).guardianPartnerId=recipient.ReflectionLifeId;
        SpecialSkillRuntime.State(recipient).guardianPartnerId=intended.ReflectionLifeId;
        var board=fixture.Board();
        var settings=BarrierCombatSettings.Active;float oldMultiplier=settings.heavyNormalBarrierMultiplier;
        try
        {
            settings.heavyNormalBarrierMultiplier=2;
            Check(AIActionEvaluator.EstimateObservedNormalPacket(heavy,intended,board,20)==40,
                "heavy normal bonus uses observed guardian recipient barrier rather than intended target");
            int raw=AIActionEvaluator.EstimateSpecialAwareAttack(heavy,intended,board);
            int dedicated=(int)typeof(AIActionEvaluator).GetMethod("EstimateObservedDedicatedPacket",PrivateStatic)
                .Invoke(null,new object[]{heavy,intended,board});
            Check(dedicated==BarrierSystem.AmplifyBarrierPortion(raw,recipient.Barrier,rule.heavyBarrierMultiplier),
                "heavy dedicated bonus uses the same observed recipient and shared barrier amplification");
            recipient.Barrier=0;intended.Barrier=100;
            Check(AIActionEvaluator.EstimateObservedNormalPacket(heavy,intended,board,20)==20,
                "intended target barrier cannot inflate a hit redirected to an empty barrier");
            intended.Barrier=0;recipient.HP=10;recipient.Barrier=5;
            SpecialSkillRuntime.State(intended).nextDamageDouble=true;
            var budget=AIObservedDamageBudget.Capture(intended);
            Check(AIActionEvaluator.EstimateObservedHitLoss(intended,board,20,HpChangeKind.NormalDamage,ref budget)==0
                &&AIActionEvaluator.EstimateObservedHitLoss(intended,board,20,HpChangeKind.NormalDamage,ref budget)==40,
                "observed partner death ends local interception while preserving intended-target vulnerability");
            Check(recipient.HP==10&&recipient.Barrier==5&&intended.Barrier==0&&intended.Special.nextDamageDouble
                &&recipient.Special.guardianPartnerId==intended.ReflectionLifeId,
                "life-link forecast never kills or unlinks the live actors");
            intended.Special.nextDamageDouble=false;recipient.Special.guardianPartnerId=null;
            budget=AIObservedDamageBudget.Capture(intended);
            Check(AIActionEvaluator.EstimateObservedHitLoss(intended,board,20,HpChangeKind.NormalDamage,ref budget)==20,
                "nonreciprocal link does not falsely protect its intended target");
            recipient.Special.guardianPartnerId=intended.ReflectionLifeId;
            int intendedHP=intended.HP;intended.HP=5;recipient.HP=50;recipient.Barrier=100;recipient.ShieldTurns=2;
            budget=AIObservedDamageBudget.Capture(intended);
            Check(AIActionEvaluator.EstimateObservedHitLoss(intended,board,10,HpChangeKind.NormalDamage,ref budget)==0
                &&AIActionEvaluator.EstimateObservedHitLoss(intended,board,10,HpChangeKind.NormalDamage,ref budget)==10,
                "lethal intended packet sacrifices partner before its perfect shield and ends interception");
            intended.HP=intendedHP;recipient.ShieldTurns=0;
            recipient.team=Team.Player;board.Refresh();
            int hiddenForecast=AIActionEvaluator.EstimateObservedNormalPacket(heavy,intended,board,20);
            recipient.Barrier=100;
            Check(AIActionEvaluator.EstimateObservedNormalPacket(heavy,intended,board,20)==hiddenForecast,
                "hidden guardian barrier changes do not leak into heavy attack forecasts");
            budget=AIObservedDamageBudget.Capture(intended);
            int hiddenLoss=AIActionEvaluator.EstimateObservedHitLoss(intended,board,20,HpChangeKind.NormalDamage,ref budget);
            recipient.HP=1;recipient.Barrier=0;recipient.Special.nextDamageDouble=true;
            budget=AIObservedDamageBudget.Capture(intended);
            Check(hiddenLoss==20&&AIActionEvaluator.EstimateObservedHitLoss(intended,board,20,HpChangeKind.NormalDamage,ref budget)==hiddenLoss,
                "hidden life-link partner protection and HP cannot change observed danger forecasts");
            int flatBefore=board.ObservedAttackFlatAdjustment(intended);
            recipient.Special.paladinCycles.Add(new PaladinAttackCycle{targetId=intended.ReflectionLifeId,stolenAttack=5});
            board.Refresh();
            Check(board.ObservedAttackFlatAdjustment(intended)==flatBefore,
                "hidden paladin cycle cannot leak into observed attack adjustment");
            recipient.team=Team.Enemy;board.Refresh();
            Check(board.ObservedAttackFlatAdjustment(intended)==flatBefore-5,
                "observed paladin cycle applies its known stolen-attack adjustment");

            rule.ability=UnitSpecialSkill.NinjaSwap;rule.ninjaChargeCurve=new AnimationCurve(new Keyframe(0,1),new Keyframe(5,2));
            heavy.Special.ninjaCharge=2;
            intended.VisionCell.Clear();int ninjaBefore=AIActionEvaluator.EstimateSpecialAwareAttack(heavy,intended,board);
            intended.VisionCell.Add(heavy.GridPosition);
            Check(AIActionEvaluator.EstimateSpecialAwareAttack(heavy,intended,board)==ninjaBefore,
                "Ninja attack forecast uses a conservative bound without querying opponent vision masks");
        }
        finally { settings.heavyNormalBarrierMultiplier=oldMultiplier; }
    }

    static void OffensiveIntegration(Status actor,Status target,SpecialSkillDefinition definition,AIBoardState board)
    {
        var ordinary=new AIAction{ActionType=AIActionType.SkillUse,Unit=actor,TargetUnit=target,Skill=new SkillData{Multiplier=1}};
        var dedicated=new AIAction{ActionType=AIActionType.SkillUse,Unit=actor,TargetUnit=target,
            SpecialCommand=new SpecialSkillCommand(actor,target)};
        var support=new AIAction{ActionType=AIActionType.SkillUse,Unit=actor,TargetUnit=actor,
            SpecialCommand=new SpecialSkillCommand(actor,actor)};
        var oldAbility=definition.ability;
        foreach(var strategy in new[]{TurnStrategy.Assault,TurnStrategy.ScoutSearch,TurnStrategy.ContactEngage})
        {
            ordinary.Score=0;AIStrategyBonus.Apply(new List<AIAction>{ordinary},strategy,board);
            foreach(var ability in new[]{UnitSpecialSkill.DragonKnightPierce,UnitSpecialSkill.HeavyWarriorBreak})
            {
                definition.ability=ability;dedicated.Score=0;
                AIStrategyBonus.Apply(new List<AIAction>{dedicated},strategy,board);
                Check(dedicated.Score==ordinary.Score&&dedicated.Score>0,"dedicated offensive strategy parity "+ability+"/"+strategy);
            }
            definition.ability=UnitSpecialSkill.WhiteMageHeal;support.Score=0;
            AIStrategyBonus.Apply(new List<AIAction>{support},strategy,board);
            Check(support.Score==0,"White Mage support receives no offensive strategy bonus "+strategy);
        }
        definition.ability=oldAbility;
        var recruit=new AIAction{ActionType=AIActionType.Summon,SummonKind=Kind.Knight,TargetPos=actor.transform.position};
        int oldBarrier=target.Barrier;target.Barrier=0;
        float baseline=BuildScore("CalcSummonBaseScore",recruit,board);
        foreach(var kind in new[]{Kind.HeavyWarrior,Kind.DragonKnight})
        {
            recruit.SummonKind=kind;
            Check(BuildScore("CalcSummonBaseScore",recruit,board)==baseline,"new melee recruitment inherits Knight baseline "+kind);
        }
        target.Barrier=oldBarrier;actor.kind=Kind.Knight;
        float demand=BuildScore("ObservedBarrierRecruitmentBonus",Kind.HeavyWarrior,board);
        Check(demand>0&&demand<=12,"observed barrier recruitment preference is positive and bounded");
        actor.kind=Kind.HeavyWarrior;
        Check(BuildScore("ObservedBarrierRecruitmentBonus",Kind.DragonKnight,board)<demand,
            "existing specialists count toward the shared infantry role instead of an empty role");
        actor.kind=Kind.DragonKnight;
        var visible=new List<Status>(board.AlivePlayerUnits);board.AlivePlayerUnits.Clear();
        Check(BuildScore("ObservedBarrierRecruitmentBonus",Kind.HeavyWarrior,board)==0,
            "hidden opponents cannot influence barrier recruitment preference");
        board.AlivePlayerUnits.AddRange(visible);
    }

    static void MovementReturns(Status actor,Status target,AIBoardState board,VisionGenerator vision,
        HashSet<Vector3Int> visible,List<UnityEngine.Object> owned,Vector3[] cells)
    {
        var history=new AITurnMovementHistory();
        var move=new AIAction{ActionType=AIActionType.Move,Unit=actor,TargetPos=cells[0],APCost=1};
        history.Record(actor,cells[0],cells[1],false);
        Check(history.Count==0&&!history.ShouldReject(move,null),"failed moves do not create a return prohibition");
        history.Record(actor,cells[0],cells[1],true);
        Check(history.ShouldReject(move,null),"same-owner-turn A to B to A ordinary return is rejected");
        move.TargetPos=cells[2];
        Check(!history.ShouldReject(move,null),"A to B to C remains legal");
        move.TargetPos=cells[0];move.ActionType=AIActionType.Retreat;
        Check(!history.ShouldReject(move,null),"explicit retreat may return to a departed cell");
        move.ActionType=AIActionType.Move;history.Clear();
        Check(!history.ShouldReject(move,null),"next owner turn starts without a return prohibition");
        for(int i=0;i<80;i++)history.Record(actor,cells[0],cells[1],true);
        Check(history.Count==64,"owner-turn departure memory has a fixed bound");
        history.Clear();

        var oldOrigin=actor.transform.position;var oldTarget=target.transform.position;
        var oldDirection=actor.direction;var oldProfile=actor.GrowthData.actionProfile;
        var profile=ScriptableObject.CreateInstance<BoardActionProfile>();owned.Add(profile);
        profile.attack.useCustom=true;profile.attack.SetCell(4,3,true);
        try
        {
            actor.transform.position=cells[0];actor.direction=Direction.N;actor.GrowthData.actionProfile=profile;
            target.transform.position=cells[2];
            var center=GridHelper.ToGridXZ(cells[1]);
            for(int x=-2;x<=2;x++)for(int z=-2;z<=2;z++)visible.Add(center+new Vector3Int(x,0,z));
            board.Refresh();
            history.Record(actor,cells[1],cells[0],true);move.TargetPos=cells[1];
            bool opens=(bool)typeof(AITurnMovementHistory).GetMethod("OpensObservedAttack",PrivateStatic)
                .Invoke(null,new object[]{move,board});
            Check(opens&&!history.ShouldReject(move,board),"return opening a visible legal attack remains available");
        }
        finally
        {
            actor.transform.position=oldOrigin;actor.direction=oldDirection;actor.GrowthData.actionProfile=oldProfile;
            target.transform.position=oldTarget;board.Refresh();
        }
    }
}
#endif
