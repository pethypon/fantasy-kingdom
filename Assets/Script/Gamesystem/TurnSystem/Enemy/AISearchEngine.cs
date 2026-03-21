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

        // 現在の盤面価値
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
        Debug.Log($"[AISearchEngine] 先読み完了: 深さ{_maxDepth} 評価{_nodesEvaluated}ノード {_elapsedMs:F0}ms");

        return result;
    }

    /// <summary>
    /// 1つの行動に対する先読み評価。
    /// 自軍行動→敵応答→自軍再応答 の3段階。
    /// </summary>
    float EvaluateAction(AIAction action, AIBoardState board, float currentBoardValue, int depth)
    {
        if (depth > _maxDepth) return 0f;

        float actionScore = 0f;

        // ---- 1手目: 自軍行動の推定効果 ----
        float immediateEffect = EstimateActionEffect(action, board);
        actionScore += immediateEffect;

        if (depth >= _maxDepth) return actionScore;

        // ---- 2手目: 敵（Player）の最善応答を推定 ----
        float enemyResponse = EstimateEnemyBestResponse(action, board);
        actionScore -= enemyResponse * 0.8f; // 敵の応答は価値を減じる

        if (depth + 1 >= _maxDepth) return actionScore;

        // ---- 3手目: 自軍の再応答を推定 ----
        float ourReResponse = EstimateOurBestReResponse(action, board);
        actionScore += ourReResponse * 0.6f; // 未来の価値は割り引く

        return actionScore;
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
                        // 撃破の価値
                        effect += GetPieceValue(action.TargetUnit) * 1.5f;
                        // クリスタルなら特大
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

                        // 範囲ヒットボーナス
                        if (action.AreaTargets != null)
                            effect += action.AreaTargets.Count * 8f;
                    }
                    if (action.Skill.FixedHeal > 0)
                        effect += action.Skill.FixedHeal * 0.3f;
                }
                break;

            case AIActionType.Move:
            case AIActionType.Surround:
                // 位置的優位の変化
                effect += EstimatePositionalGain(action, board);
                break;

            case AIActionType.Build:
                // 経済効果は数ターン後に出るため割引
                effect += 5f;
                break;

            case AIActionType.Summon:
                // 新しい駒の追加価値
                effect += 12f;
                break;

            case AIActionType.Retreat:
                // 生存価値（瀕死の駒を守る）
                if (action.Unit != null)
                {
                    float hpRatio = action.Unit.MaxHP > 0 ? (float)action.Unit.HP / action.Unit.MaxHP : 1f;
                    if (hpRatio < 0.3f)
                        effect += GetPieceValue(action.Unit) * 0.4f;
                }
                break;
        }

        // AP効率: コストが高いほど効果も高くないとペナルティ
        if (action.APCost > 0)
            effect -= action.APCost * 0.5f;

        return effect;
    }

    /// <summary>敵の最善応答を推定（視界内の敵駒ベース）</summary>
    float EstimateEnemyBestResponse(AIAction ourAction, AIBoardState board)
    {
        if (board.AlivePlayerUnits.Count == 0) return 0f;

        float worstCase = 0f;

        foreach (var pu in board.AlivePlayerUnits)
        {
            if (pu == null || !pu.gameObject.activeInHierarchy) continue;

            // 敵が反撃できるか推定
            float dist = Vector3.Distance(
                ourAction.ActionType == AIActionType.Move ? ourAction.TargetPos : ourAction.Unit?.transform.position ?? Vector3.zero,
                pu.transform.position);

            float attackRange = EstimateAttackRange(pu);

            if (dist <= attackRange + 1.5f)
            {
                // 敵が反撃可能
                Status targetUnit = ourAction.Unit;
                if (targetUnit != null)
                {
                    int counterDmg = EstimateDamage(pu, targetUnit);
                    float threat = counterDmg;

                    // 確殺されるならさらに高い脅威
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
        // 攻撃可能な敵がいれば、攻撃の価値を概算
        float bestValue = 0f;

        foreach (var unit in board.AliveEnemyUnits)
        {
            if (unit == null || !unit.gameObject.activeInHierarchy) continue;
            if (unit == firstAction.Unit) continue; // 1手目で動いた駒は除く（簡易）

            foreach (var pu in board.AlivePlayerUnits)
            {
                if (pu == null || !pu.gameObject.activeInHierarchy) continue;
                float dist = Vector3.Distance(unit.transform.position, pu.transform.position);
                float atkRange = EstimateAttackRange(unit);

                if (dist <= atkRange + 2f) // 1手移動+攻撃の範囲
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

        // 視界内の敵への接近
        foreach (var pu in board.AlivePlayerUnits)
        {
            if (pu == null || !pu.gameObject.activeInHierarchy) continue;
            float dBefore = Vector3.Distance(from, pu.transform.position);
            float dAfter = Vector3.Distance(to, pu.transform.position);

            // 攻撃圏内に入れる移動は高い位置的利得
            if (dAfter <= 2f && dBefore > 2f)
                gain += 8f;
            else if (dAfter < dBefore)
                gain += (dBefore - dAfter) * 1.5f;
        }

        // 反撃リスクの考慮
        int counterDmg = board.EstimateCounterDamageAt(to, action.Unit);
        if (counterDmg > 0)
            gain -= counterDmg * 0.3f;

        // 味方との連携距離
        float allyDist = board.GetNearestAllyDist(to, action.Unit);
        if (allyDist >= 2f && allyDist <= 4f)
            gain += 3f; // 適度な距離
        else if (allyDist > 6f)
            gain -= 4f; // 孤立リスク

        return gain;
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

    /// <summary>
    /// フェイルセーフ: 探索失敗時の安全行動列を生成
    /// 防衛優先・AP浪費回避の行動を返す
    /// </summary>
    public static AIAction GetSafeDefault(List<AIAction> candidates, AIBoardState board)
    {
        if (candidates == null || candidates.Count == 0) return null;

        // 攻撃可能ならそれを返す（最低限の反撃）
        foreach (var a in candidates)
        {
            if (a.ActionType == AIActionType.Attack && a.APCost <= board.EnemyAP)
                return a;
        }

        // 建築可能ならそれを返す（経済維持）
        foreach (var a in candidates)
        {
            if (a.ActionType == AIActionType.Build && a.APCost <= board.EnemyAP)
                return a;
        }

        // 防衛再配置
        foreach (var a in candidates)
        {
            if (a.ActionType == AIActionType.DefenseRepos && a.APCost <= board.EnemyAP)
                return a;
        }

        // 撤退
        foreach (var a in candidates)
        {
            if (a.ActionType == AIActionType.Retreat && a.APCost <= board.EnemyAP)
                return a;
        }

        // 何でもいいからAP支払い可能な行動
        foreach (var a in candidates)
        {
            if (a.ActionType != AIActionType.Wait && a.APCost <= board.EnemyAP)
                return a;
        }

        return null;
    }
}
