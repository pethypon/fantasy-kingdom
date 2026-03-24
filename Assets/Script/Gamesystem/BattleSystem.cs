using UnityEngine;

public enum GameResult { Win, Lose, TimeUpWin, TimeUpLose, TimeUpDraw }

public class BattleSystem : MonoBehaviour
{
    public Status target;
    public Status AttackSide;
    public TurnGenerater turngenerater;

    public const int ShieldDuration = 5;

    // ─── ダメージ発生（入口） ─────────────────────────────────────────
    public void DamageGenerater(TurnGenerater turngenerater)
    {
        this.turngenerater = turngenerater;
        if (target == null || turngenerater.SelectUnit == null) return;
        AttackSide = turngenerater.SelectUnit;

        // スタン中は行動不可
        if (StatusEffectSystem.IsStunned(AttackSide))
        {
            Debug.Log($"[Battle] {AttackSide.kind} はスタン中で行動不可");
            return;
        }

        // シールド中はダメージ無効
        if (target.ShieldTurns > 0)
        {
            Debug.Log($"[Battle] {target.kind} はシールド中！ ダメージ無効（残り{target.ShieldTurns}ターン）");
            FloatingDamageUI.ShowShield(target.transform.position);
            return;
        }

        // ダメージ計算（パッシブスキル補正は DamageCalculator 内で自動適用）
        int damage = SkillSystem.CalcNormalDamage(AttackSide, target);

        // Crossbow: 命中時10%でスタン付与
        if (AttackSide.kind == Kind.Crossbow && damage > 0)
        {
            if (Random.value < GameConstants.CrossbowStunChance)
            {
                StatusEffectSystem.ApplyDebuff(target, StatusEffectType.Stun, 1);
                Debug.Log($"[Battle] {AttackSide.kind} のスタン発動！ {target.kind} は1ターン行動不可");
            }
        }

        // MagicSniper: 攻撃ごとに最大HPの20%自傷 + 敵にマーキング
        if (AttackSide.kind == Kind.Magicsniper && damage > 0)
        {
            int selfDmg = Mathf.RoundToInt(AttackSide.MaxHP * GameConstants.MagicSniperSelfDamageRatio);
            AttackSide.HP -= selfDmg;
            AttackSide.HP = Mathf.Max(0, AttackSide.HP);
            Debug.Log($"[Battle] {AttackSide.kind} 自傷ダメージ {selfDmg}（残HP:{AttackSide.HP}）");
            FloatingDamageUI.ShowDamage(AttackSide.transform.position, selfDmg, AttackSide.HP <= 0);

            StatusEffectSystem.ApplyDebuff(target, StatusEffectType.Mark, 1);
            Debug.Log($"[Battle] {target.kind} にマーキング付与（被ダメ+10%、1ターン）");
        }

        ApplyDamage(damage);

        // 反射処理
        StatusEffectSystem.ProcessReflect(target, AttackSide);

        CheckCrystalShield();
        CheckDeath();
    }

    // ─── 防御側パッシブ ───────────────────────────────────────────────
    // Knight の視界内軽減/視界外増加は DamageCalculator.GetDefenderPassiveMultiplier で処理

    // ═══════════════════════════════════════════════════════════════════
    //  クリスタルシールド判定: HP50%以下で未発動なら5ターン無敵付与
    // ═══════════════════════════════════════════════════════════════════
    private void CheckCrystalShield()
    {
        if (target.kind != Kind.Crystal) return;
        if (target.ShieldActivated) return;
        if (target.MaxHP <= 0) return;

        float hpRatio = (float)target.HP / target.MaxHP;
        if (hpRatio < 0.5f && target.HP > 0)
        {
            target.ShieldTurns = ShieldDuration;
            target.ShieldActivated = true;
            Debug.Log($"[Battle] {target.team} のクリスタルが50%を切った！ {ShieldDuration}ターンの無敵シールド発動！");
            string teamLabel = target.team == Team.Player ? "味方" : "敵";
            ToastMessageUI.Show($"{teamLabel}クリスタルがシールド発動！（{ShieldDuration}ターン）",
                ToastMessageUI.MessageType.Info, 4f);
        }
    }

    /// <summary>
    /// 全クリスタルのシールドターンを1減らす（ターン開始時に呼ぶ）
    /// </summary>
    public static void TickCrystalShields(Transform crystalParent)
    {
        if (crystalParent == null) return;
        foreach (Status s in crystalParent.GetComponentsInChildren<Status>())
        {
            if (s.kind == Kind.Crystal && s.ShieldTurns > 0)
            {
                s.ShieldTurns--;
                Debug.Log($"[Battle] {s.team} クリスタルシールド残り {s.ShieldTurns} ターン");
                // シールド終了時にフラグをリセット → 再度HP50%以下で再発動可能
                if (s.ShieldTurns <= 0)
                {
                    s.ShieldActivated = false;
                    Debug.Log($"[Battle] {s.team} クリスタルシールド終了 → 再発動可能");
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ダメージ適用
    // ═══════════════════════════════════════════════════════════════════
    private void ApplyDamage(int damage)
    {
        damage = Mathf.Max(0, damage);
        target.HP -= damage;
        target.HP = Mathf.Max(0, target.HP);
        Debug.Log($"[Battle] {AttackSide.kind} → {target.kind}  DMG:{damage}  残HP:{target.HP}");

        // フローティングダメージ表示
        bool isKill = target.HP <= 0;
        if (damage > 0)
            FloatingDamageUI.ShowDamage(target.transform.position, damage, isKill);
        else
            FloatingDamageUI.ShowMiss(target.transform.position);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  HP0 判定 → 駒 / ゲーム終了対象 に分岐
    // ═══════════════════════════════════════════════════════════════════
    private void CheckDeath()
    {
        if (target.HP > 0) return;

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
    private void HandleUnitDeath()
    {
        Debug.Log($"[Battle] {target.team} の {target.kind} が撃破された");

        Vector3 cellPos = turngenerater.movegenerater.Cell(target.transform.position);
        turngenerater.movegenerater.UnitPointData.Remove(cellPos);

        if (turngenerater.SelectUnit == target)
        {
            turngenerater.SelectUnit = null;
        }

        // 死亡した駒の視界セルを探索済みに保存（半透明フォグとして残す）
        if (target.VisionCell != null && target.VisionCell.Count > 0)
        {
            var visionGen = turngenerater.visiongenerater;
            if (target.team == Team.Player && visionGen.PlayerExploard != null)
            {
                visionGen.PlayerExploard.UnionWith(target.VisionCell);
            }
            else if (target.team == Team.Enemy && visionGen.EnemyExploard != null)
            {
                visionGen.EnemyExploard.UnionWith(target.VisionCell);
            }
        }

        target.gameObject.SetActive(false);

        turngenerater.visiongenerater.VisionPoint(
            turngenerater.mapcreate,
            turngenerater.movegenerater,
            turngenerater.crystalsystem
        );
    }

    // ═══════════════════════════════════════════════════════════════════
    //  B) Crystal or King がHP0 → 勝敗確定 → ゲーム終了
    // ═══════════════════════════════════════════════════════════════════
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
    }
}
