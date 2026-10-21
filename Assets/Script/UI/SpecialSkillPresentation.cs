using System.Text;
using UnityEngine;

/// <summary>Japanese text reads the same definition and durable state as gameplay.</summary>
public static class SpecialSkillPresentation
{
    public static string Name(UnitSpecialSkill ability) => ability switch
    {
        UnitSpecialSkill.KnightBreakthrough => "前方突破",
        UnitSpecialSkill.ArcherStationary => "毒霧の退避",
        UnitSpecialSkill.MagicRitual => "生命変換必中魔法",
        UnitSpecialSkill.AssassinVisionSeal => "完全視界封鎖",
        UnitSpecialSkill.ScoutVisionGuard => "視界犠牲防御",
        UnitSpecialSkill.PriestPurification => "完全浄化",
        UnitSpecialSkill.GuardianLifeLink => "生命接続",
        UnitSpecialSkill.CrossbowStakes => "雷杭",
        UnitSpecialSkill.MagicSniperLock => "超遠距離ロック",
        UnitSpecialSkill.LifeBomb => "生命爆弾",
        UnitSpecialSkill.WhiteMageHeal => "白魔法の回復",
        UnitSpecialSkill.HolyKnightSanctuary => "聖騎士の聖域",
        UnitSpecialSkill.SamuraiCounter => "刀ゲージ・反撃",
        UnitSpecialSkill.NinjaSwap => "味方との位置交換",
        UnitSpecialSkill.BerserkerRage => "狂戦士の強化",
        UnitSpecialSkill.NavigatorPull => "正面への引き寄せ",
        UnitSpecialSkill.HeavyWarriorBreak => "バリア砕き",
        UnitSpecialSkill.DragonKnightPierce => "竜槍貫通",
        _ => "なし"
    };

    public static string Name(SpecialSkillDefinition definition) => definition == null ? "駒固有能力なし"
        : string.IsNullOrWhiteSpace(definition.displayName) ? Name(definition.ability) : definition.displayName;

