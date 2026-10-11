#if UNITY_EDITOR
using System;
using UnityEngine;

/// <summary>Balance changes must preserve damage, earned bonuses, level and death through JSON resume.</summary>
public static class BalanceSaveTests
{
    static int passed;
    static void Check(string name, bool condition)
    { if (!condition) throw new InvalidOperationException("[BalanceSave] FAIL " + name); passed++; }
    static SaveSystem.UnitSaveData RoundTrip(Status actor)
        => JsonUtility.FromJson<SaveSystem.UnitSaveData>(JsonUtility.ToJson(SaveSystem.CaptureUnit(actor)));
    public static void Run()
    {
        passed = 0;
        var go = new GameObject("Balance save fixture");
        var definition = ScriptableObject.CreateInstance<UnitData>();
        try
        {
            var actor = go.AddComponent<Status>(); actor.type = Type.Unit; actor.team = Team.Player; actor.kind = Kind.Knight;
            definition.kind = Kind.Knight; definition.baseHP = 100; definition.baseATK = 20; definition.baseDEF = 10;
            definition.hpGrowth = definition.atkGrowth = definition.defGrowth = .1f;
            definition.ApplyToStatus(actor, 3);
            actor.ATK += 17; actor.DEF += 8; actor.MaxHP += 50; actor.HP = 85;
            actor.Experience = 7; actor.SkillCooldown = 2;
            var saved = RoundTrip(actor);
            Check("formal baseline is saved separately from rewards", saved.HasDefinitionStatSnapshot
                && saved.DefinitionMaxHP == 120 && saved.DefinitionATK == 24 && saved.DefinitionDEF == 14);
            definition.baseHP = 200; definition.baseATK = 30; definition.baseDEF = 18;
            SaveGameApplier.ApplyStatusFields(actor, saved);
            Check("latest HP plus original permanent bonus", actor.MaxHP == 290);
            Check("half damaged stays half damaged", actor.HP == 145);
            Check("latest attack retains reward bonus", actor.ATK == 53);
            Check("latest defense retains reward bonus", actor.DEF == 30);
            Check("level experience and cooldown unchanged", actor.Level == 3 && actor.Experience == 7 && actor.SkillCooldown == 2);
            var resumed = RoundTrip(actor); SaveGameApplier.ApplyStatusFields(actor, resumed);
            Check("save resume repeated never multiplies bonuses", actor.HP == 145 && actor.MaxHP == 290 && actor.ATK == 53 && actor.DEF == 30);
            saved.HP = 0; saved.IsActive = false; SaveGameApplier.ApplyStatusFields(actor, saved);
            Check("balance change cannot revive a defeated actor", actor.HP == 0 && !go.activeSelf);
            var legacy = JsonUtility.FromJson<SaveSystem.UnitSaveData>(JsonUtility.ToJson(saved));
            legacy.HasDefinitionStatSnapshot = false; legacy.HP = 85; legacy.IsActive = true;
            SaveGameApplier.ApplyStatusFields(actor, legacy);
            Check("old saves without baseline retain earned legacy stats", actor.HP == 85 && actor.MaxHP == 170 && actor.ATK == 41 && actor.DEF == 22);
            saved.DefinitionATK = 1000; SaveGameApplier.ApplyStatusFields(actor, saved);
            Check("negative deltas cannot create negative attack", actor.ATK == 0);
            actor.isWildBoss = true;
            Check("special boss profiles are not rebalanced into ordinary units", !StatusBalanceRefresh.TryGetDefinitionStats(actor, out _, out _, out _));
            actor.isWildBoss = false; actor.kind = Kind.Crystal; actor.type = Type.Building;
            Check("main crystal artifact HP is not overwritten by a facility", !StatusBalanceRefresh.TryGetDefinitionStats(actor, out _, out _, out _));
        }
        finally { UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(definition); }
        Debug.Log("[BalanceSave] " + passed + " balance/reward/damage/legacy checks passed");
    }
}
#endif
