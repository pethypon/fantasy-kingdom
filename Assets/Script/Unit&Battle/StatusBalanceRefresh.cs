using System;
using UnityEngine;

/// <summary>Definition stats and safe live rebalance; reward/buff differences remain runtime state.</summary>
public static class StatusBalanceRefresh
{
    public static bool TryGetDefinitionStats(Status actor, out int maxHP, out int atk, out int def)
    {
        maxHP = atk = def = 0;
        if (actor == null || actor.isWildBoss || actor.kind == Kind.Crystal) return false;
        if (actor.type == Type.Building || actor.type == Type.Wall)
        {
            if (!FacilityData.IsKnownKind(actor.facilityKind)) return false;
            var data = FacilityData.GetLevel(actor, Mathf.Max(1, actor.Level));
            maxHP = data.HP; atk = data.ATK; def = data.DEF; return maxHP > 0;
        }
        if (actor.type != Type.Unit) return false;
        var definition = actor.GrowthData;
        if (definition != null && definition.IsValidForAuthoring)
        {
            maxHP = UnitData.CalcStat(definition.baseHP, definition.hpGrowth, actor.Level);
            atk = UnitData.CalcStat(definition.baseATK, definition.atkGrowth, actor.Level);
            def = UnitData.CalcStat(definition.baseDEF, definition.defGrowth, actor.Level); return true;
        }
        if (!UnitStaticData.Table.TryGetValue(actor.kind, out var legacy)) return false;
        maxHP = UnitData.CalcStat(legacy.BaseHP, legacy.HpGrowth, actor.Level);
        atk = UnitData.CalcStat(legacy.BaseATK, legacy.AtkGrowth, actor.Level);
        def = UnitData.CalcStat(legacy.BaseDEF, legacy.DefGrowth, actor.Level); return true;
    }

    public static void RecordDefinitionBaseline(Status actor)
    {
        if (!TryGetDefinitionStats(actor, out var hp, out var atk, out var def)) return;
        actor.HasDefinitionBalanceBaseline = true;
        actor.DefinitionBalanceMaxHP = hp; actor.DefinitionBalanceATK = atk; actor.DefinitionBalanceDEF = def;
    }

    public static bool ApplyLatestDefinition(Status actor, bool preserveHpRatio = true)
    {
        if (!TryGetDefinitionStats(actor, out var hp, out var atk, out var def)) return false;
        if (!actor.HasDefinitionBalanceBaseline)
        {
            // A domain reload cannot identify historic rewards from total stats alone.
            // Start tracking without erasing unknown runtime bonuses; a new battle applies all values.
            RecordDefinitionBaseline(actor); return false;
        }
        int oldMax = actor.MaxHP, oldHP = actor.HP;
        actor.MaxHP = Saturating(hp, (long)oldMax - actor.DefinitionBalanceMaxHP, 1);
        actor.ATK = Saturating(atk, (long)actor.ATK - actor.DefinitionBalanceATK, 0);
        actor.DEF = Saturating(def, (long)actor.DEF - actor.DefinitionBalanceDEF, 0);
        actor.HP = oldHP <= 0 ? 0 : preserveHpRatio && oldMax > 0
            ? (int)Math.Max(1, Math.Min(actor.MaxHP, Math.Round((double)oldHP / oldMax * actor.MaxHP)))
            : Mathf.Clamp(oldHP, 0, actor.MaxHP);
        RecordDefinitionBaseline(actor);
        return true;
    }
    static int Saturating(int baseline, long difference, int minimum)
        => (int)Math.Max(minimum, Math.Min(int.MaxValue, baseline + difference));
}
