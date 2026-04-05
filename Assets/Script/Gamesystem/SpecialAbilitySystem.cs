using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Special Ability の効果判定・適用を行う静的ユーティリティクラス。
/// 各システム（DamageCalculator, BattleSystem, SkillSystem, etc.）から呼び出される。
/// </summary>
public static class SpecialAbilitySystem
{
    // =====================================================================
    //  定数（GameConstants に追記分と連動）
    // =====================================================================
    // ノーマル
    public const float SwiftPostureMoveReduction = 1;      // 移動AP-1
    public const float InterceptPostureReduction = 0.10f;   // 被ダメ-10%
    public const float PierceEnhanceBonus        = 0.10f;   // 与ダメ+10%
    public const float SiegeAdaptBonus           = 0.10f;   // 与ダメ+10%
    public const int   WatchEyeVisionBonus       = 1;       // 視界+1
    public const float TenacityReduction         = 0.10f;   // 被ダメ-10%
    public const float PursuitInstinctBonus      = 0.10f;   // 与ダメ+10%
    public const float WallFamiliarDEFBonus      = 0.10f;   // DEF+10%
    public const float FocusMaintainBonus        = 0.10f;   // 与ダメ+10%
    public const float FirstAidHealRatio         = 0.03f;   // 最大HP3%
    public const float PoisonCoatChance          = 0.10f;   // 10%
    public const float FrostBladeChance          = 0.10f;   // 10%
    public const float ForesightReduction        = 0.05f;   // 被ダメ-5%
    public const float PressureShotChance        = 0.10f;   // 10%
    public const int   EfficiencyAPReduction     = 1;       // AP-1

    // レア
    public const float DesperationATKBonus       = 0.20f;   // ATK+20%
    public const float DesperationHPThreshold    = 0.30f;   // HP30%以下
    public const float IronWalkReduction         = 0.15f;   // 被ダメ-15%
    public const float HeightAdaptBonus          = 0.15f;   // 与ダメ+15%
    public const float LowHuntChance             = 0.15f;   // 15%
    public const float MagicResistReduction      = 0.15f;   // 被ダメ-15%
    public const float BreachChance              = 0.15f;   // 15%
    public const float VenomBladeChance          = 0.15f;   // 15%
    public const float FrostPressureChillChance  = 0.10f;   // 10%
    public const float FrostPressureFreezeChance = 0.05f;   // 5%
    public const float SupportSpreadRatio        = 0.50f;   // 50%

    // スーパーレア
    public const float ShadowCrossBonus          = 0.25f;   // +0.25倍率
    public const float GuardianZoneReduction     = 0.10f;   // 被ダメ-10%
    public const float SniperCorrectionBonus     = 0.20f;   // 与ダメ+20%
    public const float SniperCorrectionMinDist   = 3f;      // 3マス以上
    public const float HolyReactionHealRatio     = 0.05f;   // 最大HP5%

    // レジェンダリー
    public const float BattlefieldDominationBonus = 0.10f;  // 被ダメ+10%
    public const float IndomitableWillReduction   = 0.10f;  // 被ダメ-10%
    public const float ThunderChainChance         = 0.20f;  // 20%
    public const float IcePrisonChance            = 0.10f;  // 10%

