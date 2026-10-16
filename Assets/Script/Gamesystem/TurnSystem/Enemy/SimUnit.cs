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
    public BoardActionProfile ActionProfile;
    public FacilityKind Facility;
    public Type Type;
    int hp;
    public int HP
    {
        get => hp;
        set
        {
            if (value < hp && Type == Type.Unit)
            { NaturalRecoverySafeTurns = 0; NaturalRecoveryHpLost = true; }
            hp = value;
        }
    }
    public int MaxHP;
    public int ATK;
    public int DEF;
    public Vector3Int Position;
    public Direction Direction;
    public bool IsBoss;
    public bool IsPrimaryCommander;
    public int AssignedSkillId;
    public int SkillCooldown;
    public int Fatigue;
    public int ShieldTurns;
    public bool ShieldActivated;
    public PassiveSkill Passive;
    public List<SimEffect> Effects;
    // Scalars keep search clones independent without allocating a per-unit state object.
    public int NaturalRecoverySafeTurns;
    public int NaturalRecoveryLastRound = -1;
    public bool NaturalRecoveryHpLost, NaturalRecoveryHasBaseline;

    public bool IsAlive => HP > 0;

    public SimUnit()
    {
        Effects = new List<SimEffect>();
    }

    public SimUnit Clone()
    {
        var c = SimBoardPool.RentUnit();
        c.Facility = Facility;
        c.ActionProfile = ActionProfile;
        c.Id = Id; c.Team = Team; c.Kind = Kind; c.Type = Type;
        c.HP = HP; c.MaxHP = MaxHP; c.ATK = ATK; c.DEF = DEF;
        c.Position = Position; c.Direction = Direction;
        c.IsBoss = IsBoss; c.IsPrimaryCommander = IsPrimaryCommander; c.AssignedSkillId = AssignedSkillId;
        c.SkillCooldown = SkillCooldown; c.Fatigue = Fatigue;
        c.ShieldTurns = ShieldTurns; c.ShieldActivated = ShieldActivated;
        c.Passive = Passive;
        c.NaturalRecoverySafeTurns = NaturalRecoverySafeTurns;
        c.NaturalRecoveryLastRound = NaturalRecoveryLastRound;
        c.NaturalRecoveryHpLost = NaturalRecoveryHpLost;
        c.NaturalRecoveryHasBaseline = NaturalRecoveryHasBaseline;
        for (int i = 0; i < Effects.Count; i++)
            c.Effects.Add(Effects[i]);
        return c;
    }

    public int ApplyDamage(int amount)
    {
        if (amount <= 0 || !IsAlive) return 0;
        int previous = HP;
        HP = (int)System.Math.Max(0, (long)HP - amount);
        return previous - HP;
    }

    public int ApplyHeal(int amount)
    {
        if (amount <= 0 || !IsAlive || HP >= MaxHP) return 0;
        int previous = HP;
        HP = (int)System.Math.Min(MaxHP, (long)HP + amount);
        return HP - previous;
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
    public bool IsMovementBlocked => HasDebuff(StatusEffectType.Freeze);

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
        if (HasDebuff(StatusEffectType.Chill)) bonus += GameConstants.DebuffMoveAPBonus;
        return bonus;
    }
}
