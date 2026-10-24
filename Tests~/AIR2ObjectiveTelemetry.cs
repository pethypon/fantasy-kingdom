#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>Bounded reporting of existing AI decisions only; never generates candidates or changes AI state.</summary>
public static class AIR2ObjectiveTelemetry
{
    const int MaximumRecords = 4096;
    sealed class Turn
    {
        public Team Team;
        public int Round, Events, GeneratedEvents, GovernorEvents, SelectionEvents;
        public int KingObservedEvents, CrystalObservedEvents, TerritoryObservedEvents, CrystalMemoryEvents, CommanderMemoryEvents;
        public int MaxObservedVictoryTargets, GeneratedCandidateSamples, GovernorCandidateSamples, SelectionCandidateSamples, MaxObjectiveCandidates;
        public int ChosenAttacks, ChosenSkills, PriorityLostSamples, ScoreLostSamples, OtherSelectionSamples, GovernorRejectedSamples;
        public int EmergencyEvents, RecoveryEvents, MinAP = int.MaxValue, MaxAP;
        public string FirstMode, LastMode;
        public int LastChosenActionIdentity;
        public int LastChosenGeneration = -1;
    }
    static readonly Dictionary<(Team, int), Turn> turns = new Dictionary<(Team, int), Turn>();
    static readonly List<Turn> ordered = new List<Turn>();
    static string folder;
    static int ignoredAfterLimit, observerErrors;
    static bool warned;

    public static void Open(string outputFolder)
    {
        Close(); folder = outputFolder; turns.Clear(); ordered.Clear(); ignoredAfterLimit = observerErrors = 0; warned = false;
        Directory.CreateDirectory(folder);
        AIDecisionObserver.Observed += Observe;
    }

    public static void Close()
    {
        AIDecisionObserver.Observed -= Observe;
        if (folder == null) return;
        try
        {
            ordered.Clear(); ordered.AddRange(turns.Values);
            ordered.Sort((a, b) => { int round = a.Round.CompareTo(b.Round); return round != 0 ? round : a.Team.CompareTo(b.Team); });
            using (var csv = new StreamWriter(Path.Combine(folder, "objectives-by-turn.csv")))
            {
                Row(csv, "team", "round", "decisionEvents", "generatedEvents", "governorEvents", "selectionEvents",
                    "kingObservedAny", "mainCrystalObservedAny", "knownEnemyTerritoryAny", "rememberedMainCrystalAny", "rememberedCommanderAny", "maxObservedVictoryTargets",
                    "generatedObjectiveCandidateSamples", "governorObjectiveCandidateSamples", "selectionObjectiveCandidateSamples", "maxObjectiveCandidatesPerEvent",
                    "chosenObjectiveAttacks", "chosenObjectiveSkills", "priorityLostSamples", "scoreLostSamples", "otherSelectionSamples", "governorRejectedSamples",
                    "emergencyDecisionEvents", "economicRecoveryDecisionEvents", "minAP", "maxAP", "firstMode", "lastMode", "primaryStopReason", "observedReasons", "routeDiagnosis");
                foreach (var item in ordered)
                    Row(csv, item.Team, item.Round, item.Events, item.GeneratedEvents, item.GovernorEvents, item.SelectionEvents,
                        item.KingObservedEvents > 0, item.CrystalObservedEvents > 0, item.TerritoryObservedEvents > 0, item.CrystalMemoryEvents > 0, item.CommanderMemoryEvents > 0,
                        item.MaxObservedVictoryTargets, item.GeneratedCandidateSamples, item.GovernorCandidateSamples, item.SelectionCandidateSamples, item.MaxObjectiveCandidates,
                        item.ChosenAttacks, item.ChosenSkills, item.PriorityLostSamples, item.ScoreLostSamples, item.OtherSelectionSamples, item.GovernorRejectedSamples,
                        item.EmergencyEvents, item.RecoveryEvents, item.MinAP == int.MaxValue ? 0 : item.MinAP, item.MaxAP, item.FirstMode, item.LastMode,
                        StopReason(item), Reasons(item), "Unknown: route/range/AP counterfactual causes were not regenerated");
            }
            File.WriteAllText(Path.Combine(folder, "objectives-scope.txt"),
                "Read-only AIDecisionObserver aggregation by faction and own round. Limit=" + MaximumRecords
                + "; records=" + ordered.Count + "; ignored events after record limit=" + ignoredAfterLimit + "; observer errors=" + observerErrors + ".\n"
                + "Generated/governor/selection counts are candidate samples across existing decision events, not unique legal opportunities. Counts from different stages must not be summed as distinct actions.\n"
                + "In the R2 revised producer, 'generated' is emitted before the first governor filter and 'governor' immediately after it. These are generated proposals, not a counterfactual proof of every possible legal action. Older baseline telemetry was post-filter and cannot supply the same rejected-candidate census.\n"
                + "An observed target means a legally visible principal commander or living main crystal; memory means an existing board observation snapshot. Hidden live coordinates/HP are never inspected.\n"
                + "Chosen counts are selections, not successful attacks or damage. The R2 revised selector emits for empty and mistake branches too. If a producer has no selection event, selection_not_observed remains unknown rather than a rejection.\n"
                + "Emergency/recovery event counts record observed modes. A mixed-mode turn does not prove that its earlier crisis caused a later missed attack.\n"
                + "A round with no target in one decision is not classified as unseen for the whole round if any later decision observed it. Non-emitting turns are absent rather than fabricated as zero.\n"
                + "No paths, counterfactual ranges, reserves, or candidate sets are generated. Route/range/AP explanations remain Unknown unless represented by the existing decision evidence.\n");
        }
        catch (Exception error) { Warn(error); }
        finally { folder = null; turns.Clear(); ordered.Clear(); }
    }

