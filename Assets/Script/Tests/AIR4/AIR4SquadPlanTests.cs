#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Value graph admission contracts. NOT RUN; these do not establish real-battle/LoS/Sim parity.</summary>
public static class AIR4SquadPlanTests
{
    static int checks;
    static void Check(string id, bool condition)
    { if (!condition) throw new InvalidOperationException("[R4Squad] FAIL " + id); checks++; Debug.Log("[R4Squad] PASS " + id); }
    public static void Run()
    {
        checks = 0;
        var plan = Plan(); var world = World(); var sequence = Sequence(); var witnesses = Proofs();
        Check("value-chain matching current two-actor sequence is admitted", Compose(plan, world, sequence, witnesses, out var nodes));
        Check("S04-value-chain first action stays bound and later action is not executable",
            nodes.Count == 2 && nodes[0].BoundActionKey == "head-key" && nodes[0].CostKnown && nodes[0].Status == AIPlanOperatorStatus.Ready
            && nodes[1].BoundActionKey == null && !nodes[1].CostKnown && nodes[1].Status == AIPlanOperatorStatus.AwaitingObservation);
        Check("S06-value-chain temporary mission remains separate from permanent roles",
            nodes[1].Mission == AISquadMissionRole.Breakthrough && nodes[1].Dependencies.Count == 1 && nodes[1].Dependencies[0] == 0
            && Enum.GetValues(typeof(AIOperationSquadRole)).Length == 4);
        Check("value-chain builder does not mutate source plan or reserve AP",
            plan.Steps.Count == 1 && plan.APCost == 3 && plan.EffectiveDamage == 2 && !plan.ProvenVictory && plan.Signature == "fixture-means");
        nodes[0].RequiredEvidenceIds.Add("copied-only");
        Check("value-chain copied head owns its evidence list", !plan.Steps[0].RequiredEvidenceIds.Contains("copied-only"));

        Check("S04-value-chain combined AP is checked", !Compose(plan, World(ap: 5), sequence, witnesses, out _));
        Check("value-chain hard configured step limit is checked", !Compose(plan, world, sequence, witnesses, out _, maxSteps: 1));
        Check("value-chain remaining turn iteration slots are checked", !Compose(plan, world, sequence, witnesses, out _, slots: 1));
        Check("value-chain no new clock is added", !Compose(plan, world, sequence, witnesses, out _, remainingMs: 99));
        Check("value-chain invalid time cannot grant a sequence", !Compose(plan, world, sequence, witnesses, out _, remainingMs: double.NaN)
            && !Compose(plan, world, sequence, witnesses, out _, remainingMs: double.PositiveInfinity));

        var stale = Proofs(); stale[1] = Proof("rear-key", "rear", AIActionType.Attack, 3, 6, generation: 2);
        Check("value-chain old-generation proof rejected", !Compose(plan, world, sequence, stale, out _));
        var unsafeProof = Proofs(); unsafeProof[1] = Proof("rear-key", "rear", AIActionType.Attack, 3, 6, safe: false);
        Check("S10-value-chain unsafe actor does not acquire a new exception", !Compose(plan, world, sequence, unsafeProof, out _));
        var illegal = Proofs(); illegal[1] = Proof("rear-key", "rear", AIActionType.Attack, 3, 6, legal: false);
        Check("value-chain formal validation required for every actor", !Compose(plan, world, sequence, illegal, out _));
        var wrongHead = Proofs(); wrongHead[0] = Proof("different-key", "front", AIActionType.Attack, 3, 4);
        Check("value-chain coordination cannot replace the selected head", !Compose(plan, world, sequence, wrongHead, out _));
        var duplicateActors = Sequence(); duplicateActors[1].ActorLifeId = "front";
        var repeated = Proofs(); repeated[1] = Proof("rear-key", "front", AIActionType.Attack, 3, 6);
        Check("value-chain one tentative action per actor", !Compose(plan, world, duplicateActors, repeated, out _));
        var otherTarget = Sequence(); otherTarget[1].TargetLifeId = "other";
        Check("value-chain same observed target required", !Compose(plan, world, otherTarget, witnesses, out _));
        var staleTarget = World(pastTarget: true);
        Check("value-chain past sighting is not current target evidence", !Compose(plan, staleTarget, sequence, witnesses, out _));
        Check("value-chain dead target does not retain attack reservations", !Compose(plan, World(targetHP: 0), sequence, witnesses, out _));
        Check("value-chain commander cannot be spent by this adapter", !Compose(plan, World(frontCommander: true), sequence, witnesses, out _));
        var mismatchedDamage = Sequence(); mismatchedDamage[1].ExpectedHpLoss = 7;
        Check("value-chain finite protection witness must match coordinator", !Compose(plan, world, mismatchedDamage, witnesses, out _));
        var insufficient = Sequence(); insufficient[1].ExpectedHpLoss = 5;
        var insufficientProof = Proofs(); insufficientProof[1] = Proof("rear-key", "rear", AIActionType.Attack, 3, 5);
        Check("value-chain incomplete predicted kill is not certified as coordinator chain", !Compose(plan, world, insufficient, insufficientProof, out _));

        var control = Sequence(); control[0].ActionType = AIActionType.SkillUse; control[0].ExpectedHpLoss = 0; control[1].ExpectedHpLoss = 10;
        var controlProof = Proofs(); controlProof[0] = Proof("head-key", "front", AIActionType.SkillUse, 3, 0, control: true);
        controlProof[1] = Proof("rear-key", "rear", AIActionType.Attack, 3, 10);
        Check("S02-value-chain guaranteed control remains an observation dependency", Compose(plan, world, control, controlProof, out var controlNodes)
            && controlNodes[0].OperatorId == "DEBUFF_THEN_ATTACK" && controlNodes[1].Status == AIPlanOperatorStatus.AwaitingObservation);
        controlProof[0] = Proof("head-key", "front", AIActionType.SkillUse, 3, 0);
        Check("S02-value-chain uncertain control not treated as already applied", !Compose(plan, world, control, controlProof, out _));

        var oversized = Sequence(); var oversizedProof = Proofs();
        while (oversized.Count < 6) { oversized.Add(oversized[1]); oversizedProof.Add(oversizedProof[1]); }
        Check("value-chain never exceeds five coordinator actions", !Compose(plan, world, oversized, oversizedProof, out _));
        var config = ScriptableObject.CreateInstance<AICognitiveAIConfig>();
        try
        {
            config.Enabled = true; config.EnableMultiStepSquadPlanning = false;
            Check("feature-off does not change an existing plan", !AICognitiveSquadPlanBuilder.Enrich(plan, world, null, null, config)
                && plan.Steps.Count == 1 && plan.Signature == "fixture-means");
        }
        finally { UnityEngine.Object.DestroyImmediate(config); }
        Debug.Log("[R4Squad] source-contract checks completed " + checks + "; real-board execution is separate.");
    }

