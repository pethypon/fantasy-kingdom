/// <summary>The supported checkpoint is the stable player action phase. AI iterators are runtime state.</summary>
public static class SaveCheckpoint
{
    public static bool CanCapture(TurnGenerator turn, out string reason)
    {
        if (turn == null || !(turn.CurrentState is PlayerMove) || turn.DeveloperPlayerAIEnabled)
        { reason = "プレイヤーの操作中にセーブできます。自動操作を停止してからお試しください"; return false; }
        if (turn.Systems.BuildSystem != null && turn.Systems.BuildSystem.IsActive
            || turn.Systems.SummonSystem != null && turn.Systems.SummonSystem.IsActive)
        { reason = "建築・召喚の配置を終了してからセーブしてください"; return false; }
        reason = null; return true;
    }
}
