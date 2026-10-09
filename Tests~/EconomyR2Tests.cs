using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>R2 regressions run against real board, recipes, construction and economy, without saving fixtures.</summary>
public static class EconomyR2Tests
{
    internal static void Check(string label, bool passed)
    {
        if (!passed) throw new InvalidOperationException("[EconomyR2] FAIL " + label);
        Debug.Log("[EconomyR2] PASS " + label);
    }

    public static void PlayMode(GameSystems systems)
    {
        ProductionExpansion(systems);
        EarlyWarning(systems);
        ChainsAndGuaranteedSupply(systems);
        RecoveryGate(systems);
        EmergencyAndFog(systems);
        PlannerPaths(systems);
        RefreshAfterPurchase(systems);
        RecruitmentAndReserve(systems);
        Personality(systems);
        PerspectiveAndBudget(systems);
        Debug.Log("[EconomyR2] ECON-R2-01..11 ALL PASSED");
    }

    static void ProductionExpansion(GameSystems systems)
    {
        using (var f = new Fixture(systems))
        {
            f.Food(20); var camp = f.Definition(FacilityKind.LoggingCamp, wood: 5);
            f.Building(camp); var consumer = f.Unit(upkeepWood: 10);
            var board = f.Board(); var action = f.BuildAction(camp, board);
            var first = board.Governor.BuildAssessment(action, board);
            float secondScore = AIActionEvaluator.CalcBuildScorePublic(action, f.Personality, board, null);
            var wood = board.Governor.ProductionDemand.Get(ResourceKind.Wood);
            Check("ECON-R2-01 real recipe produces 5 against mandatory 10", Near(wood.ProductionPerTurn, 5) && Near(wood.MandatoryDemandPerTurn, 10));
            Check("ECON-R2-01 second camp improves a live deficit", first.ImprovesDeficit && first.NeedScore > 0);
            f.Building(camp); consumer.GrowthData.upkeepWood = 15; board.Refresh(); board.Governor.Evaluate(board);
            var third = board.Governor.BuildAssessment(action, board);
            float thirdScore = AIActionEvaluator.CalcBuildScorePublic(action, f.Personality, board, null);
            Check("ECON-R2-01 third camp remains eligible while demand expands", third.ImprovesDeficit && board.Governor.AllowExecution(action, board));
            Check("ECON-R2-01 equal deficits do not incur count-squared production penalty", thirdScore >= secondScore - 5);
            consumer.GrowthData.upkeepWood = 3; board.Refresh(); board.Governor.Evaluate(board);
            var surplus = board.Governor.BuildAssessment(action, board);
            float surplusScore = AIActionEvaluator.CalcBuildScorePublic(action, f.Personality, board, null);
            Check("ECON-R2-02 unnecessary third camp has overstock penalty", !surplus.ImprovesDeficit && surplus.OverstockPenalty > 0 && surplusScore < thirdScore - 20);
            var quarry = f.Definition(FacilityKind.Quarry, stone: 10); consumer.GrowthData.upkeepStone = 10;
            board.Refresh(); board.Governor.Evaluate(board);
            float neededScore = AIActionEvaluator.CalcBuildScorePublic(f.BuildAction(quarry, board), f.Personality, board, null);
            Check("ECON-R2-02 missing stone supply outranks wood surplus", neededScore > surplusScore);
        }
    }

    static void EarlyWarning(GameSystems systems)
    {
        using (var f = new Fixture(systems))
        {
            f.Resources.Bread = 80;
            var board = f.Board(); var bread = board.Governor.ProductionDemand.Get(ResourceKind.Bread);
            Check("ECON-R2-03 bread 80 with net -5 is diagnosed before exhaustion", Near(bread.Stock, 80) && Near(bread.NetPerTurn, -5)
                && board.Governor.ProductionDemand.HasWarningDeficit && board.Governor.ProductionDemand.State >= EconomicState.Warning);
            Check("ECON-R2-03 high stock does not erase production deficit", bread.ProductionDeficit >= 5 && !EconomyHelper.IsEconomySufficient(board));
            string before = JsonUtility.ToJson(f.Resources); var forecast = new StrategicEconomyForecast();
            forecast.Simulate(board); forecast.Diagnose(board, false);
            Check("forecast and demand diagnosis leave actual resource state untouched", before == JsonUtility.ToJson(f.Resources));
        }
    }

