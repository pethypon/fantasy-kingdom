using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// =====================================================================
//  AISearchEngine — 3手先探索エンジン
//
//  仕様:
//  ・探索深さは最低3ply相当
//    - 1手目: 自軍の候補行動
//    - 2手目: 敵の代表応答
//    - 3手目: その応答に対する自軍の再応答
//  ・探索対象は戦略層で絞った上位候補に限定
//  ・3手先探索の結果は単発スコアより優先して採用判断に使う
//  ・制限時間内に探索しきれない場合は戦略整合性を保った安全行動列を返す
//  ・盤面価値関数（AIBoardEvaluator）を末端評価に統合
//
//  実装制約:
//  ・実際のゲームオブジェクト移動は行わない（評価は盤面価値関数で推定）
//  ・探索は「候補行動のスコア差分」で近似する（完全シミュレーションではない）
//  ・フェイルセーフ: 探索失敗時もデフォルト方針で安全行動列を返す
// =====================================================================
public class AISearchEngine
{
    // ---- 定数 ----
    const float TimeBudgetMs = 200f;   // 1ターンの探索予算（ms）
    const int DefaultCandidateLimit = 5;

    // 盤面価値関数の統合重み（先読み評価に盤面全体評価を混合）
    const float BoardEvalWeight = 0.3f;
    // 行動効果推定の重み
    const float ActionEffectWeight = 0.7f;
    // 未来の価値の割引率
    const float FutureDiscountPly2 = 0.8f;
    const float FutureDiscountPly3 = 0.6f;

    // ---- 設定 ----
    int _maxDepth;
    int _candidateLimit;
    AIDeterministicRandom _rng;

    // ---- 統計 ----
    int _nodesEvaluated;
    float _elapsedMs;

    public AISearchEngine(int maxDepth = 3, int candidateLimit = 5, AIDeterministicRandom rng = null)
    {
        _maxDepth = Mathf.Clamp(maxDepth, 1, 3);
        _candidateLimit = Mathf.Max(3, candidateLimit);
        _rng = rng;
    }

    /// <summary>
    /// 上位候補行動に対して先読み評価を行い、補正スコアを返す。
    /// 返すのは Dictionary(AIAction → 先読み補正スコア)。
    /// 元のスコアに加算して使う。
    /// </summary>
    public Dictionary<AIAction, float> EvaluateWithLookahead(
        List<AIAction> topCandidates,
        AIBoardState board,
        AIPersonality personality,
        AILearning learning)
    {
        var result = new Dictionary<AIAction, float>();
        _nodesEvaluated = 0;

        float startTime = Time.realtimeSinceStartup * 1000f;

        // 現在の盤面価値（基準点）
        float currentBoardValue = AIBoardEvaluator.Evaluate(board);

        foreach (var action in topCandidates)
        {
            // 時間チェック
            _elapsedMs = (Time.realtimeSinceStartup * 1000f) - startTime;
            if (_elapsedMs > TimeBudgetMs)
            {
                Debug.Log($"[AISearchEngine] 時間切れ ({_elapsedMs:F0}ms) — 残り{topCandidates.Count - result.Count}候補は評価なし");
                break;
            }

            float lookaheadScore = EvaluateAction(action, board, currentBoardValue, 1);
            result[action] = lookaheadScore;
            _nodesEvaluated++;
        }

        _elapsedMs = (Time.realtimeSinceStartup * 1000f) - startTime;
        Debug.Log($"[AISearchEngine] 先読み完了: 深さ{_maxDepth} 評価{_nodesEvaluated}ノード {_elapsedMs:F0}ms  基準盤面値={currentBoardValue:F1}");

        return result;
    }

    /// <summary>
    /// 1つの行動に対する先読み評価。
    /// 自軍行動→敵応答→自軍再応答 の3段階。
    /// 盤面価値関数を統合した評価を行う。
    /// </summary>
    float EvaluateAction(AIAction action, AIBoardState board, float currentBoardValue, int depth)
    {
        if (depth > _maxDepth) return 0f;

        float actionScore = 0f;

        // ---- 1手目: 自軍行動の推定効果 ----
        float immediateEffect = EstimateActionEffect(action, board);

        // 盤面価値関数による推定差分
        // 行動後に盤面がどう変わるかを概算する
        float boardDelta = EstimateBoardValueDelta(action, board, currentBoardValue);

        // 行動効果と盤面評価を混合
        actionScore += immediateEffect * ActionEffectWeight + boardDelta * BoardEvalWeight;

        if (depth >= _maxDepth) return actionScore;

        // ---- 2手目: 敵（Player）の最善応答を推定 ----
        float enemyResponse = EstimateEnemyBestResponse(action, board);
        actionScore -= enemyResponse * FutureDiscountPly2;

        if (depth + 1 >= _maxDepth) return actionScore;

        // ---- 3手目: 自軍の再応答を推定 ----
        float ourReResponse = EstimateOurBestReResponse(action, board);
        actionScore += ourReResponse * FutureDiscountPly3;

        return actionScore;
    }

