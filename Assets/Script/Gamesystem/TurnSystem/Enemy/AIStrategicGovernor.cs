using System;
using System.Collections.Generic;
using UnityEngine;

public enum StrategicMode { Normal, EmergencyDefense, EconomicRecovery, Exploration, StrategicAttack, Hold }
public interface ILossConditionProvider { void Collect(AIBoardState board,List<Status> targets); }
public sealed class DefaultLossConditionProvider : ILossConditionProvider
{
    public void Collect(AIBoardState board,List<Status> targets)
    {
        targets.Clear();
        foreach(var actor in board.AliveEnemyUnits)if(actor!=null&&actor.IsAlive&&actor.kind==Kind.King)targets.Add(actor);
        var crystal=board.OwnCrystalParent?.GetComponentInChildren<Status>();if(crystal!=null&&crystal.IsAlive&&!targets.Contains(crystal))targets.Add(crystal);
    }
}
[Serializable]
public sealed class PersistentStrategicObjective
{
    public int Version=1,StartedTurn,PausedTurn,PausedTurns;
    public Vector3 Target;
    public bool Active,Suspended;
}

/// <summary>Priority gates above unchanged tactical/economic scoring. Facts come only from the observed board.</summary>
public sealed class AIStrategicGovernor
{
    readonly ILossConditionProvider losses;
    readonly StrategicEconomyForecast forecast=new StrategicEconomyForecast();
    readonly List<Status> critical=new List<Status>(4),defenders=new List<Status>(8),army=new List<Status>(32);
    readonly Dictionary<Status,List<Vector3>> reachable=new Dictionary<Status,List<Vector3>>();
    readonly Dictionary<Status,float> safest=new Dictionary<Status,float>();
    readonly Dictionary<(Status,Vector3Int,Status),float> damageCache=new Dictionary<(Status,Vector3Int,Status),float>();
    readonly Dictionary<AIAction,string> rejected=new Dictionary<AIAction,string>();
    readonly Dictionary<(AIActionType, FacilityKind, FacilityDefinitionData, Status, int), EconomyForecastResult> buildForecasts
        = new Dictionary<(AIActionType, FacilityKind, FacilityDefinitionData, Status, int), EconomyForecastResult>();
    readonly Dictionary<(Status,Vector3Int),float> threatCache=new Dictionary<(Status,Vector3Int),float>();
    readonly List<AIAction> reserveCandidates = new List<AIAction>(64);
    int recoveryReserveAP = -1;
    readonly Dictionary<(int,Kind,Direction),HashSet<Vector2Int>> skillReach=new Dictionary<(int,Kind,Direction),HashSet<Vector2Int>>();
    AIBoardState current;int generation=-1;
    Vector3 defensePoint;bool safeAlternativesPrepared;
    Status explorer;
    public StrategicMode Mode {get;private set;}
    public EconomyForecastResult Economy {get;private set;}
    public StrategicProductionDemand ProductionDemand { get; private set; }
    public AIBasicResourceEconomy BasicResources { get; } = new AIBasicResourceEconomy();
    public AIExplorationPlanner Exploration { get; } = new AIExplorationPlanner();
    public PersistentStrategicObjective Objective {get;private set;}=new PersistentStrategicObjective();
    int bridgedExploreObjective = -1;
    bool restoredLegacyObjective;
    public IReadOnlyDictionary<AIAction,string> Rejections=>rejected;
    public int BreadReserveTurns {get=>forecast.ReserveTurns;set=>forecast.ReserveTurns=Mathf.Clamp(value,1,8);}
    public AIStrategicGovernor(ILossConditionProvider provider=null){losses=provider??new DefaultLossConditionProvider();}
    public void Evaluate(AIBoardState board)
    {
        if(current==board&&generation==board.Generation)return;
        current=board;generation=board.Generation;reachable.Clear();damageCache.Clear();threatCache.Clear();safest.Clear();buildForecasts.Clear();safeAlternativesPrepared=false;
        recoveryReserveAP = -1;
        losses.Collect(board,critical);defenders.Clear();army.Clear();
        forecast.Prepare(board);Economy=forecast.Simulate(board);
        bool emergency=false;float severity=0;
        foreach(var target in critical)
        {
            float incoming=IncomingDamage(board,target,target.transform.position,null);
            float risk=incoming/Mathf.Max(1,target.HP);
            if (IsSevereThreat(target, incoming))
            {
                emergency = true;
                if (risk > severity) { severity = risk; defensePoint = target.transform.position; }
            }
        }
        // A remembered threat can guide scouting, but only observed legal attacks trigger emergency orders.
        ProductionDemand = forecast.Diagnose(board, !emergency && Economy.State == EconomicState.Healthy);
        Economy = ProductionDemand.Forecast;
        BasicResources.Evaluate(board, ProductionDemand, emergency);
        Mode = emergency ? StrategicMode.EmergencyDefense
            : ProductionDemand.HasCriticalDeficit || ProductionDemand.HasWarningDeficit
                || Economy.State >= EconomicState.Warning || AIBasicResourceSettings.Active.enabled
                    && BasicResources.FoundationState == EconomyFoundationState.ResourceRecovery
                ? StrategicMode.EconomicRecovery
            : board.AlivePlayerUnits.Count == 0 ? StrategicMode.Exploration : StrategicMode.StrategicAttack;
        LogProductionDemand(board);
        // Emergency orders suspend persistent exploration. Supply recovery retains targets but outranks movement.
        Exploration.Update(board, emergency);
        UpdateObjective(board,emergency||Mode==StrategicMode.EconomicRecovery);
        if(emergency)SelectDefenders(board);
    }
    void SelectDefenders(AIBoardState board)
    {
        foreach(var actor in board.AliveEnemyUnits)
            if(actor!=null&&actor.IsAlive&&actor.type==Type.Unit&&actor.kind!=Kind.King&&BoardActionProfile.For(actor)?.CanAttack!=false)army.Add(actor);
        army.Sort((a,b)=>DefenseFitness(b).CompareTo(DefenseFitness(a)));
        float need=0;
        foreach(var opponent in board.AlivePlayerUnits)if(opponent!=null&&opponent.IsAlive&&Distance(opponent.transform.position,defensePoint)<=8)need+=Mathf.Max(1,opponent.ATK);
        float available=0;
        foreach(var actor in army)
        {defenders.Add(actor);available+=Mathf.Max(1,actor.ATK)*(float)actor.HP/Mathf.Max(1,actor.MaxHP);if(available>=need*1.2f||defenders.Count>=4)break;}
    }
    float DefenseFitness(Status actor)=>Mathf.Max(1,actor.ATK)*(float)actor.HP/Mathf.Max(1,actor.MaxHP)/(1+Distance(actor.transform.position,defensePoint));
    void UpdateObjective(AIBoardState board,bool pause)
    {
        explorer = null;
        ExploreObjective assigned = null;
        foreach (var actor in board.AliveEnemyUnits)
        {
            if (actor == null || !actor.IsAlive || actor.kind == Kind.King) continue;
            var candidate = Exploration.GetObjective(actor.GetInstanceID());
            if (candidate == null || candidate.State == ObjectiveState.Completed || candidate.State == ObjectiveState.Failed) continue;
            if (explorer != null)
            {
                int rank = actor.kind == Kind.Scout ? 0 : 1;
                int chosenRank = explorer.kind == Kind.Scout ? 0 : 1;
                if (rank > chosenRank || rank == chosenRank && actor.GetInstanceID() >= explorer.GetInstanceID()) continue;
            }
            explorer = actor; assigned = candidate;
        }
        if (assigned == null)
        {
            // Older saves can contain an explicit strategic goal without an R2 scout assignment.
            // Preserve that restored goal through interruptions; never generate a new goal by scanning the map.
            if (restoredLegacyObjective && Objective.Active)
            {
                if (pause && !Objective.Suspended) { Objective.Suspended = true; Objective.PausedTurn = board.TurnCount; }
                else if (!pause && Objective.Suspended)
                { Objective.PausedTurns += Mathf.Max(0, board.TurnCount - Objective.PausedTurn); Objective.Suspended = false; }
                if (pause) return;
                if (!board.IsVisibleToEnemy(Objective.Target)
                    && board.TurnCount - Objective.StartedTurn - Objective.PausedTurns < 12) return;
                restoredLegacyObjective = false;
            }
            Objective.Active = false; Objective.Suspended = false; bridgedExploreObjective = -1;
            return;
        }
        restoredLegacyObjective = false;
        bool suspended = pause || assigned.State == ObjectiveState.Suspended;
        // Preserve the legacy save/inspection contract using a bridge to one genuine assigned frontier.
        // Multiple explorers each retain their separate R2 objective; this bridge never chooses another target.
        if (bridgedExploreObjective != assigned.ObjectiveId)
        {
            bridgedExploreObjective = assigned.ObjectiveId;
            Objective = new PersistentStrategicObjective { Active = true, Target = assigned.TargetCell,
                StartedTurn = assigned.CreatedTurn, Suspended = suspended, PausedTurn = suspended ? board.TurnCount : 0 };
            return;
        }
        Objective.Active = true; Objective.Target = assigned.TargetCell;
        if (suspended && !Objective.Suspended) { Objective.Suspended = true; Objective.PausedTurn = board.TurnCount; }
        else if (!suspended && Objective.Suspended)
        { Objective.PausedTurns += Mathf.Max(0, board.TurnCount - Objective.PausedTurn); Objective.Suspended = false; }
    }
    public void Filter(List<AIAction> actions,AIBoardState board)
    {
        Evaluate(board);rejected.Clear();safest.Clear();
        foreach(var target in critical)
        {
            if(target.kind!=Kind.King)continue;
            float minimum=float.PositiveInfinity;
            foreach(var action in actions)if(action.Unit==target&&action.ActionType!=AIActionType.Wait)
                minimum=Mathf.Min(minimum,Risk(action,board));
            // A safe hold is an alternative even when no move can escape the current attack geometry.
            if(IncomingDamage(board,target,target.transform.position,null)<target.HP)minimum=Mathf.Min(minimum,0);
            safest[target]=minimum;
        }
        safeAlternativesPrepared=true;
        int output=0;
        for(int i=0;i<actions.Count;i++)
        {
            var action=actions[i];action.StrategicPriority=Priority(action,board);
            string reason=RejectReason(action,board);
            action.StrategicRejectReason=reason;
            if(reason!=null)
            {
                rejected[action]=reason;
                if (action.ActionType == AIActionType.Build && AIEconomySettings.Active.enableDecisionLogs) DevelopmentLog.Log($"[AI経済] Rejected={action.Facility} reason={reason}");
                continue;
            }
            actions[output++]=action;
        }
        if(output<actions.Count)actions.RemoveRange(output,actions.Count-output);
    }
    public bool AllowExecution(AIAction action,AIBoardState board)
    {
        Evaluate(board);
        if(!string.IsNullOrEmpty(action.StrategicRejectReason))return false;
        string reason=RejectReason(action,board);
        if(reason==null)return true;
        action.StrategicRejectReason=reason;Telemetry(action,false);return false;
    }
    string RejectReason(AIAction action,AIBoardState board)
    {
        if(action.Unit!=null&&action.Unit.kind==Kind.King)
        {
            float risk=Risk(action,board);
            float minimum=safest.TryGetValue(action.Unit,out var safe)?safe:IncomingDamage(board,action.Unit,action.Unit.transform.position,null);
            if(risk>=action.Unit.HP&&(minimum<action.Unit.HP||safeAlternativesPrepared&&risk>minimum+.01f))return "king_known_lethal";
        }
        if(action.ActionType==AIActionType.Summon)
        {
            var projected=forecast.Simulate(board,action);
            if(projected.BreadPaymentFailed||projected.ImportantProductionStopped)return "bread_forecast_fail";
            if(projected.UpkeepPaymentFailed)return "upkeep_forecast_fail";
            if(projected.BreadCoverageTurns<BreadReserveTurns&&Mode!=StrategicMode.EmergencyDefense)return "bread_reserve_fail";
        }
        if (action.ActionType == AIActionType.Build || action.ActionType == AIActionType.Upgrade)
        {
            var assessment = ProductionDemand.EvaluateBuild(action);
            bool emergencyDefense = IsEmergencyDefenseRequired(action, board);
            if (IsMilitaryConstruction(action) && ProductionDemand.State >= EconomicState.Crisis && !emergencyDefense)
                return "economic_recovery_blocks_military_build";
            var key = (action.ActionType, action.Facility, action.FacilityDefinition,
                action.ActionType == AIActionType.Upgrade ? action.Unit : null,
                action.ActionType == AIActionType.Upgrade && action.Unit != null ? action.Unit.Level : 0);
            if (!buildForecasts.TryGetValue(key, out var projected))
                buildForecasts[key] = projected = forecast.Simulate(board, action);
            // A baseline crisis must not hide a new failure in a different resource.
            bool introducesFailure = (projected.MandatoryFailureMask & ~Economy.MandatoryFailureMask) != 0
                || (projected.FirstTurnMandatoryFailureMask & ~Economy.FirstTurnMandatoryFailureMask) != 0
                || (projected.FirstTurnInputFailureMask & ~Economy.FirstTurnInputFailureMask) != 0;
            if (!emergencyDefense && introducesFailure) return "build_causes_forecast_failure";
            if (action.ActionType == AIActionType.Build && assessment.IsProduction && !assessment.ImprovesDeficit
                && !BasicResources.IsFoundationAction(action) && !emergencyDefense)
                return "production_not_needed";
            if (action.ActionType == AIActionType.Upgrade && !EconomyHelper.ImprovesProductionDemand(action, board)
                && !BasicResources.IsFoundationAction(action) && !emergencyDefense) return "upgrade_not_needed";
        }
        if (!BasicResources.AllowSpend(action, IsEssentialDefenseSpend(action, board), out string spendReason))
            return spendReason;
        if(Mode==StrategicMode.EmergencyDefense)
        {
            if(action.ActionType==AIActionType.SubCrystal)return "emergency_expansion_suspended";
            if(IsMovement(action)&&defenders.Contains(action.Unit)&&Distance(action.TargetPos,defensePoint)>Distance(action.Unit.transform.position,defensePoint)+.01f)
                return "defender_pursuit_limit";
        }
        return null;
    }
    int Priority(AIAction a,AIBoardState board)
    {
        if(a.Unit!=null&&a.Unit.kind==Kind.King&&IsMovement(a)&&Risk(a,board)<IncomingDamage(board,a.Unit,a.Unit.transform.position,null))return 0;
        if(a.Unit!=null&&a.Unit.kind==Kind.King&&a.ActionType==AIActionType.Attack
            &&IncomingDamage(board,a.Unit,a.Unit.transform.position,null)>=a.Unit.HP&&Risk(a,board)<a.Unit.HP)return 0;
        if(Mode==StrategicMode.EmergencyDefense)
        {
            if(a.ActionType==AIActionType.Attack&&a.TargetUnit!=null&&ThreatensCritical(a.TargetUnit,board))return 1;
            if(a.ActionType==AIActionType.SkillUse&&a.Unit!=null&&defenders.Contains(a.Unit))return 1;
            if(IsMovement(a)&&defenders.Contains(a.Unit)&&Distance(a.TargetPos,defensePoint)<Distance(a.Unit.transform.position,defensePoint))return 1;
            if(a.ActionType==AIActionType.Summon&&Distance(a.TargetPos,defensePoint)<=4)return 1;
        }
        if (a.ActionType == AIActionType.Build || a.ActionType == AIActionType.Upgrade)
        {
            if (IsEmergencyDefenseRequired(a, board)) return 0;
            var production = ProductionDemand.EvaluateBuild(a);
            if (production.ImprovesCritical) return 1;
            if (production.ImprovesDeficit && Mode == StrategicMode.EconomicRecovery) return Mathf.Min(2, BasicResources.ActionPriority(a));
            if (production.ImprovesDeficit) return Mathf.Min(3, BasicResources.ActionPriority(a));
            if (a.ActionType == AIActionType.Upgrade && EconomyHelper.ImprovesProductionDemand(a, board))
                return Mathf.Min(Mode == StrategicMode.EconomicRecovery ? 2 : 3, BasicResources.ActionPriority(a));
        }
        int foundationPriority = BasicResources.ActionPriority(a);
        if (foundationPriority < 4) return foundationPriority;
        if(Mode != StrategicMode.EmergencyDefense && Mode != StrategicMode.EconomicRecovery
            &&a.Unit != null&&a.Unit.kind != Kind.King&&!a.Unit.HasMovedThisTurn&&IsMovement(a)
            &&Exploration.TryGetAssignedTarget(a.Unit,out var frontier)
            &&Exploration.IsObjectiveProgress(a.Unit,a.TargetPos,board)
            &&IncomingDamage(board,a.Unit,a.TargetPos,null)<a.Unit.HP)return 3;
        return 4;
    }
    public ProductionBuildAssessment BuildAssessment(AIAction action, AIBoardState board)
    {
        Evaluate(board);
        return ProductionDemand.EvaluateBuild(action);
    }

