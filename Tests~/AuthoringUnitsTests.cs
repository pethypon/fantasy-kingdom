#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

public static class AuthoringUnitsTests
{
    static void Check(string name, bool value)
    {
        if (!value) throw new Exception("[AuthoringUnits] FAIL " + name);
        Debug.Log("[AuthoringUnits] PASS " + name);
    }
    static UnitData Definition(string id, int hp, GameObject model)
    {
        var data = ScriptableObject.CreateInstance<UnitData>();
        data.definitionId = id; data.displayName = "検証の騎士 " + hp; data.category = "重装歩兵";
        data.kind = Kind.Knight; data.baseHP = hp; data.baseATK = 7; data.baseDEF = 3;
        data.prefab = model; data.useAuthoredAbilities = true;
        data.authoredPassive = PassiveSkill.HunterEyes; data.authoredSpecialAbility = SpecialAbility.WatchEye;
        return data;
    }
    public static void EditMode()
    {
        var catalog = ScriptableObject.CreateInstance<UnitAuthoringCatalog>();
        var model = new GameObject("AuthoringUnits model"); model.SetActive(false);
        var status = model.AddComponent<Status>(); status.kind = Kind.Knight; status.type = Type.Unit;
        var legacy = Definition("", 35, model);
        var a = Definition("test-unit-a", 70, model);
        var b = Definition("test-unit-b", 140, model);
        var profile = ScriptableObject.CreateInstance<BoardActionProfile>();
        try
        {
            catalog.units.Add(new UnitAuthoringCatalog.Entry { kind = Kind.Knight, data = legacy, prefab = model });
            catalog.units.Add(new UnitAuthoringCatalog.Entry { kind = Kind.Knight, data = a, standaloneDefinition = true });
            catalog.units.Add(new UnitAuthoringCatalog.Entry { kind = Kind.Knight, data = b, standaloneDefinition = true });
            var roles = new Dictionary<Kind, UnitData>(); var prefabs = new Dictionary<Kind, GameObject>();
            catalog.Apply(roles, prefabs);
            Check("legacy role overrides stay independent from new definitions", roles[Kind.Knight] == legacy);
            Check("two definitions can share the same AI role", catalog.EnumerateStandaloneDefinitions().Count() == 2);
            Check("stable IDs resolve distinct definitions", catalog.GetById(a.definitionId) == a && catalog.GetById(b.definitionId) == b);
            b.availableToPlayer = false;
            Check("faction availability filters summon list", catalog.EnumerateStandaloneDefinitions(Team.Player).Single() == a);
            Check("unknown ID never falls back to unrelated role", catalog.GetById("missing") == null);
            Check("data-owned prefab resolves", catalog.TryGetPrefab(a, out var resolved) && resolved == model);
            a.actionProfile = profile; profile.movement.useCustom = true; profile.movement.SetCell(3, 4, true);
            a.ApplyToStatus(status, 3); a.InitializeAbilities(status);
            Check("specific definition preserves growth and tile profile", status.GrowthData == a && BoardActionProfile.For(status) == profile);
            Check("specific definition publishes stable save ID", status.unitDefinitionId == a.definitionId);
            Check("authored abilities replace role defaults", status.passiveskill == PassiveSkill.HunterEyes
                && status.specialAbility == SpecialAbility.WatchEye && status.AssignedSkillId == -1);
            SkillData.AssignFixedSkill(status);
            Check("intentionally empty authored skill stays empty", status.AssignedSkillId == -1);
            var saved = JsonUtility.FromJson<SaveSystem.UnitSaveData>(JsonUtility.ToJson(SaveSystem.CaptureUnit(status)));
            Check("save JSON retains independent definition ID", saved.DefinitionId == a.definitionId);
            status.HP = 1; status.ATK = 0; SaveGameApplier.ApplyStatusFields(status, saved);
            Check("save fields retain custom growth profile and authored name", status.GrowthData == a
                && status.HP == saved.HP && KindNameJP.Get(status) == a.displayName);
            a.costAP = -1;
            Check("invalid costs exclude unsafe definitions", !a.IsValidForAuthoring && catalog.GetById(a.definitionId) == null);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(model); UnityEngine.Object.DestroyImmediate(catalog);
            UnityEngine.Object.DestroyImmediate(legacy); UnityEngine.Object.DestroyImmediate(a);
            UnityEngine.Object.DestroyImmediate(b); UnityEngine.Object.DestroyImmediate(profile);
        }
    }

