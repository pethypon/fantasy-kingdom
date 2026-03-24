using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  AISearchEngine — 3手先探索エンジン (Minimax + Alpha-Beta 完全シミュレーション版)
//
//  仕様:
//  ・探索深さは3ply（自軍→敵応答→自軍再応答）
//  ・SimBoardState上で実際に行動をシミュレーションする
//  ・建築、戦闘、視野の拡張、建築物の強化すべてをシミュレーション対象
//  ・AIMinimaxEngineによるAlpha-Beta枝刈り付きMinimax探索
//  ・探索結果は元のスコアに加算する補正値として返す
//
//  旧実装との違い:
//  ・旧: 行動効果を概算で推定（実際の盤面変更なし）
//  ・新: SimBoardState上で実際に行動を適用し、変化後の盤面を評価
//  ・新: Playerの応手も実際にシミュレーション
//  ・新: Minimax + Alpha-Beta枝刈りで探索効率を最大化
// =====================================================================
public class AISearchEngine
{
    // ---- 設定 ----
    int _maxDepth;
    int _candidateLimit;
    AIDeterministicRandom _rng;

    // ---- シミュレーション用の追加参照 ----
    MoveGererater _moveGen;
    UnitSetting _unitSet;
    CrystalSystem _crystalSystem;
    APSystem _apSystem;

    // ---- 統計 ----
    int _nodesEvaluated;
    float _elapsedMs;

    public AISearchEngine(int maxDepth = 3, int candidateLimit = 12, AIDeterministicRandom rng = null)
    {
        _maxDepth = Mathf.Clamp(maxDepth, 1, 20);
        _candidateLimit = Mathf.Max(3, candidateLimit);
        _rng = rng;
    }

    /// <summary>シミュレーション用の参照を設定する</summary>
    public void SetSimulationReferences(MoveGererater moveGen, UnitSetting unitSet,
        CrystalSystem crystalSystem, APSystem apSystem)
    {
        _moveGen = moveGen;
        _unitSet = unitSet;
        _crystalSystem = crystalSystem;
        _apSystem = apSystem;
    }

    /// <summary>
    /// 上位候補行動に対して3手先読み評価を行い、補正スコアを返す。
    /// 返すのは Dictionary(AIAction → 先読み補正スコア)。
    /// 元のスコアに加算して使う。
    ///
    /// 新実装: SimBoardState上でのMinimax探索を実行する。
    /// シミュレーション参照が未設定の場合はフォールバック評価を使用。
    /// </summary>
    public Dictionary<AIAction, float> EvaluateWithLookahead(
        List<AIAction> topCandidates,
        AIBoardState board,
        AIPersonality personality,
        AILearning learning)
    {
        _nodesEvaluated = 0;

        // シミュレーション参照が設定されている場合は完全シミュレーション
        if (_moveGen != null && _unitSet != null && _crystalSystem != null)
        {
            return EvaluateWithFullSimulation(topCandidates, board);
        }

        // フォールバック: 旧来のヒューリスティック評価
        return EvaluateWithHeuristic(topCandidates, board);
    }

    // ================================================================
    //  完全シミュレーション版先読み (Minimax + Alpha-Beta)
    // ================================================================
    Dictionary<AIAction, float> EvaluateWithFullSimulation(
        List<AIAction> topCandidates, AIBoardState board)
    {
        float startTime = Time.realtimeSinceStartup * 1000f;

        // SimBoardStateを生成
        SimBoardState simBoard;
        try
        {
            simBoard = SimBoardState.CreateFromGame(board, _moveGen, _unitSet, _crystalSystem, _apSystem);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[AISearchEngine] SimBoardState生成失敗: {e.Message}");
            return EvaluateWithHeuristic(topCandidates, board);
        }

        // AIMinimaxEngineで探索
        // 反復深化 + キラームーブ + 正確なターン遷移付き
        var minimax = new AIMinimaxEngine(
            maxDepth: _maxDepth,
            candidateLimit: Mathf.Max(_candidateLimit, 14),
            greedyActionsPerTurn: 10,
            timeBudgetMs: 30000f
        );

        Dictionary<AIAction, float> result;
        try
        {
            result = minimax.Search(topCandidates, simBoard, board);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[AISearchEngine] Minimax探索中に例外: {e.Message}\n{e.StackTrace}");
            result = EvaluateWithHeuristic(topCandidates, board);
        }

        _elapsedMs = (Time.realtimeSinceStartup * 1000f) - startTime;
        Debug.Log($"[AISearchEngine] 完全シミュレーション先読み完了: " +
            $"深さ{_maxDepth} 候補{topCandidates.Count} {_elapsedMs:F0}ms");

        return result;
    }

