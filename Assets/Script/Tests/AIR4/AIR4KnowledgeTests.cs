#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Source fixtures for the R4 knowledge boundary. These are value-policy tests,
/// not executed matches and not evidence of strategy effectiveness. NOT RUN at authoring.
/// </summary>
public static class AIR4KnowledgeTests
{
    sealed class Fixture
    {
        public string Id;
        public AIStrategyPredicate Axis;
        public AITruthValue ExpectedAxis;
        public AIKnowledgeRequirement[] Facts;
    }
    static int passed;
    static void Check(string id, bool value)
    { if (!value) throw new InvalidOperationException("[AIR4Knowledge] FAIL " + id); passed++; Debug.Log("[AIR4Knowledge] PASS " + id); }
    public static void Run()
    {
        passed = 0;
        var matcher = new AIStrategyKnowledgeMatcher();
        Check("K12/catalog-local-40", AIStrategyKnowledgeCatalog.All.Count == 40);
        Check("K01/catalog-validation", AIStrategyCatalogValidator.Validate(AIStrategyKnowledgeCatalog.All, out _));
        var cases = Cases(); Check("K11/exactly-40-independent-fixtures", cases.Length == 40);
        int conditions = 0;
        foreach (var test in cases)
        {
            Check(test.Id + "/registered", AIStrategyKnowledgeCatalog.TryGet(test.Id, out var principle));
            var positive = Context(test, false); var no = Context(test, false); var unknown = Context(test, true);
            no.Set(test.Axis, test.ExpectedAxis == AITruthValue.True ? AITruthValue.False : AITruthValue.True, test.Id + "/counter-evidence");
            var yesMatch = matcher.Match(principle, positive);
            var noMatch = matcher.Match(principle, no);
            var unknownMatch = matcher.Match(principle, unknown);
            Check("K:" + test.Id + ":Positive", yesMatch.State == AITruthValue.True && yesMatch.CanProposeExecution && yesMatch.EvidenceIds.Count > 0); conditions++;
            Check("K:" + test.Id + ":Negative", noMatch.State == AITruthValue.False && !noMatch.CanProposeExecution
                && noMatch.RejectionReason == "Contradicted:" + test.Axis); conditions++;
            Check("K:" + test.Id + ":Unknown", unknownMatch.State == AITruthValue.Unknown && !unknownMatch.CanProposeExecution
                && Contains(unknownMatch.UnknownPredicates, test.Axis)); conditions++;
        }
        Check("K11/120-distinct-conditions", conditions == 120);
        SchemaContracts(matcher);
        OrderingAndEvidenceContracts(matcher);
        RuntimeBridgeContracts();
        Debug.Log("[AIR4Knowledge] ALL PASSED " + passed + " (includes " + conditions + " explicit knowledge conditions)");
    }
    static void SchemaContracts(AIStrategyKnowledgeMatcher matcher)
    {
        var first = AIStrategyKnowledgeCatalog.All[0];
        Check("K01/duplicate-rejected", !AIStrategyCatalogValidator.Validate(new[] { first, first }, out _));
        Check("K02/invalid-enum-rejected", !AIStrategyCatalogValidator.Validate(new[] { Copy(first, category: (AIStrategyCategory)999) }, out _));
        Check("K02/future-version-rejected", !AIStrategyCatalogValidator.Validate(new[] { Copy(first, version: 999) }, out _));
        Check("K02/unknown-operator-rejected", !AIStrategyCatalogValidator.Validate(new[] { Copy(first, operators: new[] { "TELEPORT_ILLEGALLY" }) }, out _));
        Check("K03/empty-requirements-rejected", !AIStrategyCatalogValidator.Validate(new[] { Copy(first, requirements: Array.Empty<AIKnowledgeRequirement>()) }, out _));
        var blank = new AIKnowledgeContext("b", Team.Enemy, 1, 1);
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, -1f, 0f, 1.1f })
        {
            blank.Set(AIStrategyPredicate.TwoImportantTargetsThreatened, AITruthValue.True, "bad", bad);
            Check("K02/nonfinite-confidence-" + bad, blank.Get(AIStrategyPredicate.TwoImportantTargetsThreatened).State == AITruthValue.Unknown);
        }
        blank.Set(AIStrategyPredicate.TwoImportantTargetsThreatened, AITruthValue.True, string.Empty);
        Check("K04/no-evidence-is-not-proof", blank.Get(AIStrategyPredicate.TwoImportantTargetsThreatened).State == AITruthValue.Unknown);
        Check("K04/generation-team-isolation", blank.IsCurrent("b", Team.Enemy, 1) && !blank.IsCurrent("b", Team.Player, 1) && !blank.IsCurrent("b", Team.Enemy, 2));
        var historical = Find("HI-01");
        Check("K09/history-editorial-tag", historical.EditorialStatus == AIStrategyEditorialStatus.NeedsEditorialVerification);
        var enclosed = Context(Cases()[35], false);
        enclosed.Set(AIStrategyPredicate.TwoKnownSideRoutes, AITruthValue.False, "official-route-blocked");
        Check("K05/blocked-flanks-refute-encirclement", matcher.Match(historical, enclosed).State == AITruthValue.False);
        var unavailable = new AIKnowledgeContext("b", Team.Enemy, 1, 1, AIOperationGoal.None, 9);
        Check("K10/low-difficulty-does-not-acquire-r4", matcher.Match(first, unavailable).RejectionReason == "DifficultyFeatureUnavailable");
    }
    static void OrderingAndEvidenceContracts(AIStrategyKnowledgeMatcher matcher)
    {
        var context = new AIKnowledgeContext("b", Team.Enemy, 1, 1);
        foreach (AIStrategyPredicate predicate in Enum.GetValues(typeof(AIStrategyPredicate)))
            context.Set(predicate, AITruthValue.True, "formal/" + predicate);
        foreach (var principle in AIStrategyKnowledgeCatalog.All)
            foreach (var requirement in principle.Requirements)
                if (requirement.Expected == AITruthValue.False) context.Set(requirement.Predicate, AITruthValue.False, "formal/" + requirement.Predicate);
        var normal = new List<AIKnowledgeMatch>(); var reverse = new List<AIKnowledgeMatch>();
        matcher.Match(context, normal, 40);
        var reversed = new List<AIStrategyPrinciple>(AIStrategyKnowledgeCatalog.All); reversed.Reverse();
        matcher.Match(reversed, context, reverse, 40);
        Check("K08/order-independent-count", normal.Count == reverse.Count);
        for (int i = 0; i < normal.Count; i++) Check("K08/order-independent-" + i, normal[i].Principle.Id == reverse[i].Principle.Id);
        var effects = new HashSet<string>(StringComparer.Ordinal);
        foreach (var match in normal) Check("K07/one-effect-only-" + match.Principle.Id, effects.Add(match.Principle.EffectGroup));
        var unrelated = new AIKnowledgeContext("b", Team.Enemy, 1, 1, AIOperationGoal.EconomicRecovery);
        Check("K06/irrelevant-offense-rejected", matcher.Match(Find("CH-01"), unrelated).RejectionReason == "UnrelatedGoal");
        context.Enabled = false; matcher.Match(context, normal);
        Check("K10/off-proposes-nothing", normal.Count == 0);
        foreach (var op in AIStrategyOperatorCatalog.All)
        {
            Check("K12/operator-contract-" + op.Id, op.RequiresCurrentFormalValidation && !string.IsNullOrEmpty(op.ExpectedObservedEffect));
            if (op.Kind != AIStrategyOperatorKind.ActionSelection)
                Check("K12/planning-is-not-action-" + op.Id, !op.IsDirectlyExecutable && op.ActionTypes.Count == 0);
        }
    }
    static void RuntimeBridgeContracts()
    {
        var pastClaim = new AIWorldFact("TwoKnownSideRoutes", AITruthValue.True, AIWorldFactSource.PastObservation, 7);
        var staleClaim = new AIWorldFact("FiringLineOpened", AITruthValue.True, AIWorldFactSource.CurrentObservation, 7);
        var summary = new AIWorldFact("uncertainty", AITruthValue.True, AIWorldFactSource.CurrentObservation, 8);
        var exact = new AIWorldFact("OperationRoleMissing", AITruthValue.True, AIWorldFactSource.CurrentObservation, 8);
        var world = new AIObservedWorldModel("bridge", Team.Enemy, 8, 14, 40, 40, 20, default,
            facts: new[] { pastClaim, staleClaim, summary, exact });
        var context = AIKnowledgeContextBuilder.Build(world, Array.Empty<AIAction>(), 15);
        Check("K04/bridge-past-route-not-current-proof", context.Get(AIStrategyPredicate.TwoKnownSideRoutes).State == AITruthValue.Unknown);
        Check("K04/bridge-stale-line-not-current-proof", context.Get(AIStrategyPredicate.FiringLineOpened).State == AITruthValue.Unknown);
        Check("K04/bridge-uncertainty-is-not-valuable-scouting", context.Get(AIStrategyPredicate.DecisionRelevantUnknown).State == AITruthValue.Unknown);
        Check("K04/bridge-exact-current-proof", context.Get(AIStrategyPredicate.OperationRoleMissing).State == AITruthValue.True);
        Check("K04/bridge-no-affordable-combo-invention", context.Get(AIStrategyPredicate.SharedAPAffordable).State == AITruthValue.Unknown);
        var oldEnemy = new AIObservedUnitValue("past-enemy", Kind.Knight, Team.Player, Vector3.one,
            20, 20, 10, 10, 6, -1, false, false, false, 7, AIWorldFactSource.PastObservation, .6f);
        var malformed = new AIObservedWorldModel("bridge", Team.Enemy, 8, 14, 40, 40, 20, default, observed: new[] { oldEnemy });
        var notVisible = AIKnowledgeContextBuilder.Build(malformed, Array.Empty<AIAction>(), 15);
        Check("K04/bridge-past-enemy-does-not-prove-current-target", notVisible.Get(AIStrategyPredicate.ObservedTargetAvailable).State != AITruthValue.True);
        var forecast = new[] { new ResourceDemandState(ResourceKind.Bread, 8, 2, 3, 1, 3, 1, 4, .7f) };
        var economy = new AIWorldEconomyValue(true, 10, 10, 10, 10, 10, 10, 8, 3, 5,
            EconomicState.Crisis, true, true, forecast);
        var expedition = new AIWorldOperationValue(1, AIOperationGoal.DestroyEnemyCrystal, AIOperationStatus.Active,
            6, 7, "known-crystal", Vector3.one, true, Array.Empty<string>());
        var economicWorld = new AIObservedWorldModel("bridge", Team.Enemy, 8, 15, 40, 40, 20, economy,
            operations: new[] { expedition }, facts: new[] { new AIWorldFact("role_shortage", AITruthValue.True, AIWorldFactSource.CurrentObservation, 8) });
        var economicContext = AIKnowledgeContextBuilder.Build(economicWorld, Array.Empty<AIAction>(), 15);
        Check("K04/bridge-uses-own-official-forecast", economicContext.Get(AIStrategyPredicate.EconomyForecastAvailable).State == AITruthValue.True);
        Check("K04/bridge-links-role-shortage-to-planned-deficit", economicContext.Get(AIStrategyPredicate.ProductionMismatch).State == AITruthValue.True);
        Check("K04/bridge-expedition-upkeep-is-a-risk", economicContext.Get(AIStrategyPredicate.ExpeditionSupplyRisk).State == AITruthValue.True);
        Check("K04/bridge-does-not-invent-instant-citizens", economicContext.Get(AIStrategyPredicate.CitizenGrowthRespected).State == AITruthValue.Unknown);
    }
    static AIStrategyPrinciple Copy(AIStrategyPrinciple value, AIStrategyCategory? category = null, int? version = null,
        string[] operators = null, AIKnowledgeRequirement[] requirements = null)
        => new AIStrategyPrinciple(value.Id, version ?? value.Version, category ?? value.Category, value.Label, value.Principle,
            ArrayOf(value.GoalTags), requirements ?? ArrayOf(value.Requirements), operators ?? ArrayOf(value.OperatorIds), value.EffectGroup,
            ArrayOf(value.ExpectedEffects), ArrayOf(value.FailureConditions), ArrayOf(value.CounterResponseTags), ArrayOf(value.FallbackOperatorIds),
            value.CostModel, value.ReferenceLabel, value.EditorialStatus, value.MinimumThreatLevel);
    static T[] ArrayOf<T>(IReadOnlyList<T> values)
    { var result = new T[values.Count]; for (int i = 0; i < values.Count; i++) result[i] = values[i]; return result; }
    static bool Contains<T>(IReadOnlyList<T> values, T expected)
    { foreach (var value in values) if (EqualityComparer<T>.Default.Equals(value, expected)) return true; return false; }
    static AIStrategyPrinciple Find(string id)
    { if (!AIStrategyKnowledgeCatalog.TryGet(id, out var value)) throw new InvalidOperationException(id); return value; }
    static AIKnowledgeContext Context(Fixture fixture, bool omitAxis)
    {
        var context = new AIKnowledgeContext("fixture/" + fixture.Id, Team.Enemy, 8, 12);
        foreach (var fact in fixture.Facts)
            if (!omitAxis || fact.Predicate != fixture.Axis)
                context.Set(fact.Predicate, fact.Expected, fixture.Id + "/formal/" + fact.Predicate);
        return context;
    }
    static AIKnowledgeRequirement Y(AIStrategyPredicate value) => new AIKnowledgeRequirement(value);
    static AIKnowledgeRequirement N(AIStrategyPredicate value) => new AIKnowledgeRequirement(value, AITruthValue.False);
    static Fixture F(string id, AIStrategyPredicate axis, params AIKnowledgeRequirement[] facts)
    {
        var expected = AITruthValue.Unknown;
        foreach (var fact in facts) if (fact.Predicate == axis) expected = fact.Expected;
        if (expected == AITruthValue.Unknown) throw new InvalidOperationException("Fixture lacks decisive evidence: " + id);
        return new Fixture { Id = id, Axis = axis, ExpectedAxis = expected, Facts = facts };
    }
    // Independent fixture inputs: each explicitly includes the real positive
    // conditions and identifies the one counterexample/unknown observation axis.
    static Fixture[] Cases() => new[] {
        F("CH-01", AIStrategyPredicate.TwoImportantTargetsThreatened, Y(AIStrategyPredicate.TwoImportantTargetsThreatened),Y(AIStrategyPredicate.LegalMoveAvailable),Y(AIStrategyPredicate.SharedAPAffordable)),
        F("CH-02", AIStrategyPredicate.ReserveCanCover, Y(AIStrategyPredicate.DefenderOverloaded),N(AIStrategyPredicate.ReserveCanCover),Y(AIStrategyPredicate.ObservedTargetAvailable)),
        F("CH-03", AIStrategyPredicate.SafeDiversionAvailable, Y(AIStrategyPredicate.SafeDiversionAvailable),Y(AIStrategyPredicate.KnownTerrainRouteAvailable),Y(AIStrategyPredicate.ObservedTargetAvailable)),
        F("CH-04", AIStrategyPredicate.FiringLineOpened, Y(AIStrategyPredicate.FiringLineOpened),Y(AIStrategyPredicate.LegalMoveAvailable),Y(AIStrategyPredicate.SharedAPAffordable)),
        F("CH-05", AIStrategyPredicate.DefenseActionEffective, Y(AIStrategyPredicate.FatalThreatToObjective),Y(AIStrategyPredicate.DefenseActionEffective)),
        F("CH-06", AIStrategyPredicate.IsolationRisk, Y(AIStrategyPredicate.ActivityImproves),Y(AIStrategyPredicate.LegalMoveAvailable),N(AIStrategyPredicate.IsolationRisk)),
        F("CH-07", AIStrategyPredicate.OrderedOutcomeImproves, Y(AIStrategyPredicate.OrderedOutcomeImproves),Y(AIStrategyPredicate.SharedAPAffordable)),
        F("CH-08", AIStrategyPredicate.EssentialDefenderLost, Y(AIStrategyPredicate.TradeGainExceedsLoss),N(AIStrategyPredicate.EssentialDefenderLost),Y(AIStrategyPredicate.CounterResponseKnown)),
        F("CH-09", AIStrategyPredicate.BetterEnemyReplyAvailable, Y(AIStrategyPredicate.ForcingThreatAvailable),N(AIStrategyPredicate.BetterEnemyReplyAvailable),Y(AIStrategyPredicate.ObservedTargetAvailable)),
        F("CH-10", AIStrategyPredicate.LocalParticipationAdvantage, Y(AIStrategyPredicate.LocalParticipationAdvantage),Y(AIStrategyPredicate.RearDefenseSafe),Y(AIStrategyPredicate.SharedAPAffordable)),
        F("SH-01", AIStrategyPredicate.MutualSupportAvailable, Y(AIStrategyPredicate.MutualSupportAvailable),Y(AIStrategyPredicate.SupportCellAvailable),Y(AIStrategyPredicate.SharedAPAffordable)),
        F("SH-02", AIStrategyPredicate.ThreeLayerDefenseAvailable, Y(AIStrategyPredicate.ThreeLayerDefenseAvailable),Y(AIStrategyPredicate.SupportCellAvailable),Y(AIStrategyPredicate.ExistingDutySafe)),
        F("SH-03", AIStrategyPredicate.WinningLegalSequence, Y(AIStrategyPredicate.WinningLegalSequence),Y(AIStrategyPredicate.ObservedTargetAvailable),Y(AIStrategyPredicate.SharedAPAffordable),Y(AIStrategyPredicate.CounterResponseKnown)),
        F("SH-04", AIStrategyPredicate.NewlyObservedStrategicChange, Y(AIStrategyPredicate.NewlyObservedStrategicChange),Y(AIStrategyPredicate.StableObjectiveAvailable)),
        F("SH-05", AIStrategyPredicate.ExistingDutySafe, Y(AIStrategyPredicate.ReassignmentImproves),Y(AIStrategyPredicate.ExistingDutySafe),Y(AIStrategyPredicate.LegalMoveAvailable)),
        F("GO-01", AIStrategyPredicate.ValuableKnownRegion, Y(AIStrategyPredicate.ValuableKnownRegion),Y(AIStrategyPredicate.LegalTerritoryUseAvailable)),
        F("GO-02", AIStrategyPredicate.EnemyReinforcementExcluded, Y(AIStrategyPredicate.EnemyGroupIsolated),Y(AIStrategyPredicate.EnemyReinforcementExcluded),Y(AIStrategyPredicate.ObservedTargetAvailable)),
        F("GO-03", AIStrategyPredicate.AreaAttackExposure, Y(AIStrategyPredicate.SupportNetworkAvailable),N(AIStrategyPredicate.AreaAttackExposure),Y(AIStrategyPredicate.SupportCellAvailable)),
        F("GO-04", AIStrategyPredicate.SafeCutPosition, Y(AIStrategyPredicate.SupportCutLegal),Y(AIStrategyPredicate.SafeCutPosition),Y(AIStrategyPredicate.LocalParticipationAdvantage)),
        F("GO-05", AIStrategyPredicate.LocalGlobalConflict, Y(AIStrategyPredicate.LocalGlobalConflict),Y(AIStrategyPredicate.EconomyForecastAvailable),Y(AIStrategyPredicate.StableObjectiveAvailable)),
        F("RT-01", AIStrategyPredicate.ScoutInformationGainPositive, Y(AIStrategyPredicate.DecisionRelevantUnknown),Y(AIStrategyPredicate.ScoutInformationGainPositive),Y(AIStrategyPredicate.LegalMoveAvailable)),
        F("RT-02", AIStrategyPredicate.CompetingInvestmentAvailable, Y(AIStrategyPredicate.EconomyForecastAvailable),Y(AIStrategyPredicate.CompetingInvestmentAvailable),Y(AIStrategyPredicate.CitizenGrowthRespected)),
        F("RT-03", AIStrategyPredicate.CounterAbilityObserved, Y(AIStrategyPredicate.CounterAbilityObserved),Y(AIStrategyPredicate.CounterCapabilityAvailable),Y(AIStrategyPredicate.EconomyForecastAvailable)),
        F("RT-04", AIStrategyPredicate.ReinforcementsTimely, Y(AIStrategyPredicate.ReinforcementArrivalImproves),Y(AIStrategyPredicate.ReinforcementsTimely),Y(AIStrategyPredicate.RearDefenseSafe)),
        F("RT-05", AIStrategyPredicate.CitizenGrowthRespected, Y(AIStrategyPredicate.OperationRoleMissing),Y(AIStrategyPredicate.ProductionMismatch),Y(AIStrategyPredicate.CitizenGrowthRespected),Y(AIStrategyPredicate.EconomyForecastAvailable)),
        F("RT-06", AIStrategyPredicate.SafeForceAllocation, Y(AIStrategyPredicate.MultipleObservedFronts),Y(AIStrategyPredicate.SafeForceAllocation),Y(AIStrategyPredicate.RearDefenseSafe)),
        F("RT-07", AIStrategyPredicate.AssemblyHasFiniteDeadline, Y(AIStrategyPredicate.AssemblyImprovesStrike),Y(AIStrategyPredicate.AssemblyHasFiniteDeadline),Y(AIStrategyPredicate.ObservedTargetAvailable)),
        F("RT-08", AIStrategyPredicate.SupplyForecastAvailable, Y(AIStrategyPredicate.ExpeditionSupplyRisk),Y(AIStrategyPredicate.SupplyForecastAvailable)),
        F("SL-01", AIStrategyPredicate.HeightAndLineOfSightVerified, Y(AIStrategyPredicate.LegalFiringPositionAvailable),Y(AIStrategyPredicate.HeightAndLineOfSightVerified),Y(AIStrategyPredicate.LegalMoveAvailable)),
        F("SL-02", AIStrategyPredicate.RotationKeepsRearSafe, Y(AIStrategyPredicate.WoundedFrontline),Y(AIStrategyPredicate.RotationKeepsRearSafe),Y(AIStrategyPredicate.SharedAPAffordable)),
        F("SL-03", AIStrategyPredicate.DebuffEffectVerified, Y(AIStrategyPredicate.DebuffImprovesAttack),Y(AIStrategyPredicate.DebuffEffectVerified),Y(AIStrategyPredicate.SharedAPAffordable)),
        F("SL-04", AIStrategyPredicate.CounterLossSurvivable, Y(AIStrategyPredicate.CounterResponseKnown),Y(AIStrategyPredicate.CounterLossSurvivable),Y(AIStrategyPredicate.LegalAttackAvailable)),
        F("SL-05", AIStrategyPredicate.SharedAPAffordable, Y(AIStrategyPredicate.SharedAPContention),Y(AIStrategyPredicate.SharedAPAffordable),Y(AIStrategyPredicate.OrderedOutcomeImproves)),
        F("SL-06", AIStrategyPredicate.AffordableRoleRecruitAvailable, Y(AIStrategyPredicate.OperationRoleMissing),Y(AIStrategyPredicate.AffordableRoleRecruitAvailable),Y(AIStrategyPredicate.EconomyForecastAvailable)),
        F("SL-07", AIStrategyPredicate.ResumeConditionDefined, Y(AIStrategyPredicate.LegalDisengageAvailable),Y(AIStrategyPredicate.ResumeConditionDefined),Y(AIStrategyPredicate.RearDefenseSafe)),
        F("HI-01", AIStrategyPredicate.TwoKnownSideRoutes, Y(AIStrategyPredicate.TwoKnownSideRoutes),Y(AIStrategyPredicate.CenterSurvives),Y(AIStrategyPredicate.FlankRestrictsEscape),Y(AIStrategyPredicate.SharedAPAffordable),Y(AIStrategyPredicate.ObservedTargetAvailable)),
        F("HI-02", AIStrategyPredicate.DelayImprovesOutcome, Y(AIStrategyPredicate.InferiorObservedForce),Y(AIStrategyPredicate.DelayImprovesOutcome),Y(AIStrategyPredicate.LegalDisengageAvailable),Y(AIStrategyPredicate.RearDefenseSafe)),
        F("HI-03", AIStrategyPredicate.RearDefenseSafe, Y(AIStrategyPredicate.LocalParticipationAdvantage),Y(AIStrategyPredicate.RearDefenseSafe),Y(AIStrategyPredicate.KnownTerrainRouteAvailable)),
        F("HI-04", AIStrategyPredicate.KnownTerrainRouteAvailable, Y(AIStrategyPredicate.TerrainChangesPlan),Y(AIStrategyPredicate.KnownTerrainRouteAvailable),Y(AIStrategyPredicate.HeightAndLineOfSightVerified)),
        F("HI-05", AIStrategyPredicate.AbilityFormationResponseImproves, Y(AIStrategyPredicate.CounterAbilityObserved),Y(AIStrategyPredicate.AbilityFormationResponseImproves),Y(AIStrategyPredicate.SupportCellAvailable))
    };
}
#endif
