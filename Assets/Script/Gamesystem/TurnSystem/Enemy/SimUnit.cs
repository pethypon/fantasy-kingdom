using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  SimUnit — シミュレーション用の軽量ユニットデータ
//  GameObjectに依存せず、コピー可能な値型ベースの表現
// =====================================================================
public class SimUnit
{
    public int Id;
    public Team Team;
    public Kind Kind;
    public Type Type;
    public int HP;
    public int MaxHP;
    public int ATK;
    public int DEF;
    public Vector3Int Position;
    public Direction Direction;
    public bool IsBoss;
    public int AssignedSkillId;
    public int SkillCooldown;
    public int Fatigue;
    public int ShieldTurns;
    public bool ShieldActivated;
    public PassiveSkill Passive;
    public List<SimEffect> Effects;

    public bool IsAlive => HP > 0;

    public SimUnit()
    {
        Effects = new List<SimEffect>();
    }

    public SimUnit Clone()
    {
        var c = SimBoardPool.RentUnit();
        c.Id = Id; c.Team = Team; c.Kind = Kind; c.Type = Type;
        c.HP = HP; c.MaxHP = MaxHP; c.ATK = ATK; c.DEF = DEF;
        c.Position = Position; c.Direction = Direction;
        c.IsBoss = IsBoss; c.AssignedSkillId = AssignedSkillId;
        c.SkillCooldown = SkillCooldown; c.Fatigue = Fatigue;
        c.ShieldTurns = ShieldTurns; c.ShieldActivated = ShieldActivated;
        c.Passive = Passive;
        for (int i = 0; i < Effects.Count; i++)
            c.Effects.Add(Effects[i]);
        return c;
    }

    // ---- ステータス効果チェック ----
    public bool HasDebuff(StatusEffectType t)
    {
        for (int i = 0; i < Effects.Count; i++)
            if (Effects[i].Debuff == t) return true;
        return false;
    }

    public bool HasBuff(BuffType t)
    {
        for (int i = 0; i < Effects.Count; i++)
            if (Effects[i].Buff == t) return true;
        return false;
    }

    public bool IsStunned => HasDebuff(StatusEffectType.Stun);
    public bool IsMovementBlocked => HasDebuff(StatusEffectType.Freeze) || HasDebuff(StatusEffectType.Bind);

    // ---- ステータス修飾値 ----
    public float GetATKMod()
    {
        float mod = 1f;
        if (HasDebuff(StatusEffectType.Weaken)) mod -= GameConstants.WeakenATKReduction;
        if (HasDebuff(StatusEffectType.Chill)) mod -= GameConstants.ChillATKReduction;
        if (HasBuff(BuffType.Offensive)) mod += GameConstants.OffensiveATKBonus;
        return Mathf.Clamp(mod, 0f, 2f);
    }

    public float GetDEFMod()
    {
        float mod = 1f;
        if (HasDebuff(StatusEffectType.ArmorBreak)) mod -= GameConstants.ArmorBreakDEFReduction;
        if (HasBuff(BuffType.Defensive)) mod += GameConstants.DefensiveDEFBonus;
        return Mathf.Clamp(mod, 0f, 2f);
    }

    public float GetIncomingDamageMod()
    {
        float mod = 1f;
        if (HasDebuff(StatusEffectType.Mark)) mod += GameConstants.MarkIncomingDamageIncrease;
        if (HasDebuff(StatusEffectType.Freeze)) mod += GameConstants.FreezeIncomingDamageIncrease;
        if (HasBuff(BuffType.Barrier)) mod -= GameConstants.BarrierDamageReduction;
        return Mathf.Max(0f, mod);
    }

    public int GetMoveAPBonus()
    {
        int bonus = 0;
        if (HasDebuff(StatusEffectType.Slow)) bonus += GameConstants.DebuffMoveAPBonus;
        if (HasDebuff(StatusEffectType.Chill)) bonus += GameConstants.DebuffMoveAPBonus;
        return bonus;
    }
}
