#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;

/// <summary>Bounded, read-only R2 observer. Nothing in this file supplies AI input.</summary>
public static class AIR2PlaytestTelemetry
{
    static StreamWriter decisions, units;
    static int rows, decisionRows, unitRows, skippedDecisionEvents, skippedSnapshots, droppedRows, skillOverflow;
    static string folder, lastTeam, lastStage;
    static int lastTurn;
    static bool capReached;
    const int MaxRows = 160000;
    const int MaxSkillGroups = 2048;
    static readonly FieldInfo BoardField = typeof(AICommander).GetField("_board", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo AdvancedField = typeof(AICommander).GetField("_advanced", BindingFlags.Instance | BindingFlags.NonPublic);
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
    [Serializable] sealed class StatusFile
    {
        public int DetailRowLimit = MaxRows, WrittenDetailRows, DecisionRows, UnitRows, AttemptedRowsAfterCap;
        public int SkippedDecisionEventsAfterCap, SkippedSnapshotsAfterCap, SkillGroupLimit = MaxSkillGroups, SkillGroups, SkillSamplesAfterGroupLimit;
        public bool DetailCapReached, SkillGroupCapReached, Closed;
        public string LastTeam, LastStage;
        public int LastOwnTurn;
        public string Scope = "Read-only reporting. Detail files share a 160000-row cap including headers. Skill summary is separately bounded to 2048 groups and continues after the detail cap. Generated candidates are production proposals, not a counterfactual proof of every legal target. SkillSelfReady checks only owned activation/AP state; it does not prove a legal target exists.";
    }
    public static void Open(string outputFolder)
    {
        Close(); folder = outputFolder; rows = decisionRows = unitRows = skippedDecisionEvents = skippedSnapshots = droppedRows = skillOverflow = 0;
        capReached = false; lastTeam = lastStage = ""; lastTurn = 0; snapshots.Clear(); skills.Clear();
        decisions = new StreamWriter(Path.Combine(folder, "decisions.csv"));
        units = new StreamWriter(Path.Combine(folder, "units.csv"));
        Row(decisions, "team", "round", "generation", "stage", "mode", "AP", "group", "count", "actorLifeId", "action", "target", "score", "priority", "selected", "reason", "visibleEnemies", "affordableKinds", "summonCells", "casterKind", "skillKey", "normalSkillId", "specialAbility", "actionAP", "APAffordable", "chosenAction", "chosenScore", "chosenPriority", "selectionOutcome");
        Row(units, "team", "round", "lifeId", "kind", "type", "level", "x", "z", "facing", "hp", "maxHP", "barrier", "maxBarrier", "homeDistance", "incoming", "plan", "planStep", "planTargetX", "planTargetZ", "exploreId", "region", "exploreState", "exploreReason", "exploreX", "exploreZ", "newTiles", "houseCapacity", "assignedSkillId", "assignedSkillAP", "skillCooldown", "specialAbility", "specialEnabled", "specialAP", "specialCooldown", "specialSelfReady", "specialSelfReason");
        WriteStatus(false);
        AIDecisionObserver.Observed += Observe;
    }
    public static void Close()
    {
        AIDecisionObserver.Observed -= Observe;
        if (folder != null) { WriteSkills(); WriteStatus(true); }
        decisions?.Dispose(); units?.Dispose(); decisions = units = null;
        folder = null;
    }
    static void Observe(AIBoardState board, List<AIAction> candidates, AIAction chosen, string stage)
    {
        if (decisions == null || board == null) return;
        lastTeam = board.ActorTeam.ToString(); lastTurn = board.TurnCount; lastStage = stage;
        // Aggregate the existing proposals only. Never regenerate a skill or query an unobserved target.
        foreach (var a in candidates) RecordSkill(a, board, chosen, stage);
        if (chosen != null) RecordSkill(chosen, board, chosen, "chosen");
        if (stage == "governor" && board.Governor != null)
            foreach (var rejected in board.Governor.Rejections) RecordSkill(rejected.Key, board, null, "rejected:" + rejected.Value);
        if (rows >= MaxRows) { skippedDecisionEvents++; return; }
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
                chosen?.ActionType, chosen?.Score, chosen?.StrategicPriority, SelectionOutcome(a, chosen, board, stage));
        }
        if (chosen != null) Row(decisions, board.ActorTeam, board.TurnCount, board.Generation, "chosen", board.Governor?.Mode,
            board.EnemyAP, chosen.ActionType, 1, chosen.Unit?.ReflectionLifeId, chosen.ActionType,
            chosen.TargetUnit?.kind.ToString() ?? chosen.SummonKind.ToString(), chosen.Score, chosen.StrategicPriority, true,
            chosen.StrategicRejectReason, board.AlivePlayerUnits.Count, board.AffordableUnits.Count, board.SummonablePositions.Count,
            chosen.Unit?.kind, SkillKey(chosen), chosen.Skill?.Id, chosen.SpecialCommand != null ? SpecialSkillRuntime.Definition(chosen.Unit)?.ability.ToString() : "",
            chosen.APCost, chosen.APCost >= 0 && chosen.APCost <= board.EnemyAP, chosen.ActionType, chosen.Score, chosen.StrategicPriority, "chosen");
    }
    static void Group(string key, AIAction a)
    {
        if (!groups.TryGetValue(key, out var previous)) groups[key] = (1, a);
        else groups[key] = (previous.count + 1, previous.best.Score >= a.Score ? previous.best : a);
    }
    public static void Capture(AICommander commander)
    {
        if (commander == null || units == null) return;
        if (rows >= MaxRows) { skippedSnapshots++; return; }
        var board = BoardField?.GetValue(commander) as AIBoardState;
        if (board == null || snapshots.TryGetValue(board.ActorTeam, out int oldTurn) && oldTurn == board.TurnCount) return;
        snapshots[board.ActorTeam] = board.TurnCount;
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
            Row(units, board.ActorTeam, board.TurnCount, actor.ReflectionLifeId, actor.kind, actor.type, actor.Level,
                actor.transform.position.x, actor.transform.position.z, actor.direction, actor.HP, actor.MaxHP,
                actor.Barrier, actor.MaxBarrier, GridHelper.ChebyshevDistance(actor.transform.position, board.EnemyCrystalPos),
                board.EstimateCounterDamageAt(actor.transform.position, actor), plan?.Goal, plan?.Step, plan?.Target.x, plan?.Target.z,
                objective?.ObjectiveId, objective?.FrontierRegionId, objective?.State, objective?.LastReason,
                objective?.TargetCell.x, objective?.TargetCell.z, objective?.NewlyRevealedSinceStart,
                actor.type == Type.Building ? FacilityData.GetLevel(actor, actor.Level).SpecialValue : 0,
                actor.AssignedSkillId, assigned?.APCost, actor.SkillCooldown, special?.ability, special?.enabledForPlay,
                special != null && actor.Special != null ? (object)SpecialSkillRuntime.GetAPCost(actor) : null, actor.Special?.cooldown, ready, readyReason);
        }
        units.Flush(); decisions.Flush();
    }
    static void Row(StreamWriter writer, params object[] fields)
    {
        if (rows >= MaxRows) { droppedRows++; return; }
        for (int i = 0; i < fields.Length; i++)
        {
            if (i > 0) writer.Write(',');
            writer.Write('"'); writer.Write((Convert.ToString(fields[i], CultureInfo.InvariantCulture) ?? "").Replace("\"", "\"\"")); writer.Write('"');
        }
        writer.WriteLine();
        rows++; if (writer == decisions) decisionRows++; else if (writer == units) unitRows++;
        if (rows == MaxRows)
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
            SkillGroupCapReached = skillOverflow > 0, Closed = closed, LastTeam = lastTeam, LastOwnTurn = lastTurn, LastStage = lastStage }, true));
    }
}
#endif
