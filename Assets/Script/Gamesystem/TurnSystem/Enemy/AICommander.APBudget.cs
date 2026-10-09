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
        if (_board == null || _governor.Mode == StrategicMode.EmergencyDefense) return 0;
        // An offensive objective must not suppress an affordable, necessary supply recovery.
        // No AP is reserved for an impossible project; the army can still scout or fight.
        return _governor.GetRecoveryReserveAP(_board);
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
                || action.ActionType == AIActionType.Upgrade
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
                action.Score -= penalty;
            }


        }
    }
}
