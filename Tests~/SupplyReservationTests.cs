#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Real forecast/candidate scheduling: a funded warning must not reserve the army's AP.</summary>
public static class SupplyReservationTests
{
    static int passed;

    static void Check(string message, bool condition)
    {
        if (!condition) throw new InvalidOperationException("[SupplyReservation] FAIL " + message);
        passed++;
        Debug.Log("[SupplyReservation] PASS " + message);
    }

    public static void PlayMode(GameSystems systems)
    {
        passed = 0;
        foreach (var team in new[] { Team.Player, Team.Enemy })
        {
            VerifyFaction(systems, team);
            VerifyDiversifiedReserve(systems, team);
            VerifyRecoveryCompletion(systems, team);
            VerifyRecoveryDefenseAndBases(systems, team);
        }
        Debug.Log("[SupplyReservation] " + passed + " forecast/candidate/strategy checks passed");
    }

    static void VerifyFaction(GameSystems systems, Team team)
    {
        using (var f = new EconomyR2Tests.Fixture(systems, team))
        {
            f.Rules.aiEconomy.plannedDemandWeight = 0;
            f.Rules.aiEconomy.reserveTurns = 4;
            f.Rules.aiEconomy.forecastTurns = 4;
            f.Resources.Wood = f.Resources.Stone = f.Resources.Water = f.Resources.Iron = 200;
            f.Resources.Wheat = f.Resources.Bread = 200;
            f.Resources.Citizen = 0;
            f.Resources.MagicOre = 14;
            f.Food(20);
            f.Building(f.Definition(FacilityKind.LoggingCamp, wood: 10));
            f.Building(f.Definition(FacilityKind.Quarry, stone: 10));
            var uncertainSource = f.Definition(FacilityKind.Mine, iron: 10);
            var recipe = uncertainSource.levels[0];
            recipe.BonusChance1 = .5f;
            recipe.BonusOutput1.MagicOre = 4;
            uncertainSource.levels[0] = recipe;
            f.Building(uncertainSource);
            var consumer = f.Unit();
            consumer.GrowthData.useAuthoredUpkeep = true;
            consumer.GrowthData.upkeepMagic = 1;

            // A real affordable guaranteed alternative exists. The zero reserve must not be
            // explained by missing candidates, unaffordability or unavailable build locations.
            var guaranteedSource = f.Definition(FacilityKind.Mine, magic: 2);
            var board = f.Board(60);
            var governor = board.Governor;
            var investment = f.BuildAction(guaranteedSource, board);
            var magic = governor.ProductionDemand.Get(ResourceKind.MagicOre);
            Check(team + " reproduces a funded guarantee deficit with an existing uncertain source",
                governor.Economy.State == EconomicState.Warning && magic.NetPerTurn < 0
                && magic.ProjectedStock > magic.SafetyReserve
                && governor.ProductionDemand.PermanentProduction(ResourceKind.MagicOre) == 0
                && governor.ProductionDemand.PotentialProduction(ResourceKind.MagicOre) > magic.MandatoryDemandPerTurn
                && !governor.Economy.UpkeepPaymentFailed);
            Check(team + " preserves the complete insufficient-supply diagnosis", !EconomyHelper.IsEconomySufficient(board));
            Check(team + " ordinary supply investment remains an eligible candidate",
                governor.BuildAssessment(investment, board).ImprovesDeficit && governor.AllowExecution(investment, board));
            string stocks = JsonUtility.ToJson(f.Resources);
            int ap = f.State.GetAP(team);
            Check(team + " funded warning reserves no compulsory recovery AP", governor.GetRecoveryReserveAP(board) == 0);
            var planner = new TurnStrategyPlanner();
            var decision = planner.DecideStrategy(board, f.Personality, new AIThreatLevel(15), 60);
            Check(team + " funded warning does not force EconomyBuild", decision.Strategy != TurnStrategy.EconomyBuild
                && decision.Budget.ReservedAP == 0);
            Check(team + " scheduling reads do not spend resources or AP", stocks == JsonUtility.ToJson(f.Resources)
                && ap == f.State.GetAP(team));

            var cells = f.AdjacentCells();
            f.Opponent(cells[2], 1);
            // The economy-only fixture deliberately has no visibility. Supply a genuine
            // observed cell so Refresh cannot silently turn this contact test into a Fog test.
            var visionObject = new GameObject("Supply reservation isolated vision");
            visionObject.SetActive(false);
            visionObject.transform.SetParent(f.Crystal.transform);
            var vision = visionObject.AddComponent<VisionGenerator>();
            var visible = new HashSet<Vector3Int>();
            foreach (var cell in cells) visible.Add(GridHelper.ToGridXZ(cell));
            const BindingFlags privateFields = BindingFlags.Instance | BindingFlags.NonPublic;
            foreach (string name in new[] { "_playerVisionBox", "_enemyVisionBox", "_playerExplored", "_enemyExplored" })
                typeof(VisionGenerator).GetField(name, privateFields).SetValue(vision, new HashSet<Vector3Int>(visible));
            board = new AIBoardState(systems.MoveGenerator, systems.AttackGenerator, f.AP, f.Units,
                f.Crystals, vision, f.Builder, null, f.State, null, 60, null, team)
                { MapCreate = systems.MapCreate, ReconThreatLevel = 15, Governor = governor };
            governor.Evaluate(board);
            decision = planner.DecideStrategy(board, f.Personality, new AIThreatLevel(15), 60);
            Check(team + " principal contact remains actionable during a funded warning",
                board.HasPrincipalOpponentContact && decision.Strategy == TurnStrategy.ContactEngage
                && decision.Budget.ReservedAP == 0);

            f.Resources.MagicOre = 6;
            board.Refresh(); governor.Evaluate(board);
            magic = governor.ProductionDemand.Get(ResourceKind.MagicOre);
            Check(team + " forecast reserve erosion triggers recovery before actual payment failure",
                governor.Mode == StrategicMode.EconomicRecovery && !governor.Economy.UpkeepPaymentFailed
                && magic.ProjectedStock < magic.SafetyReserve);
            AITurnBudget.Begin(2000);
            int reserved = governor.GetRecoveryReserveAP(board);
            Check(team + " urgent recovery reserves only the cheapest executable supply action",
                reserved == guaranteedSource.GetInfo().APCost && reserved <= board.EnemyAP);
            decision = planner.DecideStrategy(board, f.Personality, new AIThreatLevel(15), 60);
            Check(team + " necessary executable recovery can temporarily precede ordinary contact",
                decision.Strategy == TurnStrategy.EconomyBuild && decision.Budget.ReservedAP == reserved);

            board.BuildablePositions.Clear(); board.AffordableBuildings.Clear();
            // No existing producer has an upgrade in this fixture. Preserve the same real deficit.
            governor = new AIStrategicGovernor(); board.Governor = governor; governor.Evaluate(board);
            Check(team + " impossible recovery cannot reserve unusable AP", governor.GetRecoveryReserveAP(board) == 0);
            decision = planner.DecideStrategy(board, f.Personality, new AIThreatLevel(15), 60);
            Check(team + " missing recovery placement leaves the army free to engage",
                decision.Strategy == TurnStrategy.ContactEngage && decision.Budget.ReservedAP == 0);

            f.Resources.MagicOre = 0;
            board.Refresh(); governor.Evaluate(board);
            var summon = new AIAction { ActionType = AIActionType.Summon, SummonKind = consumer.kind,
                SummonDefinition = consumer.GrowthData };
            Check(team + " uncertain output never finances guaranteed recruitment obligations",
                governor.Economy.UpkeepPaymentFailed && governor.Mode == StrategicMode.EconomicRecovery
                && !governor.AllowExecution(summon, board)
                && summon.StrategicRejectReason == "upkeep_forecast_fail");
        }
    }