    // =====================================================================
    //  攻撃側 Special Ability のダメージ倍率修飾
    //  DamageCalculator から呼ばれる
    // =====================================================================
    public static float GetAttackerModifier(Status attacker, Status target, bool isSingleTarget)
    {
        float mod = 0f;
        SpecialAbility sa = attacker.specialAbility;

        // 刺突強化: 単体攻撃の与ダメ+10%
        if (sa == SpecialAbility.PierceEnhance && isSingleTarget)
            mod += PierceEnhanceBonus;

        // 追撃本能: HP50%以下の敵への与ダメ+10%
        if (sa == SpecialAbility.PursuitInstinct && target.MaxHP > 0
            && (float)target.HP / target.MaxHP <= 0.5f)
            mod += PursuitInstinctBonus;

        // 集中維持: 未移動なら与ダメ+10%
        if (sa == SpecialAbility.FocusMaintain && !attacker.HasMovedThisTurn)
            mod += FocusMaintainBonus;

        // 背水: HP30%以下でATK+20%（倍率として加算）
        if (sa == SpecialAbility.Desperation && attacker.MaxHP > 0
            && (float)attacker.HP / attacker.MaxHP <= DesperationHPThreshold)
            mod += DesperationATKBonus;

        // 高所順応: 自分が高い位置にいる時、与ダメ+15%
        if (sa == SpecialAbility.HeightAdapt)
        {
            float attackerY = Mathf.RoundToInt(attacker.transform.position.y);
            float targetY = Mathf.RoundToInt(target.transform.position.y);
            if (attackerY > targetY) mod += HeightAdaptBonus;
        }

        // 影渡り: 視界外から攻撃時+0.25
        if (sa == SpecialAbility.ShadowCross)
        {
            if (target.VisionCell != null)
            {
                Vector3Int attackerCell = ToGrid(attacker.transform.position);
                if (!target.VisionCell.Contains(attackerCell))
                    mod += ShadowCrossBonus;
            }
        }

        // 狙撃補正: 3マス以上離れた敵への与ダメ+20%
        if (sa == SpecialAbility.SniperCorrection)
        {
            float dist = GridDistance(attacker.transform.position, target.transform.position);
            if (dist >= SniperCorrectionMinDist)
                mod += SniperCorrectionBonus;
        }

        return mod;
    }

    // =====================================================================
    //  防御側 Special Ability のダメージ倍率修飾
    //  DamageCalculator から呼ばれる
    // =====================================================================
    public static float GetDefenderModifier(Status attacker, Status target)
    {
        float mod = 0f;
        SpecialAbility sa = target.specialAbility;

        // 迎撃姿勢: HP100%時、そのターン最初の被ダメ-10%
        if (sa == SpecialAbility.InterceptPosture
            && target.HP == target.MaxHP && !target.InterceptUsedThisTurn)
        {
            mod -= InterceptPostureReduction;
            // フラグはダメージ適用時にセットする（BattleSystem から）
        }

        // 粘り腰: HP50%以下で被ダメ-10%
        if (sa == SpecialAbility.Tenacity && target.MaxHP > 0
            && (float)target.HP / target.MaxHP <= 0.5f)
            mod -= TenacityReduction;

        // 見切り: 攻撃者が自分の視界内にいるなら被ダメ-5%
        if (sa == SpecialAbility.Foresight && target.VisionCell != null)
        {
            Vector3Int attackerCell = ToGrid(attacker.transform.position);
            if (target.VisionCell.Contains(attackerCell))
                mod -= ForesightReduction;
        }

        // 鉄壁歩法: 移動後、そのターン被ダメ-15%
        if (sa == SpecialAbility.IronWalkDefense && target.HasMovedThisTurn)
            mod -= IronWalkReduction;

        // 耐魔皮膜: 魔法/遠距離から被ダメ-15%
        if (sa == SpecialAbility.MagicResist && IsRangedOrMagic(attacker.kind))
            mod -= MagicResistReduction;

        // 不滅の意志: 被ダメ-10%
        if (sa == SpecialAbility.IndomitableWill)
            mod -= IndomitableWillReduction;

        // 防壁慣れ: 建物隣接マスにいる時（DEF+10%相当 → 被ダメ軽減として近似）
        if (sa == SpecialAbility.WallFamiliar && IsAdjacentToBuilding(target))
            mod -= WallFamiliarDEFBonus;

        // 守護圏: 周囲1マスに GuardianZone 持ちの味方がいれば被ダメ-10%
        if (HasNearbyAllyWithAbility(target, SpecialAbility.GuardianZone))
            mod -= GuardianZoneReduction;

        // 戦場支配: 攻撃者の視界内に BattlefieldDomination 持ちの敵がいれば被ダメ+10%
        if (HasBattlefieldDominationFrom(attacker, target))
            mod += BattlefieldDominationBonus;

        return mod;
    }

