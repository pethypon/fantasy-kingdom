#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Safe army deployment competes with scouting without recruiting the king or consulting hidden actors.</summary>
public static class AIClosingAdvanceTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static readonly MethodInfo Advance = typeof(AIStrategicGovernor).GetMethod("IsSafeArmyAdvance", Hidden);
    static void Check(string label, bool condition)
    { if (!condition) throw new InvalidOperationException("[AIClosingAdvance] FAIL " + label); checks++; Debug.Log("[AIClosingAdvance] PASS " + label); }
    static bool Promoted(AIBoardState board, AIAction action) => (bool)Advance.Invoke(board.Governor, new object[] { action, board });

    public static void PlayMode(GameSystems systems)
    {
        if (!Application.isPlaying || systems?.MapCreate == null) throw new InvalidOperationException("A real started scene is required.");
        checks = 0; var random = UnityEngine.Random.state;
        string playerAP = JsonUtility.ToJson(systems.FactionState.PlayerAP), enemyAP = JsonUtility.ToJson(systems.FactionState.EnemyAP);
        string playerStock = JsonUtility.ToJson(systems.FactionState.PlayerResources), enemyStock = JsonUtility.ToJson(systems.FactionState.EnemyResources);
        using (BoardActorAnimation.SuppressEffects())
        foreach (var team in new[] { Team.Player, Team.Enemy })
        {
            Competition(systems, team); Eligibility(systems, team); Memories(systems, team); Survival(systems, team);
        }
        Check("both real AP ledgers remain unchanged", playerAP == JsonUtility.ToJson(systems.FactionState.PlayerAP)
            && enemyAP == JsonUtility.ToJson(systems.FactionState.EnemyAP));
        Check("both real stocks remain unchanged", playerStock == JsonUtility.ToJson(systems.FactionState.PlayerResources)
            && enemyStock == JsonUtility.ToJson(systems.FactionState.EnemyResources));
        Check("gameplay Random remains unchanged", random.Equals(UnityEngine.Random.state));
        Debug.Log("[AIClosingAdvance] " + checks + " safe army/priority/observation checks passed");
    }
    static void Competition(GameSystems systems, Team team)
    {
        using var f = new Fixture(systems, team);
        var board = f.Board(); var move = f.Move();
        Check(team + " healthy observed front has no supply/emergency override", board.Governor.Mode == StrategicMode.StrategicAttack);
        Check(team + " actual legal safe army advance is eligible", board.GetValidMoves(f.Actor).Contains(move.TargetPos) && Promoted(board, move));
        var scout = new AIAction { ActionType = AIActionType.Move, Unit = f.Scout, TargetPos = f.Cells[2], APCost = 2, Score = 5 };
        var actions = new List<AIAction> { scout, move }; board.Governor.Filter(actions, board);
        Check(team + " safe deployment competes at P3 alongside an actual scout step", actions.Contains(move) && move.StrategicPriority == 3
            && board.Governor.Exploration.IsAssignedUnit(f.Scout) && scout.StrategicPriority == 3);
        actions.Sort(AIAction.ComparePriorityThenScore);
        Check(team + " tactical advance score can win the same priority tier", actions[0] == move);
        foreach (var type in new[] { AIActionType.Surround, AIActionType.Support })
        {
            move = f.Move(type); board.Governor.Filter(new List<AIAction> { move }, board);
            Check(team + " safe " + type + " can advance with the army", move.StrategicPriority == 3);
        }
        foreach (var type in new[] { AIActionType.Retreat, AIActionType.DefenseRepos })
            Check(team + " offensive promotion does not capture " + type, !Promoted(board, f.Move(type)));
    }
    static void Eligibility(GameSystems systems, Team team)
    {
        using var f = new Fixture(systems, team);
        foreach (Kind kind in new[] { Kind.King, Kind.Boss, Kind.Scout, Kind.Priest, Kind.WhiteMage })
        {
            f.Actor.kind = kind; f.Actor.IsPrimaryCommander = kind == Kind.Boss;
            Check(team + " commander/scout/healer excluded: " + kind, !Promoted(f.Board(), f.Move()));
        }
        f.Actor.kind = Kind.Knight; f.Actor.IsPrimaryCommander = true;
        Check(team + " custom primary commander is excluded regardless of kind", !Promoted(f.Board(), f.Move()));
        f.Actor.IsPrimaryCommander = false; f.Actor.HP = 59;
        Check(team + " wounded unit is not pushed into the attack", !Promoted(f.Board(), f.Move()));
        f.Actor.HP = 100; f.Actor.HasMovedThisTurn = true;
        Check(team + " a unit already moved this turn is not promoted again", !Promoted(f.Board(), f.Move()));
        f.Actor.HasMovedThisTurn = false; f.Actor.ActiveEffects.Add(new ActiveEffect(StatusEffectType.Stun, 1));
        Check(team + " stunned unit is not promoted", !Promoted(f.Board(), f.Move()));
        f.Actor.ActiveEffects.Clear(); f.Profile.actionType = ActorActionType.Support;
        Check(team + " authored noncombat behavior is respected", !Promoted(f.Board(), f.Move()));
        f.Profile.actionType = ActorActionType.Combat;
        var board = f.Board(); var backwards = f.Move(); backwards.TargetPos = f.Cells[2];
        Check(team + " movement away from principal contact receives no advance tier", !Promoted(board, backwards));
        var illegal = f.Move(); illegal.TargetPos = f.Cells[1];
        Check(team + " displacement outside the authored movement mask is rejected", !Promoted(board, illegal));
        var stationary = f.Move(); stationary.TargetPos = f.Actor.transform.position;
        Check(team + " a same-cell no-op is not an advance", !Promoted(board, stationary));
        f.SetVisible(true, false);
        Check(team + " an unobserved destination receives no promotion", !Promoted(f.Board(), f.Move()));
        f.SetVisible(true); f.Economy.State.SetAP(team, 0);
        Check(team + " unaffordable move receives no promotion", !Promoted(f.Board(), f.Move()));
    }
    static void Memories(GameSystems systems, Team team)
    {
        using var f = new Fixture(systems, team);
        f.SetVisible(false);
        var board = f.Board();
        Check(team + " unseen real opponent alone cannot create advance intent", !Promoted(board, f.Move()));
        f.Foe.transform.position = new Vector3(48000, 22, -46000);
        Check(team + " hidden transform changes do not create a target", !Promoted(f.Board(), f.Move()));
        var memory = AIBoardState.CreateSharedMemory();
        memory[10001] = new AIBoardState.LastKnownInfo { Valid = true, WasPrincipalCommander = true, Kind = Kind.Boss, Type = Type.Unit,
            Position = GridHelper.ToGrid(f.Cells[5]), Turn = 56 };
        Check(team + " four-turn principal commander snapshot can guide deployment", Promoted(f.Board(memory), f.Move()));
        f.Foe.transform.position = new Vector3(-47000, 16, 49000);
        Check(team + " commander snapshot ignores another hidden transform change", Promoted(f.Board(memory), f.Move()));
        var old = memory[10001]; old.Turn = 55; memory[10001] = old;
        Check(team + " five-turn commander snapshot expires", !Promoted(f.Board(memory), f.Move()));
        old.Turn = 56; old.WasPrincipalCommander = false; memory[10001] = old;
        Check(team + " generic or neutral boss history cannot replace principal intelligence", !Promoted(f.Board(memory), f.Move()));
        memory.Clear();
        memory[int.MinValue] = new AIBoardState.LastKnownInfo { Valid = true, Kind = Kind.Crystal, Type = Type.Building,
            Position = GridHelper.ToGrid(f.Cells[5]), Turn = 56 };
        Check(team + " recently observed main crystal guides the army", Promoted(f.Board(memory), f.Move()));
        var lowThreat = f.Board(memory); lowThreat.ReconThreatLevel = 3;
        Check(team + " low threat does not use unseen crystal memory", !Promoted(lowThreat, f.Move()));
        f.SetOpposingCrystal(new Vector3(43000, 20, -43000));
        Check(team + " remembered main crystal ignores hidden system position", Promoted(f.Board(memory), f.Move()));
        old = memory[int.MinValue]; old.Turn = 55; memory[int.MinValue] = old;
        Check(team + " stale main crystal intent expires", !Promoted(f.Board(memory), f.Move()));
    }
    static void Survival(GameSystems systems, Team team)
    {
        using (var f = new Fixture(systems, team))
        {
            f.Foe.ATK = 5000; var board = f.Board(); var move = f.Move();
            Check(team + " dangerous next-turn exposure is not promoted", board.Governor.IncomingDamage(board, f.Actor, move.TargetPos, null) >= f.Actor.HP * .25f
                && !Promoted(board, move));
        }
        using (var f = new Fixture(systems, team))
        {
            foreach (var building in f.Economy.Buildings)
                if (building.facilityKind == FacilityKind.Bakery)
                { var recipe = building.AuthoredFacility.levels[0]; recipe.Output.Bread = 0; building.AuthoredFacility.levels[0] = recipe; }
            f.Economy.Resources.Bread = 0; GameContentRevision.NotifyChanged();
            var board = f.Board(); var move = f.Move(); board.Governor.Filter(new List<AIAction> { move }, board);
            Check(team + " economic recovery retains priority over deployment", board.Governor.Mode == StrategicMode.EconomicRecovery
                && !Promoted(board, move) && move.StrategicPriority == 4);
        }
        using (var f = new Fixture(systems, team))
        {
            f.Actor.transform.position = f.Cells[1]; f.Scout.transform.position = f.Cells[0];
            f.Economy.Crystal.transform.position = f.Cells[3]; f.Economy.SetOwnCrystalPosition(f.Cells[3]);
            f.Economy.Crystal.HP = 10; f.Economy.Crystal.DEF = 0; f.Foe.ATK = 5000;
            var board = f.Board(); var move = f.Move(); move.TargetPos = f.Cells[2];
            Check(team + " emergency defense retains priority over a legal army advance", board.Governor.Mode == StrategicMode.EmergencyDefense
                && board.GetValidMoves(f.Actor).Contains(move.TargetPos) && !Promoted(board, move));
        }
    }
    sealed class Fixture : IDisposable
    {
        public readonly EconomyR2Tests.Fixture Economy;
        public readonly Status Actor, Scout, Foe;
        public readonly BoardActionProfile Profile;
        public readonly Vector3[] Cells;
        readonly VisionGenerator vision;
        readonly GameSystems live;
        public Fixture(GameSystems systems, Team team)
        {
            live = systems; Economy = new EconomyR2Tests.Fixture(systems, team);
            try
            {
                Economy.Rules.aiEconomy.plannedDemandWeight = 0; Economy.Rules.aiEconomy.reserveTurns = Economy.Rules.aiEconomy.forecastTurns = 4;
                Economy.Resources.Citizen = 0; Economy.Food(100);
                Economy.Building(Economy.Definition(FacilityKind.LoggingCamp, wood: 20));
                Economy.Building(Economy.Definition(FacilityKind.Quarry, stone: 20));
                Economy.Building(Economy.Definition(FacilityKind.Mine, iron: 20, magic: 20));
                Cells = FlatCells(systems.MapCreate);
                Economy.Crystal.transform.position = Cells[0]; Economy.SetOwnCrystalPosition(Cells[0]);
                Actor = Economy.Unit(); Actor.transform.position = Cells[3]; Actor.ATK = 25; Actor.DEF = 5;
                Scout = Economy.Unit(); Scout.kind = Kind.Scout; Scout.transform.position = Cells[1];
                Foe = Economy.Opponent(Cells[5], 1);
                Profile = ScriptableObject.CreateInstance<BoardActionProfile>(); Profile.actionType = ActorActionType.Combat;
                Profile.movement.useCustom = true; Profile.movement.directionIndependent = true;
                Profile.movement.SetCell(2, 3, true); Profile.movement.SetCell(4, 3, true); Actor.GrowthData.actionProfile = Profile;
                var host = new GameObject("Army advance isolated knowledge"); host.SetActive(false); host.transform.SetParent(Economy.Crystal.transform);
                vision = host.AddComponent<VisionGenerator>(); SetVisible(true);
            }
            catch { Dispose(); throw; }
        }
        public void SetVisible(bool opponent, bool destination = true)
        {
            var seen = new HashSet<Vector3Int>();
            for (int i = 0; i < Cells.Length; i++) if ((opponent || i != 5) && (destination || i != 4)) seen.Add(GridHelper.ToGridXZ(Cells[i]));
            foreach (var field in new[] { "_playerVisionBox", "_enemyVisionBox", "_playerExplored", "_enemyExplored" })
                typeof(VisionGenerator).GetField(field, Hidden).SetValue(vision, new HashSet<Vector3Int>(seen));
        }
        public void SetOpposingCrystal(Vector3 position)
        { if (Economy.Team == Team.Player) Economy.Crystals.ECP = position; else Economy.Crystals.PCP = position; }
        public AIBoardState Board(Dictionary<int, AIBoardState.LastKnownInfo> memory = null)
        {
            var board = new AIBoardState(live.MoveGenerator, live.AttackGenerator, Economy.AP, Economy.Units, Economy.Crystals,
                vision, Economy.Builder, null, Economy.State, null, 60, memory, Economy.Team)
                { MapCreate = live.MapCreate, ReconThreatLevel = 15, Governor = new AIStrategicGovernor() };
            board.Governor.Evaluate(board); return board;
        }
        public AIAction Move(AIActionType type = AIActionType.Move) => new AIAction
            { Unit = Actor, ActionType = type, TargetPos = Cells[4], APCost = 2, Score = 100 };
        public void Dispose() { if (Profile != null) UnityEngine.Object.DestroyImmediate(Profile); Economy?.Dispose(); }
        static Vector3[] FlatCells(MapCreate map)
        {
            int region = Mathf.Clamp(ExplorationAISettings.Active.RegionSize, 2, 128);
            foreach (var origin in map.SetPos)
            {
                if (region >= 6 && (int)origin.x / region != ((int)origin.x + 5) / region) continue;
                var cells = new Vector3[6]; bool valid = true;
                for (int i = 0; i < cells.Length; i++)
                {
                    int x = Mathf.RoundToInt(origin.x) + i, z = Mathf.RoundToInt(origin.z);
                    if (!map.TryGetHeight(x, z, out float y) || Mathf.Abs(y - origin.y) > .01f
                        || map.IsRiver(x, z) || map.IsHighMountain(x, z)) { valid = false; break; }
                    cells[i] = new Vector3(x, y, z);
                    if (map.Environment.HasCell(cells[i]) || i > 0 && !map.CanTraverse(cells[i - 1], cells[i])) { valid = false; break; }
                }
                if (valid) return cells;
            }
            throw new InvalidOperationException("Army advance fixture needs six connected level cells without environmental hazards.");
        }
    }
}
#endif