    /// <summary>Reserve AP only for a currently legal supply improvement, including upgrades.</summary>
    public int GetRecoveryReserveAP(AIBoardState board)
    {
        Evaluate(board);
        if (recoveryReserveAP >= 0) return recoveryReserveAP;
        recoveryReserveAP = 0;
        if (Mode == StrategicMode.EmergencyDefense) return 0;
        reserveCandidates.Clear();
        AIActionGenerator.GenerateBuildCandidates(board, reserveCandidates);
        AIActionGenerator.GenerateUpgradeCandidates(board, reserveCandidates);
        int cheapest = int.MaxValue;
        foreach (var action in reserveCandidates)
        {
            if (action.APCost <= 0 || action.APCost > board.EnemyAP || action.APCost >= cheapest) continue;
            bool improves = ProductionDemand.EvaluateBuild(action).ImprovesDeficit
                || EconomyHelper.ImprovesProductionDemand(action, board) || BasicResources.IsFoundationAction(action);
            if (improves && AllowExecution(action, board)) cheapest = action.APCost;
        }
        recoveryReserveAP = cheapest == int.MaxValue ? 0 : cheapest;
        return recoveryReserveAP;
    }

    public static bool IsMilitaryConstruction(AIAction action)
    {
        if (action == null || (action.ActionType != AIActionType.Build && action.ActionType != AIActionType.Upgrade)) return false;
        var kind = ConstructionKind(action);
        return FacilityData.IsWall(kind) || FacilityData.IsOffensive(kind);
    }

