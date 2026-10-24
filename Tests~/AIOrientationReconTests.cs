#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>Free facing changes use owned masks and observed objectives, never an unseen occupant.</summary>
public static class AIOrientationReconTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static void Check(string label, bool condition)
    { if (!condition) throw new InvalidOperationException("[AIOrientationRecon] FAIL " + label); checks++; Debug.Log("[AIOrientationRecon] PASS " + label); }

    public static void Run(GameSystems systems, TurnGenerator turn)
    {
        if (!Application.isPlaying || systems?.MapCreate == null || turn == null)
            throw new InvalidOperationException("An initialized native match is required.");
        checks = 0;
        var failures = new List<Exception>();
        using (BoardActorAnimation.SuppressEffects())
            foreach (var team in new[] { Team.Player, Team.Enemy })
            {
                Try(() => VisibilityBeforeAdvance(systems, team));
                Try(() => ObservedCrystalFacing(systems, team));
            }
        if (failures.Count > 0) throw new AggregateException("Orientation native scenarios failed", failures);
        Debug.Log("[AIOrientationRecon] ALL PASSED " + checks + " (actual rules vision and executor, controlled flat map)");
        void Try(Action scenario)
        { try { scenario(); } catch (Exception error) { Debug.LogException(error); failures.Add(error); } }
    }

    static void VisibilityBeforeAdvance(GameSystems live, Team team)
    {
        using var f = new Fixture(live, team);
        var actor = f.Actor;
        var origin = actor.transform.position;
        int rear = actor.direction == Direction.N ? -1 : 1;
        var next = origin + Vector3.forward * rear;
        var territory = origin + Vector3.forward * rear * 3;
        var foe = f.Economy.Opponent(next, 0);
        foe.HP = foe.MaxHP = 500;
        // Earlier legitimate sight is an explicit fixture input; then production vision takes over.
        f.Territory.GetTerritory(f.Board.OpponentTeam).Add(territory);
        f.Seen.Add(GridHelper.ToGridXZ(territory));
        for (int i = 0; i <= 3; i++) f.Explored.Add(GridHelper.ToGridXZ(origin + Vector3.forward * rear * i));
        f.Board.Refresh(); f.Board.TerritoryIntel.Observe(f.Board);
        f.RefreshVision(); f.Board.TerritoryIntel.Update(f.Board, false, null);
        var before = f.Board.TerritoryIntel.CaptureState();
        Check(team + " controlled observed territory supplies a known approach behind the army", before != null
            && f.Board.TerritoryIntel.HasWaypoint && f.Board.TerritoryIntel.Waypoint.z == (int)origin.z + rear * 2);
        Check(team + " all-direction movement does not imply all-direction vision", MovePatterns.IsDirectionIndependent(actor)
            && f.Board.GetValidMoves(actor).Contains(next) && !f.Board.IsVisibleToEnemy(next)
            && !f.Board.AlivePlayerUnits.Contains(foe) && f.Board.GetAttackTargets(actor).Count == 0);
        float current = AIOrientation.AttackUtility(f.Board, actor, actor.direction);
        var alternate = actor.direction == Direction.N ? Direction.S : Direction.N;
        float other = AIOrientation.AttackUtility(f.Board, actor, alternate);
        var proposals = f.Candidates();
        var rotate = proposals.SingleOrDefault(a => a.Unit == actor && a.TargetDirection == alternate);
        Debug.Log("[AIOrientationReconReproduction] team=" + team + " kind=" + actor.kind + " facing=" + actor.direction
            + " moveIndependent=" + MovePatterns.IsDirectionIndependent(actor) + " next=" + next + " nextVisible=" + f.Board.IsVisibleToEnemy(next)
            + " observedFoes=" + f.Board.AlivePlayerUnits.Count + " currentAttack=" + current + " alternateAttack=" + other
            + " waypoint=" + f.Board.TerritoryIntel.Waypoint + " rotates=" + proposals.Count);
        Check(team + " known unobserved advance gets a free visibility preparation candidate", rotate != null
            && rotate.APCost == 0 && current == 0 && other == 0 && actor.transform.position == origin);
        Check(team + " shared preparation classifier recognizes only the same free rotation", AIOrientation.IsVisibilityPreparation(rotate, f.Board)
            && !AIOrientation.IsVisibilityPreparation(new AIAction { ActionType = AIActionType.Move, Unit = actor, TargetPos = next }, f.Board)
            && !AIOrientation.IsVisibilityPreparation(new AIAction { ActionType = AIActionType.Rotate, Unit = actor, TargetDirection = alternate, APCost = 1 }, f.Board));

        float score = rotate.Score;
        foe.transform.position = new Vector3(28, 21, 28); foe.ATK = 12345; foe.HP = 1;
        f.Board.Refresh(); var withoutBlocker = f.Candidates().SingleOrDefault(a => a.Unit == actor);
        Check(team + " unseen actor location and combat stats cannot justify or score rotation", withoutBlocker != null
            && withoutBlocker.TargetDirection == alternate && withoutBlocker.Score == score && !f.Board.AlivePlayerUnits.Contains(foe));
        foe.transform.position = next; foe.ATK = 0; foe.HP = 500; f.Board.Refresh();

        int ap = f.Economy.State.GetAP(team), generation = f.Board.Generation;
        Check(team + " production rotate executes without moving or spending AP", f.Executor.Execute(rotate, f.Board)
            && actor.direction == alternate && actor.transform.position == origin && f.Economy.State.GetAP(team) == ap
            && f.Board.Generation > generation);
        Check(team + " real new sight replaces a blind move with an observed attack opportunity", f.Vision.IsActorVisibleTo(team, foe)
            && f.Board.AlivePlayerUnits.Contains(foe) && !f.Board.GetValidMoves(actor).Contains(next)
            && f.Board.GetAttackTargets(actor).Contains(foe));
        Check(team + " visibility rotation cannot repeat within the owner turn", !f.Candidates().Any(a => a.Unit == actor)
            && !AIOrientation.IsVisibilityPreparation(rotate, f.Board)
            && !f.Executor.Execute(new AIAction { ActionType = AIActionType.Rotate, Unit = actor, TargetDirection = alternate == Direction.N ? Direction.S : Direction.N }, f.Board));

        actor.direction = alternate == Direction.N ? Direction.S : Direction.N; f.Board.RotatedUnits.Clear();
        foe.transform.position = origin + Vector3.forward * -rear; f.RefreshVision();
        int hpDamage = AIActionEvaluator.EstimateEffectiveSpecialDamage(actor, foe, f.Board, out _);
        int totalImpact = AIActionEvaluator.EstimateEffectiveSpecialImpact(actor, foe, f.Board, out _);
        Debug.Log("[AIOrientationBarrierReproduction] team=" + team + " barrier=" + foe.Barrier + " hpDamage=" + hpDamage
            + " observedImpact=" + totalImpact + " orientationUtility=" + AIOrientation.AttackUtility(f.Board, actor, actor.direction));
        Check(team + " current attack has real observed barrier impact even without HP damage", foe.Barrier > 0 && hpDamage == 0 && totalImpact > 0);
        Check(team + " preparation cannot turn away from a currently useful affordable attack", AIOrientation.AttackUtility(f.Board, actor, actor.direction) > 0
            && !f.Candidates().Any(a => a.Unit == actor));
        foe.transform.position = new Vector3(28, 21, 28); SpecialSkillRuntime.State(actor).visionZeroTurns = 1; f.RefreshVision();
        Check(team + " sight-zero cannot justify a visibility preparation", !f.Candidates().Any(a => a.Unit == actor));
        SpecialSkillRuntime.State(actor).visionZeroTurns = 0; f.Profile.vision.directionIndependent = true; f.RefreshVision();
        Check(team + " unchanged facing-independent vision cannot justify a preparation", !f.Candidates().Any(a => a.Unit == actor));
        f.Profile.vision.directionIndependent = false; f.Economy.State.SetAP(team, 0); f.RefreshVision();
        Check(team + " no affordable progress move does not justify a preparation", !f.Candidates().Any(a => a.Unit == actor));
        f.Economy.State.SetAP(team, ap);

        // Removing all observed goals must not create an arbitrary scan of the hidden world.
        f.Board.RotatedUnits.Clear(); f.Territory.GetTerritory(f.Board.OpponentTeam).Clear(); f.Board.TerritoryIntel.Reset(team);
        f.RefreshVision();
        Check(team + " no observed objective does not justify purposeless rotation", !f.Candidates().Any(a => a.Unit == actor));
    }

    static void ObservedCrystalFacing(GameSystems live, Team team)
    {
        using var f = new Fixture(live, team);
        var actor = f.Actor;
        int rear = actor.direction == Direction.N ? -1 : 1;
        var target = actor.transform.position + Vector3.forward * rear;
        var crystalObject = new GameObject("Observed opposing main crystal");
        crystalObject.transform.SetParent(f.Board.OpponentCrystalParent); crystalObject.transform.position = target;
        var crystal = crystalObject.AddComponent<Status>(); crystal.team = f.Board.OpponentTeam; crystal.type = Type.Building;
        crystal.kind = Kind.Crystal; crystal.HP = crystal.MaxHP = 500;
        f.SetOpponentCrystal(target);
        var observer = f.Economy.Unit(); observer.transform.position = target + Vector3.forward * rear;
        observer.direction = actor.direction; observer.GrowthData.actionProfile = f.Profile;
        f.RefreshVision();
        var alternate = actor.direction == Direction.N ? Direction.S : Direction.N;
        float current = AIOrientation.AttackUtility(f.Board, actor, actor.direction), other = AIOrientation.AttackUtility(f.Board, actor, alternate);
        Debug.Log("[AIOrientationCrystalReproduction] team=" + team + " visible=" + f.Board.PlayerCrystalVisible
            + " observedCrystal=" + f.Board.IsSpecialActorObserved(crystal) + " ordinaryFoes=" + f.Board.AlivePlayerUnits.Count
            + " currentAttack=" + current + " alternateAttack=" + other);
        Check(team + " allied observer sees the real separate-parent main crystal", f.Board.PlayerCrystalVisible
            && f.Board.IsSpecialActorObserved(crystal) && f.Board.AlivePlayerUnits.Count == 0);
        var rotate = f.Candidates().SingleOrDefault(a => a.Unit == actor);
        Check(team + " an observed main crystal can justify the facing needed for its legal attack", current == 0 && other > 0
            && rotate != null && rotate.TargetDirection == alternate && rotate.APCost == 0);
        Check(team + " observed attack preparation has its own legal observed classifier", AIOrientation.IsObservedAttackPreparation(rotate, f.Board));
        int ap = f.Economy.State.GetAP(team);
        Check(team + " production rotation opens the real main-crystal attack with exact AP preserved", f.Executor.Execute(rotate, f.Board)
            && f.Economy.State.GetAP(team) == ap && f.Board.GetAttackTargets(actor).Contains(crystal));

        actor.direction = alternate == Direction.N ? Direction.S : Direction.N; f.Board.RotatedUnits.Clear();
        observer.transform.position = new Vector3(28, 21, 28); f.RefreshVision();
        Check(team + " a hidden main crystal cannot justify rotation through its global parent", !f.Board.PlayerCrystalVisible
            && !f.Candidates().Any(a => a.Unit == actor));
    }

    sealed class Fixture : IDisposable
    {
        public readonly EconomyR2Tests.Fixture Economy;
        public readonly Status Actor;
        public readonly BoardActionProfile Profile;
        public readonly AIBoardState Board;
        public readonly AIActionExecutor Executor;
        public readonly VisionGenerator Vision;
        public readonly TerritorySystem Territory;
        public readonly HashSet<Vector3Int> Seen = new HashSet<Vector3Int>(), Explored = new HashSet<Vector3Int>();
        readonly GameObject host;
        readonly MapCreate map;
        readonly MoveGenerator moves;
        public Fixture(GameSystems live, Team team)
        {
            Economy = new EconomyR2Tests.Fixture(live, team);
            host = new GameObject("Orientation rules native isolated hub"); host.SetActive(false);
            Profile = ScriptableObject.CreateInstance<BoardActionProfile>();
            try
            {
                // A small native geometry at altitude 21 keeps live-scene colliders out of this controlled rules test.
                map = host.AddComponent<MapCreate>(); map.maxX = map.maxZ = 32; map.minY = 0; map.maxY = 22; map.UseR1Terrain = false;
                var heights = new int[32, 32]; map.SetPos = new List<Vector3>(1024);
                for (int x = 0; x < 32; x++) for (int z = 0; z < 32; z++) { heights[x, z] = 20; map.SetPos.Add(new Vector3(x, 21, z)); }
                Set(map, "topY", heights); Set(map, "rivers", new bool[32, 32]);
                var hub = host.AddComponent<TurnGenerator>(); hub.MarkDeveloperMatch(); hub.Context.Turn = 60;
                moves = host.AddComponent<MoveGenerator>(); moves.mapcreate = map; moves.turnGenerator = hub;
                Set(moves, "crystalsystem", Economy.Crystals); Set(moves, "unitsetting", Economy.Units);
                Set(moves, "PlayerUnit", Economy.Units.PlayerUnit); Set(moves, "EnemyUnit", Economy.Units.EnemyUnit);
                Territory = host.AddComponent<TerritorySystem>(); Territory.PTSetPos = new List<Vector3>(); Territory.ETSetPos = new List<Vector3>();
                Vision = host.AddComponent<VisionGenerator>(); Vision.Awake();
                Set(Vision, "PlayerUnit", Economy.Units.PlayerUnit); Set(Vision, "EnemyUnit", Economy.Units.EnemyUnit); Set(Vision, "territorysystem", Territory);
                Set(Vision, team == Team.Player ? "_playerVisionBox" : "_enemyVisionBox", Seen);
                Set(Vision, team == Team.Player ? "_playerExplored" : "_enemyExplored", Explored);
                Economy.Crystal.transform.position = new Vector3(-2000, 21, -2000); Economy.SetOwnCrystalPosition(Economy.Crystal.transform.position);
                SetOpponentCrystal(new Vector3(31, 21, 31));
                Actor = Economy.Unit(); Actor.transform.position = new Vector3(10, 21, 10); Actor.direction = team == Team.Player ? Direction.N : Direction.S; Actor.ATK = 30;
                Profile.actionType = ActorActionType.Combat; Profile.movement.useCustom = Profile.attack.useCustom = Profile.vision.useCustom = true;
                Profile.movement.directionIndependent = true; Profile.attack.directionIndependent = Profile.vision.directionIndependent = false;
                foreach (var p in new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down }) Profile.movement.SetCell(3 + p.x, 3 + p.y, true);
                Profile.attack.SetCell(3, 4, true); Profile.vision.SetCell(3, 3, true);
                for (int x = 2; x <= 4; x++) for (int z = 4; z <= 5; z++) Profile.vision.SetCell(x, z, true);
                Actor.GrowthData.actionProfile = Profile;
                hub.Systems.MapCreate = map; hub.Systems.MoveGenerator = moves; hub.Systems.UnitSetting = Economy.Units;
                hub.Systems.CrystalSystem = Economy.Crystals; hub.Systems.FactionState = Economy.State; hub.Systems.APSystem = Economy.AP;
                hub.Systems.VisionGenerator = Vision;
                Board = new AIBoardState(moves, live.AttackGenerator, Economy.AP, Economy.Units, Economy.Crystals, Vision,
                    null, null, Economy.State, null, 60, null, team) { MapCreate = map, TerritorySystem = Territory, ReconThreatLevel = 15 };
                Executor = new AIActionExecutor(hub, moves, live.AttackGenerator, live.BattleSystem, Economy.AP, live.SkillSystem,
                    null, null, null, new AILearning(false), team);
                RefreshVision();
            }
            catch { Dispose(); throw; }
        }
        public List<AIAction> Candidates() { var result = new List<AIAction>(); AIOrientation.AppendCandidates(Board, result); return result; }
        public void RefreshVision() { Vision.MarkVisionDirty(); Vision.VisionPoint(map, moves, Economy.Crystals); Board.Refresh(); }
        public void SetOpponentCrystal(Vector3 at) { if (Economy.Team == Team.Player) Economy.Crystals.ECP = at; else Economy.Crystals.PCP = at; }
        public void Dispose() { if (host != null) UnityEngine.Object.DestroyImmediate(host); if (Profile != null) UnityEngine.Object.DestroyImmediate(Profile); Economy?.Dispose(); }
    }
    static void Set(object source, string name, object value) => source.GetType().GetField(name, Hidden).SetValue(source, value);
}
#endif