    static void Observe(AIBoardState board, List<AIAction> candidates, AIAction chosen, string stage)
    {
        if (folder == null || board == null || candidates == null) return;
        try
        {
            var key = (board.ActorTeam, board.TurnCount);
            if (!turns.TryGetValue(key, out var item))
            {
                if (turns.Count >= MaximumRecords) { ignoredAfterLimit++; return; }
                turns[key] = item = new Turn { Team = board.ActorTeam, Round = board.TurnCount };
            }
            item.Events++; item.MinAP = Math.Min(item.MinAP, board.EnemyAP); item.MaxAP = Math.Max(item.MaxAP, board.EnemyAP);
            string mode = board.Governor?.Mode.ToString() ?? "Unknown";
            if (item.FirstMode == null) item.FirstMode = mode; item.LastMode = mode;
            if (board.Governor?.Mode == StrategicMode.EmergencyDefense) item.EmergencyEvents++;
            if (board.Governor?.Mode == StrategicMode.EconomicRecovery) item.RecoveryEvents++;
            bool king = false;
            foreach (var actor in board.AlivePlayerUnits)
                if (actor != null && actor.team == board.OpponentTeam && MatchObjectiveRules.IsCommander(actor)
                    && board.IsSpecialActorObserved(actor)) { king = true; break; }
            bool crystal = board.PlayerCrystalVisible && board.PlayerCrystalHP > 0;
            if (king) item.KingObservedEvents++;
            if (crystal) item.CrystalObservedEvents++;
            item.MaxObservedVictoryTargets = Math.Max(item.MaxObservedVictoryTargets, (king ? 1 : 0) + (crystal ? 1 : 0));
            if (board.Governor?.TerritoryIntel.HasObservedTerritory == true) item.TerritoryObservedEvents++;
            if (board.GetLastKnownPlayerCrystal().Valid) item.CrystalMemoryEvents++;
            if (board.GetLastKnownPrincipalCommander(out _, out _)) item.CommanderMemoryEvents++;
            int count = 0;
            foreach (var action in candidates)
            {
                if (!ObjectiveAction(action, board)) continue;
                count++;
                if (stage == "selected" && chosen != null && action != chosen)
                {
                    if (action.StrategicPriority > chosen.StrategicPriority) item.PriorityLostSamples++;
                    else if (action.StrategicPriority == chosen.StrategicPriority && action.Score < chosen.Score) item.ScoreLostSamples++;
                    else item.OtherSelectionSamples++;
                }
            }
            item.MaxObjectiveCandidates = Math.Max(item.MaxObjectiveCandidates, count);
            if (stage == "generated") { item.GeneratedEvents++; item.GeneratedCandidateSamples += count; }
            if (stage == "governor") { item.GovernorEvents++; item.GovernorCandidateSamples += count; }
            if (stage == "selected" || stage == "chosen")
            {
                item.SelectionEvents++; item.SelectionCandidateSamples += count;
                if (ObjectiveAction(chosen, board))
                {
                    int identity = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(chosen);
                    if (board.Generation != item.LastChosenGeneration || identity != item.LastChosenActionIdentity)
                    {
                        if (chosen.ActionType == AIActionType.Attack) item.ChosenAttacks++; else item.ChosenSkills++;
                        item.LastChosenGeneration = board.Generation; item.LastChosenActionIdentity = identity;
                    }
                }
            }
            if ((stage == "generated" || stage == "governor") && board.Governor != null)
                foreach (var rejection in board.Governor.Rejections)
                    if (ObjectiveAction(rejection.Key, board)) item.GovernorRejectedSamples++;
        }
        catch (Exception error) { observerErrors++; Warn(error); }
    }