    static FacilityKind ConstructionKind(AIAction action)
    {
        if (action.ActionType == AIActionType.Upgrade && action.Unit != null)
            return action.Unit.AuthoredFacility != null ? action.Unit.AuthoredFacility.behaviourKind : action.Unit.facilityKind;
        return action.FacilityDefinition != null ? action.FacilityDefinition.behaviourKind : action.Facility;
    }

    bool IsEssentialDefenseSpend(AIAction action, AIBoardState board)
    {
        if (Mode != StrategicMode.EmergencyDefense) return false;
        if (IsEmergencyDefenseRequired(action, board)) return true;
        if (action.ActionType != AIActionType.Summon) return false;
        var definition = action.SummonDefinition ?? board.ResolveUnitDefinition(action.SummonKind);
        if (definition?.actionProfile?.CanAttack == false) return false;
        foreach (var target in critical)
        {
            if (!IsSevereThreat(target, IncomingDamage(board, target, target.transform.position, null))
                || Distance(action.TargetPos, target.transform.position) > 4) continue;
            foreach (var opponent in board.AlivePlayerUnits)
                if (opponent != null && opponent.IsAlive && Distance(action.TargetPos, opponent.transform.position) <= 4
                    && CanThreaten(board, opponent, target.transform.position)) return true;
        }
        return false;
    }

