<<<<<<< HEAD
<<<<<<< HEAD
﻿using UnityEngine;

public enum GameResult { Win, Lose }
=======
using System.Collections.Generic;
using UnityEngine;
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13

public enum GameResult { Win, Lose }
=======
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
>>>>>>> parent of d903d2d (2)

public class BattleSystem : MonoBehaviour
{
    public Status target;
    public Status AttackSide;
    public TurnGenerater turngenerater;
<<<<<<< HEAD

<<<<<<< HEAD
    // ─── ダメージ発生（入口） ─────────────────────────────────────────
=======
>>>>>>> parent of d903d2d (2)
    public void DamageGenerater(TurnGenerater turngenerater)
    {
        this.turngenerater = turngenerater;
        //None�ȊO�͋�Ƃ̎��E�����������琧��
        if (target != null)
        {
            if (turngenerater.SelectUnit != null)
            {
                AttackSide = turngenerater.SelectUnit;
                switch (AttackSide.passiveskill)
                {
                    case PassiveSkill.HunterEyes:

                        break;
                    case PassiveSkill.Destroyer:

                        break;

                    case PassiveSkill.Assassination:
                        break;

                    case PassiveSkill.Sniper:

                        break;

                    case PassiveSkill.None:
                        int damage = (AttackSide.ATK - target.DEF);
                        damage = Mathf.Max(0,damage);
                        target.HP -= damage;
                        target.HP = Mathf.Max(0, target.HP);
                        
                        break;
                }

               
                
            }
        }
    }

