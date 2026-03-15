using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// =====================================================================
//  AIAction — 候補行動データ
// =====================================================================
public class AIAction
{
    public AIActionType ActionType;
    public Status Unit;              // 行動する駒
    public Vector3 TargetPos;        // 移動先 or 攻撃対象位置
    public Status TargetUnit;        // 攻撃対象（あれば）
    public int APCost;               // 消費AP
    public float Score;              // 最終評価点

    public override string ToString()
        => $"{ActionType}({Unit?.kind}) → {TargetPos} score={Score:F1}";
}

// =====================================================================
//  AIActionEvaluator — 行動評価計算
//  最終評価 = 基本評価 + 大きい性格補正 + 細かい性格補正 + 局面補正 + 学習補正
// =====================================================================
public static class AIActionEvaluator
{
    // ---- 全候補行動を生成・評価してスコア順に返す ----
    public static List<AIAction> EvaluateAll(
        AIPersonality personality,
        AIBoardState board,
        AILearning learning)
    {
        var actions = new List<AIAction>();

        // 全敵駒について候補行動を生成
        foreach (var unit in board.AliveEnemyUnits)
        {
            if (unit == null || !unit.gameObject.activeInHierarchy) continue;
            if (unit.type != Type.Unit) continue;
            if (StatusEffectSystem.IsStunned(unit)) continue;

            GenerateMoveCandidates(unit, board, actions);
            GenerateAttackCandidates(unit, board, actions);
            GenerateWaitCandidate(unit, board, actions);
        }

        // 各候補にスコア付け
        foreach (var action in actions)
        {
            action.Score = CalcScore(action, personality, board, learning);
        }

        // スコア降順
        actions.Sort((a, b) => b.Score.CompareTo(a.Score));
        return actions;
    }

    // ================================================================
    //  候補生成: 移動
    // ================================================================
    static void GenerateMoveCandidates(Status unit, AIBoardState board, List<AIAction> results)
    {
        if (StatusEffectSystem.IsMovementBlocked(unit)) return;

        var moves = board.GetValidMoves(unit);
        foreach (var dest in moves)
        {
            int cost = board.CalcMoveCost(unit, dest);
            if (cost > board.EnemyAP) continue;

            results.Add(new AIAction
            {
                ActionType = AIActionType.Move,
                Unit = unit,
                TargetPos = dest,
                APCost = cost
            });
        }
    }

    // ================================================================
    //  候補生成: 攻撃
    // ================================================================
    static void GenerateAttackCandidates(Status unit, AIBoardState board, List<AIAction> results)
    {
        var targets = board.GetAttackTargets(unit);
        foreach (var target in targets)
        {
            int cost = board.CalcAttackCost(unit);
            if (cost > board.EnemyAP) continue;

            results.Add(new AIAction
            {
                ActionType = AIActionType.Attack,
                Unit = unit,
                TargetPos = target.transform.position,
                TargetUnit = target,
                APCost = cost
            });
        }
    }

    // ================================================================
    //  候補生成: 待機
    // ================================================================
    static void GenerateWaitCandidate(Status unit, AIBoardState board, List<AIAction> results)
    {
        results.Add(new AIAction
        {
            ActionType = AIActionType.Wait,
            Unit = unit,
            TargetPos = unit.transform.position,
            APCost = 0
        });
    }

