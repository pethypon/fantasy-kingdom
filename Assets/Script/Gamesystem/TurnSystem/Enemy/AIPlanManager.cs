using System.Collections.Generic;
using UnityEngine;

/// <summary>Observation-driven, multi-turn operations. Role assignment is local and never constrains legal actions.</summary>
public sealed class AIPlanManager
{
    public AIPlan Current { get; private set; }
    readonly Dictionary<int, Kind> assigned = new Dictionary<int, Kind>();
    readonly HashSet<int> respondents = new HashSet<int>();
    readonly List<int> staleAssignments=new List<int>();
    readonly Dictionary<Kind,int> filledRoles=new Dictionary<Kind,int>();
    readonly List<Status> army = new List<Status>();
    int generation = -1, turn = -1, cooldownUntil;

    public void Restore(AIPlan plan)
    {
        Current = plan != null && plan.Version == 1 && System.Enum.IsDefined(typeof(AIPlanGoal), plan.Goal)
            && System.Enum.IsDefined(typeof(AIPlanStep), plan.Step) && Finite(plan.Target) && Finite(plan.RallyPoint)
            && Finite(plan.FlankPoint) && Finite(plan.DemonstrationPoint) && plan.ExpectedTurns >= 1 && plan.ExpectedTurns <= 12
            && plan.RequiredUnits != null && plan.RequiredUnits.Length > 0 && plan.RequiredUnits.Length <= 6
            && plan.Steps != null && plan.Steps.Length <= 10
            ? JsonUtility.FromJson<AIPlan>(JsonUtility.ToJson(plan)) : null;
        if(Current!=null)
        {
            Current.TargetId=0;
            if(Current.Step==AIPlanStep.ObserveResponse)Current.Step=AIPlanStep.Demonstrate;
        }
        assigned.Clear(); respondents.Clear(); generation = -1; turn = -1;
    }
    static bool Finite(Vector3 value) => !float.IsNaN(value.sqrMagnitude) && !float.IsInfinity(value.sqrMagnitude);
    public AIPlan Snapshot()
    {
        if(Current==null)return null;
        var copy=JsonUtility.FromJson<AIPlan>(JsonUtility.ToJson(Current));
        copy.TargetId=0; // Unity instance IDs are not stable across a loaded match.
        return copy;
    }

    public void Update(AIBoardState board)
    {
        if (board.ReconThreatLevel < 10) { Current = null; assigned.Clear(); return; }
        if (generation == board.Generation && turn == board.TurnCount) return;
        generation = board.Generation; turn = board.TurnCount;
        army.Clear();
        foreach (var unit in board.AliveEnemyUnits)
            if (unit != null && unit.IsAlive && unit.type == Type.Unit && unit.kind != Kind.King) army.Add(unit);
        if (Current != null && Current.Active)
        {
            Assign();
            if (Emergency(board) || army.Count * 2 < Current.InitialArmy
                || board.TurnCount - Current.StartedTurn >= Current.ExpectedTurns)
            { Finish(board, AIPlanStep.Aborted); return; }
            bool targetObserved = false;
            foreach (var target in board.AlivePlayerUnits)
                if (target != null && target.IsAlive && board.IsVisibleToEnemy(target.transform.position)
                    && (target.GetInstanceID() == Current.TargetId || (Current.TargetId==0 && Current.Goal!=AIPlanGoal.Reconnaissance
                        && target.kind==Current.TargetKind && target.facilityKind==Current.TargetFacility
                        && GridHelper.ChebyshevDistance(target.transform.position,Current.Target)<=1)))
                {
                    Current.TargetId=target.GetInstanceID(); Current.Target = target.transform.position; Current.LastObservedTurn = board.TurnCount;
                    targetObserved = true; break;
                }
            if (Current.Goal != AIPlanGoal.Reconnaissance && Current.Goal != AIPlanGoal.Ambush && !Current.TargetDestroyedConfirmed && !targetObserved
                && board.IsVisibleToEnemy(Current.Target))
            {
                // An absent stationary facility was cleared, but an absent unit may simply have moved away.
                if (Current.TargetType != Type.Unit) Current.TargetDestroyedConfirmed = true;
                else { Finish(board, AIPlanStep.Aborted); return; }
            }
            if (Current.TargetId != 0 && !Current.TargetDestroyedConfirmed && board.TurnCount - Current.LastObservedTurn > 3)
            { Finish(board, AIPlanStep.Aborted); return; }
            Advance(board);
            return;
        }
        if (board.TurnCount < cooldownUntil || army.Count == 0 || Emergency(board)) return;
        Choose(board);
        Assign();
    }