    public void SideDefender()
    {

        switch (target.passiveskill)
        {
            case PassiveSkill.Impregnable:

                break;
            case PassiveSkill.None:

                break;

           
        }
    }

<<<<<<< HEAD
    // ═══════════════════════════════════════════════════════════════════
    //  ダメージ適用
    // ═══════════════════════════════════════════════════════════════════
    private void ApplyDamage(int damage)
    {
=======
    [SerializeField] private UnitSetting _unitSetting;
    private Dictionary<Kind, UnitData> _unitDataMap;

    private void Awake()
    {
        if (_unitSetting != null)
            _unitDataMap = _unitSetting.UnitDataMap;
    }

    // ─── 攻撃側パッシブの追加倍率（1.0fに加算する値。上限2.0fまで） ─
    // 新スキル追加時はここに1エントリ追記するだけでよい
    static readonly Dictionary<PassiveSkill, System.Func<Status, Status, float>> AttackPassiveBonus =
        new Dictionary<PassiveSkill, System.Func<Status, Status, float>>
    {
        {
            // 建物・壁への攻撃時に+1.0倍（合計×2.0）
            PassiveSkill.Destroyer,
            (atk, def) => (def.type == Type.Building || def.type == Type.Wall) ? 1.0f : 0f
        },
        {
            // 距離1マスごとに+0.25倍、最大+0.75倍（3マス以上で上限）
            PassiveSkill.HunterEyes,
            (atk, def) => Mathf.Min(
                Mathf.RoundToInt(Vector3.Distance(
                    atk.transform.position, def.transform.position)) * 0.25f, 0.75f)
        },
        {
            // 建物・壁への攻撃時に+0.15倍
            PassiveSkill.Sniper,
            (atk, def) => (def.type == Type.Building || def.type == Type.Wall) ? 0.15f : 0f
        },
        // Assassination（視界外×1.25）は視界システム完成後に追加
        // 背面攻撃（×1.5）は背面判定実装後に追加
    };

    // ─── 防御側パッシブの倍率修正（負の値 = ダメージ軽減） ──────────
    static readonly Dictionary<PassiveSkill, System.Func<Status, Status, float>> DefensePassiveBonus =
        new Dictionary<PassiveSkill, System.Func<Status, Status, float>>
    {
        {
            // 視界内の攻撃を20%軽減（×0.8）
            PassiveSkill.Impregnable,
            (atk, def) => -0.2f
        },
    };

    // ─── ダメージ発生（入口） ─────────────────────────────────────────
    public void DamageGenerater(TurnGenerater turngenerater)
    {
        this.turngenerater = turngenerater;
        if (target == null || turngenerater.SelectUnit == null) return;
        AttackSide = turngenerater.SelectUnit;

        // Priestは回復処理に分岐
        if (AttackSide.kind == Kind.Priest)
        {
            HealTarget();
            return;
        }

        // GameReference準拠のダメージ式
        int rawDamage = 1 + (AttackSide.ATK / 6) + ((AttackSide.ATK / 2) - (target.DEF / 4));

        // パッシブ倍率を加算して上限2.0倍でクランプ
        float bonus = 0f;
        if (AttackPassiveBonus.TryGetValue(AttackSide.passiveskill, out var atkFunc))
            bonus += atkFunc(AttackSide, target);
        if (DefensePassiveBonus.TryGetValue(target.passiveskill, out var defFunc))
            bonus += defFunc(AttackSide, target);
        float multiplier = Mathf.Min(1f + bonus, 2f);

        int damage = Mathf.Max(1, Mathf.RoundToInt(rawDamage * multiplier));
        ApplyDamage(damage);

        // MagicSniperの自損（攻撃のたびに自最大HP×20%）
        if (AttackSide.kind == Kind.Magicsniper)
            ApplySelfDamage(AttackSide, 0.20f);

        // Crossbowの10%スタン（1ターン）
        if (AttackSide.kind == Kind.Crossbow && UnityEngine.Random.value < 0.10f)
            target.state = State.Stun;

        CheckDeath();
    }

    // ─── Priest回復処理 ───────────────────────────────────────────────
    /// <summary>
    /// 隣接する味方のHPを回復する（Priestの行動）。
    /// 回復量：最大HP×5%（GameReference準拠）。
    /// </summary>
    private void HealTarget()
    {
        if (target == null || target.type == Type.Wall) return;
        if (target.team != Team.Player) return;

        // 最大HPをUnitDataから計算する
        int maxHP = target.HP;
        if (_unitDataMap != null && _unitDataMap.TryGetValue(target.kind, out var data))
            maxHP = UnitData.CalcStat(data.baseHP, data.hpGrowth, target.Level);

        // 回復量：最大HP×5%（端数切り上げ、最低1）
        int heal = Mathf.Max(1, Mathf.CeilToInt(maxHP * 0.05f));
        target.HP = Mathf.Min(target.HP + heal, maxHP);
        Debug.Log($"[Battle] Priest 回復 +{heal}  {target.kind} HP:{target.HP}/{maxHP}");
    }

    // ─── 自損処理（MagicSniper） ──────────────────────────────────────
    /// <summary>
    /// 自分に最大HP比例のダメージを与える（MagicSniperの反動）。
    /// </summary>
    private void ApplySelfDamage(Status unit, float ratio)
    {
        if (_unitDataMap == null || !_unitDataMap.TryGetValue(unit.kind, out var data)) return;
        int maxHP = UnitData.CalcStat(data.baseHP, data.hpGrowth, unit.Level);
        int selfDamage = Mathf.Max(1, Mathf.RoundToInt(maxHP * ratio));
        unit.HP = Mathf.Max(0, unit.HP - selfDamage);
        Debug.Log($"[Battle] {unit.kind} 自損 -{selfDamage}  HP:{unit.HP}/{maxHP}");
        if (unit.HP <= 0) unit.gameObject.SetActive(false);  // 自滅
    }

    // ─── ダメージ適用 ─────────────────────────────────────────────────
    private void ApplyDamage(int damage)
    {
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
        damage = Mathf.Max(0, damage);
        target.HP -= damage;
        target.HP = Mathf.Max(0, target.HP);
        Debug.Log($"[Battle] {AttackSide.kind} → {target.kind}  DMG:{damage}  残HP:{target.HP}");
    }

<<<<<<< HEAD
    // ═══════════════════════════════════════════════════════════════════
    //  HP0 判定 → 駒 / ゲーム終了対象 に分岐
    // ═══════════════════════════════════════════════════════════════════
=======
    // ─── HP0 判定 → 駒 / ゲーム終了対象 に分岐 ──────────────────────
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    private void CheckDeath()
    {
        if (target.HP > 0) return;

<<<<<<< HEAD
        // Crystal か King のどちらかが倒れたら即ゲーム終了
        if (target.kind == Kind.Crystal || target.kind == Kind.King)
        {
            HandleGameEnd();
        }
        else if (target.type == Type.Unit)
        {
            HandleUnitDeath();
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  A) 一般駒がHP0 → 盤面から除外
    // ═══════════════════════════════════════════════════════════════════
=======
        if (target.type == Type.Wall)                                      { HandleWallDestruction(); return; }
        if (target.kind == Kind.Crystal || target.kind == Kind.King)       { HandleGameEnd(); return; }
        if (target.type == Type.Unit)                                      { HandleUnitDeath(); }
    }

    // ─── 壁破壊 ──────────────────────────────────────────────────────
    private void HandleWallDestruction()
    {
        Debug.Log($"[Battle] 壁が破壊された");
        turngenerater.movegenerater.UnitPointData.Remove(
            turngenerater.movegenerater.Cell(target.transform.position));
        target.gameObject.SetActive(false);
    }

    // ─── A) 一般駒がHP0 → 盤面から除外 ──────────────────────────────
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    private void HandleUnitDeath()
    {
        Debug.Log($"[Battle] {target.team} の {target.kind} が撃破された");

        Vector3 cellPos = turngenerater.movegenerater.Cell(target.transform.position);
        turngenerater.movegenerater.UnitPointData.Remove(cellPos);

        if (turngenerater.SelectUnit == target)
<<<<<<< HEAD
        {
            turngenerater.SelectUnit = null;
        }
=======
            turngenerater.SelectUnit = null;
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13

        target.gameObject.SetActive(false);

        turngenerater.visiongenerater.VisionPoint(
            turngenerater.mapcreate,
            turngenerater.movegenerater,
            turngenerater.crystalsystem
        );
<<<<<<< HEAD
    }

    // ═══════════════════════════════════════════════════════════════════
    //  B) Crystal or King がHP0 → 勝敗確定 → ゲーム終了
    // ═══════════════════════════════════════════════════════════════════
=======

        // 撃破した側にレベルアップを付与する（LevelSystem.csは作らない、ここに統合）
        if (AttackSide != null) GainLevel(AttackSide, target);
    }

