using System;
using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  SaveSystem.Restore — セーブデータからゲーム状態を復元
// =====================================================================
public static partial class SaveSystem
{
    // ================================================================
    //  資源・AP 復元ヘルパー
    // ================================================================

    public static void RestoreResources(ResourceSaveData src, FactionState.ResourceData dst)
    {
        dst.Wood = src.Wood; dst.Stone = src.Stone; dst.Water = src.Water;
        dst.Coal = src.Coal; dst.IronOre = src.IronOre; dst.MagicOre = src.MagicOre;
        dst.Plank = src.Plank; dst.CutStone = src.CutStone; dst.Iron = src.Iron;
        dst.Wheat = src.Wheat; dst.Bread = src.Bread; dst.Citizen = src.Citizen;
    }

    public static void RestoreAP(APSaveData src, FactionState.APData dst)
    {
        dst.Current = src.Current; dst.Reset = src.Reset;
        dst.Plus = src.Plus; dst.Minus = src.Minus;
    }

    // ================================================================
    //  タイマー復元
    // ================================================================

    public static void RestoreTimer(TimerSaveData src, TimerSystem timer)
    {
        if (src == null || timer == null) return;
        timer.PlayerTotalTime = src.PlayerTotalTime;
        timer.EnemyTotalTime = src.EnemyTotalTime;
        timer.TurnTimeLimit = src.TurnTimeLimit;
        timer.RestoreTurnTimeRemaining(src.TurnTimeRemaining);
    }

    // ================================================================
    //  霧の戦争復元
    // ================================================================

    public static void RestoreFog(FogSaveData src, VisionGenerator visionGen)
    {
        if (src == null || visionGen == null) return;

        if (visionGen.PlayerExploard == null)
            visionGen.PlayerExploard = new HashSet<Vector3Int>();
        else
            visionGen.PlayerExploard.Clear();

        if (visionGen.EnemyExploard == null)
            visionGen.EnemyExploard = new HashSet<Vector3Int>();
        else
            visionGen.EnemyExploard.Clear();

        foreach (var v in src.PlayerExplored)
            visionGen.PlayerExploard.Add(v.ToVector3Int());
        foreach (var v in src.EnemyExplored)
            visionGen.EnemyExploard.Add(v.ToVector3Int());
    }

    // ================================================================
    //  NationState追加データ復元
    // ================================================================

    public static void RestoreNationExtra(NationExtraSaveData src, NationState nation)
    {
        if (src == null || nation == null) return;
        nation.PendingReturns = new List<int>(src.PendingReturns);
        nation.StarvationCounter = src.StarvationCounter;
        nation.CitizenCapacity = src.CitizenCapacity;
        nation.ResourceCapacity = src.ResourceCapacity;
        nation.BarracksXP = src.BarracksXP;
    }

    // ================================================================
    //  AI状態復元
    // ================================================================

    public static void RestoreAILearning(AISaveData src, AILearning learning)
    {
        if (src == null || learning == null) return;

        learning.SaveCautionModifier = src.LearningCaution;
        learning.SaveCommandModifier = src.LearningCommand;
        learning.SaveTacticsModifier = src.LearningTactics;
        learning.SaveDefenseModifier = src.LearningDefense;
        learning.SaveDevelopModifier = src.LearningDevelop;

        RestoreCellCountDict(src.FailedFrontalAttacks, learning.SaveFailedFrontalAttacks);
        RestoreCellCountDict(src.SuccessFlanks, learning.SaveSuccessFlanks);
        RestoreCellCountDict(src.IsolatedDeaths, learning.SaveIsolatedDeaths);
        RestoreCellCountDict(src.PlayerDefensePositions, learning.SavePlayerDefensePositions);
        RestoreCellCountDict(src.RouteSuccess, learning.SaveRouteSuccess);
        RestoreCellCountDict(src.RouteFailure, learning.SaveRouteFailure);
    }

    public static void RestoreAICommander(AISaveData src, AICommander commander)
    {
        if (src == null || commander == null) return;

        commander.SaveTotalMoves = src.TotalMoves;
        commander.SaveTotalAttacks = src.TotalAttacks;
        commander.SaveTotalSkills = src.TotalSkills;
        commander.SaveTotalRetreats = src.TotalRetreats;
        commander.SaveTotalBuilds = src.TotalBuilds;
        commander.SaveTotalSummons = src.TotalSummons;
        commander.SaveTotalKills = src.TotalKills;
        commander.SaveTurnCount = src.AITurnCount;

        if (Enum.TryParse<TurnStrategy>(src.CurrentStrategy, out var strategy))
            commander.RestoreStrategy(strategy);

        RestoreAILearning(src, commander.Learning);
    }

    static void RestoreCellCountDict(List<CellCountData> src, Dictionary<Vector3Int, int> dst)
    {
        if (src == null || dst == null) return;
        dst.Clear();
        foreach (var entry in src)
            dst[new Vector3Int(entry.X, entry.Y, entry.Z)] = entry.Count;
    }
}
