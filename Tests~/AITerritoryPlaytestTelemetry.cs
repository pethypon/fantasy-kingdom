#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>Read-only observer: never generates candidates, refreshes a board, or supplies intel to the AI.</summary>
public static class AITerritoryPlaytestTelemetry
{
    static readonly FieldInfo BoardField = typeof(AICommander).GetField("_board", BindingFlags.Instance | BindingFlags.NonPublic);
    public static void Capture(AICommander commander, string directory, int round)
    {
        if (Environment.GetEnvironmentVariable("FK_TERRITORY_TELEMETRY") != "1" || commander == null) return;
        var board = BoardField.GetValue(commander) as AIBoardState;
        if (board == null) return;
        int visible = 0, army = 0;
        float homeDistance = 0, targetDistance = 0;
        var territory = board.TerritorySystem;
        foreach (var cell in board.OwnVisionCells)
            if (territory != null && territory.IsInTerritory(cell, board.OpponentTeam)) visible++;
        object intel = Property(commander.StrategicGovernor, "TerritoryIntel");
        object waypoint = Property(intel, "Waypoint");
        Vector3 target = waypoint is Vector3 p ? p : waypoint is Vector3Int c ? c : Vector3.zero;
        foreach (var actor in board.AliveEnemyUnits)
        {
            if (actor == null || !actor.IsAlive || actor.type != Type.Unit || MatchObjectiveRules.IsCommander(actor)
                || actor.kind == Kind.Scout || UnitRoster.IsDedicatedHealer(actor.kind)) continue;
            army++;
            homeDistance += GridHelper.ChebyshevDistance(actor.transform.position, board.EnemyCrystalPos);
            targetDistance += GridHelper.ChebyshevDistance(actor.transform.position, target);
        }
        string path = Path.Combine(directory, "territory-turns.csv");
        bool header = !File.Exists(path);
        using (var writer = new StreamWriter(path, true, new UTF8Encoding(false)))
        {
            if (header) writer.WriteLine("round,team,boardTurn,generation,AP,mode,strategy,visibleOpponentTerritory,rememberedCells,waypointActive,targetX,targetZ,suspended,reason,armyCount,meanHomeDistance,meanTargetDistance,principalContact,crystalVisible,crystalRemembered,planGoal,planStep,rejections,firstObservedTurn,lastConfirmedTurn,lastProgressTurn,strategyReason");
            object[] values = { round, board.ActorTeam, board.TurnCount, board.Generation, board.EnemyAP,
                commander.StrategicGovernor.Mode, commander.CurrentStrategy, visible,
                Property(intel,"ObservedCellCount") ?? -1, Property(intel,"HasWaypoint") ?? false,
                target.x, target.z, Property(intel,"Suspended") ?? false, Property(intel,"Reason") ?? "no territory subsystem",
                army, army>0 ? homeDistance/army : 0, army>0 ? targetDistance/army : 0,
                board.HasPrincipalOpponentContact, board.PlayerCrystalVisible, board.GetLastKnownPlayerCrystal().Valid,
                commander.CurrentPlan?.Goal.ToString() ?? "none", commander.CurrentPlan?.Step.ToString() ?? "none",
                commander.StrategicGovernor.Rejections.Count, Property(intel,"FirstObservedTurn") ?? -1,
                Property(intel,"LastConfirmedTurn") ?? -1, Property(intel,"LastProgressTurn") ?? -1,
                Property(commander,"CurrentStrategyReason") ?? "not_recorded_in_this_revision" };
            for (int i=0;i<values.Length;i++)
            { if(i>0) writer.Write(','); writer.Write('"'); writer.Write(Convert.ToString(values[i],CultureInfo.InvariantCulture)?.Replace("\"","\"\"")); writer.Write('"'); }
            writer.WriteLine();
        }
    }
    static object Property(object source,string name) => source?.GetType().GetProperty(name)?.GetValue(source);
}
#endif
