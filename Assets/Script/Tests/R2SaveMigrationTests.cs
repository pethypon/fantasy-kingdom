#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class R2SaveMigrationTests
{
    static void Check(string id, bool ok)
    { if (!ok) throw new InvalidOperationException("[R2Save] FAIL " + id); Debug.Log("[R2Save] PASS " + id); }
    static SaveSystem.GameSaveData Migrate(SaveSystem.GameSaveData data)
        => (SaveSystem.GameSaveData)typeof(SaveSystem).GetMethod("RunMigrations",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{data});
    public static void Run()
    {
        var legacy = UnitStaticData.Table[Kind.Knight];
        int oldHP = FixedUnitRules.LegacyStat(legacy.BaseHP,legacy.HpGrowth,20);
        int oldAtk = FixedUnitRules.LegacyStat(legacy.BaseATK,legacy.AtkGrowth,20);
        int oldDef = FixedUnitRules.LegacyStat(legacy.BaseDEF,legacy.DefGrowth,20);
        var soldier = new SaveSystem.UnitSaveData { Kind="Knight", Type="Unit", Team="Player", Level=20,Experience=1000,
            HP=(oldHP+12)/2, MaxHP=oldHP+12,ATK=oldAtk+7,DEF=oldDef,HasDefinitionStatSnapshot=true,
            DefinitionMaxHP=oldHP,DefinitionATK=oldAtk,DefinitionDEF=oldDef,AssignedSkillId=2,SkillCooldown=3,
            Special=new SpecialState(),BarrierRecovery=new BarrierRecoveryState{SafeTurns=3,LastProcessedRound=7} };
        var dead = JsonUtility.FromJson<SaveSystem.UnitSaveData>(JsonUtility.ToJson(soldier)); dead.HP=0; dead.IsActive=false;
        var facility = new SaveSystem.UnitSaveData { Kind="StoneWall",Type="Wall",Level=3,HP=200,MaxHP=250,ATK=8,DEF=18 };
        var unknown = new SaveSystem.UnitSaveData { Kind="Knight",Type="Unit",Level=80,HP=101,MaxHP=777,ATK=88,DEF=44 };
        var data = new SaveSystem.GameSaveData { Version=11,SchemaVersion=11,MapWidth=35,MapDepth=35,
            Units=new List<SaveSystem.UnitSaveData>{soldier,dead,facility,unknown} };
        data.PlayerResources.Iron=123; data.EnemyResources.MagicOre=99;
        data.Timer.TurnTimeRemaining=0; data.Timer.PlayerTotalTime=0; data.Timer.EnemyTotalTime=0;
        double ratio=(double)soldier.HP/soldier.MaxHP;
        data=Migrate(data);
        int expectedMax=FixedUnitRules.BaseStat(Kind.Knight,legacy.BaseHP)+12;
        Check("F01",data.Version==12&&data.SchemaVersion==12&&SaveSystem.ValidateMigrationChain(out _));
        Check("F02",soldier.Level==1&&soldier.Experience==0&&soldier.MaxHP==expectedMax
            &&soldier.ATK==FixedUnitRules.BaseStat(Kind.Knight,legacy.BaseATK)+7);
        Check("F03",soldier.HP==(int)Math.Round(ratio*expectedMax,MidpointRounding.AwayFromZero));
        Check("F04",dead.HP==0&&!dead.IsActive);
        Check("F05",facility.Level==3&&facility.HP==200&&facility.MaxHP==250&&facility.ATK==8);
        Check("F06-partial",soldier.AssignedSkillId==2&&soldier.SkillCooldown==3&&soldier.Special!=null);
        string once=JsonUtility.ToJson(data);data=Migrate(data);Check("F07",once==JsonUtility.ToJson(data));
        Check("F08",SaveSystem.ValidateMigrationChain(out _));
        var future=new SaveSystem.GameSaveData{Version=13,SchemaVersion=13};Check("F09-future",Migrate(future)==null&&future.Version==13);
        Check("F10-partial",data.MapWidth==35&&data.PlayerResources.Iron==123&&data.EnemyResources.MagicOre==99);
        Check("SVR02",data.Timer.PlayerTotalTime==0&&data.Timer.EnemyTotalTime==0);
        Check("SVR08",soldier.BarrierRecovery.SafeTurns==0&&soldier.BarrierRecovery.LastProcessedRound==7);
        Check("unknown-bonuses-preserved",unknown.FixedStatsMigrationDeferred&&unknown.HP==101&&unknown.MaxHP==777&&unknown.ATK==88
            &&unknown.LegacyUnitStats.Level==80&&unknown.LegacyUnitStats.MaximumHP==777);
        Check("SVR10-unit",once==JsonUtility.ToJson(Migrate(data)));
        SceneDefinitionOverride();
        Debug.Log("[R2Save] COMPLETE");
    }
    static void SceneDefinitionOverride()
    {
        var saved=new SaveSystem.UnitSaveData{Kind="Knight",Type="Unit",Level=20,Experience=800,HP=110,MaxHP=220,
            ATK=43,DEF=5,HasDefinitionStatSnapshot=true,DefinitionMaxHP=200,DefinitionATK=40,DefinitionDEF=5};
        var game=new SaveSystem.GameSaveData{Version=11,SchemaVersion=11,Units=new List<SaveSystem.UnitSaveData>{saved}};
        Migrate(game);
        Check("scene-override-never-guessed-from-static",saved.FixedStatsMigrationDeferred&&saved.MaxHP==220
            &&saved.LegacyUnitStats.DefinitionMaxHP==200);
        var definition=ScriptableObject.CreateInstance<UnitData>();
        var host=new GameObject("R2 id-less scene definition");
        using var effects=BoardActorAnimation.SuppressEffects();
        try
        {
            definition.kind=Kind.Knight;definition.baseHP=100;definition.baseATK=20;definition.baseDEF=10;
            var actor=host.AddComponent<Status>();actor.type=Type.Unit;actor.kind=Kind.Knight;actor.GrowthData=definition;
            actor.MaxHP=saved.MaxHP;actor.HP=saved.HP;actor.ATK=saved.ATK;actor.DEF=saved.DEF;
            actor.FixedStatsMigrationDeferred=true;actor.LegacyUnitStats=saved.LegacyUnitStats.Copy();
            var apply=typeof(SaveGameApplier).GetMethod("ApplyLatestDefinitionStats",BindingFlags.NonPublic|BindingFlags.Static);
            apply.Invoke(null,new object[]{actor,saved});
            Check("F02-scene-definition",!actor.FixedStatsMigrationDeferred&&actor.MaxHP==195&&actor.ATK==38&&actor.DEF==18&&actor.HP==98);
            var converted=SaveSystem.CaptureUnit(actor);apply.Invoke(null,new object[]{actor,converted});
            Check("F07-scene-definition",actor.MaxHP==195&&actor.ATK==38&&actor.HP==98&&actor.LegacyUnitStats==null);
        }
        finally { UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(definition); }
    }
}
#endif
