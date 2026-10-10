#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Observed-board and real economy tests. All content is in-memory and restored by the R2 fixture.</summary>
public static class ReflectionIntegrationTests
{
    static int passed;
    static void Check(string label, bool result)
    {
        if (!result) throw new InvalidOperationException("[ReflectionIntegration] FAIL " + label);
        passed++; Debug.Log("[ReflectionIntegration] PASS " + label);
    }
    static bool Near(float a, float b) => Mathf.Abs(a - b) < .001f;
    static int Stock(FactionState.ResourceData resources, ResourceKind kind)
    {
        switch (kind)
        {
            case ResourceKind.Wood: return resources.Wood;
            case ResourceKind.Stone: return resources.Stone;
            case ResourceKind.Iron: return resources.Iron;
            case ResourceKind.MagicOre: return resources.MagicOre;
            case ResourceKind.Wheat: return resources.Wheat;
            case ResourceKind.Bread: return resources.Bread;
            case ResourceKind.Water: return resources.Water;
            default: return resources.Citizen;
        }
    }

    public static void PlayMode(GameSystems systems)
    {
        passed = 0;
        FogFairness(systems);
        CommanderSelectionAndExecution(systems);
        UpkeepAndForecast(systems);
        MaintenancePrecedesProduction(systems);
        ProductionDefinitions(systems);
        ProductionIntervalsAndOverrides(systems);
        Debug.Log("[ReflectionIntegration] " + passed + " real board/economy checks passed");
    }

    static void FogFairness(GameSystems systems)
    {
        foreach (var team in new[] { Team.Enemy, Team.Player })
        using (var f = new EconomyR2Tests.Fixture(systems, team))
        {
            var cells = f.AdjacentCells();
            var own = f.Unit(); own.transform.position = cells[0]; own.ATK = 8;
            var hidden = f.Opponent(cells[2], 100);
            var board = f.Board(); var capture = new AIActionFeatureExtractor();
            var action = new AIAction { ActionType = AIActionType.Attack, Unit = own,
                TargetUnit = hidden, TargetPos = cells[2] };
            Check("127 actual hidden opponent excluded for " + team, !board.AlivePlayerUnits.Contains(hidden));
            var before = capture.Capture(action, board, 20, TurnStrategy.Assault, 15, 0);
            string original = JsonUtility.ToJson(before);
            hidden.transform.position = cells[2] + new Vector3(30, 4, 30); hidden.HP = 1; hidden.ATK = 999;
            board.Refresh();
            var after = capture.Capture(action, board, 20, TurnStrategy.Assault, 15, 0);
            Check("127 hidden current position/HP/power do not alter snapshot for " + team,
                original == JsonUtility.ToJson(after) && !after.TargetObserved && after.TargetHP == -1);
            board.AlivePlayerUnits.Add(hidden);
            var freshCapture = new AIActionFeatureExtractor();
            var observed = freshCapture.Capture(action, board, 20, TurnStrategy.Assault, 15, 0);
            Check("127 newly observed target is represented for " + team,
                observed.TargetObserved && observed.TargetHP == 1 && observed.VisibleEnemyUnitCount == 1);
        }
    }

