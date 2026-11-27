using UnityEngine;

public static partial class SpecialSkillRuntime
{
    public static bool IsSpiritOrShieldSkill(UnitSpecialSkill skill)
        => skill >= UnitSpecialSkill.SpiritPurify && skill <= UnitSpecialSkill.ShieldStance;

    static bool HasRemovableDebuff(Status target)
    {
        var state = State(target);
        return target.ActiveEffects.Exists(effect => effect.IsDebuff) || state.deathCurseTurns > 0
            || state.iceBindTurns > 0 || state.visionZeroTurns > 0 || state.scoutVisionPenaltyTurns > 0
            || state.nextDamageDouble || state.tauntTurns > 0;
    }

    static bool LegalSpiritTarget(UnitSpecialSkill skill, Status caster, Status target, out string reason)
    {
        reason = null;
        if (skill == UnitSpecialSkill.ShieldStance)
            return (target == null || target == caster) && !State(caster).shieldStance || Reject(out reason, "自分に盾の構えを使用します");
        if (skill == UnitSpecialSkill.DeathCurse || skill == UnitSpecialSkill.LightningStrike || skill == UnitSpecialSkill.IceBind)
        {
            if (!EnemyTarget(caster, target, true, out reason) || target.IsPrimaryCommander)
                return Reject(out reason, "視界内の敵の一般駒を選択してください");
            if (skill == UnitSpecialSkill.IceBind && State(target).iceBindTurns > 0) return Reject(out reason, "すでに移動不能です");
            return skill != UnitSpecialSkill.DeathCurse || State(target).deathCurseTurns == 0 || Reject(out reason, "すでに呪われています");
        }
        if (!AllyTarget(caster, target) || target == caster || !OrdinaryUnit(target) || target.IsPrimaryCommander)
            return Reject(out reason, "自分以外の味方の一般駒を選択してください");
        if (skill == UnitSpecialSkill.MartyrWard)
            return GridHelper.ChebyshevDistance(caster.GridPosition, target.GridPosition) == 1 && State(target).martyrWardTurns == 0
                || Reject(out reason, "身代わりが付いていない隣の味方を選択してください");
        if (!InVision(caster, target)) return Reject(out reason, "自分の視界内の味方を選択してください");
        return skill == UnitSpecialSkill.SpiritPurify
            ? HasRemovableDebuff(target) || Reject(out reason, "解除できる弱体がありません")
            : target.HP < target.MaxHP || Reject(out reason, "HPが満タンです");
    }

    static bool ExecuteSpiritSkill(UnitSpecialSkill skill, Status caster, Status target)
    {
        if (skill == UnitSpecialSkill.ShieldStance) { State(caster).shieldStance = true; return true; }
        var state = State(target);
        switch (skill)
        {
            case UnitSpecialSkill.SpiritPurify:
                target.ActiveEffects.RemoveAll(effect => effect.IsDebuff);
                state.deathCurseTurns = state.iceBindTurns = state.visionZeroTurns = state.scoutVisionPenaltyTurns = state.tauntTurns = 0;
                state.nextDamageDouble = false;
                state.tauntedById = null;
                break;
            case UnitSpecialSkill.MartyrWard: state.martyrWardTurns = 2; break;
            case UnitSpecialSkill.DeathCurse: state.deathCurseTurns = 2; break;
            case UnitSpecialSkill.LightningStrike: ApplyAbilityDamage(caster, target, 40, HpChangeKind.NormalDamage); break;
            case UnitSpecialSkill.LifeTransfer: target.ApplyHeal(Mathf.Max(1, FractionOfMaximum(target, .5f))); break;
            case UnitSpecialSkill.IceBind: state.iceBindTurns = 1; break;
            default: return false;
        }
        ApplyHpChange(caster, caster, caster.HP, HpChangeKind.ForcedDeath, cause: HpChangeCause.Sacrifice);
        return true;
    }

    internal static int AdjustSpiritDamage(int amount, bool frontalGuard, bool cursed)
    {
        long value = amount;
        if (frontalGuard) value = (value * 7 + 5) / 10;
        if (cursed) value = (value * 3 + 1) / 2;
        return (int)System.Math.Min(int.MaxValue, value);
    }

    static bool IsInFront(Status defender, Status attacker)
    {
        if (attacker == null) return false;
        var delta = attacker.GridPosition - defender.GridPosition;
        return delta.x == 0 && delta.z * MovePatterns.DirZ(defender.direction) > 0;
    }
}