    static bool ObjectiveAction(AIAction action, AIBoardState board)
        => action != null && (action.ActionType == AIActionType.Attack || action.ActionType == AIActionType.SkillUse)
            && action.TargetUnit != null && action.TargetUnit.team == board.OpponentTeam && MatchObjectiveRules.IsDecisive(action.TargetUnit);

    static string StopReason(Turn turn)
    {
        if (turn.ChosenAttacks + turn.ChosenSkills > 0) return "observed_target_attack_selected";
        if (turn.KingObservedEvents + turn.CrystalObservedEvents == 0) return "victory_target_unobserved";
        if (turn.EmergencyEvents == turn.Events) return "emergency_defense";
        if (turn.RecoveryEvents == turn.Events) return "supply_inadequate";
        if (turn.MaxObjectiveCandidates == 0) return "valid_attack_candidate_absent";
        if (turn.PriorityLostSamples > 0) return "attack_rejected_priority";
        if (turn.ScoreLostSamples > 0) return "attack_rejected_score";
        return turn.SelectionEvents == 0 ? "selection_not_observed" : "observed_target_attack_candidate";
    }
    static string Reasons(Turn turn)
    {
        var values = new List<string>(8);
        if (turn.KingObservedEvents + turn.CrystalObservedEvents == 0) values.Add("victory_target_unobserved");
        if (turn.MaxObjectiveCandidates > 0) values.Add("observed_target_attack_candidate");
        else if (turn.KingObservedEvents + turn.CrystalObservedEvents > 0) values.Add("valid_attack_candidate_absent");
        if (turn.PriorityLostSamples > 0) values.Add("attack_rejected_priority");
        if (turn.ScoreLostSamples > 0) values.Add("attack_rejected_score");
        if (turn.GovernorRejectedSamples > 0) values.Add("objective_attack_rejected_by_governor");
        if (turn.EmergencyEvents > 0) values.Add("emergency_defense");
        if (turn.RecoveryEvents > 0) values.Add("supply_inadequate");
        if (turn.ChosenAttacks + turn.ChosenSkills > 0) values.Add("objective_attack_selected_execution_unverified");
        return string.Join(";", values);
    }
    static void Row(StreamWriter writer, params object[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) writer.Write(',');
            writer.Write('"'); writer.Write((Convert.ToString(values[i], CultureInfo.InvariantCulture) ?? "").Replace("\"", "\"\"")); writer.Write('"');
        }
        writer.WriteLine();
    }
    static void Warn(Exception error)
    {
        if (warned) return; warned = true;
        Debug.LogWarning("[AIR2ObjectiveTelemetry] Read-only objective reporting failed: " + error.Message);
    }
}
#endif