    // =====================================================================
    //  攻撃命中時の Special Ability 効果
    //  BattleSystem / SkillSystem から呼ばれる
    // =====================================================================
    public static void OnAttackHit(Status attacker, Status target, int damage, bool isSingleTarget)
    {
        if (damage <= 0 || target == null || attacker == null) return;

        SpecialAbility sa = attacker.specialAbility;

        // 毒塗り: 単体攻撃命中時10%で毒付与
        if (sa == SpecialAbility.PoisonCoat && isSingleTarget)
        {
            if (Random.value < PoisonCoatChance)
            {
                StatusEffectSystem.ApplyDebuff(target, StatusEffectType.Poison);
                Debug.Log($"[SpecialAbility] 毒塗り発動！ {target.kind} に毒付与");
            }
        }

        // 冷気刃: 攻撃命中時10%で冷気付与
        if (sa == SpecialAbility.FrostBlade)
        {
            if (Random.value < FrostBladeChance)
            {
                StatusEffectSystem.ApplyDebuff(target, StatusEffectType.Chill);
                Debug.Log($"[SpecialAbility] 冷気刃発動！ {target.kind} に冷気付与");
            }
        }

        // 圧迫射: 攻撃命中時10%で弱体付与
        if (sa == SpecialAbility.PressureShot)
        {
            if (Random.value < PressureShotChance)
            {
                StatusEffectSystem.ApplyDebuff(target, StatusEffectType.Weaken);
                Debug.Log($"[SpecialAbility] 圧迫射発動！ {target.kind} に弱体付与");
            }
        }

        // 破勢: 攻撃命中時15%で破甲付与
        if (sa == SpecialAbility.Breach)
        {
            if (Random.value < BreachChance)
            {
                StatusEffectSystem.ApplyDebuff(target, StatusEffectType.ArmorBreak);
                Debug.Log($"[SpecialAbility] 破勢発動！ {target.kind} に破甲付与");
            }
        }

        // 猛毒刃: 単体攻撃命中時15%で毒付与
        if (sa == SpecialAbility.VenomBlade && isSingleTarget)
        {
            if (Random.value < VenomBladeChance)
            {
                StatusEffectSystem.ApplyDebuff(target, StatusEffectType.Poison);
                Debug.Log($"[SpecialAbility] 猛毒刃発動！ {target.kind} に毒付与");
            }
        }

        // 氷結圧: 攻撃命中時10%で冷気、5%で凍結付与
        if (sa == SpecialAbility.FrostPressure)
        {
            if (Random.value < FrostPressureChillChance)
            {
                StatusEffectSystem.ApplyDebuff(target, StatusEffectType.Chill);
                Debug.Log($"[SpecialAbility] 氷結圧・冷気発動！ {target.kind} に冷気付与");
            }
            if (Random.value < FrostPressureFreezeChance)
            {
                StatusEffectSystem.ApplyDebuff(target, StatusEffectType.Freeze);
                Debug.Log($"[SpecialAbility] 氷結圧・凍結発動！ {target.kind} に凍結付与");
            }
        }

        // 低所狩り: 高い位置にいる時15%で鈍足付与
        if (sa == SpecialAbility.LowHunt)
        {
            float attackerY = Mathf.RoundToInt(attacker.transform.position.y);
            float targetY = Mathf.RoundToInt(target.transform.position.y);
            if (attackerY > targetY && Random.value < LowHuntChance)
            {
                StatusEffectSystem.ApplyDebuff(target, StatusEffectType.Slow);
                Debug.Log($"[SpecialAbility] 低所狩り発動！ {target.kind} に鈍足付与");
            }
        }

        // 雷印連鎖: マーク状態の敵に攻撃命中時20%でスタン付与
        if (sa == SpecialAbility.ThunderChain)
        {
            if (StatusEffectSystem.HasDebuff(target, StatusEffectType.Mark))
            {
                if (Random.value < ThunderChainChance)
                {
                    StatusEffectSystem.ApplyDebuff(target, StatusEffectType.Stun, 1);
                    Debug.Log($"[SpecialAbility] 雷印連鎖発動！ {target.kind} にスタン付与");
                }
            }
        }

        // 氷牢結界: 冷気状態の敵を攻撃時10%で凍結付与
        if (sa == SpecialAbility.IcePrison)
        {
            if (StatusEffectSystem.HasDebuff(target, StatusEffectType.Chill))
            {
                if (Random.value < IcePrisonChance)
                {
                    StatusEffectSystem.ApplyDebuff(target, StatusEffectType.Freeze);
                    Debug.Log($"[SpecialAbility] 氷牢結界発動！ {target.kind} に凍結付与");
                }
            }
        }

        // 迎撃姿勢: 使用フラグを立てる（ダメージ計算後に呼ばれるため）
        if (target.specialAbility == SpecialAbility.InterceptPosture && !target.InterceptUsedThisTurn)
            target.InterceptUsedThisTurn = true;
    }