    bool IsSevereThreat(Status target, float incoming)
    {
        if (target == null || !target.IsAlive || incoming <= 0) return false;
        var settings = AIEconomySettings.Active;
        float threshold = target.kind == Kind.King ? settings.emergencyKingDamageFraction : settings.emergencyCrystalDamageFraction;
        if (float.IsNaN(threshold) || float.IsInfinity(threshold)) threshold = target.kind == Kind.King ? .4f : .75f;
        return incoming >= Mathf.Max(1, target.HP) * Mathf.Clamp(threshold, .05f, 1f);
    }

    // An emergency grants an exception only to a facility that covers an observed approach.
    public bool IsEmergencyDefenseRequired(AIAction action, AIBoardState board)
    {
        Evaluate(board);
        if (Mode != StrategicMode.EmergencyDefense || !IsMilitaryConstruction(action)) return false;
        var kind = ConstructionKind(action);
        Vector3 placement = action.ActionType == AIActionType.Upgrade && action.Unit != null
            ? action.Unit.transform.position : action.TargetPos;
        foreach (var target in critical)
        {
            if (!IsSevereThreat(target, IncomingDamage(board, target, target.transform.position, null))) continue;
            if (Distance(placement, target.transform.position) > 4) continue;
            foreach (var opponent in board.AlivePlayerUnits)
            {
                if (opponent == null || !opponent.IsAlive || !CanThreaten(board, opponent, target.transform.position)) continue;
                float approach = Distance(opponent.transform.position, target.transform.position);
                if (FacilityData.IsWall(kind))
                {
                    if (Distance(placement, target.transform.position) <= 2
                        && Distance(opponent.transform.position, placement) < approach
                        && Distance(opponent.transform.position, placement) + Distance(placement, target.transform.position) <= approach + 1)
                        return true;
                }
                else if (kind == FacilityKind.Cannon && Distance(placement, opponent.transform.position) <= 1) return true;
                else if (kind == FacilityKind.Mortar)
                {
                    float dx = Mathf.Abs(placement.x - opponent.transform.position.x);
                    float dz = Mathf.Abs(placement.z - opponent.transform.position.z);
                    if ((dx == 0 || dz == 0) && Mathf.Max(dx, dz) <= 2) return true;
                }
                else if ((kind == FacilityKind.RestraintTrap || kind == FacilityKind.SpikeTrap)
                    && Distance(placement, opponent.transform.position) <= 1) return true;
            }
        }
        return false;
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    void LogProductionDemand(AIBoardState board)
    {
        if (!AIEconomySettings.Active.enableDecisionLogs) return;
        DevelopmentLog.Log($"[AI経済] State={ProductionDemand.State} Mode={Mode} Turn={board.TurnCount} Team={board.ActorTeam}");
        foreach (var resource in ProductionDemand.Resources)
            DevelopmentLog.Log($"[AI経済] {resource.Resource} stock={resource.Stock:F1} prod={resource.ProductionPerTurn:F1} demand={resource.MandatoryDemandPerTurn:F1} plan={resource.PlannedDemandPerTurn:F1} net={resource.NetPerTurn:F1} reserve={resource.SafetyReserve:F1} projected={resource.ProjectedStock:F1} deficit={resource.ProductionDeficit:F1} urgency={resource.Urgency01:F2}");
        DevelopmentLog.Log($"[AI資源基盤] phase={BasicResources.FoundationState} bottleneck={BasicResources.PrimaryBottleneck}");
        foreach (var resource in BasicResources.Resources) DevelopmentLog.Log($"[AI資源基盤] {resource}");
    }

    bool ThreatensCritical(Status opponent,AIBoardState board)
    {foreach(var target in critical)if(CanThreaten(board,opponent,target.transform.position))return true;return false;}
    float Risk(AIAction action,AIBoardState board)
    {
        var unit=action.Unit;if(unit==null)return 0;Status eliminated=null;
        if(action.ActionType==AIActionType.Attack&&action.TargetUnit!=null&&action.TargetUnit.ShieldTurns<=0
            &&AIEvalHelpers.EstimateDamage(unit,action.TargetUnit)>=action.TargetUnit.HP)eliminated=action.TargetUnit;
        return IncomingDamage(board,unit,IsMovement(action)?action.TargetPos:unit.transform.position,eliminated);
    }
    public float IncomingDamage(AIBoardState board,Status self,Vector3 position,Status eliminated)
    {
        var key=(self,GridHelper.ToGridXZ(position),eliminated);if(damageCache.TryGetValue(key,out var cached))return cached;
        double total=0;
        foreach(var opponent in board.AlivePlayerUnits)
        {
            if(opponent==null||!opponent.IsAlive||opponent==eliminated||StatusEffectSystem.IsStunned(opponent))continue;
            float attack=ThreatPower(board,opponent,position);
            if(attack>0)total+=DamageCalculator.EstimateBaseDamage(Mathf.CeilToInt(attack),self.DEF);
        }
        return damageCache[key]=(float)Math.Min(int.MaxValue,total);
    }
    bool CanThreaten(AIBoardState board,Status opponent,Vector3 destination)
        =>ThreatPower(board,opponent,destination)>0;
    float ThreatPower(AIBoardState board,Status opponent,Vector3 destination)
    {
        var key=(opponent,GridHelper.ToGridXZ(destination));if(threatCache.TryGetValue(key,out var cached))return cached;
        var profile=BoardActionProfile.For(opponent);
        float Direct(Vector3 from)
        {
            float dx=destination.x-from.x,dz=destination.z-from.z;
            bool line=board.ReconMap==null||MapCreate.IsArcingAttack(opponent.kind,opponent.facilityKind)&&Distance(from,destination)>=2
                ||board.ReconMap.HasClearTerrainLine(from,destination);
            if(opponent.type!=Type.Unit&&(opponent.facilityKind==FacilityKind.Cannon||opponent.facilityKind==FacilityKind.Mortar))line=true;
            if(!line)return 0;
            bool normal=AttackPatterns.CanAttack(opponent,opponent.direction,dx,dz);
            if(opponent.type!=Type.Unit&&opponent.BuildingOperationAvailable&&profile?.CanAttack!=false&&profile?.attack?.useCustom!=true)
                normal=opponent.facilityKind==FacilityKind.Cannon?Distance(from,destination)==1
                    :opponent.facilityKind==FacilityKind.Mortar&&(dx==0||dz==0)&&Distance(from,destination)>=1&&Distance(from,destination)<=2;
            float power=normal?Mathf.Max(1,opponent.ATK):0;
            if(AttackPatterns.CanUseSkills(opponent)&&opponent.SkillCooldown<=1&&SkillData.Table.TryGetValue(opponent.AssignedSkillId,out var skill)
                &&skill.Multiplier>0&&skill.Target!=SkillTarget.Self&&skill.Target!=SkillTarget.AllySingle)
            {
                if(SkillOffsets(opponent,skill).Contains(new Vector2Int(Mathf.RoundToInt(dx),Mathf.RoundToInt(dz))))
                    power=Mathf.Max(power,opponent.ATK*(skill.Multiplier+Mathf.Max(0,skill.SecondMultiplier))+Mathf.Max(0,skill.FixedDamage));
            }
            return power;
        }
        float best=Direct(opponent.transform.position);
        if(StatusEffectSystem.IsMovementBlocked(opponent)||opponent.type!=Type.Unit||profile?.CanMove==false)return threatCache[key]=best;
        if(!reachable.TryGetValue(opponent,out var moves))reachable[opponent]=moves=board.GetValidMoves(opponent);
        foreach(var move in moves)best=Mathf.Max(best,Direct(move));
        return threatCache[key]=best;
    }
    HashSet<Vector2Int> SkillOffsets(Status actor,SkillData skill)
    {
        var key=(skill.Id,actor.kind,actor.direction);if(skillReach.TryGetValue(key,out var cached))return cached;
        var set=new HashSet<Vector2Int>();skillReach[key]=set;
        Vector2Int[] centers=null;
        if(skill.Target==SkillTarget.SelfArea)centers=new[]{Vector2Int.zero};
        else if(!AttackPatterns.SkillFixedPositions.TryGetValue(skill.Id,out centers))AttackPatterns.SkillAttackPositions.TryGetValue(actor.kind,out centers);
        if(centers==null)return set;
        var area=SkillSystem.GetAreaPositions(skill.Area,Vector3Int.zero,actor.direction);
        foreach(var center in centers)foreach(var offset in area)set.Add(new Vector2Int(center.x+offset.x,center.y*MovePatterns.DirZ(actor.direction)+offset.z));
        return set;
    }
    public void Telemetry(AIAction action,bool success)
    {
        if (success && Mode == StrategicMode.EmergencyDefense) BasicResources.NotifyEssentialSpend(action, true);
        DevelopmentLog.Log($"[AI戦略統括] 方針={Mode} 経済={Economy.State} パン収支={Economy.NetBreadPerTurn:F1} 優先=P{action.StrategicPriority} 結果={(success?"実行":"禁止")} 理由={action.StrategicRejectReason??"安全条件を満たす"}");
    }
    public PersistentStrategicObjective Snapshot()=>JsonUtility.FromJson<PersistentStrategicObjective>(JsonUtility.ToJson(Objective));
    public void RestoreExploration(AIExplorationState data)
    {
        Exploration.RestoreState(data);
        current = null; generation = -1; bridgedExploreObjective = -1;
        if (data != null) restoredLegacyObjective = false;
    }
    public void Restore(PersistentStrategicObjective data)
    {
        Objective=data!=null&&data.Version==1&&data.StartedTurn>=0&&!float.IsNaN(data.Target.sqrMagnitude)&&!float.IsInfinity(data.Target.sqrMagnitude)
            ?JsonUtility.FromJson<PersistentStrategicObjective>(JsonUtility.ToJson(data)):new PersistentStrategicObjective();
        current=null;generation=-1;
        bridgedExploreObjective = -1;
        restoredLegacyObjective = Objective.Active;
    }
    static float Distance(Vector3 a,Vector3 b)=>GridHelper.ChebyshevDistance(a,b);
    static bool IsMovement(AIAction a)=>a.ActionType==AIActionType.Move||a.ActionType==AIActionType.Retreat||a.ActionType==AIActionType.Support
        ||a.ActionType==AIActionType.Surround||a.ActionType==AIActionType.DefenseRepos;
}
