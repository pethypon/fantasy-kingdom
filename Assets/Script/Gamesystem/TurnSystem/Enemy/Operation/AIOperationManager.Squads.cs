using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Operation-owned formations. Slots are recommendations over the current legal move list, never new movement rules.</summary>
public sealed partial class AIOperationManager
{
    readonly Dictionary<string, AIOperationSquad> squadByActor = new Dictionary<string, AIOperationSquad>(StringComparer.Ordinal);
    readonly Dictionary<string, AIOperationSquadMember> squadMembers = new Dictionary<string, AIOperationSquadMember>(StringComparer.Ordinal);
    readonly List<AIOperationRecruitmentNeed> squadRecruitment = new List<AIOperationRecruitmentNeed>(48);
    readonly List<Status> squadActors = new List<Status>(80);
    readonly HashSet<Vector3Int> squadSlots = new HashSet<Vector3Int>();
    readonly HashSet<string> squadLives = new HashSet<string>(StringComparer.Ordinal);
    readonly Dictionary<Status, List<Status>> squadAttackTargets = new Dictionary<Status, List<Status>>();
    readonly Dictionary<Status, AIOperationSquadRole> squadRoleCache = new Dictionary<Status, AIOperationSquadRole>();
    readonly int[] squadAssignmentCounts = new int[4];
    AIBoardState squadRoleBoard;
    int squadRoleGeneration = -1;
    AIBoardState squadAttackBoard;
    int squadAttackGeneration = -1;
    AIBoardState squadBoard;
    int squadGeneration = -1;
    public int SquadRevision { get; private set; }
    public IReadOnlyList<AIOperationSquad> Squads => state.Squads;
    public IReadOnlyList<AIOperationRecruitmentNeed> SquadRecruitmentNeeds => squadRecruitment;
    public bool HasCoordinatedSquad
    {
        get
        {
            if (state.Squads == null || squadBoard == null || squadGeneration != squadBoard.Generation || state.Ended) return false;
            foreach (var squad in state.Squads)
            {
                if (squad?.Members == null || squad.Phase == AIOperationSquadPhase.Suspended) continue;
                int active = 0;
                foreach (var member in squad.Members)
                    if (member != null && TryGetSquad(member.ActorLifeId, out var owner) && ReferenceEquals(owner, squad) && ++active >= 3) return true;
            }
            return false;
        }
    }
    public bool TryGetSquad(string actorLifeId, out AIOperationSquad squad)
    {
        squad = null;
        if (actorLifeId == null || state.Ended || squadBoard == null || squadGeneration != squadBoard.Generation
            || !AIOperationSquadConfig.Active.Enabled || !squadByActor.TryGetValue(actorLifeId, out var candidate)
            || state.Squads == null || !state.Squads.Contains(candidate)
            || !squadMembers.TryGetValue(actorLifeId, out var member) || !CurrentSquadMembership(candidate, member, actorLifeId)
            || !Running(FindActive(candidate.OperationId)) || !features.TryOwn(actorLifeId, out var actor)
            || !LiveSquadActor(actor, squadBoard)) return false;
        squad = candidate; return true;
    }

    public static AIOperationSquadRole SquadRole(Status actor)
        => AICapabilityRoles.For(actor).PrimarySquadRole;