    static void RecoveryGate(GameSystems systems)
    {
        using (var f = new Fixture(systems))
        {
            f.Resources.Bread = 1; var bakery = f.Food(0, 10); var wall = f.Definition(FacilityKind.StoneWall);
            var board = f.Board(); var defense = f.BuildAction(wall, board); defense.Score = 100000;
            var recovery = f.BuildAction(bakery, board); var actions = new List<AIAction> { defense, recovery };
            board.Governor.Filter(actions, board);
            Check("ECON-R2-04 safe crystal in collapse enters economic recovery", board.Governor.Mode == StrategicMode.EconomicRecovery);
            Check("ECON-R2-04 wall cannot win through a huge raw score", !actions.Contains(defense)
                && defense.StrategicRejectReason == "economic_recovery_blocks_military_build");
            Check("ECON-R2-04 bakery recovery receives P1/P2", actions.Contains(recovery) && recovery.StrategicPriority <= 2);
            Check("execution gate repeats the military restriction", !board.Governor.AllowExecution(f.BuildAction(wall, board), board));
            var expensive = f.Definition(FacilityKind.LoggingCamp, wood: 20); expensive.buildCost = new FacilityData.ResourceCost(water: 395);
            f.Unit(upkeepWood: 20, upkeepWater: 40); board.Refresh(); board.Governor.Evaluate(board);
            var purchase = f.BuildAction(expensive, board);
            Check("build draining a mandatory resource is refused", !board.Governor.AllowExecution(purchase, board)
                && purchase.StrategicRejectReason == "build_causes_forecast_failure");
        }
    }

    static void ChainsAndGuaranteedSupply(GameSystems systems)
    {
        using (var f = new Fixture(systems))
        {
            f.Resources.Water = f.Resources.Wheat = 0; f.Resources.Bread = 1;
            var field = f.Definition(FacilityKind.Field, wheat: 5); var fieldRecipe = field.levels[0]; fieldRecipe.Input.Water = 2; field.levels[0] = fieldRecipe; f.Building(field);
            var bakery = f.Definition(FacilityKind.Bakery, bread: 5); var breadRecipe = bakery.levels[0]; breadRecipe.Input.Wheat = 3; breadRecipe.Input.Water = 3; bakery.levels[0] = breadRecipe; f.Building(bakery);
            var well = f.Definition(FacilityKind.Well, water: 8); var board = f.Board();
            var recovery = board.Governor.BuildAssessment(f.BuildAction(well, board), board);
            var downstream = board.Governor.BuildAssessment(f.BuildAction(bakery, board), board);
            Check("stopped bread chain preserves upstream mandatory input demand", board.Governor.ProductionDemand.ImportantProductionStopped
                && board.Governor.ProductionDemand.Get(ResourceKind.Water).MandatoryDemandPerTurn >= 5);
            Check("upstream well restores the critical bread chain", recovery.ImprovesCritical && recovery.ChainRecoveryScore > 0);
            Check("input-starved bakery cannot claim unavailable bread output", !downstream.ImprovesDeficit);
        }
        using (var f = new Fixture(systems))
        {
            f.Resources.Bread = 80; var lottery = f.Definition(FacilityKind.Bakery); var recipe = lottery.levels[0];
            recipe.BonusChance1 = .99f; recipe.BonusOutput1.Bread = 100; lottery.levels[0] = recipe; f.Building(lottery);
            var board = f.Board();
            Check("chance-only production is not promised to mandatory consumers", Near(board.Governor.ProductionDemand.Get(ResourceKind.Bread).ProductionPerTurn, 0)
                && board.Governor.ProductionDemand.HasWarningDeficit);
        }
    }