    static void CommanderSelectionAndExecution(GameSystems systems)
    {
        using (var f = new EconomyR2Tests.Fixture(systems, Team.Player))
        {
            var config = ScriptableObject.CreateInstance<AIReflectionConfig>();
            config.EnableConsoleDiary = config.EnableFileDiary = false;
            try
            {
                var commander = new AICommander(UnityEngine.Object.FindFirstObjectByType<TurnGenerator>(),
                    systems.MoveGenerator, systems.AttackGenerator, systems.BattleSystem, null, f.AP, f.Units,
                    f.Crystals, systems.MapCreate, MajorPersonality.Combat, f.Builder, null, f.State,
                    systems.SkillSystem, null, 100, 7711, Team.Player);
                var board = f.Board(); var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(AICommander).GetField("_board", flags).SetValue(commander, board);
                commander.SaveTurnCount = 10;
                string memoryOnlyRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ReflectionPlayerMemory_" + Guid.NewGuid().ToString("N"));
                var reflection = new AIActionReflectionSystem(Team.Player, config, memoryOnlyRoot, false);
                reflection.BeginBattle("CommanderSelection", 100, "Combat");
                reflection.BeginTurn(10, board, commander.CurrentStrategy, 100);
                typeof(AICommander).GetField("reflection", flags).SetValue(commander, reflection);
                var a = new AIAction { ActionType = AIActionType.Build, Facility = FacilityKind.LoggingCamp,
                    Score = 10, StrategicPriority = 4, APCost = 2 };
                var b = new AIAction { ActionType = AIActionType.Build, Facility = FacilityKind.Quarry,
                    Score = 10.01f, StrategicPriority = 4, APCost = 2 };
                var snapshot = new AIActionFeatureExtractor().Capture(a, board, 10, commander.CurrentStrategy, 100, 0);
                reflection.Profile.Learn(new AIActionRecord
                {
                    ContextKey = AIActionFeatureExtractor.ContextKey(snapshot), ActionKey = AIActionFeatureExtractor.ActionKey(a, snapshot),
                    Reward = 10, StrategicOutcome = AIStrategicOutcome.Progress, Turn = 1, BattleId = "Previous"
                }, config);
                reflection.Profile.Learn(new AIActionRecord
                {
                    ContextKey = AIActionFeatureExtractor.ContextKey(snapshot), ActionKey = AIActionFeatureExtractor.ActionKey(a, snapshot),
                    Reward = 10, StrategicOutcome = AIStrategicOutcome.Progress, Turn = 2, BattleId = "Previous"
                }, config);
                var method = typeof(AICommander).GetMethod("SelectBestAction", flags);
                var candidates = new List<AIAction> { b, a }; var failed = new HashSet<string>();
                AIAction Select() => (AIAction)method.Invoke(commander, new object[] { candidates, failed, null });
                Check("125 real commander selection uses learned modifier inside a tier", ReferenceEquals(Select(), a));
                var defense = new AIAction { ActionType = AIActionType.DefenseRepos, Score = -100, StrategicPriority = 0, APCost = 0 };
                candidates.Add(defense);
                Check("126 real commander gives survival priority precedence", ReferenceEquals(Select(), defense));
                failed.Add(defense.FailureKey);
                Check("37 learning cannot resurrect a failed action", ReferenceEquals(Select(), a));
                candidates.Remove(defense); failed.Clear(); config.EnableReflection = false;
                Check("124 real commander selects the original base-score winner with reflection off", ReferenceEquals(Select(), b));
                config.EnableReflection = true; config.ApplyLearningToSelection = false;
                Check("116 learn-only real commander preserves base-score decision", ReferenceEquals(Select(), b));
                config.ApplyLearningToSelection = true;
                var unit = f.Unit(); unit.direction = Direction.N; board.Refresh();
                var rotate = new AIAction { ActionType = AIActionType.Rotate, Unit = unit, TargetDirection = Direction.S,
                    Score = 4, StrategicPriority = 4, APCost = 0 };
                candidates.Clear(); candidates.Add(rotate); Select();
                bool executed = (bool)typeof(AICommander).GetMethod("ExecuteReflectedAction", flags)
                    .Invoke(commander, new object[] { rotate });
                Check("7/63 real executor completes action before reflection after-state", executed && unit.direction == Direction.S
                    && reflection.LastRecord != null && reflection.LastRecord.Before.ActorDirection == (int)Direction.N
                    && reflection.LastRecord.After.ActorDirection == (int)Direction.S && reflection.LastRecord.MeaningfulProgress);
                Check("113 developer-side commander records without touching persistent player learning",
                    reflection.Profile.Entries.Count > 0 && !System.IO.Directory.Exists(memoryOnlyRoot));
            }
            finally { UnityEngine.Object.DestroyImmediate(config); }
        }
    }

