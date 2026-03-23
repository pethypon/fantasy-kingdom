using UnityEngine;

/// <summary>
/// ダメージ計算を一元管理するユーティリティクラス。
/// BattleSystem, SkillSystem, AI シミュレーション全てがこの式を使用する。
/// 計算式: 1 + (ATK/6) + ((ATK/2) - (DEF/4))
/// </summary>
public static class DamageCalculator
{
    /// <summary>
    /// ステータス修飾なしの基本ダメージを計算する。
    /// </summary>
    public static float CalcRawBase(float atk, float def)
    {
        return GameConstants.DamageBase
             + (atk / GameConstants.DamageATKDivisor)
             + ((atk / GameConstants.DamageATKHalf) - (def / GameConstants.DamageDEFQuarter));
    }

    /// <summary>
    /// 通常攻撃のダメージを計算する（状態異常修飾込み）。
    /// </summary>
    public static int CalcNormal(Status attacker, Status target)
    {
        float atkMod = StatusEffectSystem.GetATKModifier(attacker);
        float defMod = StatusEffectSystem.GetDEFModifier(target);
        float incomingMod = StatusEffectSystem.GetIncomingDamageModifier(target);

        float atk = attacker.ATK * atkMod;
        float def = target.DEF * defMod;

        float baseDmg = CalcRawBase(atk, def) * incomingMod;
        return Mathf.Max(0, Mathf.RoundToInt(baseDmg));
    }

    /// <summary>
    /// スキル攻撃のダメージを計算する（状態異常修飾 + スキル倍率込み）。
    /// </summary>
    public static int CalcSkill(Status attacker, Status target, SkillData skill)
    {
        float atkMod = StatusEffectSystem.GetATKModifier(attacker);
        float defMod = StatusEffectSystem.GetDEFModifier(target);
        float incomingMod = StatusEffectSystem.GetIncomingDamageModifier(target);
        float sealMod = StatusEffectSystem.GetSkillMultiplierModifier(attacker);

        float atk = attacker.ATK * atkMod;
        float def = target.DEF * defMod;

        float baseDmg = CalcRawBase(atk, def);
        float effectiveMultiplier = Mathf.Clamp(skill.Multiplier + sealMod, 0f, 2f);
        float skillDmg = baseDmg * effectiveMultiplier * incomingMod;
        return Mathf.Max(0, Mathf.RoundToInt(skillDmg));
    }

    /// <summary>
    /// AI シミュレーション用: float値からダメージを計算する。
    /// </summary>
    public static int CalcFromValues(float atk, float def, float incomingMod)
    {
        float baseDmg = CalcRawBase(atk, def) * incomingMod;
        return Mathf.Max(0, Mathf.RoundToInt(baseDmg));
    }

    /// <summary>
    /// AI シミュレーション用: スキル倍率付きダメージを計算する。
    /// </summary>
    public static int CalcSkillFromValues(float atk, float def, float incomingMod, float skillMultiplier, float sealMod)
    {
        float baseDmg = CalcRawBase(atk, def);
        float effectiveMultiplier = Mathf.Clamp(skillMultiplier + sealMod, 0f, 2f);
        float skillDmg = baseDmg * effectiveMultiplier * incomingMod;
        return Mathf.Max(0, Mathf.RoundToInt(skillDmg));
    }
}