    public static void PlayMode(GameSystems systems)
    {
        var setting = systems.UnitSetting; var summon = systems.SummonSystem;
        var field = typeof(UnitSetting).GetField("authoringCatalog", BindingFlags.Instance | BindingFlags.NonPublic);
        var previous = field.GetValue(setting);
        var catalog = ScriptableObject.CreateInstance<UnitAuthoringCatalog>();
        var model = GameObject.CreatePrimitive(PrimitiveType.Capsule); model.name = "AuthoringUnits prefab fixture";
        var prefabStatus = model.AddComponent<Status>(); prefabStatus.kind = Kind.Knight; prefabStatus.type = Type.Unit;
        var a = Definition("test-runtime-unit-a", 77, model);
        var b = Definition("test-runtime-unit-b", 177, model);
        a.costAP = 2; a.costWood = 3; b.costAP = 3; b.costWood = 5;
        var profile = ScriptableObject.CreateInstance<BoardActionProfile>(); a.actionProfile = profile;
        var created = new List<GameObject>();
        int ap = systems.FactionState.GetAP(Team.Player), wood = systems.FactionState.PlayerResources.Wood;
        try
        {
            catalog.units.Add(new UnitAuthoringCatalog.Entry { kind = a.kind, data = a, standaloneDefinition = true });
            catalog.units.Add(new UnitAuthoringCatalog.Entry { kind = b.kind, data = b, standaloneDefinition = true });
            field.SetValue(setting, catalog);
            UnitData roleDefault = setting.UnitDataMap[Kind.Knight];
            systems.FactionState.PlayerAP.Current = 20; systems.FactionState.PlayerResources.Wood = 20;
            var positions = summon.AIGetSummonablePositions(Team.Player);
            Check("authored summon fixture has legal placement", positions.Count > 0);
            var pos = positions[0];
            Check("specific authored summon succeeds", summon.AISummonUnit(pos, a, Team.Player));
            var actor = setting.PlayerUnit.GetComponentsInChildren<Status>().First(u => u.unitDefinitionId == a.definitionId);
            created.Add(actor.gameObject);
            Check("specific authored summon applies exact stats and profile", actor.MaxHP == 77 && actor.GrowthData == a && BoardActionProfile.For(actor) == profile);
            Check("specific authored summon consumes its own AP and resources", systems.FactionState.GetAP(Team.Player) == 18
                && systems.FactionState.PlayerResources.Wood == 17);
            Check("specific authored summon does not overwrite shared role", setting.UnitDataMap[Kind.Knight] == roleDefault);
            int charged = systems.FactionState.GetAP(Team.Player);
            Check("occupied summon cannot charge twice", !summon.AISummonUnit(pos, b, Team.Player)
                && systems.FactionState.GetAP(Team.Player) == charged);
            var save = SaveSystem.CaptureUnit(actor);
            var restored = summon.SpawnUnitForLoad(Kind.Knight, Team.Player, actor.transform.position, save.DefinitionId);
            Check("load resolves definition ID before role default", restored != null && restored.GrowthData == a && restored.MaxHP == 77);
            created.Add(restored.gameObject); SaveGameApplier.ApplyStatusFields(restored, save);
            Check("custom definition growth stays available after load", restored.GrowthData == a && restored.unitDefinitionId == a.definitionId);
            restored.Experience = 0; restored.GainExperience(Status.XPRequiredForLevel(2));
            Check("custom unit levels using its own definition", restored.MaxHP == UnitData.CalcStat(a.baseHP, a.hpGrowth, 2));
            Check("load never charges summon resources", systems.FactionState.GetAP(Team.Player) == charged);
            b.availableToEnemy = false;
            Check("enemy cannot summon disallowed authored unit", !summon.CanSummon(Team.Enemy, b));
        }
        finally
        {
            summon.CancelSummonMode();
            foreach (var obj in created)
            {
                if (obj == null) continue;
                var actor = obj.GetComponent<Status>(); UnitRegistry.Instance?.Unregister(actor);
                obj.SetActive(false); UnityEngine.Object.DestroyImmediate(obj);
            }
            field.SetValue(setting, previous); systems.FactionState.PlayerAP.Current = ap;
            systems.FactionState.PlayerResources.Wood = wood;
            UnityEngine.Object.DestroyImmediate(model); UnityEngine.Object.DestroyImmediate(catalog);
            UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b); UnityEngine.Object.DestroyImmediate(profile);
            systems.MoveGenerator.UnitPointCore(); systems.RefreshVision();
        }
    }
}
#endif