    static void EmergencyAndFog(GameSystems systems)
    {
        using (var f = new Fixture(systems))
        {
            f.Resources.Bread = 1; f.Food(0, 10); var wall = f.Definition(FacilityKind.StoneWall);
            var cells = f.AdjacentCells(); f.Crystal.transform.position = cells[0]; f.SetOwnCrystalPosition(cells[0]);
            f.Crystal.HP = 10; f.Crystal.DEF = 0;
            var foe = f.Opponent(cells[2], 50); var board = f.Board();
            Check("Fog fixture keeps unobserved real actor out of the observed board", !board.AlivePlayerUnits.Contains(foe));
            var hiddenWall = f.BuildAction(wall, board); hiddenWall.TargetPos = cells[1];
            Check("Fog threat outside observation cannot unlock emergency construction", board.Governor.Mode == StrategicMode.EconomicRecovery
                && !board.Governor.AllowExecution(hiddenWall, board));
            board.AlivePlayerUnits.Add(foe); board.Governor = new AIStrategicGovernor(); board.Governor.Evaluate(board);
            var neededWall = f.BuildAction(wall, board); neededWall.TargetPos = cells[1];
            var candidates = new List<AIAction> { neededWall }; board.Governor.Filter(candidates, board);
            Check("ECON-R2-05 observed next attack threatens the crystal", board.Governor.Mode == StrategicMode.EmergencyDefense
                && board.Governor.IncomingDamage(board, f.Crystal, cells[0], null) >= f.Crystal.HP);
            Check("ECON-R2-05 necessary emergency wall survives economy gate at P0/P1", candidates.Contains(neededWall)
                && neededWall.StrategicPriority <= 1 && board.Governor.AllowExecution(neededWall, board));
            var remote = f.BuildAction(wall, board); remote.TargetPos = cells[0] + Vector3.forward * 20;
            Check("emergency does not authorize unrelated remote military construction", !board.Governor.AllowExecution(remote, board));
            board.AlivePlayerUnits.Clear(); board.Governor = new AIStrategicGovernor(); board.Governor.Evaluate(board);
            Check("damaged crystal alone does not trigger emergency defense", board.Governor.Mode == StrategicMode.EconomicRecovery);
        }
    }

    static void PlannerPaths(GameSystems systems)
    {
        using (var f = new Fixture(systems))
        {
            f.Resources.Bread = 1; f.Food(0, 10); var wall = f.Definition(FacilityKind.StoneWall);
            f.Catalog.buildings.Clear(); f.Catalog.buildings.Add(wall);
            var board = f.Board(); board.AffordableBuildings.Clear(); board.AffordableBuildings.Add(FacilityKind.StoneWall);
            int before = f.MilitaryCount; int ap = f.State.GetAP(f.Team);
            AITurnBudget.Begin(2000);
            int count = (int)typeof(AIBuildPlanner).GetMethod("TryScoreSingleBuild", Fixture.Private).Invoke(f.Planner(), new object[] { board, TurnStrategy.EconomyBuild, 20 });
            Check("ECON-R2-06 score phase cannot execute its only military candidates", count == 0 && f.MilitaryCount == before && f.State.GetAP(f.Team) == ap);
            AITurnBudget.Begin(2000); f.Planner().TryEarlyBuildPhase(board, TurnStrategy.EconomyBuild, 20);
            Check("ECON-R2-06 complete early path respects military gate", f.MilitaryCount == before);
        }
        using (var f = new Fixture(systems))
        {
            f.Resources.Bread = 1; f.Food(0, 10); var wall = f.Definition(FacilityKind.StoneWall);
            f.Catalog.buildings.Clear(); f.Catalog.buildings.Add(wall);
            var board = f.Board(); AITurnBudget.Begin(2000);
            typeof(AIBuildPlanner).GetMethod("ForceDirectBuild", Fixture.Private).Invoke(f.Planner(), new object[] { board, 40 });
            Check("ECON-R2-07 direct fallback does not bypass economic crisis", f.MilitaryCount == 0);
            AITurnBudget.Begin(2000); f.Planner().TryLateBuildPhase(board, 0, 40);
            Check("late fallback shares the economic construction gate", f.MilitaryCount == 0);
        }
        using (var f = new Fixture(systems))
        {
            // The legacy direct build order contains no walls. Catch its actual safety bypass by making
            // an affordable LoggingCamp purchase drain the input of an existing, working bread recipe.
            var bakery = f.Food(20); var recipe = bakery.levels[0]; recipe.Input.Water = 30; bakery.levels[0] = recipe;
            f.Unit(upkeepWood: 10); f.Resources.Wood = 40; f.Resources.Stone = f.Resources.Iron = f.Resources.MagicOre = 0;
            f.Resources.Water = 25; var board = f.Board();
            var danger = new AIAction { ActionType = AIActionType.Build, Facility = FacilityKind.LoggingCamp,
                APCost = FacilityData.Table[FacilityKind.LoggingCamp].APCost, TargetPos = board.BuildablePositions[0] };
            Check("ECON-R2-07 unsafe fallback fixture is affordable and improves wood supply", board.AffordableBuildings.Contains(FacilityKind.LoggingCamp)
                && board.Governor.BuildAssessment(danger, board).ImprovesDeficit);
            Check("ECON-R2-07 purchase would introduce first-turn bread input failure", !board.Governor.AllowExecution(danger, board)
                && danger.StrategicRejectReason == "build_causes_forecast_failure");
            int ap = f.State.GetAP(f.Team); string resources = JsonUtility.ToJson(f.Resources);
            AITurnBudget.Begin(2000); int count = (int)typeof(AIBuildPlanner).GetMethod("ForceDirectBuild", Fixture.Private)
                .Invoke(f.Planner(), new object[] { board, 40 });
            Check("ECON-R2-07 ForceDirectBuild preserves bread input and AP", count == 0
                && f.Builder.GetBuildingCount(f.Team, FacilityKind.LoggingCamp) == 0 && resources == JsonUtility.ToJson(f.Resources) && f.State.GetAP(f.Team) == ap);
        }
    }

