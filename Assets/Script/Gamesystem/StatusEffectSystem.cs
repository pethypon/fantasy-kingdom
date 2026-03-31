using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 状態異常・バフの付与・解除・ターン経過処理を管理する。
/// MonoBehaviour ではなく static ユーティリティとして実装。
/// </summary>
public static class StatusEffectSystem
{
    // =====================================================================
    //  状態異常の既定ターン数
    // =====================================================================
    public static int DefaultDuration(StatusEffectType t)
    {
        switch (t)
        {
            case StatusEffectType.Poison:
            case StatusEffectType.Curse:
            case StatusEffectType.Bleed:
                return 2;
            default:
                return 1;
        }
    }

    public static int DefaultDuration(BuffType t)
    {
        // 加速は即時（ターン数0）
        return t == BuffType.Haste ? 0 : 1;
    }

    // =====================================================================
    //  状態異常の免疫判定
    // =====================================================================
    public static bool IsImmune(Status target, StatusEffectType effect)
    {
        // クリスタル系は基本的に状態異常無効
        if (target.kind == Kind.Crystal || target.kind == Kind.SubCrystal)
            return true;

        // キングはスタン無効
        if (target.kind == Kind.King && effect == StatusEffectType.Stun)
            return true;

        return false;
    }

    // =====================================================================
    //  デバフ付与
    // =====================================================================
    public static bool ApplyDebuff(Status target, StatusEffectType effect, int turns = -1)
    {
        if (effect == StatusEffectType.None) return false;
        if (IsImmune(target, effect)) return false;

        // Special Ability: 不滅の意志 — 毎ターン最初の状態異常を無効化
        if (SpecialAbilitySystem.TryNullifyDebuff(target)) return false;

        if (turns < 0) turns = DefaultDuration(effect);

        // 重複チェック — 同じ状態異常がある場合
        for (int i = 0; i < target.ActiveEffects.Count; i++)
        {
            var e = target.ActiveEffects[i];
            if (e.debuffType == effect)
            {
                // スタンは延長なし
                if (effect == StatusEffectType.Stun) return false;

                // 残りターンが長い方を採用
                if (turns > e.remainingTurns)
                    e.remainingTurns = turns;
                return false;
            }
        }

        target.ActiveEffects.Add(new ActiveEffect(effect, turns));
        Debug.Log($"[StatusEffect] {target.team} {target.kind} に {effect} 付与 ({turns}T)");
        return true;
    }

    // =====================================================================
    //  バフ付与
    // =====================================================================
    public static bool ApplyBuff(Status target, BuffType buff, int turns = -1)
    {
        if (buff == BuffType.None) return false;

        if (turns < 0) turns = DefaultDuration(buff);

        // 加速は即時（重複判定なし、AP+2 はここでは行わず呼び出し元で処理）
        if (buff == BuffType.Haste)
        {
            Debug.Log($"[StatusEffect] {target.team} {target.kind} に加速（即時AP+2）");
            return true;
        }

        // 重複チェック
        for (int i = 0; i < target.ActiveEffects.Count; i++)
        {
            var e = target.ActiveEffects[i];
            if (e.buffType == buff)
            {
                if (turns > e.remainingTurns)
                    e.remainingTurns = turns;
                return false;
            }
        }

        target.ActiveEffects.Add(new ActiveEffect(buff, turns));
        Debug.Log($"[StatusEffect] {target.team} {target.kind} に {buff} 付与 ({turns}T)");
        return true;
    }

    // =====================================================================
    //  デバフ・バフの有無チェック
    // =====================================================================
    public static bool HasDebuff(Status target, StatusEffectType effect)
    {
        for (int i = 0; i < target.ActiveEffects.Count; i++)
            if (target.ActiveEffects[i].debuffType == effect) return true;
        return false;
    }

    public static bool HasBuff(Status target, BuffType buff)
    {
        for (int i = 0; i < target.ActiveEffects.Count; i++)
            if (target.ActiveEffects[i].buffType == buff) return true;
        return false;
    }

    // =====================================================================
    //  ターン開始時のティック処理（ターン数減算 + DoT ダメージ）
    // =====================================================================
    public static void TickEffects(Status target)
    {
        if (target.ActiveEffects.Count == 0) return;

        // DoT ダメージ（ターン終了時）
        for (int i = 0; i < target.ActiveEffects.Count; i++)
        {
            var e = target.ActiveEffects[i];
            if (e.debuffType == StatusEffectType.Poison)
            {
                int dmg = 8;
                target.HP = Mathf.Max(0, target.HP - dmg);
                Debug.Log($"[StatusEffect] {target.kind} が毒で {dmg} ダメージ (残HP:{target.HP})");
            }
            else if (e.debuffType == StatusEffectType.Bleed)
            {
                int dmg = 6;
                target.HP = Mathf.Max(0, target.HP - dmg);
                Debug.Log($"[StatusEffect] {target.kind} が出血で {dmg} ダメージ (残HP:{target.HP})");
            }
        }

        // ターン数減算 → 0以下は除去
        for (int i = target.ActiveEffects.Count - 1; i >= 0; i--)
        {
            target.ActiveEffects[i].remainingTurns--;
            if (target.ActiveEffects[i].remainingTurns <= 0)
            {
                var removed = target.ActiveEffects[i];
                string name = removed.IsDebuff ? removed.debuffType.ToString() : removed.buffType.ToString();
                Debug.Log($"[StatusEffect] {target.kind} の {name} が解除");
                target.ActiveEffects.RemoveAt(i);
            }
        }
    }