    public static string Description(SpecialSkillDefinition definition)
    {
        if (definition == null) return "この駒には駒固有能力が設定されていません。";
        string text = definition.ability switch
        {
            UnitSpecialSkill.KnightBreakthrough => "正面へ最大3マス、敵を押して前進します。後方が塞がると停止します。",
            UnitSpecialSkill.ArcherStationary => "発動時の周囲2マスの敵を記憶。2ターン、その敵から自身が見えなくなり、敵へ毒を付与します。対象敵のATKは20%上昇し、自身は攻撃できません（移動は可能）。",
            UnitSpecialSkill.MagicRitual => $"視界内の敵駒1体をロックし、選択した周囲の味方から最大HPの{SpecialSkillDefinition.RitualSacrifice:P0}を直接削減。次の自軍ターンに削減合計と同じ量のダメージを与えます（バリア優先）。供儀HPは返りません。使用ターンは移動できません。",
            UnitSpecialSkill.AssassinVisionSeal => "視界内の敵1体の視界を2ターン0にします。自身に「次に実際に受けるダメージ×2」が付き、被弾するまで残ります。",
            UnitSpecialSkill.ScoutVisionGuard => "発動時の視界内の味方に、2ターンの間「次のダメージ1回無効」を付与。自身の視界は2低下します。後から範囲へ入った味方には付きません。",
            UnitSpecialSkill.PriestPurification => "自身が強制死亡し、死亡地点の周囲2マスにいる敵味方の通常バフ・デバフを解除します。爆弾・生命接続・杭・標的ロック等の特殊状態は消しません。",
            UnitSpecialSkill.GuardianLifeLink => "味方の駒または建物1つと生命接続します。受けるダメージを相手が肩代わりします。攻撃された側にとって致死ダメージなら、その側を救い、接続相手が死亡して接続を解除します。",
            UnitSpecialSkill.CrossbowStakes => "弾倉へ杭弾を1発装填。満弾なら決定的な抽選で1発を杭弾へ変換します。弾は古い順に発射し、杭弾の命中時だけ敵へ杭を付けます。杭2本が作る雷線は対象の移動に追従し、侵入・滞在でダメージ。杭の寿命は各3ターン。",
            UnitSpecialSkill.MagicSniperLock => $"視界内の敵1体をロック。次ターンはその敵だけを距離・視界に関係なく通常攻撃できます。距離{SpecialSkillDefinition.SniperMaximumDistance}マスで上限×{SpecialSkillDefinition.SniperMaximum:0.##}、射撃時の距離反動は最大HPの上限{SpecialSkillDefinition.SniperMaximumRecoil:P0}。通常攻撃の20%自傷も同時に発生します。",
            UnitSpecialSkill.LifeBomb => $"自分または味方へ追従する爆弾を付与。{SpecialSkillDefinition.BombDelay}ターン後、所持者の最大HPの{SpecialSkillDefinition.BombFraction:P0}を直接削減し、同じ基準量のダメージに変えます（バリア優先）。爆発範囲・陣営・死亡時処理は設定に従います。",
            UnitSpecialSkill.WhiteMageHeal => $"視界内の味方駒1体を最大HPの{SpecialSkillDefinition.WhiteMageHealFraction:P0}回復します。通常攻撃も行えます。",
            UnitSpecialSkill.HolyKnightSanctuary => $"自身中心の設定範囲で3ターン、毎ターン最大HPの{SpecialSkillDefinition.HolyKnightHealFraction:P0}回復。発動中は移動・攻撃不可で被ダメージ×2。範囲と中断条件は設定に従います。",
            UnitSpecialSkill.SamuraiCounter => "敵の駒1体を挑発。3ターン、その敵の通常攻撃先をこの侍だけに制限します。その敵から被弾すると、反撃を受ける相手の現在バリアを0にしてから反撃します。バリアの最大値は変えません。",
            UnitSpecialSkill.NinjaSwap => "マップ上の自分以外の味方駒1体と位置交換します。距離・自身の視界による制限はありません。敵は選べません。",
            UnitSpecialSkill.BerserkerRage => $"使用するたびに強化が1段階進み、最大5段階になります。攻撃力上昇（上限×{SpecialSkillDefinition.BerserkerMaximum:0.##}）・通常攻撃AP＋1。次の通常攻撃でこのスキルによる強化を解除します。各段階の倍率は設定に従います。一部効果は未調整です。",
            UnitSpecialSkill.NavigatorPull => "視界内の敵1体を自分の正面1マスへ強制移動します。",
            UnitSpecialSkill.HeavyWarriorBreak => $"視界内・周囲1マスの敵1体を通常攻撃相当の威力で攻撃し、バリアに当たる部分を×{definition.heavyBarrierMultiplier:0.##}にします。対バリアの追加ダメージはHPへ持ち越しません。",
            UnitSpecialSkill.DragonKnightPierce => $"竜槍ゲージ2で使用できます。視界内・周囲1マスの敵1体へ通常攻撃相当の威力×{definition.dragonKnightSkillMultiplier:0.##}で攻撃し、バリアを消耗せずHPへダメージを与えます。発動後ゲージは0に戻ります。完全防御シールドは貫通しません。",
            _ => "駒固有能力の種類を設定してください。"
        };
        if (!definition.IsPassive && definition.ability != UnitSpecialSkill.BerserkerRage)
            text += $"\n使用AP：{Configured(definition.apCost)}　再使用待機：{Configured(definition.cooldown)}ターン";
        if (definition.ability == UnitSpecialSkill.BerserkerRage && definition.stageAPCosts != null)
            text += "\n1〜5段階の強化AP：" + string.Join(" / ", definition.stageAPCosts) + $"　再使用待機：{Configured(definition.cooldown)}ターン";
        if (definition.ConfigurationError != null) text += "\n<color=#FFD0A0>" + definition.ConfigurationError + "</color>";
        if (BarrierCombatSettings.Active.trialBalance
            && (definition.ability == UnitSpecialSkill.HeavyWarriorBreak || definition.ability == UnitSpecialSkill.DragonKnightPierce))
            text += "\n<color=#FFD0A0>威力・AP・再使用待機は暫定の調整値です。</color>";
        return text;
    }

