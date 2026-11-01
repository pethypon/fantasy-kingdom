#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>Resolved production definitions and an initial roster, reporting only.</summary>
public static class EconomyBalanceEvidence
{
    [Serializable] sealed class UnitRow
    {
        public string kind, definitionId;
        public int baseHP, baseATK, fixedHP, fixedATK, AP;
        public FacilityData.ProductionBundle cost, upkeep;
    }
    [Serializable] sealed class FacilityRow
    {
        public string kind;
        public int AP;
        public FacilityData.ResourceCost cost;
        public FacilityData.FacilityLevelData[] levels;
    }
    [Serializable] sealed class Export
    {
        public string scope = "Resolved New Game definitions and actual initial troops; read-only, no AI input or state edits.";
        public List<UnitRow> units = new List<UnitRow>();
        public List<FacilityRow> facilities = new List<FacilityRow>();
        public List<string> initialTroops = new List<string>();
        public string rules;
    }
    public static void Write(GameSystems s, string folder)
    {
        var output = new Export { rules = JsonUtility.ToJson(GameAuthoringRules.Active) };
        foreach (var entry in s.UnitSetting.UnitDataMap)
        {
            var u = entry.Value;
            if (u == null || !u.IsValidForAuthoring || !FixedUnitRules.UsesFixedStats(u.kind) || u.kind == Kind.Engineer) continue;
            output.units.Add(new UnitRow { kind = u.kind.ToString(), definitionId = u.definitionId,
                baseHP = u.baseHP, baseATK = u.baseATK, fixedHP = u.FixedMaxHP, fixedATK = u.FixedATK, AP = u.costAP,
                cost = new FacilityData.ProductionBundle { Wood = u.costWood, Stone = u.costStone, Iron = u.costIron,
                    MagicOre = u.costMagic, Water = u.costWater, Bread = u.costBread, Citizen = u.costCitizen }, upkeep = u.GetFixedUpkeep() });
        }
        foreach (var entry in FacilityData.Table)
        {
            var levels = new FacilityData.FacilityLevelData[entry.Value.MaxLevel];
            for (int i = 0; i < levels.Length; i++) levels[i] = FacilityData.GetLevel(entry.Key, i + 1);
            output.facilities.Add(new FacilityRow { kind = entry.Key.ToString(), AP = entry.Value.APCost, cost = entry.Value.BuildCost, levels = levels });
        }
        foreach (var parent in new[] { s.UnitSetting.PlayerUnit, s.UnitSetting.EnemyUnit })
            foreach (var unit in parent.GetComponentsInChildren<Status>())
                if (unit != null && unit.IsAlive) output.initialTroops.Add(unit.team + ":" + unit.kind);
        File.WriteAllText(Path.Combine(folder, "economy-definitions.json"), JsonUtility.ToJson(output, true));
    }
}
#endif
