using UnityEngine;

public enum GameResult { Win, Lose, TimeUpWin, TimeUpLose, TimeUpDraw }

public class BattleSystem : MonoBehaviour
{
    public Status Target { get; private set; }
    public Status Attacker { get; private set; }
    private TurnGenerator turnGenerator;

    /// <summary>攻撃対象を設定する（UnitClick から呼ばれる）</summary>
    public void SetTarget(Status target) => Target = target;

    // ─── ダメージ発生（入口） ─────────────────────────────────────────
    public void ProcessDamage(TurnGenerator turnGenerator)
    {
        if (turnGenerator == null)
        {
            Debug.LogError("[Battle] TurnGenerator が null です");
            return;
        }
        this.turnGenerator = turnGenerator;

        if (Target == null)
        {
            Debug.LogWarning("[Battle] Target が null のためダメージ処理をスキップ");
            return;
        }
        if (turnGenerator.Context.SelectUnit == null)
        {
            Debug.LogWarning("[Battle] SelectUnit が null のためダメージ処理をスキップ");
            return;
        }
        Attacker = turnGenerator.Context.SelectUnit;

        // スタン中は行動不可
        if (StatusEffectSystem.IsStunned(Attacker))
        {
            Debug.Log($"[Battle] {Attacker.kind} はスタン中で行動不可");
            return;
        }

        // シールド中はダメージ無効
        if (Target.ShieldTurns > 0)
        {
            Debug.Log($"[Battle] {Target.kind} はシールド中！ ダメージ無効（残り{Target.ShieldTurns}ターン）");
            FloatingDamageUI.ShowShield(Target.transform.position);
            return;
        }

        // ダメージ計算（パッシブスキル補正は DamageCalculator 内で自動適用）
        int damage = SkillSystem.CalcNormalDamage(Attacker, Target);

        // Crossbow: 命中時10%でスタン付与
        if (Attacker.kind == Kind.Crossbow && damage > 0)
        {
            if (Random.value < GameConstants.CrossbowStunChance)
            {
                StatusEffectSystem.ApplyDebuff(Target, StatusEffectType.Stun, 1);
                Debug.Log($"[Battle] {Attacker.kind} のスタン発動！ {Target.kind} は1ターン行動不可");
            }
        }

        // MagicSniper: 攻撃ごとに最大HPの20%自傷 + 敵にマーキング
        if (Attacker.kind == Kind.Magicsniper && damage > 0)
        {
            int selfDmg = Mathf.RoundToInt(Attacker.MaxHP * GameConstants.MagicSniperSelfDamageRatio);
            Attacker.ApplyDamage(selfDmg);
            Debug.Log($"[Battle] {Attacker.kind} 自傷ダメージ {selfDmg}（残HP:{Attacker.HP}）");
            FloatingDamageUI.ShowDamage(Attacker.transform.position, selfDmg, !Attacker.IsAlive);

            StatusEffectSystem.ApplyDebuff(Target, StatusEffectType.Mark, 1);
            Debug.Log($"[Battle] {Target.kind} にマーキング付与（被ダメ+10%、1ターン）");
        }

        // Special Ability: 致死ダメージ耐え（生還本能）
        if (SpecialAbilitySystem.TrySurviveLethal(Target, damage))
        {
            FloatingDamageUI.ShowDamage(Target.transform.position, damage, false);
        }
        else
        {
            ApplyDamage(damage);
        }

        // Special Ability: 攻撃命中時効果（単体攻撃 = true）
        SpecialAbilitySystem.OnAttackHit(Attacker, Target, damage, true);

        // ---- ML観測: プレイヤーの攻撃をMLシステムに記録 ----
        if (Attacker.team == Team.Player && turnGenerator.Systems.AICommander != null && AIConfig.IsMLEnabled)
        {
            var ml = turnGenerator.Systems.AICommander.MLIntegration;
            Vector3 ecPos = turnGenerator.Systems.CrystalSystem.ECP;
            ml.ObservePlayerAttack(Attacker, Target, damage, ecPos, turnGenerator.Context.Turn);

            // クリスタルへの直接攻撃は別途記録
            if (Target.kind == Kind.Crystal && Target.team == Team.Enemy)
                ml.ObservePlayerCrystalAttack(damage, Target.MaxHP, turnGenerator.Context.Turn);
        }

        // 反射処理
        StatusEffectSystem.ProcessReflect(Target, Attacker);

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
        if (Target.kind != Kind.Crystal) return;
        if (Target.ShieldActivated) return;
        if (Target.MaxHP <= 0) return;

        float hpRatio = (float)Target.HP / Target.MaxHP;
        if (Target.HP > 0 && hpRatio < GameConstants.CrystalShieldThreshold)
        {
            Target.ShieldTurns = GameConstants.CrystalShieldDuration;
            Target.ShieldActivated = true;
            Debug.Log($"[Battle] {Target.team} のクリスタルが50%を切った！ {GameConstants.CrystalShieldDuration}ターンの無敵シールド発動！");
            string teamLabel = Target.team == Team.Player ? "味方" : "敵";
            ToastMessageUI.Show($"{teamLabel}クリスタルがシールド発動！（{GameConstants.CrystalShieldDuration}ターン）",
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
        damage = Target.ApplyDamage(damage);
        Debug.Log($"[Battle] {Attacker.kind} → {Target.kind}  DMG:{damage}  残HP:{Target.HP}");

        bool isKill = !Target.IsAlive;
        if (damage > 0)
            FloatingDamageUI.ShowDamage(Target.transform.position, damage, isKill);
        else
            FloatingDamageUI.ShowMiss(Target.transform.position);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  HP0 判定 → 駒 / ゲーム終了対象 に分岐
    // ═══════════════════════════════════════════════════════════════════
    private void CheckDeath()
    {
        if (Target.HP > 0) return;

        if (Target.kind == Kind.Crystal || Target.kind == Kind.King)
        {
            HandleGameEnd();
        }
        else if (Target.type == Type.Unit)
        {
            HandleUnitDeath();
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  A) 一般駒がHP0 → 盤面から除外
    // ═══════════════════════════════════════════════════════════════════
    private void HandleUnitDeath()
    {
        if (Target == null) return;
        Debug.Log($"[Battle] {Target.team} の {Target.kind} が撃破された");

        if (turnGenerator.Systems.MoveGenerator != null)
        {
            Vector3 cellPos = turnGenerator.Systems.MoveGenerator.Cell(Target.transform.position);
            turnGenerator.Systems.MoveGenerator.RemoveOccupied(cellPos);
        }

        if (turnGenerator.Context.SelectUnit == Target)
        {
            turnGenerator.Context.SelectUnit = null;
        }

        // 死亡した駒の視界セルを探索済みに保存（半透明フォグとして残す）
        if (Target.VisionCell != null && Target.VisionCell.Count > 0)
        {
            var visionGen = turnGenerator.Systems.VisionGenerator;
            visionGen.AddExploredRange(Target.team, Target.VisionCell);
        }

        Target.gameObject.SetActive(false);

        turnGenerator.Systems.VisionGenerator.VisionPoint(
            turnGenerator.Systems.MapCreate,
            turnGenerator.Systems.MoveGenerator,
            turnGenerator.Systems.CrystalSystem
        );
    }

    // ═══════════════════════════════════════════════════════════════════
    //  B) Crystal or King がHP0 → 勝敗確定 → ゲーム終了
    // ═══════════════════════════════════════════════════════════════════
    private void HandleGameEnd()
    {
        GameResult result;

        if (Target.team == Team.Enemy)
        {
            result = GameResult.Win;
            Debug.Log($"[Battle] 敵 {Target.kind} 破壊 → 勝利！");
        }
        else
        {
            result = GameResult.Lose;
            Debug.Log($"[Battle] 自軍 {Target.kind} 破壊 → 敗北…");
        }

        turnGenerator.ChangeState(new GameEndState(turnGenerator, result));
    }
}
