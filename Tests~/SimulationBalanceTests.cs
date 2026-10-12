#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;

public static class SimulationBalanceTests
{
    static int passed;
    static void Check(string name, bool condition)
    { if (!condition) throw new InvalidOperationException("[SimulationBalance] FAIL " + name); passed++; }
    public static void Run(GameSystems live)
    {
        passed = 0; var data = ScriptableObject.CreateInstance<UnitData>(); SimBoardState simulated = null, clone = null;
        var definitions = live.UnitSetting.UnitDataMap; definitions.TryGetValue(Kind.Knight, out var original);
        try
        {
            data.kind = Kind.Knight; data.baseHP = 123; data.baseATK = 47; data.baseDEF = 19; data.costAP = 17;
            data.useAuthoredAbilities = true; data.authoredPassive = PassiveSkill.Impregnable;
            definitions[Kind.Knight] = data;
            var board = new AIBoardState(live.MoveGenerator, live.AttackGenerator, live.APSystem, live.UnitSetting,
                live.CrystalSystem, live.VisionGenerator, live.BuildSystem, live.SummonSystem, live.FactionState, null, 20);
            board.Refresh();
            simulated = SimBoardState.CreateFromGame(board, live.MoveGenerator, live.UnitSetting, live.CrystalSystem, live.APSystem);
            var knownMountains = new System.Collections.Generic.HashSet<Vector3Int>();
            var knownWater = new System.Collections.Generic.HashSet<Vector3Int>();
            for (int x = 0; x < live.MapCreate.maxX; x++) for (int z = 0; z < live.MapCreate.maxZ; z++)
            {
                var cell = new Vector3Int(x, 0, z); if (!board.IsTerrainKnown(cell)) continue;
                if (live.MapCreate.IsHighMountain(x, z)) knownMountains.Add(cell);
                if (live.MapCreate.IsRiver(x, z)) knownWater.Add(cell);
            }
            Check("observed terrain index exactly matches the prior full-map filter", simulated.Mountains.SetEquals(knownMountains) && simulated.Water.SetEquals(knownWater));
            Check("search captures the formal recipe", simulated.TryGetRecruitment(Kind.Knight, out var recipe)
                && recipe.HP == 123 && recipe.ATK == 47 && recipe.DEF == 19 && recipe.APCost == 17);
            data.baseATK = 333; data.costAP = 1;
            Check("in-progress search remains a consistent snapshot", recipe.ATK == 47 && recipe.APCost == 17);
            foreach (var unit in simulated.Units) SimBoardPool.ReturnUnit(unit); simulated.Units.Clear();
            simulated.EnemyAP = 60; simulated.EnemyCrystalPos = Vector3Int.zero; simulated.PlayerCrystalPos = new Vector3Int(10, 0, 10);
            simulated.MapTiles = new System.Collections.Generic.HashSet<Vector3Int>();
            for (int x = 0; x < 5; x++) for (int z = 0; z < 5; z++) simulated.MapTiles.Add(new Vector3Int(x, 0, z));
            simulated.RebuildOccupied();
            var action = SimActionGenerator.GenerateAllActions(simulated, Team.Enemy).First(a => a.Type == SimActionType.Summon && a.SummonKind == Kind.Knight);
            Check("response generator uses formal AP cost", action.APCost == 17);
            Check("formal recipe can be simulated", simulated.ApplyAction(action));
            var knight = simulated.Units.Last();
            Check("simulated recruited stats match the real definition", knight.MaxHP == 123 && knight.ATK == 47 && knight.DEF == 19);
            Check("simulated recruited abilities match the definition", knight.Passive == PassiveSkill.Impregnable);
            Check("simulated AP transaction uses the same cost", simulated.EnemyAP == 43);
            Check("missing unseen crystal is not a terminal victory", !simulated.IsTerminal());
            var decisive = new SimUnit { Team = Team.Obstacle, Kind = Kind.Boss, IsBoss = true, HP = 0 };
            simulated.Units.Add(decisive);
            Check("defeated neutral boss is not a match end", !simulated.IsTerminal());
            decisive.Team = Team.Enemy;
            Check("defeated faction commander is terminal", simulated.IsTerminal());
            decisive.IsBoss = false; decisive.Kind = Kind.Crystal; decisive.Team = Team.Player;
            Check("defeated known crystal is terminal", simulated.IsTerminal());
            decisive.HP = 1;
            Check("living known crystal is not terminal", !simulated.IsTerminal());
            clone = simulated.Clone();
            Check("clones share consistent formal snapshot", clone.TryGetRecruitment(Kind.Knight, out var copied) && copied.ATK == 47 && copied.APCost == 17);
            var playerOnly = ScriptableObject.CreateInstance<UnitData>();
            try
            {
                playerOnly.availableToEnemy = false; playerOnly.availableToPlayer = true;
                var available = new SimRecruitmentDefinition(playerOnly);
                simulated.RealActorTeam = Team.Player;
                Check("developer player AI maps recruitment ownership correctly", simulated.RecruitmentAvailable(available, Team.Enemy)
                    && !simulated.RecruitmentAvailable(available, Team.Player));
            }
            finally { UnityEngine.Object.DestroyImmediate(playerOnly); }
        }
        finally
        {
            if (original != null) definitions[Kind.Knight] = original; else definitions.Remove(Kind.Knight);
            if (clone != null) SimBoardPool.ReturnBoard(clone); if (simulated != null) SimBoardPool.ReturnBoard(simulated);
            UnityEngine.Object.DestroyImmediate(data);
        }
        Debug.Log("[SimulationBalance] " + passed + " formal recipe/snapshot/perspective checks passed");
    }
}
#endif