    static void RefreshAfterPurchase(GameSystems systems)
    {
        using (var f = new Fixture(systems))
        {
            f.Resources.Bread = 1; f.Rules.aiEconomy.plannedDemandWeight = 0; var bakery = f.Food(0, 10);
            f.Catalog.buildings.Clear(); f.Catalog.buildings.Add(bakery);
            var board = f.Board(); board.AffordableBuildings.Clear(); int generation = board.Generation;
            AITurnBudget.Begin(2000);
            // Test one purchase at a time: a second legal foundation project can change population and food demand.
            var purchase = typeof(AIBuildPlanner).GetMethod("TryScoreSingleBuild", Fixture.Private);
            var planner = f.Planner();
            int count = (int)purchase.Invoke(planner, new object[] { board, TurnStrategy.EconomyBuild, 20 });
            Check("ECON-R2-08 bakery is actually purchased through score planner", count == 1 && f.Builder.GetBuildingCount(f.Team, FacilityKind.Bakery) == 1);
            Check("ECON-R2-08 purchase refreshes board generation and demand", board.Generation > generation
                && !board.Governor.BuildAssessment(f.BuildAction(bakery, board), board).ImprovesDeficit);
            // Restrict this assertion to the bakery; baseline buildings are tested separately in foundation regressions.
            board.AffordableBuildings.Clear(); int ap = f.State.GetAP(f.Team); string resources = JsonUtility.ToJson(f.Resources);
            int second = (int)purchase.Invoke(planner, new object[] { board, TurnStrategy.EconomyBuild, 20 });
            Check("ECON-R2-08 same-turn stale deficit cannot buy a second bakery", second == 0
                && f.Builder.GetBuildingCount(f.Team, FacilityKind.Bakery) == 1 && f.State.GetAP(f.Team) == ap
                && resources == JsonUtility.ToJson(f.Resources));
            // Building costs can consume citizens, so use the purchased recipe and the actual population.
            // The real turn also spends one bread when a vacant citizen slot is refilled.
            int bread = f.Resources.Bread, citizens = f.Resources.Citizen;
            int producedBread = bakery.GetLevel(1).Output.Bread;
            f.Economy.ProcessTurn(f.Team);
            int growthBread = Mathf.Max(0, f.Resources.Citizen - citizens);
            Check("actual economy applies purchased bread output and current citizen costs", f.Resources.Bread ==
                bread + producedBread - growthBread - f.Resources.Citizen * EconomySystem.CitizenBreadCost);
        }
    }

    static void RecruitmentAndReserve(GameSystems systems)
    {
        using (var f = new Fixture(systems))
        {
            f.Resources.Bread = 25; f.Food(5);
            // Normal upkeep is now fixed to Bread/Iron 1 at Lv1; ten real units preserve this army-demand regression.
            for (int i = 0; i < 10; i++) { var unit = f.Unit(); unit.Level = 1; }
            var board = f.Board(); var bread = board.Governor.ProductionDemand.Get(ResourceKind.Bread);
            Check("ECON-R2-09 expanded army is diagnosed before a third-turn food failure", bread.NetPerTurn <= -10
                && bread.ProductionDeficit > 0 && board.Governor.ProductionDemand.HasCriticalDeficit);
            var summon = new AIAction { ActionType = AIActionType.Summon, SummonKind = Kind.Knight, SummonDefinition = f.UnitDefinition(costBread: 10) };
            Check("ECON-R2-09 forecast rejects recruitment before stocks reach zero", !board.Governor.AllowExecution(summon, board)
                && summon.StrategicRejectReason == "bread_forecast_fail");
        }
        using (var f = new Fixture(systems))
        {
            f.Resources.Bread = 40; var board = f.Board();
            board.Governor = new AIStrategicGovernor { BreadReserveTurns = 8 };
            var summon = new AIAction { ActionType = AIActionType.Summon, SummonKind = Kind.Knight, SummonDefinition = f.UnitDefinition(costBread: 5) };
            Check("configured eight-turn bread reserve retains bread_reserve_fail", !board.Governor.AllowExecution(summon, board)
                && summon.StrategicRejectReason == "bread_reserve_fail");
        }
        using (var f = new Fixture(systems))
        {
            f.Food(20); f.Resources.Wood = 100; f.Unit(upkeepWood: 10); var board = f.Board();
            var data = f.UnitDefinition(); data.costWood = 95;
            var summon = new AIAction { ActionType = AIActionType.Summon, SummonKind = Kind.Knight, SummonDefinition = data };
            Check("mandatory non-food upkeep retains upkeep_forecast_fail", !board.Governor.AllowExecution(summon, board)
                && summon.StrategicRejectReason == "upkeep_forecast_fail");
        }
    }

