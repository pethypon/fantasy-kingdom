using UnityEngine;

/// <summary>
/// ダメージ計算を一元管理するユーティリティクラス。
/// BattleSystem, SkillSystem, AI シミュレーション全てがこの式を使用する。
/// 計算式: 3 + (ATK/4) + ((ATK/2) - (DEF/3))
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
             + ((atk / GameConstants.DamageATKHalf) - (def / GameConstants.DamageDEFDivisor));
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
            case PassiveSkill.StrangeKingAura:
                // HP50%以下で激昂: ATK+25%
                if (attacker.HPRatio <= GameConstants.StrangeKingRageThreshold)
                    multiplier *= GameConstants.StrangeKingRageATKBonus;
                break;

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
        else if (target.passiveskill == PassiveSkill.StrangeKingAura)
        {
            // 異形の王: 被ダメージ-40%（クリスタル級の耐久）
            multiplier *= GameConstants.StrangeKingDamageReduction;
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

        Vector3 aPos = attacker.transform.position;
        Vector3 tPos = target.transform.position;
        int ay = GridHelper.ToGrid(aPos).y;
        int ty = GridHelper.ToGrid(tPos).y;
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
            float dist = GridDistance(aPos, tPos);
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
    /// King指揮オーラ: チェビシェフ距離2以内に生存する味方Kingがいれば ATK/DEF に +10% を付与する。
    /// King自身は対象外（自分自身のATKに適用しない）。
    /// </summary>
    public static float GetKingAuraATKMultiplier(Status unit)
    {
        return HasKingAura(unit) ? GameConstants.KingAuraATKBonus : 1f;
    }

    public static float GetKingAuraDEFMultiplier(Status unit)
    {
        return HasKingAura(unit) ? GameConstants.KingAuraDEFBonus : 1f;
    }

    private static bool HasKingAura(Status unit)
    {
        if (unit == null || unit.kind == Kind.King || unit.kind == Kind.Boss) return false;
        var reg = UnitRegistry.Instance;
        if (reg == null) return false;
        var allies = unit.team == Team.Player ? reg.PlayerUnits : reg.EnemyUnits;
        if (allies == null) return false;
        Vector3Int upos = GridHelper.ToGrid(unit.transform.position);
        for (int i = 0; i < allies.Count; i++)
        {
            var commander = allies[i];
            // 仕様3.3: King または Boss(異形の王) のいずれもオーラ発信源
            if (commander == null || !commander.IsAlive) continue;
            if (commander.kind != Kind.King && commander.kind != Kind.Boss) continue;
            if (GridHelper.IsWithinRange(GridHelper.ToGrid(commander.transform.position), upos, GameConstants.KingAuraRange))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 通常攻撃のダメージを計算する（パッシブ + 状態異常修飾 + Special Ability込み）。
    /// </summary>
    public static int CalcNormal(Status attacker, Status target)
    {
        float atkMod = ApplyUpkeepATKPenalty(attacker, StatusEffectSystem.GetATKModifier(attacker));
        float defMod = ApplyUpkeepDEFPenalty(target, StatusEffectSystem.GetDEFModifier(target));
        float incomingMod = StatusEffectSystem.GetIncomingDamageModifier(target);

        // 指揮オーラ: King周囲2マス以内の味方は ATK/DEF +10%
        atkMod *= GetKingAuraATKMultiplier(attacker);
        defMod *= GetKingAuraDEFMultiplier(target);

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

        // 指揮オーラ
        atkMod *= GetKingAuraATKMultiplier(attacker);
        defMod *= GetKingAuraDEFMultiplier(target);

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

        int ay = GridHelper.ToGrid(attacker.transform.position).y;
        int ty = GridHelper.ToGrid(target.transform.position).y;
        int dy = ty - ay;
        if (dy >= GameConstants.HighGroundYThreshold) return GameConstants.AreaSkillHighTargetMod;
        if (dy <= -GameConstants.HighGroundYThreshold) return GameConstants.AreaSkillLowTargetMod;
        return 1f;
    }

    /// <summary>
    /// ステータス修飾なしの基礎ダメージ（int）を計算する。
    /// AI の簡易見積り用。式: max(0, round(CalcRawBase(atk, def)))
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

    /// <summary>ワールド座標をグリッド座標に変換（GridHelper への薄いラッパ）</summary>
    private static Vector3Int ToGridPos(Vector3 worldPos) => GridHelper.ToGrid(worldPos);

    /// <summary>2点間のグリッド距離（XZ平面、チェビシェフ距離）</summary>
    private static float GridDistance(Vector3 a, Vector3 b) => GridHelper.ChebyshevDistance(a, b);
}
