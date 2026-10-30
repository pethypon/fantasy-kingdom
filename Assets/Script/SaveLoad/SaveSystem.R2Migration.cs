using System;
using UnityEngine;

public static partial class SaveSystem
{
    static void Migrate_V11_to_V12(GameSaveData data)
    {
        if (data.Units != null) foreach (var unit in data.Units) MigrateFixedUnit(unit);
        if (data.IndependentUnits != null) foreach (var entry in data.IndependentUnits)
            if (entry != null) MigrateFixedUnit(entry.Unit);
        if (data.PlayerNationExtra != null) data.PlayerNationExtra.BarracksXP = 0;
        if (data.EnemyNationExtra != null) data.EnemyNationExtra.BarracksXP = 0;
    }

    static void MigrateFixedUnit(UnitSaveData unit)
    {
        if (unit == null || unit.Type != StableSaveKeys.Key(Type.Unit)) return;
        int oldLevel = unit.Level, oldExperience = unit.Experience;
        unit.Level = 1; unit.Experience = 0; unit.LastMaintenanceRound = -1;
        unit.BarrierRecovery = BarrierSystem.CopyState(unit.BarrierRecovery);
        if (!StableSaveKeys.TryParse<Kind>(unit.Kind, out var kind) || !FixedUnitRules.UsesFixedStats(kind)
            || unit.HasPrimaryCommanderRole && unit.IsPrimaryCommander) { unit.FixedStatsRevision = 2; return; }
        var definition = string.IsNullOrEmpty(unit.DefinitionId) ? null : UnitAuthoringCatalog.Load()?.GetById(unit.DefinitionId);
        int hp, atk, def, oldHP, oldATK, oldDEF;
        if (definition != null && definition.IsValidForAuthoring)
        {
            hp = definition.FixedMaxHP; atk = definition.FixedATK; def = definition.FixedDEF;
            oldHP = FixedUnitRules.LegacyStat(definition.baseHP, definition.hpGrowth, oldLevel);
            oldATK = FixedUnitRules.LegacyStat(definition.baseATK, definition.atkGrowth, oldLevel);
            oldDEF = FixedUnitRules.LegacyStat(definition.baseDEF, definition.defGrowth, oldLevel);
        }
        else if (UnitStaticData.Table.TryGetValue(kind, out var legacy))
        {
            hp = FixedUnitRules.BaseStat(kind, legacy.BaseHP); atk = FixedUnitRules.BaseStat(kind, legacy.BaseATK);
            def = FixedUnitRules.BaseStat(kind, legacy.BaseDEF);
            oldHP = FixedUnitRules.LegacyStat(legacy.BaseHP, legacy.HpGrowth, oldLevel);
            oldATK = FixedUnitRules.LegacyStat(legacy.BaseATK, legacy.AtkGrowth, oldLevel);
            oldDEF = FixedUnitRules.LegacyStat(legacy.BaseDEF, legacy.DefGrowth, oldLevel);
            // An id-less scene override may differ from static defaults. Its saved baseline
            // cannot identify today's raw definition, so never substitute a guessed static base.
            if (unit.HasDefinitionStatSnapshot && (unit.DefinitionMaxHP != oldHP
                || unit.DefinitionATK != oldATK || unit.DefinitionDEF != oldDEF))
            { DeferFixedMigration(unit, oldLevel, oldExperience); return; }
        }
        else { DeferFixedMigration(unit, oldLevel, oldExperience); return; }
        // Snapshot is authoritative. Without it, only an exact old canonical base is identifiable.
        if (unit.HasDefinitionStatSnapshot)
        { oldHP = unit.DefinitionMaxHP; oldATK = unit.DefinitionATK; oldDEF = unit.DefinitionDEF; }
        else if (unit.MaxHP != oldHP || unit.ATK != oldATK || unit.DEF != oldDEF)
        { DeferFixedMigration(unit, oldLevel, oldExperience); return; }
        if (unit.MaxHP <= 0 || oldHP <= 0 || unit.HP < 0 || unit.ATK < 0 || unit.DEF < 0)
        { DeferFixedMigration(unit, oldLevel, oldExperience); return; }
        int savedMax = unit.MaxHP, savedHP = unit.HP;
        unit.MaxHP = Delta(hp, (long)savedMax - oldHP, 1);
        unit.ATK = Delta(atk, (long)unit.ATK - oldATK, 0);
        unit.DEF = Delta(def, (long)unit.DEF - oldDEF, 0);
        unit.HP = savedHP <= 0 ? 0 : (int)Math.Max(1, Math.Min(unit.MaxHP,
            Math.Round((double)savedHP / savedMax * unit.MaxHP, MidpointRounding.AwayFromZero)));
        unit.HasDefinitionStatSnapshot = true;
        unit.DefinitionMaxHP = hp; unit.DefinitionATK = atk; unit.DefinitionDEF = def;
        unit.FixedStatsRevision = 2; unit.FixedStatsMigrationDeferred = false;
        // Remove old level growth without multiplying base barrier or filling a missing snapshot.
        if (unit.HasBarrierSnapshot)
        {
            int baseBarrier = definition != null && definition.baseBarrier >= 0 ? definition.baseBarrier : BarrierSystem.Initial(kind);
            unit.MaxBarrier = BarrierSystem.UsesBarrier(kind, Type.Unit) ? Math.Max(0, baseBarrier) : 0;
            unit.Barrier = Math.Max(0, Math.Min(unit.Barrier, unit.MaxBarrier));
        }
    }
    static int Delta(int basis, long difference, int minimum)
        => (int)Math.Max(minimum, Math.Min(int.MaxValue, basis + difference));
    static void DeferFixedMigration(UnitSaveData unit, int oldLevel, int oldExperience)
    {
        unit.LegacyUnitStats = new LegacyUnitStatSnapshot { Level=oldLevel, Experience=oldExperience,
            HP=unit.HP, MaximumHP=unit.MaxHP, ATK=unit.ATK, DEF=unit.DEF,
            HasDefinitionBaseline=unit.HasDefinitionStatSnapshot, DefinitionMaxHP=unit.DefinitionMaxHP,
            DefinitionATK=unit.DefinitionATK, DefinitionDEF=unit.DefinitionDEF };
        unit.FixedStatsRevision = 2; unit.FixedStatsMigrationDeferred = true;
        unit.HasDefinitionStatSnapshot = false;
        Debug.LogWarning("[SaveSystem] 旧基礎値と恒久補正を分離できないため能力変換を保留しました: "
            + unit.Kind + " / " + unit.DefinitionId + "。元の能力とHPを保持しています。");
    }
}