    static void Personality(GameSystems systems)
    {
        using (var f = new Fixture(systems))
        {
            f.Food(20); var board = f.Board(); var wall = f.BuildAction(f.Definition(FacilityKind.StoneWall), board);
            var defense = new AIPersonality(MajorPersonality.Intellect, new PersonalityTraits { Defense = 250, Development = 10, Caution = 10, Command = 10, Obsession = 10, Tactics = 10 });
            var growth = new AIPersonality(MajorPersonality.Intellect, new PersonalityTraits { Defense = 10, Development = 10, Caution = 250, Command = 10, Obsession = 10, Tactics = 10 });
            var cells = f.AdjacentCells(); var enemy = f.Opponent(cells[2], 10); board.AlivePlayerUnits.Add(enemy);
            board.Governor = new AIStrategicGovernor(); board.Governor.Evaluate(board);
            float a = AIActionEvaluator.CalcBuildScorePublic(wall, defense, board, null), b = AIActionEvaluator.CalcBuildScorePublic(wall, growth, board, null);
            Check("ECON-R2-10 healthy defense personality retains greater wall value", a > b && board.Governor.AllowExecution(wall, board));
            f.Resources.Bread = 1; foreach (var s in f.Buildings) if (s.facilityKind == FacilityKind.Bakery) s.HP = 0;
            enemy.transform.position += Vector3.forward * 30; board.Refresh(); board.Governor.Evaluate(board);
            wall = f.BuildAction(wall.FacilityDefinition, board); wall.Score = a + 100000;
            var actions = new List<AIAction> { wall }; board.Governor.Filter(actions, board);
            Check("ECON-R2-11 defense bonus cannot override collapse with remote enemies", actions.Count == 0
                && wall.StrategicRejectReason == "economic_recovery_blocks_military_build");
        }
    }

    static void PerspectiveAndBudget(GameSystems systems)
    {
        using (var f = new Fixture(systems, Team.Player))
        {
            f.Resources.Bread = 80; var board = f.Board();
            Check("player AI perspective diagnoses its actual own resources", ReferenceEquals(board.EnemyResources, f.State.PlayerResources)
                && Near(board.Governor.ProductionDemand.Get(ResourceKind.Bread).NetPerTurn, -5));
            f.Resources.Bread = 1; f.Food(0, 10); board.Refresh(); var wall = f.BuildAction(f.Definition(FacilityKind.StoneWall), board);
            Check("player perspective applies the same economic gate", !board.Governor.AllowExecution(wall, board));
        }
        using (var f = new Fixture(systems))
        {
            f.Resources.Bread = 1; f.Food(0, 10); var board = f.Board(); int buildings = f.Buildings.Count;
            AITurnBudget.Begin(0); int built = f.Planner().TryEarlyBuildPhase(board, TurnStrategy.EconomyBuild, 40);
            Check("expired AITurnBudget cannot trigger an unbounded fallback build", built == 0 && f.Buildings.Count == buildings);
            var watch = Stopwatch.StartNew(); var forecast = new StrategicEconomyForecast();
            for (int i = 0; i < 40; i++) forecast.Diagnose(board, false);
            watch.Stop();
            Check("bounded horizon diagnosis remains within a one-second regression budget", watch.Elapsed.TotalMilliseconds < 1000);
            Debug.Log($"[EconomyR2] diagnosis40Ms={watch.Elapsed.TotalMilliseconds:F2}");
        }
    }

    static bool Near(float a, float b) => Mathf.Abs(a - b) < .05f;