    static void VerifyDiversifiedReserve(GameSystems systems, Team team)
    {
        using var f = new EconomyR2Tests.Fixture(systems, team);
        var policy = f.Rules.aiEconomy;
        policy.plannedDemandWeight = 0; policy.reserveTurns = policy.forecastTurns = 4;
        f.Units.UnitDataMap.Clear();
        f.Resources.Wood = f.Resources.Stone = f.Resources.Water = f.Resources.Iron = 200;
        f.Resources.Wheat = f.Resources.Bread = 200; f.Resources.Citizen = 0; f.Resources.MagicOre = 24;
        f.Food(20); f.Building(f.Definition(FacilityKind.LoggingCamp, wood: 10));
        f.Building(f.Definition(FacilityKind.Quarry, stone: 10));
        var sources = new List<Status>();
        for (int i = 0; i < 9; i++)
        {
            var source = f.Definition(FacilityKind.Mine);
            var recipe = source.levels[0]; recipe.BonusChance1 = i == 8 ? .6f : .5f;
            recipe.BonusOutput1.MagicOre = 1; source.levels[0] = recipe;
            sources.Add(f.Building(source));
        }
        var consumer = f.Unit(); consumer.GrowthData.useAuthoredUpkeep = true; consumer.GrowthData.upkeepMagic = 4;
        var guaranteed = f.Definition(FacilityKind.Mine, magic: 2);
        var cells = f.AdjacentCells(); f.Opponent(cells[2], 1);
        var holder = new GameObject("Diversified supply observed cells"); holder.SetActive(false);
        holder.transform.SetParent(f.Crystal.transform);
        var vision = holder.AddComponent<VisionGenerator>();
        var seen = new HashSet<Vector3Int>(); foreach (var cell in cells) seen.Add(GridHelper.ToGridXZ(cell));
        foreach (string name in new[] { "_playerVisionBox", "_enemyVisionBox", "_playerExplored", "_enemyExplored" })
            typeof(VisionGenerator).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(vision, new HashSet<Vector3Int>(seen));
        var board = new AIBoardState(systems.MoveGenerator, systems.AttackGenerator, f.AP, f.Units,
            f.Crystals, vision, f.Builder, null, f.State, null, 180, null, team)
            { MapCreate = systems.MapCreate, ReconThreatLevel = 15, Governor = new AIStrategicGovernor() };
        var governor = board.Governor; governor.Evaluate(board);
        var demand = governor.ProductionDemand; var magic = demand.Get(ResourceKind.MagicOre);
        Check(team + " reproduces nine actual uncertain sources with stock24 demand4 reserve16 projected8",
            demand.PotentialSourceCount(ResourceKind.MagicOre) == 9
            && Mathf.Abs(demand.PotentialProduction(ResourceKind.MagicOre) - 4.6f) < .01f
            && magic.Stock == 24 && magic.ProductionPerTurn == 0 && magic.MandatoryDemandPerTurn == 4
            && magic.SafetyReserve == 16 && magic.ProjectedStock == 8
            && governor.BasicResources.Get(ResourceKind.MagicOre).State == BasicResourceState.Surplus);
        Check(team + " funded diversified supply retains its guaranteed deficit warning without compulsory recovery",
            demand.State == EconomicState.Warning && !EconomyHelper.IsEconomySufficient(board)
            && !demand.HasCriticalDeficit && !AIStrategicGovernor.RequiresEconomicRecovery(demand)
            && governor.Mode == StrategicMode.StrategicAttack && governor.GetRecoveryReserveAP(board) == 0);
        var investment = f.BuildAction(guaranteed, board);
        var candidates = new List<AIAction> { investment }; governor.Filter(candidates, board);
        var decision = new TurnStrategyPlanner().DecideStrategy(board, f.Personality, new AIThreatLevel(15), 180);
        Check(team + " diversified warning permits contact and ordinary guaranteed-supply investment together",
            board.HasPrincipalOpponentContact && decision.Strategy == TurnStrategy.ContactEngage && decision.Budget.ReservedAP == 0
            && candidates.Count == 1 && investment.StrategicPriority == 4);
        var purchase = f.Definition(FacilityKind.LoggingCamp, wood: 1);
        purchase.buildCost = new FacilityData.ResourceCost(magicOre: 16);
        var unsafePurchase = f.BuildAction(purchase, board);
        string stocks = JsonUtility.ToJson(f.Resources); int ap = f.State.GetAP(team);
        Check(team + " discounted reserve credit never finances a purchase that causes guaranteed payment failure",
            !governor.AllowExecution(unsafePurchase, board) && unsafePurchase.StrategicRejectReason == "build_causes_forecast_failure"
            && stocks == JsonUtility.ToJson(f.Resources) && ap == f.State.GetAP(team));

        policy.chanceReserveCredit = 0; board.Refresh(); governor.Evaluate(board);
        Check(team + " reserve credit can be disabled without changing the guaranteed diagnosis",
            governor.Mode == StrategicMode.EconomicRecovery && governor.ProductionDemand.Get(ResourceKind.MagicOre).ProjectedStock == 8);
        policy.chanceReserveCredit = float.NaN; board.Refresh(); governor.Evaluate(board);
        Check(team + " invalid reserve credit fails closed", AIStrategicGovernor.RequiresEconomicRecovery(governor.ProductionDemand));
        policy.chanceReserveCredit = .5f;
        for (int i = 1; i < sources.Count; i++) sources[i].gameObject.SetActive(false);
        var oneSource = sources[0].AuthoredFacility.levels[0]; oneSource.BonusOutput1.MagicOre = 10;
        sources[0].AuthoredFacility.levels[0] = oneSource;
        policy.minimumChanceReserveSources = 1; board.Refresh(); governor.Evaluate(board);
        Check(team + " one large chance source cannot claim diversification even with an invalid minimum of one",
            governor.ProductionDemand.PotentialSourceCount(ResourceKind.MagicOre) == 1
            && governor.ProductionDemand.PotentialProduction(ResourceKind.MagicOre) == 5
            && governor.Mode == StrategicMode.EconomicRecovery);
        oneSource.BonusOutput1.MagicOre = 1; sources[0].AuthoredFacility.levels[0] = oneSource;
        for (int i = 1; i < sources.Count; i++) sources[i].gameObject.SetActive(true);
        policy.minimumChanceReserveSources = 3;
        f.Resources.MagicOre = 20; board.Refresh(); governor.Evaluate(board);
        Check(team + " fully funded upkeep still requires recovery when discounted supply cannot restore reserve",
            !governor.Economy.UpkeepPaymentFailed && governor.ProductionDemand.Get(ResourceKind.MagicOre).ProjectedStock == 4
            && AIStrategicGovernor.RequiresEconomicRecovery(governor.ProductionDemand) && governor.Mode == StrategicMode.EconomicRecovery);
        f.Resources.MagicOre = 15; board.Refresh(); governor.Evaluate(board);
        Check(team + " chance income never masks a future mandatory payment failure",
            governor.Economy.UpkeepPaymentFailed && governor.ProductionDemand.HasCriticalDeficit
            && AIStrategicGovernor.RequiresEconomicRecovery(governor.ProductionDemand) && governor.Mode == StrategicMode.EconomicRecovery);
        f.Resources.MagicOre = 24; f.Resources.Wood = 0; consumer.GrowthData.upkeepWood = 1;
        board.Refresh(); governor.Evaluate(board);
        Check(team + " diversified supply cannot erase a payment failure in another resource",
            (governor.Economy.MandatoryFailureMask & (1 << (int)ResourceKind.Wood)) != 0
            && AIStrategicGovernor.RequiresEconomicRecovery(governor.ProductionDemand) && governor.Mode == StrategicMode.EconomicRecovery);
    }

