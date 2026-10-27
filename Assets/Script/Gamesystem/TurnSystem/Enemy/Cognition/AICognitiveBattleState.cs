using System;

/// <summary>Optional AI-only save extension. A restored plan is an intent, never an executable reservation.</summary>
[Serializable]
public sealed class AICognitiveBattleState
{
    public int Version = 1;
    public Team Owner;
    public string BattleId;
    public long NextEventId;
    public AICognitivePlan ActivePlan;
    public AIHypothesisBattleState Hypotheses;
    public string LastReplanReason;
}
