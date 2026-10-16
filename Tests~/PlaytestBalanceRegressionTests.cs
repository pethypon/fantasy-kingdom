#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

public static class PlaytestBalanceRegressionTests
{
    static void Check(string name, bool result)
    { if (!result) throw new InvalidOperationException("[PlaytestBalance] FAIL " + name); Debug.Log("[PlaytestBalance] PASS " + name); }
    public static void PlayMode(GameSystems systems)
    {
        var enemy = systems.UnitSetting.EnemyUnit.GetComponentsInChildren<Status>().Single(u => u.IsPrimaryCommander);
        Check("initial enemy commander uses authored 500HP and bounded growth", enemy.GrowthData != null
            && enemy.GrowthData.baseHP == 500 && UnitData.CalcGrowthPerLevel(500, enemy.GrowthData.hpGrowth) == 25);
        systems.UnitSetting.RefreshRuntimeBalance();
        Check("content refresh retains initial commander definition", enemy.GrowthData.baseHP == 500);
        Check("initial enemy commander is decisive", MatchObjectiveRules.IsDecisive(enemy));
        var saved = JsonUtility.FromJson<SaveSystem.UnitSaveData>(JsonUtility.ToJson(SaveSystem.CaptureUnit(enemy)));
        Check("save stores explicit commander identity", saved.HasPrimaryCommanderRole && saved.IsPrimaryCommander);
        var ordinary = new GameObject("ordinary Boss test"); ordinary.SetActive(false);
        try
        {
            var actor = ordinary.AddComponent<Status>(); actor.kind = Kind.Boss; actor.type = Type.Unit; actor.team = Team.Enemy;
            Check("ordinary faction Boss is not decisive", !MatchObjectiveRules.IsDecisive(actor));
            saved.HasPrimaryCommanderRole = true; saved.IsPrimaryCommander = false;
            SaveGameApplier.ApplyStatusFields(actor, saved);
            Check("explicit ordinary role cannot become commander on resume", !actor.IsPrimaryCommander && !MatchObjectiveRules.IsDecisive(actor));
            saved.IsPrimaryCommander = true;
            SaveGameApplier.ApplyStatusFields(actor, saved);
            Check("commander identity restores independently of kind", actor.IsPrimaryCommander && MatchObjectiveRules.IsDecisive(actor));
            Check("commander victory explains the actual defeat condition", MatchObjectiveRules.DefeatReason(GameResult.Win, actor).Contains("王"));
            saved.HasPrimaryCommanderRole = false; saved.IsPrimaryCommander = false;
            SaveGameApplier.ApplyStatusFields(actor, saved);
            Check("legacy missing role preserves a matched initial slot", actor.IsPrimaryCommander);
            actor.team = Team.Monster;
            Check("third-faction Boss never ends a principal match", !MatchObjectiveRules.IsDecisive(actor));
        }
        finally { UnityEngine.Object.DestroyImmediate(ordinary); }
        var sim = new SimBoardState { Units = new System.Collections.Generic.List<SimUnit>() };
        var simulated = new SimUnit { Kind = Kind.Boss, Team = Team.Player, HP = 0, MaxHP = 500, IsBoss = true };
        sim.Units.Add(simulated);
        Check("ordinary simulated Boss death is not terminal", !sim.IsTerminal());
        simulated.IsPrimaryCommander = true;
        Check("observed commander death is terminal and scores a victory", sim.IsTerminal() && SimBoardEvaluator.Evaluate(sim) == AIConstants.TERMINAL_Win);
        var clone = simulated.Clone();
        Check("pooled simulation clone preserves commander identity", clone.IsPrimaryCommander);
        SimBoardPool.ReturnUnit(clone);
        sim.Units.Clear();
        Check("missing or hidden opponent does not imply victory", !sim.IsTerminal());
        foreach (var team in new[] { Team.Player, Team.Enemy })
        {
            using (var f = new EconomyR2Tests.Fixture(systems, team))
            {
                f.Food(20); var unit = f.Unit(); unit.GrowthData.upkeepMagic = 1;
                f.Resources.MagicOre = 14;
                var board = f.Board();
                Check(team + " funded guaranteed supply deficit still reports warning", board.Governor.Economy.State == EconomicState.Warning);
                Check(team + " funded warning does not halt exploration or combat", board.Governor.Mode != StrategicMode.EconomicRecovery
                    && !board.Governor.Economy.UpkeepPaymentFailed);
                f.Resources.MagicOre = 1; board.Refresh(); board.Governor.Evaluate(board);
                Check(team + " genuine upkeep danger retains recovery priority", board.Governor.Mode == StrategicMode.EconomicRecovery
                    && board.Governor.Economy.UpkeepPaymentFailed);
                f.Resources.MagicOre = 100;
                var cells = f.AdjacentCells(); unit.transform.position = cells[0];
                var royal = f.Unit(); royal.kind = Kind.Boss; royal.IsPrimaryCommander = true; royal.transform.position = cells[1];
                var visionObject = new GameObject("Substitute exploration observation");
                visionObject.SetActive(false); visionObject.transform.SetParent(f.Units.transform);
                var vision = visionObject.AddComponent<VisionGenerator>();
                var seen = new System.Collections.Generic.HashSet<Vector3Int>(cells.Select(GridHelper.ToGridXZ));
                foreach (string field in new[] { "_playerVisionBox", "_enemyVisionBox", "_playerExplored", "_enemyExplored" })
                    typeof(VisionGenerator).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(vision,
                        new System.Collections.Generic.HashSet<Vector3Int>(field.StartsWith(team == Team.Player ? "_player" : "_enemy") ? seen : new System.Collections.Generic.HashSet<Vector3Int>()));
                board = new AIBoardState(systems.MoveGenerator, systems.AttackGenerator, f.AP, f.Units, f.Crystals,
                    vision, f.Builder, null, f.State, null, 20, null, team);
                board.MapCreate = systems.MapCreate; board.ReconThreatLevel = 15; board.Governor = new AIStrategicGovernor(); board.Governor.Evaluate(board);
                Check(team + " one nonroyal unit substitutes for a missing scout", board.Exploration.IsAssignedUnit(unit)
                    && !board.Exploration.IsAssignedUnit(royal));
                var scout = f.Unit(); scout.kind = scout.GrowthData.kind = Kind.Scout; scout.transform.position = cells[2];
                board.Refresh(); board.Governor.Evaluate(board);
                Check(team + " scout return releases the substitute", board.Exploration.IsAssignedUnit(scout) && !board.Exploration.IsAssignedUnit(unit));
                var critical = new System.Collections.Generic.List<Status>();
                new DefaultLossConditionProvider().Collect(board, critical);
                Check(team + " real loss provider protects the primary Boss", critical.Contains(royal));
            }
        }
        Debug.Log("[PlaytestBalance] ALL PASSED");
    }
}
#endif
