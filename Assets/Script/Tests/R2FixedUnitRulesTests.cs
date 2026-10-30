#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;

public static class R2FixedUnitRulesTests
{
    static void Check(string id, bool ok)
    { if (!ok) throw new InvalidOperationException("[R2Fixed] FAIL " + id); Debug.Log("[R2Fixed] PASS " + id); }
    public static void Run()
    {
        var data = ScriptableObject.CreateInstance<UnitData>();
        var obj = new GameObject("R2 fixed unit contracts");
        using var animation = BoardActorAnimation.SuppressEffects();
        try
        {
            data.kind = Kind.Knight; data.baseHP = 100; data.baseATK = 20; data.baseDEF = 10;
            data.hpGrowth = float.NaN; data.atkGrowth = float.PositiveInfinity;
            Check("A01", data.FixedMaxHP == 175 && data.FixedATK == 35 && data.FixedDEF == 18);
            Check("A02", FixedUnitRules.ScaleBaseStat(0) == 0);
            Check("A03", FixedUnitRules.ScaleBaseStat(1) == 2 && FixedUnitRules.ScaleBaseStat(2) == 4 && FixedUnitRules.ScaleBaseStat(3) == 5);
            Check("A04", FixedUnitRules.ScaleBaseStat(int.MaxValue) == int.MaxValue && FixedUnitRules.ScaleBaseStat(-1) == 0);
            var actor = obj.AddComponent<Status>(); actor.kind = Kind.Knight; actor.type = Type.Unit;
            data.ApplyToStatus(actor, 100);
            Check("A05", actor.HP == 175 && actor.MaxHP == 175 && actor.Level == 1 && actor.Experience == 0);
            data.ApplyToStatus(actor, 100); StatusBalanceRefresh.ApplyLatestDefinition(actor);
            Check("A06", actor.MaxHP == 175 && actor.ATK == 35 && actor.DEF == 18);
            actor.ATK+=7;actor.MaxHP+=12;actor.HP=100;
            actor.ActiveEffects.Add(new ActiveEffect(StatusEffectType.Weaken,3));
            data.baseATK=24;
            StatusBalanceRefresh.ApplyLatestDefinition(actor);StatusBalanceRefresh.ApplyLatestDefinition(actor);
            Check("A07",actor.ATK==49&&actor.MaxHP==187&&actor.HP==100);
            Check("A08",StatusEffectSystem.HasDebuff(actor,StatusEffectType.Weaken)
                &&Math.Abs(StatusEffectSystem.GetATKModifier(actor)-(1-GameConstants.WeakenATKReduction))<.001);
            actor.ActiveEffects.Clear();data.baseATK=20;data.ApplyToStatus(actor,1);
            Check("A10", FixedUnitRules.BaseStat(Kind.King,800) == 800 && FixedUnitRules.BaseStat(Kind.Boss,20000) == 20000
                && FixedUnitRules.BaseStat(Kind.Crystal,5000) == 5000);
            actor.GainExperience(int.MaxValue); actor.GainExperienceFromDamage(int.MaxValue,100);
            Check("B04", actor.Level == 1 && actor.Experience == 0 && actor.MaxHP == 175);
            actor.UpkeepUnpaidTurns = 100;
            Check("UP08", actor.UpkeepPenaltyATKDEF == 0);
            data.useAuthoredUpkeep = true; data.upkeepIron = 1; data.upkeepBread = 2;
            data.upkeepMagic = 3; data.upkeepMagicScales = false;
            var extra = GameAuthoringRules.Active?.unitExtraUpkeep ?? default;
            var upkeep = data.GetFixedUpkeep();
            Check("C01", upkeep.Iron == FixedUnitRules.MultiplyUpkeep(1,extra.Iron) && upkeep.Bread == FixedUnitRules.MultiplyUpkeep(2,extra.Bread));
            Check("C03", upkeep.MagicOre == FixedUnitRules.MultiplyUpkeep(3,extra.MagicOre));
            Check("C05", JsonUtility.ToJson(data.GetUpkeep(1)) == JsonUtility.ToJson(data.GetUpkeep(100)));
            Check("C04", FixedUnitRules.MultiplyUpkeep(2,3) == 25 && FixedUnitRules.MultiplyUpkeep(int.MaxValue,int.MaxValue) == int.MaxValue);
            AuthoredExtraUpkeep(data);
            data.upkeepExempt = true; Check("C06-exempt", data.GetFixedUpkeep().Iron == 0);
            data.upkeepExempt = false; data.kind = Kind.King; Check("C06-king", data.GetFixedUpkeep().Iron == 0);
            data.kind = Kind.Boss; Check("C06-boss", data.GetFixedUpkeep().Bread == 0);
            data.kind = Kind.Knight; data.costWood = 7; data.costAP = 3; data.ApplyToStatus(actor, 20);
            Check("C08", data.costWood == 7 && data.costAP == 3);
            var sim = new SimRecruitmentDefinition(data);
            Check("D01", sim.HP == actor.MaxHP && sim.ATK == actor.ATK && sim.DEF == actor.DEF);
            Check("D02", UnitData.CalcGrowthPerLevel(100, 100) == 0 && actor.Level == 1);
            Check("I03", data.baseHP == 100 && data.baseATK == 20 && data.baseDEF == 10 && data.IsValidForAuthoring);
            data.kind=Kind.Engineer;actor.kind=Kind.Engineer;data.baseBarrier=-1;
            Check("engineer-barrier-requires-configuration",!BarrierSystem.UsesBarrier(actor));
            data.baseBarrier=6;data.ApplyToStatus(actor,100);
            Check("engineer-authored-barrier-live-sim",actor.MaxBarrier==6&&new SimRecruitmentDefinition(data).Barrier==6
                &&new SimUnit{Kind=Kind.Engineer,Type=Type.Unit,MaxBarrier=6,EngineerBarrierConfigured=true}.UsesBarrier);
            var create=typeof(SimBoardState).GetMethod("CreateSimUnitFromKind",BindingFlags.Instance|BindingFlags.NonPublic);
            var simulation=new SimBoardState();
            var fallback=(SimUnit)create.Invoke(simulation,new object[]{Kind.Knight,Team.Enemy,Vector3Int.zero,1});
            Check("A09",fallback.MaxHP==FixedUnitRules.BaseStat(Kind.Knight,UnitStaticData.Table[Kind.Knight].BaseHP));
            simulation.RecruitmentDefinitions=new System.Collections.Generic.Dictionary<Kind,SimRecruitmentDefinition>
                {{Kind.Engineer,new SimRecruitmentDefinition(data)}};
            var summoned=(SimUnit)create.Invoke(simulation,new object[]{Kind.Engineer,Team.Enemy,Vector3Int.zero,2});
            Check("D08",summoned.MaxHP==actor.MaxHP&&summoned.MaxBarrier==6&&summoned.UsesBarrier&&summoned.Clone().EngineerBarrierConfigured);
        }
        finally { UnityEngine.Object.DestroyImmediate(obj); UnityEngine.Object.DestroyImmediate(data); }
        Debug.Log("[R2Fixed] COMPLETE");
    }
    static void AuthoredExtraUpkeep(UnitData data)
    {
        var flags=BindingFlags.Static|BindingFlags.NonPublic;
        var field=typeof(GameAuthoringRules).GetField("loaded",flags);
        var ready=typeof(GameAuthoringRules).GetField("didLoad",flags);
        var previous=field.GetValue(null);var wasReady=ready.GetValue(null);
        var rules=ScriptableObject.CreateInstance<GameAuthoringRules>();rules.applyRules=true;
        try
        {
            rules.unitExtraUpkeep=new FacilityData.ProductionBundle{Wood=2,Stone=3,Iron=4,MagicOre=5,Water=6,Bread=7,Wheat=8,Citizen=9};
            field.SetValue(null,rules);ready.SetValue(null,true);
            data.upkeepWood=1;data.upkeepStone=2;data.upkeepWater=2;
            var actual=data.GetFixedUpkeep();
            Check("C02",actual.Wood==15&&actual.Stone==25&&actual.Iron==25&&actual.MagicOre==40
                &&actual.Water==40&&actual.Bread==45&&actual.Wheat==40&&actual.Citizen==45);
            Check("C04-global",actual.Iron==25&&actual.Bread==45);
        }
        finally { field.SetValue(null,previous);ready.SetValue(null,wasReady);UnityEngine.Object.DestroyImmediate(rules); }
    }
}
#endif