    public static string BarrierRecoveryDescription()
    {
        var settings = BarrierCombatSettings.Active;
        string timing = settings.recoveryAtOwnerTurnStart ? "自軍ターン開始時" : "自軍ターン終了時";
        string first = settings.recoverOnThresholdTurn ? "条件を満たした判定から回復" : "条件達成の次の判定から回復";
        string repeat = settings.repeatRecoveryEveryTurn ? "以後も条件を満たす毎ターンに回復" : "無被弾が続く間に1回だけ回復";
        string rounding = settings.recoveryRounding == BarrierRecoveryRounding.Floor ? "切り捨て"
            : settings.recoveryRounding == BarrierRecoveryRounding.Nearest ? "最も近い整数に丸め" : "切り上げ";
        string negated = settings.fullyNegatedHitsResetRecovery ? "完全無効の攻撃でもカウントをリセット" : "完全無効の攻撃ではカウントを維持";
        return $"HP70%以上・連続{BarrierSystem.SafeTurnThreshold}ターン無被弾で、最大バリア20%を回復。"
            + $"\n暫定設定：{timing}に判定し、{first}。{repeat}。端数は{rounding}。{negated}。";
    }

    static string Configured(int value) => value < 0 ? "未設定" : value.ToString();
    public static string PassiveDescription(SpecialSkillDefinition definition)
    {
        if (definition == null) return "なし";
        switch (definition.ability)
        {
            case UnitSpecialSkill.KnightBreakthrough:
                return "視界内の敵ATK低下：" + (definition.knightVisibleEnemyAtkPenalty < 0 ? "未設定" : definition.knightVisibleEnemyAtkPenalty.ToString("P0"))
                    + $"。自身の視界外からの攻撃は通常倍率にさらに×{SpecialSkillDefinition.KnightBlindSpotMultiplier}。";
            case UnitSpecialSkill.ArcherStationary: return $"未移動で攻撃倍率上昇（最大×{SpecialSkillDefinition.ArcherMaximum}）、強制移動でも蓄積リセット。";
            case UnitSpecialSkill.SamuraiCounter: return "通常攻撃で刀ゲージ上昇。MAX時に被弾すると、元の攻撃を受けた後、相手の現在バリアを0にして、受けたダメージの半分＋ATKで反撃します。反撃で減らしたHP量だけ自身のHPを回復。完全防御シールド・1回無効は尊重します。MAX保持は3ターン。";
            case UnitSpecialSkill.MagicRitual: return "周囲1マスにHP25%以下の駒がいる間、Crystal・Boss・強敵への攻撃×1.5、それ以外×0.75。";
            case UnitSpecialSkill.AssassinVisionSeal: return "通常移動は常に3AP（疲労・地形・移動AP補正を受けません）。自軍ターン終了時に1回、自分以外の味方の視界に入っていなければ最大HPの10%を失います（端数は切り捨て、最低1）。味方の建物・クリスタルの視界も有効です。";
            case UnitSpecialSkill.ScoutVisionGuard: return "視界内の敵味方の移動AP・攻撃APを各1回無料化（リセット範囲は設定）。";
            case UnitSpecialSkill.PriestPurification: return "毎ターン、周囲1マスの敵味方の駒を最大HP5%回復。";
            case UnitSpecialSkill.GuardianLifeLink: return "Crystalへのダメージ×2。通常攻撃ごとに攻撃AP＋1累積（リセット範囲は設定）。";
            case UnitSpecialSkill.CrossbowStakes: return "弾倉は最大3発。通常攻撃ごとに古い弾を1発消費し、攻撃しなかったターンには1発補充。";
            case UnitSpecialSkill.MagicSniperLock: return "通常攻撃ごとに最大HP20%自傷。対象のHP割合が自身より低い場合ダメージ×1.5。";
            case UnitSpecialSkill.LifeBomb: return "実際にダメージを与えず10ターン経過すると、現在HP×2で敵味方・Crystalを巻き込んで自爆（範囲は設定）。";
            case UnitSpecialSkill.HolyKnightSanctuary: return "攻撃成功で敵ATK25%を奪い、次ターン自身へ加算。2ターン後は自身の現在ATK80%を元の敵へ加算し、そのターン終了で解除。";
            case UnitSpecialSkill.WhiteMageHeal: return "なし（R1では未設定）";
            case UnitSpecialSkill.NinjaSwap: return "未攻撃ターンでクナイを最大5段階強化。視界外攻撃は通常の40%。";
            case UnitSpecialSkill.BerserkerRage: return "HPが少ないほど通常攻撃APが低下します（設定した曲線に従います）。";
            case UnitSpecialSkill.NavigatorPull: return $"正面1マスへ通常攻撃。残りAPに応じて最大×{SpecialSkillDefinition.NavigatorMaximum}。";
            case UnitSpecialSkill.HeavyWarriorBreak: return $"通常攻撃はバリアを先に減らします。通常攻撃の対バリア部分の倍率×{BarrierCombatSettings.Active.heavyNormalBarrierMultiplier:0.##}。";
            case UnitSpecialSkill.DragonKnightPierce: return "通常攻撃を使うたびに竜槍ゲージ＋1（最大2）。通常攻撃はバリアを貫通しません。";
            default: return "常時能力はありません。スキルの効果・代償は上記の固有スキルに従います。";
        }
    }

