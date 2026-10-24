#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Native production-policy contracts with isolated actors, real map geometry and real forecasts.</summary>
public static class AITerritorySafetyTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly MethodInfo Advance = typeof(AIStrategicGovernor).GetMethod("IsSafeArmyAdvance", Hidden);
    static int checks;
    static void Check(string message, bool condition)
    { if (!condition) throw new InvalidOperationException("[AITerritorySafety] FAIL " + message); checks++; Debug.Log("[AITerritorySafety] PASS " + message); }
    static bool Promoted(AIBoardState board, AIAction action) => (bool)Advance.Invoke(board.Governor, new object[] { action, board });

    public static void Run(GameSystems systems)
    {
        if (!Application.isPlaying || systems?.MapCreate == null) throw new InvalidOperationException("A real started match is required.");
        checks = 0;
        var random = UnityEngine.Random.state;
        string playerNation = JsonUtility.ToJson(systems.FactionState.PlayerNation), enemyNation = JsonUtility.ToJson(systems.FactionState.EnemyNation);
        using (BoardActorAnimation.SuppressEffects())
            foreach (var team in new[] { Team.Player, Team.Enemy })
            {
                MemoryAndSafety(systems, team);
                EconomicInterruption(systems, team);
                VisibleCombatPriority(systems, team);
                KnownDetour(systems, team);
            }
        Check("live nations/AP/resources are unchanged", playerNation == JsonUtility.ToJson(systems.FactionState.PlayerNation)
            && enemyNation == JsonUtility.ToJson(systems.FactionState.EnemyNation));
        Check("gameplay random state is unchanged", random.Equals(UnityEngine.Random.state));
        Debug.Log("[AITerritorySafety] ALL PASSED " + checks);
    }

    static void MemoryAndSafety(GameSystems systems, Team team)
    {
        using var f = new Fixture(systems, team);
        var board = f.Board(60); var move = f.Move();
        Check(team + " territory-only information does not invent hostile actors or a crystal", !board.HasPrincipalOpponentContact
            && !board.PlayerCrystalVisible && !board.GetLastKnownPlayerCrystal().Valid);
        Check(team + " no scout is necessary for a known legal army approach", !f.Actor.IsBoss
            && board.Governor.HasActiveTerritoryAdvance && board.GetValidMoves(f.Actor).Contains(move.TargetPos) && Promoted(board, move));
        var decision = new TurnStrategyPlanner().DecideStrategy(board, f.Economy.Personality, new AIThreatLevel(1), 60);
        Check(team + " low-threat territory deployment is distinguished from contact", decision.Strategy == TurnStrategy.Balanced
            && decision.Reason.Contains("敵領土") && !decision.Reason.Contains("を視認:"));
        f.HideTerritory(); board = f.Board(70);
        Check(team + " observed territory survives more than seven turns outside sight", board.Governor.TerritoryIntel.HasObservedTerritory
            && board.Governor.TerritoryIntel.ObservedCellCount == 1 && board.Governor.HasActiveTerritoryAdvance);

        var goal = board.Governor.TerritoryIntel.Waypoint;
        f.Economy.Crystal.HP = 1; f.Foe.transform.position = f.Cells[1]; f.Foe.ATK = 5000; f.See(f.Cells[1]);
        board = f.Board(71);
        Check(team + " a real observed legal lethal threat interrupts deployment", board.AlivePlayerUnits.Contains(f.Foe)
            && board.Governor.IncomingDamage(board, f.Economy.Crystal, f.Economy.Crystal.transform.position, null) >= f.Economy.Crystal.HP
            && board.Governor.Mode == StrategicMode.EmergencyDefense && board.Governor.TerritoryIntel.Suspended
            && board.Governor.TerritoryIntel.HasWaypoint && board.Governor.TerritoryIntel.Waypoint == goal && !Promoted(board, f.Move()));
        f.Foe.transform.position = new Vector3(-35000, 0, -36000); f.Economy.Crystal.HP = f.Economy.Crystal.MaxHP;
        board = f.Board(72);
        Check(team + " resolving the observed crisis resumes the retained goal", board.Governor.Mode != StrategicMode.EmergencyDefense
            && board.Governor.HasActiveTerritoryAdvance && board.Governor.TerritoryIntel.Waypoint == goal && Promoted(board, f.Move()));

        f.Economy.State.SetAP(team, 0); board = f.Board(73);
        Check(team + " AP shortage never gets an advance promotion", !Promoted(board, f.Move()));
        f.Economy.State.SetAP(team, 40); f.Actor.ActiveEffects.Add(new ActiveEffect(StatusEffectType.Stun, 1)); board = f.Board(74);
        Check(team + " stunned actors do not receive an advance promotion", !Promoted(board, f.Move()));
        f.Actor.ActiveEffects.Clear();
        var blocker = f.Economy.Unit(); blocker.transform.position = f.Cells[3]; board = f.Board(75);
        Check(team + " observed friendly occupancy is respected", !Promoted(board, f.Move()));
        blocker.gameObject.SetActive(false); board = f.Board(76);
        var wrongHeight = f.Move(); wrongHeight.TargetPos += Vector3.up * 3;
        Check(team + " an invented elevation cannot bypass the real terrain height", !Promoted(board, wrongHeight));
        f.Explored.Remove(GridHelper.ToGridXZ(f.Cells[3])); f.Visible.Remove(GridHelper.ToGridXZ(f.Cells[3])); board = f.Board(77);
        Check(team + " an unknown immediate destination is not promoted", !board.IsTerrainKnown(f.Cells[3]) && !Promoted(board, f.Move()));
        if (BlockedCliff(systems.MapCreate, out var lower, out var higher))
        {
            f.Actor.transform.position = lower; f.Explored.Add(GridHelper.ToGridXZ(lower)); f.Explored.Add(GridHelper.ToGridXZ(higher)); f.See(lower); f.See(higher);
            board = f.Board(78);
            var cliffMove = f.Move(); cliffMove.TargetPos = higher;
            Check(team + " real adjacent impassable elevation is never promoted", !systems.MapCreate.CanTraverse(lower, higher)
                && board.IsTerrainKnown(higher) && !Promoted(board, cliffMove));
        }
        else Debug.LogWarning("[AITerritorySafety] UNVERIFIED actual cliff: this generated map has no adjacent elevation blocked by CanTraverse; forged-height contract still ran.");
    }

    static void EconomicInterruption(GameSystems systems, Team team)
    {
        using var f = new Fixture(systems, team);
        // This scenario isolates a funded supply warning. Zero citizens makes the
        // missing-producer construction plan a real, separate foundation crisis.
        f.Economy.Resources.Citizen = 5;
        f.Actor.GrowthData.upkeepMagic = 1;
        var recipe = f.Mine.levels[0]; recipe.Output.MagicOre = 0; f.Mine.levels[0] = recipe;
        f.Economy.Resources.MagicOre = 14; GameContentRevision.NotifyChanged();
        var board = f.Board(60);
        Check(team + " funded mandatory-supply warning is diagnosed by the real forecast", board.Governor.Economy.State == EconomicState.Warning
            && !board.Governor.Economy.UpkeepPaymentFailed && !board.Governor.ProductionDemand.HasCriticalDeficit
            && !board.Governor.BasicResources.HasCriticalDeficit);
        Debug.Log("[AITerritorySafety] funded warning diagnosis " + team + " mode=" + board.Governor.Mode
            + " criticalDemand=" + board.Governor.ProductionDemand.HasCriticalDeficit
            + " recoveryDemand=" + AIStrategicGovernor.RequiresEconomicRecovery(board.Governor.ProductionDemand)
            + " basicCritical=" + board.Governor.BasicResources.HasCriticalDeficit
            + " activeGoal=" + board.Governor.HasActiveTerritoryAdvance + " intel=" + board.Governor.TerritoryIntel.Reason
            + " reserveAP=" + board.Governor.GetRecoveryReserveAP(board) + " promoted=" + Promoted(board, f.Move()));
        Check(team + " funded warning keeps territory deployment and reserves no compulsory AP", board.Governor.Mode != StrategicMode.EconomicRecovery
            && board.Governor.HasActiveTerritoryAdvance && board.Governor.GetRecoveryReserveAP(board) == 0 && Promoted(board, f.Move()));
        var goal = board.Governor.TerritoryIntel.Waypoint;
        f.Economy.Resources.MagicOre = 0; board = f.Board(61);
        Check(team + " actual forecast upkeep failure pauses the same territory goal", board.Governor.Economy.UpkeepPaymentFailed
            && board.Governor.Mode == StrategicMode.EconomicRecovery && board.Governor.TerritoryIntel.Suspended
            && board.Governor.TerritoryIntel.HasWaypoint && board.Governor.TerritoryIntel.Waypoint == goal && !Promoted(board, f.Move()));
        f.Economy.Resources.MagicOre = 400; recipe.Output.MagicOre = 30; f.Mine.levels[0] = recipe; GameContentRevision.NotifyChanged();
        board = f.Board(62);
        Check(team + " restoring guaranteed supply resumes the saved deployment", board.Governor.Mode != StrategicMode.EconomicRecovery
            && board.Governor.HasActiveTerritoryAdvance && board.Governor.TerritoryIntel.Waypoint == goal && Promoted(board, f.Move()));
    }

    static void VisibleCombatPriority(GameSystems systems, Team team)
    {
        using var f = new Fixture(systems, team);
        f.Foe.transform.position = f.Cells[1]; f.Foe.ATK = 1; f.See(f.Cells[1]);
        var board = f.Board(60);
        var attack = new AIAction { ActionType = AIActionType.Attack, Unit = f.Actor, TargetUnit = f.Foe,
            APCost = board.CalcAttackCost(f.Actor), Score = 1 };
        var stage = f.Move(); stage.Score = 100000;
        var candidates = new List<AIAction> { stage, attack }; board.Governor.Filter(candidates, board);
        Check(team + " a currently legal observed attack outranks territory staging irrespective of float score",
            candidates.Contains(attack) && candidates.Contains(stage) && Promoted(board, stage)
            && attack.StrategicPriority < stage.StrategicPriority);
    }

    static void KnownDetour(GameSystems systems, Team team)
    {
        using var f = new Fixture(systems, team, detour: true);
        var board = f.Board(60); var move = f.Move();
        var goal = (Vector3)board.Governor.TerritoryIntel.Waypoint;
        float before = Vector3.Distance(f.Actor.transform.position, goal), after = Vector3.Distance(move.TargetPos, goal);
        Check(team + " U-route fixture requires a step farther from the observed goal in straight-line distance", board.Governor.HasActiveTerritoryAdvance
            && after > before && board.GetValidMoves(f.Actor).Contains(move.TargetPos));
        Check(team + " known-route improvement promotes a necessary detour", board.Governor.TerritoryIntel.IsProgress(f.Actor, move.TargetPos, board)
            && board.Governor.TerritoryIntel.ProgressScore(f.Actor, move.TargetPos, board) > 0 && Promoted(board, move));
    }

    static bool BlockedCliff(MapCreate map, out Vector3 lower, out Vector3 higher)
    {
        foreach (var cell in map.SetPos)
            foreach (var offset in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
            {
                int x = Mathf.RoundToInt(cell.x + offset.x), z = Mathf.RoundToInt(cell.z + offset.z);
                if (!map.TryGetHeight(x, z, out float height) || height <= cell.y + .01f) continue;
                var to = new Vector3(x, height, z);
                if (!map.CanTraverse(cell, to)) { lower = cell; higher = to; return true; }
            }
        lower = higher = default; return false;
    }

    internal sealed class Fixture : IDisposable
    {
        public readonly EconomyR2Tests.Fixture Economy;
        public readonly Status Actor, Foe;
        public readonly Vector3[] Cells;
        public readonly HashSet<Vector3Int> Visible = new HashSet<Vector3Int>(), Explored = new HashSet<Vector3Int>();
        public readonly FacilityDefinitionData Mine;
        readonly GameSystems live;
        readonly GameObject host;
        readonly VisionGenerator vision;
        readonly TerritorySystem territory;
        readonly BoardActionProfile profile;
        readonly AIStrategicGovernor governor = new AIStrategicGovernor();
        readonly Dictionary<int, AIBoardState.LastKnownInfo> memory = AIBoardState.CreateSharedMemory();
        readonly bool detour;
        public Fixture(GameSystems systems, Team team, bool detour = false)
        {
            live = systems; this.detour = detour; Economy = new EconomyR2Tests.Fixture(systems, team);
            try
            {
                Economy.Rules.aiEconomy.plannedDemandWeight = 0; Economy.Resources.Citizen = 0; Economy.Food(100);
                Economy.Building(Economy.Definition(FacilityKind.LoggingCamp, wood: 30));
                Economy.Building(Economy.Definition(FacilityKind.Quarry, stone: 30));
                Mine = Economy.Definition(FacilityKind.Mine, iron: 30, magic: 30); Economy.Building(Mine);
                host = new GameObject("Territory safety isolated knowledge"); host.SetActive(false);
                vision = host.AddComponent<VisionGenerator>(); territory = host.AddComponent<TerritorySystem>();
                territory.PTSetPos = new List<Vector3>(); territory.ETSetPos = new List<Vector3>();
                profile = ScriptableObject.CreateInstance<BoardActionProfile>(); profile.actionType = ActorActionType.Combat;
                profile.movement.useCustom = profile.attack.useCustom = true;
                profile.movement.directionIndependent = profile.attack.directionIndependent = true;
                foreach (var offset in new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down })
                { profile.movement.SetCell(3 + offset.x, 3 + offset.y, true); profile.attack.SetCell(3 + offset.x, 3 + offset.y, true); }
                var origin = FlatPatch(live.MapCreate, detour ? 5 : 6, detour ? 3 : 1);
                Cells = new Vector3[6]; for (int i = 0; i < Cells.Length; i++) Cells[i] = origin + Vector3.right * i;
                Actor = Economy.Unit(); Actor.ATK = 25; Actor.HP = Actor.MaxHP = 100; Actor.GrowthData.actionProfile = profile;
                Foe = Economy.Opponent(new Vector3(-35000, 0, -36000), 1);
                if (team == Team.Player) Economy.Crystals.ECP = new Vector3(35000, 0, 36000); else Economy.Crystals.PCP = new Vector3(35000, 0, 36000);
                if (detour)
                {
                    Actor.transform.position = origin + Vector3.right;
                    foreach (var offset in new[] { new Vector2Int(1, 0), new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(0, 2),
                        new Vector2Int(1, 2), new Vector2Int(2, 2), new Vector2Int(3, 2), new Vector2Int(4, 2), new Vector2Int(4, 1), new Vector2Int(4, 0) })
                        Explored.Add(GridHelper.ToGridXZ(origin + new Vector3(offset.x, 0, offset.y)));
                    See(Actor.transform.position); See(origin + Vector3.right * 4);
                    territory.GetTerritory(team == Team.Player ? Team.Enemy : Team.Player).Add(origin + Vector3.right * 4);
                    Cells[3] = origin;
                }
                else
                {
                    Actor.transform.position = Cells[2];
                    foreach (var cell in Cells) { Explored.Add(GridHelper.ToGridXZ(cell)); See(cell); }
                    territory.GetTerritory(team == Team.Player ? Team.Enemy : Team.Player).Add(Cells[5]);
                }
                Economy.Crystal.transform.position = origin; Economy.SetOwnCrystalPosition(origin);
                if (detour) { Economy.Crystal.transform.position = new Vector3(-20000, 0, -21000); Economy.SetOwnCrystalPosition(Economy.Crystal.transform.position); }
            }
            catch { Dispose(); throw; }
        }
        public void See(Vector3 cell) => Visible.Add(GridHelper.ToGridXZ(cell));
        public void HideTerritory() { Visible.Remove(GridHelper.ToGridXZ(Cells[5])); }
        public AIBoardState Board(int turn)
        {
            foreach (var field in new[] { "_playerVisionBox", "_enemyVisionBox", "_playerExplored", "_enemyExplored" })
                typeof(VisionGenerator).GetField(field, Hidden).SetValue(vision,
                    field == (Economy.Team == Team.Player ? "_playerVisionBox" : "_enemyVisionBox") ? Visible
                    : field == (Economy.Team == Team.Player ? "_playerExplored" : "_enemyExplored") ? Explored : new HashSet<Vector3Int>());
            var board = new AIBoardState(live.MoveGenerator, live.AttackGenerator, Economy.AP, Economy.Units, Economy.Crystals,
                vision, Economy.Builder, null, Economy.State, null, turn, memory, Economy.Team)
                { MapCreate = live.MapCreate, TerritorySystem = territory, ReconThreatLevel = 1, Governor = governor };
            board.Refresh(); governor.Evaluate(board); return board;
        }
        public AIAction Move() => new AIAction { ActionType = AIActionType.Move, Unit = Actor, TargetPos = Cells[3], APCost = 3, Score = 100 };
        public void Dispose()
        { if (profile != null) UnityEngine.Object.DestroyImmediate(profile); if (host != null) UnityEngine.Object.DestroyImmediate(host); Economy?.Dispose(); }
        static Vector3 FlatPatch(MapCreate map, int width, int depth)
        {
            foreach (var origin in map.SetPos)
            {
                bool valid = true;
                for (int x = 0; x < width && valid; x++) for (int z = 0; z < depth && valid; z++)
                {
                    int cx = Mathf.RoundToInt(origin.x) + x, cz = Mathf.RoundToInt(origin.z) + z;
                    if (!map.TryGetHeight(cx, cz, out float y) || Mathf.Abs(y - origin.y) > .01f || map.IsRiver(cx, cz)
                        || map.IsHighMountain(cx, cz) || map.Environment.HasCell(new Vector3(cx, y, cz))) { valid = false; break; }
                    if (x > 0 && !map.CanTraverse(new Vector3(cx - 1, y, cz), new Vector3(cx, y, cz))) valid = false;
                    if (z > 0 && !map.CanTraverse(new Vector3(cx, y, cz - 1), new Vector3(cx, y, cz))) valid = false;
                }
                if (valid) return origin;
            }
            throw new InvalidOperationException("Territory safety fixture needs a connected " + width + "x" + depth + " level patch without hazards.");
        }
    }
}
#endif