    /// <summary>
    /// 行動後の盤面価値変化を推定する（AIBoardEvaluator統合）。
    /// 実際に盤面を変更せず、行動種別から差分を概算する。
    /// </summary>
    float EstimateBoardValueDelta(AIAction action, AIBoardState board, float currentBoardValue)
    {
        float delta = 0f;

        switch (action.ActionType)
        {
            case AIActionType.Attack:
                if (action.TargetUnit != null)
                {
                    int dmg = EstimateDamage(action.Unit, action.TargetUnit);
                    bool wouldKill = dmg >= action.TargetUnit.HP;

                    if (wouldKill)
                    {
                        // 駒を除去した場合の価値変化（駒価値差が改善）
                        float pieceVal = AIBoardEvaluator.GetPieceValuePublic(action.TargetUnit);
                        delta += pieceVal;

                        // クリスタル攻撃は安全度も改善
                        if (action.TargetUnit.kind == Kind.Crystal)
                            delta += 50f;
                    }
                    else
                    {
                        // HPを削った分の価値変化
                        float targetMaxHP = Mathf.Max(1f, action.TargetUnit.MaxHP);
                        float hpLossRatio = dmg / targetMaxHP;
                        delta += hpLossRatio * AIBoardEvaluator.GetPieceValuePublic(action.TargetUnit) * 0.7f;
                    }
                }
                break;

            case AIActionType.Move:
            case AIActionType.Surround:
                // 前線位置の変化による前線厚み・クリスタル安全度への影響
                if (action.Unit != null)
                {
                    Vector3 from = action.Unit.transform.position;
                    Vector3 to = action.TargetPos;

                    // クリスタル防御への影響
                    float crystalDistBefore = Vector3.Distance(from, board.EnemyCrystalPos);
                    float crystalDistAfter = Vector3.Distance(to, board.EnemyCrystalPos);
                    if (crystalDistAfter < crystalDistBefore && crystalDistAfter < 5f)
                        delta += 3f; // クリスタル防御強化

                    // 前線進出の価値
                    if (board.CanUsePlayerCrystalAsTarget())
                    {
                        float advanceBefore = Vector3.Distance(from, board.PlayerCrystalPos);
                        float advanceAfter = Vector3.Distance(to, board.PlayerCrystalPos);
                        float advance = advanceBefore - advanceAfter;
                        if (advance > 0) delta += advance * 1.5f;
                    }
                }
                break;

            case AIActionType.Build:
                // 経済継続性の改善
                delta += 8f; // 建物追加による経済スコア向上
                break;

            case AIActionType.Summon:
                // 駒価値の追加
                delta += 15f; // 新駒追加による駒価値差改善
                break;

            case AIActionType.Retreat:
                // 生存による駒価値維持
                if (action.Unit != null)
                {
                    float hpRatio = action.Unit.MaxHP > 0 ? (float)action.Unit.HP / action.Unit.MaxHP : 1f;
                    if (hpRatio < 0.3f)
                    {
                        float pieceVal = AIBoardEvaluator.GetPieceValuePublic(action.Unit);
                        delta += pieceVal * 0.3f; // 駒温存の価値
                    }
                }
                break;
        }

        return delta;
    }

    /// <summary>
    /// 行動の即座の効果を推定する（実際のゲーム状態を変えずに）
    /// </summary>
    float EstimateActionEffect(AIAction action, AIBoardState board)
    {
        float effect = 0f;

        switch (action.ActionType)
        {
            case AIActionType.Attack:
                if (action.TargetUnit != null)
                {
                    int dmg = EstimateDamage(action.Unit, action.TargetUnit);
                    bool wouldKill = dmg >= action.TargetUnit.HP;

                    if (wouldKill)
                    {
                        effect += GetPieceValue(action.TargetUnit) * 1.5f;
                        if (action.TargetUnit.kind == Kind.Crystal)
                            effect += 100f;
                    }
                    else
                    {
                        effect += dmg * 0.5f;
                    }
                }
                break;

            case AIActionType.SkillUse:
                if (action.Skill != null)
                {
                    if (action.Skill.Multiplier > 0 && action.TargetUnit != null)
                    {
                        int dmg = SkillSystem.CalcSkillDamage(action.Unit, action.TargetUnit, action.Skill);
                        effect += dmg * 0.6f;
                        if (dmg >= action.TargetUnit.HP)
                            effect += GetPieceValue(action.TargetUnit);

                        if (action.AreaTargets != null)
                            effect += action.AreaTargets.Count * 8f;
                    }
                    if (action.Skill.FixedHeal > 0)
                        effect += action.Skill.FixedHeal * 0.3f;
                }
                break;

            case AIActionType.Move:
            case AIActionType.Surround:
                effect += EstimatePositionalGain(action, board);
                break;

            case AIActionType.Build:
                effect += 5f;
                break;

            case AIActionType.Summon:
                effect += 12f;
                break;

            case AIActionType.Retreat:
                if (action.Unit != null)
                {
                    float hpRatio = action.Unit.MaxHP > 0 ? (float)action.Unit.HP / action.Unit.MaxHP : 1f;
                    if (hpRatio < 0.3f)
                        effect += GetPieceValue(action.Unit) * 0.4f;
                }
                break;
        }

        // AP効率
        if (action.APCost > 0)
            effect -= action.APCost * 0.5f;

        return effect;
    }