    void UpdateSquads(AIBoardState board, int turn)
    {
        if (squadBoard == board && squadGeneration == board.Generation) return;
        var policy = AIOperationSquadConfig.Active;
        if (state.Squads == null) state.Squads = new List<AIOperationSquad>();
        squadByActor.Clear(); squadMembers.Clear(); squadRecruitment.Clear(); squadSlots.Clear(); squadLives.Clear(); squadAttackTargets.Clear();
        squadBoard = board; squadGeneration = board.Generation;
        if (!policy.Enabled || state.Ended) { state.Squads.Clear(); AdvanceSquadRevision(); return; }
        state.Squads.Sort((a, b) => a == null ? b == null ? 0 : 1 : b == null ? -1 : a.SquadId.CompareTo(b.SquadId));
        for (int i = 0; i < state.Squads.Count;)
        {
            var squad = state.Squads[i]; var plan = squad != null ? FindActive(squad.OperationId) : null;
            if (squad?.Members == null || squad.Members.Count > 5 || plan == null || !Running(plan)) { state.Squads.RemoveAt(i); continue; }
            squad.Members.RemoveAll(member => member == null || !features.TryOwn(member.ActorLifeId, out var actor)
                || !LiveSquadActor(actor, board)
                || !plan.AssignedUnitLifeIds.Contains(member.ActorLifeId)
                || assignments.TryGetValue(member.ActorLifeId, out var assigned) && assigned != plan
                || !squadLives.Add(member.ActorLifeId));
            foreach (var member in squad.Members) { member.HasSlot = false; member.MarchSlot = false; }
            squad.HasApproachWaypoint = false;
            if (squad.Members.Count == 0) state.Squads.RemoveAt(i); else i++;
        }
        foreach (var plan in state.ActiveOperations)
        {
            if (!Running(plan) || plan.PrimaryGoal == AIOperationGoal.EconomicRecovery) continue;
            foreach (string life in plan.AssignedUnitLifeIds)
            {
                if (squadLives.Contains(life) || !features.TryOwn(life, out var actor) || !LiveSquadActor(actor, board)) continue;
                AIOperationSquad selected = null;
                foreach (var squad in state.Squads) if (squad.OperationId == plan.OperationId && squad.Members.Count < policy.Size)
                    if (selected == null || squad.Members.Count < selected.Members.Count) selected = squad;
                if (selected == null)
                {
                    if (state.Squads.Count >= policy.Limit || state.NextSquadId >= long.MaxValue - 1) continue;
                    selected = new AIOperationSquad { SquadId = ++state.NextSquadId, OperationId = plan.OperationId,
                        FormedTurn = turn, LastAssemblyTurn = turn, LastProgressTurn = turn };
                    state.Squads.Add(selected);
                }
                selected.Members.Add(new AIOperationSquadMember { ActorLifeId = life, Role = CachedSquadRole(actor) }); squadLives.Add(life);
            }
        }
        BalanceSmallSquads(policy.Size);
        AlignSquadSupport();
        state.Squads.Sort((a, b) => a.SquadId.CompareTo(b.SquadId));
        foreach (var squad in state.Squads)
        {
            var plan = FindActive(squad.OperationId);
            if (plan == null) continue;
            squad.GoalX = plan.TargetX; squad.GoalZ = plan.TargetZ;
            squad.Phase = plan.Status == AIOperationStatus.Suspended ? AIOperationSquadPhase.Suspended : AIOperationSquadPhase.Advancing;
            squadActors.Clear(); Vector3 center = Vector3.zero;
            foreach (var member in squad.Members)
                if (features.TryOwn(member.ActorLifeId, out var actor) && actor != null && actor.IsAlive)
                {
                    member.Role = CachedSquadRole(actor);
                    float ratio = actor.HP / Mathf.Max(1f, actor.MaxHP);
                    member.Recovering = ratio < AIUnitPreservationConfig.Active.WoundedRatio
                        || member.Recovering && ratio < AIUnitPreservationConfig.Active.RejoinRatio;
                    squadActors.Add(actor); center += actor.transform.position;
                    if (!squadByActor.ContainsKey(member.ActorLifeId)) { squadByActor.Add(member.ActorLifeId, squad); squadMembers.Add(member.ActorLifeId, member); }
                }
            if (squadActors.Count == 0 || squad.Phase == AIOperationSquadPhase.Suspended) continue;
            center /= squadActors.Count;
            ChooseSquadApproach(squad, plan, center, board);
            bool together = true, contact = false, recovering = false;
            foreach (var actor in squadActors)
            {
                together &= AIOperationFeatureExtractor.Distance(actor.transform.position, center) <= Mathf.Clamp(policy.CohesionRadius, 2, 8);
                if (!contact) contact = LegalSquadTargets(actor, board).Count > 0;
            }
            if (contact) squad.Phase = AIOperationSquadPhase.Engaged;
            else if (!together && turn - squad.LastAssemblyTurn <= Mathf.Clamp(policy.AssemblyGraceTurns, 1, 5)) squad.Phase = AIOperationSquadPhase.Assembling;
            foreach (var member in squad.Members) recovering |= member.Recovering;
            if (!contact && (recovering || squad.Members.Count < 3)) squad.Phase = AIOperationSquadPhase.Reorganizing;
            squad.LastReason = contact ? "observed_combat" : together ? "formation_advancing"
                : squad.Phase == AIOperationSquadPhase.Assembling ? "members_regrouping" : "assembly_grace_elapsed_continue_with_available_members";
            foreach (var member in squad.Members)
                if (features.TryOwn(member.ActorLifeId, out var actor)) PickFormationSlot(squad, member, actor, plan, center, board);
            AddSquadRecruitment(squad, plan, board.TurnCount, policy.Size);
        }
        AdvanceSquadRevision();
    }
    void AdvanceSquadRevision() => SquadRevision = SquadRevision == int.MaxValue ? 0 : SquadRevision + 1;

