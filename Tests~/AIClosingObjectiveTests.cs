#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Isolated observation snapshots: finishing intent may use memories, never hidden live transforms.</summary>
public static class AIClosingObjectiveTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;

    static void Check(string label, bool condition)
    {
        if (!condition) throw new InvalidOperationException("[AIClosingObjective] FAIL " + label);
        checks++;
        Debug.Log("[AIClosingObjective] PASS " + label);
    }

    static void Set<T>(AIBoardState board, string property, T value)
        => typeof(AIBoardState).GetProperty(property).SetValue(board, value);

    public static void Run(GameSystems systems, TurnGenerator turn)
    {
        if (systems == null || turn == null) throw new ArgumentNullException("AI closing tests require a live game.");
        checks = 0;
        var random = UnityEngine.Random.state;
        string playerAP = JsonUtility.ToJson(systems.FactionState.PlayerAP);
        string enemyAP = JsonUtility.ToJson(systems.FactionState.EnemyAP);
        string playerStock = JsonUtility.ToJson(systems.FactionState.GetResources(Team.Player));
        string enemyStock = JsonUtility.ToJson(systems.FactionState.GetResources(Team.Enemy));
        try
        {
            using (BoardActorAnimation.SuppressEffects())
            foreach (var team in new[] { Team.Enemy, Team.Player })
            {
                using (var f = new Fixture(systems, team))
                {
                    VerifyVisiblePriorities(f);
                    VerifyCrystalMemory(f);
                    VerifyCommanderMemory(f);
                    VerifyEmergency(f);
                }
            }
            Check("objective reads preserve both AP ledgers", playerAP == JsonUtility.ToJson(systems.FactionState.PlayerAP)
                && enemyAP == JsonUtility.ToJson(systems.FactionState.EnemyAP));
            Check("objective reads preserve both resource ledgers", playerStock == JsonUtility.ToJson(systems.FactionState.GetResources(Team.Player))
                && enemyStock == JsonUtility.ToJson(systems.FactionState.GetResources(Team.Enemy)));
            Check("objective reads preserve Unity random state", random.Equals(UnityEngine.Random.state));
        }
        finally
        {
            // The fixture uses its own vision, crystal parents and memory. Only global registration/caches need rebuilding.
            UnityEngine.Random.state = random;
            systems.MoveGenerator.UnitPointCore();
        }
        Debug.Log("[AIClosingObjective] " + checks + " observation/objective checks passed");
    }

    static void VerifyVisiblePriorities(Fixture f)
    {
        f.Reset();
        var opponentBuilding = f.Actor(Kind.None, f.Board.OpponentTeam, new Vector3(20, 0, 10), Type.Building);
        var neutralBuilding = f.Actor(Kind.None, Team.Obstacle, new Vector3(20, 0, 11), Type.Building);
        var king = f.Actor(Kind.King, f.Board.OpponentTeam, new Vector3(20, 0, 12));
        f.Observe(opponentBuilding, neutralBuilding, king);
        var policy = new AIEvolutionPolicy(); policy.BeginTurn(f.Board);
        Check(f.Label + " opponent King outranks equally distant ordinary and neutral buildings",
            policy.HasObjective && policy.Objective == king.transform.position);

        f.Reset(); f.Observe(opponentBuilding);
        policy = new AIEvolutionPolicy(); policy.BeginTurn(f.Board);
        Check(f.Label + " ordinary observed contact can start an objective", policy.HasObjective && policy.Objective == opponentBuilding.transform.position);
        f.Observe(king); policy.UpdateObjective(f.Board);
        Check(f.Label + " newly observed King replaces an ordinary objective before its hold expires",
            policy.HasObjective && policy.Objective == king.transform.position);

        f.Reset();
        var combatant = f.Actor(Kind.Knight, f.Board.OpponentTeam, new Vector3(20, 0, 13));
        f.Observe(neutralBuilding, combatant);
        policy = new AIEvolutionPolicy(); policy.BeginTurn(f.Board);
        Check(f.Label + " principal opponent troop outranks equally distant neutral building",
            policy.HasObjective && policy.Objective == combatant.transform.position);
    }

    static void VerifyCrystalMemory(Fixture f)
    {
        f.Reset(); f.ObserveMainCrystal(100);
        Vector3 recorded = f.MainCrystal.transform.position;
        var policy = new AIEvolutionPolicy(); policy.BeginTurn(f.Board);
        Check(f.Label + " visible living main crystal alone starts an objective",
            policy.HasObjective && policy.Objective == recorded && f.Board.AlivePlayerUnits.Count == 0);
        int started = policy.StartedTurn;
        f.HideMainCrystal();
        f.MainCrystal.transform.position = new Vector3(45000, 12, -39000);
        f.MoveHiddenCrystalSystem(f.MainCrystal.transform.position);
        for (int age = 1; age < 5; age++)
        {
            f.Turn(started + age); policy.UpdateObjective(f.Board);
            Check(f.Label + " main crystal memory retains its observed position, age " + age,
                policy.HasObjective && policy.Objective == recorded && policy.StartedTurn == started);
        }
        Check(f.Label + " remembered crystal never grants a hidden attack target", f.Board.GetAttackTargets(f.Own).Count == 0);
        f.Turn(started + 5); policy.UpdateObjective(f.Board);
        Check(f.Label + " hidden crystal intent expires at the five-turn boundary", !policy.HasObjective);

        f.Reset(); f.ObserveMainCrystal(100);
        policy = new AIEvolutionPolicy(); policy.BeginTurn(f.Board);
        f.ObserveMainCrystal(0); policy.UpdateObjective(f.Board);
        Check(f.Label + " observed destroyed crystal clears intent immediately", !policy.HasObjective);
        policy = new AIEvolutionPolicy(); policy.BeginTurn(f.Board);
        Check(f.Label + " destroyed crystal memory cannot start a new objective", !policy.HasObjective);

        f.Reset(); f.ObserveMainCrystal(100); f.HideMainCrystal(); f.Board.ReconThreatLevel = 3;
        policy = new AIEvolutionPolicy(); policy.BeginTurn(f.Board);
        Check(f.Label + " low threat does not acquire unseen remembered crystal intent", !policy.HasObjective);
    }

    static void VerifyCommanderMemory(Fixture f)
    {
        f.Reset();
        var king = f.Actor(Kind.King, f.Board.OpponentTeam, new Vector3(21, 0, 10));
        f.Observe(king); f.RememberCommander(king);
        Vector3Int recorded = GridHelper.ToGrid(king.transform.position);
        int observedTurn = f.Board.TurnCount;
        f.Board.AlivePlayerUnits.Clear(); f.Seen.Clear();
        king.transform.position = new Vector3(-43000, 17, 47000); f.Changed();
        Check(f.Label + " principal commander memory reads the snapshot, not the hidden actor",
            f.Board.GetLastKnownPrincipalCommander(out var position, out int stamp) && position == recorded && stamp == observedTurn);
        var policy = new AIEvolutionPolicy(); policy.BeginTurn(f.Board);
        Check(f.Label + " remembered principal King can guide deployment", policy.HasObjective && policy.Objective == (Vector3)recorded);
        Check(f.Label + " remembered King never grants a hidden attack target", f.Board.GetAttackTargets(f.Own).Count == 0);
        f.Turn(observedTurn + 4);
        Check(f.Label + " principal commander memory remains available at age four",
            f.Board.GetLastKnownPrincipalCommander(out position, out stamp) && position == recorded);
        f.Turn(observedTurn + 5);
        Check(f.Label + " principal commander memory expires at age five", !f.Board.GetLastKnownPrincipalCommander(out _, out _));
        f.Turn(observedTurn + 1); f.Board.ReconThreatLevel = 3;
        Check(f.Label + " low threat cannot use commander memory", !f.Board.GetLastKnownPrincipalCommander(out _, out _));
        f.Board.ReconThreatLevel = 15; f.Seen.Add(GridHelper.ToGridXZ(recorded)); f.Changed();
        Check(f.Label + " visible empty former King position is not a current commander target",
            !f.Board.GetLastKnownPrincipalCommander(out _, out _));

        f.Reset();
        f.Memory[24001] = new AIBoardState.LastKnownInfo { Valid = true, Kind = Kind.King, Type = Type.Unit,
            Position = new Vector3Int(21, 0, 10), Turn = f.Board.TurnCount, WasPrincipalCommander = false };
        f.Changed();
        Check(f.Label + " unclassified or neutral King history cannot become a principal objective",
            !f.Board.GetLastKnownPrincipalCommander(out _, out _));

        f.Reset();
        var custom = f.Actor(Kind.Knight, f.Board.OpponentTeam, new Vector3(21, 0, 11)); custom.IsPrimaryCommander = true;
        f.RememberCommander(custom);
        Check(f.Label + " custom primary commander is recognized from observed decisive classification",
            f.Board.GetLastKnownPrincipalCommander(out position, out _) && position == GridHelper.ToGrid(custom.transform.position));
    }

    static void VerifyEmergency(Fixture f)
    {
        f.Reset(); f.ObserveMainCrystal(100);
        var policy = new AIEvolutionPolicy(); policy.BeginTurn(f.Board);
        Set(f.Board, "EnemyCrystalHP", 20); f.Changed(); policy.UpdateObjective(f.Board);
        Check(f.Label + " own crystal emergency clears offensive intent", !policy.HasObjective);
        f.Reset(); f.ObserveMainCrystal(100);
        policy = new AIEvolutionPolicy(); policy.BeginTurn(f.Board);
        var attacker = f.Actor(Kind.Knight, f.Board.OpponentTeam, new Vector3(12, 0, 10));
        f.Observe(attacker); policy.UpdateObjective(f.Board);
        Check(f.Label + " observed adjacent opponent threat interrupts offense", !policy.HasObjective);
    }

    sealed class Fixture : IDisposable
    {
        readonly List<GameObject> objects = new List<GameObject>();
        readonly CrystalSystem crystals;
        public readonly AIBoardState Board;
        public readonly Dictionary<int, AIBoardState.LastKnownInfo> Memory = AIBoardState.CreateSharedMemory();
        public readonly HashSet<Vector3Int> Seen = new HashSet<Vector3Int>();
        public readonly Status Own, MainCrystal;
        public string Label => Board.ActorTeam.ToString();

        public Fixture(GameSystems systems, Team team)
        {
            var config = Object("Closing objective isolated configuration", false);
            var vision = config.AddComponent<VisionGenerator>();
            typeof(VisionGenerator).GetField("_enemyVisionBox", Hidden).SetValue(vision, Seen);
            typeof(VisionGenerator).GetField("_playerVisionBox", Hidden).SetValue(vision, Seen);
            typeof(VisionGenerator).GetField("_enemyExplored", Hidden).SetValue(vision, new HashSet<Vector3Int>());
            typeof(VisionGenerator).GetField("_playerExplored", Hidden).SetValue(vision, new HashSet<Vector3Int>());
            crystals = config.AddComponent<CrystalSystem>();
            var ownCrystal = Actor(Kind.Crystal, team, new Vector3(10, 0, 10), Type.Building);
            Team opponent = team == Team.Player ? Team.Enemy : Team.Player;
            MainCrystal = Actor(Kind.Crystal, opponent, new Vector3(22, 0, 10), Type.Building);
            crystals.Playercrystal = team == Team.Player ? ownCrystal.transform : MainCrystal.transform;
            crystals.Enemycrystal = team == Team.Player ? MainCrystal.transform : ownCrystal.transform;
            crystals.PCP = crystals.Playercrystal.position; crystals.ECP = crystals.Enemycrystal.position;
            Board = new AIBoardState(systems.MoveGenerator, systems.AttackGenerator, systems.APSystem, systems.UnitSetting,
                crystals, vision, systems.BuildSystem, systems.SummonSystem, systems.FactionState, systems.SubCrystalSystem,
                10, Memory, team) { MapCreate = systems.MapCreate, ReconThreatLevel = 15 };
            Own = Actor(Kind.Knight, team, new Vector3(12, 0, 10));
            Reset();
        }

        GameObject Object(string name, bool active)
        {
            var value = new GameObject(name); value.SetActive(active); objects.Add(value); return value;
        }

        public Status Actor(Kind kind, Team team, Vector3 position, Type type = Type.Unit)
        {
            var value = Object("Closing objective fixture " + kind, false);
            var actor = value.AddComponent<Status>(); actor.kind = kind; actor.team = team; actor.type = type;
            actor.MaxHP = 100; actor.HP = 100; actor.ATK = 20; actor.DEF = 5; actor.direction = Direction.N;
            value.transform.position = position; value.SetActive(true); return actor;
        }

        public void Reset()
        {
            Board.AliveEnemyUnits.Clear(); Board.AliveEnemyUnits.Add(Own);
            Board.AlivePlayerUnits.Clear(); Memory.Clear(); Seen.Clear();
            Board.ReconThreatLevel = 15;
            Set(Board, "TurnCount", 10); Set(Board, "EnemyCrystalPos", new Vector3(10, 0, 10));
            Set(Board, "EnemyCrystalHP", 100); Set(Board, "EnemyCrystalMaxHP", 100);
            Set(Board, "PlayerCrystalVisible", false); Set(Board, "PlayerCrystalHP", -1); Set(Board, "PlayerCrystalPos", Vector3.zero);
            typeof(AIBoardState).GetField("_lastKnownPlayerCrystal", Hidden).SetValue(Board, default(AIBoardState.LastKnownInfo));
            MainCrystal.transform.position = new Vector3(22, 0, 10); MoveHiddenCrystalSystem(MainCrystal.transform.position);
            Changed();
        }

        public void Observe(params Status[] actors)
        {
            foreach (var actor in actors)
            {
                if (!Board.AlivePlayerUnits.Contains(actor)) Board.AlivePlayerUnits.Add(actor);
                Seen.Add(GridHelper.ToGridXZ(actor.transform.position));
            }
            Changed();
        }

        public void RememberCommander(Status actor)
        {
            Memory[actor.GetInstanceID()] = new AIBoardState.LastKnownInfo { Valid = true, Kind = actor.kind, Type = actor.type,
                Position = GridHelper.ToGrid(actor.transform.position), Turn = Board.TurnCount,
                WasPrincipalCommander = actor.team == Board.OpponentTeam && MatchObjectiveRules.IsCommander(actor) };
            Changed();
        }

        public void ObserveMainCrystal(int hp)
        {
            Seen.Add(GridHelper.ToGridXZ(MainCrystal.transform.position));
            Set(Board, "PlayerCrystalVisible", true); Set(Board, "PlayerCrystalHP", hp);
            Set(Board, "PlayerCrystalPos", MainCrystal.transform.position);
            var memory = new AIBoardState.LastKnownInfo { Valid = true, Kind = Kind.Crystal, Type = Type.Building,
                Position = GridHelper.ToGrid(MainCrystal.transform.position), Turn = Board.TurnCount };
            Memory[int.MinValue] = memory;
            typeof(AIBoardState).GetField("_lastKnownPlayerCrystal", Hidden).SetValue(Board, memory);
            Changed();
        }

        public void HideMainCrystal()
        {
            Seen.Clear(); Set(Board, "PlayerCrystalVisible", false); Set(Board, "PlayerCrystalHP", -1); Changed();
        }

        public void MoveHiddenCrystalSystem(Vector3 position)
        {
            if (MainCrystal.team == Team.Player) crystals.PCP = position; else crystals.ECP = position;
        }

        public void Turn(int value) { Set(Board, "TurnCount", value); Changed(); }
        public void Changed()
        {
            Set(Board, "Generation", Board.Generation + 1);
            typeof(AIBoardState).GetField("_visionCacheVersion", Hidden).SetValue(Board, -1);
            Board.Allies.Rebuild(Board.AliveEnemyUnits); Board.Enemies.Rebuild(Board.AlivePlayerUnits);
        }

        public void Dispose()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
            objects.Clear();
        }
    }
}
#endif
