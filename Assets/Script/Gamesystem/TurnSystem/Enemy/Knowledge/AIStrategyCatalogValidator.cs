using System;
using System.Collections.Generic;

/// <summary>Rejects malformed design data instead of partially enabling an invalid catalog.</summary>
public static class AIStrategyCatalogValidator
{
    public static bool Validate(IReadOnlyList<AIStrategyPrinciple> catalog, out string reason)
    {
        reason = string.Empty;
        if (catalog == null || catalog.Count == 0 || catalog.Count > 256) return Reject("InvalidCatalogSize", out reason);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in catalog)
        {
            if (value == null || !ValidId(value.Id) || !ids.Add(value.Id)) return Reject("DuplicateOrInvalidKnowledgeId", out reason);
            if (value.Version <= 0 || value.Version > AIStrategyKnowledgeCatalog.Version) return Reject("UnsupportedKnowledgeVersion:" + value.Id, out reason);
            if (!Enum.IsDefined(typeof(AIStrategyCategory), value.Category)
                || !Enum.IsDefined(typeof(AIStrategyEditorialStatus), value.EditorialStatus)
                || !Enum.IsDefined(typeof(AIStrategyCostModel), value.CostModel)) return Reject("InvalidEnum:" + value.Id, out reason);
            if (string.IsNullOrWhiteSpace(value.Label) || string.IsNullOrWhiteSpace(value.Principle)
                || string.IsNullOrWhiteSpace(value.EffectGroup) || value.GoalTags.Count == 0
                || value.Requirements.Count == 0 || value.OperatorIds.Count == 0
                || value.ExpectedEffects.Count == 0 || value.FailureConditions.Count == 0
                || value.CounterResponseTags.Count == 0 || value.FallbackOperatorIds.Count == 0)
                return Reject("IncompleteKnowledge:" + value.Id, out reason);
            if (value.MinimumThreatLevel < 0 || value.MinimumThreatLevel > 100) return Reject("InvalidDifficultyGate:" + value.Id, out reason);
            var predicates = new HashSet<AIStrategyPredicate>();
            foreach (var requirement in value.Requirements)
                if (!Enum.IsDefined(typeof(AIStrategyPredicate), requirement.Predicate)
                    || (requirement.Expected != AITruthValue.True && requirement.Expected != AITruthValue.False)
                    || !predicates.Add(requirement.Predicate)) return Reject("InvalidOrRepeatedPrecondition:" + value.Id, out reason);
            foreach (var goal in value.GoalTags)
                if (!Enum.IsDefined(typeof(AIOperationGoal), goal) || goal == AIOperationGoal.None)
                    return Reject("InvalidGoal:" + value.Id, out reason);
            foreach (var name in value.OperatorIds)
                if (!AIStrategyOperatorCatalog.TryGet(name, out _)) return Reject("UnsupportedOperator:" + name, out reason);
            foreach (var name in value.FallbackOperatorIds)
                if (!AIStrategyOperatorCatalog.TryGet(name, out _)) return Reject("UnsupportedFallback:" + name, out reason);
            if (value.Category == AIStrategyCategory.Historical
                && value.EditorialStatus != AIStrategyEditorialStatus.NeedsEditorialVerification)
                return Reject("UnverifiedHistoricalReference:" + value.Id, out reason);
        }
        return true;
    }
    static bool ValidId(string value)
    {
        if (value == null || value.Length != 5 || value[2] != '-' || value[3] < '0' || value[3] > '9'
            || value[4] < '0' || value[4] > '9') return false;
        var prefix = value.Substring(0, 2);
        return prefix == "CH" || prefix == "SH" || prefix == "GO" || prefix == "RT" || prefix == "SL" || prefix == "HI";
    }
    static bool Reject(string message, out string reason) { reason = message; return false; }
}
