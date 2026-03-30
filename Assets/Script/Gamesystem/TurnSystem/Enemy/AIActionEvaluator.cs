using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  AIActionEvaluator — 行動評価計算（コーディネーター）
//  候補生成は AIActionGenerator に委譲。
//  AIAction データクラスは AIAction.cs に分離。
//  最終評価 = 基本評価 + 大きい性格補正 + 細かい性格補正 + 局面補正 + 学習補正
// =====================================================================
public static class AIActionEvaluator
{
    // ================================================================
    //  定数
    // ================================================================

    // --- フェーズ閾値（AIConstants から参照） ---
    const int TurnEarlyEnd = AIConstants.TurnEarlyEnd;
    const int TurnMidEnd   = AIConstants.TurnMidEnd;

    // --- 20ターン以降の生産施設優先度ブースト ---
    const int TurnProductionBoost = AIConstants.TurnProductionBoost;
    const float ProductionBoostScore = 55f;

    // --- 30ターン以降の全建築に対する大幅ブースト ---
    const int TurnLateBuildBoost = AIConstants.TurnLateBuildBoost;
    const float LateBuildBoostScore = 120f;

    // --- 建築の重複ペナルティ係数（count² × この値） ---
    const float DuplicatePenaltyFactor = 15f;


    // ================================================================
    //  共通ヘルパー
    // ================================================================

    /// <summary>ターンに応じたフェーズ別スコアを返す</summary>
    static float PhaseScore(int turn, float early, float mid, float late)
    {
        if (turn <= TurnEarlyEnd) return early;
        if (turn <= TurnMidEnd)   return mid;
        return late;
    }

