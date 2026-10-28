using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One observed value snapshot per board generation. Geography outside current vision is never refreshed from the map.</summary>
public sealed class AIWorldModelBuilder
{
    readonly Dictionary<Vector3Int, AIWorldTerrainValue> terrainMemory = new Dictionary<Vector3Int, AIWorldTerrainValue>();
    readonly List<Status> actors = new List<Status>();
    readonly HashSet<string> currentLives = new HashSet<string>(StringComparer.Ordinal);
    readonly List<Vector3Int> cells = new List<Vector3Int>();
    readonly HashSet<Vector3Int> uniqueCells = new HashSet<Vector3Int>();
    AIBoardState cachedBoard;
    AIObservedWorldModel cached;
    string battle = string.Empty;
    Team owner;
    int maxUnitSetting, maxTerrainSetting;
    public int BuildCount { get; private set; }

    public void Invalidate() { cachedBoard = null; cached = null; }
    public void Clear() { Invalidate(); terrainMemory.Clear(); actors.Clear(); currentLives.Clear(); cells.Clear(); uniqueCells.Clear(); battle = string.Empty; }

    public AIObservedWorldModel Build(AIBoardState board, string battleId, int ownTurn,
        int maxUnits = 128, int maxTerrainCells = 1600)
    {
        if (board == null) return null;
        maxUnits = Mathf.Clamp(maxUnits, 1, 256); maxTerrainCells = Mathf.Clamp(maxTerrainCells, 1, 1600);
        battleId = battleId ?? string.Empty; ownTurn = Mathf.Max(0, ownTurn);
        if (battle != battleId || owner != board.ActorTeam) { Clear(); battle = battleId; owner = board.ActorTeam; }
        if (cachedBoard == board && cached != null && cached.BoardGeneration == board.Generation && cached.OwnTurn == ownTurn
            && maxUnitSetting == maxUnits && maxTerrainSetting == maxTerrainCells) return cached;
        var own = new List<AIObservedUnitValue>(); var observed = new List<AIObservedUnitValue>();
        var past = new List<AIObservedUnitValue>(); var facts = new List<AIWorldFact>();
        var objectives = new List<AIWorldObjectiveValue>(); var operations = new List<AIWorldOperationValue>();
        var terrain = new List<AIWorldTerrainValue>(); var belief = new List<AIWorldBeliefCell>();
        int observationTurn = board.TurnCount;
        bool truncated = false;
        try
        {
            actors.Clear(); currentLives.Clear();
            foreach (var actor in board.SpecialObservedActors)
            {
                // Formal actor visibility includes stealth and the observing faction. A tile alone is insufficient.
                if (actor != null && board.IsSpecialActorObserved(actor)) actors.Add(actor);
            }
            actors.Sort((a, b) => string.CompareOrdinal(a.ReflectionLifeId, b.ReflectionLifeId));
            foreach (var actor in actors)
            {
                // Do not allow a cached live identity to outlive visibility or death during capture.
                if (actor == null || !board.IsSpecialActorObserved(actor)) continue;
                var destination = actor.team == board.ActorTeam ? own : observed;
                if (destination.Count >= maxUnits) { truncated = true; continue; }
                var value = Capture(actor, board, observationTurn);
                if (!currentLives.Add(value.LifeId)) continue;
                destination.Add(value);
                facts.Add(new AIWorldFact("actor:" + value.LifeId, AITruthValue.True,
                    AIWorldFactSource.CurrentObservation, observationTurn, 1, value.LifeId, value.Position, true));
                if (value.IsCrystal && (value.Team == board.ActorTeam || value.Team == board.OpponentTeam))
                    objectives.Add(new AIWorldObjectiveValue("crystal:" + value.LifeId, 0,
                        value.Team == board.ActorTeam ? AIOperationGoal.DefendOwnCrystal : AIOperationGoal.DestroyEnemyCrystal,
                        value.LifeId, value.Position, true, AIWorldFactSource.CurrentObservation, observationTurn, 1));
            }

            var memories = new List<AIBoardState.LastKnownInfo>();
            foreach (var pair in board.ObservedHistory)
                if (pair.Value.Valid && pair.Value.Turn <= board.TurnCount && pair.Value.Turn >= 0
                    && !currentLives.Contains(pair.Value.LifeId ?? string.Empty)) memories.Add(pair.Value);
            memories.Sort((a, b) => { int turn = b.Turn.CompareTo(a.Turn); return turn != 0 ? turn : string.CompareOrdinal(a.LifeId, b.LifeId); });
            foreach (var memory in memories)
            {
                if (past.Count >= maxUnits) { truncated = true; break; }
                int age = Mathf.Max(0, board.TurnCount - memory.Turn);
                // Existing observation memory already owns lifetime policy. No live HP/AP read is permitted here.
                float confidence = Mathf.Clamp01(1f / (1 + age));
                string life = memory.LifeId ?? string.Empty;
                past.Add(new AIObservedUnitValue(life, memory.Kind, memory.ObservedTeam, memory.Position,
                    -1, -1, -1, -1, memory.ObservedAttackPower, -1, memory.WasPrincipalCommander,
                    memory.Type != Type.Unit, memory.Kind == Kind.Crystal, memory.Turn,
                    AIWorldFactSource.PastObservation, confidence));
                facts.Add(new AIWorldFact("past:" + life, AITruthValue.Unknown, AIWorldFactSource.PastObservation,
                    memory.Turn, confidence, life, memory.Position, true, expirationPolicy: "observation-memory"));
                if (memory.Kind == Kind.Crystal && memory.ObservedTeam == board.OpponentTeam)
                    objectives.Add(new AIWorldObjectiveValue("remembered-crystal:" + life, 0,
                        AIOperationGoal.DestroyEnemyCrystal, life, memory.Position, true,
                        AIWorldFactSource.PastObservation, memory.Turn, confidence));
            }

            var map = board.MapCreate != null ? board.MapCreate : board.ReconMap;
            int width = map != null ? Mathf.Clamp(map.maxX, 0, 256) : 0;
            int depth = map != null ? Mathf.Clamp(map.maxZ, 0, 256) : 0;
            cells.Clear(); uniqueCells.Clear();
            foreach (var supplied in board.ObservedTerrainCells)
            {
                var cell = GridHelper.ToGridXZ(supplied);
                if (cell.x < 0 || cell.z < 0 || cell.x >= width || cell.z >= depth) continue;
                if (uniqueCells.Add(cell)) cells.Add(cell);
            }
            cells.Sort(CompareCells);
            foreach (var cell in cells)
            {
                if (terrain.Count >= maxTerrainCells) { truncated = true; break; }
                bool visible = board.IsVisibleToEnemy(cell);
                if (visible && map != null)
                {
                    bool hasHeight = map.TryGetHeight(cell.x, cell.z, out float height) && AIWorldFact.Finite(height);
                    bool blocked = !hasHeight || board.KnownEnvironmentDefinition(cell)?.blocksMovement == true;
                    var value = new AIWorldTerrainValue(cell, blocked ? AITruthValue.False : AITruthValue.True,
                        hasHeight, hasHeight ? height : 0, observationTurn, AIWorldFactSource.CurrentObservation, AITruthValue.True);
                    terrainMemory[cell] = value; terrain.Add(value);
                }
                else if (terrainMemory.TryGetValue(cell, out var old))
                    terrain.Add(new AIWorldTerrainValue(cell, old.Walkable, old.HasHeight, old.Height,
                        old.ObservedTurn, AIWorldFactSource.PastObservation, AITruthValue.False));
                else
                    terrain.Add(new AIWorldTerrainValue(cell, AITruthValue.Unknown, false, 0, -1,
                        AIWorldFactSource.Unknown, AITruthValue.False));
            }
            // The fixed board has at most 1600 cells; retain no history beyond the configured snapshot bound.
            if (terrainMemory.Count > maxTerrainCells)
            {
                terrainMemory.Clear();
                foreach (var value in terrain) if (value.Source != AIWorldFactSource.Unknown) terrainMemory[value.Cell] = value;
            }

            // Existing belief distributions already enforce actor limits. Copy values, never their ScriptableObject profiles.
            foreach (var contact in board.Belief.Contacts)
            {
                cells.Clear(); foreach (var cell in contact.Cells.Keys) cells.Add(cell); cells.Sort(CompareCells);
                foreach (var cell in cells)
                {
                    if (belief.Count >= 2048) { truncated = true; break; }
                    float probability = contact.Cells[cell];
                    if (!AIWorldFact.Finite(probability) || !AIWorldFact.Finite(contact.UnknownProbability)) continue;
                    belief.Add(new AIWorldBeliefCell(contact.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        cell, probability, contact.UnknownProbability, observationTurn));
                }
                if (belief.Count >= 2048) break;
            }

            if (board.Operations != null)
                foreach (var plan in board.Operations.ActiveOperations)
                {
                    if (plan == null) continue;
                    if (operations.Count >= 16) { truncated = true; break; }
                    var ids = new List<string>();
                    if (plan.AssignedUnitLifeIds != null) foreach (var id in plan.AssignedUnitLifeIds)
                        if (ids.Count < 80 && !string.IsNullOrEmpty(id)) ids.Add(id);
                    ids.Sort(StringComparer.Ordinal);
                    var position = new Vector3(plan.TargetX, 0, plan.TargetZ);
                    operations.Add(new AIWorldOperationValue(plan.OperationId, plan.PrimaryGoal, plan.Status,
                        plan.StartTurn, plan.LastProgressTurn, plan.TargetLifeId, position, plan.HasTargetPosition, ids.ToArray()));
                    facts.Add(new AIWorldFact("operation:" + plan.OperationId, AITruthValue.True,
                        AIWorldFactSource.CurrentObservation, observationTurn, value: plan.LastProgressTurn));
                    // These are known plan goals, not claims that a hidden target still occupies the location.
                    objectives.Add(new AIWorldObjectiveValue("operation:" + plan.OperationId, plan.OperationId,
                        plan.PrimaryGoal, plan.TargetLifeId, position, plan.HasTargetPosition,
                        AIWorldFactSource.Belief, observationTurn, .5f));
                }
            operations.Sort((a, b) => a.OperationId.CompareTo(b.OperationId));
            objectives.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

            var resources = board.EnemyResources;
            var demand = resources != null ? board.ProductionDemand : null;
            var economy = new AIWorldEconomyValue(resources != null, resources?.Wood ?? 0, resources?.Stone ?? 0,
                resources?.Water ?? 0, resources?.Iron ?? 0, resources?.MagicOre ?? 0, resources?.Wheat ?? 0,
                resources?.Bread ?? 0, resources?.Citizen ?? 0, board.CitizenCapacity, demand?.State ?? EconomicState.Healthy,
                demand?.HasCriticalDeficit ?? false, demand?.HasMandatoryPaymentFailure ?? false, demand?.Resources);
            AddFact(facts, "visible_opponents", observed.Count > 0, observationTurn);
            AddFact(facts, "emergency_defense", board.Governor?.Mode == StrategicMode.EmergencyDefense, observationTurn, board.Governor != null);
            AddFact(facts, "economic_recovery", board.Governor?.Mode == StrategicMode.EconomicRecovery, observationTurn, board.Governor != null);
            AddFact(facts, "resource_bottleneck", demand?.HasCriticalDeficit == true, observationTurn, demand != null);
            bool missing = false;
            int missingFront = 0, missingRanged = 0, missingHealer = 0, missingScout = 0;
            if (board.Operations != null) foreach (var need in board.Operations.SquadRecruitmentNeeds)
            {
                if (need.MissingCount <= 0 || need.NeededFromTurn > board.TurnCount) continue;
                missing = true;
                switch (need.Role)
                {
                    case "frontline": missingFront = Mathf.Min(80, missingFront + need.MissingCount); break;
                    case "ranged": missingRanged = Mathf.Min(80, missingRanged + need.MissingCount); break;
                    case "healer": missingHealer = Mathf.Min(80, missingHealer + need.MissingCount); break;
                    case "scout": missingScout = Mathf.Min(80, missingScout + need.MissingCount); break;
                }
            }
            AddFact(facts, "role_shortage", missing, observationTurn, board.Operations != null);
            AddFact(facts, "OperationRoleMissing", missing, observationTurn, board.Operations != null);
            AddRoleFact(facts, "frontline", missingFront, observationTurn, board.Operations != null);
            AddRoleFact(facts, "ranged", missingRanged, observationTurn, board.Operations != null);
            AddRoleFact(facts, "healer", missingHealer, observationTurn, board.Operations != null);
            AddRoleFact(facts, "scout", missingScout, observationTurn, board.Operations != null);
            bool hasCrystal = false;
            foreach (var objective in objectives) if (objective.Goal == AIOperationGoal.DestroyEnemyCrystal && objective.Source != AIWorldFactSource.Belief) { hasCrystal = true; break; }
            AddFact(facts, "known_enemy_crystal", hasCrystal, observationTurn, hasCrystal); // Absence of a sighting is not proof of no crystal.
            AddFact(facts, "uncertainty", past.Count > 0 || belief.Count > 0 || terrain.Count < width * depth, observationTurn);
            cachedBoard = board; maxUnitSetting = maxUnits; maxTerrainSetting = maxTerrainCells; BuildCount++;
            return cached = new AIObservedWorldModel(battleId, board.ActorTeam, ownTurn, board.Generation,
                width, depth, board.EnemyAP, economy, own.ToArray(), observed.ToArray(), past.ToArray(),
                terrain.ToArray(), belief.ToArray(), objectives.ToArray(), operations.ToArray(), facts.ToArray(), truncated, observationTurn,
                board.ObservationIdentity);
        }
        finally { actors.Clear(); currentLives.Clear(); cells.Clear(); uniqueCells.Clear(); }
    }