    // ─── B) Crystal or King がHP0 → 勝敗確定 → ゲーム終了 ──────────
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    private void HandleGameEnd()
    {
        GameResult result;

        if (target.team == Team.Enemy)
        {
            result = GameResult.Win;
            Debug.Log($"[Battle] 敵 {target.kind} 破壊 → 勝利！");
        }
        else
        {
            result = GameResult.Lose;
            Debug.Log($"[Battle] 自軍 {target.kind} 破壊 → 敗北…");
        }

        turngenerater.ChangeState(new GameEndState(turngenerater, result));
<<<<<<< HEAD
=======
    }

    // ─── レベルアップ ─────────────────────────────────────────────────
    /// <summary>
    /// Lv差に応じてattackerのLvを上げてステータスを再計算する。
    /// 弱い敵を狩り続けても成長しないよう、Lv差がプラスのときだけ付与する。
    /// Barracksの経験値ボーナス（+5/8/10%）は呼び出し元から係数として渡す。
    /// </summary>
    private void GainLevel(Status attacker, Status defeated, float barracksBonus = 0f)
    {
        int diff = defeated.Level - attacker.Level;
        // 弱い敵を狩り続けても成長しないよう、Lv差がプラスのときだけ付与する
        if (diff < 1) return;

        int gain = diff <= 5  ? 1 :
                   diff <= 10 ? 2 :
                   diff <= 15 ? 3 :
                   diff <= 25 ? 4 :
                   diff <= 40 ? 5 :
                   diff <= 60 ? 6 :
                   diff <= 80 ? 7 : 8;

        gain = Mathf.Max(1, Mathf.RoundToInt(gain * (1f + barracksBonus)));
        attacker.Level += gain;

        // ステータスを新しいLvで再計算（UnitData.ApplyToStatusを再利用）
        if (_unitDataMap != null && _unitDataMap.TryGetValue(attacker.kind, out var data))
            data.ApplyToStatus(attacker, attacker.Level);

        Debug.Log($"[Level] {attacker.kind}  Lv{attacker.Level - gain} → Lv{attacker.Level}");
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    }
=======
>>>>>>> parent of d903d2d (2)
}