    static FacilityDefinitionData RecoveryFixture(EconomyR2Tests.Fixture f, int magic)
    {
        f.Rules.aiEconomy.plannedDemandWeight = 0;
        f.Rules.aiEconomy.reserveTurns = f.Rules.aiEconomy.forecastTurns = 4;
        f.Resources.Wood = f.Resources.Stone = f.Resources.Water = f.Resources.Iron = 200;
        f.Resources.Wheat = f.Resources.Bread = 200;
        f.Resources.Citizen = 0; f.Resources.MagicOre = magic;
        f.Food(20);
        f.Building(f.Definition(FacilityKind.LoggingCamp, wood: 10));
        f.Building(f.Definition(FacilityKind.Quarry, stone: 10));
        var mine = f.Definition(FacilityKind.Mine, iron: 10);
        var recipe = mine.levels[0]; recipe.BonusChance1 = .5f; recipe.BonusOutput1.MagicOre = 4;
        mine.levels[0] = recipe; f.Building(mine);
        var unit = f.Unit(); unit.GrowthData.useAuthoredUpkeep = true; unit.GrowthData.upkeepMagic = 1;
        return f.Definition(FacilityKind.Mine, magic: 2);
    }

    static void VerifyRecoveryCompletion(GameSystems systems, Team team)
    {
        using var f = new EconomyR2Tests.Fixture(systems, team);
        var source = RecoveryFixture(f, 6);
        var config = ScriptableObject.CreateInstance<AIOperationConfig>();
        var reflectionConfig = ScriptableObject.CreateInstance<AIReflectionConfig>();
        config.EnablePersistentLearning = false;
        reflectionConfig.EnableConsoleDiary = reflectionConfig.EnableFileDiary = false;
        try
        {
            var board = f.Board(60);
            var manager = new AIOperationManager(config);
            string battleId = "SupplyCompletion_" + team;
            manager.BeginBattle(team, battleId); manager.BeginTurn(board, TurnStrategy.EconomyBuild, 1);
            var recovery = manager.PrimaryOperation;
            Check(team + " actual reserve erosion creates a retained economic recovery operation",
                recovery?.PrimaryGoal == AIOperationGoal.EconomicRecovery);
            var action = f.BuildAction(source, board);
            var reflection = new AIActionReflectionSystem(team, reflectionConfig, persistent: false);
            reflection.BeginBattle(battleId, 15); reflection.BeginTurn(60, board, TurnStrategy.EconomyBuild, 15);
            var token = reflection.BeginAction(action, board);
            manager.TagAction(reflection.GetPendingRecord(token), action);
            var executor = new AIActionExecutor(UnityEngine.Object.FindFirstObjectByType<TurnGenerator>(),
                systems.MoveGenerator, systems.AttackGenerator, systems.BattleSystem, f.AP, systems.SkillSystem,
                null, f.Builder, null, null, team);
            int ap = f.State.GetAP(team);
            Check(team + " recovery producer is actually purchased through the paid executor",
                executor.Execute(action, board) && f.State.GetAP(team) == ap - action.APCost);
            board.Governor.Evaluate(board);
            reflection.CompleteAction(token, true, board);
            var record = reflection.LastRecord;
            Check(team + " purchased producer creates observed Healthy recovery evidence",
                board.Governor.Economy.State == EconomicState.Healthy && record.ExecutionSucceeded
                && record.Outcome.EconomyBefore != EconomicState.Healthy
                && record.Outcome.EconomyAfter == EconomicState.Healthy && record.Reward >= 0);

            // Exercise retained/save evidence at the turn boundary, rather than ObserveAction's
            // independent immediate-completion path. Every fact still comes from the paid action.
            record.OperationContribution = (float)typeof(AIOperationManager)
                .GetMethod("Contribution", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(manager, new object[] { record, recovery });
            recovery.ActionIds.Add(record.ActionId); recovery.ActionEvidence.Add(record);
            var restored = new AIOperationManager(config);
            restored.BeginBattle(team, battleId, manager.Snapshot(false));
            restored.BeginTurn(board, TurnStrategy.Balanced, 2);
            AIOperationPlan completed = null;
            foreach (var plan in restored.State.CompletedOperations)
                if (plan.OperationId == recovery.OperationId) completed = plan;
            Check(team + " achieved retained recovery completes as success before strategic retirement",
                completed != null && completed.Status == AIOperationStatus.Success && completed.PrimaryGoalAchieved
                && completed.AbortReason == AIOperationAbortReason.None && !completed.StrategicAbort);
            var learned = completed == null ? null : restored.Profile.Find(completed.ContextKey,
                completed.PrimaryGoal, completed.PatternKey);
            Check(team + " completed recovery learns its verified achievement",
                learned != null && learned.Successes == 1 && completed.LearningReward > 0);
            var credits = restored.DrainCredits();
            Check(team + " paid recovery action receives bounded operation credit",
                credits.Count == 1 && credits[0].ActionId == record.ActionId
                && credits[0].Delta > 0 && credits[0].Delta <= config.MaxOperationCreditPerAction);
            restored.EndTurn(board, 2); restored.BeginTurn(board, TurnStrategy.Balanced, 3);
            Check(team + " completed recovery cannot credit the action twice", restored.DrainCredits().Count == 0);
        }
        finally { UnityEngine.Object.DestroyImmediate(config); UnityEngine.Object.DestroyImmediate(reflectionConfig); }
    }

    static void VerifyRecoveryDefenseAndBases(GameSystems systems, Team team)
    {
        using var f = new EconomyR2Tests.Fixture(systems, team);
        RecoveryFixture(f, 0);
        var config = ScriptableObject.CreateInstance<AIOperationConfig>();
        config.EnablePersistentLearning = false; config.MinimumBaseAssaultScore = 0;
        try
        {
            var cells = f.AdjacentCells();
            f.Crystal.transform.position = cells[0]; f.SetOwnCrystalPosition(cells[0]);
            var ownUnits = team == Team.Player ? f.Units.PlayerUnit : f.Units.EnemyUnit;
            foreach (var unit in ownUnits.GetComponentsInChildren<Status>()) { unit.ATK = 60; unit.DEF = 20; }
            var target = f.Opponent(cells[2], 0);
            target.kind = Kind.SubCrystal; target.type = Type.Building; target.facilityKind = FacilityKind.SubCrystal;
            target.transform.SetParent(f.Builder.GetBuildingParent(target.team));
            var holder = new GameObject("Recovery operation observed cells"); holder.SetActive(false);
            holder.transform.SetParent(f.Crystal.transform);
            var vision = holder.AddComponent<VisionGenerator>();
            var seen = new HashSet<Vector3Int>(); foreach (var cell in cells) seen.Add(GridHelper.ToGridXZ(cell));
            foreach (string name in new[] { "_playerVisionBox", "_enemyVisionBox", "_playerExplored", "_enemyExplored" })
                typeof(VisionGenerator).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(vision, new HashSet<Vector3Int>(seen));
            var board = new AIBoardState(systems.MoveGenerator, systems.AttackGenerator, f.AP, f.Units,
                f.Crystals, vision, f.Builder, null, f.State, null, 60, null, team)
                { MapCreate = systems.MapCreate, ReconThreatLevel = 15, Governor = new AIStrategicGovernor() };
            var manager = new AIOperationManager(config);
            manager.BeginBattle(team, "RecoveryBase_" + team); manager.BeginTurn(board, TurnStrategy.EconomyBuild, 1);
            var recovery = manager.PrimaryOperation;
            Check(team + " real payment crisis blocks even a legally known base assault",
                board.Governor.Economy.State >= EconomicState.Crisis && manager.KnownEnemyBases.Count == 1
                && !HasBaseCandidate(manager) && recovery?.PrimaryGoal == AIOperationGoal.EconomicRecovery);
            f.Resources.MagicOre = 14; board.Refresh(); manager.Refresh(board, TurnStrategy.Balanced);
            Check(team + " funded Warning retires held economic priority without inventing success",
                recovery.Status == AIOperationStatus.Aborted && recovery.StrategicAbort
                && recovery.AbortReason == AIOperationAbortReason.StrategicPriorityChanged
                && !recovery.PrimaryGoalAchieved && manager.DrainCredits().Count == 0);
            Check(team + " funded Warning permits legally observed known-base assault candidates",
                board.Governor.Economy.State == EconomicState.Warning && HasBaseCandidate(manager));

            f.Resources.MagicOre = 6; board.Refresh(); board.Governor.Evaluate(board);
            var defenseManager = new AIOperationManager(config);
            defenseManager.BeginBattle(team, "RecoveryDefense_" + team);
            defenseManager.BeginTurn(board, TurnStrategy.EconomyBuild, 1);
            var held = defenseManager.PrimaryOperation;
            Check(team + " pre-defense operation is genuinely needed economic recovery",
                held?.PrimaryGoal == AIOperationGoal.EconomicRecovery);
            f.Resources.MagicOre = 14; f.Crystal.HP = 10; f.Crystal.DEF = 0;
            f.Opponent(cells[2], 100);
            board.Refresh(); defenseManager.Refresh(board, TurnStrategy.CrystalDefense);
            Check(team + " actual defense emergency suspends recovery before strategic retirement",
                board.Governor.Mode == StrategicMode.EmergencyDefense && held.Status == AIOperationStatus.Suspended
                && held.AbortReason == AIOperationAbortReason.CrystalEmergency
                && defenseManager.State.ActiveOperations.Contains(held)
                && defenseManager.PrimaryOperation?.PrimaryGoal == AIOperationGoal.DefendOwnCrystal
                && defenseManager.DrainCredits().Count == 0);
        }
        finally { UnityEngine.Object.DestroyImmediate(config); }
    }

    static bool HasBaseCandidate(AIOperationManager manager)
    {
        foreach (var candidate in manager.LastCandidates) if (candidate.Plan.IsBaseAssault) return true;
        return false;
    }
}
#endif
