using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// スキルの実行（ダメージ計算・効果適用・範囲取得）を担当する。
/// BattleSystem と連携してダメージを適用する。
/// </summary>
public class SkillSystem : MonoBehaviour
{
    [Header("参照")]
    public TurnGenerator turnGenerator;
    public BattleSystem battlesystem;
    public MoveGenerator moveGenerator;

    private FactionState _factionState;

    public void Init(FactionState factionState)
    {
        _factionState = factionState;
    }

    // =====================================================================
    //  ダメージ計算（仕様準拠）
    //  通常: 1 + (ATK/6) + ((ATK/2) - (DEF/4))
    //  スキル: 通常ダメージ × スキル倍率
    // =====================================================================
    public static int CalcNormalDamage(Status attacker, Status target)
    {
        return DamageCalculator.CalcNormal(attacker, target);
    }

    public static int CalcSkillDamage(Status attacker, Status target, SkillData skill)
    {
        return DamageCalculator.CalcSkill(attacker, target, skill);
    }

    // =====================================================================
    //  スキル実行（メインエントリ）
    // =====================================================================
    public void ExecuteSkill(Status attacker, Status target, SkillData skill)
    {
        if (skill == null) return;

        Debug.Log($"[SkillSystem] {attacker.kind} がスキル '{skill.Name}' を使用 (AP:{skill.APCost})");

        // スタン中は行動不可
        if (StatusEffectSystem.IsStunned(attacker))
        {
            Debug.Log($"[SkillSystem] {attacker.kind} はスタン中で行動不可");
            return;
        }

        // 攻撃スキル
        if (skill.Multiplier > 0 && target != null)
        {
            ExecuteAttackSkill(attacker, target, skill);
        }

        // 回復スキル
        if (skill.FixedHeal > 0)
        {
            ExecuteHealSkill(attacker, target, skill);
        }

        // バフ付与（自身 or 対象）
        if (skill.GrantBuff != BuffType.None)
        {
            Status buffTarget = skill.BuffToSelf ? attacker : (target ?? attacker);
            StatusEffectSystem.ApplyBuff(buffTarget, skill.GrantBuff);

            // 加速の場合はAP+2即時
            if (skill.GrantBuff == BuffType.Haste && _factionState != null)
            {
                _factionState.ModifyAP(buffTarget.team, 2);
                Debug.Log($"[SkillSystem] 加速 AP+2 ({buffTarget.team})");
            }

            // Special Ability: 支援波及（バフを周囲味方に波及）
            if (!skill.BuffToSelf && buffTarget != attacker)
            {
                Transform unitParent = attacker.team == Team.Player
                    ? turnGenerator.Systems.UnitSetting?.PlayerUnit
                    : turnGenerator.Systems.UnitSetting?.EnemyUnit;
                SpecialAbilitySystem.ProcessSupportSpread(attacker, buffTarget, skill.GrantBuff, 0, unitParent);
            }
        }

        // 特殊効果
        ProcessSpecialEffect(attacker, target, skill);
    }

    // =====================================================================
    //  攻撃スキル実行
    // =====================================================================
    private void ExecuteAttackSkill(Status attacker, Status target, SkillData skill)
    {
        // シールド中はダメージ無効
        if (target.ShieldTurns > 0)
        {
            Debug.Log($"[SkillSystem] {target.kind} はシールド中！ ダメージ無効");
            FloatingDamageUI.ShowShield(target.transform.position);
            return;
        }

        int damage = CalcSkillDamage(attacker, target, skill);
        target.HP = Mathf.Max(0, target.HP - damage);
        Debug.Log($"[SkillSystem] {attacker.kind} → {target.kind} '{skill.Name}' DMG:{damage} 残HP:{target.HP}");

        // フローティングダメージ表示
        bool isKill = target.HP <= 0;
        if (damage > 0)
            FloatingDamageUI.ShowDamage(target.transform.position, damage, isKill);
        else
            FloatingDamageUI.ShowMiss(target.transform.position);

        // 2連撃 / チェイン（DamageCalculator経由で重複排除）
        if (skill.Area == SkillAreaShape.SingleDouble && skill.SecondMultiplier > 0)
        {
            float sealMod = StatusEffectSystem.GetSkillMultiplierModifier(attacker);
            float atk = attacker.ATK * StatusEffectSystem.GetATKModifier(attacker);
            float def = target.DEF * StatusEffectSystem.GetDEFModifier(target);
            int dmg2 = DamageCalculator.CalcSkillFromValues(
                atk, def,
                StatusEffectSystem.GetIncomingDamageModifier(target),
                skill.SecondMultiplier, sealMod);
            target.HP = Mathf.Max(0, target.HP - dmg2);
            Debug.Log($"[SkillSystem] 2段目 DMG:{dmg2} 残HP:{target.HP}");
        }

        // デバフ付与
        if (skill.InflictDebuff != StatusEffectType.None && skill.DebuffChance > 0)
        {
            if (Random.Range(0f, 1f) <= skill.DebuffChance)
            {
                StatusEffectSystem.ApplyDebuff(target, skill.InflictDebuff);
            }
        }

        // Special Ability: 攻撃命中時効果（単体スキル判定）
        bool isSingle = skill.Area == SkillAreaShape.Single
                     || skill.Area == SkillAreaShape.SingleDouble
                     || skill.Area == SkillAreaShape.SingleChain;
        SpecialAbilitySystem.OnAttackHit(attacker, target, damage, isSingle);

        // 反射処理
        StatusEffectSystem.ProcessReflect(target, attacker);
    }