    public static bool HasState(Status actor)
    {
        var state = actor?.Special;
        return SpecialSkillRuntime.Definition(actor) != null || state != null &&
            (state.ritualActive || state.sniperActive || !string.IsNullOrEmpty(state.guardianPartnerId)
            || !string.IsNullOrEmpty(state.stakeAId) || !string.IsNullOrEmpty(state.bombCasterId)
            || state.nextDamageDouble || state.visionZeroTurns > 0 || state.damageNullUnused
            || state.holyKnightTurns > 0 || state.samuraiGauge > 0 || state.ninjaCharge > 0 || state.berserkerStage > 0 || state.dragonKnightGauge > 0);
    }

    public static string Summary(Status actor)
    {
        var state = actor?.Special;
        if (state == null) return string.Empty;
        if (actor.ShieldTurns > 0) return $"完全防御 {actor.ShieldTurns}T";
        if (state.damageNullUnused && state.damageNullTurns > 0) return "1回無効";
        if (state.nextDamageDouble) return "次の被ダメージ×2";
        if (state.visionZeroTurns > 0) return $"視界0 {state.visionZeroTurns}T";
        if (!string.IsNullOrEmpty(state.bombCasterId)) return "生命爆弾";
        if (state.ritualActive) return $"儀式 {state.storedSacrificeDamage}";
        if (!string.IsNullOrEmpty(state.guardianPartnerId)) return "生命接続";
        if (state.sniperActive) return "狙撃ロック";
        var definition = SpecialSkillRuntime.Definition(actor);
        if (definition?.ability == UnitSpecialSkill.DragonKnightPierce)
            return $"竜槍 {state.dragonKnightGauge}/2";
        if (definition?.ability == UnitSpecialSkill.SamuraiCounter && state.samuraiGauge > 0)
            return $"刀 {state.samuraiGauge}/{definition.samuraiGaugeMax}";
        if (state.berserkerStage > 0) return $"強化 {state.berserkerStage}/5";
        if (state.ninjaCharge > 0) return $"クナイ {state.ninjaCharge}/5";
        if (state.holyKnightTurns > 0) return $"聖域 {state.holyKnightTurns}T";
        if (definition?.ability == UnitSpecialSkill.ArcherStationary && state.stationaryTurns > 0)
            return $"未移動 {state.stationaryTurns}T　攻撃×{definition.ArcherMultiplier(state.stationaryTurns):0.##}";
        return string.Empty;
    }