    /// <summary>New in-memory faction systems; only map/territory geometry is shared with the live scene.</summary>
    internal sealed class Fixture : IDisposable
    {
        internal const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static FieldInfo StaticField(System.Type type, string name) => type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
        readonly GameSystems live;
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        readonly BuildSystem oldBuildRef;
        readonly UnityEngine.Random.State random;
        readonly string oldPlayerNation, oldEnemyNation;
        readonly object oldCatalog, oldCatalogLoaded, oldRules, oldRulesLoaded;
        readonly object oldClock, oldWallClock, oldLimit;
        readonly bool clockRunning, wallRunning;
        readonly GameObject root;
        bool disposed;
        int syntheticSlot;
        public readonly Team Team;
        public readonly FactionState State;
        public readonly APSystem AP;
        public readonly BuildSystem Builder;
        public readonly UnitSetting Units;
        public readonly CrystalSystem Crystals;
        public readonly EconomySystem Economy;
        public readonly FacilityAuthoringCatalog Catalog;
        public readonly GameAuthoringRules Rules;
        public readonly Status Crystal;
        public AIPersonality Personality = new AIPersonality(MajorPersonality.Growth, 1701);
        public FactionState.ResourceData Resources => State.GetResources(Team);
        public List<Status> Buildings { get { var result = new List<Status>(); Builder.GetBuildingParent(Team).GetComponentsInChildren(false, result); return result; } }
        public int MilitaryCount { get { int count = 0; foreach (var s in Buildings) if (FacilityData.IsWall(s.facilityKind) || FacilityData.IsOffensive(s.facilityKind)) count++; return count; } }

        public Fixture(GameSystems systems, Team actorTeam = global::Team.Enemy)
        {
            live = systems; Team = actorTeam; oldBuildRef = live.MoveGenerator.BuildSystemRef; random = UnityEngine.Random.state;
            oldPlayerNation = JsonUtility.ToJson(live.FactionState.PlayerNation); oldEnemyNation = JsonUtility.ToJson(live.FactionState.EnemyNation);
            oldCatalog = StaticField(typeof(FacilityAuthoringCatalog), "loaded").GetValue(null);
            oldCatalogLoaded = StaticField(typeof(FacilityAuthoringCatalog), "didLoad").GetValue(null);
            oldRules = StaticField(typeof(GameAuthoringRules), "loaded").GetValue(null);
            oldRulesLoaded = StaticField(typeof(GameAuthoringRules), "didLoad").GetValue(null);
            oldClock = StaticField(typeof(AITurnBudget), "clock").GetValue(null); oldWallClock = StaticField(typeof(AITurnBudget), "wallClock").GetValue(null);
            oldLimit = StaticField(typeof(AITurnBudget), "limit").GetValue(null);
            clockRunning = (oldClock as Stopwatch)?.IsRunning == true; wallRunning = (oldWallClock as Stopwatch)?.IsRunning == true;
            (oldClock as Stopwatch)?.Stop(); (oldWallClock as Stopwatch)?.Stop();
            root = new GameObject("Economy R2 temporary fixture"); owned.Add(root);
            try
            {
                Catalog = ScriptableObject.CreateInstance<FacilityAuthoringCatalog>(); owned.Add(Catalog);
                Rules = ScriptableObject.CreateInstance<GameAuthoringRules>(); owned.Add(Rules); Rules.applyRules = true;
                StaticField(typeof(FacilityAuthoringCatalog), "loaded").SetValue(null, Catalog); StaticField(typeof(FacilityAuthoringCatalog), "didLoad").SetValue(null, true);
                StaticField(typeof(GameAuthoringRules), "loaded").SetValue(null, Rules); StaticField(typeof(GameAuthoringRules), "didLoad").SetValue(null, true);
                State = Child("Factions").AddComponent<FactionState>();
                State.PlayerNation = Child("Player nation").AddComponent<NationState>(); State.EnemyNation = Child("Enemy nation").AddComponent<NationState>();
                foreach (var team in new[] { global::Team.Player, global::Team.Enemy })
                {
                    var res = State.GetResources(team); res.Wood = res.Stone = res.Water = res.Iron = res.MagicOre = res.Wheat = res.Bread = 400; res.Citizen = 5;
                    State.GetNation(team).TurnsAlive = 20; State.GetNation(team).SubCrystals = 0;
                    State.GetAPData(team).Reset = 40; State.SetAP(team, 40);
                }
                AP = Child("AP").AddComponent<APSystem>(); AP.Init(State);
                Units = Child("Units").AddComponent<UnitSetting>(); Units.PlayerUnit = Child("Player units").transform; Units.EnemyUnit = Child("Enemy units").transform;
                Crystals = Child("Crystals").AddComponent<CrystalSystem>(); Crystals.Playercrystal = Child("Player crystal").transform; Crystals.Enemycrystal = Child("Enemy crystal").transform;
                Crystals.PCP = live.CrystalSystem.PCP; Crystals.ECP = live.CrystalSystem.ECP;
                Crystal = Actor("Fixture crystal", Team == global::Team.Player ? Crystals.Playercrystal : Crystals.Enemycrystal,
                    Team, Kind.Crystal, Team == global::Team.Player ? Crystals.PCP : Crystals.ECP); Crystal.type = Type.Building; Crystal.HP = Crystal.MaxHP = 15000;
                Builder = Child("Builder").AddComponent<BuildSystem>(); Builder.PlayerBuildingParent = Child("Player buildings").transform; Builder.EnemyBuildingParent = Child("Enemy buildings").transform;
                Builder.Init(UnityEngine.Object.FindFirstObjectByType<TurnGenerator>(), live.TerritorySystem, AP, State, live.MoveGenerator, live.MapCreate);
                Economy = Child("Economy").AddComponent<EconomySystem>(); Economy.Init(Builder, State, Units, Crystals);
                AITurnBudget.Begin(2000);
            }
            catch { Dispose(); throw; }
        }

