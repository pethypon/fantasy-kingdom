#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class AIKnownBaseTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int passed;
    static void Check(string label, bool value)
    { if (!value) throw new InvalidOperationException("[KnownBases] FAIL " + label); passed++; Debug.Log("[KnownBases] PASS " + label); }
    static void Set(VisionGenerator vision, string name, object value) => typeof(VisionGenerator).GetField(name, Private).SetValue(vision, value);
    public static void PlayMode(GameSystems live)
    {
        passed = 0;
        ScoringAndLearningContracts();
        foreach (var team in new[] { Team.Enemy, Team.Player }) Scenario(live, team);
        Debug.Log("[KnownBases] " + passed + " observed-base/opportunity/lifetime/save checks passed");
    }
    static void Scenario(GameSystems live, Team team)
    {
        using var f = new EconomyR2Tests.Fixture(live, team);
        var holder = new GameObject("Known bases isolated vision"); holder.SetActive(false);
        var vision = holder.AddComponent<VisionGenerator>(); var config = ScriptableObject.CreateInstance<AIOperationConfig>();
        config.EnablePersistentLearning = false; config.MaximumKnownBases = 8; config.MinimumBaseAssaultScore = 0;
        try
        {
            Set(vision, "PlayerUnit", f.Units.PlayerUnit); Set(vision, "EnemyUnit", f.Units.EnemyUnit); Set(vision, "territorysystem", live.TerritorySystem);
            var seen = new HashSet<Vector3Int>();
            foreach (string field in new[] { "_playerVisionBox", "_enemyVisionBox", "_playerExplored", "_enemyExplored" }) Set(vision, field, new HashSet<Vector3Int>());
            Set(vision, team == Team.Player ? "_playerVisionBox" : "_enemyVisionBox", seen);
            var cells = f.AdjacentCells();
            f.Crystal.transform.position = cells[0]; f.SetOwnCrystalPosition(cells[0]);
            for (int i = 0; i < 6; i++) { var unit = f.Unit(); unit.ATK = 40; unit.DEF = 20; unit.transform.position = cells[0]; }
            var scout = f.Unit(); scout.kind = Kind.Scout; scout.GrowthData.kind = Kind.Scout; scout.transform.position = cells[1];
            var target = f.Opponent(cells[2], 0); target.kind = Kind.SubCrystal; target.type = Type.Building;
            target.facilityKind = FacilityKind.SubCrystal; target.transform.SetParent(f.Builder.GetBuildingParent(target.team));
            // Isolate base intelligence from economic recovery. A large stock alone does not
            // make seven unsupported soldiers sustainable once temporary crystal income ends.
            f.Building(f.Definition(FacilityKind.LoggingCamp, wood: 20));
            f.Building(f.Definition(FacilityKind.Quarry, stone: 20));
            f.Building(f.Definition(FacilityKind.Well, water: 20));
            f.Building(f.Definition(FacilityKind.Field, wheat: 20));
            f.Building(f.Definition(FacilityKind.Bakery, bread: 20));
            f.Building(f.Definition(FacilityKind.Mine, iron: 20, magic: 20));
            var board = new AIBoardState(live.MoveGenerator, live.AttackGenerator, f.AP, f.Units, f.Crystals, vision, f.Builder,
                null, f.State, null, 20, null, team); board.MapCreate = live.MapCreate; board.ReconThreatLevel = 15;
            board.Governor = new AIStrategicGovernor(); board.Governor.Evaluate(board);
            Check("base intelligence fixture has sustainable economy " + team,
                board.ProductionDemand.State == EconomicState.Healthy);
            var manager = new AIOperationManager(config, new AIOperationLearningProfile { ProfileId = "KnownBase_Test" });
            manager.BeginBattle(team, "KnownBase_Test"); manager.BeginTurn(board, TurnStrategy.Balanced, 1);
            Check("T02/T21 unseen base is absent " + team, manager.KnownEnemyBases.Count == 0);
            seen.Add(GridHelper.ToGridXZ(cells[2])); board.Refresh(); manager.Refresh(board, TurnStrategy.Balanced);
            Check("T06 currently visible base with unseen surroundings has unknown defense " + team,
                manager.KnownEnemyBases.Count == 1 && !manager.KnownEnemyBases[0].DefenseIntelKnown);
            bool unknownScout = false;
            foreach (var candidate in manager.LastCandidates)
                unknownScout |= candidate.Plan.IsBaseAssault
                    && candidate.Plan.BaseAssaultPattern == AIBaseAssaultPattern.ScoutThenAssault
                    && !candidate.Plan.BaseDefenseKnown
                    && candidate.Plan.Steps.Count > 0
                    && candidate.Plan.Steps[0].Type == AIOperationStepType.Scout
                    && candidate.Plan.Steps[0].Required;
            Check("T06 unknown defense requests required reconnaissance before assault " + team, unknownScout);
            for (int x = 0; x < live.MapCreate.maxX; x++) for (int z = 0; z < live.MapCreate.maxZ; z++) seen.Add(new Vector3Int(x, 0, z));
            board.Refresh(); manager.Refresh(board, TurnStrategy.Balanced);
            Check("T01 observed subcrystal registered " + team, manager.KnownEnemyBases.Count == 1 && manager.KnownEnemyBases[0].Type == AIEnemyBaseType.SubCrystal);
            var known = manager.KnownEnemyBases[0]; int discovered = known.FirstSeenTurn; float oldHp = known.LastKnownHpRatio;
            Check("T05 bounded assault candidates " + team, CountAssaults(manager) > 0 && CountAssaults(manager) <= 6);
            Check("T05 valuable discovered base can replace a retained scouting operation " + team, manager.PrimaryOperation?.IsBaseAssault == true);
            Check("T24 repeat refresh retains first discovery " + team, known.FirstSeenTurn == discovered && known.RediscoveryCount == 0);
            seen.Clear(); board.Refresh(); manager.Refresh(board, TurnStrategy.Balanced);
            target.HP = 1; target.ATK = 9999;
            board.Refresh(); manager.Refresh(board, TurnStrategy.Balanced);
            Check("T21 hidden updated HP is not read " + team, known.LastKnownHpRatio == oldHp && !known.CurrentlyVisible && !known.ConfirmedDestroyed);
            manager.EndTurn(board, 1); manager.BeginTurn(board, TurnStrategy.Balanced, 5);
            Check("T07 stale target remains known with lower confidence " + team, manager.KnownEnemyBases.Count == 1 && known.IntelConfidence < 1);
            bool scoutPattern = false; foreach (var candidate in manager.LastCandidates) if (candidate.Plan.IsBaseAssault && candidate.Plan.BaseAssaultPattern == AIBaseAssaultPattern.ScoutThenAssault) scoutPattern = true;
            Check("T06/T07 stale intel requests scout " + team, scoutPattern);
            var snapshot = manager.Snapshot(false); var loaded = new AIOperationManager(config); loaded.BeginBattle(team, "KnownBase_Test"); loaded.Restore(snapshot);
            Check("T22 battle save retains base LifeID without live refs " + team, loaded.KnownEnemyBases.Count == 1 && loaded.KnownEnemyBases[0].StableTargetId == target.ReflectionLifeId
                && !loaded.KnownEnemyBases[0].CurrentlyVisible);
            loaded.ConfirmKnownBaseDestroyed(target.ReflectionLifeId, false, false, board);
            Check("hidden death event does not leak " + team, !loaded.KnownEnemyBases[0].ConfirmedDestroyed);
            loaded.ConfirmKnownBaseDestroyed(target.ReflectionLifeId, true, false, board);
            Check("T12 observed other-faction destruction confirmed " + team, loaded.KnownEnemyBases[0].ConfirmedDestroyed);
            snapshot.KnownEnemyBases[0].X = int.MaxValue; loaded.Restore(snapshot); loaded.BeginTurn(board, TurnStrategy.Balanced, 6);
            Check("T23 invalid saved coordinate discarded " + team, loaded.KnownEnemyBases.Count == 0);
            seen.Clear(); FillVision(seen, live.MapCreate);
            target.HP = target.MaxHP; target.ATK = 0;
            OpportunityContracts(f, live, board, target, config, team);
            RaidWithdrawalContract(f, board, target, config, team);
            target.kind = Kind.SubCrystal; target.facilityKind = FacilityKind.SubCrystal;
            seen.Clear();
            seen.Add(GridHelper.ToGridXZ(cells[2])); target.HP = target.MaxHP;
            for (int turn = 6; turn <= 505; turn++)
            {
                target.RestoreReflectionLifeId("bounded_base_" + turn); board.Refresh(); manager.EndTurn(board, turn - 1); manager.BeginTurn(board, TurnStrategy.Balanced, turn);
            }
            Check("T20 500 turns registry stays capped " + team, manager.KnownEnemyBases.Count <= config.MaximumKnownBases);
            Check("fixed discovery memory remains 256 bytes " + team, manager.State.BaseDiscoveryBits.Length == 64);
            foreach (var entry in manager.KnownEnemyBases) Check("battle memory remains value-only " + team, entry.StableTargetId.StartsWith("bounded_base_") || entry.StableTargetId == known.StableTargetId);
            target.RestoreReflectionLifeId(known.StableTargetId); board.Refresh(); manager.EndTurn(board, 505); manager.BeginTurn(board, TurnStrategy.Balanced, 506);
            var rediscovered = FindBase(manager, known.StableTargetId);
            Check("T24 evicted identity does not receive first-discovery bonus again " + team,
                rediscovered != null && rediscovered.DiscoveryBonusTurn != 506 && rediscovered.RediscoveryCount > 0);
            var boundedSnapshot = manager.Snapshot(false);
            var restoredBounded = new AIOperationManager(config); restoredBounded.BeginBattle(team, "KnownBase_Test"); restoredBounded.Restore(boundedSnapshot);
            Check("fixed discovery filter survives save " + team, EqualBits(boundedSnapshot.BaseDiscoveryBits, restoredBounded.State.BaseDiscoveryBits));
        }
        finally { UnityEngine.Object.DestroyImmediate(holder); UnityEngine.Object.DestroyImmediate(config); }
    }
    static int CountAssaults(AIOperationManager manager)
    { int count = 0; foreach (var candidate in manager.LastCandidates) if (candidate.Plan.IsBaseAssault) count++; return count; }
    static void FillVision(HashSet<Vector3Int> cells, MapCreate map)
    { for (int x = 0; x < map.maxX; x++) for (int z = 0; z < map.maxZ; z++) cells.Add(new Vector3Int(x, 0, z)); }
    static AIKnownEnemyBase FindBase(AIOperationManager manager, string life)
    { foreach (var entry in manager.KnownEnemyBases) if (entry.StableTargetId == life) return entry; return null; }
    static bool EqualBits(int[] first, int[] second)
    { if (first == null || second == null || first.Length != second.Length) return false; for (int i = 0; i < first.Length; i++) if (first[i] != second[i]) return false; return true; }
    static void OpportunityContracts(EconomyR2Tests.Fixture f, GameSystems live, AIBoardState board, Status target, AIOperationConfig config, Team team)
    {
        board.Refresh();
        var turtle = new AIOperationManager(config); turtle.BeginBattle(team, "TurtleBase_Test");
        turtle.BeginTurn(board, TurnStrategy.Balanced, 1, playerModel: new AIPlayerModel.Profile { Turtle = 1 });
        bool flank = false, diversion = false;
        foreach (var candidate in turtle.LastCandidates)
        {
            flank |= candidate.Plan.IsBaseAssault && candidate.Plan.BaseAssaultPattern == AIBaseAssaultPattern.FlankAssault;
            diversion |= candidate.Plan.IsBaseAssault && candidate.Plan.BaseAssaultPattern == AIBaseAssaultPattern.DiversionAssault;
        }
        Check("T16/T17 observed turtle favors flank/diversion " + team, flank && diversion);
        foreach (var plan in turtle.ActiveOperations)
            if (plan.IsBaseAssault) Check("T76 start retains generalized base context " + team, plan.ContextKey.Contains("|base=SubCrystal") && !plan.ContextKey.Contains(target.ReflectionLifeId));
        ReinforcementDemandContracts(f, board, config, team);
        var defensive = new AIOperationManager(config); defensive.BeginBattle(team, "DefenseBase_Test"); defensive.BeginTurn(board, TurnStrategy.CrystalDefense, 1);
        Check("T10 emergency defense bypasses base assault candidates " + team, CountAssaults(defensive) == 0 && defensive.OperationRequiredCitizenDemand == 0);
        Check("T10 emergency defense remains primary " + team, defensive.PrimaryOperation?.PrimaryGoal == AIOperationGoal.DefendOwnCrystal);
        var main = f.Opponent(target.transform.position, 0); main.kind = Kind.Crystal; main.type = Type.Building;
        main.transform.SetParent(team == Team.Player ? f.Crystals.Enemycrystal : f.Crystals.Playercrystal);
        if (team == Team.Player) f.Crystals.ECP = main.transform.position; else f.Crystals.PCP = main.transform.position;
        board.Refresh(); var mainManager = new AIOperationManager(config); mainManager.BeginBattle(team, "MainBase_Test"); mainManager.BeginTurn(board, TurnStrategy.Balanced, 1);
        var mainKnown = FindBase(mainManager, main.ReflectionLifeId);
        Check("T03 main crystal enters legal observed index with greater value " + team,
            mainKnown != null && mainKnown.Type == AIEnemyBaseType.MainCrystal && mainKnown.EstimatedStrategicValue > FindBase(mainManager, target.ReflectionLifeId).EstimatedStrategicValue);
        main.gameObject.SetActive(false);
    }
    static void ReinforcementDemandContracts(EconomyR2Tests.Fixture f, AIBoardState board, AIOperationConfig config, Team team)
    {
        float oldMinimum = config.MinimumBaseAssaultForce;
        var originalDefinitions = new Dictionary<Kind, UnitData>(f.Units.UnitDataMap);
        var authored = (List<UnitData>)typeof(UnitSetting).GetField("authoredDefinitions", Private).GetValue(f.Units);
        var originalAuthored = new List<UnitData>(authored);
        try
        {
            // A force shortage is not always a population shortage. The standard Guardian,
            // for example, costs no Citizen. Use explicit recipes rather than catalog assumptions.
            var reinforcement = f.UnitDefinition(); reinforcement.kind = Kind.Guardian;
            reinforcement.baseATK = 40; reinforcement.baseDEF = 20; reinforcement.costCitizen = 0;
            f.Units.UnitDataMap.Clear(); f.Units.UnitDataMap[reinforcement.kind] = reinforcement; authored.Clear();
            config.MinimumBaseAssaultForce = 10000; board.Refresh();
            var free = new AIOperationManager(config); free.BeginBattle(team, "CitizenFreeBase_Test"); free.BeginTurn(board, TurnStrategy.Balanced, 1);
            bool freePreparing = false;
            foreach (var candidate in free.LastCandidates)
                if (candidate.Plan.IsBaseAssault) freePreparing |= candidate.Plan.BaseAssaultPattern == AIBaseAssaultPattern.ScoutThenAssault
                    && candidate.Plan.RequiredAssaultForce >= 10000 && candidate.Plan.RequiredAdditionalCitizens == 0;
            Check("T18 citizen-free reinforcements do not invent housing demand " + team, freePreparing && free.OperationRequiredCitizenDemand == 0);

            reinforcement.costCitizen = 3; board.Refresh();
            var paid = new AIOperationManager(config); paid.BeginBattle(team, "PaidCitizenBase_Test"); paid.BeginTurn(board, TurnStrategy.Balanced, 1);
            int expected = Mathf.Clamp(config.MaximumBaseReinforcementUnits, 1, 16) * reinforcement.costCitizen;
            bool preparing = false;
            foreach (var candidate in paid.LastCandidates)
                if (candidate.Plan.IsBaseAssault) preparing |= candidate.Plan.BaseAssaultPattern == AIBaseAssaultPattern.ScoutThenAssault
                    && candidate.Plan.RequiredAdditionalCitizens == expected;
            Check("T04/T18 insufficient force supplies actual bounded reinforcement citizen cost " + team,
                preparing && paid.OperationRequiredCitizenDemand == expected);
            foreach (var plan in paid.ActiveOperations)
                if (plan.IsBaseAssault) Check("insufficient assault force never becomes active " + team, plan.Status == AIOperationStatus.Preparing);
        }
        finally
        {
            config.MinimumBaseAssaultForce = oldMinimum;
            f.Units.UnitDataMap.Clear(); foreach (var pair in originalDefinitions) f.Units.UnitDataMap.Add(pair.Key, pair.Value);
            authored.Clear(); authored.AddRange(originalAuthored); board.Refresh();
        }
    }

    static void RaidWithdrawalContract(EconomyR2Tests.Fixture f, AIBoardState board, Status target, AIOperationConfig config, Team team)
    {
        target.kind = FacilityData.ToUnitKind(FacilityKind.Bakery); target.facilityKind = FacilityKind.Bakery;
        board.Refresh(); var manager = new AIOperationManager(config); manager.BeginBattle(team, "RaidBase_Test"); manager.BeginTurn(board, TurnStrategy.Balanced, 1);
        AIOperationPlan raid = null;
        foreach (var candidate in manager.LastCandidates)
            if (candidate.Plan.IsBaseAssault && candidate.Plan.BaseAssaultPattern == AIBaseAssaultPattern.RaidAndWithdraw) { raid = candidate.Plan; break; }
        Check("T09 economic target produces bounded raid " + team, raid != null);
        if (raid.OperationId == 0)
        {
            manager.State.ActiveOperations.Clear();
            typeof(AIOperationManager).GetMethod("Start", Private).Invoke(manager, new object[] { raid, board, 1, true });
        }
        Check("raid fixture moves every assigned actor to the objective " + team,
            MoveAssignedActors(raid, board, target.transform.position) == raid.AssignedUnitLifeIds.Count);
        raid.RallyX = target.transform.position.x + 10 < board.MapCreate.maxX ? Mathf.RoundToInt(target.transform.position.x) + 10 : Mathf.Max(0, Mathf.RoundToInt(target.transform.position.x) - 10);
        raid.RallyZ = Mathf.RoundToInt(target.transform.position.z); raid.CurrentStep = raid.Steps.Count - 2; raid.Status = AIOperationStatus.Active;
        target.HP = 0; board.Refresh(); manager.ConfirmKnownBaseDestroyed(target.ReflectionLifeId, true, true, board);
        var action = new AIActionRecord { ActionId = 99001, OperationId = raid.OperationId, OperationStepId = raid.Steps[raid.CurrentStep].StepId,
            TargetLifeId = target.ReflectionLifeId, ExecutionSucceeded = true, ActionType = AIActionType.Attack,
            Outcome = new AIActionOutcome { DamageDealt = 100 } };
        action.Outcome.KilledLifeIds.Add(target.ReflectionLifeId); manager.ObserveAction(action, board);
        Check("T13/T29 own destruction retains raid until safe withdrawal " + team,
            manager.State.ActiveOperations.Contains(raid) && raid.CurrentContext.ConfirmedTargetDestroyed && raid.Steps[raid.CurrentStep].Type == AIOperationStepType.Retreat);
        Check("raid fixture moves every surviving assigned actor to the rally " + team,
            MoveAssignedActors(raid, board, new Vector3(raid.RallyX, 0, raid.RallyZ)) == raid.AssignedUnitLifeIds.Count);
        board.Refresh(); manager.EndTurn(board, 1);
        Check("T29 raid completes after regrouping without forced next invasion " + team
            + " withdrawal=" + raid.BaseWithdrawalComplete + " achieved=" + raid.PrimaryGoalAchieved
            + " status=" + raid.Status + " abort=" + raid.AbortReason
            + " assigned=" + raid.AssignedUnitLifeIds.Count + " rally=(" + raid.RallyX + "," + raid.RallyZ + ")",
            raid.BaseWithdrawalComplete && raid.PrimaryGoalAchieved && !manager.State.ActiveOperations.Contains(raid));
        target.HP = target.MaxHP;
    }
    static int MoveAssignedActors(AIOperationPlan plan, AIBoardState board, Vector3 destination)
    {
        // UnitSetting is a coordinator, not necessarily the parent of the unit transforms.
        int moved = 0;
        foreach (var actor in board.AliveEnemyUnits)
            if (actor != null && actor.IsAlive && plan.AssignedUnitLifeIds.Contains(actor.ReflectionLifeId))
            { actor.transform.position = destination; moved++; }
        return moved;
    }

    static void ScoringAndLearningContracts()
    {
        var config = ScriptableObject.CreateInstance<AIOperationConfig>();
        try
        {
            var scorer = new AIOperationScorer(config);
            var plan = new AIOperationPlan { OperationId = 1, BattleId = "BaseScore_Test", IsBaseAssault = true,
                PrimaryGoal = AIOperationGoal.BreakEnemyDefense, BaseTargetType = AIEnemyBaseType.EconomyFacility,
                BaseAssaultPattern = AIBaseAssaultPattern.RaidAndWithdraw, TargetLifeId = "visible_bakery", HasTargetPosition = true,
                StartTurn = 1, ExpectedEndTurn = 8, MaximumEndTurn = 15, LastProgressTurn = 2, Status = AIOperationStatus.Success,
                PatternKey = "Scout>Regroup>Approach>Attack>DestroyObjective>Retreat" };
            var before = new AIOperationContext { Turn = 1, OwnCrystalHpRatio = 1, OwnUnits = 4, OwnMilitaryPower = 100,
                EconomyState = EconomicState.Healthy, ObjectiveLifeId = plan.TargetLifeId, ObjectiveKnown = true,
                ObjectiveHpKnown = true, ObjectiveHpRatio = 1, TargetObserved = true };
            var after = before.Copy(); after.Turn = 3; after.EnemyKills = 20; after.EnemyKilledPower = 2000; after.RouteOpened = true;
            var incomplete = scorer.Evaluate(plan, before, after, null);
            Check("T27 troop kills cannot replace the primary facility destruction", !incomplete.PrimaryGoalAchieved && incomplete.FinalScore <= 69);
            after.ConfirmedTargetDestroyed = true; after.ObjectiveHpRatio = 0;
            var destroyed = scorer.Evaluate(plan, before, after, null);
            Check("T13/T28 undefended economic target destruction is a primary success", destroyed.PrimaryGoalAchieved);
            Check("T30 pending withdrawal cannot receive Perfect", !destroyed.CanReachPerfect && destroyed.FinalScore <= 94);
            plan.BaseWithdrawalComplete = true; var withdrawn = scorer.Evaluate(plan, before, after, null);
            Check("T29 safe successful withdrawal permits high evaluation", withdrawn.PrimaryGoalAchieved && withdrawn.CanReachPerfect);
            after.OwnUnits = 0; after.OwnLosses = 4; var wiped = scorer.Evaluate(plan, before, after, null);
            Check("T30 catastrophic post-raid loss cannot receive Perfect", !wiped.CanReachPerfect && wiped.Rank != AIOperationRank.Perfect);
            after = before.Copy(); after.Turn = 3;
            plan.StartContext = before; plan.CurrentContext = after; plan.BaseDefenseKnown = true; plan.EstimatedBaseDefense = 40; plan.BaseIntelConfidence = 1;
            plan.ContextKey = AIOperationFeatureExtractor.ContextKey(plan, before);
            var profile = new AIOperationLearningProfile { ProfileId = "BaseLearning_Test" };
            var result = new AIOperationEvaluationResult { PrimaryGoalAchieved = true, FinalScore = 90, LearningReward = 2 };
            profile.Learn(plan, result, config);
            Check("T26 one successful sample cannot change candidate selection", profile.GetModifier(plan.ContextKey, plan.PrimaryGoal, plan.PatternKey, config) == 0);
            plan.OperationId++; profile.Learn(plan, result, config);
            Check("T25 repeated success slightly improves the matching pattern", profile.GetModifier(plan.ContextKey, plan.PrimaryGoal, plan.PatternKey, config) > 0 && profile.GetModifier(plan.ContextKey, plan.PrimaryGoal, plan.PatternKey, config) <= 3);
            string learning = JsonUtility.ToJson(profile);
            Check("T78 cross-battle learning contains generalized category and no target identity", learning.Contains("base=EconomyFacility") && !learning.Contains(plan.TargetLifeId));
        }
        finally { UnityEngine.Object.DestroyImmediate(config); }
    }
}
#endif
