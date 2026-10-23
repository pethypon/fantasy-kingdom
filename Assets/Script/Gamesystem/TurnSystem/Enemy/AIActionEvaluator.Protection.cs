using System.Collections.Generic;
using UnityEngine;

public static partial class AIActionEvaluator
{
    /// <summary>Territory protection is inferred only from owned or observed units, never a global registry.</summary>
    public static bool HasObservedTerritoryIntruder(AIBoardState board, Status boss)
    {
        if (board == null || boss == null || !boss.isWildBoss || !board.IsSpecialActorObserved(boss)) return false;
        bool Inside(Status actor)
        {
            if (actor == null || actor == boss || !actor.IsAlive || actor.type != Type.Unit
                || actor.team != Team.Player && actor.team != Team.Enemy
                || !board.IsSpecialActorObserved(actor)) return false;
            var cell = actor.GridPosition;
            return Mathf.Max(Mathf.Abs(cell.x - boss.wildBossTerritoryCenter.x),
                Mathf.Abs(cell.z - boss.wildBossTerritoryCenter.z)) <= boss.wildBossTerritoryRadius;
        }
        foreach (var actor in board.AliveEnemyUnits) if (Inside(actor)) return true;
        foreach (var actor in board.AlivePlayerUnits) if (Inside(actor)) return true;
        return false;
    }

    public static bool IsObservedWildBossProtected(AIBoardState board, Status target)
        => target != null && target.isWildBoss && !HasObservedTerritoryIntruder(board, target);

    public static bool IsObservedCompleteProtection(AIBoardState board, Status target)
        => target != null && (target.ShieldTurns > 0 || IsObservedWildBossProtected(board, target));

    // One-use damage nullification is deliberately retained: consuming it can be useful.
    // Pure HP/barrier damage against persistent protection cannot justify spending AP.
    public static bool IsIneffectiveProtectedAttack(AIAction action, AIBoardState board)
    {
        if (action == null) return false;
        if (action.ActionType == AIActionType.Attack)
            return IsObservedCompleteProtection(board, action.TargetUnit);
        if (action.ActionType != AIActionType.SkillUse) return false;
        if (action.SpecialCommand != null)
        {
            var ability=SpecialSkillRuntime.Definition(action.SpecialCommand.Caster)?.ability;
            return (ability==UnitSpecialSkill.HeavyWarriorBreak || ability==UnitSpecialSkill.DragonKnightPierce)
                && IsObservedCompleteProtection(board,action.SpecialCommand.Target);
        }
        var skill = action.Skill;
        if (skill == null || skill.Multiplier <= 0 && skill.FixedDamage <= 0
            || skill.FixedHeal > 0 || skill.GrantBuff != BuffType.None
                && (skill.GrantBuff!=BuffType.Defensive || DamageCalculator.UsesLegacyDefence)
            || skill.InflictDebuff != StatusEffectType.None && skill.DebuffChance>0
                && (skill.InflictDebuff!=StatusEffectType.ArmorBreak || DamageCalculator.UsesLegacyDefence)
            || !string.IsNullOrEmpty(skill.SpecialEffect)) return false;
        if (action.AreaTargets != null && action.AreaTargets.Count > 0)
        {
            foreach (var target in action.AreaTargets)
                if (target != null && target.IsAlive && !IsObservedCompleteProtection(board, target)) return false;
            return true;
        }
        return IsObservedCompleteProtection(board, action.TargetUnit);
    }

    public static void RemoveIneffectiveProtectedAttacks(List<AIAction> actions, AIBoardState board)
    {
        for (int i = actions.Count - 1; i >= 0; i--)
            if (IsIneffectiveProtectedAttack(actions[i], board)) actions.RemoveAt(i);
    }
}
