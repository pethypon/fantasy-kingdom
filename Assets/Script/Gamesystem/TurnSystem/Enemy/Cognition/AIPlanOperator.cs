using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public enum AIPlanOperatorStatus { Proposed, Ready, AwaitingObservation, Completed, Failed, Invalidated }
public enum AISquadMissionRole { None, Guard, Flank, Bait, Breakthrough, Suppress, RetreatCover, ScoutRoute }

/// <summary>Value-only dependency node. An unbound continuation is an intention, never an executable order.</summary>
[Serializable]
public sealed class AIPlanOperator
{
    public int StepId;
    public string OperatorId, ActorLifeId, TargetObservedId, BoundActionKey;
    public int ExpectedCost;
    public bool CostKnown, PreparationProven;
    public AIPlanOperatorStatus Status;
    public AISquadMissionRole Mission;
    public List<int> Dependencies = new List<int>();
    public List<string> RequiredEvidenceIds = new List<string>();
    public string ExpectedEffect, RevalidationRule, FailureFallback;
    public AIPlanOperator Copy()
    {
        var copy = (AIPlanOperator)MemberwiseClone();
        copy.Dependencies = new List<int>(Dependencies ?? new List<int>());
        copy.RequiredEvidenceIds = new List<string>(RequiredEvidenceIds ?? new List<string>());
        return copy;
    }
}

/// <summary>Stable across saves/object recreation. No Unity InstanceID or locale-dependent formatting.</summary>
public static class AICognitiveActionKey
{
    public static string For(AIAction action)
    {
        if (action == null) return string.Empty;
        var p = action.TargetPos;
        return ((int)action.ActionType).ToString(CultureInfo.InvariantCulture) + "|" +
            (action.Unit != null ? action.Unit.ReflectionLifeId : string.Empty) + "|" +
            (action.TargetUnit != null ? action.TargetUnit.ReflectionLifeId : string.Empty) + "|" +
            p.x.ToString("R", CultureInfo.InvariantCulture) + "," + p.y.ToString("R", CultureInfo.InvariantCulture) + "," +
            p.z.ToString("R", CultureInfo.InvariantCulture) + "|" + action.APCost.ToString(CultureInfo.InvariantCulture) + "|" +
            ((int)action.TargetDirection).ToString(CultureInfo.InvariantCulture) + "|" +
            ((int)action.Facility).ToString(CultureInfo.InvariantCulture) + "|" + (action.FacilityDefinition?.definitionId ?? string.Empty) + "|" +
            ((int)action.SummonKind).ToString(CultureInfo.InvariantCulture) + "|" + (action.SummonDefinition?.definitionId ?? string.Empty) + "|" +
            (action.Skill != null ? action.Skill.Id.ToString(CultureInfo.InvariantCulture) : string.Empty) + "|" +
            SpecialKey(action.SpecialCommand);
    }

    static string SpecialKey(SpecialSkillCommand command)
    {
        if (command == null) return string.Empty;
        var text = new System.Text.StringBuilder();
        var ability = SpecialSkillRuntime.Definition(command.Caster)?.ability ?? UnitSpecialSkill.None;
        text.Append(((int)ability).ToString(CultureInfo.InvariantCulture));
        text.Append(':').Append(command.Caster != null ? command.Caster.ReflectionLifeId : string.Empty);
        text.Append(':').Append(command.Target != null ? command.Target.ReflectionLifeId : string.Empty);
        text.Append(':').Append(command.BerserkerStage.ToString(CultureInfo.InvariantCulture));
        if (command.Sacrifices != null)
        {
            var lives = new List<string>(command.Sacrifices.Count);
            foreach (var sacrifice in command.Sacrifices) if (sacrifice != null) lives.Add(sacrifice.ReflectionLifeId);
            lives.Sort(StringComparer.Ordinal);
            foreach (string life in lives) text.Append(':').Append(life);
        }
        return text.ToString();
    }
}

/// <summary>Optional evidence from the existing formal evaluator/Sim. Defaults do not prove a line-opening or victory.</summary>
public struct AICognitiveActionAssessment
{
    public bool Legal, HardSafe, PreparationProven, ProvenVictory, OpenedAlliedFireLine;
    public float ExpectedGoalProgress, EffectiveDamage, EffectiveSupport, PredictedIncomingDamage, Confidence, ExpectedInformationGain;
    public bool IsFinite => AIWorldFact.Finite(ExpectedGoalProgress) && ExpectedGoalProgress >= 0 &&
        AIWorldFact.Finite(EffectiveDamage) && EffectiveDamage >= 0 && AIWorldFact.Finite(EffectiveSupport) && EffectiveSupport >= 0 &&
        AIWorldFact.Finite(PredictedIncomingDamage) && PredictedIncomingDamage >= 0 &&
        AIWorldFact.Finite(ExpectedInformationGain) && ExpectedInformationGain >= 0 &&
        AIWorldFact.Finite(Confidence) && Confidence >= 0 && Confidence <= 1;
}

public readonly struct AICognitivePlannerLimits
{
    public readonly int MaxPlansPerGoal, MaxPlansTotal, MaxOperatorsPerPlan, MaxEnemyResponses, BeamWidth, MaxKnowledgeMatches;
    public readonly float SwitchMargin;
    public AICognitivePlannerLimits(int maxPlansPerGoal = 6, int maxPlansTotal = 8, int maxOperatorsPerPlan = 6,
        int maxEnemyResponses = 3, int beamWidth = 6, float switchMargin = .18f, int maxKnowledgeMatches = 12)
    {
        MaxPlansPerGoal = Mathf.Clamp(maxPlansPerGoal, 1, 8); MaxPlansTotal = Mathf.Clamp(maxPlansTotal, 1, 8);
        MaxOperatorsPerPlan = Mathf.Clamp(maxOperatorsPerPlan, 1, 6); MaxEnemyResponses = Mathf.Clamp(maxEnemyResponses, 1, 3);
        BeamWidth = Mathf.Clamp(beamWidth, 1, 6); MaxKnowledgeMatches = Mathf.Clamp(maxKnowledgeMatches, 1, 12);
        SwitchMargin = AIWorldFact.Finite(switchMargin) ? Mathf.Clamp(switchMargin, 0, 1) : .18f;
    }
    public static AICognitivePlannerLimits From(AICognitiveAIConfig config) => config == null ? new AICognitivePlannerLimits(6) :
        new AICognitivePlannerLimits(config.MaxPlansPerGoal, config.MaxPlansTotal, config.MaxOperatorsPerPlan,
            config.MaxEnemyResponses, config.BeamWidth, config.SwitchMargin, config.MaxKnowledgeMatches);
}