    static AIObservedUnitValue Capture(Status actor, AIBoardState board, int turn)
        => new AIObservedUnitValue(actor.ReflectionLifeId, actor.kind, actor.team, actor.transform.position,
            actor.HP, actor.MaxHP, actor.Barrier, actor.MaxBarrier, actor.ATK, actor.team == board.ActorTeam ? board.EnemyAP : -1,
            actor.IsPrimaryCommander, actor.type != Type.Unit && actor.kind != Kind.Crystal, actor.kind == Kind.Crystal, turn);

    static int CompareCells(Vector3Int first, Vector3Int second)
    { int x = first.x.CompareTo(second.x); if (x != 0) return x; int z = first.z.CompareTo(second.z); return z != 0 ? z : first.y.CompareTo(second.y); }
    static void AddFact(List<AIWorldFact> facts, string id, bool value, int turn, bool known = true)
        => facts.Add(new AIWorldFact(id, known ? value ? AITruthValue.True : AITruthValue.False : AITruthValue.Unknown,
            known ? AIWorldFactSource.CurrentObservation : AIWorldFactSource.Unknown, turn, known ? 1 : 0));
    static void AddRoleFact(List<AIWorldFact> facts, string role, int missing, int turn, bool known)
        => facts.Add(new AIWorldFact("missing_role:" + role, known ? missing > 0 ? AITruthValue.True : AITruthValue.False : AITruthValue.Unknown,
            known ? AIWorldFactSource.CurrentObservation : AIWorldFactSource.Unknown, turn, known ? 1 : 0, value: missing));
}