        GameObject Child(string name) { var go = new GameObject(name); go.transform.SetParent(root.transform); return go; }
        Status Actor(string name, Transform parent, Team team, Kind kind, Vector3 at)
        {
            var go = Child(name); go.transform.SetParent(parent); go.transform.position = at;
            var status = go.AddComponent<Status>(); status.team = team; status.kind = kind; status.type = Type.Unit; status.HP = status.MaxHP = 100; status.Level = 1;
            return status;
        }
        public FacilityDefinitionData Definition(FacilityKind kind, int wood = 0, int stone = 0, int bread = 0, int water = 0, int wheat = 0, int iron = 0, int magic = 0)
        {
            var d = ScriptableObject.CreateInstance<FacilityDefinitionData>(); owned.Add(d);
            d.definitionId = "economy.r2.fixture." + kind + "." + owned.Count; d.displayName = "R2 fixture " + kind; d.behaviourKind = kind; d.buildAP = 2;
            d.buildCost = new FacilityData.ResourceCost(wood: 2, stone: 2);
            d.levels = new[] { new FacilityData.FacilityLevelData { HP = 100, Output = new FacilityData.ProductionBundle { Wood = wood, Stone = stone, Bread = bread, Water = water, Wheat = wheat, Iron = iron, MagicOre = magic } } };
            Catalog.buildings.Add(d); return d;
        }
        public Status Building(FacilityDefinitionData definition)
        {
            var status = Actor("Existing " + definition.behaviourKind, Builder.GetBuildingParent(Team), Team,
                FacilityData.ToUnitKind(definition.behaviourKind), new Vector3(-200 - syntheticSlot++, 0, -200));
            status.type = Type.Building; status.facilityKind = definition.behaviourKind; status.AuthoredFacility = definition; status.authoredFacilityId = definition.definitionId;
            return status;
        }
        public FacilityDefinitionData Food(int currentBread, int candidateBread = 10)
        {
            Building(Definition(FacilityKind.Well, water: 20)); Building(Definition(FacilityKind.Field, wheat: 20));
            var bakery = Definition(FacilityKind.Bakery, bread: currentBread > 0 ? currentBread : candidateBread);
            if (currentBread > 0) Building(bakery); return bakery;
        }
        public UnitData UnitDefinition(int upkeepWood = 0, int upkeepBread = 0, int upkeepWater = 0, int costBread = 0)
        {
            var data = ScriptableObject.CreateInstance<UnitData>(); owned.Add(data); data.kind = Kind.Knight; data.baseHP = 100;
            data.upkeepWood = upkeepWood; data.upkeepBread = upkeepBread; data.upkeepWater = upkeepWater; data.costBread = costBread; return data;
        }
        public Status Unit(int upkeepWood = 0, int upkeepBread = 0, int upkeepWater = 0)
        {
            var unit = Actor("Fixture upkeep unit", Team == global::Team.Player ? Units.PlayerUnit : Units.EnemyUnit, Team,
                Kind.Knight, new Vector3(-300 - syntheticSlot++, 0, -300)); unit.Level = 6; unit.GrowthData = UnitDefinition(upkeepWood, upkeepBread, upkeepWater); return unit;
        }
        public Status Opponent(Vector3 position, int atk)
        {
            var opposite = Team == global::Team.Player ? global::Team.Enemy : global::Team.Player;
            var foe = Actor("Fixture observed threat", opposite == global::Team.Player ? Units.PlayerUnit : Units.EnemyUnit, opposite, Kind.Knight, position);
            foe.ATK = atk; foe.GrowthData = UnitDefinition();
            var profile = ScriptableObject.CreateInstance<BoardActionProfile>(); owned.Add(profile); foe.GrowthData.actionProfile = profile;
            profile.actionType = ActorActionType.Stationary; profile.attack.useCustom = true;
            // Relative X=-2,-1 on the profile's 7x7 board; no offscreen movement knowledge is needed.
            profile.attack.SetCell(1, 3, true); profile.attack.SetCell(2, 3, true); return foe;
        }
        public AIBoardState Board(int turn = 20)
        {
            var b = new AIBoardState(live.MoveGenerator, live.AttackGenerator, AP, Units, Crystals, null, Builder, null, State, null, turn, null, Team);
            b.MapCreate = live.MapCreate; b.ReconThreatLevel = 15; b.Governor = new AIStrategicGovernor(); b.Governor.Evaluate(b); return b;
        }
        public AIAction BuildAction(FacilityDefinitionData d, AIBoardState board)
        {
            Check("fixture has real legal build space", board.BuildablePositions.Count > 0);
            return new AIAction { ActionType = AIActionType.Build, Facility = d.behaviourKind, FacilityDefinition = d, APCost = d.buildAP, TargetPos = board.BuildablePositions[0] };
        }
        public AIBuildPlanner Planner()
        {
            var executor = new AIActionExecutor(UnityEngine.Object.FindFirstObjectByType<TurnGenerator>(), live.MoveGenerator, live.AttackGenerator,
                live.BattleSystem, AP, live.SkillSystem, null, Builder, null, null, Team);
            return new AIBuildPlanner(AP, State, executor, Personality, null);
        }
        public Vector3[] AdjacentCells()
        {
            foreach (var cell in live.MapCreate.SetPos)
                if (live.MapCreate.TryGetHeight((int)cell.x + 1, (int)cell.z, out float y1)
                    && live.MapCreate.TryGetHeight((int)cell.x + 2, (int)cell.z, out float y2)
                    && Mathf.Abs(cell.y - y1) < .01f && Mathf.Abs(cell.y - y2) < .01f)
                    return new[] { cell, cell + Vector3.right, cell + Vector3.right * 2 };
            throw new InvalidOperationException("Economy R2 fixture needs three adjacent level cells");
        }
        public void SetOwnCrystalPosition(Vector3 position) { if (Team == global::Team.Player) Crystals.PCP = position; else Crystals.ECP = position; }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            try
            {
                if (root != null) foreach (var actor in root.GetComponentsInChildren<Status>(true)) UnitRegistry.Instance?.Unregister(actor);
                live.MoveGenerator.BuildSystemRef = oldBuildRef;
                StaticField(typeof(FacilityAuthoringCatalog), "loaded").SetValue(null, oldCatalog); StaticField(typeof(FacilityAuthoringCatalog), "didLoad").SetValue(null, oldCatalogLoaded);
                StaticField(typeof(GameAuthoringRules), "loaded").SetValue(null, oldRules); StaticField(typeof(GameAuthoringRules), "didLoad").SetValue(null, oldRulesLoaded);
                JsonUtility.FromJsonOverwrite(oldPlayerNation, live.FactionState.PlayerNation); JsonUtility.FromJsonOverwrite(oldEnemyNation, live.FactionState.EnemyNation);
            }
            finally
            {
                for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
                StaticField(typeof(AITurnBudget), "clock").SetValue(null, oldClock); StaticField(typeof(AITurnBudget), "wallClock").SetValue(null, oldWallClock); StaticField(typeof(AITurnBudget), "limit").SetValue(null, oldLimit);
                if (clockRunning) (oldClock as Stopwatch)?.Start(); if (wallRunning) (oldWallClock as Stopwatch)?.Start();
                UnityEngine.Random.state = random; live.MoveGenerator.UnitPointCore(); live.RefreshVision();
            }
        }
    }
}
