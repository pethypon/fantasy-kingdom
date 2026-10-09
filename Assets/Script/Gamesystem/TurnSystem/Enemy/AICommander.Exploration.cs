using System;
using UnityEngine;

/// <summary>Persistent frontier integration stays separate from action reflection and combat selection.</summary>
public partial class AICommander
{
    public AIExplorationPlanner Exploration => _governor.Exploration;
    bool explorationWarningLogged;
    public AIExplorationState CaptureExplorationState()
    {
        try
        {
            _governor.Exploration.FlushTelemetry();
            return _governor.Exploration.CaptureState();
        }
        catch (Exception error) { WarnExploration(error); return null; }
    }
    public void RestoreExplorationState(AIExplorationState saved)
    {
        if (saved == null) return;
        try { _governor.RestoreExploration(saved); }
        catch (Exception error) { WarnExploration(error); }
    }
    void RecordExplorationAction(AIAction action, bool success)
    {
        if (action == null || _board == null) return;
        try
        {
            // Finalized board observations credit discoveries to the current turn even when reflection is disabled.
            _governor.Exploration.Update(_board, _governor.Mode == StrategicMode.EmergencyDefense);
            _governor.Exploration.RecordAction(action, success, _board);
        }
        catch (Exception error) { WarnExploration(error); }
    }
    public void EndExplorationTurn()
    {
        try { _governor.Exploration.FlushTelemetry(); }
        catch (Exception error) { WarnExploration(error); }
    }
    void WarnExploration(Exception error)
    {
        if (explorationWarningLogged) return;
        explorationWarningLogged = true;
        Debug.LogWarning("[AI探索] 探索情報の保存・記録を完了できませんでした。通常のAI処理は続行します: " + error.Message);
    }
}