    public static bool Emergency(AIBoardState board)
    {
        if (board.EnemyCrystalMaxHP > 0 && board.EnemyCrystalHP < board.EnemyCrystalMaxHP * .4f) return true;
        foreach (var enemy in board.AlivePlayerUnits)
            if (enemy != null && board.IsVisibleToEnemy(enemy.transform.position)
                && GridHelper.ChebyshevDistance(enemy.transform.position, board.EnemyCrystalPos) <= 3) return true;
        return false;
    }

    void Choose(AIBoardState board)
    {
        int visibleTroops = 0;
        foreach(var observed in board.AlivePlayerUnits)
            if(observed!=null && observed.IsAlive && observed.type==Type.Unit && board.IsVisibleToEnemy(observed.transform.position))visibleTroops++;
        Status target = null; float best = float.NegativeInfinity;
        foreach (var observed in board.AlivePlayerUnits)
        {
            if (observed == null || !observed.IsAlive || !board.IsVisibleToEnemy(observed.transform.position)) continue;
            float score = observed.kind == Kind.King && observed.HP < observed.MaxHP * .4f ? 100
                : observed.facilityKind == FacilityKind.Bakery && observed.type != Type.Unit ? 80
                : observed.kind == Kind.SubCrystal ? 70 : observed.kind == Kind.Crystal ? 60 : -100;
            score -= GridHelper.ChebyshevDistance(observed.transform.position, board.EnemyCrystalPos);
            if (score > best) { best = score; target = observed; }
        }
        var goal = AIPlanGoal.Reconnaissance;
        if (target != null && best > -50)
        {
            goal = target.kind == Kind.King ? AIPlanGoal.KingStrike
                : target.facilityKind == FacilityKind.Bakery ? AIPlanGoal.EconomicRaid : AIPlanGoal.CrystalStrike;
            if (goal == AIPlanGoal.CrystalStrike && Has(Kind.Scout) && Has(Kind.Assassin) && Has(Kind.Knight)
                && visibleTroops >= 2) goal = AIPlanGoal.EasternFeint;
        }
        else if (Has(Kind.Scout) && RangedCount() >= 2 && visibleTroops > 0) goal = AIPlanGoal.Ambush;
        Vector3 home = board.EnemyCrystalPos;
        Vector3 aim = target != null && best > -50 ? target.transform.position
            : board.Belief.TryGetPriority(out var probable) ? probable : home + board.GetUnexploredDirection().normalized * 7;
        Vector3 forward = (aim - home).normalized;
        if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
        Vector3 side = new Vector3(forward.z, 0, -forward.x);
        // The visible demonstration uses one flank; the real wing uses the opposite flank.
        var demonstration = Clamp(board, home + forward * 4 + side * 4);
        var flank = Clamp(board, aim - side * 4 - forward * 2);
        Current = new AIPlan {
            Goal = goal, Target = Clamp(board, aim), TargetId = target != null && best > -50 ? target.GetInstanceID() : 0,
            TargetType = target != null ? target.type : Type.Unit, TargetKind = target != null ? target.kind : Kind.None,
            TargetFacility = target != null ? target.facilityKind : default,
            DemonstrationPoint = demonstration, FlankPoint = flank, RallyPoint = goal == AIPlanGoal.Ambush ? Clamp(board, demonstration-forward) : Clamp(board, home + forward * 2),
            StartedTurn = board.TurnCount, StepStartedTurn = board.TurnCount, LastObservedTurn = board.TurnCount,
            InitialArmy = army.Count, ExpectedTurns = goal == AIPlanGoal.EconomicRaid ? 9 : 8,
            RequiredUnits = goal == AIPlanGoal.EasternFeint ? new[] { Kind.Scout, Kind.Assassin, Kind.Knight }
                : goal == AIPlanGoal.Ambush ? new[] { Kind.Scout, Kind.Crossbow, Kind.Bomber }
                : goal == AIPlanGoal.EconomicRaid ? new[] { Kind.Assassin, Kind.Knight }
                : goal == AIPlanGoal.Reconnaissance ? new[] { Kind.Scout } : new[] { Kind.Knight, Kind.Knight },
            Steps = goal == AIPlanGoal.EasternFeint
                ? new[] { AIPlanStep.Assemble, AIPlanStep.Demonstrate, AIPlanStep.ObserveResponse, AIPlanStep.Flank, AIPlanStep.Strike, AIPlanStep.Withdraw }
                : goal == AIPlanGoal.Ambush
                    ? new[] { AIPlanStep.Assemble, AIPlanStep.Demonstrate, AIPlanStep.ObserveResponse, AIPlanStep.Strike, AIPlanStep.Withdraw }
                : goal == AIPlanGoal.EconomicRaid
                    ? new[] { AIPlanStep.Assemble, AIPlanStep.Flank, AIPlanStep.Strike, AIPlanStep.SupplyDelay, AIPlanStep.MainAssault, AIPlanStep.Withdraw }
                    : new[] { AIPlanStep.Assemble, AIPlanStep.Strike, AIPlanStep.Withdraw },
            SuccessCondition = "視認済み目標の排除、または偵察先の確認後に帰還",
            AbortCondition = "自陣危機、戦力半減、目標情報の失効、反応なし、期限切れ"
        };
        respondents.Clear(); assigned.Clear();
        DevelopmentLog.Log($"[AIPlan] 開始 {Current.Goal} 目標={Current.Target} 期限={Current.ExpectedTurns}ターン");
    }