    /// <summary>敵の最善応答を推定（視界内の敵駒ベース）</summary>
    float EstimateEnemyBestResponse(AIAction ourAction, AIBoardState board)
    {
        if (board.AlivePlayerUnits.Count == 0) return 0f;

        float worstCase = 0f;

        // 行動後の自駒位置を推定
        Vector3 unitPosAfter = GetPositionAfterAction(ourAction);

        foreach (var pu in board.AlivePlayerUnits)
        {
            if (pu == null || !pu.gameObject.activeInHierarchy) continue;

            float dist = Vector3.Distance(unitPosAfter, pu.transform.position);
            float attackRange = EstimateAttackRange(pu);

            if (dist <= attackRange + 1.5f)
            {
                Status targetUnit = ourAction.Unit;
                if (targetUnit != null)
                {
                    int counterDmg = EstimateDamage(pu, targetUnit);
                    float threat = counterDmg;

                    if (counterDmg >= targetUnit.HP)
                        threat += GetPieceValue(targetUnit) * 0.5f;

                    if (threat > worstCase) worstCase = threat;
                }
            }
        }

        return worstCase;
    }

    /// <summary>自軍の再応答を推定（3手目）</summary>
    float EstimateOurBestReResponse(AIAction firstAction, AIBoardState board)
    {
        float bestValue = 0f;

        foreach (var unit in board.AliveEnemyUnits)
        {
            if (unit == null || !unit.gameObject.activeInHierarchy) continue;
            if (unit == firstAction.Unit) continue;

            foreach (var pu in board.AlivePlayerUnits)
            {
                if (pu == null || !pu.gameObject.activeInHierarchy) continue;
                float dist = Vector3.Distance(unit.transform.position, pu.transform.position);
                float atkRange = EstimateAttackRange(unit);

                if (dist <= atkRange + 2f)
                {
                    int dmg = EstimateDamage(unit, pu);
                    float value = dmg * 0.4f;
                    if (dmg >= pu.HP) value += GetPieceValue(pu) * 0.3f;
                    if (value > bestValue) bestValue = value;
                }
            }
        }

        return bestValue;
    }

    /// <summary>移動の位置的利得を推定</summary>
    float EstimatePositionalGain(AIAction action, AIBoardState board)
    {
        if (action.Unit == null) return 0f;
        float gain = 0f;

        Vector3 from = action.Unit.transform.position;
        Vector3 to = action.TargetPos;

        foreach (var pu in board.AlivePlayerUnits)
        {
            if (pu == null || !pu.gameObject.activeInHierarchy) continue;
            float dBefore = Vector3.Distance(from, pu.transform.position);
            float dAfter = Vector3.Distance(to, pu.transform.position);

            if (dAfter <= 2f && dBefore > 2f)
                gain += 8f;
            else if (dAfter < dBefore)
                gain += (dBefore - dAfter) * 1.5f;
        }

        int counterDmg = board.EstimateCounterDamageAt(to, action.Unit);
        if (counterDmg > 0)
            gain -= counterDmg * 0.3f;

        float allyDist = board.GetNearestAllyDist(to, action.Unit);
        if (allyDist >= 2f && allyDist <= 4f)
            gain += 3f;
        else if (allyDist > 6f)
            gain -= 4f;

        return gain;
    }

    /// <summary>行動後の駒位置を推定</summary>
    static Vector3 GetPositionAfterAction(AIAction action)
    {
        if (action.ActionType == AIActionType.Move
            || action.ActionType == AIActionType.Surround
            || action.ActionType == AIActionType.Support
            || action.ActionType == AIActionType.Retreat
            || action.ActionType == AIActionType.DefenseRepos)
        {
            return action.TargetPos;
        }
        return action.Unit != null ? action.Unit.transform.position : Vector3.zero;
    }

    // ---- ヘルパー ----

