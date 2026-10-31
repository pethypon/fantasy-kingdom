using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Small evidence-bound hypothesis table. Actual outcome IDs are deduplicated; unsupported guesses cannot become certain.</summary>
public sealed class AIHypothesisManager
{
    readonly Dictionary<string, AIHypothesis> hypotheses = new Dictionary<string, AIHypothesis>(StringComparer.Ordinal);
    readonly HashSet<string> eventIds = new HashSet<string>(StringComparer.Ordinal);
    readonly Queue<string> eventOrder = new Queue<string>();
    readonly HashSet<string> current = new HashSet<string>(StringComparer.Ordinal);
    readonly List<string> removals = new List<string>();
    string battle;
    Team team;
    int lastOwnTurn = -1;
    int lastGlobalTurn = -1;
    public void Clear() { hypotheses.Clear(); eventIds.Clear(); eventOrder.Clear(); current.Clear(); removals.Clear(); battle = null; lastOwnTurn = lastGlobalTurn = -1; }

    public IReadOnlyList<AIHypothesis> Update(AIObservedWorldModel world, IReadOnlyList<AIProblem> problems, int maxHypotheses = 32)
    {
        if (world == null) return Array.Empty<AIHypothesis>();
        if (battle != world.BattleId || team != world.ActorTeam) { Clear(); battle = world.BattleId; team = world.ActorTeam; }
        lastOwnTurn = world.OwnTurn;
        lastGlobalTurn = world.GlobalTurn;
        maxHypotheses = Mathf.Clamp(maxHypotheses, 1, 64); current.Clear();
        if (problems != null) foreach (var problem in problems)
        {
            if (problem == null || problem.Status != AIProblemStatus.Active) continue;
            switch (problem.Type)
            {
                case AIProblemType.NoProgress:
                    if (world.UnknownCount > 0) Add(problem, AIHypothesisCause.UnknownRoute, "SCOUT_FOR_ANSWER", "A newly observed route changes the next legal approach.", .35f, maxHypotheses);
                    Add(problem, AIHypothesisCause.FormationDispersed, "ASSEMBLE", "Operation members can legally support the same engagement.", .3f, maxHypotheses);
                    Add(problem, AIHypothesisCause.ObservedDefense, "TRADE_EVALUATE", "Current observed defense explains the lack of useful attacks.", .3f, maxHypotheses);
                    break;
                case AIProblemType.BlockedRoute: Add(problem, AIHypothesisCause.UnknownRoute, "SCOUT_FOR_ANSWER", "A different observed route is legal.", .5f, maxHypotheses); break;
                case AIProblemType.UnusableRangeOrLineOfSight:
                    Add(problem, AIHypothesisCause.FiringLine, "OPEN_LINE", "Formal attack generation gains a target after repositioning.", .5f, maxHypotheses);
                    Add(problem, AIHypothesisCause.InsufficientAP, "ORDER_ACTIONS", "The exact official action cost is affordable in a different order.", .3f, maxHypotheses); break;
                case AIProblemType.InsufficientForce: Add(problem, AIHypothesisCause.ObservedDefense, "ASSEMBLE", "Existing observed defenders exceed immediate participating force.", .5f, maxHypotheses); break;
                case AIProblemType.FormationBroken: Add(problem, AIHypothesisCause.FormationDispersed, "ASSEMBLE", "A legal support assignment is restored.", .6f, maxHypotheses); break;
                case AIProblemType.ResourceBottleneck: Add(problem, AIHypothesisCause.ForecastDeficit, "PLAN_PRODUCTION", "Official own-side production forecast recovers without unpaid upkeep.", .8f, maxHypotheses); break;
                case AIProblemType.ActionPriorityMismatch: Add(problem, AIHypothesisCause.PriorityConflict, "SWITCH_STANCE", "A safe prepared action survives normal selection priority.", .6f, maxHypotheses); break;
                case AIProblemType.InvalidAssumption: Add(problem, AIHypothesisCause.ObservationChanged, "SCOUT_FOR_ANSWER", "Fresh observation supports or contradicts the old assumption.", .5f, maxHypotheses); break;
                case AIProblemType.RepeatedFailure: Add(problem, AIHypothesisCause.RepeatedUnproductiveMethod, "IMPROVE_POSITION", "A different method creates actual objective progress.", .6f, maxHypotheses); break;
            }
        }
        removals.Clear(); foreach (var id in hypotheses.Keys) if (!current.Contains(id)) removals.Add(id);
        foreach (var id in removals) hypotheses.Remove(id);
        var result = new List<AIHypothesis>(); foreach (var hypothesis in hypotheses.Values) result.Add(hypothesis.Copy());
        result.Sort((a, b) => string.CompareOrdinal(a.HypothesisId, b.HypothesisId)); return result.AsReadOnly();
    }