    static void UpkeepAndForecast(GameSystems systems)
    {
        foreach (var team in new[] { Team.Enemy, Team.Player })
        using (var f = new EconomyR2Tests.Fixture(systems, team))
        {
            f.Rules.breadPerCitizen = 0; f.Resources.Citizen = 5;
            int expected = 0;
            foreach (int level in new[] { 1, 9, 10, 20 })
            {
                var unit = f.Unit(); unit.Level = level;
                expected += 1 + level / 10;
            }
            var board = f.Board(); var forecast = new StrategicEconomyForecast { ForecastTurns = 1 };
            var diagnosis = forecast.Diagnose(board, false);
            int bread = f.Resources.Bread, iron = f.Resources.Iron;
            Check("139 forecast uses Lv1 and 10-level boundaries for " + team,
                Near(diagnosis.Get(ResourceKind.Bread).MandatoryDemandPerTurn, expected)
                && Near(diagnosis.Get(ResourceKind.Iron).MandatoryDemandPerTurn, expected));
            f.Economy.ProcessTurn(team);
            Check("136/139 real army pays matching upkeep for " + team,
                f.Resources.Bread == bread - expected && f.Resources.Iron == iron - expected);
            Check("139 forecast ending bread/iron equals real economy for " + team,
                Near(diagnosis.Get(ResourceKind.Bread).ProjectedStock, f.Resources.Bread)
                && Near(diagnosis.Get(ResourceKind.Iron).ProjectedStock, f.Resources.Iron));
            foreach (var unit in board.AliveEnemyUnits)
                Check("139 fully paid Lv" + unit.Level + " has no unpaid turn", unit.UpkeepUnpaidTurns == 0);
        }
    }

    static void MaintenancePrecedesProduction(GameSystems systems)
    {
        using (var f = new EconomyR2Tests.Fixture(systems))
        {
            f.Rules.breadPerCitizen = 0; f.Resources.Citizen = 5;
            f.Resources.Bread = f.Resources.Iron = 0;
            var unit = f.Unit(); unit.Level = 1;
            f.Building(f.Definition(FacilityKind.Bakery, bread: 10));
            f.Building(f.Definition(FacilityKind.Mine, iron: 10));
            var forecast = new StrategicEconomyForecast { ForecastTurns = 1 };
            var diagnosis = forecast.Diagnose(f.Board(), false);
            Check("141 forecast cannot use later production to pay earlier upkeep",
                diagnosis.Forecast.UpkeepPaymentFailed && diagnosis.Forecast.FirstTurnMandatoryFailureMask != 0);
            f.Economy.ProcessTurn(f.Team);
            Check("141 produced stock does not retroactively clear unpaid upkeep",
                unit.UpkeepUnpaidTurns == 1 && f.Resources.Bread == 10 && f.Resources.Iron == 10);
            Check("141 forecast remains equal after recording unpaid turn",
                Near(diagnosis.Get(ResourceKind.Bread).ProjectedStock, f.Resources.Bread)
                && Near(diagnosis.Get(ResourceKind.Iron).ProjectedStock, f.Resources.Iron));
        }
    }

