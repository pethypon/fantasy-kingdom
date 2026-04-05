using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  AIStrategyBonus — 戦略別ボーナス・ペナルティの適用
//  AIActionEvaluator から分離: 各TurnStrategy専用のスコア補正ロジック
//
//  責務: 「今ターンの方針」に合う行動にボーナスを付与する
//  入力: 候補行動リスト + 戦略 + 盤面
//  出力: 各行動のScoreに戦略ボーナスを加算
// =====================================================================
public static class AIStrategyBonus
{
    // ================================================================
    //  エントリポイント: 全候補に戦略ボーナスを適用
    // ================================================================
    public static void Apply(List<AIAction> actions, TurnStrategy strategy, AIBoardState board)
    {
        foreach (var action in actions)
        {
            action.Score += CalcBonus(action, strategy, board);
            ApplyLateTurnBuildBoost(action, board);
        }
    }

    static float CalcBonus(AIAction a, TurnStrategy strategy, AIBoardState board)
    {
        switch (strategy)
        {
            case TurnStrategy.Assault:        return AssaultBonus(a, board);
            case TurnStrategy.CrystalDefense: return CrystalDefenseBonus(a, board);
            case TurnStrategy.RetreatRegroup: return RetreatRegroupBonus(a, board);
            case TurnStrategy.EconomyBuild:   return EconomyBuildBonus(a, board);
            case TurnStrategy.Balanced:       return BalancedBonus(a, board);
            case TurnStrategy.ScoutSearch:    return ScoutSearchBonus(a, board);
            case TurnStrategy.ContactEngage:  return ContactEngageBonus(a, board);
            default:                          return 0f;
        }
    }

    // ================================================================
    //  各戦略のボーナス計算
    // ================================================================

    static float AssaultBonus(AIAction a, AIBoardState board)
    {
        float b = 0f;
        if (a.ActionType == AIActionType.Attack) b += 18f;
        if (a.ActionType == AIActionType.SkillUse && a.Skill != null && a.Skill.Multiplier > 0) b += 15f;
        if (a.ActionType == AIActionType.Surround) b += 12f;
        if (a.ActionType == AIActionType.Move)
            b += AIEvalHelpers.GetApproachToEnemy(a, board) * 4f;
        if (a.ActionType == AIActionType.Retreat) b -= 10f;
        if (a.ActionType == AIActionType.Build) b -= 5f;
        return b;
    }

    static float CrystalDefenseBonus(AIAction a, AIBoardState board)
    {
        float b = 0f;
        if (a.ActionType == AIActionType.DefenseRepos) b += 22f;
        if (a.ActionType == AIActionType.Move && a.Unit != null)
        {
            if (a.Unit.kind == Kind.Scout)
            {
                int scoutNewCells = board.EstimateNewVisionCells(a.TargetPos);
                if (scoutNewCells > 3) b += 15f;
            }
            else
            {
                float dist = Vector3.Distance(a.TargetPos, board.EnemyCrystalPos);
                if (dist < 4f) b += 18f;
                else if (dist < 6f) b += 8f;
            }
        }
        if (a.ActionType == AIActionType.Build && FacilityData.IsWall(a.Facility)) b += 15f;
        if (a.ActionType == AIActionType.Attack && a.TargetUnit != null)
        {
            float tDist = Vector3.Distance(a.TargetUnit.transform.position, board.EnemyCrystalPos);
            if (tDist < 5f) b += 15f;
        }
        if ((a.ActionType == AIActionType.Surround || a.ActionType == AIActionType.Move)
            && a.Unit != null && a.Unit.kind != Kind.Scout)
        {
            float destDist = Vector3.Distance(a.TargetPos, board.EnemyCrystalPos);
            if (destDist > 8f) b -= 12f;
        }
        return b;
    }