    // =====================================================================
    //  致死ダメージ耐え判定（生還本能）
    //  BattleSystem の ApplyDamage から呼ばれる
    // =====================================================================
    public static bool TrySurviveLethal(Status target, int damage)
    {
        if (target.specialAbility != SpecialAbility.SurvivalInstinct) return false;
        if (target.SurvivalInstinctUsed) return false;
        if (target.HP - damage > 0) return false; // 致死ではない

        target.SurvivalInstinctUsed = true;
        target.HP = 1;
        Debug.Log($"[SpecialAbility] 生還本能発動！ {target.kind} がHP1で耐えた");
        FloatingDamageUI.ShowHeal(target.transform.position, 0); // 生存演出
        return true;
    }

    // =====================================================================
    //  ターン開始時処理
    //  PlayerStart / EnemyStart から呼ばれる
    // =====================================================================
    public static void OnTurnStart(Transform unitParent)
    {
        if (unitParent == null) return;

        foreach (Status s in unitParent.GetComponentsInChildren<Status>())
        {
            if (s.type != Type.Unit) continue;

            // フラグリセット
            s.HasMovedThisTurn = false;
            s.FirstSkillUsedThisTurn = false;
            s.DebuffNullifiedThisTurn = false;
            s.InterceptUsedThisTurn = false;

            // 浄化光輪: 周囲1マスの味方1体の状態異常を1つ解除+守勢
            if (s.specialAbility == SpecialAbility.PurifyHalo)
                ProcessPurifyHalo(s, unitParent);
        }
    }

    // =====================================================================
    //  ターン終了時処理
    //  PlayerMove.HandleTurnEnd / EnemyMove から呼ばれる
    // =====================================================================
    public static void OnTurnEnd(Transform unitParent)
    {
        if (unitParent == null) return;

        foreach (Status s in unitParent.GetComponentsInChildren<Status>())
        {
            if (s.type != Type.Unit || !s.gameObject.activeSelf) continue;

            // 応急処置: 周囲1マスに味方なしなら最大HP3%回復
            if (s.specialAbility == SpecialAbility.FirstAid)
                ProcessFirstAid(s, unitParent);

            // 聖域反応: 周囲1マスの味方が状態異常持ちならHP5%回復
            if (s.specialAbility == SpecialAbility.HolyReaction)
                ProcessHolyReaction(s, unitParent);
        }
    }

