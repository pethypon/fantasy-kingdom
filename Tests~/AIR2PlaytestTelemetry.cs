#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;

/// <summary>Bounded, read-only R2 observer. Nothing in this file supplies AI input.</summary>
public static class AIR2PlaytestTelemetry
{
    static StreamWriter decisions, units, nations, operations;
    static int operationRows;
    static int nationRows;
    static readonly List<Status> ownBuildings = new List<Status>(64);
    static int rows, decisionRows, unitRows, skippedDecisionEvents, skippedSnapshots, droppedRows, skillOverflow, scoreTraceOverflow;
    static string folder, lastTeam, lastStage;
    static int lastTurn;
    static bool capReached;
    const int MaxRows = 160000;
    const int DecisionColumns = 43;
    const int DecisionRowLimit = 120000, UnitRowLimit = 40000;
    const int MaxSkillGroups = 2048;
    static readonly FieldInfo BoardField = typeof(AICommander).GetField("_board", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo AdvancedField = typeof(AICommander).GetField("_advanced", BindingFlags.Instance | BindingFlags.NonPublic);
    // Optional cached R3 values only: the same observer compiles against the pre-R3
    // baseline, and never invokes a planner or regenerates an action while reporting.
    static readonly FieldInfo PreservationCache = typeof(AIStrategicGovernor).GetField("preservationAssessments", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo HousingCache = typeof(AIStrategicGovernor).GetField("housingAssessments", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly Dictionary<(System.Type, string), MemberInfo> optionalMembers = new Dictionary<(System.Type, string), MemberInfo>();
    static object Value(object source, string name)
    {
        if (source == null) return null;
        var key = (source.GetType(), name);
        if (!optionalMembers.TryGetValue(key, out var member))
        {
            member = (MemberInfo)key.Item1.GetField(name) ?? key.Item1.GetProperty(name);
            optionalMembers[key] = member;
        }
        return member is FieldInfo field ? field.GetValue(source) : member is PropertyInfo property ? property.GetValue(source) : null;
    }
    static object Preservation(AIBoardState board, Status actor)
    {
        var cache = board.Governor != null ? PreservationCache?.GetValue(board.Governor) as IDictionary : null;
        return actor != null && cache != null && cache.Contains(actor) ? cache[actor] : null;
    }
    static readonly Dictionary<Team, int> snapshots = new Dictionary<Team, int>();
    static readonly Dictionary<string, (int count, AIAction best)> groups = new Dictionary<string, (int, AIAction)>();
    sealed class SkillCount
    {
        public string Team, Stage, Kind, Key;
        public int Samples, Turns, LastTurn = -1, Affordable, Chosen, PriorityLost, ScoreLost, OtherSelection, Rejected;
        public int MinAP = int.MaxValue, MaxAP, BestPriority = int.MaxValue, WorstPriority;
        public float BestScore = float.NegativeInfinity;
    }
    static readonly Dictionary<string, SkillCount> skills = new Dictionary<string, SkillCount>();
    sealed class DecisionCount
    {
        public string Team, Stage, Action, Kind, Reason;
        public int Samples, Chosen, LastTurn = -1, Turns, PriorityLost, ScoreLost;
        public double ScoreTotal;
    }
    static readonly Dictionary<string, DecisionCount> decisionSummary = new Dictionary<string, DecisionCount>();
    sealed class ScoreTrace { public float Last; public string Components; }
    static readonly Dictionary<AIAction, ScoreTrace> scoreTraces = new Dictionary<AIAction, ScoreTrace>();
    static string Trace(AIAction action)
    {
        if (action == null || !scoreTraces.TryGetValue(action, out var trace)) return "";
        return trace.Components + ";later_delta=" + (action.Score - trace.Last).ToString("R", CultureInfo.InvariantCulture);
    }
    static int decisionSummaryOverflow;
    [Serializable] sealed class StatusFile
    {
        public int DetailRowLimit = MaxRows, DecisionLimit = DecisionRowLimit, UnitLimit = UnitRowLimit;
        public int WrittenDetailRows, DecisionRows, UnitRows, AttemptedRowsAfterCap;
        public int SkippedDecisionEventsAfterCap, SkippedSnapshotsAfterCap, SkillGroupLimit = MaxSkillGroups, SkillGroups, SkillSamplesAfterGroupLimit;
        public bool DetailCapReached, SkillGroupCapReached, Closed;
        public int ScoreTraceLimit = 8192, ScoreTraceOverflowSamples;
        public string LastTeam, LastStage;
        public int LastOwnTurn;
        public string Scope = "Read-only reporting. The 160000-row detail limit reserves 40000 rows for unit trajectories and 120000 for candidate details, including headers. Skill and decision summaries continue after detail limits. Generated scores precede evaluation. Candidate samples are not distinct opportunities. SkillSelfReady checks owned activation/AP state, not legal target presence.";
    }
    public static void Open(string outputFolder)
    {
        Close(); folder = outputFolder; rows = decisionRows = unitRows = skippedDecisionEvents = skippedSnapshots = droppedRows = skillOverflow = 0;
        capReached = false; scoreTraceOverflow = 0; lastTeam = lastStage = ""; lastTurn = 0; snapshots.Clear(); skills.Clear();
        decisionSummary.Clear(); decisionSummaryOverflow = 0;
        scoreTraces.Clear();
        decisions = new StreamWriter(Path.Combine(folder, "decisions.csv"));
        units = new StreamWriter(Path.Combine(folder, "units.csv"));
        nations = new StreamWriter(Path.Combine(folder, "nation-economy.csv")); nationRows = 0;
        operations = new StreamWriter(Path.Combine(folder, "operation-orders.csv")); operationRows = 0;
        operations.WriteLine("team,round,generation,mode,AP,operationId,goal,status,step,targetX,targetZ,assigned,lastProgress,squadId,squadPhase,chosenActor,chosenType,chosenTarget,chosenOperation,actorX,actorZ,destinationX,destinationZ,operationMembers,squadMembers,coordinationSteps");
        NationRow("team", "round", "mode", "starvation", "citizen", "capacity", "houses", "houseLevels", "army", "wounded", "healers", "plannedRecruitRoles", "resource", "stock", "mandatoryPerTurn", "netPerTurn", "projectedStock", "safetyReserve", "foundationState", "basicResourceState", "requiredCitizenStock", "developmentFightingTarget", "developmentCitizenDemand", "developmentRequiredStock", "housingRequiredCapacity", "housingReasons", "housingRecoveryStarted", "housingRecoveryLastProgress", "housingDeferredTurns", "housingRecoveryStatus", "plannedAP", "currentMaxAP", "mainCrystalKnowledge");
        Row(decisions, "team", "round", "generation", "stage", "mode", "AP", "group", "count", "actorLifeId", "action", "target", "score", "priority", "selected", "reason", "visibleEnemies", "affordableKinds", "summonCells", "casterKind", "skillKey", "normalSkillId", "specialAbility", "actionAP", "APAffordable", "chosenAction", "chosenScore", "chosenPriority", "selectionOutcome", "scoreComponents", "observedIncoming", "twoExchangeLoss", "legalSaferMove", "preservationSafety", "recoveryPhase", "refugeX", "refugeZ", "healingPossible", "alliesNear", "saferMoveCount", "patientHP", "patientMaxHP", "chosenActorLifeId", "chosenTargetLifeId");
        Row(units, "team", "round", "lifeId", "kind", "type", "level", "x", "z", "facing", "hp", "maxHP", "barrier", "maxBarrier", "homeDistance", "incoming", "plan", "planStep", "planTargetX", "planTargetZ", "exploreId", "region", "exploreState", "exploreReason", "exploreX", "exploreZ", "newTiles", "houseCapacity", "assignedSkillId", "assignedSkillAP", "skillCooldown", "specialAbility", "specialEnabled", "specialAP", "specialCooldown", "specialSelfReady", "specialSelfReason", "preservationSafety", "recoveryPhase", "observedIncoming", "twoExchangeLoss", "legalSaferMove", "saferMoveCount", "healingPossible", "alliesNear", "refugeX", "refugeZ", "preservationReason", "operationId", "operationGoal", "operationStatus", "operationStep", "operationX", "operationZ", "operationProgressTurn", "squadId", "squadPhase", "squadReason");
        WriteStatus(false);
        AIDecisionObserver.Observed += Observe;
    }
    public static void Close()
    {
        AIDecisionObserver.Observed -= Observe;
        if (folder != null) { WriteSkills(); WriteDecisions(); WriteStatus(true); }
        decisions?.Dispose(); units?.Dispose(); decisions = units = null;
        nations?.Dispose(); nations = null; ownBuildings.Clear(); groups.Clear();
        operations?.Dispose(); operations = null;
        scoreTraces.Clear();
        folder = null;
    }
    static void Observe(AIBoardState board, List<AIAction> candidates, AIAction chosen, string stage)
    {
        if (decisions == null || board == null) return;
        if (stage == "selected" && operations != null && operationRows < 12000 && board.Operations != null)
        {
            AIOperationSquad chosenSquad = null;
            if (chosen?.Unit != null) board.Operations.TryGetSquad(chosen.Unit.ReflectionLifeId, out chosenSquad);
            foreach (var plan in board.Operations.State.ActiveOperations)
            {
                if (operationRows++ >= 12000) break;
                var fields = new object[] { board.ActorTeam, board.TurnCount, board.Generation, board.Governor?.Mode,
                    board.EnemyAP, plan.OperationId, plan.PrimaryGoal, plan.Status, plan.CurrentStep, plan.TargetX, plan.TargetZ,
                    plan.AssignedUnitLifeIds.Count, plan.LastProgressTurn, chosenSquad?.SquadId, chosenSquad?.Phase,
                    chosen?.Unit?.ReflectionLifeId, chosen?.ActionType, chosen?.TargetUnit?.ReflectionLifeId, chosenSquad?.OperationId,
                    chosen?.Unit?.transform.position.x, chosen?.Unit?.transform.position.z, chosen?.TargetPos.x, chosen?.TargetPos.z,
                    string.Join(";", plan.AssignedUnitLifeIds), chosenSquad != null ? string.Join(";", chosenSquad.Members.ConvertAll(member => member.ActorLifeId)) : "",
                    board.Operations.CoordinationSteps.Count };
                for (int i = 0; i < fields.Length; i++)
                {
                    if (i > 0) operations.Write(','); operations.Write('"');
                    operations.Write((Convert.ToString(fields[i], CultureInfo.InvariantCulture) ?? "").Replace("\"", "\"\"")); operations.Write('"');
                }
                operations.WriteLine();
            }
        }
        if (stage == "generated") scoreTraces.Clear();
        if (stage.StartsWith("score:", StringComparison.Ordinal))
        {
            if (decisionRows >= DecisionRowLimit || rows >= MaxRows) return;
            // Actual one-pass score differences; never re-evaluate a candidate to report it.
            foreach (var action in candidates)
            {
                if (!scoreTraces.TryGetValue(action, out var trace))
                {
                    if (scoreTraces.Count >= 8192) { scoreTraceOverflow++; continue; }
                    scoreTraces[action] = new ScoreTrace { Last = action.Score,
                        Components = stage.Substring(6) + "=" + action.Score.ToString("R", CultureInfo.InvariantCulture) };
                }
                else
                {
                    float delta = action.Score - trace.Last; trace.Last = action.Score;
                    if (delta != 0) trace.Components += ";" + stage.Substring(6) + "=" + delta.ToString("R", CultureInfo.InvariantCulture);
                }
            }
            return;
        }
        lastTeam = board.ActorTeam.ToString(); lastTurn = board.TurnCount; lastStage = stage;
        // Aggregate the existing proposals only. Never regenerate a skill or query an unobserved target.
        foreach (var a in candidates) { RecordSkill(a, board, chosen, stage); RecordDecision(a, board, chosen, stage); }
        if (chosen != null) RecordSkill(chosen, board, chosen, "chosen");
        if (stage == "governor" && board.Governor != null)
            foreach (var rejected in board.Governor.Rejections)
            {
                RecordSkill(rejected.Key, board, null, "rejected:" + rejected.Value);
                RecordDecision(rejected.Key, board, null, "rejected");
            }
        if (decisionRows >= DecisionRowLimit || rows >= MaxRows) { skippedDecisionEvents++; return; }
        if (stage == "selected" && board.Governor != null
            && PreservationCache?.GetValue(board.Governor) is IDictionary patients)
            foreach (DictionaryEntry pair in patients)
            {
                var patient = pair.Key as Status; var assessment = pair.Value;
                if (patient == null || !patient.IsAlive || patient.team != board.ActorTeam) continue;
                var refuge = Value(assessment, "Refuge");
                Row(decisions, board.ActorTeam, board.TurnCount, board.Generation, "preservation", board.Governor.Mode, board.EnemyAP,
                    "cached:" + Value(assessment, "Safety"), 1, patient.ReflectionLifeId, "assessment", null, null, null, null,
                    Value(assessment, "Reason"), board.AlivePlayerUnits.Count, null, null, patient.kind, null, null, null, null, null,
                    chosen?.ActionType, chosen?.Score, chosen?.StrategicPriority, "read_only_cached_assessment", null,
                    Value(assessment, "IncomingCurrent"), Value(assessment, "RepeatedObservedLoss"), Value(assessment, "HasLegalSaferMove"),
                    Value(assessment, "Safety"), Value(assessment, "Phase"), refuge is Vector3 point ? (object)point.x : null,
                    refuge is Vector3 secondPoint ? (object)secondPoint.z : null, Value(assessment, "HealingPossible"),
                    Value(assessment, "AlliesNear"), Value(assessment, "SaferMoveCount"), patient.HP, patient.MaxHP,
                    chosen?.Unit?.ReflectionLifeId, chosen?.TargetUnit?.ReflectionLifeId);
            }
        groups.Clear();
        if (stage == "governor" && board.Governor != null)
            foreach (var rejected in board.Governor.Rejections)
            {
                Group("rejected:" + ActionGroup(rejected.Key) + ":" + rejected.Value, rejected.Key);
            }
        foreach (var a in candidates)
            Group(ActionGroup(a), a);
        foreach (var group in groups)
        {
            var a = group.Value.best;
            Row(decisions, board.ActorTeam, board.TurnCount, board.Generation, stage, board.Governor?.Mode, board.EnemyAP,
                group.Key, group.Value.count, a.Unit?.ReflectionLifeId, a.ActionType, a.TargetUnit?.kind.ToString() ?? a.SummonKind.ToString(),
                a.Score, a.StrategicPriority, a == chosen, a.StrategicRejectReason, board.AlivePlayerUnits.Count,
                board.AffordableUnits.Count, board.SummonablePositions.Count, a.Unit?.kind, SkillKey(a), a.Skill?.Id,
                a.SpecialCommand != null ? SpecialSkillRuntime.Definition(a.Unit)?.ability.ToString() : "", a.APCost, a.APCost >= 0 && a.APCost <= board.EnemyAP,
                chosen?.ActionType, chosen?.Score, chosen?.StrategicPriority, SelectionOutcome(a, chosen, board, stage), Trace(a));
        }
        if (chosen != null) Row(decisions, board.ActorTeam, board.TurnCount, board.Generation, "chosen", board.Governor?.Mode,
            board.EnemyAP, chosen.ActionType, 1, chosen.Unit?.ReflectionLifeId, chosen.ActionType,
            chosen.TargetUnit?.kind.ToString() ?? chosen.SummonKind.ToString(), chosen.Score, chosen.StrategicPriority, true,
            chosen.StrategicRejectReason, board.AlivePlayerUnits.Count, board.AffordableUnits.Count, board.SummonablePositions.Count,
            chosen.Unit?.kind, SkillKey(chosen), chosen.Skill?.Id, chosen.SpecialCommand != null ? SpecialSkillRuntime.Definition(chosen.Unit)?.ability.ToString() : "",
            chosen.APCost, chosen.APCost >= 0 && chosen.APCost <= board.EnemyAP, chosen.ActionType, chosen.Score, chosen.StrategicPriority, "chosen", Trace(chosen));
    }
    static void Group(string key, AIAction a)
    {
        if (!groups.TryGetValue(key, out var previous)) groups[key] = (1, a);
        else groups[key] = (previous.count + 1, previous.best.Score >= a.Score ? previous.best : a);
    }
    public static void Capture(AICommander commander)
    {
        if (commander == null || units == null) return;
        var board = BoardField?.GetValue(commander) as AIBoardState;
        if (board == null || snapshots.TryGetValue(board.ActorTeam, out int oldTurn) && oldTurn == board.TurnCount) return;
        snapshots[board.ActorTeam] = board.TurnCount;
        CaptureNation(board);
        if (unitRows >= UnitRowLimit || rows >= MaxRows) { skippedSnapshots++; return; }
        var plan = (AdvancedField?.GetValue(commander) as AIAdvancedStrategy)?.Plans.Current;
        foreach (var actor in board.AliveEnemyUnits)
        {
            if (actor == null || !actor.IsAlive || actor.team != board.ActorTeam) continue;
            var objective = board.Exploration.GetObjective(actor.GetInstanceID());
            SkillData.Table.TryGetValue(actor.AssignedSkillId, out var assigned);
            var special = SpecialSkillRuntime.Definition(actor);
            var systems = board.SpecialSystems ?? actor.RuntimeSystems;
            string readyReason = special == null ? "no_definition" : systems == null ? "systems_unavailable" : actor.Special == null ? "self_state_unavailable" : "";
            bool ready = special != null && systems != null && actor.Special != null && SpecialSkillRuntime.CanUse(actor, systems, out readyReason);
            object preservation = Preservation(board, actor);
            var refuge = Value(preservation, "Refuge");
            AIOperationSquad squad = null;
            board.Operations?.TryGetSquad(actor.ReflectionLifeId, out squad);
            AIOperationPlan operation = null;
            if (squad != null && board.Operations != null)
                foreach (var candidate in board.Operations.State.ActiveOperations)
                    if (candidate.OperationId == squad.OperationId) { operation = candidate; break; }
            Row(units, board.ActorTeam, board.TurnCount, actor.ReflectionLifeId, actor.kind, actor.type, actor.Level,
                actor.transform.position.x, actor.transform.position.z, actor.direction, actor.HP, actor.MaxHP,
                actor.Barrier, actor.MaxBarrier, GridHelper.ChebyshevDistance(actor.transform.position, board.EnemyCrystalPos),
                board.EstimateCounterDamageAt(actor.transform.position, actor), plan?.Goal, plan?.Step, plan?.Target.x, plan?.Target.z,
                objective?.ObjectiveId, objective?.FrontierRegionId, objective?.State, objective?.LastReason,
                objective?.TargetCell.x, objective?.TargetCell.z, objective?.NewlyRevealedSinceStart,
                actor.type == Type.Building ? FacilityData.GetLevel(actor, actor.Level).SpecialValue : 0,
                actor.AssignedSkillId, assigned?.APCost, actor.SkillCooldown, special?.ability, special?.enabledForPlay,
                special != null && actor.Special != null ? (object)SpecialSkillRuntime.GetAPCost(actor) : null, actor.Special?.cooldown, ready, readyReason,
                Value(preservation, "Safety"), Value(preservation, "Phase"), Value(preservation, "IncomingCurrent"),
                Value(preservation, "RepeatedObservedLoss"), Value(preservation, "HasLegalSaferMove"), Value(preservation, "SaferMoveCount"),
                Value(preservation, "HealingPossible"), Value(preservation, "AlliesNear"), refuge is Vector3 point ? (object)point.x : null,
                refuge is Vector3 secondPoint ? (object)secondPoint.z : null, Value(preservation, "Reason"),
                operation?.OperationId, operation?.PrimaryGoal, operation?.Status, operation?.CurrentStep,
                operation?.TargetX, operation?.TargetZ, operation?.LastProgressTurn,
                squad?.SquadId, squad?.Phase, squad?.LastReason);
        }
        units.Flush(); decisions.Flush();
    }
    static void CaptureNation(AIBoardState board)
    {
        var governor = board.Governor;
        if (governor == null || nations == null || nationRows >= 32768) return;
        board.CollectOwnBuildings(ownBuildings);
        int capacity = FactionState.BaseCitizenCap, houses = 0, levels = 0, army = 0, wounded = 0, healers = 0;
        foreach (var building in ownBuildings)
        {
            if (building.facilityKind != FacilityKind.House && building.facilityKind != FacilityKind.LuxuryHouse) continue;
            houses++; levels += building.Level;
            capacity += FacilityData.GetLevel(building, building.Level).SpecialValue;
        }
        foreach (var actor in board.AliveEnemyUnits)
        {
            if (actor == null || !actor.IsAlive || actor.type != Type.Unit || MatchObjectiveRules.IsCommander(actor)) continue;
            army++; if (actor.HP < actor.MaxHP * .4f) wounded++;
            if (AIHealingCapability.HasHealing(actor)) healers++;
        }
        string order = "";
        foreach (var need in governor.RecruitmentPlan) order += need.Role + ":" + need.Definition.kind + ":" + need.Reason + ";";
        float requiredCapacity = 0; int desiredAP = 0;
        var housingReasons = new HashSet<string>();
        if (HousingCache?.GetValue(governor) is IDictionary housing)
            foreach (object assessment in housing.Values)
            {
                if (Value(assessment, "RequiredStock") is float needed) requiredCapacity = Mathf.Max(requiredCapacity, needed);
                if (Value(Value(assessment, "APPlan"), "DesiredAP") is int plannedAP) desiredAP = Mathf.Max(desiredAP, plannedAP);
                string reason = Convert.ToString(Value(assessment, "Reason"), CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(reason)) housingReasons.Add(reason);
            }
        var sortedReasons = new List<string>(housingReasons); sortedReasons.Sort(StringComparer.Ordinal);
        foreach (var resource in governor.ProductionDemand.Resources)
            NationRow(board.ActorTeam, board.TurnCount, governor.Mode, board.NationStarvationCounter, board.EnemyResources?.Citizen,
                capacity, houses, levels, army, wounded, healers, order, resource.Resource, resource.Stock,
                resource.MandatoryDemandPerTurn, resource.NetPerTurn, resource.ProjectedStock, resource.SafetyReserve,
                governor.BasicResources.FoundationState, governor.BasicResources.Get(resource.Resource)?.State,
                governor.RecruitmentRequiredCitizenStock, Value(governor, "DevelopmentFightingTarget"), Value(governor, "DevelopmentCitizenDemand"),
                Value(governor, "DevelopmentRequiredCitizenStock"), requiredCapacity, string.Join(";", sortedReasons),
                Value(governor, "HousingRecoveryStartedTurn"), Value(governor, "HousingRecoveryLastProgressTurn"),
                Value(governor, "HousingRecoveryDeferredTurns"), Value(governor, "HousingRecoveryStatus"), desiredAP, board.OwnAPData?.Maximum, Value(board,"MainCrystalKnowledge"));
        nations.Flush(); ownBuildings.Clear();
    }
    static void NationRow(params object[] fields)
    {
        if (nationRows++ >= 32768) return;
        for (int i = 0; i < fields.Length; i++)
        {
            if (i > 0) nations.Write(',');
            nations.Write('"'); nations.Write((Convert.ToString(fields[i], CultureInfo.InvariantCulture) ?? "").Replace("\"", "\"\"")); nations.Write('"');
        }
        nations.WriteLine();
    }
    static void Row(StreamWriter writer, params object[] fields)
    {
        if (rows >= MaxRows || writer == decisions && decisionRows >= DecisionRowLimit || writer == units && unitRows >= UnitRowLimit)
        { droppedRows++; return; }
        int count = writer == decisions ? Math.Max(fields.Length, DecisionColumns) : fields.Length;
        for (int i = 0; i < count; i++)
        {
            if (i > 0) writer.Write(',');
            writer.Write('"'); writer.Write((Convert.ToString(i < fields.Length ? fields[i] : null, CultureInfo.InvariantCulture) ?? "").Replace("\"", "\"\"")); writer.Write('"');
        }
        writer.WriteLine();
        rows++; if (writer == decisions) decisionRows++; else if (writer == units) unitRows++;
        if (!capReached && (rows == MaxRows || decisionRows == DecisionRowLimit || unitRows == UnitRowLimit))
        { capReached = true; decisions?.Flush(); units?.Flush(); WriteSkills(); WriteStatus(false); }
    }

    static string SkillKey(AIAction action)
    {
        if (action?.ActionType != AIActionType.SkillUse) return "";
        if (action.SpecialCommand != null)
            return "special:" + (SpecialSkillRuntime.Definition(action.Unit)?.ability.ToString() ?? "undefined")
                + (action.SpecialCommand.BerserkerStage > 0 ? ":stage" + action.SpecialCommand.BerserkerStage : "");
        return action.Skill != null ? "normal:" + action.Skill.Id : "undefined";
    }
    static string ActionGroup(AIAction action)
        => action.ActionType + ":" + (action.ActionType == AIActionType.SkillUse ? action.Unit?.kind + ":" + SkillKey(action) + ":P" + action.StrategicPriority
            : action.ActionType == AIActionType.Summon ? action.SummonKind.ToString()
            : action.ActionType == AIActionType.Build ? action.Facility.ToString() : action.StrategicPriority.ToString());
    static string SelectionOutcome(AIAction action, AIAction chosen, AIBoardState board, string stage)
    {
        if (action == chosen) return "chosen";
        if (!string.IsNullOrEmpty(action.StrategicRejectReason)) return "rejected:" + action.StrategicRejectReason;
        if (action.APCost < 0 || action.APCost > board.EnemyAP) return "AP_unaffordable";
        if (stage != "selected" || chosen == null) return "not_selection_stage";
        if (action.StrategicPriority > chosen.StrategicPriority) return "priority_lost";
        if (action.StrategicPriority == chosen.StrategicPriority && action.Score < chosen.Score) return "score_lost";
        return "tie_or_selector_gate";
    }
    static void RecordSkill(AIAction action, AIBoardState board, AIAction chosen, string stage)
    {
        if (action?.ActionType != AIActionType.SkillUse || action.Unit == null || action.Unit.team != board.ActorTeam) return;
        string skill = SkillKey(action), key = board.ActorTeam + ":" + stage + ":" + action.Unit.kind + ":" + skill;
        if (!skills.TryGetValue(key, out var count))
        {
            if (skills.Count >= MaxSkillGroups) { skillOverflow++; if (skillOverflow == 1) WriteStatus(false); return; }
            skills[key] = count = new SkillCount { Team = board.ActorTeam.ToString(), Stage = stage, Kind = action.Unit.kind.ToString(), Key = skill };
        }
        count.Samples++; if (count.LastTurn != board.TurnCount) { count.Turns++; count.LastTurn = board.TurnCount; }
        if (action.APCost >= 0 && action.APCost <= board.EnemyAP) count.Affordable++;
        count.MinAP = Math.Min(count.MinAP, action.APCost); count.MaxAP = Math.Max(count.MaxAP, action.APCost);
        count.BestPriority = Math.Min(count.BestPriority, action.StrategicPriority); count.WorstPriority = Math.Max(count.WorstPriority, action.StrategicPriority);
        count.BestScore = Mathf.Max(count.BestScore, action.Score);
        string outcome = SelectionOutcome(action, chosen, board, stage);
        if (action == chosen) count.Chosen++;
        else if (stage.StartsWith("rejected:", StringComparison.Ordinal) || outcome.StartsWith("rejected:", StringComparison.Ordinal)) count.Rejected++;
        else if (stage == "selected")
        { if (outcome == "priority_lost") count.PriorityLost++; else if (outcome == "score_lost") count.ScoreLost++; else count.OtherSelection++; }
    }
    static void RecordDecision(AIAction action, AIBoardState board, AIAction chosen, string stage)
    {
        string kind = action.Unit != null ? action.Unit.kind.ToString() : action.ActionType == AIActionType.Summon
            ? action.SummonKind.ToString() : action.Facility.ToString();
        string reason = action.StrategicRejectReason ?? "";
        string key = board.ActorTeam + ":" + stage + ":" + action.ActionType + ":" + kind + ":" + reason;
        if (!decisionSummary.TryGetValue(key, out var count))
        {
            if (decisionSummary.Count >= 4096) { decisionSummaryOverflow++; return; }
            decisionSummary[key] = count = new DecisionCount { Team = board.ActorTeam.ToString(), Stage = stage,
                Action = action.ActionType.ToString(), Kind = kind, Reason = reason };
        }
        count.Samples++; count.ScoreTotal += action.Score;
        if (count.LastTurn != board.TurnCount) { count.Turns++; count.LastTurn = board.TurnCount; }
        if (action == chosen) count.Chosen++;
        else if (stage == "selected" && chosen != null)
        {
            if (action.StrategicPriority > chosen.StrategicPriority) count.PriorityLost++;
            else if (action.StrategicPriority == chosen.StrategicPriority && action.Score < chosen.Score) count.ScoreLost++;
        }
    }
    static void WriteDecisions()
    {
        using var output = new StreamWriter(Path.Combine(folder, "decision-summary.csv"));
        output.WriteLine("team,stage,action,kind,reason,candidateSamples,ownTurns,chosen,priorityLost,scoreLost,meanScore");
        foreach (var row in decisionSummary.Values)
        {
            var fields = new object[] { row.Team, row.Stage, row.Action, row.Kind, row.Reason, row.Samples,
                row.Turns, row.Chosen, row.PriorityLost, row.ScoreLost, row.Samples > 0 ? row.ScoreTotal / row.Samples : 0 };
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0) output.Write(','); output.Write('"');
                output.Write(Convert.ToString(fields[i], CultureInfo.InvariantCulture).Replace("\"", "\"\"")); output.Write('"');
            }
            output.WriteLine();
        }
        File.WriteAllText(Path.Combine(folder, "decision-summary-status.txt"), "groupLimit=4096 groups="
            + decisionSummary.Count + " samplesAfterLimit=" + decisionSummaryOverflow + "\nCandidate samples include repeated evaluation; generated scores precede evaluation.");
    }
    static void WriteSkills()
    {
        using var output = new StreamWriter(Path.Combine(folder, "skills.csv"));
        output.WriteLine("team,stage,casterKind,skillKey,candidateSamples,ownTurns,APAffordable,chosen,priorityLost,scoreLost,otherSelection,rejected,minAP,maxAP,bestPriority,worstPriority,bestScore");
        foreach (var count in skills.Values)
        {
            var fields = new object[] { count.Team, count.Stage, count.Kind, count.Key, count.Samples, count.Turns, count.Affordable,
                count.Chosen, count.PriorityLost, count.ScoreLost, count.OtherSelection, count.Rejected, count.MinAP, count.MaxAP,
                count.BestPriority, count.WorstPriority, count.BestScore };
            for (int i = 0; i < fields.Length; i++) { if (i > 0) output.Write(','); output.Write('"'); output.Write((Convert.ToString(fields[i], CultureInfo.InvariantCulture) ?? "").Replace("\"", "\"\"")); output.Write('"'); }
            output.WriteLine();
        }
    }
    static void WriteStatus(bool closed)
    {
        File.WriteAllText(Path.Combine(folder, "telemetry-status.json"), JsonUtility.ToJson(new StatusFile {
            WrittenDetailRows = rows, DecisionRows = decisionRows, UnitRows = unitRows, AttemptedRowsAfterCap = droppedRows,
            SkippedDecisionEventsAfterCap = skippedDecisionEvents, SkippedSnapshotsAfterCap = skippedSnapshots,
            SkillGroups = skills.Count, SkillSamplesAfterGroupLimit = skillOverflow, DetailCapReached = capReached,
            SkillGroupCapReached = skillOverflow > 0, ScoreTraceOverflowSamples = scoreTraceOverflow,
            Closed = closed, LastTeam = lastTeam, LastOwnTurn = lastTurn, LastStage = lastStage }, true));
    }
}
#endif
