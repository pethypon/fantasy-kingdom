using System;
using System.Collections.Generic;

public sealed class AIKnowledgeMatch
{
    public AIStrategyPrinciple Principle { get; }
    public AITruthValue State { get; }
    public float Confidence { get; }
    public IReadOnlyList<string> EvidenceIds { get; }
    public IReadOnlyList<AIStrategyPredicate> UnknownPredicates { get; }
    public string RejectionReason { get; }
    public bool CanProposeExecution => State == AITruthValue.True;
    public AIKnowledgeMatch(AIStrategyPrinciple principle, AITruthValue state, float confidence,
        string[] evidence, AIStrategyPredicate[] unknown, string reason)
    {
        Principle = principle; State = state; Confidence = confidence;
        EvidenceIds = Array.AsReadOnly(evidence ?? Array.Empty<string>());
        UnknownPredicates = Array.AsReadOnly(unknown ?? Array.Empty<AIStrategyPredicate>());
        RejectionReason = reason ?? string.Empty;
    }
}

/// <summary>Event-driven, deterministic matching. Unknown is never an executable match.</summary>
public sealed class AIStrategyKnowledgeMatcher
{
    const int HardMatchCap = 40;
    readonly List<AIKnowledgeMatch> scratch = new List<AIKnowledgeMatch>(40);
    readonly HashSet<AIStrategyCategory> represented = new HashSet<AIStrategyCategory>();
    readonly HashSet<string> selectedEffects = new HashSet<string>(StringComparer.Ordinal);

    public AIKnowledgeMatch Match(AIStrategyPrinciple principle, AIKnowledgeContext context)
    {
        if (principle == null || context == null || !context.Enabled)
            return Rejected(principle, "DisabledOrMissingContext");
        if (context.ThreatLevel < principle.MinimumThreatLevel) return Rejected(principle, "DifficultyFeatureUnavailable");
        if (context.Goal != AIOperationGoal.None && !MatchesGoal(principle, context.Goal))
            return Rejected(principle, "UnrelatedGoal");
        if (principle.Requirements.Count == 0) return Rejected(principle, "NoPreconditions");
        var evidence = new List<string>(principle.Requirements.Count);
        var unknown = new List<AIStrategyPredicate>();
        float confidence = 1f;
        foreach (var requirement in principle.Requirements)
        {
            var fact = context.Get(requirement.Predicate);
            if (fact.State == AITruthValue.Unknown) { unknown.Add(requirement.Predicate); continue; }
            if (fact.State != requirement.Expected)
                return new AIKnowledgeMatch(principle, AITruthValue.False, fact.Confidence,
                    new[] { fact.EvidenceId }, Array.Empty<AIStrategyPredicate>(), "Contradicted:" + requirement.Predicate);
            confidence = Math.Min(confidence, fact.Confidence);
            if (!evidence.Contains(fact.EvidenceId)) evidence.Add(fact.EvidenceId);
        }
        evidence.Sort(StringComparer.Ordinal); unknown.Sort();
        return new AIKnowledgeMatch(principle, unknown.Count == 0 ? AITruthValue.True : AITruthValue.Unknown,
            unknown.Count == 0 ? confidence : 0f, evidence.ToArray(), unknown.ToArray(),
            unknown.Count == 0 ? string.Empty : "NeedsFormalEvidence");
    }

    public void Match(AIKnowledgeContext context, IList<AIKnowledgeMatch> output, int maxMatches = 12)
        => Match(AIStrategyKnowledgeCatalog.All, context, output, maxMatches);

    public void Match(IReadOnlyList<AIStrategyPrinciple> catalog, AIKnowledgeContext context,
        IList<AIKnowledgeMatch> output, int maxMatches = 12)
    {
        if (output == null) throw new ArgumentNullException(nameof(output));
        output.Clear(); scratch.Clear(); represented.Clear(); selectedEffects.Clear();
        int limit = Math.Max(0, Math.Min(HardMatchCap, maxMatches));
        if (context == null || !context.Enabled || catalog == null || limit == 0) return;
        for (int i = 0; i < catalog.Count && i < 256; i++)
        {
            var match = Match(catalog[i], context);
            if (match.State != AITruthValue.False) scratch.Add(match);
        }
        scratch.Sort(Compare);
        // Proved solutions are retained before any verification candidate.
        // Within Unknown retrieval, reserve category diversity as well: an ID-only
        // first-12 slice would permanently starve RTS/SLG proof refinement.
        AddState(AITruthValue.True, output, limit);
        represented.Clear();
        AddState(AITruthValue.Unknown, output, limit);
        // Make the output order independent of catalog registration and HashSet order.
        var sorted = new List<AIKnowledgeMatch>(output); sorted.Sort(Compare); output.Clear();
        foreach (var match in sorted) output.Add(match);
    }
    void AddState(AITruthValue state, IList<AIKnowledgeMatch> output, int limit)
    {
        for (int i = 0; i < scratch.Count && output.Count < limit; i++)
        {
            var match = scratch[i];
            if (match.State == state && !represented.Contains(match.Principle.Category)
                && selectedEffects.Add(match.Principle.EffectGroup))
            { output.Add(match); represented.Add(match.Principle.Category); }
        }
        for (int i = 0; i < scratch.Count && output.Count < limit; i++)
        {
            var match = scratch[i];
            if (match.State == state && selectedEffects.Add(match.Principle.EffectGroup)) output.Add(match);
        }
    }
    static bool MatchesGoal(AIStrategyPrinciple principle, AIOperationGoal goal)
    { foreach (var tag in principle.GoalTags) if (tag == goal) return true; return false; }
    static int Compare(AIKnowledgeMatch a, AIKnowledgeMatch b)
    {
        int state = Rank(a.State).CompareTo(Rank(b.State)); if (state != 0) return state;
        int evidence = b.EvidenceIds.Count.CompareTo(a.EvidenceIds.Count); if (evidence != 0) return evidence;
        int unknown = a.UnknownPredicates.Count.CompareTo(b.UnknownPredicates.Count); if (unknown != 0) return unknown;
        int confidence = b.Confidence.CompareTo(a.Confidence); if (confidence != 0) return confidence;
        return string.CompareOrdinal(a.Principle.Id, b.Principle.Id);
    }
    static int Rank(AITruthValue state) => state == AITruthValue.True ? 0 : state == AITruthValue.Unknown ? 1 : 2;
    static AIKnowledgeMatch Rejected(AIStrategyPrinciple principle, string reason)
        => new AIKnowledgeMatch(principle, AITruthValue.False, 0f, Array.Empty<string>(), Array.Empty<AIStrategyPredicate>(), reason);
}
