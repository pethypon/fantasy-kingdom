#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Regression for ranged AP waste against a territory-protected boss, for both faction perspectives.</summary>
public static class ProtectedTargetAITests
{
    static int checks;
    static void Check(bool value,string label)
    { if(!value) throw new InvalidOperationException("[ProtectedTargetAI] "+label);checks++; }

    public static void Run(GameSystems live)
    {
        checks=0;
        foreach(var team in new[]{Team.Player,Team.Enemy}) Perspective(live,team);
        Debug.Log("[ProtectedTargetAI] "+checks+" observation/protection/candidate checks passed");
    }

    static void Perspective(GameSystems live,Team team)
    {
        using(var f=new EconomyR2Tests.Fixture(live,team))
        {
            var owned=new List<UnityEngine.Object>();
            Status registeredHidden=null;
            try
            {
                var cells=f.AdjacentCells();
                var actor=f.Unit();actor.transform.position=cells[0];actor.ATK=20;
                var profile=ScriptableObject.CreateInstance<BoardActionProfile>();owned.Add(profile);
                profile.attack.useCustom=true;profile.attack.directionIndependent=true;profile.attack.SetCell(5,3,true);
                actor.GrowthData.actionProfile=profile;
                var boss=f.Opponent(cells[2],10);boss.isWildBoss=true;boss.kind=Kind.Boss;
                boss.wildBossTerritoryCenter=boss.GridPosition;boss.wildBossTerritoryRadius=0;
                var hidden=f.Opponent(cells[2]+Vector3.forward*20,10);
                UnitRegistry.Instance.Register(hidden);registeredHidden=hidden;
                var go=new GameObject("Protected target controlled vision");owned.Add(go);go.SetActive(false);
                var vision=go.AddComponent<VisionGenerator>();
                var visible=new HashSet<Vector3Int>{actor.GridPosition,boss.GridPosition};
                string prefix=team==Team.Player?"_player":"_enemy";
                foreach(string field in new[]{prefix+"VisionBox",prefix+"Explored"})
                    typeof(VisionGenerator).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(vision,visible);
                var board=new AIBoardState(live.MoveGenerator,live.AttackGenerator,f.AP,f.Units,f.Crystals,
                    vision,f.Builder,null,f.State,null,20,null,team){MapCreate=live.MapCreate,ReconThreatLevel=15};
                Check(board.IsSpecialActorObserved(boss)&&!board.IsSpecialActorObserved(hidden),team+" controlled observations");
                int ap=f.State.GetAP(team),hp=boss.HP;
                var attack=new AIAction{ActionType=AIActionType.Attack,Unit=actor,TargetUnit=boss,TargetPos=boss.transform.position,APCost=3};
                Check(AIActionEvaluator.IsIneffectiveProtectedAttack(attack,board),team+" protected boss has no damaging attack utility");
                Check(!board.GetAttackTargets(actor).Contains(boss),team+" protected boss excluded from attack query");
                var candidates=new List<AIAction>();AIActionGenerator.GenerateAttackCandidates(actor,board,candidates);
                Check(candidates.Count==0,team+" protected boss generates no ordinary attack");
                Check(AIActionEvaluator.CalcBuildScorePublic(attack,f.Personality,board,null)<0,team+" stale protected attack has negative score");
                var budget=AIObservedDamageBudget.Capture(boss);
                Check(AIActionEvaluator.EstimateObservedHitLoss(boss,board,50,HpChangeKind.NormalDamage,ref budget)==0,
                    team+" observed forecast protects boss");
                var oldCenter=boss.wildBossTerritoryCenter;
                boss.wildBossTerritoryCenter=new Vector3Int(99999,0,99999);
                var damageSkill=new SkillData{Multiplier=2,Target=SkillTarget.EnemySingle};
                Check(DamageCalculator.CalcNormal(actor,boss)==0&&DamageCalculator.CalcSkill(actor,boss,damageSkill)==0
                    &&SpecialSkillRuntime.PreviewDamage(boss,50)==0,
                    team+" actual protected normal/skill damage and UI preview agree at zero");
                hidden.transform.position=boss.wildBossTerritoryCenter;
                Check(DamageCalculator.CalcNormal(actor,boss)>0&&SpecialSkillRuntime.PreviewDamage(boss,50)>0
                    &&AIActionEvaluator.EstimateSpecialAwareAttack(actor,boss,board)==0,
                    team+" actual hidden intruder unlocks protection without leaking through observed forecast");
                boss.wildBossTerritoryCenter=oldCenter;
                // Do not refresh observations: a hidden actor changing location must not alter this faction's knowledge.
                hidden.transform.position=boss.transform.position;
                var secondBudget=AIObservedDamageBudget.Capture(boss);
                Check(AIActionEvaluator.IsIneffectiveProtectedAttack(attack,board)
                    &&AIActionEvaluator.EstimateSpecialAwareAttack(actor,boss,board)==0
                    &&AIActionEvaluator.EstimateObservedHitLoss(boss,board,50,HpChangeKind.NormalDamage,ref secondBudget)==0,
                    team+" hidden intruder cannot leak through scoring or incoming forecast");
                var pure=new AIAction{ActionType=AIActionType.SkillUse,Unit=actor,TargetUnit=boss,
                    Skill=new SkillData{Multiplier=2,Target=SkillTarget.EnemySingle}};
                Check(AIActionEvaluator.IsIneffectiveProtectedAttack(pure,board),team+" pure damage skill is rejected");
                pure.Skill.InflictDebuff=StatusEffectType.Stun;pure.Skill.DebuffChance=1;
                Check(!AIActionEvaluator.IsIneffectiveProtectedAttack(pure,board),team+" useful status skill is retained");
                var definition=ScriptableObject.CreateInstance<SpecialSkillDefinition>();owned.Add(definition);
                definition.enabledForPlay=true;actor.GrowthData.specialSkill=definition;
                foreach(var ability in new[]{UnitSpecialSkill.HeavyWarriorBreak,UnitSpecialSkill.DragonKnightPierce})
                {
                    definition.ability=ability;
                    var dedicated=new AIAction{ActionType=AIActionType.SkillUse,Unit=actor,TargetUnit=boss,
                        SpecialCommand=new SpecialSkillCommand(actor,boss)};
                    Check(AIActionEvaluator.IsIneffectiveProtectedAttack(dedicated,board),
                        team+" pure dedicated damage cannot regain priority against protection: "+ability);
                }
                actor.GrowthData.specialSkill=null;
                var snapshot=SimBoardState.CreateFromGame(board,live.MoveGenerator,f.Units,f.Crystals,f.AP);
                try
                {
                    SimUnit captured=null;foreach(var unit in snapshot.Units)if(unit.IsWildBoss)captured=unit;
                    Check(captured!=null&&captured.WildBossProtected&&captured.ApplyDamage(50)==0,
                        team+" search captures conservative protection without hidden registry");
                    var clone=captured.Clone();try{Check(clone.WildBossProtected,team+" pooled clone retains protection");}
                    finally{SimBoardPool.ReturnUnit(clone);}
                }
                finally{SimBoardPool.ReturnBoard(snapshot);}
                actor.transform.position=boss.transform.position;
                Check(AIActionEvaluator.HasObservedTerritoryIntruder(board,boss)
                    &&!AIActionEvaluator.IsIneffectiveProtectedAttack(attack,board),team+" own territory entry legally unlocks attacks");
                actor.transform.position=cells[0];boss.isWildBoss=false;boss.kind=Kind.Knight;
                boss.GrowthData.baseBarrier=50;boss.ResetBarrier();
                Check(!AIActionEvaluator.IsIneffectiveProtectedAttack(attack,board)
                    &&board.GetAttackTargets(actor).Contains(boss),team+" finite barrier is not complete protection");
                boss.Special.damageNullUnused=true;boss.Special.damageNullTurns=1;
                Check(!AIActionEvaluator.IsIneffectiveProtectedAttack(attack,board),team+" consuming one-use null remains useful");
                boss.ShieldTurns=1;
                Check(AIActionEvaluator.IsIneffectiveProtectedAttack(attack,board),team+" persistent perfect shield is complete protection");
                Check(f.State.GetAP(team)==ap&&boss.HP==hp,team+" all prediction leaves AP and HP unchanged");
            }
            finally
            {
                if(registeredHidden!=null)UnitRegistry.Instance.Unregister(registeredHidden);
                for(int i=owned.Count-1;i>=0;i--)if(owned[i]!=null)UnityEngine.Object.DestroyImmediate(owned[i]);
            }
        }
    }
}
#endif