    // =====================================================================
    //  回復スキル実行
    // =====================================================================
    private void ExecuteHealSkill(Status attacker, Status healTarget, SkillData skill)
    {
        Status t = healTarget ?? attacker;
        float healMod = StatusEffectSystem.GetHealModifier(t);
        int heal = Mathf.RoundToInt(skill.FixedHeal * healMod);
        t.HP = Mathf.Min(t.MaxHP, t.HP + heal);
        Debug.Log($"[SkillSystem] {t.kind} を {heal} 回復 (残HP:{t.HP})");
        FloatingDamageUI.ShowHeal(t.transform.position, heal);

        // Special Ability: 支援波及（回復を周囲味方に50%波及）
        Transform unitParent = attacker.team == Team.Player
            ? turnGenerator.Systems.UnitSetting?.PlayerUnit
            : turnGenerator.Systems.UnitSetting?.EnemyUnit;
        SpecialAbilitySystem.ProcessSupportSpread(attacker, t, BuffType.None, heal, unitParent);
    }

    // =====================================================================
    //  範囲スキルの対象座標取得
    // =====================================================================
    public static List<Vector3Int> GetAreaPositions(SkillAreaShape area, Vector3Int center, Direction dir)
    {
        var positions = new List<Vector3Int>();

        switch (area)
        {
            case SkillAreaShape.Single:
            case SkillAreaShape.SingleDouble:
            case SkillAreaShape.SingleChain:
            case SkillAreaShape.Landing1:
                positions.Add(center);
                break;

            case SkillAreaShape.Cross1:
                positions.Add(center);
                positions.Add(center + Vector3Int.right);
                positions.Add(center + Vector3Int.left);
                positions.Add(center + new Vector3Int(0, 0, 1));
                positions.Add(center + new Vector3Int(0, 0, -1));
                break;

            case SkillAreaShape.Cross2:
                positions.Add(center);
                for (int i = 1; i <= 2; i++)
                {
                    positions.Add(center + Vector3Int.right * i);
                    positions.Add(center + Vector3Int.left * i);
                    positions.Add(center + new Vector3Int(0, 0, i));
                    positions.Add(center + new Vector3Int(0, 0, -i));
                }
                break;

            case SkillAreaShape.Line3:
            case SkillAreaShape.Line4:
            case SkillAreaShape.Line5:
            case SkillAreaShape.Line7:
                int lineLen = area == SkillAreaShape.Line3 ? 3 :
                              area == SkillAreaShape.Line4 ? 4 :
                              area == SkillAreaShape.Line5 ? 5 : 7;
                int dz = dir == Direction.S ? -1 : 1;
                for (int i = 1; i <= lineLen; i++)
                    positions.Add(center + new Vector3Int(0, 0, dz * i));
                break;

            case SkillAreaShape.Area2x2:
                for (int x = 0; x <= 1; x++)
                    for (int z = 0; z <= 1; z++)
                        positions.Add(center + new Vector3Int(x, 0, z));
                break;

            case SkillAreaShape.Area3x3:
                for (int x = -1; x <= 1; x++)
                    for (int z = -1; z <= 1; z++)
                        positions.Add(center + new Vector3Int(x, 0, z));
                break;

            case SkillAreaShape.Area5x5:
                for (int x = -2; x <= 2; x++)
                    for (int z = -2; z <= 2; z++)
                        positions.Add(center + new Vector3Int(x, 0, z));
                break;

            case SkillAreaShape.Surround1:
                for (int x = -1; x <= 1; x++)
                    for (int z = -1; z <= 1; z++)
                        positions.Add(center + new Vector3Int(x, 0, z));
                break;

            case SkillAreaShape.Surround2:
                for (int x = -2; x <= 2; x++)
                    for (int z = -2; z <= 2; z++)
                        positions.Add(center + new Vector3Int(x, 0, z));
                break;
        }

        return positions;
    }

