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
            {
                NaturalRecoverySafeTurns = 0; NaturalRecoveryHpLost = true;
                RecordBarrierDamage();
            }
            hp = value;
        }
    }
    public int MaxHP;
    public int ATK;
    public int DEF;
    int barrier;
    public int Barrier
    {
        get => barrier;
        set
        {
            int next = Mathf.Clamp(value, 0, Mathf.Max(0, MaxBarrier));
            if (next < barrier) RecordBarrierDamage();
            barrier = next;
        }
    }
    public int MaxBarrier;
    public int BarrierSafeTurns, BarrierLastRound = -1;
    public int BarrierLastDamagedRound = -1, SimulationRound = -1;
    public bool BarrierDamaged, BarrierHasRecovered, BarrierHasOwnTurnBaseline;
    public Vector3Int Position;
    public Direction Direction;
    public bool IsBoss;
    public bool IsWildBoss;
    public bool WildBossProtected;
    public bool UsesBarrier => !IsPrimaryCommander && BarrierSystem.UsesBarrier(Kind, Type, IsWildBoss);
    public bool IsPrimaryCommander;
    public int AssignedSkillId;
    public int SkillCooldown;
    public int Fatigue;
    public int ShieldTurns;
    public bool HasDamageShield => Kind != Kind.Crystal && ShieldTurns > 0;
    public string LifeIdentity;
    public MainCrystalCounterState CrystalCounter;
    public bool ShieldActivated;
    public PassiveSkill Passive;
    // Definition-bound identity and vision state needed by the Assassin's end-turn passive.
    public bool AssassinPassive;
    public int VisionZeroTurns, ScoutVisionPenaltyTurns, InnateVisionBonus;
    // Immutable current masks are shared by clones until this observer moves or its effects tick.
    public Vector3Int[] CapturedVisionCells;
    public int AssassinLastEndRound = -1;
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
        c.MaxBarrier = MaxBarrier; c.Barrier = Barrier;
        c.BarrierSafeTurns = BarrierSafeTurns; c.BarrierLastRound = BarrierLastRound;
        c.BarrierDamaged = BarrierDamaged;
        c.BarrierLastDamagedRound = BarrierLastDamagedRound; c.SimulationRound = SimulationRound;
        c.BarrierHasRecovered = BarrierHasRecovered; c.BarrierHasOwnTurnBaseline = BarrierHasOwnTurnBaseline;
        c.Position = Position; c.Direction = Direction;
        c.IsBoss = IsBoss; c.IsWildBoss = IsWildBoss; c.WildBossProtected = WildBossProtected;
        c.IsPrimaryCommander = IsPrimaryCommander; c.AssignedSkillId = AssignedSkillId;
        c.SkillCooldown = SkillCooldown; c.Fatigue = Fatigue;
        c.ShieldTurns = ShieldTurns; c.ShieldActivated = ShieldActivated;
        c.LifeIdentity = LifeIdentity; c.CrystalCounter = CrystalCounter?.Copy();
        c.Passive = Passive;
        c.AssassinPassive = AssassinPassive;
        c.VisionZeroTurns = VisionZeroTurns; c.ScoutVisionPenaltyTurns = ScoutVisionPenaltyTurns;
        c.InnateVisionBonus = InnateVisionBonus;
        c.CapturedVisionCells = CapturedVisionCells;
        c.AssassinLastEndRound = AssassinLastEndRound;
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
        if (HasDamageShield || WildBossProtected) { RecordNegatedHit(); return 0; }
        int beforeBarrier = UsesBarrier ? Barrier : 0;
        if (UsesBarrier) Barrier = BarrierSystem.Absorb(Barrier, amount, out amount);
        int hpLoss = ApplyHpDirectDamage(amount);
        return (int)System.Math.Min(int.MaxValue,(long)hpLoss + beforeBarrier - (UsesBarrier ? Barrier : 0));
    }

    void RecordBarrierDamage()
    {
        if (!UsesBarrier) return;
        BarrierSafeTurns = 0; BarrierDamaged = true; BarrierHasRecovered = false;
        BarrierLastDamagedRound = SimulationRound;
    }

    public void RecordNegatedHit()
    {
        if (BarrierCombatSettings.Active.fullyNegatedHitsResetRecovery) RecordBarrierDamage();
    }

    public int ApplyHpDirectDamage(int amount)
    {
        if (amount <= 0 || !IsAlive) return 0;
        int previous = HP;
        HP = (int)System.Math.Max(0, (long)HP - amount);
        if (!IsAlive) Barrier = 0;
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