    // ================================================================
    //  スコア計算
    //  最終評価 = 基本評価 + 大きい性格補正 + 細かい性格補正 + 局面補正 + 学習補正
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
            case AIActionType.Wait:
                return 1f; // 待機は最低評価
            default:
                return 5f;
        }
    }

    static float CalcAttackBaseScore(AIAction action, AIBoardState board)
    {
        if (action.TargetUnit == null) return 0f;

        float score = 30f; // 攻撃の基本価値

        // 撃破期待値: 倒せそうなら大幅加点
        int expectedDmg = EstimateDamage(action.Unit, action.TargetUnit);
        if (expectedDmg >= action.TargetUnit.HP)
            score += 40f; // 撃破可能

        // HP比率が低い敵ほど価値が高い
        if (action.TargetUnit.MaxHP > 0)
        {
            float hpRatio = (float)action.TargetUnit.HP / action.TargetUnit.MaxHP;
            score += (1f - hpRatio) * 15f;
        }

        // Crystal/King を攻撃できるなら最優先
        if (action.TargetUnit.kind == Kind.Crystal)
            score += 50f;
        if (action.TargetUnit.kind == Kind.King)
            score += 35f;

        // シールド中の対象は価値低
        if (action.TargetUnit.ShieldTurns > 0)
            score -= 30f;

        return score;
    }

    static float CalcMoveBaseScore(AIAction action, AIBoardState board)
    {
        float score = 10f;

        Vector3 unitPos = action.Unit.transform.position;
        Vector3 dest = action.TargetPos;

        // プレイヤークリスタルへの接近度
        float distBefore = Vector3.Distance(unitPos, board.PlayerCrystalPos);
        float distAfter = Vector3.Distance(dest, board.PlayerCrystalPos);
        float approach = distBefore - distAfter;
        score += approach * 3f;

        // 最寄りの敵ユニットへの接近
        float nearestPlayerDist = GetNearestPlayerDist(dest, board);
        if (nearestPlayerDist < 3f)
            score += 5f; // 攻撃圏に入れるなら加点

        // 高台ボーナス
        if (dest.y > unitPos.y)
            score += 2f;

        return score;
    }

    // ---- 大きい性格補正 ----
    static float CalcMajorBonus(AIAction action, AIPersonality p, AIBoardState board)
    {
        float bonus = 0f;

        switch (p.Major)
        {
            case MajorPersonality.Combat:
                // 攻撃・前進を重視
                if (action.ActionType == AIActionType.Attack)
                    bonus += 15f;
                if (action.ActionType == AIActionType.Move)
                {
                    float approach = GetApproachToEnemy(action, board);
                    bonus += approach * 5f;
                }
                if (action.ActionType == AIActionType.Wait)
                    bonus -= 5f;
                break;

            case MajorPersonality.Intellect:
                // 防衛・整形を重視
                if (action.ActionType == AIActionType.Attack)
                    bonus += 5f;
                if (action.ActionType == AIActionType.Move)
                {
                    // 味方との距離が近いなら加点（連携重視）
                    float allyDist = GetNearestAllyDist(action.TargetPos, action.Unit, board);
                    if (allyDist < 4f)
                        bonus += 8f;
                    // 自軍クリスタルから離れすぎない
                    float crystalDist = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
                    if (crystalDist > 10f)
                        bonus -= 5f;
                }
                break;

            case MajorPersonality.Adaptive:
                // 局面補正に委ねる（ここでは中立）
                if (action.ActionType == AIActionType.Attack)
                    bonus += 8f;
                break;

            case MajorPersonality.Growth:
                // 基本は中庸、学習補正に委ねる
                if (action.ActionType == AIActionType.Attack)
                    bonus += 10f;
                break;
        }

        return bonus;
    }

    // ---- 細かい性格補正 ----
    static float CalcTraitBonus(AIAction action, AIPersonality p, AIBoardState board)
    {
        float bonus = 0f;

        switch (action.ActionType)
        {
            case AIActionType.Attack:
                // 執着性: 攻撃への意欲
                bonus += p.ObsessionRate * 20f;
                // 慎重性: 不利交換なら減点
                if (action.TargetUnit != null)
                {
                    int myDmg = EstimateDamage(action.Unit, action.TargetUnit);
                    int counterDmg = EstimateDamage(action.TargetUnit, action.Unit);
                    if (counterDmg > myDmg)
                        bonus -= p.CautionRate * 25f; // 慎重なら不利交換を避ける
                }
                break;

            case AIActionType.Move:
                // 戦術性: 側面・背後への回り込みを評価
                bonus += CalcTacticalMoveBonus(action, p, board);
                // 防衛性: 自軍中枢から離れすぎる移動を減点
                float distFromBase = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
                if (distFromBase > 8f)
                    bonus -= p.DefenseRate * 15f;
                // 指揮性: 味方との連携
                float allyDist = GetNearestAllyDist(action.TargetPos, action.Unit, board);
                if (allyDist < 3f)
                    bonus += p.CommandRate * 12f;
                else if (allyDist > 6f)
                    bonus -= p.CommandRate * 10f; // 孤立を嫌う
                // 慎重性: 危険位置への移動回避
                float dangerDist = GetNearestPlayerDist(action.TargetPos, board);
                if (dangerDist < 2f)
                    bonus -= p.CautionRate * 10f;
                break;

            case AIActionType.Retreat:
                bonus += p.CautionRate * 20f;
                bonus += p.DefenseRate * 10f;
                break;

            case AIActionType.Wait:
                bonus += p.CautionRate * 3f;
                bonus -= p.ObsessionRate * 5f;
                break;
        }

        // 発展性: 将来投資（建築・サブクリ系）
        if (action.ActionType == AIActionType.Build ||
            action.ActionType == AIActionType.SubCrystal)
        {
            bonus += p.DevelopRate * 25f;
        }

        return bonus;
    }

    // ---- 局面補正（変動型に特に影響） ----
    static float CalcSituationBonus(AIAction action, AIPersonality p, AIBoardState board)
    {
        float bonus = 0f;
        float advantageRatio = board.GetAdvantageRatio();

        if (p.Major == MajorPersonality.Adaptive)
        {
            // 有利時: 攻勢寄り
            if (advantageRatio > 0.2f)
            {
                if (action.ActionType == AIActionType.Attack)
                    bonus += 12f;
                if (action.ActionType == AIActionType.Move)
                    bonus += GetApproachToEnemy(action, board) * 4f;
            }
            // 不利時: 防衛寄り
            else if (advantageRatio < -0.2f)
            {
                if (action.ActionType == AIActionType.Retreat)
                    bonus += 15f;
                if (action.ActionType == AIActionType.Move)
                {
                    float retreatValue = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
                    if (retreatValue < 5f)
                        bonus += 10f; // 自陣に寄せる
                }
            }
        }

        // 全性格共通: クリスタル危機時は防衛優先
        if (board.EnemyCrystalHP < board.EnemyCrystalMaxHP * 0.5f)
        {
            if (action.ActionType == AIActionType.Move)
            {
                float crystalDist = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
                if (crystalDist < 3f)
                    bonus += 20f; // クリスタル防衛
            }
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
        Vector3 from = action.Unit.transform.position;
        Vector3 to = action.TargetPos;
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
        foreach (var au in board.AliveEnemyUnits)
        {
            if (au == null || !au.gameObject.activeInHierarchy) continue;
            if (au == self) continue;
            float d = Vector3.Distance(pos, au.transform.position);
            if (d < nearest) nearest = d;
        }
        return nearest;
    }

    static float CalcTacticalMoveBonus(AIAction action, AIPersonality p, AIBoardState board)
    {
        float bonus = 0f;
        // 敵の背後や側面に回れるポジションを評価
        foreach (var pu in board.AlivePlayerUnits)
        {
            if (pu == null || !pu.gameObject.activeInHierarchy) continue;
            float dist = Vector3.Distance(action.TargetPos, pu.transform.position);
            if (dist < 2f)
            {
                // 攻撃圏に入れる位置で、正面以外なら戦術加点
                Vector3 diff = action.TargetPos - pu.transform.position;
                bool isFlanking = Mathf.Abs(diff.x) > Mathf.Abs(diff.z);
                if (isFlanking)
                    bonus += p.TacticsRate * 10f;
            }
        }
        return bonus;
    }
}
