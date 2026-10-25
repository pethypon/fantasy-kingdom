#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Observed, moderate-damage individual preservation contracts. No production rules are changed.</summary>
public static class AIR3PreservationTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks, failures;
    static void Check(string label, bool condition)
    {
        if (!condition) throw new InvalidOperationException("[AIR3Preservation] FAIL " + label);
        checks++; Debug.Log("[AIR3Preservation] PASS " + label);
    }
    static void Case(string label, Action test)
    {
        try { test(); }
        catch (Exception error) { failures++; Debug.Log("[AIR3PreservationCase] FAIL " + label + " " + (error.InnerException ?? error).Message); }
    }
    public static void Run(GameSystems live, TurnGenerator turn)
    {
        checks = failures = 0;
        string nations = JsonUtility.ToJson(live.FactionState.PlayerNation) + JsonUtility.ToJson(live.FactionState.EnemyNation);
        var random = UnityEngine.Random.state;
        using (BoardActorAnimation.SuppressEffects())
            foreach (var team in new[] { Team.Player, Team.Enemy })
            {
                foreach (int level in new[] { 1, 20, 50, 100 })
                    foreach (int hp in new[] { 20, 35, 50, 60 })
                        Case(team + " Lv" + level + " HP" + hp, () => Attrition(live, team, level, hp));
                Case(team + " WoundedSingleSurvivor", () => Single(live, team));
                Case(team + " no AP", () => Unavailable(live, team));
                Case(team + " healthy winning fight", () => Fight(live, team));
                Case(team + " allied killing support", () => Support(live, team));
                Case(team + " actual recovery and return", () => Recovery(live, team));
                Case(team + " no fictitious healing", () => NoHealing(live, team));
                Case(team + " hidden actor invariance", () => HiddenActor(live, team));
                Case(team + " persistence bounds", () => Persistence(live, team));
            }
        Check("tests preserve both live nations and resources", nations == JsonUtility.ToJson(live.FactionState.PlayerNation) + JsonUtility.ToJson(live.FactionState.EnemyNation));
        Check("tests preserve gameplay random state", random.Equals(UnityEngine.Random.state));
        Debug.Log("[AIR3Preservation] RESULT checks=" + checks + " failedCases=" + failures);
        if (failures > 0) throw new InvalidOperationException("[AIR3Preservation] " + failures + " failed cases");
    }
    sealed class Fixture : IDisposable
    {
        public readonly AITerritorySafetyTests.Fixture Inner;
        public readonly BoardActionProfile ThreatProfile;
        public Status Actor => Inner.Actor;
        public Status Foe => Inner.Foe;
        public Vector3 Origin => Inner.Cells[1];
        public Vector3 Safety => Origin + Vector3.forward;
        public Fixture(GameSystems live, Team team, int level, int hp, int attack)
        {
            Inner = new AITerritorySafetyTests.Fixture(live, team, true);
            ThreatProfile = ScriptableObject.CreateInstance<BoardActionProfile>();
            ThreatProfile.actionType = ActorActionType.Combat;
            ThreatProfile.movement.useCustom = ThreatProfile.attack.useCustom = true;
            ThreatProfile.attack.directionIndependent = true;
            ThreatProfile.attack.SetCell(2, 3, true); ThreatProfile.attack.SetCell(1, 3, true);
            Foe.GrowthData.actionProfile = ThreatProfile; Foe.transform.position = Origin + Vector3.right;
            Foe.HP = Foe.MaxHP = 1000; Foe.ATK = attack; Foe.Barrier = 0;
            Actor.Level = level; Actor.HP = Actor.MaxHP = 100; Actor.HP = hp; Actor.Barrier = 0; Actor.ATK = 1;
            Actor.transform.position = Origin;
            // A Knight has a real blind-side damage multiplier. These cases isolate ordinary
            // nonlethal attrition, so each known opponent must also be in the actor's own footprint.
            Actor.VisionCell.Clear(); Actor.VisionCell.Add(GridHelper.ToGrid(Foe.transform.position));
            Foe.VisionCell.Clear(); Foe.VisionCell.Add(GridHelper.ToGrid(Actor.transform.position));
            Inner.Economy.Resources.Citizen = 20;
            foreach (var cell in new[] { Origin, Foe.transform.position, Safety, Origin - Vector3.right })
            { Inner.Explored.Add(GridHelper.ToGridXZ(cell)); Inner.See(cell); }
        }
        public AIBoardState Board(int turn = 60) => Inner.Board(turn);
        public AIAction Attack(AIBoardState board) => new AIAction { ActionType = AIActionType.Attack, Unit = Actor,
            TargetUnit = Foe, TargetPos = Foe.transform.position, APCost = board.CalcAttackCost(Actor), Score = 10000 };
        public AIAction Retreat(AIBoardState board)
        {
            var list = new List<AIAction>(); AIActionGenerator.GenerateRetreatCandidates(Actor, board, list);
            var result = list.Find(a => a.TargetPos == Safety);
            if (result != null) result.Score = 1;
            return result;
        }
        public void Dispose() { UnityEngine.Object.DestroyImmediate(ThreatProfile); Inner.Dispose(); }
    }
    static void Attrition(GameSystems live, Team team, int level, int hp)
    {
        // Current production raw-to-effective damage is lower than raw ATK. Select a moderate
        // raw packet, then assert the actual observed loss instead of assuming ATK == HP loss.
        using var f = new Fixture(live, team, level, hp, Mathf.CeilToInt(hp * .85f));
        var board = f.Board(); var governor = board.Governor;
        float incoming = governor.IncomingDamage(board, f.Actor, f.Origin, null);
        Debug.Log("[AIR3PreservationPrecondition] team=" + team + " level=" + level + " hp=" + hp
            + " actualHP=" + f.Actor.HP + " incoming=" + incoming + " rawATK=" + f.Foe.ATK + " selfSees=" + SpecialSkillRuntime.InVision(f.Actor, f.Foe));
        Check(team + " moderate observed hit is nonlethal now but dangerous across continued exchange", incoming > 0 && incoming < hp && incoming * 2 >= hp);
        var retreat = f.Retreat(board); var attack = f.Attack(board);
        Check(team + " lateral escape is actually legal and safe", retreat != null && board.GetValidMoves(f.Actor).Contains(retreat.TargetPos)
            && governor.IncomingDamage(board, f.Actor, retreat.TargetPos, null) == 0);
        var choices = new List<AIAction> { attack, retreat }; governor.Filter(choices, board);
        Debug.Log("[AIR3PreservationEvidence] team=" + team + " level=" + level + " hp=" + hp + " incoming=" + incoming
            + " retreatP=" + retreat.StrategicPriority + " attackP=" + attack.StrategicPriority + " mode=" + governor.Mode);
        Check(team + " nonlethal attrition rescue survives the strategic priority gate", choices.Contains(retreat)
            && retreat.StrategicPriority <= 1 && retreat.StrategicPriority < attack.StrategicPriority);
        Check(team + " actual selector chooses the legal nonlethal retreat", ReferenceEquals(Select(live.AICommander, board, choices), retreat));
    }
    static void Single(GameSystems live, Team team)
    {
        using var f = new Fixture(live, team, 20, 35, 10);
        var board = f.Board(); var retreat = f.Retreat(board); var attack = f.Attack(board);
        float incoming = board.Governor.IncomingDamage(board, f.Actor, f.Origin, null);
        Debug.Log("[AIR3PreservationPrecondition] single team=" + team + " hp=" + f.Actor.HP + " incoming=" + incoming + " ownCount=" + board.AliveEnemyUnits.Count);
        Check(team + " single wounded case is neither an immediate nor a two-hit death", incoming > 0 && incoming * 2 < f.Actor.HP
            && f.Actor.HP == 35 && board.AliveEnemyUnits.Count == 1);
        var actions = new List<AIAction> { attack, retreat }; board.Governor.Filter(actions, board);
        Check(team + " a lone wounded survivor can take a safe recovery position without a whole-army retreat", retreat != null
            && retreat.StrategicPriority <= 1 && ReferenceEquals(Select(live.AICommander, board, actions), retreat));
    }
    static void Unavailable(GameSystems live, Team team)
    {
        using var f = new Fixture(live, team, 50, 35, 20);
        f.Inner.Economy.State.SetAP(team, 0); var board = f.Board();
        var candidates = new List<AIAction>(); AIActionGenerator.GenerateRetreatCandidates(f.Actor, board, candidates);
        Check(team + " paid retreat is never invented without AP", candidates.Count == 0);
        f.Inner.Economy.State.SetAP(team, 40);
        foreach (var effect in new[] { StatusEffectType.Stun, StatusEffectType.Freeze })
        {
            f.Actor.ActiveEffects.Add(new ActiveEffect(effect, 1)); board = f.Board(61); candidates.Clear();
            AIActionGenerator.GenerateRetreatCandidates(f.Actor, board, candidates);
            Check(team + " no retreat is invented through " + effect, candidates.Count == 0);
            f.Actor.ActiveEffects.Clear();
        }
    }
    static void Fight(GameSystems live, Team team)
    {
        using var f = new Fixture(live, team, 20, 60, 20);
        f.Foe.HP = 1; f.Actor.ATK = 100;
        var board = f.Board(); var attack = f.Attack(board); var retreat = f.Retreat(board);
        var candidates = new List<AIAction> { attack, retreat }; board.Governor.Filter(candidates, board);
        Check(team + " killing the observed threat remains preferable to unnecessary retreat", candidates.Contains(attack)
            && ReferenceEquals(Select(live.AICommander, board, candidates), attack));
    }
    static void Support(GameSystems live, Team team)
    {
        using var f = new Fixture(live, team, 50, 35, 10);
        var ally = f.Inner.Economy.Unit(); ally.transform.position = f.Foe.transform.position + Vector3.forward;
        ally.GrowthData.actionProfile = f.Actor.GrowthData.actionProfile; ally.HP = ally.MaxHP = 100; ally.ATK = 100;
        ally.VisionCell.Clear(); ally.VisionCell.Add(GridHelper.ToGrid(f.Foe.transform.position)); f.Inner.See(ally.transform.position);
        f.Foe.HP = 1; var board = f.Board();
        var rescue = new AIAction { ActionType = AIActionType.Attack, Unit = ally, TargetUnit = f.Foe,
            TargetPos = f.Foe.transform.position, APCost = board.CalcAttackCost(ally), Score = 10000 };
        var retreat = f.Retreat(board);
        Check(team + " healthy support has a real legal killing attack", board.GetAttackTargets(ally).Contains(f.Foe)
            && AIActionEvaluator.CanFinishSpecialTarget(ally, f.Foe, board));
        var candidates = new List<AIAction> { rescue, retreat }; board.Governor.Filter(candidates, board);
        Check(team + " removing a wounded ally's observed threat competes with retreat", rescue.StrategicPriority <= 1
            && ReferenceEquals(Select(live.AICommander, board, candidates), rescue));
    }
    static void Recovery(GameSystems live, Team team)
    {
        using var f = new Fixture(live, team, 50, 35, 10);
        var board = f.Board(); var governor = board.Governor; var retreat = f.Retreat(board);
        var assessment = governor.AssessPreservation(f.Actor, board);
        Check(team + " nonlethal wound records individual withdrawal without global regroup", assessment.Safety == AIUnitSafetyLevel.WithdrawalRecommended
            && assessment.Phase == AIUnitRecoveryPhase.Withdrawing && assessment.HasLegalSaferMove && assessment.HealingPossible);
        int ap = f.Inner.Economy.State.GetAP(team);
        Check(team + " moderate wound retreat executes on the real movement and AP path", Execute(live, f, retreat, board)
            && f.Actor.transform.position == f.Safety && f.Inner.Economy.State.GetAP(team) == ap - retreat.APCost);
        f.Actor.HasMovedThisTurn = false; board = f.Board(61);
        assessment = governor.AssessPreservation(f.Actor, board);
        Check(team + " an evacuated injured unit keeps recovery phase in a safe position", assessment.Phase == AIUnitRecoveryPhase.Recovering
            && assessment.IncomingCurrent == 0 && f.Actor.HP == 35);
        var unsafeReturn = new AIAction { ActionType = AIActionType.Move, Unit = f.Actor, TargetPos = f.Origin,
            APCost = board.CalcMoveCost(f.Actor, f.Origin), Score = 10000 };
        Check(team + " immediate unsafe return is legal movement but prohibited by recovery policy", board.GetValidMoves(f.Actor).Contains(f.Origin)
            && governor.PreservationRejectReason(unsafeReturn, board) == "unit_recovery_unsafe_reentry");
        var snapshot = JsonUtility.FromJson<AIUnitPreservationState>(JsonUtility.ToJson(governor.CapturePreservationState(team)));
        governor.ReleasePreservationState(); governor.RestorePreservationState(snapshot, team); board = f.Board(62);
        Check(team + " stable-life-id JSON roundtrip retains the wounded recovery phase", governor.AssessPreservation(f.Actor, board).Phase == AIUnitRecoveryPhase.Recovering);
        // The actual rule includes its damage reset and five safe owner-turn delay.
        for (int round = 62; round <= 78; round++) NaturalRecoverySystem.ProcessActor(f.Actor, round);
        board = f.Board(79); assessment = governor.AssessPreservation(f.Actor, board);
        Check(team + " actual natural recovery releases the individual recovery record", f.Actor.HP == f.Actor.MaxHP
            && assessment.Phase == AIUnitRecoveryPhase.Ready && governor.TrackedRecoveryUnits == 0);
        f.Foe.transform.position = new Vector3(-35000, 0, -36000); board = f.Board(80);
        var rejoin = new AIAction { ActionType = AIActionType.Move, Unit = f.Actor, TargetPos = f.Safety + Vector3.right };
        rejoin.APCost = board.CalcMoveCost(f.Actor, rejoin.TargetPos);
        Check(team + " recovered unit safely rejoins using the real executor", board.GetValidMoves(f.Actor).Contains(rejoin.TargetPos)
            && governor.PreservationRejectReason(rejoin, board) == null && Execute(live, f, rejoin, board));
    }
    static void NoHealing(GameSystems live, Team team)
    {
        using var f = new Fixture(live, team, 1, 35, 1);
        f.Actor.MaxHP = 8; f.Actor.HP = 2;
        var board = f.Board(); var assessment = board.Governor.AssessPreservation(f.Actor, board);
        Check(team + " sub-ten maximum HP does not invent natural healing", NaturalRecoverySystem.HealAmount(f.Actor.MaxHP) == 0
            && !assessment.HealingPossible);
        var retreat = f.Retreat(board); Check(team + " low-HP escape still uses real legal destinations", retreat != null);
        Check(team + " low-HP escape executes without an invented recovery source", Execute(live, f, retreat, board));
        board = f.Board(61);
        Check(team + " unavailable healing never traps the unit in indefinite recovery", board.Governor.AssessPreservation(f.Actor, board).Phase == AIUnitRecoveryPhase.Ready);
    }
    static void HiddenActor(GameSystems live, Team team)
    {
        using var f = new Fixture(live, team, 20, 35, 10);
        var hidden = f.Inner.Economy.Opponent(new Vector3(-35000, 0, -36000), 5000);
        var board = f.Board(); var before = board.Governor.AssessPreservation(f.Actor, board);
        hidden.HP = hidden.MaxHP = 100000; hidden.ATK = 1; hidden.transform.position = new Vector3(-34000, 0, -35000);
        board = f.Board(61); var after = board.Governor.AssessPreservation(f.Actor, board);
        Check(team + " hidden enemy state changes cannot alter observed individual safety", !board.AlivePlayerUnits.Contains(hidden)
            && before.Safety == after.Safety && before.IncomingCurrent == after.IncomingCurrent
            && before.RepeatedObservedLoss == after.RepeatedObservedLoss && before.Refuge == after.Refuge);
    }
    static void Persistence(GameSystems live, Team team)
    {
        using var f = new Fixture(live, team, 20, 35, 10);
        var board = f.Board(); var governor = board.Governor; governor.AssessPreservation(f.Actor, board);
        var saved = governor.CapturePreservationState(team);
        Check(team + " recovery saves plain stable identity and bounded live record", saved.Records.Count == 1
            && saved.Records[0].ActorLifeId == f.Actor.ReflectionLifeId && saved.Records[0].LastTurn == 60);
        governor.RestorePreservationState(saved, team == Team.Player ? Team.Enemy : Team.Player);
        Check(team + " wrong-owner recovery state is rejected", governor.TrackedRecoveryUnits == 0);
        saved.Records[0].Refuge = new Vector3(float.NaN, 0, 1); governor.RestorePreservationState(saved, team);
        Check(team + " nonfinite recovery coordinate is rejected", governor.TrackedRecoveryUnits == 0);
        saved.Records.Clear();
        for (int i = 0; i < 128; i++) saved.Records.Add(new AIUnitPreservationRecord { ActorLifeId = "departed-" + i,
            Phase = AIUnitRecoveryPhase.Recovering, LastTurn = 60, Refuge = f.Safety });
        governor.RestorePreservationState(saved, team);
        Check(team + " restored state is bounded by 128 records", governor.TrackedRecoveryUnits == 128);
        board = f.Board(61); governor.AssessPreservation(f.Actor, board);
        Check(team + " departed stable identities are pruned before the next assessment", governor.TrackedRecoveryUnits == 1);
        f.Actor.gameObject.SetActive(false); board = f.Board(62);
        // Preparing the next board drops the dead actor even when no candidate can refer to it.
        governor.PrunePreservationState(board);
        Check(team + " destroyed or disabled actors leave no long-lived recovery record", governor.TrackedRecoveryUnits == 0);
        governor.ReleasePreservationState();
        Check(team + " explicit battle release clears recovery ownership", governor.TrackedRecoveryUnits == 0);
    }
    static bool Execute(GameSystems live, Fixture f, AIAction action, AIBoardState board)
        => action != null && (bool)typeof(AIR2CommandTests).GetMethod("ExecuteMovement", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { live, f.Inner, action, board });
    static AIAction Select(AICommander commander, AIBoardState board, List<AIAction> candidates)
    {
        var field = typeof(AICommander).GetField("_board", Hidden); var original = field.GetValue(commander);
        var difficulty = typeof(AICommander).GetField("_threatLevel", Hidden); var oldDifficulty = difficulty.GetValue(commander);
        try
        {
            field.SetValue(commander, board);
            // Deterministic selection contract: ordinary difficulty's deliberate mistakes remain
            // a gameplay feature and are not a test of whether support survives priority filtering.
            difficulty.SetValue(commander, new AIThreatLevel(21));
            return (AIAction)typeof(AICommander).GetMethod("SelectBestAction", Hidden).Invoke(commander,
                new object[] { candidates, new HashSet<string>(), new HashSet<string>() });
        }
        finally { field.SetValue(commander, original); difficulty.SetValue(commander, oldDifficulty); }
    }
}
#endif
