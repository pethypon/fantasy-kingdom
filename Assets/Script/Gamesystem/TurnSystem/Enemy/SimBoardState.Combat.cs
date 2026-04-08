using UnityEngine;

// =====================================================================
//  SimBoardState.Combat — ダメージ計算 & クリスタルシールド
//  SkillSystem / DamageCalculator と同一の式を使用
// =====================================================================
public partial class SimBoardState
{
    /// <summary>通常攻撃ダメージ計算。</summary>
    public static int CalcDamage(SimUnit attacker, SimUnit defender)
    {
        if (attacker == null || defender == null) return 0;

        float atkMod = attacker.GetATKMod();
        float defMod = defender.GetDEFMod();
        float incomingMod = defender.GetIncomingDamageMod();

        float atk = attacker.ATK * atkMod;
        float def = defender.DEF * defMod;

        float baseDmg = DamageCalculator.CalcRawBase(atk, def);
        baseDmg *= incomingMod;

        return Mathf.Max(0, Mathf.RoundToInt(baseDmg));
    }

    /// <summary>スキルダメージ計算。封技/固定ダメージを反映。</summary>
    public static int CalcSkillDamage(SimUnit caster, SimUnit target, SkillData skill)
    {
        if (skill == null || caster == null || target == null) return 0;

        float atkMod = caster.GetATKMod();
        float defMod = target.GetDEFMod();
        float incomingMod = target.GetIncomingDamageMod();

        float atk = caster.ATK * atkMod;
        float def = target.DEF * defMod;

        float baseDmg = DamageCalculator.CalcRawBase(atk, def);
        float skillMul = skill.Multiplier;

        // 封技修飾
        if (caster.HasDebuff(StatusEffectType.Seal))
            skillMul = Mathf.Max(0, skillMul - GameConstants.SealSkillReduction);

        baseDmg *= skillMul * incomingMod;

        // 固定ダメージ加算
        baseDmg += skill.FixedDamage;

        return Mathf.Max(1, Mathf.RoundToInt(baseDmg));
    }

    /// <summary>クリスタルHPが閾値を下回った際にシールドを1度だけ発動。</summary>
    void CheckCrystalShield(SimUnit target)
    {
        if (target.Kind != Kind.Crystal) return;
        if (target.ShieldActivated) return;
        if (target.MaxHP <= 0 || !target.IsAlive) return;

        float hpRatio = (float)target.HP / target.MaxHP;
        if (hpRatio < GameConstants.CrystalShieldThreshold)
        {
            target.ShieldTurns = GameConstants.CrystalShieldDuration;
            target.ShieldActivated = true;
        }
    }

    int GetSkillCooldownFromRarity(int skillId)
    {
        if (!SkillData.Table.TryGetValue(skillId, out var skill)) return 2;
        switch (skill.Rarity)
        {
            case SkillRarity.Normal: return 1;
            case SkillRarity.Rare: return 2;
            case SkillRarity.SuperRare: return 3;
            case SkillRarity.Legendary: return 4;
            default: return 2;
        }
    }

    // ================================================================
    //  AP計算ヘルパー (APSystem.CalcCost と同一)
    // ================================================================
    public int CalcMoveCost(SimUnit unit)
    {
        return GameConstants.BaseMoveAPCost + unit.Fatigue + unit.GetMoveAPBonus();
    }

    public int CalcAttackCost(SimUnit unit)
    {
        return GameConstants.BaseAttackAPCost + unit.Fatigue;
    }
}