    void AssignSquadRoles(AIOperationPlan plan, int quota, bool recon)
    {
        PruneOperationSquadAssignees(plan);
        // Reassignment clears the ownership index. Reclaim retained members
        // before searching for reinforcements, otherwise a retained actor can
        // be selected again and falsely counted as several units of force.
        foreach (string life in plan.AssignedUnitLifeIds) assignments[life] = plan;
        Array.Clear(squadAssignmentCounts, 0, squadAssignmentCounts.Length);
        foreach (string life in plan.AssignedUnitLifeIds)
            if (features.TryOwn(life, out var actor) && actor != null && actor.IsAlive) squadAssignmentCounts[(int)CachedSquadRole(actor)]++;
        // Retain wounded members for their normal recovery and return. Replace an excess
        // healthy role only when a concrete missing-role recruit can take its place.
        foreach (var actor in ownArmy)
        {
            if (actor == null || !actor.IsAlive || assignments.ContainsKey(actor.ReflectionLifeId)
                || actor.HP < actor.MaxHP * .4f || !EligibleSquadActor(actor)) continue;
            var role = CachedSquadRole(actor);
            if (squadAssignmentCounts[(int)role] >= DesiredRoleCount(role, quota, recon)) continue;
            if (plan.AssignedUnitLifeIds.Count >= quota)
                for (int i = plan.AssignedUnitLifeIds.Count - 1; i >= 0; i--)
                    if (features.TryOwn(plan.AssignedUnitLifeIds[i], out var prior) && prior != null
                        && prior.HP >= prior.MaxHP * AIUnitPreservationConfig.Active.RejoinRatio
                        && squadAssignmentCounts[(int)CachedSquadRole(prior)] > DesiredRoleCount(CachedSquadRole(prior), quota, recon))
                    { squadAssignmentCounts[(int)CachedSquadRole(prior)]--; assignments.Remove(prior.ReflectionLifeId); plan.AssignedUnitLifeIds.RemoveAt(i); break; }
        }
        while (plan.AssignedUnitLifeIds.Count < quota)
        {
            Status best = null; float bestValue = float.NegativeInfinity;
            foreach (var actor in ownArmy)
            {
                if (actor == null || !actor.IsAlive || assignments.ContainsKey(actor.ReflectionLifeId)
                    || plan.AssignedUnitLifeIds.Contains(actor.ReflectionLifeId)
                    || actor.HP < actor.MaxHP * .4f || !EligibleSquadActor(actor)) continue;
                var role = CachedSquadRole(actor); int desired = DesiredRoleCount(role, quota, recon);
                float shortage = desired > 0 ? Mathf.Max(0, desired - squadAssignmentCounts[(int)role]) / (float)desired : 0;
                float value = shortage * 100 + actor.HP / Mathf.Max(1f, actor.MaxHP) * 4
                    + Mathf.Min(10, AIOperationFeatureExtractor.MilitaryPower(actor) * .02f)
                    - (plan.HasTargetPosition ? AIOperationFeatureExtractor.Distance(actor.transform.position, new Vector3(plan.TargetX, 0, plan.TargetZ)) * .05f : 0);
                if (best == null || value > bestValue || Mathf.Approximately(value, bestValue)
                    && string.CompareOrdinal(actor.ReflectionLifeId, best.ReflectionLifeId) < 0) { best = actor; bestValue = value; }
            }
            if (best == null) break;
            string life = best.ReflectionLifeId; plan.AssignedUnitLifeIds.Add(life); assignments[life] = plan;
            squadAssignmentCounts[(int)CachedSquadRole(best)]++;
            if (!plan.OriginalAssignedUnitLifeIds.Contains(life) && plan.OriginalAssignedUnitLifeIds.Count < config.AssignmentLimit) plan.OriginalAssignedUnitLifeIds.Add(life);
        }
        plan.AssignedUnitRoles.Clear();
        foreach (string life in plan.AssignedUnitLifeIds)
            if (features.TryOwn(life, out var actor))
            { string role = AICapabilityRoles.RecruitmentRole(actor); if (role != null && !plan.AssignedUnitRoles.Contains(role)) plan.AssignedUnitRoles.Add(role); }
    }
    bool EligibleSquadActor(Status actor)
    {
        if (actor == null || !actor.IsAlive || actor.type != Type.Unit || actor.team != state.Faction
            || !actor.gameObject.activeInHierarchy || MatchObjectiveRules.IsCommander(actor)) return false;
        var capability = AICapabilityRoles.For(actor);
        return !capability.Engineer && (capability.CanAttack || capability.Reconnaissance || capability.Healing);
    }
    void PrepareSquadRoleCache(AIBoardState board)
    { if (squadRoleBoard != board || squadRoleGeneration != board.Generation) { squadRoleCache.Clear(); squadRoleBoard = board; squadRoleGeneration = board.Generation; } }
    AIOperationSquadRole CachedSquadRole(Status actor)
    {
        if (squadRoleCache.TryGetValue(actor, out var role)) return role;
        role = SquadRole(actor); if (squadRoleCache.Count < 128) squadRoleCache.Add(actor, role); return role;
    }
    static int DesiredRoleCount(AIOperationSquadRole role, int quota, bool recon)
    {
        if (quota <= 0) return 0;
        if (recon) return role == AIOperationSquadRole.Scout ? 1 : role == AIOperationSquadRole.Frontline ? quota >= 2 ? 1 : 0
            : role == AIOperationSquadRole.Ranged ? quota >= 3 ? 1 : 0 : 0;
        int groups = (quota + 4) / 5, healers = quota / 5;
        int ranged = Mathf.Min(groups * 2, Mathf.Max(0, (quota - healers) / 2));
        return role == AIOperationSquadRole.Healer ? healers : role == AIOperationSquadRole.Ranged ? ranged
            : role == AIOperationSquadRole.Frontline ? quota - ranged - healers : 0;
    }