    // =====================================================================
    //  全ユニットのティック
    // =====================================================================
    public static void TickAllUnits(Transform unitParent)
    {
        if (unitParent == null) return;
        foreach (Status s in unitParent.GetComponentsInChildren<Status>())
        {
            if (s.type == Type.Unit)
            {
                TickEffects(s);

                // ユニットシールドのターン経過
                if (s.ShieldTurns > 0)
                {
                    s.ShieldTurns--;
                    Debug.Log($"[StatusEffect] {s.team} {s.kind} シールド残り {s.ShieldTurns} ターン");
                }

                // スキルクールダウンのターン経過
                if (s.SkillCooldown > 0)
                {
                    s.SkillCooldown--;
                    if (s.SkillCooldown == 0)
                        Debug.Log($"[StatusEffect] {s.team} {s.kind} のスキルクールダウンが解除");
                }
            }
        }
    }

    // =====================================================================
    //  全エフェクトクリア
    // =====================================================================
    public static void ClearAllEffects(Status target)
    {
        target.ActiveEffects.Clear();
    }

    // =====================================================================
    //  修飾値の計算ヘルパー
    // =====================================================================

    /// <summary>ATK修飾: 弱体-15%, 冷気-10%, 攻勢+15%</summary>
    public static float GetATKModifier(Status unit)
    {
        float mod = 1f;
        if (HasDebuff(unit, StatusEffectType.Weaken)) mod -= GameConstants.WeakenATKReduction;
        if (HasDebuff(unit, StatusEffectType.Chill))  mod -= GameConstants.ChillATKReduction;
        if (HasBuff(unit, BuffType.Offensive))         mod += GameConstants.OffensiveATKBonus;
        return Mathf.Clamp(mod, 0f, 2f);
    }

    /// <summary>DEF修飾: 破甲-15%, 守勢+20%</summary>
    public static float GetDEFModifier(Status unit)
    {
        float mod = 1f;
        if (HasDebuff(unit, StatusEffectType.ArmorBreak)) mod -= GameConstants.ArmorBreakDEFReduction;
        if (HasBuff(unit, BuffType.Defensive))            mod += GameConstants.DefensiveDEFBonus;
        return Mathf.Clamp(mod, 0f, 2f);
    }

    /// <summary>被ダメージ修飾: マーク+10%, 凍結+10%, 障壁-30%</summary>
    public static float GetIncomingDamageModifier(Status target)
    {
        float mod = 1f;
        if (HasDebuff(target, StatusEffectType.Mark))   mod += GameConstants.MarkIncomingDamageIncrease;
        if (HasDebuff(target, StatusEffectType.Freeze)) mod += GameConstants.FreezeIncomingDamageIncrease;
        if (HasBuff(target, BuffType.Barrier))          mod -= GameConstants.BarrierDamageReduction;
        return Mathf.Max(0f, mod);
    }

    /// <summary>スキル倍率修飾: 封技-20%</summary>
    public static float GetSkillMultiplierModifier(Status attacker)
    {
        if (HasDebuff(attacker, StatusEffectType.Seal)) return -GameConstants.SealSkillReduction;
        return 0f;
    }

    /// <summary>回復量修飾: 毒-25%, 呪傷-50%</summary>
    public static float GetHealModifier(Status target)
    {
        float mod = 1f;
        if (HasDebuff(target, StatusEffectType.Poison)) mod -= (1f - GameConstants.PoisonHealReduction);
        if (HasDebuff(target, StatusEffectType.Curse))  mod -= (1f - GameConstants.CurseHealReduction);
        return Mathf.Max(0f, mod);
    }

    /// <summary>移動AP追加コスト: 鈍足+2, 冷気+2</summary>
    public static int GetMoveAPCostBonus(Status unit)
    {
        int bonus = 0;
        if (HasDebuff(unit, StatusEffectType.Slow))  bonus += GameConstants.DebuffMoveAPBonus;
        if (HasDebuff(unit, StatusEffectType.Chill)) bonus += GameConstants.DebuffMoveAPBonus;
        return bonus;
    }

    /// <summary>移動不可チェック: 凍結 or 束縛</summary>
    public static bool IsMovementBlocked(Status unit)
    {
        return HasDebuff(unit, StatusEffectType.Freeze)
            || HasDebuff(unit, StatusEffectType.Bind);
    }

    /// <summary>行動不可チェック: スタン</summary>
    public static bool IsStunned(Status unit)
    {
        return HasDebuff(unit, StatusEffectType.Stun);
    }

    /// <summary>視界修飾: 盲目-1, 看破+1</summary>
    public static int GetVisionModifier(Status unit)
    {
        int mod = 0;
        if (HasDebuff(unit, StatusEffectType.Blind)) mod -= 1;
        if (HasBuff(unit, BuffType.Insight))          mod += 1;
        return mod;
    }

    // =====================================================================
    //  反射ダメージ処理
    // =====================================================================
    public static int ProcessReflect(Status target, Status attacker)
    {
        if (!HasBuff(target, BuffType.Reflect)) return 0;
        int reflectDmg = Random.Range(5, 11); // 5〜10
        attacker.HP = Mathf.Max(0, attacker.HP - reflectDmg);
        Debug.Log($"[StatusEffect] {target.kind} の反射で {attacker.kind} に {reflectDmg} ダメージ");
        return reflectDmg;
    }
}
