using UnityEngine;

/// <summary>Only commanded normal attacks consume this quota. Skills and passive damage never do.</summary>
public static class NormalAttackTurnRules
{
    public static int Limit => Mathf.Clamp(GameplayTuning.Active?.normalAttacksPerTurn ?? 1, 1, 16);
    public const string ExhaustedReason = "通常攻撃済み（次の自軍ターンから）";
    public static bool Exhausted(Status actor) => actor != null && actor.type == Type.Unit && actor.NormalAttacksThisTurn >= Limit;
    public static void Record(Status actor) { if (actor != null && actor.type == Type.Unit) actor.NormalAttacksThisTurn = Mathf.Min(16, actor.NormalAttacksThisTurn + 1); }
}