    static Vector3 Clamp(AIBoardState board, Vector3 p)
    {
        var map = board.ReconMap;
        if (map == null) return p;
        p.x = Mathf.Clamp(Mathf.Round(p.x), 0, map.maxX - 1); p.z = Mathf.Clamp(Mathf.Round(p.z), 0, map.maxZ - 1); p.y = 0;
        return p;
    }
    bool Has(Kind kind) { foreach (var unit in army) if (unit.kind == kind) return true; return false; }
    int RangedCount() { int n = 0; foreach (var unit in army) if (AIPlayerModel.IsRanged(unit.kind) || unit.kind == Kind.Bomber) n++; return n; }

    void Assign()
    {
        if (Current == null || Current.RequiredUnits == null) return;
        var stale = staleAssignments;stale.Clear();
        foreach(var pair in assigned)
        {
            bool alive=false;foreach(var unit in army)if(unit.GetInstanceID()==pair.Key && unit.HP>=unit.MaxHP*.35f){alive=true;break;}
            if(!alive)stale.Add(pair.Key);
        }
        foreach(int id in stale)assigned.Remove(id);
        var filled = filledRoles;filled.Clear();
        foreach(var role in assigned.Values){filled.TryGetValue(role,out var n);filled[role]=n+1;}
        foreach (var kind in Current.RequiredUnits)
        {
            filled.TryGetValue(kind,out var existing);
            if(existing>0){filled[kind]=existing-1;continue;}
            Status best = null; float fitness = float.NegativeInfinity;
            foreach (var unit in army)
            {
                if (assigned.ContainsKey(unit.GetInstanceID()) || unit.HP < unit.MaxHP * .4f) continue;
                bool match = unit.kind == kind || ((kind == Kind.Crossbow || kind == Kind.Bomber) && AIPlayerModel.IsRanged(unit.kind));
                if (!match) continue;
                float value = (float)unit.HP / Mathf.Max(1, unit.MaxHP) - GridHelper.ChebyshevDistance(unit.transform.position, Current.RallyPoint) * .01f;
                if(Current.Goal==AIPlanGoal.Ambush && kind==Kind.Scout)
                    value=1-Mathf.Abs((float)unit.HP/Mathf.Max(1,unit.MaxHP)-.55f);
                if (value > fitness) { best = unit; fitness = value; }
            }
            if (best != null) assigned[best.GetInstanceID()] = kind;
        }
    }