    void BalanceSmallSquads(int maximum)
    {
        foreach (var small in state.Squads)
            while (small.Members.Count < 3)
            {
                AIOperationSquad donor = null;
                foreach (var candidate in state.Squads)
                    if (candidate != small && candidate.OperationId == small.OperationId && candidate.Members.Count > 3
                        && (donor == null || candidate.Members.Count > donor.Members.Count)) donor = candidate;
                if (donor == null || small.Members.Count >= maximum) break;
                small.Members.Add(donor.Members[donor.Members.Count - 1]); donor.Members.RemoveAt(donor.Members.Count - 1);
            }
    }

    void AlignSquadSupport()
    {
        // A partial second squad must not strand the only healer with the rear
        // group while the leading frontline fights unsupported. Swap memberships,
        // preserving counts, unique ownership and the retained operation identity.
        foreach (var source in state.Squads)
        {
            var plan = FindActive(source.OperationId);
            if (plan == null || !plan.HasTargetPosition || plan.Status != AIOperationStatus.Active) continue;
            int healerIndex = -1;
            for (int i = 0; i < source.Members.Count; i++)
                if (features.TryOwn(source.Members[i].ActorLifeId, out var actor) && CachedSquadRole(actor) == AIOperationSquadRole.Healer)
                { healerIndex = i; break; }
            if (healerIndex < 0) continue;
            int FrontDistance(AIOperationSquad squad)
            {
                int distance = int.MaxValue / 2;
                foreach (var member in squad.Members)
                    if (features.TryOwn(member.ActorLifeId, out var actor) && actor.IsAlive
                        && CachedSquadRole(actor) == AIOperationSquadRole.Frontline)
                        distance = Math.Min(distance, AIOperationFeatureExtractor.Distance(actor.transform.position, new Vector3(plan.TargetX, 0, plan.TargetZ)));
                return distance;
            }
            var destination = source; int best = FrontDistance(source);
            foreach (var candidate in state.Squads)
            {
                if (candidate == source || candidate.OperationId != source.OperationId) continue;
                bool hasHealer = false;
                foreach (var member in candidate.Members)
                    if (features.TryOwn(member.ActorLifeId, out var actor) && CachedSquadRole(actor) == AIOperationSquadRole.Healer) hasHealer = true;
                int distance = FrontDistance(candidate);
                if (!hasHealer && distance + 2 < best) { destination = candidate; best = distance; }
            }
            if (destination == source) continue;
            int exchange = -1, furthest = -1, frontliners = 0;
            foreach (var member in destination.Members)
                if (features.TryOwn(member.ActorLifeId, out var actor) && CachedSquadRole(actor) == AIOperationSquadRole.Frontline) frontliners++;
            for (int i = 0; i < destination.Members.Count; i++)
            {
                if (!features.TryOwn(destination.Members[i].ActorLifeId, out var actor) || !actor.IsAlive
                    || CachedSquadRole(actor) == AIOperationSquadRole.Healer
                    || frontliners <= 1 && CachedSquadRole(actor) == AIOperationSquadRole.Frontline) continue;
                int distance = AIOperationFeatureExtractor.Distance(actor.transform.position, new Vector3(plan.TargetX, 0, plan.TargetZ));
                if (distance > furthest) { exchange = i; furthest = distance; }
            }
            if (exchange < 0) continue;
            var healer = source.Members[healerIndex]; source.Members[healerIndex] = destination.Members[exchange];
            destination.Members[exchange] = healer;
        }
    }

