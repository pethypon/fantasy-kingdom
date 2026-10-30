using UnityEngine;

public enum GameResult { Win, Lose, TimeUpWin, TimeUpLose, TimeUpDraw }

/// <summary>Legacy identifiers are retained; only TurnEnd is an operative crystal-counter event.</summary>
public enum CrystalCounterTrigger { OnHit, TurnStart, TurnEnd }

public partial class BattleSystem : MonoBehaviour
{
    public Status Target { get; private set; }
    public Status Attacker { get; private set; }
    private TurnGenerator turnGenerator;
    Status actualRecipient;

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
        if (!Attacker.IsAlive || !Target.IsAlive || (!NeutralFactionSystem.AreHostile(Attacker, Target) && !SpecialSkillRuntime.IsAssassinAllyTarget(Attacker, Target))) return;
        if (!SpecialSkillRuntime.CanNormalAttackTarget(Attacker, Target)) return;
        var terrain = turnGenerator.Systems.MapCreate;
        bool lockedShot = SpecialSkillRuntime.TryGetSniperTarget(Attacker, out var lockedTarget) && lockedTarget == Target;
        if (!lockedShot && terrain != null && !terrain.CanAttackAcrossTerrain(Attacker, Target.transform.position)) return;

        // スタン中は行動不可
        if (StatusEffectSystem.IsStunned(Attacker))
        {
            { if (DevelopmentLog.IsEnabled(AILogLevel.Verbose)) DevelopmentLog.Log($"[Battle] {Attacker.kind} はスタン中で行動不可"); }
            return;
        }

        if (!SpecialSkillRuntime.TryBeginNormalShot(Attacker, out var firedRound)) return;
        BoardActorAnimation.NotifyAttack(Attacker, Target.transform.position);
        SpecialSkillRuntime.BeginNormalAttack(Attacker);
        float lockedDistance = lockedShot ? GridHelper.ChebyshevDistance(Attacker.transform.position, Target.transform.position) : -1;

        // シールド中はダメージ無効
        if (Target.HasDamageShield)
        {
            BarrierSystem.RecordNegatedHit(Target);
            SpecialSkillRuntime.OnAttackLanded(Attacker);
            SpecialSkillRuntime.CompleteNormalShot(Attacker, Target, firedRound, false);
            SpecialSkillRuntime.OnNormalAttackResolved(Attacker, Target, 0, false, lockedDistance);
            { if (DevelopmentLog.IsEnabled(AILogLevel.Verbose)) DevelopmentLog.Log($"[Battle] {Target.kind} はシールド中！ ダメージ無効（残り{Target.ShieldTurns}ターン）"); }
            FloatingDamageUI.ShowShield(Target.transform.position);
            BoardActionFeedback.Shield(Target);
            return;
        }

        // ダメージ計算（パッシブスキル補正は DamageCalculator 内で自動適用）
        int damage = SkillSystem.CalcNormalDamage(Attacker, Target);

        // Crossbow: 命中時10%でスタン付与
        if (Attacker.kind == Kind.Crossbow && damage > 0 && SpecialSkillRuntime.Definition(Attacker)?.enabledForPlay != true)
        {
            if (Random.value < GameConstants.CrossbowStunChance)
            {
                StatusEffectSystem.ApplyDebuff(Target, StatusEffectType.Stun, 1);
                { if (DevelopmentLog.IsEnabled(AILogLevel.Verbose)) DevelopmentLog.Log($"[Battle] {Attacker.kind} のスタン発動！ {Target.kind} は1ターン行動不可"); }
            }
        }

        // MagicSniper: 攻撃ごとに最大HPの20%自傷 + 敵にマーキング
        if (Attacker.kind == Kind.Magicsniper && damage > 0 && !SpecialSkillRuntime.HasAbility(Attacker, UnitSpecialSkill.MagicSniperLock))
        {
            int selfDmg = Mathf.RoundToInt(Attacker.MaxHP * GameConstants.MagicSniperSelfDamageRatio);
            SpecialSkillRuntime.ApplyHpChange(Attacker, Attacker, selfDmg, HpChangeKind.NormalDamage, cause: HpChangeCause.SelfRecoil);
            { if (DevelopmentLog.IsEnabled(AILogLevel.Verbose)) DevelopmentLog.Log($"[Battle] {Attacker.kind} 自傷ダメージ {selfDmg}（残HP:{Attacker.HP}）"); }

            StatusEffectSystem.ApplyDebuff(Target, StatusEffectType.Mark, 1);
            { if (DevelopmentLog.IsEnabled(AILogLevel.Verbose)) DevelopmentLog.Log($"[Battle] {Target.kind} にマーキング付与（被ダメ+10%、1ターン）"); }
        }

        // Special Ability: 致死ダメージ耐え（生還本能）
        if (SpecialSkillRuntime.WouldAssassinate(Attacker, Target)) damage = Mathf.Max(damage, Target.HP);
        damage = ApplyDamage(damage);