    void Advance(AIBoardState board)
    {
        int age = board.TurnCount - Current.StepStartedTurn;
        switch (Current.Step)
        {
            case AIPlanStep.Assemble:
                if (assigned.Count >= Current.RequiredUnits.Length)
                    Step(board, Current.Goal == AIPlanGoal.EasternFeint || Current.Goal == AIPlanGoal.Ambush ? AIPlanStep.Demonstrate
                        : Current.Goal == AIPlanGoal.EconomicRaid ? AIPlanStep.Flank : AIPlanStep.Strike);
                else if (age >= 2) Finish(board, AIPlanStep.Aborted);
                break;
            case AIPlanStep.Demonstrate:
                if (Near(Kind.Scout, Current.DemonstrationPoint, 2) && (Current.Goal != AIPlanGoal.Ambush || SupportsReady()))
                {
                    respondents.Clear();
                    foreach (var enemy in board.AlivePlayerUnits)
                        if (enemy != null && enemy.IsAlive && enemy.type==Type.Unit && board.IsVisibleToEnemy(enemy.transform.position)
                            && GridHelper.ChebyshevDistance(enemy.transform.position, Current.DemonstrationPoint) <= 3) respondents.Add(enemy.GetInstanceID());
                    Step(board, AIPlanStep.ObserveResponse);
                }
                else if (age >= 2) Finish(board, AIPlanStep.Aborted);
                break;
            case AIPlanStep.ObserveResponse:
                int arrivals = 0;
                Status pursuer = null;
                foreach (var enemy in board.AlivePlayerUnits)
                    if (enemy != null && enemy.IsAlive && enemy.type==Type.Unit && board.IsVisibleToEnemy(enemy.transform.position) && !respondents.Contains(enemy.GetInstanceID())
                        && GridHelper.ChebyshevDistance(enemy.transform.position, Current.DemonstrationPoint) <= 3)
                    { arrivals++; if(pursuer==null)pursuer=enemy; }
                Current.VisibleResponders = arrivals;
                if (arrivals >= (Current.Goal == AIPlanGoal.Ambush ? 1 : 2))
                {
                    if(Current.Goal==AIPlanGoal.Ambush && pursuer!=null)
                    {
                        Current.Target=pursuer.transform.position; Current.TargetId=pursuer.GetInstanceID();
                        Current.TargetType=pursuer.type; Current.TargetKind=pursuer.kind; Current.TargetFacility=pursuer.facilityKind;
                        Current.LastObservedTurn=board.TurnCount;
                    }
                    Step(board, Current.Goal == AIPlanGoal.Ambush ? AIPlanStep.Strike : AIPlanStep.Flank);
                }
                else if (age >= 2) Finish(board, AIPlanStep.Aborted);
                break;
            case AIPlanStep.Flank:
                if (Near(Kind.Assassin, Current.FlankPoint, 2) || age >= 2) Step(board, AIPlanStep.Strike);
                break;
            case AIPlanStep.Strike:
                if (Current.Goal == AIPlanGoal.Reconnaissance && board.IsVisibleToEnemy(Current.Target)) Step(board, AIPlanStep.Withdraw);
                else if (Current.TargetDestroyedConfirmed) Step(board, Current.Goal == AIPlanGoal.EconomicRaid ? AIPlanStep.SupplyDelay : AIPlanStep.Withdraw);
                else if (Current.Goal == AIPlanGoal.Ambush && Current.SuccessfulStrikes > 0) Step(board, AIPlanStep.Withdraw);
                else if (Current.Goal == AIPlanGoal.Ambush && age >= 2) Finish(board, AIPlanStep.Aborted);
                break;
            case AIPlanStep.SupplyDelay:
                if (age >= 2) Step(board, AIPlanStep.MainAssault); // Estimated supply delay; never reads hidden Bread.
                break;
            case AIPlanStep.MainAssault:
                if (age >= 2) Step(board, AIPlanStep.Withdraw);
                break;
            case AIPlanStep.Withdraw:
                bool arrived = true;
                foreach (var unit in army) if (assigned.ContainsKey(unit.GetInstanceID()) && GridHelper.ChebyshevDistance(unit.transform.position, Current.RallyPoint) > 3) arrived = false;
                if (arrived || age >= 2) Finish(board, AIPlanStep.Complete);
                break;
        }
    }

