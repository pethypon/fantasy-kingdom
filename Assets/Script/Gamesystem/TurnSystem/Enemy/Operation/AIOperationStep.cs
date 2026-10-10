using System;
using System.Collections.Generic;

[Serializable]
public sealed class AIOperationStep
{
    public int StepId;
    public AIOperationStepType Type;
    public string Purpose;
    public bool Required, Completed, Failed;
    public int StartTurn, EndTurn;
    public List<long> ActionIds = new List<long>();
    public AIOperationStep Copy()
    {
        var copy = (AIOperationStep)MemberwiseClone();
        copy.ActionIds = ActionIds == null ? new List<long>() : new List<long>(ActionIds);
        return copy;
    }
}

[Serializable]
public sealed class AIOperationRevision
{
    public int Turn;
    public string Reason;
    public AIOperationGoal OldGoal, NewGoal;
    public AIOperationContext OldStartContext, NewStartContext;
    public List<AIOperationStep> OldSteps = new List<AIOperationStep>();
    public List<AIOperationStep> NewSteps = new List<AIOperationStep>();
    public AIOperationRevision Copy()
    {
        var copy = (AIOperationRevision)MemberwiseClone();
        copy.OldSteps = CopySteps(OldSteps); copy.NewSteps = CopySteps(NewSteps);
        copy.OldStartContext = OldStartContext?.Copy(); copy.NewStartContext = NewStartContext?.Copy();
        return copy;
    }
    public static List<AIOperationStep> CopySteps(List<AIOperationStep> source)
    {
        var copy = new List<AIOperationStep>();
        if (source != null) foreach (var item in source) if (item != null) copy.Add(item.Copy());
        return copy;
    }
}

[Serializable]
public sealed class AIOperationActionCredit
{
    public long OperationId, ActionId;
    public float Delta, Contribution;
}