    public static string Details(Status actor, int round)
    {
        if (actor == null) return string.Empty;
        var definition = SpecialSkillRuntime.Definition(actor);
        var state = actor.Special;
        var text = definition != null && definition.ability != UnitSpecialSkill.None
            ? new StringBuilder("<b>固有スキル</b>\n").Append(definition.IsPassive ? "使用スキルなし（常時発動）" : Description(definition))
                .Append("\n\n<b>固有アビリティ</b>\n").Append(PassiveDescription(definition))
            : new StringBuilder("<b>通常スキル</b>\n").Append(SkillDescription(actor.AssignedSkillId))
                .Append("\n\n<b>常時能力</b>\n").Append(PassiveDescription(actor.passiveskill))
                .Append("\n\n<b>設定されたアビリティ</b>\n").Append(AbilityDescription(actor.specialAbility));
        if (actor.type == Type.Unit)
        {
            int safe = Mathf.Clamp(actor.NaturalRecovery?.ConsecutiveSafeTurns ?? 0, 0, NaturalRecoverySystem.SafeTurnThreshold);
            text.Append("\n\n<b>共通・自然回復</b>\nHPを失わなかった5ターン目の終了時から、毎自軍ターン終了時に最大HP10%回復。")
                .Append("\n無損失経過：").Append(safe).Append(" / 5ターン／回復量：").Append(NaturalRecoverySystem.HealAmount(actor.MaxHP));
        }
        if (BarrierSystem.UsesBarrier(actor))
        {
            int safe = Mathf.Clamp(actor.BarrierRecovery?.SafeTurns ?? 0, 0, BarrierSystem.SafeTurnThreshold);
            text.Append("\n\n<b>バリア</b>\n現在 ").Append(actor.Barrier).Append(" / ").Append(actor.MaxBarrier)
                .Append("。先にバリアを消耗し、超過分だけHPが減少します。最大値は毎Lv＋").Append(BarrierSystem.GrowthPerLevel).Append('。')
                .Append('\n').Append(BarrierRecoveryDescription())
                .Append("\n無被弾経過：").Append(safe).Append(" / ").Append(BarrierSystem.SafeTurnThreshold)
                .Append("ターン。HP自然回復・完全防御シールド・軽減障壁とは別の効果です。");
        }
        if (actor.ShieldTurns > 0)
            text.Append("\n\n<b>完全防御シールド</b>\nダメージを無効化。残り ").Append(actor.ShieldTurns).Append("ターン。");
        if (state == null) return text.ToString();
        text.Append("\n\n<b>現在の特殊状態</b>");
        bool any = false;
        void Line(string value) { text.Append('\n').Append(value); any = true; }
        if (definition?.ability == UnitSpecialSkill.CrossbowStakes) Line(SpecialSkillRuntime.CrossbowMagazineText(actor));
        if (state.archerEscapeTurns > 0) Line($"毒霧の退避：あと{state.archerEscapeTurns}ターン（攻撃不可）／対象{state.archerSnapshotIds.Count}体");
        if (state.tauntTurns > 0) Line($"挑発：あと{state.tauntTurns}ターン／攻撃先：{TargetName(state.tauntedById)}");
        if (state.guardianAttackCount > 0) Line($"攻撃APの累積増加：＋{state.guardianAttackCount}");
        if (definition?.ability == UnitSpecialSkill.LifeBomb) Line($"自爆まで：無加害{state.bomberIdleTurns} / 10ターン");
        if (state.cooldown > 0) Line($"再使用待機：あと{state.cooldown}ターン");
        if (definition?.ability == UnitSpecialSkill.ArcherStationary)
            Line($"未移動：{state.stationaryTurns}ターン／現在の攻撃倍率×{definition.ArcherMultiplier(state.stationaryTurns):0.##}");
        if (state.ritualActive) Line($"儀式対象：{TargetName(state.ritualTargetId)}／供儀合計：{state.storedSacrificeDamage}／発射まで自軍{Mathf.Max(0, state.ritualFireTurn - state.ownerTurn)}ターン");
        if (state.nextDamageDouble) Line("次に実際に受けるダメージ×2（被弾まで持続）");
        if (state.visionZeroTurns > 0) Line($"視界0：あと{state.visionZeroTurns}ターン");
        if (state.scoutVisionPenaltyTurns > 0) Line($"斥候の視界−2：あと{state.scoutVisionPenaltyTurns}ターン");
        if (state.damageNullUnused && state.damageNullTurns > 0) Line($"【無効】次のダメージ1回無効：あと{state.damageNullTurns}ターン");
        if (!string.IsNullOrEmpty(state.guardianPartnerId)) Line("生命接続先：" + TargetName(state.guardianPartnerId));
        if (!string.IsNullOrEmpty(state.stakeAId))
        {
            int age = Mathf.Max(1, state.ownerTurn - state.stakeAAppliedTurn + 1);
            Line($"杭A：{TargetName(state.stakeAId)}／残り{Mathf.Max(0, SpecialSkillDefinition.StakeLifetime - age + 1)}ターン");
            if (!string.IsNullOrEmpty(state.stakeBId)) Line($"杭B：{TargetName(state.stakeBId)}／残り{Mathf.Max(0, SpecialSkillDefinition.StakeLifetime - Mathf.Max(1, state.ownerTurn - state.stakeBAppliedTurn + 1) + 1)}ターン");
            if (!string.IsNullOrEmpty(state.stakeBId)) Line($"雷線の現在倍率：×{SpecialSkillRuntime.LightningMultiplier(actor):0.##}");
        }
        if (state.sniperActive)
        {
            Line("狙撃対象：" + TargetName(state.sniperTargetId));
            if (definition != null && TryFind(state.sniperTargetId, out var target))
            {
                float distance = Mathf.Max(Mathf.Abs(actor.GridPosition.x - target.GridPosition.x), Mathf.Abs(actor.GridPosition.z - target.GridPosition.z));
                Line($"距離：{distance:0.#}マス／攻撃×{definition.SniperMultiplier(distance):0.##}／反動：最大HPの{definition.SniperRecoil(distance):P0}");
            }
            Line($"発射可能まで自軍{Mathf.Max(0, state.sniperFireTurn - state.ownerTurn)}ターン");
        }
        if (!string.IsNullOrEmpty(state.bombCasterId)) Line($"生命爆弾：あと{Mathf.Max(0, state.bombDueRound - round)}ターン／予定威力：{SpecialSkillRuntime.FractionOfMaximum(actor, SpecialSkillDefinition.BombFraction)}");
        if (state.samuraiGauge > 0) Line($"刀ゲージ：{state.samuraiGauge}／MAX保持残り：{state.samuraiMaxTurns}ターン");
        if (definition?.ability == UnitSpecialSkill.DragonKnightPierce)
            Line($"竜槍ゲージ：{state.dragonKnightGauge} / 2（通常攻撃1回につき＋1、固有スキル発動後に0）");
        if (state.ninjaCharge > 0) Line($"クナイ強化：{state.ninjaCharge}段階");
        if (state.berserkerStage > 0) Line($"狂戦士の強化：{state.berserkerStage}段階");
        if (state.holyKnightTurns > 0 || state.holyKnightRecoveryLock) Line($"聖域：回復あと{state.holyKnightTurns}回（このターン移動・攻撃不可／被ダメージ×2）");
        if (!any) Line("現在、付与された特殊状態はありません。");
        return text.ToString();
    }

