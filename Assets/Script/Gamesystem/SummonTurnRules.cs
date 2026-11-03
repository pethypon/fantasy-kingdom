using System;
using UnityEngine;

/// <summary>One quota per faction, shared by every summon origin. Only a real owner-turn start resets it.</summary>
public sealed class SummonTurnRules
{
    public static int Limit => Mathf.Clamp(GameplayTuning.Active?.summonsPerTurn ?? 1, 1, 16);
    // Preserve saves made with any supported authoring quota, even after lowering it.
    // Restore clamps the spent count; it never grants another slot.
    const int MaximumSavedLimit = 16;
    public static string LimitReason => $"召喚上限：1ターン合計{Limit}体までです";
    public const string AttackReason = "召喚したターンは攻撃不可（次の自軍ターンから）";

    [Serializable]
    public sealed class Snapshot { public int Player, Enemy; }

    int player, enemy;
    static bool Supported(Team team) => team == Team.Player || team == Team.Enemy;
    public int Used(Team team) => team == Team.Player ? player : team == Team.Enemy ? enemy : Limit;
    public int Remaining(Team team) => Limit - Used(team);
    public bool TryReserve(Team team)
    {
        if (!Supported(team) || Used(team) >= Limit) return false;
        if (team == Team.Player) player++; else enemy++;
        return true;
    }
    public void ReleaseFailedSummon(Team team)
    {
        if (team == Team.Player) player = Mathf.Max(0, player - 1);
        else if (team == Team.Enemy) enemy = Mathf.Max(0, enemy - 1);
    }
    public void BeginOwnerTurn(Team team)
    {
        if (team == Team.Player) player = 0;
        else if (team == Team.Enemy) enemy = 0;
    }
    public Snapshot Capture() => new Snapshot { Player = player, Enemy = enemy };
    public void Restore(Snapshot saved)
    {
        player = Mathf.Clamp(saved?.Player ?? 0, 0, Limit);
        enemy = Mathf.Clamp(saved?.Enemy ?? 0, 0, Limit);
    }
    public static bool Valid(Snapshot saved) => saved == null
        || saved.Player >= 0 && saved.Player <= MaximumSavedLimit && saved.Enemy >= 0 && saved.Enemy <= MaximumSavedLimit;

    public static bool AttackLocked(Status actor) => actor != null && actor.type == Type.Unit && actor.SummonAttackLocked;
    public static bool SkillBlocked(Status actor, SkillData skill) => AttackLocked(actor) && IsAttackSkill(skill);
    public static bool DamageBlocked(Status source, Status target, HpChangeCause cause)
    {
        if (!AttackLocked(source) || source == target) return false;
        switch (cause)
        {
            case HpChangeCause.NormalAttack: case HpChangeCause.Skill: case HpChangeCause.Counter:
            case HpChangeCause.CrossbowLightning: case HpChangeCause.Bomb: case HpChangeCause.Explosion: return true;
            default: return false;
        }
    }
    public static bool ModeBlocked(Status actor, bool skill)
    {
        if (!AttackLocked(actor)) return false;
        if (!skill) return true;
        var definition = SpecialSkillRuntime.Definition(actor);
        return definition != null && definition.ability != UnitSpecialSkill.None ? IsAttackSkill(definition.ability)
            : SkillData.Table.TryGetValue(actor.AssignedSkillId, out var legacy) && IsAttackSkill(legacy);
    }
    // Hostile control and delayed attacks are offensive too. Healing, allied support and self-buffs remain usable.
    public static bool IsAttackSkill(UnitSpecialSkill ability)
    {
        switch (ability)
        {
            case UnitSpecialSkill.KnightBreakthrough: case UnitSpecialSkill.MagicRitual:
            case UnitSpecialSkill.AssassinVisionSeal: case UnitSpecialSkill.CrossbowStakes:
            case UnitSpecialSkill.MagicSniperLock: case UnitSpecialSkill.LifeBomb:
            case UnitSpecialSkill.SamuraiCounter: case UnitSpecialSkill.NavigatorPull:
            case UnitSpecialSkill.HeavyWarriorBreak: case UnitSpecialSkill.DragonKnightPierce: return true;
            default: return false;
        }
    }
    public static bool IsAttackSkill(SkillData skill)
    {
        if (skill == null) return false;
        if (skill.Multiplier > 0 || skill.SecondMultiplier > 0 || skill.InflictDebuff != StatusEffectType.None) return true;
        switch (skill.Target)
        {
            case SkillTarget.EnemySingle: case SkillTarget.EnemyOrBuilding: case SkillTarget.LowHPEnemy:
            case SkillTarget.FlyingEnemy: case SkillTarget.DesignatedTile: case SkillTarget.AdjacentCenter:
            case SkillTarget.DirectionLine: case SkillTarget.DesignatedRow: return true;
            default: return false;
        }
    }
}