    public bool SupportsSquadPatient(Status healer, Status patient)
        => healer != null && patient != null && healer != patient && healer.IsAlive && patient.IsAlive
            && AIHealingCapability.HasHealing(healer)
            && TryGetSquad(healer.ReflectionLifeId, out var owner) && TryGetSquad(patient.ReflectionLifeId, out var recipient)
            && owner.OperationId == recipient.OperationId;

    void PickFormationSlot(AIOperationSquad squad, AIOperationSquadMember member, Status actor,
        AIOperationPlan plan, Vector3 center, AIBoardState board)
    {
        Vector3 Aim(Status unit) => squad.HasApproachWaypoint
            && AIOperationFeatureExtractor.Distance(unit.transform.position, new Vector3(squad.ApproachX, 0, squad.ApproachZ)) > 1
                ? new Vector3(squad.ApproachX, 0, squad.ApproachZ) : MovementAim(plan, unit);
        Vector3 forward = Aim(actor) - center; forward.y = 0;
        if (forward.sqrMagnitude < .01f) forward = Vector3.forward; else forward.Normalize();
        var side = new Vector3(forward.z, 0, -forward.x);
        int index = squad.Members.IndexOf(member); float lateral = index % 2 == 0 ? -1 : 1;
        bool rear = member.Recovering || actor.HP < actor.MaxHP * AIUnitPreservationConfig.Active.RejoinRatio;
        var capability = AICapabilityRoles.For(actor);
        float rank = rear ? -2 : member.Role == AIOperationSquadRole.Frontline ? 1
            : member.Role == AIOperationSquadRole.Scout ? 1 : member.Role == AIOperationSquadRole.Healer ? -2
            : -Mathf.Clamp(capability.MaximumAttackReach - 1, 1, 4);
        // A moving local anchor, not a hard geometric grid. Only current real legal moves are inspected.
        Vector3 anchor = center;
        if (squad.Phase != AIOperationSquadPhase.Assembling && !rear)
        {
            // Once the grace period ends, a lagging member cannot pull the whole squad
            // back indefinitely. Healthy front units establish the moving local anchor.
            Status lead = null; int bestDistance = int.MaxValue;
            foreach (var partner in squadActors)
            {
                if (partner.HP < partner.MaxHP * AIUnitPreservationConfig.Active.RejoinRatio) continue;
                int distance = AIOperationFeatureExtractor.Distance(partner.transform.position, Aim(partner));
                // Keep the firing line anchored on a real healthy shield/front actor
                // when one is available, instead of following the fastest ranged unit.
                if (CachedSquadRole(partner) != AIOperationSquadRole.Frontline) distance += 3;
                if (lead == null || distance < bestDistance
                    || distance == bestDistance && string.CompareOrdinal(partner.ReflectionLifeId, lead.ReflectionLifeId) < 0)
                { lead = partner; bestDistance = distance; }
            }
            anchor = (lead != null ? lead.transform.position : center) + forward;
        }
        Vector3 ideal = anchor + forward * rank + side * lateral;
        // Formation is subordinate to the retained operation. A legal route may
        // initially lead sideways/backwards around terrain, rather than orbiting
        // the lead actor or walking back to an unreachable geometric slot.
        if (!rear && squad.Phase != AIOperationSquadPhase.Assembling
            && TryPickMarchSlot(plan, member, actor, member.Role == AIOperationSquadRole.Healer ? ideal : Aim(actor), ideal, board)) return;
        float best = float.NegativeInfinity; Vector3 destination = actor.transform.position; bool found = false;
        foreach (var cell in board.GetValidMoves(actor))
        {
            if (board.CalcMoveCost(actor, cell) > board.EnemyAP || squadSlots.Contains(GridHelper.ToGridXZ(cell))
                || member.Role != AIOperationSquadRole.Scout && !board.IsTerrainKnown(cell)) continue;
            float danger = board.Governor?.IncomingDamage(board, actor, cell, null) ?? board.EstimateCounterDamageAt(cell, actor);
            if (danger >= actor.HP) continue;
            if (member.Role == AIOperationSquadRole.Ranged && AdjacentObservedHostile(cell, board)) continue;
            float score = -AIOperationFeatureExtractor.Distance(cell, ideal) - danger / Mathf.Max(1, actor.HP) * 4;
            if (!rear) score += Mathf.Clamp(AIOperationFeatureExtractor.Distance(actor.transform.position, Aim(actor))
                - AIOperationFeatureExtractor.Distance(cell, Aim(actor)), -2, 2);
            if (member.Role == AIOperationSquadRole.Healer) score += board.CountAlliesNear(cell, actor, 2) * .2f;
            if (!found || score > best || Mathf.Approximately(score, best) && CompareCell(cell, destination) < 0)
            { found = true; best = score; destination = cell; }
        }
        member.HasSlot = found;
        if (found) { member.SlotX = Mathf.RoundToInt(destination.x); member.SlotZ = Mathf.RoundToInt(destination.z); squadSlots.Add(GridHelper.ToGridXZ(destination)); }
    }
    static int CompareCell(Vector3 a, Vector3 b) => a.x != b.x ? a.x.CompareTo(b.x) : a.z != b.z ? a.z.CompareTo(b.z) : a.y.CompareTo(b.y);