    static void ProductionDefinitions(GameSystems systems)
    {
        foreach (var entry in FacilityData.Table)
        {
            var recipe = FacilityData.GetLevel(entry.Key, 1);
            if (!recipe.HasProduction) continue;
            using (var f = new EconomyR2Tests.Fixture(systems))
            {
                f.Rules.breadPerCitizen = 0; f.Resources.Citizen = 5;
                // Probability bonuses are disabled only in this temporary definition so exact guaranteed output is testable.
                recipe.BonusChance1 = recipe.BonusChance2 = 0;
                var definition = f.Definition(entry.Key); definition.levels[0] = recipe;
                f.Building(definition);
                var before = JsonUtility.ToJson(f.Resources);
                var forecast = new StrategicEconomyForecast { ForecastTurns = 1 };
                var prediction = forecast.Diagnose(f.Board(), false);
                Check("140 forecast leaves source stock intact for " + entry.Key, before == JsonUtility.ToJson(f.Resources));
                f.Economy.ProcessTurn(f.Team);
                for (int i = 0; i < 7; i++)
                {
                    var kind = (ResourceKind)i;
                    Check("140 production definition, runtime and forecast agree " + entry.Key + "/" + kind,
                        Near(prediction.Get(kind).ProjectedStock, Stock(f.Resources, kind)));
                }
            }
        }
    }

    static void ProductionIntervalsAndOverrides(GameSystems systems)
    {
        using (var f = new EconomyR2Tests.Fixture(systems))
        {
            f.Rules.breadPerCitizen = 0; f.Resources.Citizen = 5; f.Resources.Wood = 0;
            var definition = f.Definition(FacilityKind.LoggingCamp, wood: 1);
            var original = definition.levels[0]; original.ProductionIntervalTurns = 2; definition.levels[0] = original;
            f.Building(definition);
            for (int turn = 20; turn < 24; turn++)
            {
                f.State.GetNation(f.Team).TurnsAlive = turn;
                int initial = f.Resources.Wood;
                var prediction = new StrategicEconomyForecast { ForecastTurns = 1 }.Diagnose(f.Board(), false);
                f.Economy.ProcessTurn(f.Team);
                int expected = turn % 2 == 0 ? 1 : 0;
                Check("93 fractional-per-turn output is represented without truncation at turn " + turn,
                    f.Resources.Wood == initial + expected && Near(prediction.Get(ResourceKind.Wood).ProjectedStock, f.Resources.Wood));
            }
            Check("93 one resource per two turns totals two over four turns", f.Resources.Wood == 2);
            f.Rules.buildingProduction.applyAdjustments = true;
            f.Rules.buildingProduction.adjustments = new[] { new BuildingProductionAdjustment
            {
                buildingType = FacilityKind.LoggingCamp, definitionId = definition.definitionId,
                level = 1, output = new FacilityData.ProductionBundle { Wood = 3 }, intervalTurns = 1
            } };
            f.State.GetNation(f.Team).TurnsAlive = 24;
            var board = f.Board(); int wood = f.Resources.Wood;
            var predictionAdjusted = new StrategicEconomyForecast { ForecastTurns = 1 }.Diagnose(board, false);
            f.Economy.ProcessTurn(f.Team);
            Check("98 centralized authored output override reaches runtime and forecast",
                f.Resources.Wood == wood + 3 && Near(predictionAdjusted.Get(ResourceKind.Wood).ProjectedStock, f.Resources.Wood));
            Check("96 balance override never rewrites the original asset recipe", definition.levels[0].Output.Wood == 1
                && definition.levels[0].ProductionIntervalTurns == 2);
            var unrelated = f.Definition(FacilityKind.LoggingCamp, wood: 5);
            Check("91 authored identity prevents override leaking to a different building", unrelated.GetLevel(1).Output.Wood == 5);
            f.Rules.buildingProduction.adjustments = new[] { new BuildingProductionAdjustment
            {
                buildingType = FacilityKind.SubCrystal, level = 1,
                output = new FacilityData.ProductionBundle { Wood = 99 }, intervalTurns = 3
            } };
            var crystalRecipe = new FacilityData.FacilityLevelData { Output = new FacilityData.ProductionBundle { Wood = 7 } };
            Check("95 separate crystal economy is not changed by building-production override",
                BuildingProductionBalance.Resolve(FacilityKind.SubCrystal, "", 1, crystalRecipe).Output.Wood == 7);
        }
    }
}
#endif
