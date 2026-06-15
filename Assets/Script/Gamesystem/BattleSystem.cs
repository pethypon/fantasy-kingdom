using UnityEngine;

public enum GameResult { Win, Lose, TimeUpWin, TimeUpLose, TimeUpDraw }

/// <summary>クリスタル反撃の発動契機（被弾時 / ターン開始時）。ログ・統計識別に使用。</summary>
public enum CrystalCounterTrigger { OnHit, TurnStart }

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
            Target.ShieldEverActivated = true;
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

        // 反逆の騎士王: 反撃バフ有効時は受けたダメージの1.5倍を反射
        if (damage > 0) WildBossSystem.TryReflectDamage(Target, Attacker, damage);

        // 与ダメージ = 獲得XP（ユニットのみ・自軍同士は除外）。兵舎XP%を乗算
        if (damage > 0 && Attacker != null && Attacker.type == Type.Unit && Attacker.team != Target.team)
        {
            int barracksXP = turnGenerator?.Systems?.FactionState != null
                ? turnGenerator.Systems.FactionState.GetBarracksXP(Attacker.team)
                : 0;
            Attacker.GainExperienceFromDamage(damage, barracksXP);
        }

        // StrangeKingAura: 与ダメージの20%を吸収してHP回復
        if (damage > 0 && Attacker != null &&
            Attacker.passiveskill == PassiveSkill.StrangeKingAura && Attacker.team != Target.team)
        {
            int heal = Mathf.RoundToInt(damage * GameConstants.StrangeKingLifesteal);
            if (heal > 0) Attacker.ApplyHeal(heal);
        }

        // クリスタル反撃: クリスタルが攻撃された時、領土内の敵に反撃
        if (Target.kind == Kind.Crystal && damage > 0 && Attacker != null && Attacker.team != Target.team)
            ProcessCrystalCounterAttack(Target, Attacker);

        bool isKill = !Target.IsAlive;
        if (damage > 0)
            FloatingDamageUI.ShowDamage(Target.transform.position, damage, isKill);
        else
            FloatingDamageUI.ShowMiss(Target.transform.position);

        // 実績
        if (isKill && Attacker != null && Attacker.team == Team.Player && Target.team != Team.Player)
        {
            AchievementSystem.GetOrCreate().OnKill();
            if (Target.kind == Kind.Crystal)
                AchievementSystem.Instance.OnCrystalKill((float)damage / System.Math.Max(1, Target.MaxHP));
            if (Target.isWildBoss)
                AchievementSystem.Instance.OnWildBossDefeated();
        }

        // 統計記録
        if (MatchStats.Instance != null)
        {
            MatchStats.Instance.RecordDamage(Attacker != null ? Attacker.team : Team.None,
                Target.team, damage, isKill, Target.isWildBoss);
            if (damage > 0 && Attacker != null && Attacker.type == Type.Unit
                && Attacker.team != Target.team)
            {
                MatchStats.Instance.RecordXPGain(Attacker.team, damage);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  クリスタル反撃: 被弾クリスタルの領土内にいる最も近い敵にMaxHP×30%ダメージ
    //  シールド発動後は3体まで拡大
    // ═══════════════════════════════════════════════════════════════════
    private void ProcessCrystalCounterAttack(Status crystal, Status attacker)
    {
        ProcessCrystalCounter(turnGenerator?.Systems, crystal, CrystalCounterTrigger.OnHit);
    }

    /// <summary>
    /// クリスタル反撃のコアロジック（被弾時 / ターン開始時の双方から呼ばれる）。
    /// 領土内の敵候補を最近接順に最大N体（シールド前=1, シールド発動後=3）選び、
    /// 各々の MaxHP × 30% を DEF 無視固定ダメージとして適用する。
    /// </summary>
    public static void ProcessCrystalCounter(GameSystems systems, Status crystal, CrystalCounterTrigger trigger)
    {
        if (systems == null || crystal == null) return;
        if (!crystal.IsAlive || crystal.HP <= 0) return;
        if (crystal.MaxHP <= 0) return; // 未初期化クリスタル等の防御的ガード

        Team crystalTeam = crystal.team;
        Team enemyTeam = crystalTeam == Team.Player ? Team.Enemy : Team.Player;

        Transform enemyParent = enemyTeam == Team.Player
            ? systems.UnitSetting?.PlayerUnit
            : systems.UnitSetting?.EnemyUnit;
        if (enemyParent == null) return;

        Vector3Int crystalPos = GridHelper.ToGridXZ(crystal.transform.position);
        var candidates = new System.Collections.Generic.List<(Status s, int d)>();
        foreach (Transform child in enemyParent)
        {
            var s = child.GetComponent<Status>();
            if (s == null || !s.IsAlive) continue;
            var sPos = GridHelper.ToGridXZ(s.transform.position);
            if (systems.TerritorySystem != null && !systems.TerritorySystem.IsInTerritory(sPos, crystalTeam)) continue;
            int d = GridHelper.ChebyshevDistance(crystalPos, sPos);
            candidates.Add((s, d));
        }
        if (candidates.Count == 0) return;

        candidates.Sort((a, b) => a.d.CompareTo(b.d));
        int targetCount = crystal.ShieldEverActivated ? GameConstants.CrystalCounterTargetsAfterShield : 1;
        int count = Mathf.Min(targetCount, candidates.Count);

        for (int i = 0; i < count; i++)
        {
            var t = candidates[i].s;
            int dmg = Mathf.RoundToInt(t.MaxHP * GameConstants.CrystalCounterDamageRatio);

            // Special Ability: 致死ダメージ耐え（生還本能）— 他のダメージ経路と同様に適用
            if (SpecialAbilitySystem.TrySurviveLethal(t, dmg))
            {
                FloatingDamageUI.ShowDamage(t.transform.position, dmg, false);
                Debug.Log($"[Battle] クリスタル反撃[{trigger}]: {t.kind} は生還本能で耐えた  targets={count}");
                continue;
            }

            int actual = t.ApplyDamage(dmg);
            FloatingDamageUI.ShowDamage(t.transform.position, actual, !t.IsAlive);
            Debug.Log($"[Battle] クリスタル反撃[{trigger}]: {t.kind} に {actual} ダメージ（残HP:{t.HP}）  targets={count}");

            // 死亡時は HandleDeathIfDead に一元委譲する。
            // 旧コードは Unit のみインライン処理しており、King がクリスタル反撃で
            // 倒れても勝敗判定(TryTriggerGameEndIfDecisive)が走らなかった。
            // HandleDeathIfDead は占有解除・視界再計算・探索済み保存・勝敗判定を含む。
            t.HandleDeathIfDead();
        }

        // 視界再計算は HandleDeathIfDead 内で実施済み（死者がいなければ盤面不変）。
    }

    /// <summary>
    /// ターン開始時にクリスタル反撃を発動する。
    /// 引数 team は「これからターンを開始する側」(=領土侵入されている側ではなく侵入している側)。
    /// 反撃対象となるのは team の敵側クリスタルの領土に侵入している team のユニット。
    /// </summary>
    public static void ProcessCrystalCounterAtTurnStart(GameSystems systems, Team team)
    {
        if (systems?.CrystalSystem == null) return;
        // 侵入される側（敵側）のクリスタルから反撃
        Team enemyTeam = team == Team.Player ? Team.Enemy : Team.Player;
        Transform crystalParent = enemyTeam == Team.Player
            ? systems.CrystalSystem.Playercrystal
            : systems.CrystalSystem.Enemycrystal;
        if (crystalParent == null) return;

        foreach (Transform t in crystalParent)
        {
            var s = t.GetComponent<Status>();
            if (s == null || s.kind != Kind.Crystal) continue;
            ProcessCrystalCounter(systems, s, CrystalCounterTrigger.TurnStart);
        }
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

        // レベル・経験値・ステータスを Lv1 にリセット（次回スポーン時のために）
        Target.ResetToLv1();

        Target.gameObject.SetActive(false);

        // 駒の死亡で盤面が変化したため同フレームキャッシュを無効化してから再計算する
        turnGenerator.Systems.VisionGenerator.MarkVisionDirty();
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