    // =====================================================================
    //  範囲攻撃の実行（指定座標内の敵全体に適用）
    // =====================================================================
    public void ExecuteAreaSkill(Status attacker, SkillData skill, List<Status> targets)
    {
        if (skill == null || targets == null) return;

        // Special Ability: 迫撃適応の対象数カウント
        int enemyHitCount = 0;

        foreach (Status t in targets)
        {
            if (skill.Multiplier > 0)
            {
                if (t.ShieldTurns > 0)
                {
                    Debug.Log($"[SkillSystem] {t.kind} はシールド中！ ダメージ無効");
                    continue;
                }

                int damage = CalcSkillDamage(attacker, t, skill);

                // Special Ability: 迫撃適応ボーナス（対象数は全体で計算後に適用）
                float saAreaMod = SpecialAbilitySystem.GetAreaAttackModifier(attacker, targets.Count);
                if (saAreaMod > 0f)
                    damage = Mathf.RoundToInt(damage * (1f + saAreaMod));

                // Special Ability: 致死ダメージ耐え（生還本能）
                if (!SpecialAbilitySystem.TrySurviveLethal(t, damage))
                {
                    t.HP = Mathf.Max(0, t.HP - damage);
                }
                Debug.Log($"[SkillSystem] 範囲 {attacker.kind} → {t.kind} '{skill.Name}' DMG:{damage} 残HP:{t.HP}");

                enemyHitCount++;

                // デバフ付与
                if (skill.InflictDebuff != StatusEffectType.None && skill.DebuffChance > 0)
                {
                    if (Random.Range(0f, 1f) <= skill.DebuffChance)
                        StatusEffectSystem.ApplyDebuff(t, skill.InflictDebuff);
                }

                // Special Ability: 攻撃命中時効果（範囲 = 非単体）
                SpecialAbilitySystem.OnAttackHit(attacker, t, damage, false);

                // 反射
                StatusEffectSystem.ProcessReflect(t, attacker);
            }
        }

        // Special Ability: 砲撃管制（3体以上巻き込み → マーク付与）
        SpecialAbilitySystem.OnAreaAttackComplete(attacker, targets);

        // 自傷処理
        if (skill.FixedDamage > 0)
        {
            attacker.HP = Mathf.Max(0, attacker.HP - skill.FixedDamage);
            Debug.Log($"[SkillSystem] {attacker.kind} 自傷 {skill.FixedDamage} (残HP:{attacker.HP})");
        }
    }

    // =====================================================================
    //  範囲支援スキルの実行（味方にバフ・回復）
    // =====================================================================
    public void ExecuteAreaSupportSkill(Status caster, SkillData skill, List<Status> allies)
    {
        if (skill == null || allies == null) return;

        foreach (Status ally in allies)
        {
            if (skill.FixedHeal > 0)
            {
                float healMod = StatusEffectSystem.GetHealModifier(ally);
                int heal = Mathf.RoundToInt(skill.FixedHeal * healMod);
                ally.HP = Mathf.Min(ally.MaxHP, ally.HP + heal);
                Debug.Log($"[SkillSystem] 範囲回復 {ally.kind} +{heal} (残HP:{ally.HP})");
                FloatingDamageUI.ShowHeal(ally.transform.position, heal);
            }

            if (skill.GrantBuff != BuffType.None)
            {
                StatusEffectSystem.ApplyBuff(ally, skill.GrantBuff);
            }
        }
    }

