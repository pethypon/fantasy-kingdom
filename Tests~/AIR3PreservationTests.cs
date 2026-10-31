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
                Case(team + " production save and restore routes", () => SaveRoutes(live, team));
                Case(team + " emergency defender's unique safe lateral exit", () => EmergencyDefender(live, team));
                Case(team + " healthy defender pursuit remains constrained", () => HealthyDefender(live, team));
                Case(team + " legal allied kill saves a lethal base threat", () => BaseSavingKill(live, team));
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
    static void EmergencyDefender(GameSystems live, Team team)
    {
        using var f = new Fixture(live, team, 20, 35, 100);
        var home = f.Origin - Vector3.right;
        var exit = f.Origin + Vector3.forward * 2;
        f.Inner.Economy.Crystal.transform.position = home; f.Inner.Economy.SetOwnCrystalPosition(home);
        f.Actor.GrowthData.actionProfile.movement.cells = 0;
        f.Actor.GrowthData.actionProfile.movement.SetCell(3, 5, true);
        foreach (var cell in new[] { home, exit }) { f.Inner.Explored.Add(GridHelper.ToGridXZ(cell)); f.Inner.See(cell); }
        var board = f.Board(); var governor = board.Governor;
        float baseIncoming = governor.IncomingDamage(board, f.Inner.Economy.Crystal, home, null);
        // The observed base threat is severe but NONLETHAL in one exchange. There is
        // no immediate loss-condition rescue requiring this wounded soldier's sacrifice.
        f.Inner.Economy.Crystal.HP = Mathf.CeilToInt(baseIncoming / .9f);
        int moveCost = board.CalcMoveCost(f.Actor, exit);
        f.Inner.Economy.State.SetAP(team, moveCost); board = f.Board(61);
        governor = board.Governor; baseIncoming = governor.IncomingDamage(board, f.Inner.Economy.Crystal, home, null);
        float current = governor.IncomingDamage(board, f.Actor, f.Origin, null);
        float safe = governor.IncomingDamage(board, f.Actor, exit, null);
        var legal = board.GetValidMoves(f.Actor);
        var skills = new List<AIAction>(); AIActionGenerator.GenerateSkillCandidates(f.Actor, board, skills);
        Debug.Log("[AIR3PreservationDefensePrecondition] side=" + team + " mode=" + governor.Mode
            + " actorHP=" + f.Actor.HP + " incoming=" + current + " baseHP=" + f.Inner.Economy.Crystal.HP
            + " baseIncoming=" + baseIncoming + " legalMoveCount=" + legal.Count + " escapeRisk=" + safe
            + " moveAP=" + moveCost + " availableAP=" + board.EnemyAP);
        Check(team + " defense fixture has a real severe but nonlethal base threat", board.AlivePlayerUnits.Contains(f.Foe)
            && governor.Mode == StrategicMode.EmergencyDefense && baseIncoming > 0 && baseIncoming < f.Inner.Economy.Crystal.HP);
        Check(team + " the wounded actor is genuinely assigned as an emergency defender",
            ((ICollection<Status>)typeof(AIStrategicGovernor).GetField("defenders", Hidden).GetValue(governor)).Contains(f.Actor));
        Check(team + " defender fixture offers exactly one legal affordable safe exit from lethal exposure", current >= f.Actor.HP
            && safe == 0 && legal.Count == 1 && legal[0] == exit && moveCost >= 0 && board.EnemyAP == moveCost);
        Check(team + " the only escape increases the actual Chebyshev distance from the defended base",
            GridHelper.ChebyshevDistance(exit, home) > GridHelper.ChebyshevDistance(f.Origin, home));
        Check(team + " no owned killing or healing action requires retaining the lethal position", board.AliveEnemyUnits.Count == 1
            && board.GetAttackTargets(f.Actor).Contains(f.Foe) && !AIActionEvaluator.CanFinishSpecialTarget(f.Actor, f.Foe, board)
            && skills.Count == 0);
        var options = new List<AIAction>(); AIActionGenerator.GenerateRetreatCandidates(f.Actor, board, options);
        var retreat = options.Find(action => action.TargetPos == exit); var attack = f.Attack(board);
        Check(team + " common retreat generation retains the unique emergency exit", retreat != null);
        retreat.Score = 1; var actions = new List<AIAction> { retreat, attack }; governor.Filter(actions, board);
        Debug.Log("[AIR3PreservationDefenseEvidence] side=" + team + " retreatPriority=" + retreat.StrategicPriority
            + " retreatRejected=" + retreat.StrategicRejectReason + " retained=" + actions.Contains(retreat)
            + " attackPriority=" + attack.StrategicPriority + " targetHP=" + f.Foe.HP);
        Check(team + " a P0 individual escape survives the emergency defender pursuit gate", retreat.StrategicPriority == 0
            && actions.Contains(retreat) && retreat.StrategicRejectReason == null);
        Check(team + " actual selector saves the defender instead of an ineffective lethal hold",
            ReferenceEquals(Select(live.AICommander, board, actions), retreat));
    }
    static void HealthyDefender(GameSystems live, Team team)
    {
        using var f = new Fixture(live, team, 20, 35, 100);
        f.Actor.HP = f.Actor.MaxHP = 1000;
        var home = f.Origin - Vector3.right; var exit = f.Origin + Vector3.forward * 2;
        f.Inner.Economy.Crystal.transform.position = home; f.Inner.Economy.SetOwnCrystalPosition(home);
        f.Actor.GrowthData.actionProfile.movement.cells = 0; f.Actor.GrowthData.actionProfile.movement.SetCell(3, 5, true);
        f.Inner.Explored.Add(GridHelper.ToGridXZ(exit)); f.Inner.See(exit);
        var board = f.Board();
        float threat = board.Governor.IncomingDamage(board, f.Inner.Economy.Crystal, home, null);
        f.Inner.Economy.Crystal.HP = Mathf.CeilToInt(threat / .9f); board = f.Board(61);
        var movement = new AIAction { ActionType = AIActionType.Move, Unit = f.Actor, TargetPos = exit,
            APCost = board.CalcMoveCost(f.Actor, exit) };
        Check(team + " healthy pursuit guard has no individual emergency", board.Governor.Mode == StrategicMode.EmergencyDefense
            && board.GetValidMoves(f.Actor).Contains(exit)
            && board.Governor.AssessPreservation(f.Actor, board).Safety == AIUnitSafetyLevel.HoldPositionOrFight);
        var actions = new List<AIAction> { movement }; board.Governor.Filter(actions, board);
        Check(team + " a healthy defender still obeys the emergency pursuit boundary", actions.Count == 0
            && movement.StrategicRejectReason == "defender_pursuit_limit");
    }
    static void BaseSavingKill(GameSystems live, Team team)
    {
        using var f = new Fixture(live, team, 20, 35, 100);
        var allyProfile = ScriptableObject.CreateInstance<BoardActionProfile>();
        try
        {
            var home = f.Origin - Vector3.right; var exit = f.Origin + Vector3.forward * 2;
            f.Inner.Economy.Crystal.transform.position = home; f.Inner.Economy.SetOwnCrystalPosition(home);
            f.Inner.Economy.Crystal.HP = 1;
            f.Actor.GrowthData.actionProfile.movement.cells = 0; f.Actor.GrowthData.actionProfile.movement.SetCell(3, 5, true);
            allyProfile.actionType = ActorActionType.Stationary; allyProfile.attack.useCustom = true;
            allyProfile.attack.directionIndependent = true; allyProfile.attack.SetCell(3, 2, true);
            var ally = f.Inner.Economy.Unit(); ally.GrowthData.actionProfile = allyProfile;
            ally.transform.position = f.Foe.transform.position + Vector3.forward; ally.ATK = 5000; ally.HP = ally.MaxHP = 1000;
            ally.VisionCell.Clear(); ally.VisionCell.Add(GridHelper.ToGrid(f.Foe.transform.position));
            foreach (var cell in new[] { home, exit, ally.transform.position })
            { f.Inner.Explored.Add(GridHelper.ToGridXZ(cell)); f.Inner.See(cell); }
            var board = f.Board(); int moveAP = board.CalcMoveCost(f.Actor, exit), attackAP = board.CalcAttackCost(ally);
            f.Inner.Economy.State.SetAP(team, Mathf.Max(moveAP, attackAP)); board = f.Board(61);
            var governor = board.Governor;
            float baseBefore = governor.IncomingDamage(board, f.Inner.Economy.Crystal, home, null);
            float baseAfterKill = governor.IncomingDamage(board, f.Inner.Economy.Crystal, home, f.Foe);
            Check(team + " lethal-base fixture has both a real dying defender and a real dying loss condition",
                governor.Mode == StrategicMode.EmergencyDefense && baseBefore >= f.Inner.Economy.Crystal.HP
                && governor.IncomingDamage(board, f.Actor, f.Origin, null) >= f.Actor.HP
                && governor.IncomingDamage(board, f.Actor, exit, null) == 0);
            Check(team + " a different safe ally has a legal observed lethal attack against the base's attacker",
                ally != f.Actor && board.IsSpecialActorObserved(f.Foe) && !MatchObjectiveRules.IsDecisive(f.Foe)
                && board.GetAttackTargets(ally).Contains(f.Foe) && AIActionEvaluator.CanFinishSpecialTarget(ally, f.Foe, board)
                && governor.IncomingDamage(board, ally, ally.transform.position, null) < ally.HP);
            Check(team + " eliminating the observed attacker actually removes the base's lethal forecast",
                baseBefore > 0 && baseAfterKill == 0 && board.AlivePlayerUnits.Count == 1);
            Check(team + " current AP funds either rescue but cannot fund escape followed by the saving kill",
                moveAP > 0 && attackAP > 0 && moveAP <= board.EnemyAP && attackAP <= board.EnemyAP
                && moveAP + attackAP > board.EnemyAP);
            // Use the complete production score pipeline. No giant artificial float score can
            // conceal a lower priority, and no new game balance values are adopted by this fixture.
            var generated = AIActionEvaluator.EvaluateAll(f.Inner.Economy.Personality, board, new AILearning(false), TurnStrategy.CrystalDefense);
            var kill = generated.Find(a => a.ActionType == AIActionType.Attack && a.Unit == ally && a.TargetUnit == f.Foe);
            var retreat = generated.Find(a => a.ActionType == AIActionType.Retreat && a.Unit == f.Actor && a.TargetPos == exit);
            Check(team + " complete production generation retains both base-saving kill and personal escape", kill != null && retreat != null);
            Debug.Log("[AIR3PreservationBaseRescue] side=" + team + " baseHP=" + f.Inner.Economy.Crystal.HP
                + " baseBefore=" + baseBefore + " baseAfterKill=" + baseAfterKill + " escapeP=" + retreat.StrategicPriority
                + " escapeScore=" + retreat.Score + " savingKillP=" + kill.StrategicPriority + " savingKillScore=" + kill.Score
                + " availableAP=" + board.EnemyAP);
            Check(team + " a legal base-saving kill remains P0 beside individual escape", kill.StrategicPriority == 0 && retreat.StrategicPriority == 0);
            Check(team + " actual selector uses the real scored base-saving kill before spending its AP on escape",
                ReferenceEquals(Select(live.AICommander, board, new List<AIAction> { retreat, kill }), kill));
        }
        finally { UnityEngine.Object.DestroyImmediate(allyProfile); }
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
        for (int round = 62; round <= 78; round++)
        {
            NaturalRecoverySystem.ProcessActor(f.Actor, round);
            // A1 regroup policy now also waits for the formal R2 barrier process;
            // natural healing alone must not manufacture recovered protection.
            BarrierSystem.ProcessActorEnd(f.Actor, round);
        }
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
    static void SaveRoutes(GameSystems live, Team team)
    {
        using var f = new Fixture(live, team, 20, 35, 10);
        var board = f.Board(); var retreat = f.Retreat(board);
        Check(team + " save-route fixture genuinely retreats before collection", Execute(live, f, retreat, board));
        board = f.Board(61); var governor = board.Governor;
        Check(team + " save-route fixture has real recovery state", governor.AssessPreservation(f.Actor, board).Phase == AIUnitRecoveryPhase.Recovering);
        var state = governor.CapturePreservationState(team); string identity = f.Actor.ReflectionLifeId;
        var host = new GameObject("R3 isolated production save routes"); host.SetActive(false);
        DeveloperPlayerAIController controller = null; AICommander enemy = null, restored = null;
        try
        {
            var hub = host.AddComponent<TurnGenerator>(); hub.MarkDeveloperMatch(); hub.Context.Turn = 61;
            hub.Systems.MapCreate = live.MapCreate; hub.Systems.CrystalSystem = f.Inner.Economy.Crystals;
            hub.Systems.UnitSetting = f.Inner.Economy.Units; hub.Systems.FactionState = f.Inner.Economy.State;
            hub.Systems.APSystem = f.Inner.Economy.AP; hub.Systems.BuildSystem = f.Inner.Economy.Builder;
            hub.Systems.MoveGenerator = live.MoveGenerator; hub.Systems.VisionGenerator = live.VisionGenerator;
            controller = new DeveloperPlayerAIController(hub);
            typeof(TurnGenerator).GetField("_playerAI", Hidden).SetValue(hub, controller);
            enemy = SaveCommander(hub, live, f, Team.Enemy);
            if (team == Team.Enemy) enemy.RestorePreservationState(state); else hub.RestorePlayerPreservation(state);
            var collected = SaveSystem.CollectGameState(hub, f.Inner.Economy.State, live.TimerSystem, null, enemy);
            var collectedState = team == Team.Enemy ? collected.AI.Preservation : collected.PlayerPreservation;
            Check(team + " production collector includes the correct own-side stable identity", collectedState?.Owner == team
                && collectedState.Records.Count == 1 && collectedState.Records[0].ActorLifeId == identity
                && (team == Team.Enemy ? collected.AI.HasPreservationSnapshot : collected.HasPlayerPreservationSnapshot));
            Check(team + " production unit snapshot and recovery snapshot share the same stable identity",
                collected.Units.Exists(unit => unit.ReflectionLifeId == identity));
            Check(team + " player load and collection do not activate developer automation", !hub.DeveloperPlayerAIEnabled);
            var parse = typeof(SaveSystem).GetMethod("ParseGame", BindingFlags.Static | BindingFlags.NonPublic);
            var loaded = (SaveSystem.GameSaveData)parse.Invoke(null, new object[] { JsonUtility.ToJson(collected) });
            var loadedState = team == Team.Enemy ? loaded.AI.Preservation : loaded.PlayerPreservation;
            Check(team + " real game JSON parser retains optional recovery presence and ownership", loadedState?.Owner == team
                && loadedState.Records.Count == 1 && loadedState.Records[0].Phase == AIUnitRecoveryPhase.Recovering);
            restored = SaveCommander(hub, live, f, team);
            if (team == Team.Enemy) SaveSystem.RestoreAICommander(loaded.AI, restored);
            else
            {
                hub.RestorePlayerPreservation(loaded.HasPlayerPreservationSnapshot ? loaded.PlayerPreservation : null);
                Check(team + " player recovery survives pending load with automation disabled", !hub.DeveloperPlayerAIEnabled
                    && hub.CapturePlayerPreservation()?.Records.Count == 1);
                var pending = hub.CapturePlayerPreservation();
                typeof(DeveloperPlayerAIController).GetField("commander", Hidden).SetValue(controller, restored);
                controller.RestorePreservationState(pending);
            }
            Check(team + " production commander restore receives its own recovery record", restored.CapturePreservationState().Records.Count == 1
                && restored.CapturePreservationState().Records[0].ActorLifeId == identity);
            f.Actor.gameObject.SetActive(false);
            var replacement = f.Inner.Economy.Unit(); replacement.GrowthData = f.Actor.GrowthData;
            SaveGameApplier.ApplyStatusFields(replacement, loaded.Units.Find(unit => unit.ReflectionLifeId == identity));
            board = f.Board(62); board.Governor = restored.StrategicGovernor; board.Governor.Evaluate(board);
            Check(team + " production status applier reconnects recovery to a different Unity instance", replacement.GetInstanceID() != f.Actor.GetInstanceID()
                && replacement.ReflectionLifeId == identity
                && board.Governor.AssessPreservation(replacement, board).Phase == AIUnitRecoveryPhase.Recovering);
            var legacy = (SaveSystem.GameSaveData)parse.Invoke(null,
                new object[] { "{\"AI\":{\"Preservation\":{}},\"PlayerPreservation\":{}}" });
            Check(team + " old absent snapshot flags cannot fabricate either side's recovery", legacy.AI.Preservation == null
                && legacy.PlayerPreservation == null);
            if (team == Team.Enemy) SaveSystem.RestoreAICommander(legacy.AI, restored);
            else hub.RestorePlayerPreservation(legacy.HasPlayerPreservationSnapshot ? legacy.PlayerPreservation : null);
            Check(team + " production legacy restore clears previous recovery rather than reviving stale state", restored.CapturePreservationState().Records.Count == 0);
            restored.ReleaseRuntimeReferences();
            Check(team + " production runtime release removes every recovery reference", restored.StrategicGovernor.TrackedRecoveryUnits == 0);
            if (team == Team.Player) PendingCopies(hub, controller, state);
        }
        finally
        {
            controller?.Dispose(); enemy?.ReleaseRuntimeReferences(); restored?.ReleaseRuntimeReferences();
            UnityEngine.Object.DestroyImmediate(host);
        }
    }
    static AICommander SaveCommander(TurnGenerator hub, GameSystems live, Fixture f, Team owner)
    {
        var result = new AICommander(hub, live.MoveGenerator, live.AttackGenerator, live.BattleSystem, live.VisionGenerator,
            f.Inner.Economy.AP, f.Inner.Economy.Units, f.Inner.Economy.Crystals, live.MapCreate, MajorPersonality.Intellect,
            f.Inner.Economy.Builder, null, f.Inner.Economy.State, null, null, 1, 251025, owner);
        result.SaveTurnCount = 61; return result;
    }
    static void PendingCopies(TurnGenerator hub, DeveloperPlayerAIController controller, AIUnitPreservationState original)
    {
        // Detach the initialized commander to exercise the actual lazy-player storage boundary.
        typeof(DeveloperPlayerAIController).GetField("commander", Hidden).SetValue(controller, null);
        var source = AIUnitPreservationState.CopyForOwner(original, Team.Player);
        hub.RestorePlayerPreservation(source); source.Records.Clear();
        Check("player pending restore owns a copy instead of the caller's mutable DTO", hub.CapturePlayerPreservation().Records.Count == 1);
        var captured = hub.CapturePlayerPreservation(); captured.Records.Clear();
        Check("player pending capture cannot mutate the future resume state", hub.CapturePlayerPreservation().Records.Count == 1);
        var wrongOwner = AIUnitPreservationState.CopyForOwner(original, Team.Player); wrongOwner.Owner = Team.Enemy;
        hub.RestorePlayerPreservation(wrongOwner);
        Check("player pending route rejects a hostile side's recovery snapshot", hub.CapturePlayerPreservation() == null);
        var oversized = new AIUnitPreservationState { Owner = Team.Player };
        for (int i = 0; i <= AIUnitPreservationState.MaximumRecords; i++) oversized.Records.Add(original.Records[0].Copy());
        controller.RestorePreservationState(oversized);
        Check("player pending storage enforces the same bounded validator as active AI", controller.CapturePreservationState() == null);
        var detached = AIUnitPreservationState.CopyForOwner(original, Team.Player);
        controller.RestorePreservationState(detached); detached.Records[0].Refuge = Vector3.zero;
        Check("controller pending records are deeply detached", controller.CapturePreservationState().Records[0].Refuge == original.Records[0].Refuge);
        controller.Dispose();
        Check("controller disposal releases pending recovery payload", controller.CapturePreservationState() == null);
    }
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
