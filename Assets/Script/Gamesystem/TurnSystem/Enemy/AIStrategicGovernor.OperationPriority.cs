using System;

public sealed partial class AIStrategicGovernor
{
    bool AllowsWinningOperationSequence(AIAction action, AIBoardState board)
    {
        if (board.Operations == null || Mode == StrategicMode.EmergencyDefense) return false;
        try { return board.Operations.AllowsObjectiveSacrifice(action, board); }
        catch (Exception) { board.Operations = null; return false; }
    }

    int OperationPriority(AIAction action, AIBoardState board)
    {
        if (board.Operations == null || Mode == StrategicMode.EmergencyDefense || Mode == StrategicMode.EconomicRecovery) return 4;
        try { return board.Operations.SquadActionPriority(action, board); }
        catch (Exception error)
        {
            board.Operations = null;
            if (DevelopmentLog.IsEnabled(AILogLevel.Verbose))
                DevelopmentLog.Log("[AI作戦] 部隊候補を通常判断へ戻しました: " + error.GetType().Name);
            return 4;
        }
    }
}