    /// <summary>基礎経済施設5種(Well,LoggingCamp,Quarry,Field,House)の設置済み種類数</summary>
    static int CalcCoreEconomyCount(AIBoardState board)
    {
        return (board.GetBuildingCount(FacilityKind.Well) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.LoggingCamp) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.Quarry) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.Field) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.House) > 0 ? 1 : 0);
    }

    /// <summary>原料生産施設5種(Well,LoggingCamp,Quarry,Field,Mine)の設置済み種類数</summary>
    static int CalcRawFacilityCount(AIBoardState board)
    {
        return (board.GetBuildingCount(FacilityKind.Well) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.LoggingCamp) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.Quarry) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.Field) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.Mine) > 0 ? 1 : 0);
    }

    /// <summary>加工施設4種(Smelter,Bakery,LumberMill,StoneWorks)の設置済み種類数</summary>
    static int CalcProcessingFacilityCount(AIBoardState board)
    {
        return (board.GetBuildingCount(FacilityKind.Smelter) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.Bakery) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.LumberMill) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.StoneWorks) > 0 ? 1 : 0);
    }

    /// <summary>資源量に応じた緊急度ボーナス(枯渇→最大, 少量→中, やや不足→小)</summary>
    static float ResourceEmergencyBonus(int amount, float depleted, float low, float moderate,
        int lowThreshold = 20, int moderateThreshold = 50)
    {
        if (amount <= 0)                 return depleted;
        if (amount <= lowThreshold)      return low;
        if (amount <= moderateThreshold) return moderate;
        return 0f;
    }

    /// <summary>指定施設が基礎経済5種の中でまだ建っていないものかどうか</summary>
    static bool IsMissingCoreFacility(FacilityKind facility, AIBoardState board)
    {
        switch (facility)
        {
            case FacilityKind.Well:        return board.GetBuildingCount(FacilityKind.Well) == 0;
            case FacilityKind.LoggingCamp: return board.GetBuildingCount(FacilityKind.LoggingCamp) == 0;
            case FacilityKind.Quarry:      return board.GetBuildingCount(FacilityKind.Quarry) == 0;
            case FacilityKind.Field:       return board.GetBuildingCount(FacilityKind.Field) == 0;
            case FacilityKind.House:       return board.GetBuildingCount(FacilityKind.House) == 0;
            case FacilityKind.LumberMill:  return board.GetBuildingCount(FacilityKind.LumberMill) == 0;
            case FacilityKind.StoneWorks:  return board.GetBuildingCount(FacilityKind.StoneWorks) == 0;
            case FacilityKind.Bakery:      return board.GetBuildingCount(FacilityKind.Bakery) == 0;
            case FacilityKind.Smelter:     return board.GetBuildingCount(FacilityKind.Smelter) == 0;
            case FacilityKind.Mine:        return board.GetBuildingCount(FacilityKind.Mine) == 0;
            default: return false;
        }
    }

    static bool IsProcessingFacility(FacilityKind facility)
    {
        return facility == FacilityKind.LumberMill
            || facility == FacilityKind.StoneWorks
            || facility == FacilityKind.Bakery
            || facility == FacilityKind.Smelter;
    }

    // ---- 全候補行動を生成・評価してスコア順に返す ----
    public static List<AIAction> EvaluateAll(
        AIPersonality personality,
        AIBoardState board,
        AILearning learning,
        TurnStrategy strategy = TurnStrategy.Balanced)
    {
        var actions = new List<AIAction>();

        // 候補生成は AIActionGenerator に委譲
        AIActionGenerator.GenerateAllCandidates(board, actions);

        // 各候補にスコア付け
        foreach (var action in actions)
        {
            action.Score = CalcScore(action, personality, board, learning);
        }

        // ターン方針ボーナス
        ApplyStrategyBonus(actions, strategy, board);

        // 次ターン反撃圏ペナルティ
        ApplyCounterDangerPenalty(actions, board, personality);

        // 撤退→回復チェーンボーナス（強化版: ヒーラー/壁/味方カバーまで見る）
        ApplyRetreatRegroupBonus(actions, personality, board);

        // BOSS前線参加条件チェック
        ApplyBossFrontlineConditions(actions, personality, board);

        // 経済余裕による段階的召喚ボーナス
        ApplyGradualArmyExpansion(actions, board);

        // スコア降順
        actions.Sort((a, b) => b.Score.CompareTo(a.Score));
        return actions;
    }

    // ================================================================
    //  候補生成メソッドは AIActionGenerator.cs に移動済み
    // ================================================================
    // ================================================================
    //  ターン方針ボーナス
    //  AICommander が選んだ方針に合う行動にボーナスを与える
    // ================================================================
    static void ApplyStrategyBonus(List<AIAction> actions, TurnStrategy strategy, AIBoardState board)
    {
        foreach (var action in actions)
        {
            float bonus = 0f;
            switch (strategy)
            {
                case TurnStrategy.Assault:
                    if (action.ActionType == AIActionType.Attack) bonus += 18f;
                    if (action.ActionType == AIActionType.SkillUse && action.Skill != null && action.Skill.Multiplier > 0) bonus += 15f;
                    if (action.ActionType == AIActionType.Surround) bonus += 12f;
                    if (action.ActionType == AIActionType.Move)
                        bonus += GetApproachToEnemy(action, board) * 4f;
                    if (action.ActionType == AIActionType.Retreat) bonus -= 10f;
                    if (action.ActionType == AIActionType.Build) bonus -= 5f;
                    break;

                case TurnStrategy.CrystalDefense:
                    if (action.ActionType == AIActionType.DefenseRepos) bonus += 22f;
                    if (action.ActionType == AIActionType.Move && action.Unit != null)
                    {
                        // Scoutは防衛時でも偵察に出す（早期警戒）
                        if (action.Unit.kind == Kind.Scout)
                        {
                            int scoutNewCells = board.EstimateNewVisionCells(action.TargetPos);
                            if (scoutNewCells > 3) bonus += 15f;
                        }
                        else
                        {
                            float dist = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
                            if (dist < 4f) bonus += 18f;
                            else if (dist < 6f) bonus += 8f;
                        }
                    }
                    if (action.ActionType == AIActionType.Build && FacilityData.IsWall(action.Facility)) bonus += 15f;
                    if (action.ActionType == AIActionType.Attack)
                    {
                        // クリスタル付近の敵への攻撃は加点
                        if (action.TargetUnit != null)
                        {
                            float tDist = Vector3.Distance(action.TargetUnit.transform.position, board.EnemyCrystalPos);
                            if (tDist < 5f) bonus += 15f;
                        }
                    }
                    // クリスタルから離れる動きを抑制（Scoutは例外）
                    if (action.ActionType == AIActionType.Surround || action.ActionType == AIActionType.Move)
                    {
                        if (action.Unit != null && action.Unit.kind != Kind.Scout)
                        {
                            float destDist = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
                            if (destDist > 8f) bonus -= 12f;
                        }
                    }
                    break;

                case TurnStrategy.RetreatRegroup:
                    if (action.ActionType == AIActionType.Retreat) bonus += 20f;
                    if (action.ActionType == AIActionType.Support) bonus += 15f;
                    if (action.ActionType == AIActionType.SkillUse && action.Skill != null && action.Skill.FixedHeal > 0) bonus += 18f;
                    if (action.ActionType == AIActionType.SkillUse && action.Skill != null && action.Skill.GrantBuff == BuffType.Defensive) bonus += 10f;
                    if (action.ActionType == AIActionType.Attack) bonus -= 8f;
                    if (action.ActionType == AIActionType.Surround) bonus -= 12f;
                    break;

                case TurnStrategy.EconomyBuild:
                {
                    int coreCount = CalcCoreEconomyCount(board);

                    if (action.ActionType == AIActionType.Build)
                    {
                        bonus += 30f;
                        if (coreCount < 5)
                            bonus += IsMissingCoreFacility(action.Facility, board) ? 40f : -15f;

                        // ★ 生産チェーン逆算: 不足資源を生む施設を強く加点
                        var deficits = board.DiagnoseProductionChainDeficit();
                        for (int i = 0; i < deficits.Count; i++)
                        {
                            if (deficits[i] == action.Facility)
                            {
                                // リスト先頭ほど優先度が高い
                                bonus += Mathf.Max(5f, 50f - i * 10f);
                                break;
                            }
                        }
                    }
                    if (action.ActionType == AIActionType.SubCrystal) bonus += 15f;
                    if (action.ActionType == AIActionType.Summon)
                    {
                        bool hasBakery = board.GetBuildingCount(FacilityKind.Bakery) > 0;
                        bonus += (coreCount >= 5 && hasBakery) ? 10f : -40f;
                    }
                    if (action.ActionType == AIActionType.Attack)   bonus -= 10f;
                    if (action.ActionType == AIActionType.Move)     bonus -= 8f;
                    if (action.ActionType == AIActionType.Surround) bonus -= 8f;
                    if (action.ActionType == AIActionType.Retreat)  bonus -= 5f;
                    break;
                }

                case TurnStrategy.Balanced:
                {
                    int coreEconCount = CalcCoreEconomyCount(board);
                    bool econEstablished = coreEconCount >= 5;

                    if (action.ActionType == AIActionType.Summon)
                    {
                        bool hasBakery = board.GetBuildingCount(FacilityKind.Bakery) > 0;
                        bonus += (econEstablished && hasBakery) ? 20f : -30f;
                    }
                    if (action.ActionType == AIActionType.Build)
                    {
                        // 経済未成熟時は建築を強く推奨
                        if (!econEstablished)
                        {
                            bonus += 35f;
                            if (IsMissingCoreFacility(action.Facility, board))
                                bonus += 25f;
                            // 加工施設不足ボーナス
                            if (IsProcessingFacility(action.Facility) &&
                                board.GetBuildingCount(action.Facility) == 0)
                                bonus += 20f;
                        }
                        else
                        {
                            bonus += 8f;
                        }

                        // ★ 生産チェーン逆算ボーナス（Balancedでも適用）
                        var deficits = board.DiagnoseProductionChainDeficit();
                        for (int i = 0; i < deficits.Count; i++)
                        {
                            if (deficits[i] == action.Facility)
                            {
                                bonus += Mathf.Max(5f, 35f - i * 8f);
                                break;
                            }
                        }
                    }
                    if (action.ActionType == AIActionType.Attack)   bonus += 8f;
                    if (action.ActionType == AIActionType.SkillUse) bonus += 5f;
                    if (action.ActionType == AIActionType.Move)
                    {
                        // 経済未成熟時は移動ボーナスを抑制
                        float moveBonus = GetApproachToEnemy(action, board) * 3f;
                        if (!econEstablished) moveBonus *= 0.5f;
                        bonus += moveBonus;
                    }
                    break;
                }

                case TurnStrategy.ScoutSearch:
                {
                    // 索敵戦略: 偵察・未探索展開を最優先
                    if (action.ActionType == AIActionType.Move && action.Unit != null)
                    {
                        int newCells = board.EstimateNewVisionCells(action.TargetPos);
                        if (newCells > 0)
                            bonus += Mathf.Min(newCells * 4f, 35f);

                        // Scoutは特に強い索敵ボーナス
                        if (action.Unit.kind == Kind.Scout)
                            bonus += 20f;

                        // 味方連携維持
                        float allyDist = GetNearestAllyDist(action.TargetPos, action.Unit, board);
                        if (allyDist >= 2f && allyDist <= 5f)
                            bonus += 8f;
                        else if (allyDist > 7f)
                            bonus -= 10f;
                    }
                    // 索敵中は攻撃・スキルが発生したら優先（見つけた敵を逃さない）
                    if (action.ActionType == AIActionType.Attack) bonus += 12f;
                    if (action.ActionType == AIActionType.SkillUse && action.Skill != null && action.Skill.Multiplier > 0) bonus += 10f;
                    // 建築は索敵中でも維持（経済が弱い時はむしろ推奨）
                    if (action.ActionType == AIActionType.Build)
                    {
                        int econCount = CalcCoreEconomyCount(board);
                        bonus += econCount < 5 ? 10f : -5f;
                    }
                    // Waitを強く減点
                    if (action.ActionType == AIActionType.Wait) bonus -= 15f;
                    break;
                }

                case TurnStrategy.ContactEngage:
                {
                    // 初接敵戦略: 攻撃・スキル・交戦前進を最優先
                    if (action.ActionType == AIActionType.Attack) bonus += 25f;
                    if (action.ActionType == AIActionType.SkillUse && action.Skill != null)
                    {
                        if (action.Skill.Multiplier > 0)
                            bonus += 22f; // 攻撃スキル
                        // 範囲攻撃で複数巻き込み
                        if (action.AreaTargets != null && action.AreaTargets.Count > 1)
                            bonus += action.AreaTargets.Count * 8f;
                    }
                    if (action.ActionType == AIActionType.Move)
                    {
                        // 次ターン攻撃可能になる位置を強く加点
                        float approach = GetApproachToEnemy(action, board);
                        bonus += approach * 6f;

                        // 次ターン攻撃圏内に入れる位置を高評価
                        float nearestEnemy = GetNearestPlayerDist(action.TargetPos, board);
                        if (nearestEnemy <= 2f)
                            bonus += 15f;
                        else if (nearestEnemy <= 3.5f)
                            bonus += 8f;
                    }
                    if (action.ActionType == AIActionType.Surround) bonus += 18f;
                    // Waitを非常に強く減点
                    if (action.ActionType == AIActionType.Wait) bonus -= 25f;
                    if (action.ActionType == AIActionType.Retreat) bonus -= 12f;
                    if (action.ActionType == AIActionType.Build) bonus -= 10f;
                    break;
                }
            }
            action.Score += bonus;

            // ================================================================
            //  ★★ 30ターン以降: 戦略に関係なく建築を超優先
            //  経済が未成熟なまま30ターン経過=深刻な問題
            //  移動アクションを大幅に減点し、建築が確実に選ばれるようにする
            // ================================================================
            if (board.TurnCount >= TurnLateBuildBoost)
            {
                int coreEcon = CalcCoreEconomyCount(board);
                bool econWeak = coreEcon < 5;

                if (econWeak)
                {
                    // 建築系アクションは大幅加点
                    if (action.ActionType == AIActionType.Build)
                        action.Score += 100f;
                    if (action.ActionType == AIActionType.SubCrystal)
                        action.Score += 60f;

                    // 移動系アクションは大幅減点（建築にAPを回す）
                    if (action.ActionType == AIActionType.Move
                        || action.ActionType == AIActionType.Support
                        || action.ActionType == AIActionType.Surround)
                        action.Score -= 50f;
                    if (action.ActionType == AIActionType.Wait)
                        action.Score -= 100f;
                }
                else
                {
                    // 経済は充足しているが、上位施設がない場合は建築推奨
                    int proc = CalcProcessingFacilityCount(board);
                    if (proc < 3 && action.ActionType == AIActionType.Build)
                        action.Score += 50f;
                }
            }
        }
    }

    // ================================================================
    //  次ターン反撃圏ペナルティ
    //  行動後の位置でプレイヤーに狙われやすいかを評価し減点
    //  中核ユニット・ヒーラー・BOSS護衛・召喚直後は死亡リスクを重く見る
    // ================================================================
    static void ApplyCounterDangerPenalty(List<AIAction> actions, AIBoardState board, AIPersonality personality)
    {
        foreach (var action in actions)
        {
            // 移動系・攻撃後にその場に残る行動が対象
            if (action.Unit == null) continue;
            if (action.ActionType != AIActionType.Move
                && action.ActionType != AIActionType.Surround
                && action.ActionType != AIActionType.Support
                && action.ActionType != AIActionType.Attack
                && action.ActionType != AIActionType.SkillUse) continue;

            // 行動後の位置を推定
            Vector3 posAfter;
            if (action.ActionType == AIActionType.Move
                || action.ActionType == AIActionType.Surround
                || action.ActionType == AIActionType.Support)
            {
                posAfter = action.TargetPos;
            }
            else
            {
                posAfter = action.Unit.transform.position; // 攻撃/スキルは移動しない
            }

            int counterDmg = board.EstimateCounterDamageAt(posAfter, action.Unit);
            if (counterDmg <= 0) continue;

            float hpRatio = action.Unit.MaxHP > 0 ? (float)action.Unit.HP / action.Unit.MaxHP : 1f;

            // 反撃ダメで死ぬ場合は大きなペナルティ
            bool wouldDie = counterDmg >= action.Unit.HP;

            // 重要度重み: BOSS・ヒーラー・Priest・King は死亡リスクを重く見る
            float importanceMult = 1f;
            if (action.Unit.IsBoss) importanceMult = 2.0f;
            else if (action.Unit.kind == Kind.King) importanceMult = 2.5f;
            else if (action.Unit.kind == Kind.Priest) importanceMult = 1.8f;
            // BOSS近接護衛（BOSSの近くにいるGuardian/Knight）
            else if (personality.HasBoss && (action.Unit.kind == Kind.Guardian || action.Unit.kind == Kind.Knight))
            {
                float bossDist = Vector3.Distance(posAfter, personality.BossUnit.transform.position);
                if (bossDist < 3f) importanceMult = 1.5f;
            }

            // 孤立度: 周りに味方がいないと危険度UP
            int alliesNear = board.CountAlliesNear(posAfter, action.Unit, 3f);
            float isolationMult = alliesNear == 0 ? 1.5f : alliesNear == 1 ? 1.2f : 1f;

            // 退路安全性: 退路が塞がれている場合はさらに危険
            float retreatSafety = EvalRetreatPathSafety(posAfter, action.Unit, board);
            if (retreatSafety < -5f) isolationMult *= 1.3f; // 退路なし→危険度UP

            float penalty;
            if (wouldDie)
            {
                // 確殺されるなら大ペナルティ（ただし攻撃で相手を確殺する場合は軽減）
                penalty = 35f * importanceMult * isolationMult;
                if (action.ActionType == AIActionType.Attack && action.TargetUnit != null)
                {
                    int myDmg = EstimateDamage(action.Unit, action.TargetUnit);
                    if (myDmg >= action.TargetUnit.HP)
                        penalty *= 0.3f; // 相打ちなら許容
                }
            }
            else
            {
                // 死なないが痛い
                float dmgRatio = (float)counterDmg / Mathf.Max(1, action.Unit.HP);
                penalty = dmgRatio * 20f * importanceMult * isolationMult;
                // HP低い駒がさらにダメージを受ける場合は追加
                if (hpRatio < 0.4f) penalty += 10f * importanceMult;
            }

            action.Score -= penalty;
        }
    }

    // ================================================================
    //  撤退→再編チェーンボーナス（強化版）
    //  撤退先で: ヒーラー圏内 / 壁の後ろ / 味方がカバー / 次ターン反撃位置
    // ================================================================
    static void ApplyRetreatRegroupBonus(List<AIAction> actions, AIPersonality p, AIBoardState board)
    {
        float chainMultiplier = 1f;
        if (p.ShouldApplyMajorBonus && p.Major == MajorPersonality.Intellect)
            chainMultiplier = 1.5f;

        foreach (var action in actions)
        {
            if (action.ActionType != AIActionType.Retreat && action.ActionType != AIActionType.DefenseRepos)
                continue;
            if (action.Unit == null) continue;

            float bonus = 0f;

            // 1. ヒーラー範囲内に下がれる
            if (board.HasHealerInRange(action.TargetPos, 4f))
                bonus += 12f;

            // 2. 壁/防衛建築の後ろに下がれる
            if (board.HasDefensiveStructureNear(action.TargetPos, 3f))
                bonus += 8f;

            // 3. 味方がカバーできる位置（味方が2体以上近くにいる）
            int alliesNear = board.CountAlliesNear(action.TargetPos, action.Unit, 3f);
            if (alliesNear >= 2)
                bonus += 10f;
            else if (alliesNear >= 1)
                bonus += 5f;

            // 4. 撤退先から次ターン反撃可能（近くに敵がいて反撃圏に入る）
            float nearestPlayerDist = GetNearestPlayerDist(action.TargetPos, board);
            if (nearestPlayerDist >= 2f && nearestPlayerDist <= 4f)
                bonus += 6f; // 適度な距離=次ターン攻撃可能

            // 5. 撤退先で反撃を受けにくい
            int counterDmg = board.EstimateCounterDamageAt(action.TargetPos, action.Unit);
            if (counterDmg == 0)
                bonus += 8f;
            else if (counterDmg < action.Unit.HP * 0.2f)
                bonus += 4f;

            // 6. 退路の安全性 — 撤退先からさらに逃げ道があるか
            float retreatPathSafety = EvalRetreatPathSafety(action.TargetPos, action.Unit, board);
            bonus += retreatPathSafety;

            action.Score += bonus * chainMultiplier;
        }
    }

    // ================================================================
    //  BOSS前線参加条件
    //  ただ前に出るだけでなく、価値がある時だけ前進させる
    // ================================================================
    static void ApplyBossFrontlineConditions(List<AIAction> actions, AIPersonality p, AIBoardState board)
    {
        if (!p.HasBoss) return;
        var boss = p.BossUnit;

        // 敵が見えない場合は前進条件を緩和（展開期に引き籠り防止）
        bool noVisibleEnemies = board.AlivePlayerUnits.Count == 0;

        foreach (var action in actions)
        {
            if (action.Unit != boss) continue;
            if (action.ActionType != AIActionType.Move && action.ActionType != AIActionType.Surround) continue;

            float approach = GetApproachToEnemy(action, board);
            if (approach <= 0) continue; // 前進していないなら条件不要

            // 敵が見えない場合は自由に前進可能（味方に追従して展開する）
            if (noVisibleEnemies) continue;

            // 前進条件を評価
            float conditionScore = 0f;
            int conditionsMet = 0;

            // 条件1: 周囲に護衛がいる（2体以上）
            int escortsNear = board.CountAlliesNear(action.TargetPos, boss, 3f);
            if (escortsNear >= 2) { conditionScore += 8f; conditionsMet++; }

            // 条件2: 前線の穴を埋められる（クリスタル方向に味方がいない空白地帯）
            float nearestAllyOnFrontline = float.MaxValue;
            foreach (var u in board.AliveEnemyUnits)
            {
                if (u == null || u == boss || !u.gameObject.activeInHierarchy) continue;
                float d = Vector3.Distance(action.TargetPos, u.transform.position);
                if (d < nearestAllyOnFrontline) nearestAllyOnFrontline = d;
            }
            if (nearestAllyOnFrontline > 4f) // 穴がある
            { conditionScore += 6f; conditionsMet++; }

            // 条件3: 確殺ラインを作れる（近くの敵を次ターン確殺できる）
            foreach (var pu in board.AlivePlayerUnits)
            {
                if (pu == null || !pu.gameObject.activeInHierarchy) continue;
                float dist = Vector3.Distance(action.TargetPos, pu.transform.position);
                if (dist < 2.5f) // 攻撃圏内
                {
                    int dmg = EstimateDamage(boss, pu);
                    if (dmg >= pu.HP) { conditionScore += 12f; conditionsMet++; break; }
                }
            }

            // 条件4: 次ターン下がれる（後ろに移動先がある＝安全マス）
            int counterDmg = board.EstimateCounterDamageAt(action.TargetPos, boss);
            if (counterDmg < boss.HP * 0.3f) { conditionScore += 5f; conditionsMet++; }

            // 条件5: 自分が出ることで2体以上の指揮影響が上がる
            int influencedCount = 0;
            foreach (var u in board.AliveEnemyUnits)
            {
                if (u == null || u == boss || !u.gameObject.activeInHierarchy) continue;
                float distBefore = Vector3.Distance(u.transform.position, boss.transform.position);
                float distAfter = Vector3.Distance(u.transform.position, action.TargetPos);
                if (distAfter < distBefore && distAfter < 10f) influencedCount++;
            }
            if (influencedCount >= 2) { conditionScore += 8f; conditionsMet++; }

            // 条件が不十分ならペナルティ（出る時は強い、出ない時は徹底して出ない）
            if (conditionsMet < 2)
            {
                // 条件不足: 前進を大きく減点
                action.Score -= approach * 15f;
            }
            else
            {
                // 条件十分: 前進を加点
                action.Score += conditionScore;
            }
        }
    }

    // ================================================================
    //  経済余裕による段階的軍拡
    //  資源に余裕が出てきて維持費も払えるなら少しずつ駒を増やす
    // ================================================================
    static void ApplyGradualArmyExpansion(List<AIAction> actions, AIBoardState board)
    {
        int allyCount = board.AliveEnemyUnits.Count;
        float surplus = board.GetEconomicSurplus();

        // 軍が極端に少ない場合は経済余裕に関係なく軍拡を検討
        bool desperateForUnits = allyCount <= 3 && board.TurnCount > 5;
        if (surplus < 0.15f && !desperateForUnits) return;

        // 維持費を払える余裕度に応じて召喚ボーナス
        float expansionBonus = 0f;
        if (desperateForUnits)
            expansionBonus = 20f; // 駒が少なすぎる → 最優先
        else if (surplus > 0.7f)
            expansionBonus = 15f; // 資源潤沢
        else if (surplus > 0.5f)
            expansionBonus = 10f; // まあまあ
        else if (surplus > 0.3f)
            expansionBonus = 8f;
        else
            expansionBonus = 5f;  // 最低限

        // 軍が充実している場合は軍拡を控える
        if (allyCount >= 8) expansionBonus *= 0.3f;
        else if (allyCount >= 6) expansionBonus *= 0.6f;

        foreach (var action in actions)
        {
            if (action.ActionType != AIActionType.Summon) continue;
            action.Score += expansionBonus;

            // 安い駒を優先（維持費が少ない = 長期的に負担が少ない）
            if (action.SummonKind == Kind.Knight || action.SummonKind == Kind.Archer || action.SummonKind == Kind.Scout)
                action.Score += 5f; // 低コスト駒は維持しやすい
        }
    }

    // ================================================================
    //  スコア計算
    // ================================================================
    static float CalcScore(AIAction action, AIPersonality p, AIBoardState board, AILearning learning)
    {
        float baseScore = CalcBaseScore(action, board);
        float majorBonus = CalcMajorBonus(action, p, board);
        float traitBonus = CalcTraitBonus(action, p, board);
        float situationBonus = CalcSituationBonus(action, p, board);
        float learnBonus = learning != null ? learning.GetBonus(action, board) : 0f;

        return baseScore + majorBonus + traitBonus + situationBonus + learnBonus;
    }

    // ---- 基本評価 ----
    static float CalcBaseScore(AIAction action, AIBoardState board)
    {
        switch (action.ActionType)
        {
            case AIActionType.Attack:
                return CalcAttackBaseScore(action, board);
            case AIActionType.Move:
                return CalcMoveBaseScore(action, board);
            case AIActionType.SkillUse:
                return CalcSkillBaseScore(action, board);
            case AIActionType.Retreat:
                return CalcRetreatBaseScore(action, board);
            case AIActionType.Support:
                return CalcSupportBaseScore(action, board);
            case AIActionType.Surround:
                return CalcSurroundBaseScore(action, board);
            case AIActionType.DefenseRepos:
                return CalcDefenseReposBaseScore(action, board);
            case AIActionType.Build:
                return CalcBuildBaseScore(action, board);
            case AIActionType.Summon:
                return CalcSummonBaseScore(action, board);
            case AIActionType.SubCrystal:
                return CalcSubCrystalBaseScore(action, board);
            case AIActionType.Wait:
                return 1f;
            default:
                return 5f;
        }
    }

    static float CalcAttackBaseScore(AIAction action, AIBoardState board)
    {
        if (action.TargetUnit == null) return 0f;

        float score = 30f;

        int expectedDmg = EstimateDamage(action.Unit, action.TargetUnit);
        if (expectedDmg >= action.TargetUnit.HP)
            score += 40f;

        if (action.TargetUnit.MaxHP > 0)
        {
            float hpRatio = (float)action.TargetUnit.HP / action.TargetUnit.MaxHP;
            score += (1f - hpRatio) * 15f;
        }

        if (action.TargetUnit.kind == Kind.Crystal)
            score += 50f;
        if (action.TargetUnit.kind == Kind.King)
            score += 35f;

        if (action.TargetUnit.ShieldTurns > 0)
            score -= 30f;

        return score;
    }

    static float CalcMoveBaseScore(AIAction action, AIBoardState board)
    {
        float score = 10f;

        Vector3 unitPos = action.Unit.transform.position;
        Vector3 dest = action.TargetPos;

        // ★ 視界制限: Playerクリスタルへの接近加点は視認済みの場合のみ
        if (board.CanUsePlayerCrystalAsTarget())
        {
            float distBefore = Vector3.Distance(unitPos, board.PlayerCrystalPos);
            float distAfter = Vector3.Distance(dest, board.PlayerCrystalPos);
            float approach = distBefore - distAfter;
            score += approach * 3f;
        }
        else
        {
            // 未視認時: Last Known Position があればそちらへ向かう（信頼度減衰付き）
            var lkCrystal = board.GetLastKnownPlayerCrystal();
            if (lkCrystal.Valid)
            {
                int age = board.TurnCount - lkCrystal.Turn;
                float reliability = Mathf.Clamp01(1f - age * 0.15f);
                if (reliability > 0.1f)
                {
                    Vector3 lkPos = new Vector3(lkCrystal.Position.x, 0, lkCrystal.Position.z);
                    float distBefore = Vector3.Distance(unitPos, lkPos);
                    float distAfter = Vector3.Distance(dest, lkPos);
                    float approach = distBefore - distAfter;
                    score += approach * 1.5f * reliability; // 減衰した加点
                }
            }
            // Last Known Player位置への接近（痕跡情報）
            var lkPositions = board.GetLastKnownPlayerPositions();
            foreach (var (pos, reliability) in lkPositions)
            {
                Vector3 lkPos = new Vector3(pos.x, 0, pos.z);
                float distBefore = Vector3.Distance(unitPos, lkPos);
                float distAfter = Vector3.Distance(dest, lkPos);
                float approach = distBefore - distAfter;
                if (approach > 0)
                    score += approach * 1f * reliability;
            }

            // 未探索方向への展開ボーナス
            Vector3 unexploredDir = board.GetUnexploredDirection();
            float dotProduct = Vector3.Dot((dest - unitPos).normalized, unexploredDir);
            if (dotProduct > 0.3f)
                score += dotProduct * 5f;
        }

        // 視界内の敵に近づく加点（視認済みの敵だけ）
        float nearestPlayerDist = GetNearestPlayerDist(dest, board);
        if (nearestPlayerDist < 3f)
            score += 5f;

        // 次ターン攻撃可能位置を優先（交戦開始時）
        if (board.AlivePlayerUnits.Count > 0)
        {
            // 次ターンに攻撃圏内に入れる位置を高評価
            foreach (var pu in board.AlivePlayerUnits)
            {
                if (pu == null || !pu.gameObject.activeInHierarchy) continue;
                float dist = Vector3.Distance(dest, pu.transform.position);
                if (dist <= 2f)
                    score += 8f; // 攻撃圏内に入れる
                else if (dist <= 3.5f)
                    score += 4f; // 次ターンで届く距離
            }
        }

        if (dest.y > unitPos.y)
            score += 2f;

        // 偵察ボーナス: 未探索エリアへの移動を高評価
        int newVisionCells = board.EstimateNewVisionCells(dest);

        if (action.Unit.kind == Kind.Scout)
        {
            // Scout専用: 偵察に特化した強い加点
            if (newVisionCells > 0)
                score += Mathf.Min(newVisionCells * 3f, 30f);
            float explorationRatio = board.GetExplorationRatio();
            if (explorationRatio < 0.5f)
                score += (1f - explorationRatio) * 15f;
        }
        else if (board.AlivePlayerUnits.Count == 0)
        {
            // 敵が見えない時: 全ユニットに探索ドライブを付与
            if (newVisionCells > 0)
                score += Mathf.Min(newVisionCells * 2f, 20f);

            // 探索率が低い場合、前方への展開を強化
            float explorationRatio = board.GetExplorationRatio();
            if (explorationRatio < 0.6f)
                score += (1f - explorationRatio) * 10f;

            // 味方と離れすぎない移動を優先（展開しつつ連携維持）
            float allyDist = board.GetNearestAllyDist(dest, action.Unit);
            if (allyDist > 6f)
                score -= 8f; // 孤立しすぎ
            else if (allyDist >= 2f && allyDist <= 4f)
                score += 5f; // 適度な距離感
        }

        return score;
    }

    static float CalcSkillBaseScore(AIAction action, AIBoardState board)
    {
        if (action.Skill == null) return 0f;
        float score = 20f; // スキル使用の基本価値（通常攻撃より少し低め→APコスト高い分）

        var skill = action.Skill;

        // 攻撃スキル
        if (skill.Multiplier > 0 && action.TargetUnit != null)
        {
            int expectedDmg = SkillSystem.CalcSkillDamage(action.Unit, action.TargetUnit, skill);
            if (expectedDmg >= action.TargetUnit.HP)
                score += 45f; // 確殺ボーナス

            if (action.TargetUnit.kind == Kind.Crystal)
                score += 40f;
            if (action.TargetUnit.kind == Kind.King)
                score += 30f;

            if (action.TargetUnit.ShieldTurns > 0)
                score -= 25f;

            // 範囲スキルで複数ヒット
            if (action.AreaTargets != null && action.AreaTargets.Count > 1)
                score += action.AreaTargets.Count * 12f;

            // 高倍率スキル
            score += skill.Multiplier * 10f;
        }

        // 回復スキル
        if (skill.FixedHeal > 0 && action.TargetUnit != null)
        {
            float hpRatio = action.TargetUnit.MaxHP > 0
                ? (float)action.TargetUnit.HP / action.TargetUnit.MaxHP : 1f;
            score += (1f - hpRatio) * 35f; // HP低いほど価値が高い
            if (hpRatio < 0.3f)
                score += 15f; // 瀕死ボーナス
        }

        // バフスキル
        if (skill.GrantBuff != BuffType.None)
        {
            score += 12f;
            if (skill.GrantBuff == BuffType.Haste)
                score += 8f; // AP回復は非常に価値が高い

            // 自己バフは敵が視界内にいない場合、大幅に減点（無駄なAP消費を防止）
            if (skill.Target == SkillTarget.Self || skill.Target == SkillTarget.SelfArea)
            {
                if (board.AlivePlayerUnits.Count == 0)
                    score -= 30f; // 敵不在時は自己バフの価値を大幅に下げる
                else
                {
                    // 敵がいても遠い場合は減点
                    float nearestEnemy = float.MaxValue;
                    foreach (var pu in board.AlivePlayerUnits)
                    {
                        if (pu == null || !pu.gameObject.activeInHierarchy) continue;
                        float d = Vector3.Distance(action.Unit.transform.position, pu.transform.position);
                        if (d < nearestEnemy) nearestEnemy = d;
                    }
                    if (nearestEnemy > 8f)
                        score -= 15f; // 敵が遠い場合もバフの価値は低い
                }
            }
        }

        // デバフ付き
        if (skill.InflictDebuff != StatusEffectType.None)
        {
            score += 8f;
            if (skill.InflictDebuff == StatusEffectType.Stun)
                score += 12f;
            if (skill.InflictDebuff == StatusEffectType.Freeze)
                score += 10f;
        }

        // APコスト効率（高APスキルは少し減点）
        score -= (skill.APCost - 4) * 1.5f;

        return score;
    }

    static float CalcRetreatBaseScore(AIAction action, AIBoardState board)
    {
        float score = 8f;
        if (action.Unit == null) return score;

        float hpRatio = action.Unit.MaxHP > 0 ? (float)action.Unit.HP / action.Unit.MaxHP : 1f;
        // HP低いほど撤退価値UP
        score += (1f - hpRatio) * 25f;
        if (hpRatio < 0.2f) score += 15f; // 瀕死

        // 自陣に近づくほど加点
        float dist = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
        if (dist < 5f) score += 8f;

        return score;
    }

    static float CalcSupportBaseScore(AIAction action, AIBoardState board)
    {
        float score = 15f;
        if (action.TargetUnit == null) return score;

        // 援護対象のHP低いほど価値UP
        float targetHpRatio = action.TargetUnit.MaxHP > 0
            ? (float)action.TargetUnit.HP / action.TargetUnit.MaxHP : 1f;
        score += (1f - targetHpRatio) * 20f;

        // 重要駒の援護は価値が高い
        if (action.TargetUnit.kind == Kind.King) score += 10f;

        return score;
    }

    static float CalcSurroundBaseScore(AIAction action, AIBoardState board)
    {
        float score = 18f;
        if (action.TargetUnit == null) return score;

        // 包囲対象のHP低いほど確殺チャンスで価値UP
        float hpRatio = action.TargetUnit.MaxHP > 0
            ? (float)action.TargetUnit.HP / action.TargetUnit.MaxHP : 1f;
        score += (1f - hpRatio) * 15f;

        // 重要駒の包囲は価値が高い
        if (action.TargetUnit.kind == Kind.Crystal) score += 25f;
        if (action.TargetUnit.kind == Kind.King) score += 15f;

        return score;
    }

    static float CalcDefenseReposBaseScore(AIAction action, AIBoardState board)
    {
        float score = 12f;
        if (action.Unit == null) return score;

        // クリスタルに近づくほど加点
        float dist = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
        if (dist < 3f) score += 15f;
        else if (dist < 5f) score += 8f;

        // クリスタルが危険なほど加点
        if (board.EnemyCrystalHP < board.EnemyCrystalMaxHP * 0.3f)
            score += 20f;
        else if (board.EnemyCrystalHP < board.EnemyCrystalMaxHP * 0.5f)
            score += 10f;

        return score;
    }

    static float CalcSubCrystalBaseScore(AIAction action, AIBoardState board)
    {
        float score = 22f; // サブクリ展開は領地拡張の高い価値

        // 敵クリスタルから離れすぎていない場所が良い
        float distFromHome = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
        if (distFromHome < 8f) score += 10f;

        // ★ 前線に近い場所は加点（視認済みの場合のみ）
        if (board.CanUsePlayerCrystalAsTarget())
        {
            float distToEnemy = Vector3.Distance(action.TargetPos, board.PlayerCrystalPos);
            score += Mathf.Max(0, 15f - distToEnemy);
        }

        return score;
    }

    static float CalcBuildBaseScore(AIAction action, AIBoardState board)
    {
        float score = 15f; // 建築の基本価値
        var facility = action.Facility;
        int turn = board.TurnCount;

        // フェーズ別スコアは PhaseScore(turn, early, mid, late) で計算

        // ================================================================
        //  生産チェーン認識: 不足資源を生産する建物を高評価
        // ================================================================
        float scarcityBonus = CalcScarcityBonus(facility, board);
        score += scarcityBonus;

        // ================================================================
        //  建物種別ごとのフェーズ対応スコア
        // ================================================================
        switch (facility)
        {
            // --- 基礎資源（原料生産） ---
            case FacilityKind.Well:
                score += PhaseScore(turn, 40f, 12f, 5f);
                if (board.GetBuildingCount(FacilityKind.Well) == 0)
                {
                    score += 40f;
                    if (board.EnemyResources != null)
                        score += ResourceEmergencyBonus(board.EnemyResources.Water, 50f, 30f, 15f);
                }
                break;

            case FacilityKind.LoggingCamp:
                score += PhaseScore(turn, 35f, 12f, 5f);
                if (board.GetBuildingCount(FacilityKind.LoggingCamp) == 0)
                {
                    score += 40f;
                    if (board.EnemyResources != null)
                        score += ResourceEmergencyBonus(board.EnemyResources.Wood, 50f, 30f, 15f);
                }
                break;

            case FacilityKind.Quarry:
                score += PhaseScore(turn, 30f, 12f, 5f);
                if (board.GetBuildingCount(FacilityKind.Quarry) == 0)
                {
                    score += 30f;
                    if (board.EnemyResources != null)
                        score += ResourceEmergencyBonus(board.EnemyResources.Stone, 40f, 20f, 10f);
                }
                break;

            case FacilityKind.Field:
                score += PhaseScore(turn, 32f, 14f, 5f);
                if (board.GetBuildingCount(FacilityKind.Field) == 0) score += 25f;
                if (board.EnemyResources != null && board.EnemyResources.Bread <= 10)
                    score += 20f;
                break;

            case FacilityKind.Mine:
                score += PhaseScore(turn, 20f, 22f, 12f);
                if (turn >= TurnProductionBoost) score += ProductionBoostScore;
                if (board.GetBuildingCount(FacilityKind.Mine) == 0) score += 25f;
                if (board.EnemyResources != null)
                {
                    if (board.EnemyResources.Iron <= 5) score += 18f;
                    if (board.EnemyResources.MagicOre <= 5) score += 12f;
                }
                break;

            // --- 加工施設 ---
            case FacilityKind.LumberMill:
                score += PhaseScore(turn, 25f, 22f, 10f);
                if (turn >= TurnProductionBoost) score += ProductionBoostScore;
                if (board.GetBuildingCount(FacilityKind.LumberMill) == 0 &&
                    board.GetBuildingCount(FacilityKind.LoggingCamp) > 0) score += 35f;
                if (board.EnemyResources != null && board.EnemyResources.Plank < 10)
                    score += 20f;
                break;

            case FacilityKind.StoneWorks:
                score += PhaseScore(turn, 25f, 22f, 10f);
                if (turn >= TurnProductionBoost) score += ProductionBoostScore;
                if (board.GetBuildingCount(FacilityKind.StoneWorks) == 0 &&
                    board.GetBuildingCount(FacilityKind.Quarry) > 0) score += 35f;
                if (board.EnemyResources != null && board.EnemyResources.CutStone < 10)
                    score += 20f;
                break;

            case FacilityKind.Bakery:
                score += PhaseScore(turn, 35f, 25f, 12f);
                if (board.GetBuildingCount(FacilityKind.Bakery) == 0 &&
                    board.GetBuildingCount(FacilityKind.Field) > 0) score += 40f;
                if (board.EnemyResources != null && board.EnemyResources.Bread < 20)
                    score += 25f;
                break;

            case FacilityKind.Smelter:
                score += PhaseScore(turn, 20f, 22f, 14f);
                if (turn >= TurnProductionBoost) score += ProductionBoostScore;
                if (board.GetBuildingCount(FacilityKind.Smelter) == 0 &&
                    board.GetBuildingCount(FacilityKind.Mine) > 0) score += 30f;
                if (board.EnemyResources != null && board.EnemyResources.Iron <= 5)
                    score += 20f;
                break;

            // --- インフラ ---
            case FacilityKind.House:
                score += PhaseScore(turn, 35f, 25f, 15f);
                if (board.GetBuildingCount(FacilityKind.House) == 0)
                {
                    score += 40f;
                    if (board.EnemyResources != null && board.EnemyResources.Citizen <= 0)
                        score += 50f;
                }
                if (board.EnemyResources != null)
                {
                    if (board.EnemyResources.Citizen <= 0) score += 35f;
                    else if (board.EnemyResources.Citizen <= 2) score += 20f;
                }
                break;

            case FacilityKind.Warehouse:
                score += PhaseScore(turn, 2f, 15f, 12f);
                if (board.GetBuildingCount(FacilityKind.Warehouse) == 0 && turn >= 10)
                    score += 18f;
                break;

            case FacilityKind.Barracks:
                score += PhaseScore(turn, 3f, 15f, 20f);
                if (board.GetBuildingCount(FacilityKind.Barracks) == 0 && turn >= 12)
                    score += 20f;
                break;

            // --- 防衛建築 ---
            case FacilityKind.WoodWall:
            case FacilityKind.StoneWall:
                score += PhaseScore(turn, 3f, 8f, 15f);
                if (board.EnemyCrystalHP < board.EnemyCrystalMaxHP * 0.5f)
                    score += 20f;
                if (CalcCoreEconomyCount(board) < 4)
                    score -= 30f;
                break;

            // --- 攻撃建築 ---
            case FacilityKind.Mortar:
            case FacilityKind.Cannon:
                score += PhaseScore(turn, 2f, 10f, 18f);
                break;

            case FacilityKind.RestraintTrap:
            case FacilityKind.SpikeTrap:
                score += PhaseScore(turn, 3f, 10f, 15f);
                break;

            case FacilityKind.HeroSword:
                score += turn > TurnMidEnd ? 20f : 2f;
                break;
        }

        // サブクリスタル（領地拡張）
        if (FacilityData.IsSubCrystal(facility))
            score += 15f;

        // 2棟目以降は価値が大幅に下がる（指数的収穫逓減）
        int existingCount = board.GetBuildingCount(facility);
        if (existingCount > 0)
            score -= existingCount * existingCount * DuplicatePenaltyFactor;

        // 加工施設ボーナス: 原料備蓄過多 & 加工資源不足 → 加工施設を強く推奨
        score += CalcProcessingOverstockBonus(facility, board);

        // ★ 生産チェーン逆算ボーナス: 不足資源から必要施設を診断し加点
        var chainDeficits = board.DiagnoseProductionChainDeficit();
        for (int i = 0; i < chainDeficits.Count; i++)
        {
            if (chainDeficits[i] == facility)
            {
                score += Mathf.Max(5f, 30f - i * 6f);
                break;
            }
        }

        // ================================================================
        //  ★★ 30ターン以降: 全建築スコアを大幅ブースト
        //  経済が未成熟なまま30ターン経過 = 建築が全く機能していない
        //  移動よりも確実に建築が選ばれるように極めて高いスコアを付与
        // ================================================================
        if (turn >= TurnLateBuildBoost)
        {
            bool isMilitary = FacilityData.IsWall(facility) || FacilityData.IsOffensive(facility);

            // 経済施設はまだ0棟なら最優先
            if (existingCount == 0 && !isMilitary)
                score += LateBuildBoostScore + 80f; // +200相当
            else if (existingCount == 0)
                score += LateBuildBoostScore;        // 軍事施設も+120
            else
                score += LateBuildBoostScore * 0.5f; // 2棟目以降も+60

            // 特に不足している施設への追加ブースト
            switch (facility)
            {
                case FacilityKind.LumberMill:
                case FacilityKind.StoneWorks:
                case FacilityKind.Smelter:
                    if (existingCount == 0) score += 60f;
                    break;
                case FacilityKind.Mine:
                    if (existingCount == 0) score += 50f;
                    break;
                case FacilityKind.House:
                    if (board.EnemyResources != null && board.EnemyResources.Citizen <= 2)
                        score += 80f;
                    break;
                case FacilityKind.Warehouse:
                    if (existingCount == 0 && turn >= 35) score += 40f;
                    break;
                case FacilityKind.Barracks:
                    if (existingCount == 0 && turn >= 35) score += 50f;
                    break;
            }
        }

        return score;
    }

    /// <summary>原料が備蓄過多で加工資源が不足している場合のボーナス</summary>
    static float CalcProcessingOverstockBonus(FacilityKind facility, AIBoardState board)
    {
        if (board.EnemyResources == null) return 0f;
        var res = board.EnemyResources;

        switch (facility)
        {
            case FacilityKind.LumberMill:
                return (res.Wood > 200 && res.Plank < 20) ? 40f : 0f;
            case FacilityKind.StoneWorks:
                return (res.Stone > 200 && res.CutStone < 20) ? 40f : 0f;
            case FacilityKind.Smelter:
                return ((res.IronOre > 10 || res.Coal > 20) && res.Iron < 10) ? 35f : 0f;
            case FacilityKind.Bakery:
                return (res.Wheat > 30 && res.Bread < 20) ? 40f : 0f;
            default:
                return 0f;
        }
    }

    // ================================================================
    //  不足資源ボーナス: その建物が生産する資源が不足しているほど高評価
    // ================================================================
    static float CalcScarcityBonus(FacilityKind facility, AIBoardState board)
    {
        float bonus = 0f;
        switch (facility)
        {
            case FacilityKind.Well:
                bonus += board.GetResourceScarcity("Water") * 30f;
                // 井戸が無い場合、水が潤沢でも将来の枯渇を見越して加点
                if (board.GetBuildingCount(FacilityKind.Well) == 0)
                    bonus += 20f;
                break;
            case FacilityKind.LoggingCamp:
                bonus += board.GetResourceScarcity("Wood") * 30f;
                // 伐採所が無い場合、木材が潤沢でも将来の枯渇を見越して加点
                if (board.GetBuildingCount(FacilityKind.LoggingCamp) == 0)
                    bonus += 20f;
                break;
            case FacilityKind.Quarry:
                bonus += board.GetResourceScarcity("Stone") * 28f;
                bonus += board.GetResourceScarcity("Coal") * 10f;
                // 採石場が無い場合、石材が潤沢でも将来の枯渇を見越して加点
                if (board.GetBuildingCount(FacilityKind.Quarry) == 0)
                    bonus += 15f;
                break;
            case FacilityKind.Field:
                bonus += board.GetResourceScarcity("Wheat") * 15f;
                break;
            case FacilityKind.Mine:
                bonus += board.GetResourceScarcity("IronOre") * 18f;
                bonus += board.GetResourceScarcity("MagicOre") * 12f;
                break;
            case FacilityKind.LumberMill:
                bonus += board.GetResourceScarcity("Plank") * 18f;
                break;
            case FacilityKind.StoneWorks:
                bonus += board.GetResourceScarcity("CutStone") * 18f;
                break;
            case FacilityKind.Bakery:
                bonus += board.GetResourceScarcity("Bread") * 22f;
                break;
            case FacilityKind.Smelter:
                bonus += board.GetResourceScarcity("Iron") * 20f;
                break;
        }
        return bonus;
    }

    static float CalcSummonBaseScore(AIAction action, AIBoardState board)
    {
        float score = 30f; // 召喚の基本価値

        // 経済基盤の充実度で召喚の可否を判断
        // 原料施設（各1pt）+ 加工施設（各2pt）= 基盤スコア
        int rawCount = CalcRawFacilityCount(board);
        int procCount = CalcProcessingFacilityCount(board);
        float infraScore = rawCount + procCount * 2f;

        // 基盤スコアが足りないほど召喚を抑制し、建築（特にWell/Field/Bakery）を優先させる
        // Bakery(パン工房)が無い状態での召喚は特に危険（パン枯渇→市民減少の連鎖）
        bool hasBakery = board.GetBuildingCount(FacilityKind.Bakery) > 0;
        if (infraScore < 3f)
            score -= 120f; // 基盤なし → 召喚禁止レベル
        else if (infraScore < 5f)
            score -= 60f;  // 基盤不足 → 建築優先
        else if (!hasBakery)
            score -= 40f;  // Bakeryなし → パン供給が危険
        else if (infraScore < 7f)
            score += 0f;   // 基盤が整い始めた → 召喚解禁
        else
            score += 20f;  // 基盤十分 → 積極的に召喚

        // 自軍駒数が少ないほど召喚価値が上がる
        int allyCount = board.AliveEnemyUnits.Count;
        if (allyCount <= 2) score += 35f;
        else if (allyCount <= 4) score += 25f;
        else if (allyCount <= 6) score += 15f;
        else score += 5f;

        // ★ 前線に近い位置に配置するほど加点（視認済みの場合のみ）
        if (board.CanUsePlayerCrystalAsTarget())
        {
            float dist = Vector3.Distance(action.TargetPos, board.PlayerCrystalPos);
            score += Mathf.Max(0, 15f - dist);
        }

        // Scout召喚ボーナス: 探索率が低く敵が見えない場合、偵察要員として優先
        if (action.SummonKind == Kind.Scout && board.AlivePlayerUnits.Count == 0)
        {
            float explorationRatio = board.GetExplorationRatio();
            if (explorationRatio < 0.5f)
                score += 20f;
            else if (explorationRatio < 0.7f)
                score += 10f;
        }

        // 戦闘ユニット召喚ボーナス
        switch (action.SummonKind)
        {
            case Kind.Knight:  score += 12f; break;
            case Kind.Archer:  score += 10f; break;
            case Kind.Magic:   score += 8f; break;
            case Kind.Assassin: score += 6f; break;
            case Kind.Scout:   score += 5f; break;
        }

        return score;
    }

    // ---- 大きい性格補正 ----
    // 仕様: BOSSが生存している場合のみ適用
    // 通常駒はBOSSからの距離に応じて影響度が減衰する
    static float CalcMajorBonus(AIAction action, AIPersonality p, AIBoardState board)
    {
        if (!p.ShouldApplyMajorBonus) return 0f;

        // BOSSからの指揮影響度を計算（BOSS自身=1.0、遠い駒ほど低い）
        float influence = action.Unit != null ? p.GetCommandInfluence(action.Unit) : 0.5f;

        // BOSS自身の前線参加は性格によって制御
        // ただし敵が見えない場合は前進を制限しない（展開期に引き籠り防止）
        if (action.Unit != null && action.Unit.IsBoss)
        {
            if (action.ActionType == AIActionType.Move && board.AlivePlayerUnits.Count > 0)
            {
                float approach = GetApproachToEnemy(action, board);
                if (approach > 0 && p.BossFrontlineRate < 0.5f)
                    return -approach * (1f - p.BossFrontlineRate) * 8f; // 知性型BOSSは前に出にくい
            }
        }

        float bonus = 0f;

        switch (p.Major)
        {
            case MajorPersonality.Combat:
                if (action.ActionType == AIActionType.Attack)
                    bonus += 15f;
                if (action.ActionType == AIActionType.SkillUse && action.Skill != null && action.Skill.Multiplier > 0)
                    bonus += 12f; // 攻撃スキル好む
                if (action.ActionType == AIActionType.Move)
                {
                    float approach = GetApproachToEnemy(action, board);
                    bonus += approach * 5f;
                }
                if (action.ActionType == AIActionType.Surround)
                    bonus += 10f; // 包囲好む
                if (action.ActionType == AIActionType.Wait)
                    bonus -= 5f;
                if (action.ActionType == AIActionType.Retreat)
                    bonus -= 8f; // 撤退嫌い
                if (action.ActionType == AIActionType.Summon)
                    bonus += 8f;
                break;

            case MajorPersonality.Intellect:
                if (action.ActionType == AIActionType.Attack)
                    bonus += 5f;
                if (action.ActionType == AIActionType.SkillUse && action.Skill != null)
                {
                    // バフ・回復スキル好む
                    if (action.Skill.FixedHeal > 0 || action.Skill.GrantBuff != BuffType.None)
                        bonus += 12f;
                    else
                        bonus += 5f;
                }
                if (action.ActionType == AIActionType.Move)
                {
                    float allyDist = GetNearestAllyDist(action.TargetPos, action.Unit, board);
                    if (allyDist < 4f)
                        bonus += 8f;
                    float crystalDist = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
                    if (crystalDist > 10f)
                        bonus -= 5f;
                }
                if (action.ActionType == AIActionType.Support)
                    bonus += 10f; // 援護好む
                if (action.ActionType == AIActionType.Build)
                    bonus += 12f;
                if (action.ActionType == AIActionType.Retreat)
                    bonus += 5f; // 撤退に積極的
                break;

            case MajorPersonality.Adaptive:
                if (action.ActionType == AIActionType.Attack)
                    bonus += 8f;
                if (action.ActionType == AIActionType.SkillUse)
                    bonus += 6f;
                break;

            case MajorPersonality.Growth:
                if (action.ActionType == AIActionType.Attack)
                    bonus += 10f;
                if (action.ActionType == AIActionType.SkillUse)
                    bonus += 8f;
                // 成長型は経済建築を重視
                if (action.ActionType == AIActionType.Build)
                    bonus += 10f;
                if (action.ActionType == AIActionType.Summon)
                    bonus += 6f;
                break;
        }

        // 指揮影響度を乗算（BOSSから遠い駒ほど大きい性格の影響が弱まる）
        return bonus * influence;
    }

    // ---- 細かい性格補正 ----
    static float CalcTraitBonus(AIAction action, AIPersonality p, AIBoardState board)
    {
        float bonus = 0f;

        switch (action.ActionType)
        {
            case AIActionType.Attack:
                bonus += p.ObsessionRate * 20f;
                if (action.TargetUnit != null)
                {
                    int myDmg = EstimateDamage(action.Unit, action.TargetUnit);
                    int counterDmg = EstimateDamage(action.TargetUnit, action.Unit);
                    if (counterDmg > myDmg)
                        bonus -= p.CautionRate * 25f;
                }
                break;

            case AIActionType.SkillUse:
                if (action.Skill != null)
                {
                    // 攻撃スキル → 執着性が影響
                    if (action.Skill.Multiplier > 0)
                        bonus += p.ObsessionRate * 15f;
                    // 回復・バフスキル → 指揮性が影響
                    if (action.Skill.FixedHeal > 0 || action.Skill.GrantBuff != BuffType.None)
                        bonus += p.CommandRate * 12f;
                    // デバフスキル → 戦術性が影響
                    if (action.Skill.InflictDebuff != StatusEffectType.None)
                        bonus += p.TacticsRate * 15f;
                    // 自己バフ → 慎重性が影響（敵が視界内にいる場合のみ）
                    if (action.Skill.Target == SkillTarget.Self && action.Skill.GrantBuff != BuffType.None
                        && board.AlivePlayerUnits.Count > 0)
                        bonus += p.CautionRate * 8f;
                    // 範囲スキルで複数ヒット → 戦術性
                    if (action.AreaTargets != null && action.AreaTargets.Count > 1)
                        bonus += p.TacticsRate * (action.AreaTargets.Count * 5f);
                }
                break;

            case AIActionType.Move:
                bonus += CalcTacticalMoveBonus(action, p, board);
                float distFromBase = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
                if (distFromBase > 8f)
                {
                    // Scoutは偵察任務のため遠方ペナルティを軽減
                    float defPenalty = action.Unit.kind == Kind.Scout ? 5f : 15f;
                    bonus -= p.DefenseRate * defPenalty;
                }
                float allyDist = GetNearestAllyDist(action.TargetPos, action.Unit, board);
                if (allyDist < 3f)
                    bonus += p.CommandRate * 12f;
                else if (allyDist > 6f)
                    bonus -= p.CommandRate * 10f;
                float dangerDist = GetNearestPlayerDist(action.TargetPos, board);
                if (dangerDist < 2f)
                    bonus -= p.CautionRate * 10f;
                // Scout偵察は戦術性で強化（索敵は戦術の基礎）
                if (action.Unit.kind == Kind.Scout)
                {
                    int newCells = board.EstimateNewVisionCells(action.TargetPos);
                    if (newCells > 0)
                        bonus += p.TacticsRate * Mathf.Min(newCells * 2f, 20f);
                }
                break;

            case AIActionType.Retreat:
                bonus += p.CautionRate * 20f;
                bonus += p.DefenseRate * 10f;
                bonus -= p.ObsessionRate * 8f; // 執着性高いと撤退しにくい
                break;

            case AIActionType.Support:
                bonus += p.CommandRate * 18f;
                bonus += p.DefenseRate * 8f;
                break;

            case AIActionType.Surround:
                bonus += p.TacticsRate * 20f;
                bonus += p.ObsessionRate * 8f;
                break;

            case AIActionType.DefenseRepos:
                bonus += p.DefenseRate * 22f;
                bonus += p.CautionRate * 10f;
                bonus -= p.ObsessionRate * 5f; // 攻め気質だと防衛再配置しにくい
                break;

            case AIActionType.Build:
                bonus += p.DevelopRate * 20f;
                if (FacilityData.IsWall(action.Facility) || FacilityData.IsOffensive(action.Facility))
                    bonus += p.DefenseRate * 15f;
                // 慎重な性格は序盤の経済投資を重視
                if (board.TurnCount <= TurnEarlyEnd && !FacilityData.IsWall(action.Facility) && !FacilityData.IsOffensive(action.Facility))
                    bonus += p.CautionRate * 12f;
                break;

            case AIActionType.Summon:
                bonus += p.CommandRate * 15f;
                bonus += p.ObsessionRate * 5f;
                break;

            case AIActionType.Wait:
                bonus += p.CautionRate * 3f;
                bonus -= p.ObsessionRate * 5f;
                break;
        }

        if (action.ActionType == AIActionType.SubCrystal)
        {
            bonus += p.DevelopRate * 25f;
        }

        return bonus;
    }

    // ---- 局面補正 ----
    static float CalcSituationBonus(AIAction action, AIPersonality p, AIBoardState board)
    {
        float bonus = 0f;
        float advantageRatio = board.GetAdvantageRatio();

        if (p.Major == MajorPersonality.Adaptive)
        {
            if (advantageRatio > 0.2f)
            {
                // 有利時：攻撃・スキル攻撃・前進・包囲を強化
                if (action.ActionType == AIActionType.Attack)
                    bonus += 12f;
                if (action.ActionType == AIActionType.SkillUse && action.Skill != null && action.Skill.Multiplier > 0)
                    bonus += 10f;
                if (action.ActionType == AIActionType.Move)
                    bonus += GetApproachToEnemy(action, board) * 4f;
                if (action.ActionType == AIActionType.Surround)
                    bonus += 10f;
                if (action.ActionType == AIActionType.Summon)
                    bonus += 8f;
            }
            else if (advantageRatio < -0.2f)
            {
                // 不利時：撤退・援護・回復・建築を強化
                if (action.ActionType == AIActionType.Retreat)
                    bonus += 15f;
                if (action.ActionType == AIActionType.Support)
                    bonus += 12f;
                if (action.ActionType == AIActionType.SkillUse && action.Skill != null && action.Skill.FixedHeal > 0)
                    bonus += 12f;
                if (action.ActionType == AIActionType.Build)
                    bonus += 10f;
                if (action.ActionType == AIActionType.Move)
                {
                    float retreatValue = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
                    if (retreatValue < 5f)
                        bonus += 10f;
                }
            }
        }

        // クリスタル危機時は防衛優先
        if (board.EnemyCrystalHP < board.EnemyCrystalMaxHP * 0.5f)
        {
            if (action.ActionType == AIActionType.Move && action.Unit != null)
            {
                float crystalDist = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
                if (crystalDist < 3f)
                    bonus += 20f;
            }
            // 防衛再配置の価値UP
            if (action.ActionType == AIActionType.DefenseRepos)
                bonus += 18f;
            // 壁建築の価値UP
            if (action.ActionType == AIActionType.Build && FacilityData.IsWall(action.Facility))
                bonus += 15f;
        }

        // 駒が少ない時は召喚優先
        if (board.AliveEnemyUnits.Count <= 2)
        {
            if (action.ActionType == AIActionType.Summon)
                bonus += 15f;
        }

        // 経済逼迫時：敵不在の自己バフスキルにAPを浪費しない
        if (action.ActionType == AIActionType.SkillUse && action.Skill != null)
        {
            float surplus = board.GetEconomicSurplus();
            // 経済余剰が低い場合、非攻撃スキル（バフ・回復）のスコアを減点
            if (surplus < 0.3f && action.Skill.Multiplier <= 0 && board.AlivePlayerUnits.Count == 0)
            {
                bonus -= 20f; // 経済逼迫 + 敵不在 → スキル使用は無駄
            }
            // AP残量が少ない場合、高コストスキルを抑制
            if (board.EnemyAP <= action.Skill.APCost + 2 && action.Skill.Multiplier <= 0)
            {
                bonus -= 10f; // APギリギリで非攻撃スキルは避ける（移動や建築のAPを残す）
            }
        }

        // 経済状況に応じた建築ボーナス
        if (action.ActionType == AIActionType.Build)
        {
            bool isMilitary = FacilityData.IsWall(action.Facility) || FacilityData.IsOffensive(action.Facility);
            int basicProducers = board.GetBuildingCount(FacilityKind.Well)
                               + board.GetBuildingCount(FacilityKind.LoggingCamp)
                               + board.GetBuildingCount(FacilityKind.Quarry);

            if (basicProducers == 0 && !isMilitary)
                bonus += 25f;
            if (board.TurnCount <= 4 && isMilitary)
                bonus -= 15f;
        }

        return bonus;
    }

    // ================================================================
    //  ヘルパー
    // ================================================================
    static int EstimateDamage(Status attacker, Status defender)
    {
        int atk = attacker.ATK;
        int def = defender.DEF;
        return Mathf.Max(0, 1 + (atk / 6) + ((atk / 2) - (def / 4)));
    }

    static float GetApproachToEnemy(AIAction action, AIBoardState board)
    {
        if (action.Unit == null) return 0f;
        Vector3 from = action.Unit.transform.position;
        Vector3 to = action.TargetPos;

        // ★ 視界内の敵がいれば、最寄りの敵への接近度を使う
        if (board.AlivePlayerUnits.Count > 0)
        {
            float bestApproach = 0f;
            foreach (var pu in board.AlivePlayerUnits)
            {
                if (pu == null || !pu.gameObject.activeInHierarchy) continue;
                float dBefore = Vector3.Distance(from, pu.transform.position);
                float dAfter = Vector3.Distance(to, pu.transform.position);
                float a = dBefore - dAfter;
                if (a > bestApproach) bestApproach = a;
            }
            return bestApproach;
        }

        // ★ Playerクリスタル未視認時は直接接近を使わない
        if (!board.CanUsePlayerCrystalAsTarget())
        {
            // Last Known Position があれば軽い接近評価
            var lkCrystal = board.GetLastKnownPlayerCrystal();
            if (lkCrystal.Valid)
            {
                int age = board.TurnCount - lkCrystal.Turn;
                float reliability = Mathf.Clamp01(1f - age * 0.15f);
                if (reliability > 0.1f)
                {
                    Vector3 lkPos = new Vector3(lkCrystal.Position.x, 0, lkCrystal.Position.z);
                    float dBefore = Vector3.Distance(from, lkPos);
                    float dAfter = Vector3.Distance(to, lkPos);
                    return (dBefore - dAfter) * reliability * 0.5f;
                }
            }
            return 0f; // 情報なし = 接近評価なし
        }

        // Playerクリスタル視認済みの場合のみ従来のロジック
        float distBefore = Vector3.Distance(from, board.PlayerCrystalPos);
        float distAfter = Vector3.Distance(to, board.PlayerCrystalPos);
        return distBefore - distAfter;
    }

    static float GetNearestPlayerDist(Vector3 pos, AIBoardState board)
    {
        float nearest = float.MaxValue;
        foreach (var pu in board.AlivePlayerUnits)
        {
            if (pu == null || !pu.gameObject.activeInHierarchy) continue;
            float d = Vector3.Distance(pos, pu.transform.position);
            if (d < nearest) nearest = d;
        }
        return nearest;
    }

    static float GetNearestAllyDist(Vector3 pos, Status self, AIBoardState board)
    {
        float nearest = float.MaxValue;
        if (self == null) return nearest;
        foreach (var au in board.AliveEnemyUnits)
        {
            if (au == null || !au.gameObject.activeInHierarchy) continue;
            if (au == self) continue;
            float d = Vector3.Distance(pos, au.transform.position);
            if (d < nearest) nearest = d;
        }
        return nearest;
    }

    /// <summary>
    /// 退路安全性評価: 撤退先からさらに移動可能なマスのうち
    /// 敵の攻撃圏外に出られるマスがどれだけあるかを評価する。
    /// 退路が塞がれている（=袋小路）場合はペナルティを返す。
    /// </summary>
    static float EvalRetreatPathSafety(Vector3 retreatPos, Status unit, AIBoardState board)
    {
        // 撤退先から到達可能な隣接マス（4方向）を調べる
        Vector3[] directions = {
            new Vector3(1, 0, 0), new Vector3(-1, 0, 0),
            new Vector3(0, 0, 1), new Vector3(0, 0, -1)
        };

        int safePaths = 0;   // 敵攻撃圏外に出られる経路数
        int totalPaths = 0;  // 有効な隣接マス数

        foreach (var dir in directions)
        {
            Vector3 neighbor = retreatPos + dir;
            // マップ外チェック（簡易: 有効タイルかどうか）
            if (!board.IsValidTile(neighbor)) continue;
            totalPaths++;

            // 隣接マスでの被ダメージ推定
            int dmgAtNeighbor = board.EstimateCounterDamageAt(neighbor, unit);
            if (dmgAtNeighbor < unit.HP * 0.3f)
                safePaths++;
        }

        if (totalPaths == 0)
            return -15f; // 完全に袋小路

        float safeRatio = (float)safePaths / totalPaths;

        if (safeRatio <= 0f)
            return -12f; // すべての退路が敵の攻撃圏内
        if (safeRatio < 0.5f)
            return -5f;  // 退路が半分以上塞がれている
        if (safeRatio >= 0.75f)
            return 6f;   // 十分な退路がある

        return 0f; // 普通
    }

    static float CalcTacticalMoveBonus(AIAction action, AIPersonality p, AIBoardState board)
    {
        float bonus = 0f;
        foreach (var pu in board.AlivePlayerUnits)
        {
            if (pu == null || !pu.gameObject.activeInHierarchy) continue;
            float dist = Vector3.Distance(action.TargetPos, pu.transform.position);
            if (dist < 2f)
            {
                Vector3 diff = action.TargetPos - pu.transform.position;
                bool isFlanking = Mathf.Abs(diff.x) > Mathf.Abs(diff.z);
                if (isFlanking)
                    bonus += p.TacticsRate * 10f;
            }
        }
        return bonus;
    }

    // ================================================================
    //  公開ラッパー: AICommander の建築先行フェーズから利用
    // ================================================================

    /// <summary>建築候補のみを生成して results に追加する</summary>
    public static void GenerateBuildCandidatesPublic(AIBoardState board, List<AIAction> results)
        => AIActionGenerator.GenerateBuildCandidates(board, results);

    /// <summary>サブクリスタル候補のみを生成して results に追加する</summary>
    public static void GenerateSubCrystalCandidatesPublic(AIBoardState board, List<AIAction> results)
        => AIActionGenerator.GenerateSubCrystalCandidates(board, results);

    /// <summary>建築アクション用のスコアを計算する</summary>
    public static float CalcBuildScorePublic(AIAction action, AIPersonality p, AIBoardState board, AILearning learning)
        => CalcScore(action, p, board, learning);
}