    static float RetreatRegroupBonus(AIAction a, AIBoardState board)
    {
        float b = 0f;
        if (a.ActionType == AIActionType.Retreat) b += 20f;
        if (a.ActionType == AIActionType.Support) b += 15f;
        if (a.ActionType == AIActionType.SkillUse && a.Skill != null && a.Skill.FixedHeal > 0) b += 18f;
        if (a.ActionType == AIActionType.SkillUse && a.Skill != null && a.Skill.GrantBuff == BuffType.Defensive) b += 10f;
        if (a.ActionType == AIActionType.Attack) b -= 8f;
        if (a.ActionType == AIActionType.Surround) b -= 12f;
        return b;
    }

    static float EconomyBuildBonus(AIAction a, AIBoardState board)
    {
        float b = 0f;
        int coreCount = EconomyHelper.CalcCoreEconomyCount(board);

        if (a.ActionType == AIActionType.Build)
        {
            b += 30f;
            if (coreCount < 5)
                b += EconomyHelper.IsMissingCoreFacility(a.Facility, board) ? 40f : -15f;
            b += CalcChainDeficitBonus(a.Facility, board, 50f, 10f);
        }
        if (a.ActionType == AIActionType.SubCrystal) b += 15f;
        if (a.ActionType == AIActionType.Summon)
        {
            bool hasBakery = board.GetBuildingCount(FacilityKind.Bakery) > 0;
            b += (coreCount >= 5 && hasBakery) ? 10f : -40f;
        }
        if (a.ActionType == AIActionType.Attack)   b -= 10f;
        if (a.ActionType == AIActionType.Move)     b -= 8f;
        if (a.ActionType == AIActionType.Surround) b -= 8f;
        if (a.ActionType == AIActionType.Retreat)  b -= 5f;
        return b;
    }

    static float BalancedBonus(AIAction a, AIBoardState board)
    {
        float b = 0f;
        int coreEconCount = EconomyHelper.CalcCoreEconomyCount(board);
        bool econEstablished = coreEconCount >= 5;

        if (a.ActionType == AIActionType.Summon)
        {
            bool hasBakery = board.GetBuildingCount(FacilityKind.Bakery) > 0;
            b += (econEstablished && hasBakery) ? 20f : -30f;
        }
        if (a.ActionType == AIActionType.Build)
        {
            if (!econEstablished)
            {
                b += 35f;
                if (EconomyHelper.IsMissingCoreFacility(a.Facility, board)) b += 25f;
                if (EconomyHelper.IsProcessingFacility(a.Facility) &&
                    board.GetBuildingCount(a.Facility) == 0) b += 20f;
            }
            else
            {
                b += 8f;
            }
            b += CalcChainDeficitBonus(a.Facility, board, 35f, 8f);
        }
        if (a.ActionType == AIActionType.Attack)   b += 8f;
        if (a.ActionType == AIActionType.SkillUse) b += 5f;
        if (a.ActionType == AIActionType.Move)
        {
            float moveBonus = AIEvalHelpers.GetApproachToEnemy(a, board) * 3f;
            if (!econEstablished) moveBonus *= 0.5f;
            b += moveBonus;
        }
        return b;
    }

    static float ScoutSearchBonus(AIAction a, AIBoardState board)
    {
        float b = 0f;
        if (a.ActionType == AIActionType.Move && a.Unit != null)
        {
            int newCells = board.EstimateNewVisionCells(a.TargetPos);
            if (newCells > 0)  b += Mathf.Min(newCells * 4f, 35f);
            if (a.Unit.kind == Kind.Scout) b += 20f;
            float allyDist = AIEvalHelpers.GetNearestAllyDist(a.TargetPos, a.Unit, board);
            if (allyDist >= 2f && allyDist <= 5f) b += 8f;
            else if (allyDist > 7f) b -= 10f;
        }
        if (a.ActionType == AIActionType.Attack) b += 12f;
        if (a.ActionType == AIActionType.SkillUse && a.Skill != null && a.Skill.Multiplier > 0) b += 10f;
        if (a.ActionType == AIActionType.Build)
        {
            int econCount = EconomyHelper.CalcCoreEconomyCount(board);
            b += econCount < 5 ? 10f : -5f;
        }
        if (a.ActionType == AIActionType.Wait) b -= 15f;
        return b;
    }

