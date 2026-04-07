using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  AICommander.APBudget — AP予約・建築需要算出・予約ペナルティ
// =====================================================================
public partial class AICommander
{
    // ================================================================
    //  AP予約計算: 建築/召喚用にAPを確保する
    //  移動でAPを使い果たして建築/召喚できなくなるのを防ぐ
    // ================================================================
    int CalcReservedAP()
    {
        if (_board == null) return 0;

        // TurnStrategyPlannerが計画したAP予約があればそれを基準にする
        int reserved = _apBudget.ReservedAP;

        // 追加チェック: 建てるべき建物のAPコストも考慮
        if (_board.BuildablePositions.Count > 0)
        {
            int cheapestNeeded = GetCheapestNeededBuildAP();
            if (cheapestNeeded > 0)
                reserved = Mathf.Max(reserved, cheapestNeeded);

            if (_board.AffordableBuildings.Count > 0)
            {
                int cheapestBuild = int.MaxValue;
                foreach (var fk in _board.AffordableBuildings)
                {
                    if (FacilityData.Table.TryGetValue(fk, out var info))
                        cheapestBuild = Mathf.Min(cheapestBuild, info.APCost);
                }
                if (cheapestBuild < int.MaxValue)
                    reserved = Mathf.Max(reserved, cheapestBuild);
            }
        }

        // 召喚可能なら召喚コストも考慮
        if (_board.SummonablePositions.Count > 0 && _board.AffordableUnits.Count > 0)
        {
            int cheapestSummon = int.MaxValue;
            foreach (var k in _board.AffordableUnits)
            {
                if (UnitStaticData.Table.TryGetValue(k, out var info))
                    cheapestSummon = Mathf.Min(cheapestSummon, info.CostAP);
            }
            if (cheapestSummon < int.MaxValue)
                reserved = Mathf.Max(reserved, cheapestSummon);
        }

        return reserved;
    }

    /// <summary>
    /// 経済状況に応じて「建てるべき建物」の最安APコストを返す。
    /// AffordableBuildings と独立して、施設不足を診断しAPを確保する。
    /// </summary>
    int GetCheapestNeededBuildAP()
    {
        if (_board.EnemyResources == null) return 0;

        int cheapest = int.MaxValue;

        // 原料施設が4棟未満 → 安い原料施設のAP分を予約
        int rawCount = EconomyHelper.CountEconBuildings(_board);
        if (rawCount < 4)
        {
            // Well(3), Field(3), LoggingCamp(4), Quarry(4)
            cheapest = Mathf.Min(cheapest, 3);
        }

        // 加工施設が0 → 加工施設のAP分を予約
        int procCount = EconomyHelper.CountProcessingBuildings(_board);
        if (procCount == 0 && rawCount >= 2)
        {
            // LumberMill(6), StoneWorks(6), Bakery(5)
            cheapest = Mathf.Min(cheapest, 5);
        }

        // 住宅なし & 市民不足
        if (_board.GetBuildingCount(FacilityKind.House) == 0
            && _board.EnemyResources.Citizen <= 1)
        {
            cheapest = Mathf.Min(cheapest, 7); // House costs 7 AP
        }

        return cheapest < int.MaxValue ? cheapest : 0;
    }

    // ================================================================
    //  AP予約ペナルティ: 移動系が予約APを食い込む場合に減点
    // ================================================================
    void ApplyAPReservationPenalty(List<AIAction> actions, int reservedAP)
    {
        if (reservedAP <= 0) return;

        // 経済が未成熟かどうかで重みを変える
        bool econWeak = !EconomyHelper.IsEconomySufficient(_board);

        foreach (var action in actions)
        {
            // 建築・召喚・サブクリスタルは予約対象なのでペナルティなし
            if (action.ActionType == AIActionType.Build
                || action.ActionType == AIActionType.Summon
                || action.ActionType == AIActionType.SubCrystal)
                continue;

            int apAfterAction = _board.EnemyAP - action.APCost;

            // 攻撃は高価値なので軽いペナルティのみ
            if (action.ActionType == AIActionType.Attack
                || action.ActionType == AIActionType.SkillUse)
            {
                if (apAfterAction < reservedAP)
                    action.Score -= 8f;
                continue;
            }

            // 移動系: AP予約を食い込む場合は強く減点
            if (apAfterAction < reservedAP)
            {
                // 経済未成熟時は非常に強いペナルティ（建築を移動より優先させる）
                float penalty = econWeak ? 60f : 20f;
                // 30ターン以降は更に厳しく
                if (_turnCount >= 30 && econWeak) penalty = 100f;
                action.Score -= penalty;
            }

            // 経済未成熟時: AP予約に関係なく全移動を減点（建築の相対的優位を確保）
            if (econWeak && _turnCount >= 10)
            {
                action.Score -= 15f;
                if (_turnCount >= 30) action.Score -= 30f;
            }
        }
    }
}
