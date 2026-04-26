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
    public const float SwiftPostureMoveReduction = 1;
    public const float InterceptPostureReduction = 0.10f;
    public const float PierceEnhanceBonus        = 0.10f;
    public const float SiegeAdaptBonus           = 0.10f;
    public const int   WatchEyeVisionBonus       = 1;
    public const float TenacityReduction         = 0.10f;
    public const float PursuitInstinctBonus      = 0.10f;
    public const float WallFamiliarDEFBonus      = 0.10f;
    public const float FocusMaintainBonus        = 0.10f;
    public const float FirstAidHealRatio         = 0.03f;
    public const float PoisonCoatChance          = 0.10f;
    public const float FrostBladeChance          = 0.10f;
    public const float ForesightReduction        = 0.05f;
    public const float PressureShotChance        = 0.10f;
    public const int   EfficiencyAPReduction     = 1;

    // レア
    public const float DesperationATKBonus       = 0.20f;
    public const float DesperationHPThreshold    = 0.30f;
    public const float IronWalkReduction         = 0.15f;
    public const float HeightAdaptBonus          = 0.15f;
    public const float LowHuntChance             = 0.15f;
    public const float MagicResistReduction      = 0.15f;
    public const float BreachChance              = 0.15f;
    public const float VenomBladeChance          = 0.15f;
    public const float FrostPressureChillChance  = 0.10f;
    public const float FrostPressureFreezeChance = 0.05f;
    public const float SupportSpreadRatio        = 0.50f;

    // スーパーレア
    public const float ShadowCrossBonus          = 0.25f;
    public const float GuardianZoneReduction     = 0.10f;
    public const float SniperCorrectionBonus     = 0.20f;
    public const float SniperCorrectionMinDist   = 3f;
    public const float HolyReactionHealRatio     = 0.05f;

    // レジェンダリー
    public const float BattlefieldDominationBonus = 0.10f;
    public const float IndomitableWillReduction   = 0.10f;
    public const float ThunderChainChance         = 0.20f;
    public const float IcePrisonChance            = 0.10f;

    // =====================================================================
    //  攻撃側 Special Ability のダメージ倍率修飾
    // =====================================================================
    public static float GetAttackerModifier(Status attacker, Status target, bool isSingleTarget)
    {
        float mod = 0f;
        SpecialAbility sa = attacker.specialAbility;

        if (sa == SpecialAbility.PierceEnhance && isSingleTarget)
            mod += PierceEnhanceBonus;

        if (sa == SpecialAbility.PursuitInstinct && target.HPRatio <= 0.5f)
            mod += PursuitInstinctBonus;

        if (sa == SpecialAbility.FocusMaintain && !attacker.HasMovedThisTurn)
            mod += FocusMaintainBonus;

        if (sa == SpecialAbility.Desperation && attacker.HPRatio <= DesperationHPThreshold)
            mod += DesperationATKBonus;

        if (sa == SpecialAbility.HeightAdapt && IsHigherThan(attacker, target))
            mod += HeightAdaptBonus;

        if (sa == SpecialAbility.ShadowCross && !IsInTargetVision(attacker, target))
            mod += ShadowCrossBonus;

        if (sa == SpecialAbility.SniperCorrection)
        {
            float dist = GridHelper.ChebyshevDistance(attacker.transform.position, target.transform.position);
            if (dist >= SniperCorrectionMinDist)
                mod += SniperCorrectionBonus;
        }

        return mod;
    }

    // =====================================================================
    //  防御側 Special Ability のダメージ倍率修飾
    // =====================================================================
    public static float GetDefenderModifier(Status attacker, Status target)
    {
        float mod = 0f;
        SpecialAbility sa = target.specialAbility;

        if (sa == SpecialAbility.InterceptPosture
            && target.HP == target.MaxHP && !target.InterceptUsedThisTurn)
        {
            mod -= InterceptPostureReduction;
        }

        if (sa == SpecialAbility.Tenacity && target.HPRatio <= 0.5f)
            mod -= TenacityReduction;

        if (sa == SpecialAbility.Foresight && IsInTargetVision(attacker, target))
            mod -= ForesightReduction;

        if (sa == SpecialAbility.IronWalkDefense && target.HasMovedThisTurn)
            mod -= IronWalkReduction;

        if (sa == SpecialAbility.MagicResist && IsRangedOrMagic(attacker.kind))
            mod -= MagicResistReduction;

        if (sa == SpecialAbility.IndomitableWill)
            mod -= IndomitableWillReduction;

        if (sa == SpecialAbility.WallFamiliar && IsAdjacentToBuilding(target))
            mod -= WallFamiliarDEFBonus;

        if (HasNearbyAllyWithAbility(target, SpecialAbility.GuardianZone))
            mod -= GuardianZoneReduction;

        if (HasBattlefieldDominationFrom(attacker, target))
            mod += BattlefieldDominationBonus;

        return mod;
    }

    // =====================================================================
    //  攻撃命中時の Special Ability 効果
    // =====================================================================
    public static void OnAttackHit(Status attacker, Status target, int damage, bool isSingleTarget)
    {
        if (damage <= 0 || target == null || attacker == null) return;

        SpecialAbility sa = attacker.specialAbility;

        TryApplyOnHitDebuff(sa, SpecialAbility.PoisonCoat, isSingleTarget,
            PoisonCoatChance, target, StatusEffectType.Poison, "毒塗り", "毒");

        TryApplyOnHitDebuff(sa, SpecialAbility.FrostBlade, true,
            FrostBladeChance, target, StatusEffectType.Chill, "冷気刃", "冷気");

        TryApplyOnHitDebuff(sa, SpecialAbility.PressureShot, true,
            PressureShotChance, target, StatusEffectType.Weaken, "圧迫射", "弱体");

        TryApplyOnHitDebuff(sa, SpecialAbility.Breach, true,
            BreachChance, target, StatusEffectType.ArmorBreak, "破勢", "破甲");

        TryApplyOnHitDebuff(sa, SpecialAbility.VenomBlade, isSingleTarget,
            VenomBladeChance, target, StatusEffectType.Poison, "猛毒刃", "毒");

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
        if (sa == SpecialAbility.LowHunt && IsHigherThan(attacker, target))
        {
            if (Random.value < LowHuntChance)
            {
                StatusEffectSystem.ApplyDebuff(target, StatusEffectType.Chill);
                Debug.Log($"[SpecialAbility] 低所狩り発動！ {target.kind} に冷気付与");
            }
        }

        // 雷印連鎖: マーク状態の敵に攻撃命中時20%でスタン付与
        if (sa == SpecialAbility.ThunderChain
            && StatusEffectSystem.HasDebuff(target, StatusEffectType.Mark)
            && Random.value < ThunderChainChance)
        {
            StatusEffectSystem.ApplyDebuff(target, StatusEffectType.Stun, 1);
            Debug.Log($"[SpecialAbility] 雷印連鎖発動！ {target.kind} にスタン付与");
        }

        // 氷牢結界: 冷気状態の敵を攻撃時10%で凍結付与
        if (sa == SpecialAbility.IcePrison
            && StatusEffectSystem.HasDebuff(target, StatusEffectType.Chill)
            && Random.value < IcePrisonChance)
        {
            StatusEffectSystem.ApplyDebuff(target, StatusEffectType.Freeze);
            Debug.Log($"[SpecialAbility] 氷牢結界発動！ {target.kind} に凍結付与");
        }

        // 迎撃姿勢: 使用フラグを立てる
        if (target.specialAbility == SpecialAbility.InterceptPosture && !target.InterceptUsedThisTurn)
            target.InterceptUsedThisTurn = true;
    }

    // =====================================================================
    //  致死ダメージ耐え判定（生還本能）
    // =====================================================================
    public static bool TrySurviveLethal(Status target, int damage)
    {
        if (target.specialAbility != SpecialAbility.SurvivalInstinct) return false;
        if (target.SurvivalInstinctUsed) return false;
        if (target.HP - damage > 0) return false;

        target.SurvivalInstinctUsed = true;
        target.HP = 1;
        Debug.Log($"[SpecialAbility] 生還本能発動！ {target.kind} がHP1で耐えた");
        FloatingDamageUI.ShowHeal(target.transform.position, 0);
        return true;
    }

    // =====================================================================
    //  ターン開始時処理
    // =====================================================================
    public static void OnTurnStart(Transform unitParent)
    {
        if (unitParent == null) return;

        Status[] units = unitParent.GetComponentsInChildren<Status>();
        foreach (Status s in units)
        {
            if (s.type != Type.Unit) continue;

            s.ResetTurnFlags();

            if (s.specialAbility == SpecialAbility.PurifyHalo)
                ProcessPurifyHalo(s, units);
        }
    }

    // =====================================================================
    //  ターン終了時処理
    // =====================================================================
    public static void OnTurnEnd(Transform unitParent)
    {
        if (unitParent == null) return;

        Status[] units = unitParent.GetComponentsInChildren<Status>();
        foreach (Status s in units)
        {
            if (s.type != Type.Unit || !s.gameObject.activeSelf) continue;

            if (s.specialAbility == SpecialAbility.FirstAid)
                ProcessFirstAid(s, units);

            if (s.specialAbility == SpecialAbility.HolyReaction)
                ProcessHolyReaction(s, units);
        }
    }

    // =====================================================================
    //  範囲攻撃の Special Ability 効果
    // =====================================================================
    public static float GetAreaAttackModifier(Status attacker, int enemyCount)
    {
        if (attacker.specialAbility == SpecialAbility.SiegeAdapt && enemyCount >= 2)
            return SiegeAdaptBonus;
        return 0f;
    }

    public static void OnAreaAttackComplete(Status attacker, List<Status> targets)
    {
        if (attacker.specialAbility != SpecialAbility.ArtilleryControl) return;
        if (targets == null || targets.Count < 3) return;

        foreach (Status t in targets)
            StatusEffectSystem.ApplyDebuff(t, StatusEffectType.Mark);

        Debug.Log($"[SpecialAbility] 砲撃管制発動！ {targets.Count}体にマーク付与");
    }

    // =====================================================================
    //  支援波及処理
    // =====================================================================
    public static void ProcessSupportSpread(Status caster, Status primaryTarget,
                                             BuffType buff, int healAmount, Transform unitParent)
    {
        if (caster.specialAbility != SpecialAbility.SupportSpread) return;
        if (unitParent == null) return;

        Vector3Int casterCell = GridHelper.ToGrid(primaryTarget.transform.position);
        Status bestAlly = FindNearestAlly(caster, primaryTarget, casterCell,
                                          unitParent.GetComponentsInChildren<Status>());

        if (bestAlly == null) return;

        if (buff != BuffType.None)
        {
            StatusEffectSystem.ApplyBuff(bestAlly, buff);
            Debug.Log($"[SpecialAbility] 支援波及: {bestAlly.kind} にも {buff} 付与");
        }

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
    //  視界ボーナス / AP コスト削減
    // =====================================================================

    public static int GetVisionBonus(Status unit)
    {
        return unit.specialAbility == SpecialAbility.WatchEye ? WatchEyeVisionBonus : 0;
    }

    public static int GetMoveAPReduction(Status unit)
    {
        return unit.specialAbility == SpecialAbility.SwiftPosture && !unit.HasMovedThisTurn
            ? (int)SwiftPostureMoveReduction : 0;
    }

    public static int GetSkillAPReduction(Status unit)
    {
        return unit.specialAbility == SpecialAbility.Efficiency && !unit.FirstSkillUsedThisTurn
            ? EfficiencyAPReduction : 0;
    }

    // =====================================================================
    //  不滅の意志: デバフ無効化判定
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
    //  内部ヘルパー: 条件判定
    // =====================================================================

    /// <summary>攻撃者が防御者より高い位置にいるか</summary>
    private static bool IsHigherThan(Status attacker, Status target)
    {
        return Mathf.RoundToInt(attacker.transform.position.y) > Mathf.RoundToInt(target.transform.position.y);
    }

    /// <summary>攻撃者がターゲットの視界内にいるか</summary>
    private static bool IsInTargetVision(Status unit, Status target)
    {
        if (target.VisionCell == null) return false;
        return target.VisionCell.Contains(GridHelper.ToGrid(unit.transform.position));
    }

    /// <summary>遠距離/魔法系ユニットかどうか</summary>
    private static bool IsRangedOrMagic(Kind kind)
    {
        return kind == Kind.Archer || kind == Kind.Magic || kind == Kind.Crossbow
            || kind == Kind.Magicsniper || kind == Kind.Bomber;
    }

    // =====================================================================
    //  内部ヘルパー: 近傍ユニット検索
    // =====================================================================

    /// <summary>建物隣接マスにいるか（UnitRegistry 使用）</summary>
    private static bool IsAdjacentToBuilding(Status unit)
    {
        Vector3Int uCell = GridHelper.ToGrid(unit.transform.position);

        // UnitRegistry が利用可能ならキャッシュから検索
        if (UnitRegistry.Instance != null)
        {
            return CheckBuildingsAdjacent(UnitRegistry.Instance.PlayerBuildings, uCell)
                || CheckBuildingsAdjacent(UnitRegistry.Instance.EnemyBuildings, uCell);
        }

        // フォールバック: FindObjectsByType
        Status[] allStatuses = Object.FindObjectsByType<Status>(FindObjectsSortMode.None);
        foreach (Status s in allStatuses)
        {
            if (s.type != Type.Building && s.type != Type.Wall) continue;
            if (!s.gameObject.activeSelf) continue;
            if (GridHelper.IsWithinRange(uCell, GridHelper.ToGrid(s.transform.position), 1))
                return true;
        }
        return false;
    }

    private static bool CheckBuildingsAdjacent(IReadOnlyList<Status> buildings, Vector3Int cell)
    {
        for (int i = 0; i < buildings.Count; i++)
        {
            var s = buildings[i];
            if (s == null || !s.gameObject.activeSelf) continue;
            if (GridHelper.IsWithinRange(cell, GridHelper.ToGrid(s.transform.position), 1))
                return true;
        }
        return false;
    }

    /// <summary>周囲1マスに指定 SpecialAbility 持ちの味方がいるか</summary>
    private static bool HasNearbyAllyWithAbility(Status target, SpecialAbility ability)
    {
        Vector3Int tCell = GridHelper.ToGrid(target.transform.position);

        if (UnitRegistry.Instance != null)
        {
            var allies = UnitRegistry.Instance.GetActiveUnits(target.team);
            foreach (Status s in allies)
            {
                if (s == target) continue;
                if (s.specialAbility != ability) continue;
                if (GridHelper.IsWithinRange(tCell, GridHelper.ToGrid(s.transform.position), 1))
                    return true;
            }
            return false;
        }

        Status[] allStatuses = Object.FindObjectsByType<Status>(FindObjectsSortMode.None);
        foreach (Status s in allStatuses)
        {
            if (s == target) continue;
            if (s.type != Type.Unit || !s.gameObject.activeSelf) continue;
            if (s.team != target.team) continue;
            if (s.specialAbility != ability) continue;
            if (GridHelper.IsWithinRange(tCell, GridHelper.ToGrid(s.transform.position), 1))
                return true;
        }
        return false;
    }

    /// <summary>攻撃者チームに BattlefieldDomination 持ちがいて、ターゲットがその視界内か</summary>
    private static bool HasBattlefieldDominationFrom(Status attacker, Status target)
    {
        Vector3Int targetCell = GridHelper.ToGrid(target.transform.position);

        if (UnitRegistry.Instance != null)
        {
            var allies = UnitRegistry.Instance.GetActiveUnits(attacker.team);
            foreach (Status s in allies)
            {
                if (s.specialAbility != SpecialAbility.BattlefieldDomination) continue;
                if (s.VisionCell != null && s.VisionCell.Contains(targetCell))
                    return true;
            }
            return false;
        }

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

    // =====================================================================
    //  内部ヘルパー: デバフ付与の共通パターン
    // =====================================================================

    /// <summary>攻撃命中時に確率でデバフを付与する共通処理</summary>
    private static void TryApplyOnHitDebuff(SpecialAbility current, SpecialAbility required,
                                             bool condition, float chance,
                                             Status target, StatusEffectType debuff,
                                             string abilityName, string debuffName)
    {
        if (current != required || !condition) return;
        if (Random.value < chance)
        {
            StatusEffectSystem.ApplyDebuff(target, debuff);
            Debug.Log($"[SpecialAbility] {abilityName}発動！ {target.kind} に{debuffName}付与");
        }
    }

    // =====================================================================
    //  内部ヘルパー: ターン処理
    // =====================================================================

    /// <summary>周囲1マスの最も近い味方を見つける</summary>
    private static Status FindNearestAlly(Status caster, Status primaryTarget,
                                           Vector3Int centerCell, Status[] units)
    {
        Status bestAlly = null;
        float bestDist = float.MaxValue;

        foreach (Status s in units)
        {
            if (s == primaryTarget || s == caster) continue;
            if (s.type != Type.Unit || !s.gameObject.activeSelf) continue;
            if (s.team != caster.team) continue;

            Vector3Int sCell = GridHelper.ToGrid(s.transform.position);
            int dist = GridHelper.ChebyshevDistance(sCell, centerCell);
            if (dist <= 1 && dist < bestDist)
            {
                bestDist = dist;
                bestAlly = s;
            }
        }
        return bestAlly;
    }

    /// <summary>浄化光輪: 周囲1マスの味方1体の状態異常を1つ解除+守勢付与</summary>
    private static void ProcessPurifyHalo(Status caster, Status[] units)
    {
        Vector3Int cCell = GridHelper.ToGrid(caster.transform.position);

        foreach (Status s in units)
        {
            if (s == caster) continue;
            if (s.type != Type.Unit || !s.gameObject.activeSelf) continue;
            if (!GridHelper.IsWithinRange(cCell, GridHelper.ToGrid(s.transform.position), 1)) continue;

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
                return;
            }
        }
    }

    /// <summary>応急処置: 周囲1マスに味方なしなら最大HP3%回復</summary>
    private static void ProcessFirstAid(Status unit, Status[] units)
    {
        Vector3Int uCell = GridHelper.ToGrid(unit.transform.position);

        foreach (Status s in units)
        {
            if (s == unit) continue;
            if (s.type != Type.Unit || !s.gameObject.activeSelf) continue;
            if (GridHelper.IsWithinRange(uCell, GridHelper.ToGrid(s.transform.position), 1))
                return; // 味方がいるので発動しない
        }

        int heal = Mathf.Max(1, Mathf.RoundToInt(unit.MaxHP * FirstAidHealRatio));
        unit.HP = Mathf.Min(unit.MaxHP, unit.HP + heal);
        Debug.Log($"[SpecialAbility] 応急処置: {unit.kind} が {heal} 回復 (残HP:{unit.HP})");
    }

    /// <summary>聖域反応: 周囲1マスの味方で状態異常持ちのHP5%回復</summary>
    private static void ProcessHolyReaction(Status caster, Status[] units)
    {
        Vector3Int cCell = GridHelper.ToGrid(caster.transform.position);

        foreach (Status s in units)
        {
            if (s == caster) continue;
            if (s.type != Type.Unit || !s.gameObject.activeSelf) continue;
            if (!GridHelper.IsWithinRange(cCell, GridHelper.ToGrid(s.transform.position), 1)) continue;

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