    // ================================================================
    //  フォールバック: ヒューリスティック評価 (旧ロジック)
    // ================================================================
    Dictionary<AIAction, float> EvaluateWithHeuristic(
        List<AIAction> topCandidates, AIBoardState board)
    {
        var result = new Dictionary<AIAction, float>();
        float startTime = Time.realtimeSinceStartup * 1000f;
        float currentBoardValue = AIBoardEvaluator.Evaluate(board);

        foreach (var action in topCandidates)
        {
            _elapsedMs = (Time.realtimeSinceStartup * 1000f) - startTime;
            if (_elapsedMs > 5000f) // フォールバック時は5秒制限
            {
                Debug.Log($"[AISearchEngine] フォールバック時間切れ ({_elapsedMs:F0}ms)");
                break;
            }

            float lookaheadScore = EvaluateActionHeuristic(action, board, currentBoardValue, 1);
            result[action] = lookaheadScore;
            _nodesEvaluated++;
        }

        _elapsedMs = (Time.realtimeSinceStartup * 1000f) - startTime;
        Debug.Log($"[AISearchEngine] フォールバック先読み完了: " +
            $"深さ{_maxDepth} 評価{_nodesEvaluated}ノード {_elapsedMs:F0}ms");

        return result;
    }

    // ================================================================
    //  ヒューリスティック評価 (旧ロジック — フォールバック用)
    // ================================================================
    float EvaluateActionHeuristic(AIAction action, AIBoardState board,
        float currentBoardValue, int depth)
    {
        if (depth > _maxDepth) return 0f;

        float actionScore = 0f;

        // 即座の効果推定
        float immediateEffect = EstimateActionEffect(action, board);
        float boardDelta = EstimateBoardValueDelta(action, board, currentBoardValue);
        actionScore += immediateEffect * 0.7f + boardDelta * 0.3f;

        if (depth >= _maxDepth) return actionScore;

        // 敵の応答推定
        float enemyResponse = EstimateEnemyBestResponse(action, board);
        actionScore -= enemyResponse * 0.8f;

        if (depth + 1 >= _maxDepth) return actionScore;

        // 自軍の再応答推定
        float ourReResponse = EstimateOurBestReResponse(action, board);
        actionScore += ourReResponse * 0.6f;

        return actionScore;
    }

    // ================================================================
    //  フェイルセーフ: 探索失敗時の安全行動列
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

        // 1. クリスタル防衛
        AIAction crystalDefense = FindCrystalDefenseAction(affordable, board);
        if (crystalDefense != null) return crystalDefense;

        // 2. 瀕死の駒を撤退
        foreach (var a in affordable)
        {
            if (a.ActionType == AIActionType.Retreat && a.Unit != null)
            {
                float hpRatio = a.Unit.MaxHP > 0 ? (float)a.Unit.HP / a.Unit.MaxHP : 1f;
                if (hpRatio < 0.3f) return a;
            }
        }

        // 3. 攻撃可能なら攻撃
        foreach (var a in affordable)
        {
            if (a.ActionType == AIActionType.Attack) return a;
        }

        // 4. 防衛再配置
        foreach (var a in affordable)
        {
            if (a.ActionType == AIActionType.DefenseRepos) return a;
        }

        // 5. 建築
        foreach (var a in affordable)
        {
            if (a.ActionType == AIActionType.Build) return a;
        }