    public static bool TryFind(string id, out Status target)
    {
        target = SpecialSkillRuntime.FindActor(id);
        return target != null && target.IsAlive;
    }
    static string TargetName(string id) => TryFind(id, out var target) ? KindNameJP.Get(target) : "対象不在（解除待ち）";

    public static string SkillDescription(int id)
    {
        if (!SkillData.Table.TryGetValue(id, out var skill)) return "なし（割当が未設定です）";
        var text = new StringBuilder(skill.Name).Append("　AP ").Append(skill.APCost)
            .Append("\n対象：").Append(TargetDescription(skill.Target)).Append("／範囲：").Append(AreaDescription(skill.Area));
        if (skill.Multiplier > 0) text.Append("\n攻撃倍率×").Append(skill.Multiplier.ToString("0.##"));
        if (skill.SecondMultiplier > 0) text.Append("／2段目×").Append(skill.SecondMultiplier.ToString("0.##"));
        if (skill.FixedHeal > 0) text.Append("\n回復量：").Append(skill.FixedHeal);
        if (skill.FixedDamage > 0) text.Append("\n固定効果値：").Append(skill.FixedDamage);
        if (skill.InflictDebuff != StatusEffectType.None) text.Append("\n").Append(DebuffName(skill.InflictDebuff)).Append("を付与 ").Append((skill.DebuffChance * 100).ToString("0.#")).Append('%');
        if (skill.GrantBuff != BuffType.None) text.Append("\n").Append(skill.BuffToSelf ? "自身に" : "対象に").Append(BuffName(skill.GrantBuff)).Append("を付与");
        if (skill.InflictDebuff == StatusEffectType.ArmorBreak || skill.GrantBuff == BuffType.Defensive)
            text.Append("\n一部効果は未調整です。");
        if (!string.IsNullOrEmpty(skill.SpecialEffect)) text.Append("\n特殊効果：").Append(skill.SpecialEffect switch
        {
            "SmashBuilding" => "建物への追加効果", "FlamePoison" => "炎と毒", "ShadowRush" => "影の急襲",
            "BloodSacrifice" => "HPを代償にした攻撃", "DeathSight" => "死を見据える狙撃", "SiegeBreaker" => "建物への破壊攻撃",
            "JudgementMark" => "裁きと標的付与", "Catastrophe" => "自傷を伴う範囲攻撃", "LastSignal" => "味方への攻勢付与", _ => "選択したスキル固有の効果"
        });
        return text.ToString();
    }