    // =====================================================================
    //  範囲攻撃の Special Ability 効果（迫撃適応 / 砲撃管制）
    //  SkillSystem.ExecuteAreaSkill から呼ばれる
    // =====================================================================
    public static float GetAreaAttackModifier(Status attacker, int enemyCount)
    {
        float mod = 0f;

        // 迫撃適応: 2体以上巻き込みで+10%
        if (attacker.specialAbility == SpecialAbility.SiegeAdapt && enemyCount >= 2)
            mod += SiegeAdaptBonus;

        return mod;
    }

    public static void OnAreaAttackComplete(Status attacker, List<Status> targets)
    {
        if (attacker.specialAbility != SpecialAbility.ArtilleryControl) return;
        if (targets == null || targets.Count < 3) return;

        // 砲撃管制: 3体以上巻き込みで全員にマーク付与
        foreach (Status t in targets)
        {
            StatusEffectSystem.ApplyDebuff(t, StatusEffectType.Mark);
        }
        Debug.Log($"[SpecialAbility] 砲撃管制発動！ {targets.Count}体にマーク付与");
    }

    // =====================================================================
    //  支援波及処理
    //  SkillSystem から呼ばれる
    // =====================================================================
    public static void ProcessSupportSpread(Status caster, Status primaryTarget,
                                             BuffType buff, int healAmount, Transform unitParent)
    {
        if (caster.specialAbility != SpecialAbility.SupportSpread) return;
        if (unitParent == null) return;

        Vector3Int casterCell = ToGrid(primaryTarget.transform.position);
        Status bestAlly = null;
        float bestDist = float.MaxValue;

        foreach (Status s in unitParent.GetComponentsInChildren<Status>())
        {
            if (s == primaryTarget || s == caster) continue;
            if (s.type != Type.Unit || !s.gameObject.activeSelf) continue;
            if (s.team != caster.team) continue;

            Vector3Int sCell = ToGrid(s.transform.position);
            float dist = Mathf.Max(Mathf.Abs(sCell.x - casterCell.x), Mathf.Abs(sCell.z - casterCell.z));
            if (dist <= 1 && dist < bestDist)
            {
                bestDist = dist;
                bestAlly = s;
            }
        }

        if (bestAlly == null) return;

        // バフ波及
        if (buff != BuffType.None)
        {
            StatusEffectSystem.ApplyBuff(bestAlly, buff);
            Debug.Log($"[SpecialAbility] 支援波及: {bestAlly.kind} にも {buff} 付与");
        }

        // 回復波及（50%）
        if (healAmount > 0)
        {
            int spreadHeal = Mathf.RoundToInt(healAmount * SupportSpreadRatio);
            float healMod = StatusEffectSystem.GetHealModifier(bestAlly);
            spreadHeal = Mathf.RoundToInt(spreadHeal * healMod);
            bestAlly.HP = Mathf.Min(bestAlly.MaxHP, bestAlly.HP + spreadHeal);
            Debug.Log($"[SpecialAbility] 支援波及: {bestAlly.kind} を {spreadHeal} 回復");
            FloatingDamageUI.ShowHeal(bestAlly.transform.position, spreadHeal);
        }
    }

    // =====================================================================
    //  視界ボーナス
    //  VisionGenerator / StatusEffectSystem から呼ばれる
    // =====================================================================
    public static int GetVisionBonus(Status unit)
    {
        if (unit.specialAbility == SpecialAbility.WatchEye)
            return WatchEyeVisionBonus;
        return 0;
    }

    // =====================================================================
    //  移動APコスト削減（迅速体勢）
    //  APSystem から呼ばれる
    // =====================================================================
    public static int GetMoveAPReduction(Status unit)
    {
        if (unit.specialAbility == SpecialAbility.SwiftPosture && !unit.HasMovedThisTurn)
            return (int)SwiftPostureMoveReduction;
        return 0;
    }

    // =====================================================================
    //  スキルAPコスト削減（省力化）
    //  APSystem から呼ばれる
    // =====================================================================
    public static int GetSkillAPReduction(Status unit)
    {
        if (unit.specialAbility == SpecialAbility.Efficiency && !unit.FirstSkillUsedThisTurn)
            return EfficiencyAPReduction;
        return 0;
    }

