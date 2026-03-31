using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// スキルの実行（ダメージ計算・効果適用・範囲取得）を担当する。
/// BattleSystem と連携してダメージを適用する。
/// </summary>
public class SkillSystem : MonoBehaviour
{
    [Header("参照")]
    public TurnGenerater turngenerater;
    public BattleSystem battlesystem;
    public MoveGererater movegenerater;

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
                    ? turngenerater.GetComponent<UnitSetting>()?.PlayerUnit
                    : turngenerater.GetComponent<UnitSetting>()?.EnemyUnit;
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

        // 2連撃 / チェイン
        if (skill.Area == SkillAreaShape.SingleDouble && skill.SecondMultiplier > 0)
        {
            float sealMod = StatusEffectSystem.GetSkillMultiplierModifier(attacker);
            float atk = attacker.ATK * StatusEffectSystem.GetATKModifier(attacker);
            float def = target.DEF * StatusEffectSystem.GetDEFModifier(target);
            float baseDmg = 1f + (atk / 6f) + ((atk / 2f) - (def / 4f));
            float mul2 = Mathf.Clamp(skill.SecondMultiplier + sealMod, 0f, 2f);
            int dmg2 = Mathf.Max(0, Mathf.RoundToInt(baseDmg * mul2 * StatusEffectSystem.GetIncomingDamageModifier(target)));
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
            ? turngenerater.GetComponent<UnitSetting>()?.PlayerUnit
            : turngenerater.GetComponent<UnitSetting>()?.EnemyUnit;
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
                // 建物に当てた場合: 被ダメ+10%（マーク相当）
                if (target != null && target.type == Type.Building)
                    StatusEffectSystem.ApplyDebuff(target, StatusEffectType.Mark);
                break;

            case "FlamePoison":
                // 25%で毒 or 出血（ランダム二択、既に付与判定は上流で行う）
                // 上流で Poison を InflictDebuff に設定済み。出血の代替判定
                if (target != null && !StatusEffectSystem.HasDebuff(target, StatusEffectType.Poison))
                {
                    if (Random.Range(0f, 1f) <= 0.25f)
                        StatusEffectSystem.ApplyDebuff(target, StatusEffectType.Bleed);
                }
                break;

            case "ShadowRush":
                // 視界外からなら追加+0.20 倍率分のダメージ
                if (target != null && attacker != null)
                {
                    // 対象の視界に攻撃者がいなければボーナス
                    if (target.VisionCell != null)
                    {
                        Vector3Int attackerCell = new Vector3Int(
                            Mathf.RoundToInt(attacker.transform.position.x),
                            Mathf.RoundToInt(attacker.transform.position.y),
                            Mathf.RoundToInt(attacker.transform.position.z));
                        if (!target.VisionCell.Contains(attackerCell))
                        {
                            float bonusMul = 0.20f;
                            float atk = attacker.ATK * StatusEffectSystem.GetATKModifier(attacker);
                            float def = target.DEF * StatusEffectSystem.GetDEFModifier(target);
                            float base_ = 1f + (atk / 6f) + ((atk / 2f) - (def / 4f));
                            int bonus = Mathf.Max(0, Mathf.RoundToInt(base_ * bonusMul));
                            target.HP = Mathf.Max(0, target.HP - bonus);
                            Debug.Log($"[SkillSystem] シャドウラッシュ追加ダメージ +{bonus}");
                        }
                    }
                }
                break;

            case "BloodSacrifice":
                // 自身が最大HPの10%自傷
                if (attacker != null && attacker.MaxHP > 0)
                {
                    int selfDmg = Mathf.RoundToInt(attacker.MaxHP * 0.10f);
                    attacker.HP = Mathf.Max(0, attacker.HP - selfDmg);
                    Debug.Log($"[SkillSystem] ブラッドサクリファイス自傷 {selfDmg} (残HP:{attacker.HP})");
                }
                break;

            case "PhantomDrive":
                // 攻撃後 AP+2
                if (_factionState != null)
                {
                    _factionState.ModifyAP(attacker.team, 2);
                    Debug.Log($"[SkillSystem] ファントムドライブ AP+2");
                }
                break;

            case "BastionCall":
                // DEF+25%, 被ダメ-10%（守勢バフで近似）
                // 守勢(DEF+20%)を付与済み。追加として障壁も付与
                if (target != null)
                    StatusEffectSystem.ApplyBuff(target, BuffType.Barrier);
                break;

            case "DeathSight":
                // 対象HP50%以下なら追加+0.30
                if (target != null && target.MaxHP > 0)
                {
                    float hpRatio = (float)target.HP / target.MaxHP;
                    if (hpRatio <= 0.5f)
                    {
                        float atk = attacker.ATK * StatusEffectSystem.GetATKModifier(attacker);
                        float def = target.DEF * StatusEffectSystem.GetDEFModifier(target);
                        float base_ = 1f + (atk / 6f) + ((atk / 2f) - (def / 4f));
                        int bonus = Mathf.Max(0, Mathf.RoundToInt(base_ * 0.30f));
                        target.HP = Mathf.Max(0, target.HP - bonus);
                        Debug.Log($"[SkillSystem] デスサイト追加ダメージ +{bonus} (HP50%以下)");
                    }
                }
                break;

            case "SiegeBreaker":
                // 建物相手なら追加+0.40
                if (target != null && target.type == Type.Building)
                {
                    float atk = attacker.ATK * StatusEffectSystem.GetATKModifier(attacker);
                    float def = target.DEF * StatusEffectSystem.GetDEFModifier(target);
                    float base_ = 1f + (atk / 6f) + ((atk / 2f) - (def / 4f));
                    int bonus = Mathf.Max(0, Mathf.RoundToInt(base_ * 0.40f));
                    target.HP = Mathf.Max(0, target.HP - bonus);
                    Debug.Log($"[SkillSystem] シージブレイカー建物追加 +{bonus}");
                }
                break;

            case "JudgementMark":
                // 中心対象にマーク（範囲攻撃の中心の敵にのみマーク付与）
                if (target != null)
                    StatusEffectSystem.ApplyDebuff(target, StatusEffectType.Mark);
                break;

            case "LastSignal":
                // 範囲内味方 ATK+20%, AP+2
                // ATK+20% は攻勢(+15%)で近似 + 追加処理
                // AP+2 は ExecuteAreaSupportSkill 後に呼び出し元で処理
                break;

            case "Catastrophe":
                // 使用者も固定20ダメージ
                if (attacker != null)
                {
                    attacker.HP = Mathf.Max(0, attacker.HP - 20);
                    Debug.Log($"[SkillSystem] カタストロフ使用者に20ダメージ (残HP:{attacker.HP})");
                }
                break;
        }
    }

}
