#if UNITY_EDITOR
using System;
using UnityEngine;

/// <summary>Material-capacity contracts for a planned purchase. See per-run evidence for execution results.</summary>
public static class AIR3MaterialCapacityTests
{
    static void Check(string id, bool result)
    { if (!result) throw new InvalidOperationException("[R3MaterialCapacity] FAIL " + id); Debug.Log("[R3MaterialCapacity] PASS " + id); }
    public static void Run()
    {
        var priest = ScriptableObject.CreateInstance<UnitData>();
        var archer = ScriptableObject.CreateInstance<UnitData>();
        var healing = ScriptableObject.CreateInstance<SpecialSkillDefinition>();
        healing.ability = UnitSpecialSkill.PriestPurification; healing.enabledForPlay = true; healing.apCost = 8; healing.cooldown = 0;
        priest.specialSkill = healing;
        try
        {
            priest.kind = Kind.Priest; priest.baseHP = 28; priest.baseATK = 3;
            priest.costWater = 100; priest.costCitizen = 1; priest.costAP = 14;
            archer.kind = Kind.Archer; archer.baseHP = 24; archer.baseATK = 15;
            archer.costWater = 70; archer.costCitizen = 1; archer.costAP = 7;
            var resources = new ResourceDemandState[8];
            for (int i = 0; i < 8; i++) resources[i] = new ResourceDemandState((ResourceKind)i, 200, 11, 1, 0, 20, 200, 1, 0);
            resources[(int)ResourceKind.Water] = new ResourceDemandState(ResourceKind.Water, 200, 79, 32, 0, 136, 200, 32, 0);
            var one = new[] { new AIRecruitmentNeed(priest, "healer", "operation_squad_role_missing") };
            float requirement = Required(one, resources, 200);
            Check("purchase-plus-protected-reserve-exceeds-cap", requirement > 200 && requirement <= 300);
            Check("already-expanded-capacity-does-not-repeat-investment", Required(one, resources, 300) == 300);
            var both = new[] { one[0], new AIRecruitmentNeed(archer, "ranged", "operation_squad_role_missing") };
            Check("sequential-orders-are-not-summed", Required(both, resources, 200) <= requirement);
            priest.costAP = 100;
            Check("unpayable-AP-does-not-justify-storage", Required(one, resources, 200) == 200);
            priest.costAP = 14; priest.availableToEnemy = false;
            Check("foreign-definition-does-not-justify-storage", Required(one, resources, 200) == 200);
            priest.availableToEnemy = true; priest.costWater = 1000000;
            Check("unreachable-authored-cost-does-not-create-endless-investment", Required(one, resources, 200) == 200);
            priest.costWater = 100;
            var dry = (ResourceDemandState[])resources.Clone();
            dry[(int)ResourceKind.Water] = new ResourceDemandState(ResourceKind.Water, 20, 0, 0, 0, 20, 20, 0, 0);
            Check("missing-income-needs-supply-not-capacity", Required(one, dry, 200) == 200);
            priest.costWater = 10;
            Check("purchase-that-fits-never-requests-expansion", Required(one, resources, 200) == 200);
            priest.costWater = 100;
            var demand = new StrategicProductionDemand(resources,
                new EconomyForecastResult(EconomicState.Healthy, 10, 10, 100, false, false, false),
                false, false, 0, new float[8], new float[8], 0, 0, 4, 7, requirement - 200, AIEconomySettings.Active);
            var warehouse = demand.EvaluateBuild(FacilityKind.Warehouse);
            Check("existing-production-assessment-recognizes-planned-capacity", warehouse.IsProduction && warehouse.ImprovesDeficit);
        }
        finally { UnityEngine.Object.DestroyImmediate(priest); UnityEngine.Object.DestroyImmediate(archer); UnityEngine.Object.DestroyImmediate(healing); }
    }
    static float Required(AIRecruitmentNeed[] needs, ResourceDemandState[] resources, int capacity)
        => StrategicEconomyForecast.RequiredRecruitmentStorageCapacity(needs, resources, capacity, 7, 37, Team.Enemy, 4, 4);
}
#endif