    // =====================================================================
    //  不滅の意志: デバフ無効化判定
    //  StatusEffectSystem から呼ばれる
    // =====================================================================
    public static bool TryNullifyDebuff(Status target)
    {
        if (target.specialAbility != SpecialAbility.IndomitableWill) return false;
        if (target.DebuffNullifiedThisTurn) return false;

        target.DebuffNullifiedThisTurn = true;
        Debug.Log($"[SpecialAbility] 不滅の意志発動！ {target.kind} が状態異常を無効化");
        return true;
    }

    // =====================================================================
    //  内部ヘルパー
    // =====================================================================

    private static Vector3Int ToGrid(Vector3 pos)
    {
        return new Vector3Int(
            Mathf.RoundToInt(pos.x),
            Mathf.RoundToInt(pos.y),
            Mathf.RoundToInt(pos.z));
    }

    private static float GridDistance(Vector3 a, Vector3 b)
    {
        float dx = Mathf.Abs(Mathf.RoundToInt(a.x) - Mathf.RoundToInt(b.x));
        float dz = Mathf.Abs(Mathf.RoundToInt(a.z) - Mathf.RoundToInt(b.z));
        return Mathf.Max(dx, dz);
    }

    /// <summary>遠距離/魔法系ユニットかどうか</summary>
    private static bool IsRangedOrMagic(Kind kind)
    {
        return kind == Kind.Archer || kind == Kind.Magic || kind == Kind.Crossbow
            || kind == Kind.Magicsniper || kind == Kind.Bomber;
    }

    /// <summary>建物隣接マスにいるか</summary>
    private static bool IsAdjacentToBuilding(Status unit)
    {
        Vector3Int uCell = ToGrid(unit.transform.position);

        // シーン内の全Statusから建物を探索（パフォーマンスは許容範囲）
        Status[] allStatuses = Object.FindObjectsByType<Status>(FindObjectsSortMode.None);
        foreach (Status s in allStatuses)
        {
            if (s.type != Type.Building && s.type != Type.Wall) continue;
            if (!s.gameObject.activeSelf) continue;
            Vector3Int bCell = ToGrid(s.transform.position);
            if (Mathf.Abs(uCell.x - bCell.x) <= 1 && Mathf.Abs(uCell.z - bCell.z) <= 1)
                return true;
        }
        return false;
    }

    /// <summary>周囲1マスに指定 SpecialAbility 持ちの味方がいるか</summary>
    private static bool HasNearbyAllyWithAbility(Status target, SpecialAbility ability)
    {
        Vector3Int tCell = ToGrid(target.transform.position);

        Status[] allStatuses = Object.FindObjectsByType<Status>(FindObjectsSortMode.None);
        foreach (Status s in allStatuses)
        {
            if (s == target) continue;
            if (s.type != Type.Unit || !s.gameObject.activeSelf) continue;
            if (s.team != target.team) continue;
            if (s.specialAbility != ability) continue;
            Vector3Int sCell = ToGrid(s.transform.position);
            if (Mathf.Abs(tCell.x - sCell.x) <= 1 && Mathf.Abs(tCell.z - sCell.z) <= 1)
                return true;
        }
        return false;
    }

    /// <summary>攻撃者チームに BattlefieldDomination 持ちがいて、ターゲットがその視界内か</summary>
    private static bool HasBattlefieldDominationFrom(Status attacker, Status target)
    {
        Vector3Int targetCell = ToGrid(target.transform.position);

        Status[] allStatuses = Object.FindObjectsByType<Status>(FindObjectsSortMode.None);
        foreach (Status s in allStatuses)
        {
            if (s.type != Type.Unit || !s.gameObject.activeSelf) continue;
            if (s.team != attacker.team) continue;
            if (s.specialAbility != SpecialAbility.BattlefieldDomination) continue;
            if (s.VisionCell != null && s.VisionCell.Contains(targetCell))
                return true;
        }
        return false;
    }

