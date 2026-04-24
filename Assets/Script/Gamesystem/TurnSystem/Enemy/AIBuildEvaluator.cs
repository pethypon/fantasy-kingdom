using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  AIBuildEvaluator — 建築・召喚・サブクリスタルの基本評価
//  AIActionEvaluator から分離。
// =====================================================================
static class AIBuildEvaluator
{
    const int TurnEarlyEnd = AIConstants.TurnEarlyEnd;
    const int TurnMidEnd   = AIConstants.TurnMidEnd;
    const int TurnProductionBoost = AIConstants.TurnProductionBoost;
    const float ProductionBoostScore = 55f;
    const int TurnLateBuildBoost = AIConstants.TurnLateBuildBoost;
    const float LateBuildBoostScore = 120f;
    const float DuplicatePenaltyFactor = 15f;

    internal static float CalcSubCrystalBaseScore(AIAction action, AIBoardState board)
    {
        float score = 22f;

        float distFromHome = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
        if (distFromHome < 8f) score += 10f;

        if (board.CanUsePlayerCrystalAsTarget())
        {
            float distToEnemy = Vector3.Distance(action.TargetPos, board.PlayerCrystalPos);
            score += Mathf.Max(0, 15f - distToEnemy);
        }

        return score;
    }

    internal static float CalcBuildBaseScore(AIAction action, AIBoardState board)
    {
        float score = 15f;
        var facility = action.Facility;
        int turn = board.TurnCount;

        float scarcityBonus = CalcScarcityBonus(facility, board);
        score += scarcityBonus;

        switch (facility)
        {
            case FacilityKind.Well:
                score += AIActionEvaluator.PhaseScore(turn, 40f, 12f, 5f);
                if (board.GetBuildingCount(FacilityKind.Well) == 0)
                {
                    score += 40f;
                    if (board.EnemyResources != null)
                        score += AIActionEvaluator.ResourceEmergencyBonus(board.EnemyResources.Water, 50f, 30f, 15f);
                }
                break;

            case FacilityKind.LoggingCamp:
                score += AIActionEvaluator.PhaseScore(turn, 35f, 12f, 5f);
                if (board.GetBuildingCount(FacilityKind.LoggingCamp) == 0)
                {
                    score += 40f;
                    if (board.EnemyResources != null)
                        score += AIActionEvaluator.ResourceEmergencyBonus(board.EnemyResources.Wood, 50f, 30f, 15f);
                }
                break;

            case FacilityKind.Quarry:
                score += AIActionEvaluator.PhaseScore(turn, 30f, 12f, 5f);
                if (board.GetBuildingCount(FacilityKind.Quarry) == 0)
                {
                    score += 30f;
                    if (board.EnemyResources != null)
                        score += AIActionEvaluator.ResourceEmergencyBonus(board.EnemyResources.Stone, 40f, 20f, 10f);
                }
                break;

            case FacilityKind.Field:
                score += AIActionEvaluator.PhaseScore(turn, 32f, 14f, 5f);
                if (board.GetBuildingCount(FacilityKind.Field) == 0) score += 25f;
                if (board.EnemyResources != null && board.EnemyResources.Bread <= 10)
                    score += 20f;
                break;

            case FacilityKind.Mine:
                score += AIActionEvaluator.PhaseScore(turn, 20f, 22f, 12f);
                if (turn >= TurnProductionBoost) score += ProductionBoostScore;
                if (board.GetBuildingCount(FacilityKind.Mine) == 0) score += 25f;
                if (board.EnemyResources != null)
                {
                    if (board.EnemyResources.Iron <= 5) score += 18f;
                    if (board.EnemyResources.MagicOre <= 5) score += 12f;
                }
                break;

            case FacilityKind.Bakery:
                score += AIActionEvaluator.PhaseScore(turn, 35f, 25f, 12f);
                if (board.GetBuildingCount(FacilityKind.Bakery) == 0 &&
                    board.GetBuildingCount(FacilityKind.Field) > 0) score += 40f;
                if (board.EnemyResources != null && board.EnemyResources.Bread < 20)
                    score += 25f;
                break;

            case FacilityKind.House:
                score += AIActionEvaluator.PhaseScore(turn, 35f, 25f, 15f);
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
                score += AIActionEvaluator.PhaseScore(turn, 2f, 15f, 12f);
                if (board.GetBuildingCount(FacilityKind.Warehouse) == 0 && turn >= 10)
                    score += 18f;
                break;

            case FacilityKind.Barracks:
                score += AIActionEvaluator.PhaseScore(turn, 3f, 15f, 20f);
                if (board.GetBuildingCount(FacilityKind.Barracks) == 0 && turn >= 12)
                    score += 20f;
                break;

            case FacilityKind.WoodWall:
            case FacilityKind.StoneWall:
                score += AIActionEvaluator.PhaseScore(turn, 3f, 8f, 15f);
                if (board.EnemyCrystalHP < board.EnemyCrystalMaxHP * 0.5f)
                    score += 20f;
                if (AIActionEvaluator.CalcCoreEconomyCount(board) < 4)
                    score -= 30f;
                break;

            case FacilityKind.Mortar:
            case FacilityKind.Cannon:
                score += AIActionEvaluator.PhaseScore(turn, 2f, 10f, 18f);
                break;

            case FacilityKind.RestraintTrap:
            case FacilityKind.SpikeTrap:
                score += AIActionEvaluator.PhaseScore(turn, 3f, 10f, 15f);
                break;

            case FacilityKind.HeroSword:
                score += turn > TurnMidEnd ? 20f : 2f;
                break;
        }

        if (FacilityData.IsSubCrystal(facility))
            score += 15f;

        int existingCount = board.GetBuildingCount(facility);
        if (existingCount > 0)
            score -= existingCount * existingCount * DuplicatePenaltyFactor;

        score += CalcProcessingOverstockBonus(facility, board);

        var chainDeficits = board.DiagnoseProductionChainDeficit();
        for (int i = 0; i < chainDeficits.Count; i++)
        {
            if (chainDeficits[i] == facility)
            {
                score += Mathf.Max(5f, 30f - i * 6f);
                break;
            }
        }

        if (turn >= TurnLateBuildBoost)
        {
            bool isMilitary = FacilityData.IsWall(facility) || FacilityData.IsOffensive(facility);

            if (existingCount == 0 && !isMilitary)
                score += LateBuildBoostScore + 80f;
            else if (existingCount == 0)
                score += LateBuildBoostScore;
            else
                score += LateBuildBoostScore * 0.5f;

            switch (facility)
            {
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

    static float CalcProcessingOverstockBonus(FacilityKind facility, AIBoardState board)
    {
        if (board.EnemyResources == null) return 0f;
        var res = board.EnemyResources;

        switch (facility)
        {
            case FacilityKind.Bakery:
                return (res.Wheat > 30 && res.Bread < 20) ? 40f : 0f;
            default:
                return 0f;
        }
    }

    static float CalcScarcityBonus(FacilityKind facility, AIBoardState board)
    {
        float bonus = 0f;
        switch (facility)
        {
            case FacilityKind.Well:
                bonus += board.GetResourceScarcity("Water") * 30f;
                if (board.GetBuildingCount(FacilityKind.Well) == 0) bonus += 20f;
                break;
            case FacilityKind.LoggingCamp:
                bonus += board.GetResourceScarcity("Wood") * 30f;
                if (board.GetBuildingCount(FacilityKind.LoggingCamp) == 0) bonus += 20f;
                break;
            case FacilityKind.Quarry:
                bonus += board.GetResourceScarcity("Stone") * 28f;
                if (board.GetBuildingCount(FacilityKind.Quarry) == 0) bonus += 15f;
                break;
            case FacilityKind.Field:
                bonus += board.GetResourceScarcity("Wheat") * 15f;
                break;
            case FacilityKind.Mine:
                bonus += board.GetResourceScarcity("Iron") * 20f;
                bonus += board.GetResourceScarcity("MagicOre") * 12f;
                break;
            case FacilityKind.Bakery:
                bonus += board.GetResourceScarcity("Bread") * 22f;
                break;
        }
        return bonus;
    }

    internal static float CalcSummonBaseScore(AIAction action, AIBoardState board)
    {
        float score = 30f;

        int rawCount = AIActionEvaluator.CalcRawFacilityCount(board);
        int procCount = AIActionEvaluator.CalcProcessingFacilityCount(board);
        float infraScore = rawCount + procCount * 2f;

        bool hasBakery = board.GetBuildingCount(FacilityKind.Bakery) > 0;
        if (infraScore < 3f)
            score -= 120f;
        else if (infraScore < 5f)
            score -= 60f;
        else if (!hasBakery)
            score -= 40f;
        else if (infraScore < 7f)
            score += 0f;
        else
            score += 20f;

        int allyCount = board.AliveEnemyUnits.Count;
        if (allyCount <= 2) score += 35f;
        else if (allyCount <= 4) score += 25f;
        else if (allyCount <= 6) score += 15f;
        else score += 5f;

        if (board.CanUsePlayerCrystalAsTarget())
        {
            float dist = Vector3.Distance(action.TargetPos, board.PlayerCrystalPos);
            score += Mathf.Max(0, 15f - dist);
        }

        if (action.SummonKind == Kind.Scout && board.AlivePlayerUnits.Count == 0)
        {
            float explorationRatio = board.GetExplorationRatio();
            if (explorationRatio < 0.5f)
                score += 20f;
            else if (explorationRatio < 0.7f)
                score += 10f;
        }

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
}
