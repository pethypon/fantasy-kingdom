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
            UpgradeMarginals();
            Check("siege-ranks-real-hp-progress-per-ap", AICombatEvaluator.AttackEfficiency(20, 2) > AICombatEvaluator.AttackEfficiency(2, 2)
                && AICombatEvaluator.AttackEfficiency(20, 2) > AICombatEvaluator.AttackEfficiency(20, 8));
            Check("siege-efficiency-zero-and-extreme-values-remain-bounded", AICombatEvaluator.AttackEfficiency(0, 0) == 0
                && AICombatEvaluator.AttackEfficiency(int.MaxValue, 0) <= 20);
            Check("equally-winning-targets-prefer-less-observed-work", AICombatEvaluator.VictoryPressure(300, 20, 2)
                > AICombatEvaluator.VictoryPressure(5000, 20, 2));
            Check("victory-pressure-handles-zero-impact-and-int32-barrier-sums", AICombatEvaluator.VictoryPressure(5000, 0, 2) == 0
                && AICombatEvaluator.VictoryPressure(2L * int.MaxValue, 1, int.MaxValue) >= 0);
            Check("long-passive-siege-competes-with-deployment", AICombatEvaluator.PassiveSiegeWorkload(5000, 14, 2) > 20
                && AICombatEvaluator.PassiveSiegeWorkload(100, 14, 2) == 0);
            Check("passive-workload-remains-finite-for-zero-impact", AICombatEvaluator.PassiveSiegeWorkload(5000, 0, 0) == 30);
        }
        finally { UnityEngine.Object.DestroyImmediate(priest); UnityEngine.Object.DestroyImmediate(archer); UnityEngine.Object.DestroyImmediate(healing); }
    }
    static float Required(AIRecruitmentNeed[] needs, ResourceDemandState[] resources, int capacity)
        => StrategicEconomyForecast.RequiredRecruitmentStorageCapacity(needs, resources, capacity, 7, 37, Team.Enemy, 4, 4);

    static void UpgradeMarginals()
    {
        var definition = ScriptableObject.CreateInstance<FacilityDefinitionData>();
        var host = new GameObject("marginal-upgrade-contract");
        try
        {
            definition.definitionId = "test:marginal-upgrade"; definition.behaviourKind = FacilityKind.Well;
            definition.levels = new[] {
                new FacilityData.FacilityLevelData { HP = 100, Output = new FacilityData.ProductionBundle { Water = 6 } },
                new FacilityData.FacilityLevelData { HP = 100, Output = new FacilityData.ProductionBundle { Water = 12 } } };
            var actor = host.AddComponent<Status>(); actor.type = Type.Building; actor.facilityKind = FacilityKind.Well;
            actor.HP = actor.MaxHP = 100; actor.Level = 1; actor.AuthoredFacility = definition;
            var action = new AIAction { ActionType = AIActionType.Upgrade, Unit = actor, TargetUnit = actor };
            StrategicProductionDemand Demand(ResourceDemandState[] resources, float storage = 0)
                => new StrategicProductionDemand(resources, new EconomyForecastResult(EconomicState.Healthy, 10, 10, 100, false, false, false),
                    true, true, 1 << (int)ResourceKind.Water, new float[8], new float[8], 0, 0, 4, 7, storage, AIEconomySettings.Active);
            var states = new ResourceDemandState[8];
            for (int i = 0; i < states.Length; i++) states[i] = new ResourceDemandState((ResourceKind)i, 100, 0, 0, 0, 0, 100, 0, 0);
            states[(int)ResourceKind.Water] = new ResourceDemandState(ResourceKind.Water, 100, 0, 0, 0, 200, 100, 6, 1);
            var demand = Demand(states); var upgraded = demand.EvaluateBuild(action);
            var added = demand.EvaluateBuild(FacilityKind.Well, definition);
            Check("equal-marginal-supply-has-equal-critical-coverage", upgraded.ImprovesCritical && upgraded.DeficitCoverage01 == added.DeficitCoverage01
                && Mathf.Abs(upgraded.FinalScore - added.FinalScore) < .01f);
            actor.facilityKind = definition.behaviourKind = FacilityKind.Warehouse;
            definition.levels[0] = new FacilityData.FacilityLevelData { HP = 100, SpecialValue = 10 };
            definition.levels[1] = new FacilityData.FacilityLevelData { HP = 100, SpecialValue = 20 };
            var warehouseUpgrade = Demand(states, 15).EvaluateBuild(action);
            Check("warehouse-upgrade-counts-added-capacity-only", warehouseUpgrade.ImprovesDeficit
                && Mathf.Abs(warehouseUpgrade.DeficitCoverage01 - 10f / 15) < .01f);
            actor.facilityKind = definition.behaviourKind = FacilityKind.Bakery;
            definition.levels[0] = new FacilityData.FacilityLevelData { HP = 100, Input = new FacilityData.ProductionBundle { Wheat = 5 }, Output = new FacilityData.ProductionBundle { Bread = 4 } };
            definition.levels[1] = new FacilityData.FacilityLevelData { HP = 100, Input = new FacilityData.ProductionBundle { Wheat = 5 }, Output = new FacilityData.ProductionBundle { Bread = 8 } };
            states[(int)ResourceKind.Water] = new ResourceDemandState(ResourceKind.Water, 100, 0, 0, 0, 0, 100, 0, 0);
            states[(int)ResourceKind.Wheat] = new ResourceDemandState(ResourceKind.Wheat, 0, 0, 5, 0, 5, 0, 5, 1);
            states[(int)ResourceKind.Bread] = new ResourceDemandState(ResourceKind.Bread, 0, 0, 0, 0, 10, 0, 10, 1);
            Check("upgrade-cannot-promise-output-without-existing-inputs", !Demand(states).EvaluateBuild(action).ImprovesDeficit);
            actor.Level = 2;
            Check("maximum-level-upgrade-is-not-an-investment", !Demand(states).EvaluateBuild(action).IsProduction);
        }
        finally { UnityEngine.Object.DestroyImmediate(host); UnityEngine.Object.DestroyImmediate(definition); }
    }
}
#endif