    /// <summary>浄化光輪: 周囲1マスの味方1体の状態異常を1つ解除+守勢付与</summary>
    private static void ProcessPurifyHalo(Status caster, Transform unitParent)
    {
        Vector3Int cCell = ToGrid(caster.transform.position);

        foreach (Status s in unitParent.GetComponentsInChildren<Status>())
        {
            if (s == caster) continue;
            if (s.type != Type.Unit || !s.gameObject.activeSelf) continue;

            Vector3Int sCell = ToGrid(s.transform.position);
            if (Mathf.Abs(cCell.x - sCell.x) > 1 || Mathf.Abs(cCell.z - sCell.z) > 1) continue;

            // 状態異常を持っている味方を探す
            ActiveEffect debuffToRemove = null;
            for (int i = 0; i < s.ActiveEffects.Count; i++)
            {
                if (s.ActiveEffects[i].IsDebuff)
                {
                    debuffToRemove = s.ActiveEffects[i];
                    break;
                }
            }

            if (debuffToRemove != null)
            {
                Debug.Log($"[SpecialAbility] 浄化光輪: {s.kind} の {debuffToRemove.debuffType} を解除");
                s.ActiveEffects.Remove(debuffToRemove);
                StatusEffectSystem.ApplyBuff(s, BuffType.Defensive);
                return; // 1体のみ
            }
        }
    }

    /// <summary>応急処置: 周囲1マスに味方なしなら最大HP3%回復</summary>
    private static void ProcessFirstAid(Status unit, Transform unitParent)
    {
        Vector3Int uCell = ToGrid(unit.transform.position);

        foreach (Status s in unitParent.GetComponentsInChildren<Status>())
        {
            if (s == unit) continue;
            if (s.type != Type.Unit || !s.gameObject.activeSelf) continue;

            Vector3Int sCell = ToGrid(s.transform.position);
            if (Mathf.Abs(uCell.x - sCell.x) <= 1 && Mathf.Abs(uCell.z - sCell.z) <= 1)
                return; // 味方がいるので発動しない
        }

        // 味方なし → 回復
        int heal = Mathf.Max(1, Mathf.RoundToInt(unit.MaxHP * FirstAidHealRatio));
        unit.HP = Mathf.Min(unit.MaxHP, unit.HP + heal);
        Debug.Log($"[SpecialAbility] 応急処置: {unit.kind} が {heal} 回復 (残HP:{unit.HP})");
    }

    /// <summary>聖域反応: 周囲1マスの味方で状態異常持ちのHP5%回復</summary>
    private static void ProcessHolyReaction(Status caster, Transform unitParent)
    {
        Vector3Int cCell = ToGrid(caster.transform.position);

        foreach (Status s in unitParent.GetComponentsInChildren<Status>())
        {
            if (s == caster) continue;
            if (s.type != Type.Unit || !s.gameObject.activeSelf) continue;

            Vector3Int sCell = ToGrid(s.transform.position);
            if (Mathf.Abs(cCell.x - sCell.x) > 1 || Mathf.Abs(cCell.z - sCell.z) > 1) continue;

            // 状態異常持ちか
            bool hasDebuff = false;
            for (int i = 0; i < s.ActiveEffects.Count; i++)
            {
                if (s.ActiveEffects[i].IsDebuff) { hasDebuff = true; break; }
            }

            if (hasDebuff)
            {
                int heal = Mathf.Max(1, Mathf.RoundToInt(s.MaxHP * HolyReactionHealRatio));
                s.HP = Mathf.Min(s.MaxHP, s.HP + heal);
                Debug.Log($"[SpecialAbility] 聖域反応: {s.kind} を {heal} 回復 (残HP:{s.HP})");
            }
        }
    }
}
