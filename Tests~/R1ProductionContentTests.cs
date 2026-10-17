#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Production wiring and conservative migration regression, run after explicit installation.</summary>
public static class R1ProductionContentTests
{
    static int checks;
    static void Check(bool condition, string name)
    { if (!condition) throw new InvalidOperationException("[R1ProductionTests] FAIL " + name); checks++; }

    public static void InstallAndRun()
    {
        R1ProductionContentInstaller.Install();
        Run();
    }

    public static void Run()
    {
        checks = 0;
        var settings = AssetDatabase.LoadAssetAtPath<R1ProductionContentSettings>(R1ProductionContentInstaller.SettingsPath);
        R1ProductionContentInstaller.ValidateSettings(settings);
        Check(settings.entries.Count == 16, "sixteen editable role configurations");
        Check(settings.installed.Count == 16, "all roles have explicit installation records");
        Check(BuildSummonUIBuilder.SummonableKinds.Length == 16, "all sixteen roles have summon rows");
        var catalog = UnitAuthoringCatalog.Load();
        var dataMap = new Dictionary<Kind, UnitData>(); var prefabs = new Dictionary<Kind, GameObject>();
        catalog.Apply(dataMap, prefabs);
        var abilityIds = new HashSet<int>();
        foreach (var entry in settings.entries)
        {
            var record = settings.installed.Find(item => item != null && item.kind == entry.kind);
            Check(record != null && record.data != null && record.ability != null, "installed " + entry.kind);
            Check(record.data.specialSkill == record.ability && record.ability.enabledForPlay
                && record.ability.ConfigurationError == null, "playable configured skill " + entry.kind);
            Check(abilityIds.Add(record.ability.GetInstanceID()), "independent ability definition " + entry.kind);
            if (!entry.createPlayableUnit)
            {
                Check(record.data == entry.sourceData, "original existing UnitData identity preserved " + entry.kind);
                continue;
            }
            Check(dataMap.TryGetValue(entry.kind, out var data) && data == record.data
                && prefabs.TryGetValue(entry.kind, out var prefab) && prefab == data.prefab, "runtime and summon catalog " + entry.kind);
            Check(data.IsValidForAuthoring && data.AvailableFor(Team.Player) && data.AvailableFor(Team.Enemy), "both factions can recruit " + entry.kind);
            Check(catalog.GetById(data.definitionId) == data, "save/load identity " + entry.kind);
            var renderer = data.prefab.GetComponentInChildren<Renderer>(true);
            var actor = data.prefab.GetComponentInChildren<Status>(true);
            Check(renderer != null && renderer.enabled && data.prefab.GetComponentInChildren<Collider>(true) != null
                && actor != null && actor.kind == data.kind, "visible selectable matching prefab " + entry.kind);
            Check(data.actionProfile != null && data.actionProfile.movement.useCustom && data.actionProfile.movement.Offsets.Count > 0
                && data.actionProfile.attack.useCustom && data.actionProfile.attack.Offsets.Count > 0
                && data.actionProfile.vision.useCustom && data.actionProfile.vision.Offsets.Count > 0, "movement attack vision inherited " + entry.kind);
            if (entry.kind == Kind.Ninja)
            {
                bool ranged = false;
                foreach (var offset in data.actionProfile.attack.Offsets) if (Mathf.Max(Mathf.Abs(offset.x), Mathf.Abs(offset.y)) > 1) ranged = true;
                Check(ranged, "ninja has ranged kunai attacks");
            }
            if (entry.kind == Kind.Navigator)
                Check(data.actionProfile.attack.Offsets.Count == 1 && data.actionProfile.attack.Contains(0, 1, Direction.N)
                    && data.actionProfile.attack.Contains(0, -1, Direction.S), "navigator forward one-cell attack mirrors correctly");
        }
        CheckConservativeFill();
        string beforeSettings = EditorJsonUtility.ToJson(settings), beforeCatalog = EditorJsonUtility.ToJson(catalog);
        var abilitySnapshots = new Dictionary<SpecialSkillDefinition, string>();
        foreach (var record in settings.installed) abilitySnapshots[record.ability] = EditorJsonUtility.ToJson(record.ability);
        R1ProductionContentInstaller.Install(settings);
        Check(beforeSettings == EditorJsonUtility.ToJson(settings) && beforeCatalog == EditorJsonUtility.ToJson(catalog), "repeat installation adds no records or catalog entries");
        foreach (var pair in abilitySnapshots) Check(pair.Value == EditorJsonUtility.ToJson(pair.Key), "repeat installation preserves authored settings " + pair.Key.name);
        Debug.Log("[R1ProductionTests] " + checks + " checks passed.");
    }

    static void CheckConservativeFill()
    {
        var target = ScriptableObject.CreateInstance<SpecialSkillDefinition>();
        var defaults = ScriptableObject.CreateInstance<SpecialSkillDefinition>();
        try
        {
            defaults.apCost = 14; defaults.cooldown = 3; defaults.knightVisibleEnemyAtkPenalty = .2f;
            defaults.knightPenaltyStackRule = AbilityStackRule.Strongest;
            defaults.crossbowInitialAmmo = 3; defaults.scoutFreeMoveScope = AbilityResetScope.OwnerTurn;
            defaults.stationaryAttackCurve = AnimationCurve.Linear(0, 1, 3, 1.75f);
            target.apCost = 9; target.cooldown = 7; target.knightVisibleEnemyAtkPenalty = .11f;
            target.knightPenaltyStackRule = AbilityStackRule.Multiply;
            R1ProductionContentInstaller.FillUnconfiguredValues(target, defaults);
            Check(target.apCost == 9 && target.cooldown == 7 && Mathf.Approximately(target.knightVisibleEnemyAtkPenalty, .11f)
                && target.knightPenaltyStackRule == AbilityStackRule.Multiply, "existing scalar and enum tuning preserved");
            Check(target.crossbowInitialAmmo == 3 && target.scoutFreeMoveScope == AbilityResetScope.OwnerTurn
                && target.stationaryAttackCurve.length == 2, "unconfigured scalar enum curve filled");
            target.crossbowInitialAmmo = 1; target.stationaryAttackCurve = AnimationCurve.Linear(0, 1, 8, 1.75f);
            R1ProductionContentInstaller.FillUnconfiguredValues(target, defaults);
            Check(target.crossbowInitialAmmo == 1 && Mathf.Approximately(target.stationaryAttackCurve.keys[1].time, 8), "subsequent user tuning preserved");
        }
        finally { UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(defaults); }
    }
}
#endif