    static float ContactEngageBonus(AIAction a, AIBoardState board)
    {
        float b = 0f;
        if (a.ActionType == AIActionType.Attack) b += 25f;
        if (a.ActionType == AIActionType.SkillUse && a.Skill != null)
        {
            if (a.Skill.Multiplier > 0) b += 22f;
            if (a.AreaTargets != null && a.AreaTargets.Count > 1)
                b += a.AreaTargets.Count * 8f;
        }
        if (a.ActionType == AIActionType.Move)
        {
            float approach = AIEvalHelpers.GetApproachToEnemy(a, board);
            b += approach * 6f;
            float nearestEnemy = AIEvalHelpers.GetNearestPlayerDist(a.TargetPos, board);
            if (nearestEnemy <= 2f) b += 15f;
            else if (nearestEnemy <= 3.5f) b += 8f;
        }
        if (a.ActionType == AIActionType.Surround) b += 18f;
        if (a.ActionType == AIActionType.Wait) b -= 25f;
        if (a.ActionType == AIActionType.Retreat) b -= 12f;
        if (a.ActionType == AIActionType.Build) b -= 10f;
        return b;
    }

    // ================================================================
    //  共通ヘルパー
    // ================================================================

    /// <summary>生産チェーン逆算ボーナス</summary>
    static float CalcChainDeficitBonus(FacilityKind facility, AIBoardState board, float maxBonus, float decayPerRank)
    {
        var deficits = board.DiagnoseProductionChainDeficit();
        for (int i = 0; i < deficits.Count; i++)
        {
            if (deficits[i] == facility)
                return Mathf.Max(5f, maxBonus - i * decayPerRank);
        }
        return 0f;
    }

    /// <summary>30ターン以降の建築超優先ブースト</summary>
    static void ApplyLateTurnBuildBoost(AIAction action, AIBoardState board)
    {
        if (board.TurnCount < AIConstants.TurnLateBuildBoost) return;

        int coreEcon = EconomyHelper.CalcCoreEconomyCount(board);
        bool econWeak = coreEcon < 5;

        if (econWeak)
        {
            if (action.ActionType == AIActionType.Build) action.Score += 100f;
            if (action.ActionType == AIActionType.SubCrystal) action.Score += 60f;
            if (action.ActionType == AIActionType.Move
                || action.ActionType == AIActionType.Support
                || action.ActionType == AIActionType.Surround) action.Score -= 50f;
            if (action.ActionType == AIActionType.Wait) action.Score -= 100f;
        }
        else
        {
            int proc = EconomyHelper.CountProcessingBuildings(board);
            if (proc < 3 && action.ActionType == AIActionType.Build)
                action.Score += 50f;
        }
    }
}

// =====================================================================
//  AIEvalHelpers — 評価用共通ヘルパー
//  AIActionEvaluator と AIStrategyBonus の両方から使用
// =====================================================================
public static class AIEvalHelpers
{
    public static float GetApproachToEnemy(AIAction action, AIBoardState board)
    {
        if (action.Unit == null) return 0f;
        Vector3 from = action.Unit.transform.position;
        Vector3 to = action.TargetPos;

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

        if (!board.CanUsePlayerCrystalAsTarget())
        {
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
            return 0f;
        }

        float distBefore = Vector3.Distance(from, board.PlayerCrystalPos);
        float distAfter = Vector3.Distance(to, board.PlayerCrystalPos);
        return distBefore - distAfter;
    }

    public static float GetNearestPlayerDist(Vector3 pos, AIBoardState board)
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

    public static float GetNearestAllyDist(Vector3 pos, Status self, AIBoardState board)
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

    public static int EstimateDamage(Status attacker, Status defender)
    {
        int atk = attacker.ATK;
        int def = defender.DEF;
        return Mathf.Max(0, 1 + (atk / 6) + ((atk / 2) - (def / 4)));
    }
}
