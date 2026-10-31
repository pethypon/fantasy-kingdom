using System.Collections.Generic;

/// <summary>Compatibility entry points for the retired engineer. Ordinary mine
/// investment is handled by the shared construction and economy planner.</summary>
public static partial class AIActionGenerator
{
    public static bool HasRareOreSupplyGap(AIBoardState board) => false;
    public static void GenerateEngineerBuildCandidates(AIBoardState board, List<AIAction> results) { }
    public static bool IsEngineerSupplyProgress(AIAction action, AIBoardState board) => false;
}