        // Special Ability: 攻撃命中時効果（単体攻撃 = true）
        SpecialAbilitySystem.OnAttackHit(Attacker, Target, damage, true);
        SpecialSkillRuntime.CompleteNormalShot(Attacker, Target, firedRound, damage > 0);
        SpecialSkillRuntime.OnAttackLanded(Attacker);
        SpecialSkillRuntime.OnNormalAttackResolved(Attacker, actualRecipient ?? Target, damage, (actualRecipient ?? Target).HP <= 0, lockedDistance);

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
        if (!Attacker.IsAlive) Attacker.HandleDeathIfDead();
    }

    // ─── 防御側パッシブ ───────────────────────────────────────────────
    // Knight の視界内軽減/視界外増加は DamageCalculator.GetDefenderPassiveMultiplier で処理

    // ═══════════════════════════════════════════════════════════════════
    //  旧メインクリスタルシールドの互換値を無効化
    // ═══════════════════════════════════════════════════════════════════
    private void CheckCrystalShield() => MainCrystalRules.ClearLegacyShield(Target);

    /// <summary>Legacy call compatibility: clear the abolished main-crystal shield, never tick or activate it.</summary>
    public static void TickCrystalShields(Transform crystalParent)
    {
        if (crystalParent == null) return;
        foreach (var crystal in crystalParent.GetComponentsInChildren<Status>()) MainCrystalRules.ClearLegacyShield(crystal);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ダメージ適用
    // ═══════════════════════════════════════════════════════════════════
    private int ApplyDamage(int damage)
    {
        bool crystalAttackAttempt = damage > 0;
        var damageKind = SpecialSkillRuntime.WouldAssassinate(Attacker, Target) ? HpChangeKind.ExecutionDamage : HpChangeKind.NormalDamage;
        var recipient = SpecialSkillRuntime.ResolveDamageRecipient(Target, damage, damageKind) ?? Target;
        actualRecipient = recipient;
        damage = SpecialSkillRuntime.ApplyHpChange(Attacker, Target, damage, damageKind, cause: HpChangeCause.NormalAttack);
        SpecialSkillRuntime.CompleteSpecialDeath(recipient == Target ? null : recipient);
        { if (DevelopmentLog.IsEnabled(AILogLevel.Verbose)) DevelopmentLog.Log($"[Battle] {Attacker.kind} → {Target.kind}  DMG:{damage}  残HP:{Target.HP}"); }

        // Combat attribution and unique kill rewards remain independent of abolished unit XP.
        if (damage > 0 && Attacker != null && Attacker.team != Target.team)
        {
            Status.AwardDamageExperience(Attacker, recipient, damage, turnGenerator?.Systems?.FactionState);
        }

        // 反逆の騎士王: 反撃バフ有効時は受けたダメージの1.5倍を反射
        if (damage > 0) WildBossSystem.TryReflectDamage(recipient, Attacker, damage);

        // StrangeKingAura: 与ダメージの20%を吸収してHP回復
        if (damage > 0 && Attacker != null &&
            Attacker.passiveskill == PassiveSkill.StrangeKingAura && Attacker.team != Target.team)
        {
            int heal = Mathf.RoundToInt(damage * GameConstants.StrangeKingLifesteal);
            if (heal > 0) Attacker.ApplyHeal(heal);
        }

        // 正式な攻撃済み予約だけを記録し、反撃ダメージは攻撃陣営の終了まで遅延。
        if (crystalAttackAttempt && Target.kind == Kind.Crystal && Attacker != null && Attacker.team != Target.team)
            MainCrystalRules.RegisterAttack(Target, Attacker.team, turnGenerator?.Context.Turn ?? -1);

        bool isKill = !recipient.IsAlive;
        if (damage <= 0) FloatingDamageUI.ShowMiss(Target.transform.position);

        // 実績
        if (isKill && Attacker != null && Attacker.team == Team.Player && Target.team != Team.Player)
        {
            AchievementSystem.GetOrCreate().OnKill();
            if (recipient.kind == Kind.Crystal)
                AchievementSystem.Instance.OnCrystalKill((float)damage / System.Math.Max(1, recipient.MaxHP));
            if (recipient.isWildBoss)
                AchievementSystem.Instance.OnWildBossDefeated();
        }

        // 統計記録
        if (MatchStats.Instance != null)
        {
            MatchStats.Instance.RecordDamage(Attacker != null ? Attacker.team : Team.None,
                recipient.team, damage, isKill, recipient.isWildBoss);
        }
        return damage;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  旧反撃APIは互換目的のみ。正式な終了時処理は BattleSystem.CrystalCounters に集約
    // ═══════════════════════════════════════════════════════════════════
    /// <summary>Legacy entry points intentionally cannot fire an on-hit/start-of-turn counter.</summary>
    public static void ProcessCrystalCounter(GameSystems systems, Status crystal, CrystalCounterTrigger trigger) { }
    public static void ProcessCrystalCounterAtTurnStart(GameSystems systems, Team team) { }

    // ═══════════════════════════════════════════════════════════════════
    //  HP0 判定 → 駒 / ゲーム終了対象 に分岐
    // ═══════════════════════════════════════════════════════════════════
    private void CheckDeath()
    {
        if (Target.HP > 0) return;

        if (MatchObjectiveRules.IsDecisive(Target))
        {
            HandleGameEnd();
        }
        else if (Target.type == Type.Unit)
        {
            HandleUnitDeath();
        }
        else if (Target.type == Type.Building || Target.type == Type.Wall)
            turnGenerator.Systems.SubCrystalSystem?.DestroyBuilding(Target);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  A) 一般駒がHP0 → 盤面から除外
    // ═══════════════════════════════════════════════════════════════════
    private void HandleUnitDeath()
    {
        if (Target == null) return;
        { if (DevelopmentLog.IsEnabled(AILogLevel.Verbose)) DevelopmentLog.Log($"[Battle] {Target.team} の {Target.kind} が撃破された"); }

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

        Target.DeactivateAfterDeath();

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
            { if (DevelopmentLog.IsEnabled(AILogLevel.Verbose)) DevelopmentLog.Log($"[Battle] 敵 {Target.kind} 破壊 → 勝利！"); }
        }
        else
        {
            result = GameResult.Lose;
            { if (DevelopmentLog.IsEnabled(AILogLevel.Verbose)) DevelopmentLog.Log($"[Battle] 自軍 {Target.kind} 破壊 → 敗北…"); }
        }

        turnGenerator.ChangeState(new GameEndState(turnGenerator, result, Target));
    }
}