    static bool Compose(AICognitivePlan plan, AIObservedWorldModel world, List<AIOperationCoordinationStep> steps,
        List<AICognitiveSquadActionProof> proof, out List<AIPlanOperator> nodes, int maxSteps = 5, int slots = 5, double remainingMs = 500)
        => AICognitiveSquadPlanBuilder.TryComposeObservedChain(plan, world, steps, proof, maxSteps, slots, remainingMs, 50, out nodes);
    static AICognitivePlan Plan()
    {
        var plan = new AICognitivePlan { BattleId = "squad-fixture", OwnerTeam = Team.Enemy, BoardGenerationAtValidation = 1,
            Status = AICognitivePlanStatus.Active, Goal = AIOperationGoal.DestroyEnemyCrystal, TargetObservedId = "target",
            Signature = "fixture-means", PlanId = "fixture-plan", APCost = 3, EffectiveDamage = 2 };
        plan.Steps.Add(new AIPlanOperator { StepId = 0, OperatorId = "ATTACK", ActorLifeId = "front", TargetObservedId = "target",
            ExpectedCost = 3, CostKnown = true, BoundActionKey = "head-key", Status = AIPlanOperatorStatus.Ready });
        return plan;
    }
    static AIObservedWorldModel World(int ap = 20, bool pastTarget = false, int targetHP = 10, bool frontCommander = false)
        => new AIObservedWorldModel("squad-fixture", Team.Enemy, 2, 1, 40, 40, ap, default,
            own: new[] { Unit("front", Team.Enemy, 20, frontCommander), Unit("rear", Team.Enemy, 20, false) },
            observed: new[] { new AIObservedUnitValue("target", Kind.Crystal, Team.Player, new Vector3(3, 0, 3), targetHP, 10,
                0, 0, 0, -1, false, true, true, pastTarget ? 6 : 7,
                pastTarget ? AIWorldFactSource.PastObservation : AIWorldFactSource.CurrentObservation) }, globalTurn: 7);
    static AIObservedUnitValue Unit(string id, Team team, int hp, bool commander)
        => new AIObservedUnitValue(id, Kind.Knight, team, new Vector3(1, 0, 1), hp, hp, 0, 0, 5, 20, commander, false, false, 7);
    static List<AIOperationCoordinationStep> Sequence() => new List<AIOperationCoordinationStep> {
        new AIOperationCoordinationStep { ActorLifeId = "front", TargetLifeId = "target", ActionType = AIActionType.Attack, APCost = 3, ExpectedHpLoss = 4 },
        new AIOperationCoordinationStep { ActorLifeId = "rear", TargetLifeId = "target", ActionType = AIActionType.Attack, APCost = 3, ExpectedHpLoss = 6 } };
    static List<AICognitiveSquadActionProof> Proofs() => new List<AICognitiveSquadActionProof> {
        Proof("head-key", "front", AIActionType.Attack, 3, 4), Proof("rear-key", "rear", AIActionType.Attack, 3, 6) };
    static AICognitiveSquadActionProof Proof(string key, string actor, AIActionType type, int cost, int hpLoss,
        int generation = 1, bool legal = true, bool safe = true, bool control = false)
        => new AICognitiveSquadActionProof(key, actor, "target", type, cost, hpLoss, generation, legal, safe, control);
}
#endif