    bool Near(Kind role, Vector3 goal, int radius)
    { foreach (var unit in army) if (assigned.TryGetValue(unit.GetInstanceID(), out var k) && k == role && GridHelper.ChebyshevDistance(unit.transform.position, goal) <= radius) return true; return false; }
    bool SupportsReady()
    {
        int ready=0;
        foreach(var unit in army)
            if(assigned.TryGetValue(unit.GetInstanceID(),out var role) && role!=Kind.Scout
                && (AttackPatterns.CanAttack(unit.kind,unit.direction,Current.DemonstrationPoint.x-unit.transform.position.x,Current.DemonstrationPoint.z-unit.transform.position.z)
                    || GridHelper.ChebyshevDistance(unit.transform.position,Current.DemonstrationPoint)<=2))ready++;
        return ready>=2;
    }
    void Step(AIBoardState board, AIPlanStep step)
    { Current.Step = step; Current.StepStartedTurn = board.TurnCount; DevelopmentLog.Log($"[AIPlan] {Current.Goal} → {step}"); }
    void Finish(AIBoardState board, AIPlanStep step) { Step(board, step); cooldownUntil = board.TurnCount + 1; }

    public float Bonus(AIAction action, AIBoardState board)
    {
        if (Current == null || !Current.Active || board.ReconThreatLevel < 10) return 0;
        if (action.ActionType == AIActionType.Summon && Current.Step == AIPlanStep.Assemble)
        {
            int needed=0, present=0;
            foreach(var required in Current.RequiredUnits)if(required==action.SummonKind)needed++;
            foreach(var role in assigned.Values)if(role==action.SummonKind)present++;
            if(needed>present)return 35;
        }
        if (action.Unit == null || !assigned.TryGetValue(action.Unit.GetInstanceID(), out var roleKind)) return 0;
        bool moving = action.ActionType == AIActionType.Move || action.ActionType == AIActionType.Retreat || action.ActionType == AIActionType.Support;
        Vector3 goal = Current.Target;
        if (Current.Step == AIPlanStep.Assemble || Current.Step == AIPlanStep.Withdraw || Current.Step == AIPlanStep.SupplyDelay) goal = Current.RallyPoint;
        if (Current.Step == AIPlanStep.Demonstrate || Current.Step == AIPlanStep.ObserveResponse)
            goal = roleKind == Kind.Scout ? Current.DemonstrationPoint : Current.RallyPoint;
        if (Current.Step == AIPlanStep.Flank) goal = Current.FlankPoint;
        if (Current.Step == AIPlanStep.MainAssault && board.Recon.TryGetContactTarget(out var contact)) goal = contact;
        if (moving)
        {
            if (action.Unit.HP < action.Unit.MaxHP * .35f || board.EstimateCounterDamageAt(action.TargetPos, action.Unit) + board.Belief.RiskAt(action.TargetPos, board.ReconThreatLevel) >= action.Unit.HP * .7f)
                return -35; // A demonstration must never demand a sacrificial scout.
            float progress = GridHelper.ChebyshevDistance(action.Unit.transform.position, goal) - GridHelper.ChebyshevDistance(action.TargetPos, goal);
            return Mathf.Clamp(progress, -3, 3) * 14;
        }
        if (action.ActionType == AIActionType.Attack || action.ActionType == AIActionType.SkillUse)
        {
            if (action.TargetUnit != null && action.TargetUnit.GetInstanceID() == Current.TargetId && (Current.Step == AIPlanStep.Strike || Current.Step == AIPlanStep.MainAssault)) return 35;
            if (Current.Goal == AIPlanGoal.Ambush && Current.Step == AIPlanStep.Strike) return 30;
            if(Current.Step==AIPlanStep.Flank || Current.Step==AIPlanStep.Demonstrate || Current.Step==AIPlanStep.ObserveResponse)
            {
                bool finishing=action.TargetUnit!=null && action.TargetUnit.ShieldTurns<=0 && AIEvalHelpers.EstimateDamage(action.Unit,action.TargetUnit)>=action.TargetUnit.HP;
                if(!finishing)return -20;
            }
        }
        return 0;
    }

    public void RecordSuccess(AIAction action)
    {
        if (Current == null || !Current.Active || action.TargetUnit == null) return;
        if(Current.Goal==AIPlanGoal.Ambush && Current.Step==AIPlanStep.Strike
            && (action.ActionType==AIActionType.Attack || action.ActionType==AIActionType.SkillUse && action.Skill!=null && action.Skill.Multiplier>0))Current.SuccessfulStrikes++;
        if ((action.ActionType == AIActionType.Attack || action.ActionType == AIActionType.SkillUse)
            && action.TargetUnit.GetInstanceID() == Current.TargetId && !action.TargetUnit.IsAlive) Current.TargetDestroyedConfirmed = true;
    }
}
