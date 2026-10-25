#if UNITY_EDITOR
using System;
using UnityEngine;

/// <summary>Real governor facts versus the legacy plan's independent proximity/HP veto.</summary>
public static class AIR3CommandAuthorityTests
{
    static int passed;
    static void Check(string label, bool ok)
    { if (!ok) throw new InvalidOperationException("[AIR3CommandAuthority] FAIL " + label); passed++; Debug.Log("[AIR3CommandAuthority] PASS " + label); }
    public static void Run(GameSystems live)
    {
        passed = 0;
        foreach (var team in new[] { Team.Player, Team.Enemy })
        {
            using var f = new AITerritorySafetyTests.Fixture(live, team);
            f.Economy.Resources.Citizen = 20;
            f.Economy.Crystal.HP = Mathf.Max(1, f.Economy.Crystal.MaxHP / 3);
            var board = f.Board(60); board.ReconThreatLevel = 15;
            Check(team + " low historical crystal HP alone has no current observed attack", board.Governor.Mode != StrategicMode.EmergencyDefense
                && board.Governor.IncomingDamage(board, f.Economy.Crystal, f.Economy.Crystal.transform.position, null) == 0);
            Check(team + " legacy operation cannot invent an emergency that cancels the governor's orders", !AIPlanManager.Emergency(board));
            f.Foe.transform.position = f.Cells[1]; f.Foe.ATK = 5000; f.See(f.Cells[1]);
            board = f.Board(61);
            Check(team + " a real observed lethal base threat still interrupts operations", board.Governor.Mode == StrategicMode.EmergencyDefense
                && AIPlanManager.Emergency(board));
            // Standalone legacy callers retain their documented conservative fallback.
            board.Governor = null;
            Check(team + " standalone callers retain the low-HP emergency fallback", AIPlanManager.Emergency(board));
        }
        Debug.Log("[AIR3CommandAuthority] ALL PASSED " + passed);
    }
}
#endif