    static bool AdjacentObservedHostile(Vector3 cell, AIBoardState board)
    {
        foreach (var target in board.AlivePlayerUnits)
            if (target != null && target.IsAlive && board.IsSpecialActorObserved(target)
                && GridHelper.ChebyshevDistance(cell, target.transform.position) <= 1) return true;
        return false;
    }

    void ChooseSquadApproach(AIOperationSquad squad, AIOperationPlan plan, Vector3 center, AIBoardState board)
    {
        squad.HasApproachWaypoint = false;
        if (board.MapCreate == null || plan.Status != AIOperationStatus.Active || board.ReconThreatLevel < Mathf.Max(1, config.MinimumThreatLevel)) return;
        bool flank = plan.PrimaryGoal == AIOperationGoal.FlankEnemyForce || plan.PrimaryGoal == AIOperationGoal.SurroundEnemyForce
            || plan.PrimaryGoal == AIOperationGoal.CreateDiversion
            || plan.IsBaseAssault && (plan.BaseAssaultPattern == AIBaseAssaultPattern.FlankAssault || plan.BaseAssaultPattern == AIBaseAssaultPattern.DiversionAssault);
        if (!flank) return;
        int ordinal = 0, peers = 0;
        foreach (var other in state.Squads)
            if (other.OperationId == squad.OperationId) { if (other.SquadId < squad.SquadId) ordinal++; peers++; }
        if (peers < 2) return;
        var target = new Vector3(plan.TargetX, 0, plan.TargetZ);
        var forward = target - center; forward.y = 0;
        if (forward.sqrMagnitude < .01f) return;
        forward.Normalize(); var side = new Vector3(forward.z, 0, -forward.x);
        var desired = target + side * (ordinal % 2 == 0 ? -RallyRadius : RallyRadius);
        Vector3 best = default; float cost = float.PositiveInfinity;
        // A bounded, known-only corridor test. An unknown or obstructed flank remains
        // an ordinary approach; it is never declared a confirmed alternative route.
        for (int offset = -1; offset <= 1; offset++)
        {
            int x = Mathf.RoundToInt(desired.x + forward.x * offset), z = Mathf.RoundToInt(desired.z + forward.z * offset);
            var cell = new Vector3(x, 0, z);
            if (!board.IsTerrainKnown(cell) || !board.MapCreate.TryGetHeight(x, z, out float height)) continue;
            cell.y = height;
            if (!KnownBaseCorridorClear(board, center, cell)) continue;
            float distance = AIOperationFeatureExtractor.Distance(center, cell);
            if (distance < cost || Mathf.Approximately(distance, cost) && CompareCell(cell, best) < 0) { cost = distance; best = cell; }
        }
        if (float.IsPositiveInfinity(cost)) return;
        squad.HasApproachWaypoint = true; squad.ApproachX = Mathf.RoundToInt(best.x); squad.ApproachZ = Mathf.RoundToInt(best.z);
    }