    static int EstimateDamage(Status attacker, Status defender)
    {
        if (attacker == null || defender == null) return 0;
        int atk = attacker.ATK;
        int def = defender.DEF;
        return Mathf.Max(0, 1 + (atk / 6) + ((atk / 2) - (def / 4)));
    }

    static float GetPieceValue(Status unit)
    {
        if (unit == null) return 0f;
        switch (unit.kind)
        {
            case Kind.Crystal:      return 200f;
            case Kind.King:         return 100f;
            case Kind.Boss:         return 80f;
            case Kind.Magicsniper:  return 35f;
            case Kind.Priest:       return 35f;
            case Kind.Bomber:       return 32f;
            case Kind.Magic:        return 30f;
            case Kind.Guardian:     return 30f;
            case Kind.Archer:       return 28f;
            case Kind.Crossbow:     return 28f;
            case Kind.Assassin:     return 26f;
            case Kind.Knight:       return 25f;
            case Kind.Scout:        return 18f;
            default:                return 20f;
        }
    }

    static float EstimateAttackRange(Status unit)
    {
        if (unit == null) return 1.5f;
        switch (unit.kind)
        {
            case Kind.Archer:       return 3f;
            case Kind.Magic:        return 2f;
            case Kind.Crossbow:     return 2f;
            case Kind.Magicsniper:  return 4f;
            case Kind.Bomber:       return 3f;
            default:                return 1.5f;
        }
    }

    // ================================================================
    //  フェイルセーフ: 探索失敗時の安全行動列
    //  仕様準拠の優先順位:
    //  1. クリスタル防衛
    //  2. 致命的損失の回避
    //  3. AP浪費の抑制
    //  4. 前線維持
    //  5. 最低限の経済維持
    // ================================================================
    public static AIAction GetSafeDefault(List<AIAction> candidates, AIBoardState board)
    {
        if (candidates == null || candidates.Count == 0) return null;

        var affordable = new List<AIAction>();
        foreach (var a in candidates)
        {
            if (a.ActionType != AIActionType.Wait && a.APCost <= board.EnemyAP)
                affordable.Add(a);
        }
        if (affordable.Count == 0) return null;

        // 1. クリスタル防衛: クリスタル付近での攻撃・防衛再配置を最優先
        AIAction crystalDefense = FindCrystalDefenseAction(affordable, board);
        if (crystalDefense != null) return crystalDefense;

        // 2. 致命的損失の回避: 瀕死の駒を撤退させる
        foreach (var a in affordable)
        {
            if (a.ActionType == AIActionType.Retreat && a.Unit != null)
            {
                float hpRatio = a.Unit.MaxHP > 0 ? (float)a.Unit.HP / a.Unit.MaxHP : 1f;
                if (hpRatio < 0.3f)
                    return a;
            }
        }

        // 3. AP浪費の抑制: 攻撃可能ならそれを返す（APを有効活用）
        foreach (var a in affordable)
        {
            if (a.ActionType == AIActionType.Attack)
                return a;
        }

        // 4. 前線維持: 防衛再配置
        foreach (var a in affordable)
        {
            if (a.ActionType == AIActionType.DefenseRepos)
                return a;
        }

        // 5. 最低限の経済維持: 建築
        foreach (var a in affordable)
        {
            if (a.ActionType == AIActionType.Build)
                return a;
        }

        // それ以外: 何でもいいから実行可能な行動
        return affordable.Count > 0 ? affordable[0] : null;
    }

    /// <summary>クリスタル付近の脅威に対処する行動を探す</summary>
    static AIAction FindCrystalDefenseAction(List<AIAction> candidates, AIBoardState board)
    {
        // クリスタル付近に敵がいるか確認
        bool crystalThreatened = false;
        foreach (var pu in board.AlivePlayerUnits)
        {
            if (pu == null || !pu.gameObject.activeInHierarchy) continue;
            if (Vector3.Distance(pu.transform.position, board.EnemyCrystalPos) < 5f)
            {
                crystalThreatened = true;
                break;
            }
        }

        if (!crystalThreatened) return null;

        // クリスタル付近での攻撃を優先
        AIAction bestAttack = null;
        float bestScore = float.MinValue;
        foreach (var a in candidates)
        {
            if (a.ActionType == AIActionType.Attack && a.TargetUnit != null)
            {
                float dist = Vector3.Distance(a.TargetUnit.transform.position, board.EnemyCrystalPos);
                if (dist < 5f && a.Score > bestScore)
                {
                    bestScore = a.Score;
                    bestAttack = a;
                }
            }
        }
        if (bestAttack != null) return bestAttack;

        // 防衛再配置
        foreach (var a in candidates)
        {
            if (a.ActionType == AIActionType.DefenseRepos)
                return a;
        }

        return null;
    }
}