    // =====================================================================
    //  特殊効果処理
    // =====================================================================
    private void ProcessSpecialEffect(Status attacker, Status target, SkillData skill)
    {
        if (string.IsNullOrEmpty(skill.SpecialEffect)) return;

        switch (skill.SpecialEffect)
        {
            case "SmashBuilding":
                if (target != null && target.type == Type.Building)
                    StatusEffectSystem.ApplyDebuff(target, StatusEffectType.Mark);
                break;

            case "FlamePoison":
                if (target != null && !StatusEffectSystem.HasDebuff(target, StatusEffectType.Poison))
                {
                    if (Random.Range(0f, 1f) <= GameConstants.FlamePoisonBleedChance)
                        StatusEffectSystem.ApplyDebuff(target, StatusEffectType.Bleed);
                }
                break;

            case "ShadowRush":
                if (target != null && attacker != null && target.VisionCell != null)
                {
                    Vector3Int attackerCell = GridHelper.ToGrid(attacker.transform.position);
                    if (!target.VisionCell.Contains(attackerCell))
                    {
                        int bonus = CalcBonusDamage(attacker, target, GameConstants.ShadowRushBonusMultiplier);
                        target.HP = Mathf.Max(0, target.HP - bonus);
                        Debug.Log($"[SkillSystem] シャドウラッシュ追加ダメージ +{bonus}");
                    }
                }
                break;

            case "BloodSacrifice":
                if (attacker != null && attacker.MaxHP > 0)
                {
                    int selfDmg = Mathf.RoundToInt(attacker.MaxHP * GameConstants.BloodSacrificeRatio);
                    attacker.HP = Mathf.Max(0, attacker.HP - selfDmg);
                    Debug.Log($"[SkillSystem] ブラッドサクリファイス自傷 {selfDmg} (残HP:{attacker.HP})");
                }
                break;

            case "PhantomDrive":
                if (_factionState != null)
                {
                    _factionState.ModifyAP(attacker.team, 2);
                    Debug.Log($"[SkillSystem] ファントムドライブ AP+2");
                }
                break;

            case "BastionCall":
                if (target != null)
                    StatusEffectSystem.ApplyBuff(target, BuffType.Barrier);
                break;

            case "DeathSight":
                if (target != null && target.MaxHP > 0)
                {
                    float hpRatio = (float)target.HP / target.MaxHP;
                    if (hpRatio <= GameConstants.LowHPThreshold)
                    {
                        int bonus = CalcBonusDamage(attacker, target, GameConstants.DeathSightBonusMultiplier);
                        target.HP = Mathf.Max(0, target.HP - bonus);
                        Debug.Log($"[SkillSystem] デスサイト追加ダメージ +{bonus} (HP50%以下)");
                    }
                }
                break;

            case "SiegeBreaker":
                if (target != null && target.type == Type.Building)
                {
                    int bonus = CalcBonusDamage(attacker, target, GameConstants.SiegeBreakerBonusMultiplier);
                    target.HP = Mathf.Max(0, target.HP - bonus);
                    Debug.Log($"[SkillSystem] シージブレイカー建物追加 +{bonus}");
                }
                break;

            case "JudgementMark":
                if (target != null)
                    StatusEffectSystem.ApplyDebuff(target, StatusEffectType.Mark);
                break;

            case "LastSignal":
                break;

            case "Catastrophe":
                if (attacker != null)
                {
                    attacker.HP = Mathf.Max(0, attacker.HP - GameConstants.CatastropheSelfDamage);
                    Debug.Log($"[SkillSystem] カタストロフ使用者に{GameConstants.CatastropheSelfDamage}ダメージ (残HP:{attacker.HP})");
                }
                break;
        }
    }

    /// <summary>
    /// 追加ダメージ計算ヘルパー（DamageCalculator経由で重複排除）。
    /// ステータス修飾済みの基礎ダメージに倍率を掛ける。
    /// </summary>
    private static int CalcBonusDamage(Status attacker, Status target, float bonusMultiplier)
    {
        float atk = attacker.ATK * StatusEffectSystem.GetATKModifier(attacker);
        float def = target.DEF * StatusEffectSystem.GetDEFModifier(target);
        float baseDmg = DamageCalculator.CalcRawBase(atk, def);
        return Mathf.Max(0, Mathf.RoundToInt(baseDmg * bonusMultiplier));
    }

}