    void AddSquadRecruitment(AIOperationSquad squad, AIOperationPlan plan, int turn, int size)
    {
        if (squadRecruitment.Count >= 48 || plan.Status == AIOperationStatus.Suspended) return;
        int front = 0, ranged = 0, healers = 0, scouts = 0;
        foreach (var member in squad.Members)
            switch (member.Role) { case AIOperationSquadRole.Frontline: front++; break; case AIOperationSquadRole.Ranged: ranged++; break; case AIOperationSquadRole.Healer: healers++; break; case AIOperationSquadRole.Scout: scouts++; break; }
        bool recon = plan.PrimaryGoal == AIOperationGoal.ScoutRegion || plan.PrimaryGoal == AIOperationGoal.LocateEnemyMainForce;
        void Need(string role, int amount) { if (amount > 0 && squadRecruitment.Count < 48) squadRecruitment.Add(new AIOperationRecruitmentNeed(role, amount, turn, plan.OperationId)); }
        Need("frontline", Mathf.Max(0, (recon ? 1 : size >= 4 ? 2 : 1) - front));
        Need("ranged", Mathf.Max(0, (recon ? 1 : size >= 5 ? 2 : 1) - ranged));
        if (recon) Need("scout", Mathf.Max(0, 1 - scouts));
        else if (size >= 5) Need("healer", Mathf.Max(0, 1 - healers));
    }