    void Add(AIProblem problem, AIHypothesisCause cause, string test, string expected, float confidence, int limit)
    {
        string id = problem.ProblemId + ":" + cause;
        if (current.Count >= limit || !current.Add(id)) return;
        if (hypotheses.TryGetValue(id, out var old))
        {
            // A refutation is retained while its observed problem/context is unchanged.
            old.SupportingEvidenceIds = problem.EvidenceIds; return;
        }
        hypotheses[id] = new AIHypothesis { HypothesisId = id, ProblemId = problem.ProblemId, CauseCode = cause,
            SupportingEvidenceIds = problem.EvidenceIds, Confidence = Mathf.Min(.9f, confidence * problem.Confidence),
            TestActionType = test, ExpectedObservation = expected, ExpectedInformationGain = .25f,
            Outcome = AIHypothesisOutcome.Untested };
    }

    public bool ObserveOutcome(string eventId, string hypothesisId, AIHypothesisOutcome outcome, int turn, string evidenceId = null)
    {
        if (!ValidText(eventId, 256) || !ValidText(hypothesisId, 128) || turn < 0 || lastGlobalTurn >= 0 && turn > lastGlobalTurn
            || evidenceId != null && !ValidText(evidenceId, 256)
            || !Enum.IsDefined(typeof(AIHypothesisOutcome), outcome) || outcome == AIHypothesisOutcome.Untested
            || !hypotheses.TryGetValue(hypothesisId, out var hypothesis)) return false;
        string eventKey = eventId + ":" + hypothesisId;
        if (!eventIds.Add(eventKey)) return false;
        eventOrder.Enqueue(eventKey); while (eventOrder.Count > 128) eventIds.Remove(eventOrder.Dequeue());
        hypothesis.Outcome = outcome; hypothesis.TestedTurn = turn;
        if (outcome == AIHypothesisOutcome.Refuted)
        { hypothesis.Confidence *= .5f; hypothesis.Contradictions = Array.AsReadOnly(new[] { evidenceId ?? eventId }); }
        else if (outcome == AIHypothesisOutcome.Supported) hypothesis.Confidence = Mathf.Min(.95f, hypothesis.Confidence + .1f);
        return true;
    }

    public AIHypothesisBattleState Snapshot(string battleId, Team actorTeam)
    {
        var state = new AIHypothesisBattleState { BattleId = battleId, ActorTeam = actorTeam };
        if (battle != battleId || team != actorTeam) return state;
        state.LastOwnTurn = lastOwnTurn;
        state.LastGlobalTurn = lastGlobalTurn;
        var ordered = new List<AIHypothesis>(hypotheses.Values);
        ordered.Sort((a, b) => string.CompareOrdinal(a.HypothesisId, b.HypothesisId));
        foreach (var hypothesis in ordered)
        {
            if (state.Entries.Count >= 32) break;
            var entry = new AIHypothesisSaveEntry { HypothesisId = hypothesis.HypothesisId, ProblemId = hypothesis.ProblemId,
                CauseCode = hypothesis.CauseCode, Outcome = hypothesis.Outcome, Confidence = hypothesis.Confidence,
                TestActionType = hypothesis.TestActionType, ExpectedObservation = hypothesis.ExpectedObservation,
                ExpectedInformationGain = hypothesis.ExpectedInformationGain, TestedTurn = hypothesis.TestedTurn };
            CopyEvidence(hypothesis.SupportingEvidenceIds, entry.SupportingEvidenceIds);
            CopyEvidence(hypothesis.Contradictions, entry.Contradictions); state.Entries.Add(entry);
        }
        foreach (var id in eventOrder) if (state.ProcessedEventIds.Count < 128) state.ProcessedEventIds.Add(id);
        return state;
    }