    public static string PassiveDescription(PassiveSkill ability) => ability switch
    {
        PassiveSkill.Impregnable => $"鉄壁：被ダメージ×{GameConstants.ImpregnableDamageReduction:0.##}",
        PassiveSkill.HunterEyes => $"狩人の目：自身の視界内の敵へのダメージ×{GameConstants.HunterEyesDamageBonus:0.##}",
        PassiveSkill.Destroyer => $"破壊者：建物・クリスタルへのダメージ×{GameConstants.DestroyerBuildingBonus:0.##}",
        PassiveSkill.Assassination => $"暗殺：背面攻撃の追加倍率×{GameConstants.AssassinationBackAttackBonus:0.##}",
        PassiveSkill.Sniper => $"狙撃：距離{GameConstants.SniperMinRange}マス以上へのダメージ×{GameConstants.SniperLongRangeBonus:0.##}",
        PassiveSkill.StrangeKingAura => $"異形の王のオーラ：被ダメージ×{GameConstants.StrangeKingDamageReduction:0.##}、被害の{GameConstants.StrangeKingLifesteal:P0}を回復。HP{GameConstants.StrangeKingRageThreshold:P0}以下で攻撃×{GameConstants.StrangeKingRageATKBonus:0.##}",
        _ => "なし"
    };
    public static string AbilityDescription(SpecialAbility ability) => ability == SpecialAbility.WallFamiliar ? "防壁慣れ：効果未調整"
        : SpecialAbilityData.Table.TryGetValue(ability, out var entry) ? entry.NameJP + "：" + entry.Description : "なし";
    public static string DebuffName(StatusEffectType value) => value switch
    {
        StatusEffectType.Stun => "スタン", StatusEffectType.Mark => "標的", StatusEffectType.ArmorBreak => "破甲", StatusEffectType.Weaken => "弱体",
        StatusEffectType.Poison => "毒", StatusEffectType.Chill => "冷気", StatusEffectType.Freeze => "凍結", StatusEffectType.Seal => "封技",
        StatusEffectType.Blind => "視界封じ", StatusEffectType.NarrowVision => "視界縮小", _ => "なし"
    };
    public static string BuffName(BuffType value) => value switch
    {
        BuffType.Offensive => "攻勢", BuffType.Defensive => "守勢", BuffType.Haste => "加速", BuffType.Insight => "看破",
        BuffType.Barrier => "軽減障壁", BuffType.Reflect => "反射", _ => "なし"
    };
    static string TargetDescription(SkillTarget value) => value switch
    {
        SkillTarget.Self => "自身", SkillTarget.EnemySingle => "敵1体", SkillTarget.AllySingle => "味方1体", SkillTarget.EnemyOrBuilding => "敵の駒・建物",
        SkillTarget.DirectionLine => "指定方向", SkillTarget.DesignatedTile => "指定マス", SkillTarget.SelfArea => "自身の周囲", SkillTarget.AdjacentCenter => "隣接中心",
        SkillTarget.LowHPEnemy => "HPが低い敵", SkillTarget.FlyingEnemy => "飛行・高所の敵", SkillTarget.DesignatedRow => "指定列", _ => "設定に従う"
    };
    static string AreaDescription(SkillAreaShape value) => value switch
    {
        SkillAreaShape.Single => "単体", SkillAreaShape.Cross1 => "十字1マス", SkillAreaShape.Cross2 => "十字2マス",
        SkillAreaShape.Line3 => "直線3マス", SkillAreaShape.Line4 => "直線4マス", SkillAreaShape.Line5 => "直線5マス", SkillAreaShape.Line7 => "直線7マス",
        SkillAreaShape.Area2x2 => "2×2", SkillAreaShape.Area3x3 => "3×3", SkillAreaShape.Area5x5 => "5×5", SkillAreaShape.Surround1 => "周囲1マス",
        SkillAreaShape.Surround2 => "周囲2マス", SkillAreaShape.SingleChain => "単体＋隣接1体", SkillAreaShape.SingleDouble => "単体2連撃", SkillAreaShape.Landing1 => "着弾1マス", _ => "設定に従う"
    };
}