    public int SquadActionPriority(AIAction action, AIBoardState board)
    {
        if (!LegalSquadAction(action, board, out var squad, out var member)) return 4;
        if (coordinationHead == action && CurrentCoordination(board)) return 2;
        if (action.ActionType == AIActionType.Attack && action.TargetUnit != null && board.IsSpecialActorObserved(action.TargetUnit)
            && LegalSquadTargets(action.Unit, board).Contains(action.TargetUnit)
            && AIOperationFeatureExtractor.Distance(action.TargetUnit.transform.position, new Vector3(squad.GoalX, 0, squad.GoalZ)) <= DefenseRadius)
            return 2;
        if (IsMovement(action.ActionType) && member.HasSlot && !action.Unit.HasMovedThisTurn
            && action.TargetPos.x == member.SlotX && action.TargetPos.z == member.SlotZ
            && board.GetValidMoves(action.Unit).Contains(action.TargetPos)) return 3;
        return 4;
    }
    public float SquadActionBonus(AIAction action, AIBoardState board)
    {
        if (!LegalSquadAction(action, board, out var squad, out var member)) return 0;
        float bonus = coordinationHead == action && CurrentCoordination(board) ? 1 : 0;
        if (IsMovement(action.ActionType) && member.HasSlot && action.TargetPos.x == member.SlotX && action.TargetPos.z == member.SlotZ) bonus += 1;
        return Mathf.Min(Mathf.Clamp(AIOperationConfig.Finite(AIOperationSquadConfig.Active.MaximumScoreBonus), 0, 3), bonus);
    }
    bool LegalSquadAction(AIAction action, AIBoardState board, out AIOperationSquad squad, out AIOperationSquadMember member)
    {
        squad = null; member = null;
        if (action?.Unit == null || board == null || !AIOperationSquadConfig.Active.Enabled || state.Ended || action.Unit.team != board.ActorTeam
            || !action.Unit.IsAlive || MatchObjectiveRules.IsCommander(action.Unit) || action.APCost < 0 || action.APCost > board.EnemyAP
            || StatusEffectSystem.IsStunned(action.Unit)) return false;
        if (squadBoard != board || squadGeneration != board.Generation) return false;
        if (!TryGetSquad(action.Unit.ReflectionLifeId, out squad)
            || !squadMembers.TryGetValue(action.Unit.ReflectionLifeId, out member)) return false;
        var point = IsMovement(action.ActionType) ? action.TargetPos : action.Unit.transform.position;
        return (board.Governor?.IncomingDamage(board, action.Unit, point, null) ?? board.EstimateCounterDamageAt(point, action.Unit)) < action.Unit.HP
            || AllowsObjectiveSacrifice(action, board);
    }

    void SanitizeSquads(AIOperationBattleState restored)
    {
        SanitizeSquadState(restored);
        squadByActor.Clear(); squadMembers.Clear(); squadRecruitment.Clear(); squadSlots.Clear(); squadAttackTargets.Clear(); squadBoard = null; squadGeneration = -1;
        squadRoleCache.Clear(); squadRoleBoard = null; squadRoleGeneration = -1;
        ClearCoordination();
        ClearMarch();
    }
    List<Status> LegalSquadTargets(Status actor, AIBoardState board)
    {
        if (board == null || actor == null) return new List<Status>();
        if (squadAttackBoard != board || squadAttackGeneration != board.Generation)
        { squadAttackTargets.Clear(); squadAttackBoard = board; squadAttackGeneration = board.Generation; }
        if (squadAttackTargets.TryGetValue(actor, out var targets)) return targets;
        targets = board.GetAttackTargets(actor);
        if (squadAttackTargets.Count < 128) squadAttackTargets.Add(actor, targets);
        return targets;
    }
}
