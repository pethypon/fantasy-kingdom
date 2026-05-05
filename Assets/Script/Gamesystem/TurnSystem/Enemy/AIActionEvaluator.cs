using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  AIActionEvaluator — 行動評価コーディネーター（partial分割）
//
//  責務分割:
//    AIActionEvaluator.cs              … コーディネーター / 公開API
//    AIActionEvaluator.Combat.cs       … 戦闘系スコアディスパッチ
//    AIActionEvaluator.Economy.cs      … 経済/建築ヘルパー
//    AIActionEvaluator.Positioning.cs  … 局面/位置補正
//    AIActionEvaluator.Personality.cs  … 性格補正集約
//
//  最終評価 = 基本評価 + 大きい性格補正 + 細かい性格補正 + 局面補正 + 学習補正
// =====================================================================
public static partial class AIActionEvaluator
{
    const int TurnEarlyEnd = AIConstants.TurnEarlyEnd;
    const int TurnMidEnd   = AIConstants.TurnMidEnd;

    /// <summary>ターンに応じたフェーズ別スコアを返す</summary>
    internal static float PhaseScore(int turn, float early, float mid, float late)
    {
        if (turn <= TurnEarlyEnd) return early;
        if (turn <= TurnMidEnd)   return mid;
        return late;
    }

    // ================================================================
    //  全候補行動を生成・評価してスコア順に返す
    // ================================================================
    public static List<AIAction> EvaluateAll(
        AIPersonality personality,
        AIBoardState board,
        AILearning learning,
        TurnStrategy strategy = TurnStrategy.Balanced)
    {
        var actions = new List<AIAction>();
        EvaluateAllInto(actions, personality, board, learning, strategy);
        return actions;
    }

    /// <summary>
    /// 既存の List を再利用して候補を生成・スコア付けする。
    /// AICommander.ExecuteTurn のホットループから毎回 new List を避けるための公開オーバーロード。
    /// 入力 List は冒頭で Clear される。
    /// </summary>
    public static void EvaluateAllInto(
        List<AIAction> actions,
        AIPersonality personality,
        AIBoardState board,
        AILearning learning,
        TurnStrategy strategy = TurnStrategy.Balanced)
    {
        if (actions == null) return;
        actions.Clear();

        // 候補生成は AIActionGenerator に委譲
        AIActionGenerator.GenerateAllCandidates(board, actions);

        // 各候補にスコア付け
        for (int i = 0; i < actions.Count; i++)
            actions[i].Score = CalcScore(actions[i], personality, board, learning);

        // ターン方針ボーナス（AIStrategyBonusに委譲）
        AIStrategyBonus.Apply(actions, strategy, board);

        // 事後補正は AIActionModifiers に委譲（Positioning パーシャルに集約）
        ApplyPositioningModifiers(actions, personality, board);

        // スコア降順
        actions.Sort((a, b) => b.Score.CompareTo(a.Score));
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