    /// <summary>Corrupt/future/foreign payloads fall back to an empty table. Pass the restored own turn to reject future observations.</summary>
    public bool Restore(AIHypothesisBattleState state, string battleId, Team actorTeam, int currentOwnTurn = int.MaxValue,
        int currentGlobalTurn = int.MaxValue)
    {
        Clear(); battle = battleId; team = actorTeam;
        if (state == null) return true; // Legacy saves carry no R4 table.
        if (state.Version != AIHypothesisBattleState.CurrentVersion || !ValidText(battleId, 128)
            || state.BattleId != battleId || state.ActorTeam != actorTeam
            || actorTeam != Team.Player && actorTeam != Team.Enemy || state.LastOwnTurn < -1 || state.LastOwnTurn > currentOwnTurn
            || state.Entries == null || state.Entries.Count > 32 || state.ProcessedEventIds == null || state.ProcessedEventIds.Count > 128)
            return false;
        int savedGlobalTurn = state.LastGlobalTurn >= 0 ? state.LastGlobalTurn : state.LastOwnTurn;
        if (savedGlobalTurn > currentGlobalTurn || savedGlobalTurn < state.LastOwnTurn
            || state.Entries.Count > 0 && state.LastOwnTurn < 0) return false;
        var restored = new Dictionary<string, AIHypothesis>(StringComparer.Ordinal);
        foreach (var entry in state.Entries)
        {
            if (entry == null || !ValidText(entry.HypothesisId, 128) || !ValidText(entry.ProblemId, 128)
                || !ValidText(entry.TestActionType, 64) || !ValidText(entry.ExpectedObservation, 512)
                || !Enum.IsDefined(typeof(AIHypothesisCause), entry.CauseCode) || !Enum.IsDefined(typeof(AIHypothesisOutcome), entry.Outcome)
                || !AIWorldFact.Finite(entry.Confidence) || entry.Confidence < 0 || entry.Confidence > 1
                || !AIWorldFact.Finite(entry.ExpectedInformationGain) || entry.ExpectedInformationGain < 0
                || entry.ExpectedInformationGain > 1000 || entry.TestedTurn < -1 || entry.TestedTurn > savedGlobalTurn
                || entry.Outcome != AIHypothesisOutcome.Untested && entry.TestedTurn < 0
                || !ValidEvidence(entry.SupportingEvidenceIds) || !ValidEvidence(entry.Contradictions)
                || restored.ContainsKey(entry.HypothesisId)) return false;
            restored.Add(entry.HypothesisId, new AIHypothesis { HypothesisId = entry.HypothesisId, ProblemId = entry.ProblemId,
                CauseCode = entry.CauseCode, Outcome = entry.Outcome, Confidence = entry.Confidence,
                TestActionType = entry.TestActionType, ExpectedObservation = entry.ExpectedObservation,
                ExpectedInformationGain = entry.ExpectedInformationGain, TestedTurn = entry.TestedTurn,
                SupportingEvidenceIds = Array.AsReadOnly(entry.SupportingEvidenceIds.ToArray()),
                Contradictions = Array.AsReadOnly(entry.Contradictions.ToArray()) });
        }
        var restoredEvents = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in state.ProcessedEventIds)
            if (!ValidText(id, 512) || !restoredEvents.Add(id)) return false;
        lastOwnTurn = state.LastOwnTurn;
        lastGlobalTurn = savedGlobalTurn;
        foreach (var pair in restored) hypotheses.Add(pair.Key, pair.Value);
        foreach (var id in state.ProcessedEventIds) { eventIds.Add(id); eventOrder.Enqueue(id); }
        return true;
    }

    static bool ValidText(string value, int maximum) => !string.IsNullOrEmpty(value) && value.Length <= maximum;
    static bool ValidEvidence(List<string> evidence)
    {
        if (evidence == null || evidence.Count > 16) return false;
        foreach (var id in evidence) if (!ValidText(id, 256)) return false;
        return true;
    }
    static void CopyEvidence(IReadOnlyList<string> source, List<string> target)
    { for (int i = 0; i < source.Count && i < 16; i++) if (ValidText(source[i], 256)) target.Add(source[i]); }
}
