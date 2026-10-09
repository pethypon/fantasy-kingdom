#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;

/// <summary>Pure rule regressions for reflection release; no scene, asset or player-profile writes.</summary>
public static class ReflectionBalanceCoreTests
{
    static int passed;
    static void Check(string label, bool result)
    {
        if (!result) throw new InvalidOperationException("[ReflectionBalance] FAIL " + label);
        passed++;
        Debug.Log("[ReflectionBalance] PASS " + label);
    }

    public static void RunAll()
    {
        passed = 0;
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var loaded = typeof(GameAuthoringRules).GetField("loaded", flags);
        var didLoad = typeof(GameAuthoringRules).GetField("didLoad", flags);
        object previousRules = loaded.GetValue(null), previousLoaded = didLoad.GetValue(null);
        var rules = ScriptableObject.CreateInstance<GameAuthoringRules>();
        var unit = ScriptableObject.CreateInstance<UnitData>();
        try
        {
            rules.applyRules = true;
            loaded.SetValue(null, rules); didLoad.SetValue(null, true);
            foreach (int citizens in new[] { 0, 20, 30, 100 })
            {
                var ap = new FactionState.APData { Reset = 30, Plus = citizens, Current = 3 };
                ap.ResetForTurn();
                Check("133 AP citizens=" + citizens, ap.Current == Math.Min(60, 30 + citizens));
                Check("133 AP display maximum matches refill", ap.Maximum == ap.Current);
            }
            var refill = new FactionState.APData { Current = 3, Reset = 30, Plus = 24 };
            refill.ResetForTurn();
            Check("134 AP refills to 54 instead of adding to remaining 3", refill.Current == 54);
            SaveSystem.RestoreAP(new SaveSystem.APSaveData { Reset = 30, Plus = 24, Current = 999 }, refill);
            Check("80 restored AP cannot exceed own maximum", refill.Current == 54 && refill.Maximum == 54);
            SaveSystem.RestoreAP(new SaveSystem.APSaveData { Reset = 30, Plus = 20, Current = 50 }, refill);
            Check("80 old 50 AP save retains the current amount", refill.Current == 50 && refill.Maximum == 50);
            SaveSystem.RestoreAP(new SaveSystem.APSaveData { Reset = 30, Plus = 20, Current = -1 }, refill);
            Check("73 restored AP cannot be negative", refill.Current == 0);

            var state = new ThirdFactionDirectorState { CurrentIP = 42 };
            var ip = new ThirdFactionIPSystem(state);
            ip.BeginTurn(); Check("135 IP still recovers exactly five", ip.Current == 47);
            ip.BeginTurn(); Check("135 IP still caps at 50 while AP caps at 60", ip.Current == 50 && GameConstants.MaxAP == 60);

            unit.kind = Kind.Knight;
            foreach (int level in new[] { 1, 9, 10, 19, 20 })
            {
                int expected = 1 + level / 10;
                var upkeep = unit.GetUpkeep(level);
                Check("136/138 normal Lv" + level + " bread and iron", upkeep.Bread == expected && upkeep.Iron == expected);
                Check("138 multiplier agrees at Lv" + level, UnitData.UpkeepMultiplier(level) == expected);
            }
            unit.upkeepWood = 2; unit.upkeepStone = 3; unit.upkeepWater = 4; unit.upkeepMagic = 1;
            unit.upkeepBread = 3; unit.upkeepIron = 2; unit.upkeepMagicScales = false;
            var authored = unit.GetUpkeep(10);
            Check("88 authored extra resources remain available and bread/iron follow shared base", authored.Wood == 4 && authored.Stone == 6 && authored.Water == 8
                && authored.Bread == 2 && authored.Iron == 2 && authored.MagicOre == 1);
            unit.kind = Kind.King; Check("137 king is exempt even with authored cost", unit.GetUpkeep(20).IsEmpty);
            unit.kind = Kind.Boss; Check("137 demon lord boss is exempt", unit.GetUpkeep(20).IsEmpty);
            unit.kind = Kind.Knight; unit.upkeepExempt = true;
            Check("137 explicitly authored summoned body is exempt", unit.GetUpkeep(20).IsEmpty);
            unit.upkeepExempt = false; unit.kind = Kind.Guardian;
            Check("137 normal production unit is not accidentally summoned-body exempt", !unit.GetUpkeep(1).IsEmpty);
            Check("88 upkeep definition is not modified by the calculation", unit.upkeepBread == 3 && unit.upkeepIron == 2);
        }
        finally
        {
            loaded.SetValue(null, previousRules); didLoad.SetValue(null, previousLoaded);
            UnityEngine.Object.DestroyImmediate(unit); UnityEngine.Object.DestroyImmediate(rules);
        }
        Debug.Log("[ReflectionBalance] " + passed + " pure rule checks passed");
    }
}
#endif
