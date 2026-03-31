using UnityEngine;

/// <summary>
/// ダメージ計算を一元管理するユーティリティクラス。
/// BattleSystem, SkillSystem, AI シミュレーション全てがこの式を使用する。
/// 計算式: 1 + (ATK/6) + ((ATK/2) - (DEF/4))
///
/// パッシブスキル適用順序:
///   基礎ダメージ → 攻撃側パッシブ → 防御側パッシブ → バフ/デバフ → 最終確定
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
    /// 攻撃側パッシブスキルの倍率を計算する。
    /// Assassin: 対象の視界外から攻撃で1.25倍
    /// Archer: 距離に応じて最大+0.75倍、飛行ユニットに1.25倍
    /// Magician: 建物に1.15倍、距離に応じて最大+0.75倍
    /// Guardian: 建物に2.0倍
    /// </summary>
    public static float GetAttackerPassiveMultiplier(Status attacker, Status target)
    {
        float multiplier = 1f;

        switch (attacker.kind)
        {
            case Kind.Assassin:
                // 視界外から攻撃した場合ダメージ1.25倍
                if (target.VisionCell == null ||
                    !target.VisionCell.Contains(ToGridPos(attacker.transform.position)))
                {
                    multiplier *= GameConstants.AssassinShadowstrikeDamage;
                }
                break;

            case Kind.Archer:
                // 距離ボーナス: 1マス+0.25, 2マス+0.5, 3マス+0.75(最大)
                float archerDist = GridDistance(attacker.transform.position, target.transform.position);
                float distBonus = Mathf.Min(archerDist * GameConstants.ArcherDistanceBonusPerTile,
                                            GameConstants.ArcherDistanceBonusMax);
                multiplier += distBonus;
                // 飛行ユニットに1.25倍（将来Flying判定追加時に拡張）
                break;

            case Kind.Magic:
                // 建物に1.15倍
                if (target.type == Type.Building)
                    multiplier *= GameConstants.MagicianBuildingBonus;
                // 距離ボーナス: Archerと同じ
                float magicDist = GridDistance(attacker.transform.position, target.transform.position);
                float magicDistBonus = Mathf.Min(magicDist * GameConstants.ArcherDistanceBonusPerTile,
                                                 GameConstants.ArcherDistanceBonusMax);
                multiplier += magicDistBonus;
                break;

            case Kind.Guardian:
                // 建物に2倍
                if (target.type == Type.Building)
                    multiplier *= GameConstants.GuardianBuildingBonus;
                break;
        }

        return Mathf.Min(multiplier, GameConstants.PassiveMultiplierMax);
    }

    /// <summary>
    /// 防御側パッシブスキルの倍率を計算する。
    /// Knight: 視界内からの攻撃を20%軽減、視界外からの攻撃は10%増加
    /// </summary>
    public static float GetDefenderPassiveMultiplier(Status attacker, Status target)
    {
        float multiplier = 1f;

        if (target.kind == Kind.Knight)
        {
            Vector3Int attackerGrid = ToGridPos(attacker.transform.position);
            if (target.VisionCell != null && target.VisionCell.Contains(attackerGrid))
            {
                // 視界内からの攻撃 → 20%軽減
                multiplier *= GameConstants.KnightVisionDamageReduction;
            }
            else
            {
                // 視界外からの攻撃 → 10%増加
                multiplier *= GameConstants.KnightOutOfVisionDamageIncrease;
            }
        }

        return multiplier;
    }

    /// <summary>
    /// 通常攻撃のダメージを計算する（パッシブ + 状態異常修飾 + Special Ability込み）。
    /// </summary>
    public static int CalcNormal(Status attacker, Status target)
    {
        float atkMod = StatusEffectSystem.GetATKModifier(attacker);
        float defMod = StatusEffectSystem.GetDEFModifier(target);
        float incomingMod = StatusEffectSystem.GetIncomingDamageModifier(target);

        float atk = attacker.ATK * atkMod;
        float def = target.DEF * defMod;

        float baseDmg = CalcRawBase(atk, def);

        // パッシブスキル適用（攻撃側 → 防御側）
        float passiveMod = GetAttackerPassiveMultiplier(attacker, target)
                         * GetDefenderPassiveMultiplier(attacker, target);

        // Special Ability 修飾（通常攻撃は単体扱い）
        float saAttack = SpecialAbilitySystem.GetAttackerModifier(attacker, target, true);
        float saDefend = SpecialAbilitySystem.GetDefenderModifier(attacker, target);

        float finalDmg = baseDmg * passiveMod * (1f + saAttack + saDefend) * incomingMod;
        return Mathf.Max(0, Mathf.RoundToInt(finalDmg));
    }

    /// <summary>
    /// スキル攻撃のダメージを計算する（パッシブ + 状態異常修飾 + スキル倍率 + Special Ability込み）。
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

        // パッシブスキル適用（攻撃側 → 防御側）
        float passiveMod = GetAttackerPassiveMultiplier(attacker, target)
                         * GetDefenderPassiveMultiplier(attacker, target);

        // Special Ability 修飾（単体スキルかどうかで判定）
        bool isSingle = skill.Area == SkillAreaShape.Single
                     || skill.Area == SkillAreaShape.SingleDouble
                     || skill.Area == SkillAreaShape.SingleChain;
        float saAttack = SpecialAbilitySystem.GetAttackerModifier(attacker, target, isSingle);
        float saDefend = SpecialAbilitySystem.GetDefenderModifier(attacker, target);

        float skillDmg = baseDmg * effectiveMultiplier * passiveMod * (1f + saAttack + saDefend) * incomingMod;
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

    // ─── ヘルパー ─────────────────────────────────────────────────────

    /// <summary>ワールド座標をグリッド座標に変換</summary>
    private static Vector3Int ToGridPos(Vector3 worldPos)
    {
        return new Vector3Int(
            Mathf.RoundToInt(worldPos.x),
            Mathf.RoundToInt(worldPos.y),
            Mathf.RoundToInt(worldPos.z));
    }

    /// <summary>2点間のグリッド距離（XZ平面、チェビシェフ距離）</summary>
    private static float GridDistance(Vector3 a, Vector3 b)
    {
        float dx = Mathf.Abs(Mathf.RoundToInt(a.x) - Mathf.RoundToInt(b.x));
        float dz = Mathf.Abs(Mathf.RoundToInt(a.z) - Mathf.RoundToInt(b.z));
        return Mathf.Max(dx, dz);
    }
}