        return affordable.Count > 0 ? affordable[0] : null;
    }

    static AIAction FindCrystalDefenseAction(List<AIAction> candidates, AIBoardState board)
    {
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

        foreach (var a in candidates)
        {
            if (a.ActionType == AIActionType.DefenseRepos) return a;
        }

        return null;
    }

    // ================================================================
    //  ヒューリスティック推定ヘルパー (フォールバック用)
    // ================================================================

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
                        delta += AIBoardEvaluator.GetPieceValuePublic(action.TargetUnit);
                        if (action.TargetUnit.kind == Kind.Crystal) delta += 50f;
                    }
                    else
                    {
                        float maxHP = Mathf.Max(1f, action.TargetUnit.MaxHP);
                        delta += (dmg / maxHP) * AIBoardEvaluator.GetPieceValuePublic(action.TargetUnit) * 0.7f;
                    }
                }
                break;
            case AIActionType.Move:
            case AIActionType.Surround:
                if (action.Unit != null)
                {
                    Vector3 from = action.Unit.transform.position;
                    Vector3 to = action.TargetPos;
                    float crystalDistBefore = Vector3.Distance(from, board.EnemyCrystalPos);
                    float crystalDistAfter = Vector3.Distance(to, board.EnemyCrystalPos);
                    if (crystalDistAfter < crystalDistBefore && crystalDistAfter < 5f)
                        delta += 3f;
                    if (board.CanUsePlayerCrystalAsTarget())
                    {
                        float advance = Vector3.Distance(from, board.PlayerCrystalPos)
                            - Vector3.Distance(to, board.PlayerCrystalPos);
                        if (advance > 0) delta += advance * 1.5f;
                    }
                }
                break;
            case AIActionType.Build:
                delta += 8f;
                break;
            case AIActionType.Summon:
                delta += 15f;
                break;
            case AIActionType.Retreat:
                if (action.Unit != null)
                {
                    float hpR = action.Unit.MaxHP > 0 ? (float)action.Unit.HP / action.Unit.MaxHP : 1f;
                    if (hpR < 0.3f) delta += AIBoardEvaluator.GetPieceValuePublic(action.Unit) * 0.3f;
                }
                break;
        }
        return delta;
    }

    float EstimateActionEffect(AIAction action, AIBoardState board)
    {
        float effect = 0f;
        switch (action.ActionType)
        {
            case AIActionType.Attack:
                if (action.TargetUnit != null)
                {
                    int dmg = EstimateDamage(action.Unit, action.TargetUnit);
                    if (dmg >= action.TargetUnit.HP)
                    {
                        effect += GetPieceValue(action.TargetUnit) * 1.5f;
                        if (action.TargetUnit.kind == Kind.Crystal) effect += 100f;
                    }
                    else
                        effect += dmg * 0.5f;
                }
                break;
            case AIActionType.SkillUse:
                if (action.Skill != null && action.Skill.Multiplier > 0 && action.TargetUnit != null)
                {
                    int dmg = SkillSystem.CalcSkillDamage(action.Unit, action.TargetUnit, action.Skill);
                    effect += dmg * 0.6f;
                    if (dmg >= action.TargetUnit.HP) effect += GetPieceValue(action.TargetUnit);
                    if (action.AreaTargets != null) effect += action.AreaTargets.Count * 8f;
                }
                if (action.Skill != null && action.Skill.FixedHeal > 0)
                    effect += action.Skill.FixedHeal * 0.3f;
                break;
            case AIActionType.Move:
            case AIActionType.Surround:
                effect += EstimatePositionalGain(action, board);
                break;
            case AIActionType.Build: effect += 5f; break;
            case AIActionType.Summon: effect += 12f; break;
            case AIActionType.Retreat:
                if (action.Unit != null)
                {
                    float hpR = action.Unit.MaxHP > 0 ? (float)action.Unit.HP / action.Unit.MaxHP : 1f;
                    if (hpR < 0.3f) effect += GetPieceValue(action.Unit) * 0.4f;
                }
                break;
        }
        if (action.APCost > 0) effect -= action.APCost * 0.5f;
        return effect;
    }

    float EstimateEnemyBestResponse(AIAction ourAction, AIBoardState board)
    {
        if (board.AlivePlayerUnits.Count == 0) return 0f;
        float worstCase = 0f;
        Vector3 unitPosAfter = GetPositionAfterAction(ourAction);
        foreach (var pu in board.AlivePlayerUnits)
        {
            if (pu == null || !pu.gameObject.activeInHierarchy) continue;
            float dist = Vector3.Distance(unitPosAfter, pu.transform.position);
            float attackRange = EstimateAttackRange(pu);
            if (dist <= attackRange + 1.5f && ourAction.Unit != null)
            {
                int counterDmg = EstimateDamage(pu, ourAction.Unit);
                float threat = counterDmg;
                if (counterDmg >= ourAction.Unit.HP) threat += GetPieceValue(ourAction.Unit) * 0.5f;
                if (threat > worstCase) worstCase = threat;
            }
        }
        return worstCase;
    }

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
                if (dist <= EstimateAttackRange(unit) + 2f)
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

    float EstimatePositionalGain(AIAction action, AIBoardState board)
    {
        if (action.Unit == null) return 0f;
        float gain = 0f;
        Vector3 from = action.Unit.transform.position;
        Vector3 to = action.TargetPos;
        foreach (var pu in board.AlivePlayerUnits)
        {
            if (pu == null || !pu.gameObject.activeInHierarchy) continue;
            float dB = Vector3.Distance(from, pu.transform.position);
            float dA = Vector3.Distance(to, pu.transform.position);
            if (dA <= 2f && dB > 2f) gain += 8f;
            else if (dA < dB) gain += (dB - dA) * 1.5f;
        }
        int counterDmg = board.EstimateCounterDamageAt(to, action.Unit);
        if (counterDmg > 0) gain -= counterDmg * 0.3f;
        float allyDist = board.GetNearestAllyDist(to, action.Unit);
        if (allyDist >= 2f && allyDist <= 4f) gain += 3f;
        else if (allyDist > 6f) gain -= 4f;
        return gain;
    }

    static Vector3 GetPositionAfterAction(AIAction action)
    {
        if (action.ActionType == AIActionType.Move
            || action.ActionType == AIActionType.Surround
            || action.ActionType == AIActionType.Support
            || action.ActionType == AIActionType.Retreat
            || action.ActionType == AIActionType.DefenseRepos)
            return action.TargetPos;
        return action.Unit != null ? action.Unit.transform.position : Vector3.zero;
    }

    static int EstimateDamage(Status attacker, Status defender)
    {
        if (attacker == null || defender == null) return 0;
        return Mathf.Max(0, 1 + (attacker.ATK / 6) + ((attacker.ATK / 2) - (defender.DEF / 4)));
    }

    static float GetPieceValue(Status unit)
    {
        if (unit == null) return 0f;
        return AIConstants.GetPieceValue(unit.kind);
    }

    static float EstimateAttackRange(Status unit)
    {
        if (unit == null) return AIConstants.AR_Default;
        return AIConstants.GetAttackRange(unit.kind);
    }
}
