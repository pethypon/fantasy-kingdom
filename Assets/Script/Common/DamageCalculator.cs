using UnityEngine;

/// <summary>
/// ダメージ計算を一元管理するユーティリティクラス。
/// BattleSystem, SkillSystem, AI シミュレーション全てがこの式を使用する。
/// 計算式: 3 + (ATK/4) + ((ATK/2) - (DEF/4))
///
/// パッシブスキル適用順序:
///   基礎ダメージ → 攻撃側パッシブ → 防御側パッシブ → 地形補正 → バフ/デバフ → 最終確定
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
    /// Kindベースの従来パッシブ + PassiveSkill 再設計の効果を併算。
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
                float archerDist = GridDistance(attacker.transform.position, target.transform.position);
                float distBonus = Mathf.Min(archerDist * GameConstants.ArcherDistanceBonusPerTile,
                                            GameConstants.ArcherDistanceBonusMax);
                multiplier += distBonus;
                break;

            case Kind.Magic:
                if (target.type == Type.Building)
                    multiplier *= GameConstants.MagicianBuildingBonus;
                float magicDist = GridDistance(attacker.transform.position, target.transform.position);
                float magicDistBonus = Mathf.Min(magicDist * GameConstants.ArcherDistanceBonusPerTile,
                                                 GameConstants.ArcherDistanceBonusMax);
                multiplier += magicDistBonus;
                break;

            case Kind.Guardian:
                if (target.type == Type.Building)
                    multiplier *= GameConstants.GuardianBuildingBonus;
                break;
        }

        // PassiveSkill 再設計: 攻撃側効果
        switch (attacker.passiveskill)
        {
            case PassiveSkill.HunterEyes:
                // 攻撃者の視界内に対象があれば +15%
                if (attacker.VisionCell != null &&
                    attacker.VisionCell.Contains(ToGridPos(target.transform.position)))
                {
                    multiplier *= GameConstants.HunterEyesDamageBonus;
                }
                break;

            case PassiveSkill.Destroyer:
                // 建物/クリスタルへ +30%
                if (target.type == Type.Building || target.kind == Kind.Crystal ||
                    target.kind == Kind.SubCrystal)
                {
                    multiplier *= GameConstants.DestroyerBuildingBonus;
                }
                break;

            case PassiveSkill.Sniper:
                // 距離3以上で +20%
                float sniperDist = GridDistance(attacker.transform.position, target.transform.position);
                if (sniperDist >= GameConstants.SniperMinRange)
                    multiplier *= GameConstants.SniperLongRangeBonus;
                break;
        }

        return Mathf.Min(multiplier, GameConstants.PassiveMultiplierMax);
    }

    /// <summary>
    /// 防御側パッシブスキルの倍率を計算する。
    /// Knight: 視界内からの攻撃を20%軽減、視界外からの攻撃は10%増加
    /// Impregnable: 被ダメ-15%
    /// </summary>
    public static float GetDefenderPassiveMultiplier(Status attacker, Status target)
    {
        float multiplier = 1f;

        if (target.kind == Kind.Knight)
        {
            Vector3Int attackerGrid = ToGridPos(attacker.transform.position);
            if (target.VisionCell != null && target.VisionCell.Contains(attackerGrid))
                multiplier *= GameConstants.KnightVisionDamageReduction;
            else
                multiplier *= GameConstants.KnightOutOfVisionDamageIncrease;
        }

        // PassiveSkill 再設計: 防御側効果
        if (target.passiveskill == PassiveSkill.Impregnable)
        {
            multiplier *= GameConstants.ImpregnableDamageReduction;
        }

        return multiplier;
    }

    /// <summary>
    /// 背面攻撃判定: 対象の向きと攻撃者の相対位置から背後からの攻撃かどうかを判定。
    /// Direction.N の背面は -Z 方向、Direction.S の背面は +Z 方向。
    /// </summary>
    public static bool IsBackAttack(Status attacker, Status target)
    {
        if (attacker == null || target == null) return false;
        Vector3Int atk = ToGridPos(attacker.transform.position);
        Vector3Int tgt = ToGridPos(target.transform.position);
        int dz = atk.z - tgt.z;
        if (target.direction == Direction.N) return dz < 0;
        return dz > 0;
    }

    /// <summary>
    /// 背面攻撃倍率: 背後+15%、Assassinationパッシブ持ちは更に×1.20。
    /// </summary>
    public static float GetBackAttackMultiplier(Status attacker, Status target)
    {
        if (!IsBackAttack(attacker, target)) return 1f;
        float mult = GameConstants.BackAttackBonus;
        if (attacker.passiveskill == PassiveSkill.Assassination)
            mult *= GameConstants.AssassinationBackAttackBonus;
        return mult;
    }

    /// <summary>
    /// 地形ボーナス倍率: 低地→高台攻撃で×1.35、高台から遠距離のY-1対象に×1.10。
    /// 高低差はワールド座標のY（タイル面の高さ）を使用する。
    /// </summary>
    public static float GetTerrainMultiplier(Status attacker, Status target)
    {
        if (attacker == null || target == null) return 1f;

        int ay = Mathf.RoundToInt(attacker.transform.position.y);
        int ty = Mathf.RoundToInt(target.transform.position.y);
        int dy = ty - ay; // プラスなら target が高い

        float mult = 1f;

        // 低地→高台: 全攻撃に×1.35
        if (dy >= GameConstants.HighGroundYThreshold)
        {
            mult *= GameConstants.LowToHighAttackBonus;
        }
        // 高台→低地: 遠距離攻撃かつ 1段差（Y-1）で+10%
        else if (dy <= -GameConstants.HighGroundYThreshold)
        {
            float dist = GridDistance(attacker.transform.position, target.transform.position);
            bool isRanged = IsRangedKind(attacker.kind) && dist >= 2f;
            if (isRanged && dy == -1)
                mult *= GameConstants.HighGroundRangedBonus;
        }

        return mult;
    }

    private static bool IsRangedKind(Kind k)
    {
        return k == Kind.Archer || k == Kind.Magic || k == Kind.Crossbow
            || k == Kind.Magicsniper || k == Kind.Bomber;
    }

    /// <summary>
    /// 通常攻撃のダメージを計算する（パッシブ + 状態異常修飾 + Special Ability込み）。
    /// </summary>
    public static int CalcNormal(Status attacker, Status target)
    {
        float atkMod = ApplyUpkeepATKPenalty(attacker, StatusEffectSystem.GetATKModifier(attacker));
        float defMod = ApplyUpkeepDEFPenalty(target, StatusEffectSystem.GetDEFModifier(target));
        float incomingMod = StatusEffectSystem.GetIncomingDamageModifier(target);

        float atk = attacker.ATK * atkMod;
        float def = target.DEF * defMod;

        float baseDmg = CalcRawBase(atk, def);

        // パッシブスキル適用（攻撃側 → 防御側）
        float passiveMod = GetAttackerPassiveMultiplier(attacker, target)
                         * GetDefenderPassiveMultiplier(attacker, target);

        // 背面攻撃 + 地形効果
        float backMod = GetBackAttackMultiplier(attacker, target);
        float terrainMod = GetTerrainMultiplier(attacker, target);

        // Special Ability 修飾（通常攻撃は単体扱い）
        float saAttack = SpecialAbilitySystem.GetAttackerModifier(attacker, target, true);
        float saDefend = SpecialAbilitySystem.GetDefenderModifier(attacker, target);

        float finalDmg = baseDmg * passiveMod * backMod * terrainMod
                       * (1f + saAttack + saDefend) * incomingMod;
        return Mathf.Max(0, Mathf.RoundToInt(finalDmg));
    }

    /// <summary>維持費未払いペナルティをATK修飾に反映する</summary>
    private static float ApplyUpkeepATKPenalty(Status s, float baseMod)
    {
        if (s == null) return baseMod;
        return baseMod * (1f - s.UpkeepPenaltyATKDEF);
    }

    /// <summary>維持費未払いペナルティをDEF修飾に反映する</summary>
    private static float ApplyUpkeepDEFPenalty(Status s, float baseMod)
    {
        if (s == null) return baseMod;
        return baseMod * (1f - s.UpkeepPenaltyATKDEF);
    }

    /// <summary>
    /// スキル攻撃のダメージを計算する（パッシブ + 状態異常修飾 + スキル倍率 + Special Ability込み）。
    /// </summary>
    public static int CalcSkill(Status attacker, Status target, SkillData skill)
    {
        float atkMod = ApplyUpkeepATKPenalty(attacker, StatusEffectSystem.GetATKModifier(attacker));
        float defMod = ApplyUpkeepDEFPenalty(target, StatusEffectSystem.GetDEFModifier(target));
        float incomingMod = StatusEffectSystem.GetIncomingDamageModifier(target);
        float sealMod = StatusEffectSystem.GetSkillMultiplierModifier(attacker);

        float atk = attacker.ATK * atkMod;
        float def = target.DEF * defMod;

        float baseDmg = CalcRawBase(atk, def);
        float effectiveMultiplier = Mathf.Clamp(skill.Multiplier + sealMod, 0f, 2f);

        // パッシブスキル適用（攻撃側 → 防御側）
        float passiveMod = GetAttackerPassiveMultiplier(attacker, target)
                         * GetDefenderPassiveMultiplier(attacker, target);

        // 背面攻撃 + 地形効果
        float backMod = GetBackAttackMultiplier(attacker, target);
        float terrainMod = GetTerrainMultiplier(attacker, target);

        // 範囲スキル限定の地形修飾（対象の高低差で±）
        float areaTerrainMod = GetAreaSkillTerrainMod(skill, attacker, target);

        // Special Ability 修飾（単体スキルかどうかで判定）
        bool isSingle = skill.Area == SkillAreaShape.Single
                     || skill.Area == SkillAreaShape.SingleDouble
                     || skill.Area == SkillAreaShape.SingleChain;
        float saAttack = SpecialAbilitySystem.GetAttackerModifier(attacker, target, isSingle);
        float saDefend = SpecialAbilitySystem.GetDefenderModifier(attacker, target);

        float skillDmg = baseDmg * effectiveMultiplier * passiveMod * backMod * terrainMod * areaTerrainMod
                       * (1f + saAttack + saDefend) * incomingMod;
        return Mathf.Max(0, Mathf.RoundToInt(skillDmg));
    }

    /// <summary>
    /// 範囲スキル対象の地形修飾: 高台対象は×0.80、低地対象は×1.10（単体スキルは影響なし）。
    /// </summary>
    public static float GetAreaSkillTerrainMod(SkillData skill, Status attacker, Status target)
    {
        if (skill == null || attacker == null || target == null) return 1f;
        bool isArea = !(skill.Area == SkillAreaShape.Single
                     || skill.Area == SkillAreaShape.SingleDouble
                     || skill.Area == SkillAreaShape.SingleChain);
        if (!isArea) return 1f;

        int ay = Mathf.RoundToInt(attacker.transform.position.y);
        int ty = Mathf.RoundToInt(target.transform.position.y);
        int dy = ty - ay;
        if (dy >= GameConstants.HighGroundYThreshold) return GameConstants.AreaSkillHighTargetMod;
        if (dy <= -GameConstants.HighGroundYThreshold) return GameConstants.AreaSkillLowTargetMod;
        return 1f;
    }

    /// <summary>
    /// ステータス修飾なしの基礎ダメージ（int）を計算する。
    /// AI の簡易見積り用。式: max(0, 1 + ATK/6 + (ATK/2 - DEF/4))
    /// </summary>
    public static int EstimateBaseDamage(int atk, int def)
    {
        return Mathf.Max(0, Mathf.RoundToInt(CalcRawBase(atk, def)));
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
