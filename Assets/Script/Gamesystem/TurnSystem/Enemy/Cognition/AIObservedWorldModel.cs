using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct AIObservedUnitValue
{
    public readonly string LifeId;
    public readonly Kind Kind;
    public readonly Team Team;
    public readonly Vector3 Position;
    // -1 means that this attribute was not part of the observation, especially for past contacts.
    public readonly int HP, MaxHP, Barrier, MaxBarrier, ATK, AP, ObservedTurn;
    public readonly bool IsCommander, IsBuilding, IsCrystal;
    public readonly AIWorldFactSource Source;
    public readonly float Confidence;
    public AIObservedUnitValue(string lifeId, Kind kind, Team team, Vector3 position, int hp, int maxHp,
        int barrier, int maxBarrier, int atk, int ap, bool commander, bool building, bool crystal,
        int observedTurn, AIWorldFactSource source = AIWorldFactSource.CurrentObservation, float confidence = 1)
    {
        LifeId = lifeId ?? string.Empty; Kind = kind; Team = team; Position = position;
        HP = hp; MaxHP = maxHp; Barrier = barrier; MaxBarrier = maxBarrier; ATK = atk; AP = ap;
        IsCommander = commander; IsBuilding = building; IsCrystal = crystal;
        ObservedTurn = observedTurn; Source = source;
        Confidence = AIWorldFact.Finite(confidence) ? Mathf.Clamp01(confidence) : 0;
    }
}

public readonly struct AIWorldTerrainValue
{
    public readonly Vector3Int Cell;
    public readonly AITruthValue Walkable, CurrentlyVisible;
    public readonly bool HasHeight;
    public readonly float Height;
    public readonly int ObservedTurn;
    public readonly AIWorldFactSource Source;
    public AIWorldTerrainValue(Vector3Int cell, AITruthValue walkable, bool hasHeight, float height,
        int turn, AIWorldFactSource source, AITruthValue visible)
    { Cell = cell; Walkable = walkable; HasHeight = hasHeight; Height = height; ObservedTurn = turn; Source = source; CurrentlyVisible = visible; }
}

public readonly struct AIWorldObjectiveValue
{
    public readonly string Id, TargetLifeId;
    public readonly long OperationId;
    public readonly AIOperationGoal Goal;
    public readonly Vector3 Position;
    public readonly bool HasPosition;
    public readonly AIWorldFactSource Source;
    public readonly int ObservedTurn;
    public readonly float Confidence;
    public AIWorldObjectiveValue(string id, long operationId, AIOperationGoal goal, string targetLifeId,
        Vector3 position, bool hasPosition, AIWorldFactSource source, int turn, float confidence)
    { Id = id ?? string.Empty; OperationId = operationId; Goal = goal; TargetLifeId = targetLifeId ?? string.Empty;
        Position = position; HasPosition = hasPosition; Source = source; ObservedTurn = turn;
        Confidence = AIWorldFact.Finite(confidence) ? Mathf.Clamp01(confidence) : 0; }
}

public readonly struct AIWorldOperationValue
{
    public readonly long OperationId;
    public readonly AIOperationGoal Goal;
    public readonly AIOperationStatus Status;
    public readonly int StartTurn, LastProgressTurn;
    public readonly string TargetLifeId;
    public readonly Vector3 Position;
    public readonly bool HasPosition;
    public readonly IReadOnlyList<string> AssignedLifeIds;
    public AIWorldOperationValue(long id, AIOperationGoal goal, AIOperationStatus status, int startTurn,
        int lastProgressTurn, string targetLifeId, Vector3 position, bool hasPosition, string[] assignedLifeIds)
    { OperationId = id; Goal = goal; Status = status; StartTurn = startTurn; LastProgressTurn = lastProgressTurn;
        TargetLifeId = targetLifeId ?? string.Empty; Position = position; HasPosition = hasPosition;
        AssignedLifeIds = Array.AsReadOnly((string[])(assignedLifeIds ?? Array.Empty<string>()).Clone()); }
}

public readonly struct AIWorldBeliefCell
{
    public readonly string ContactId;
    public readonly Vector3Int Cell;
    public readonly float Probability, UnknownProbability;
    public readonly int ObservedTurn;
    public AIWorldBeliefCell(string contactId, Vector3Int cell, float probability, float unknownProbability, int turn)
    { ContactId = contactId ?? string.Empty; Cell = cell; Probability = Mathf.Clamp01(probability);
        UnknownProbability = Mathf.Clamp01(unknownProbability); ObservedTurn = turn; }
}

public readonly struct AIWorldEconomyValue
{
    public readonly bool Known, HasCriticalDeficit, HasMandatoryPaymentFailure;
    public readonly int Wood, Stone, Water, Iron, MagicOre, Wheat, Bread, Citizen, CitizenCapacity;
    public readonly EconomicState State;
    public readonly IReadOnlyList<ResourceDemandState> Forecast;
    public AIWorldEconomyValue(bool known, int wood, int stone, int water, int iron, int magicOre,
        int wheat, int bread, int citizen, int capacity, EconomicState state,
        bool critical, bool paymentFailure, ResourceDemandState[] forecast)
    { Known = known; Wood = wood; Stone = stone; Water = water; Iron = iron; MagicOre = magicOre;
        Wheat = wheat; Bread = bread; Citizen = citizen; CitizenCapacity = capacity; State = state;
        HasCriticalDeficit = critical; HasMandatoryPaymentFailure = paymentFailure;
        Forecast = Array.AsReadOnly((ResourceDemandState[])(forecast ?? Array.Empty<ResourceDemandState>()).Clone()); }
}

/// <summary>Immutable, faction-local value snapshot. No Unity actor, Transform or mutable board collection escapes.</summary>
public sealed class AIObservedWorldModel
{
    public string BattleId { get; }
    public Team ActorTeam { get; }
    public Team Team => ActorTeam;
    public int OwnTurn { get; }
    public int GlobalTurn { get; }
    public int BoardGeneration { get; }
    public int Width { get; }
    public int Depth { get; }
    public int OwnAP { get; }
    public int UnknownCount { get; }
    public bool Truncated { get; }
    public AIWorldEconomyValue OwnEconomy { get; }
    public IReadOnlyList<AIObservedUnitValue> OwnUnits { get; }
    public IReadOnlyList<AIObservedUnitValue> ObservedOpponents { get; }
    public IReadOnlyList<AIObservedUnitValue> LastKnownOpponents { get; }
    public IReadOnlyList<AIWorldTerrainValue> KnownTerrain { get; }
    public IReadOnlyList<AIWorldBeliefCell> BeliefRegions { get; }
    public IReadOnlyList<AIWorldObjectiveValue> Objectives { get; }
    public IReadOnlyList<AIWorldOperationValue> OngoingOperations { get; }
    public IReadOnlyList<AIWorldFact> Facts { get; }
    public IReadOnlyList<string> ObservedFactIds { get; }
    readonly Dictionary<string, AIWorldFact> factIndex;

    public AIObservedWorldModel(string battleId, Team actorTeam, int ownTurn, int generation,
        int width, int depth, int ownAp, AIWorldEconomyValue economy,
        AIObservedUnitValue[] own = null, AIObservedUnitValue[] observed = null, AIObservedUnitValue[] past = null,
        AIWorldTerrainValue[] terrain = null, AIWorldBeliefCell[] belief = null,
        AIWorldObjectiveValue[] objectives = null, AIWorldOperationValue[] operations = null,
        AIWorldFact[] facts = null, bool truncated = false, int globalTurn = -1)
    {
        BattleId = battleId ?? string.Empty; ActorTeam = actorTeam; OwnTurn = ownTurn; BoardGeneration = generation;
        GlobalTurn = globalTurn >= 0 ? globalTurn : ownTurn;
        Width = Mathf.Clamp(width, 0, 256); Depth = Mathf.Clamp(depth, 0, 256); OwnAP = Mathf.Max(0, ownAp);
        OwnEconomy = economy; Truncated = truncated;
        OwnUnits = Freeze(own); ObservedOpponents = Freeze(observed); LastKnownOpponents = Freeze(past);
        KnownTerrain = Freeze(terrain); BeliefRegions = Freeze(belief); Objectives = Freeze(objectives); OngoingOperations = Freeze(operations);
        factIndex = new Dictionary<string, AIWorldFact>(StringComparer.Ordinal);
        var accepted = new List<AIWorldFact>(); var ids = new List<string>(); int unknown = 0;
        if (facts != null) foreach (var fact in facts)
        {
            if (string.IsNullOrEmpty(fact.Id) || factIndex.ContainsKey(fact.Id) || fact.ObservedTurn > GlobalTurn) continue;
            factIndex.Add(fact.Id, fact); accepted.Add(fact); ids.Add(fact.Id);
            if (fact.Truth == AITruthValue.Unknown) unknown++;
        }
        Facts = accepted.AsReadOnly(); ObservedFactIds = ids.AsReadOnly();
        UnknownCount = unknown + Mathf.Max(0, Width * Depth - KnownTerrain.Count);
    }

    static IReadOnlyList<T> Freeze<T>(T[] values) => Array.AsReadOnly((T[])(values ?? Array.Empty<T>()).Clone());
    public bool TryFact(string id, out AIWorldFact fact)
    { if (id != null) return factIndex.TryGetValue(id, out fact); fact = default; return false; }
    public AITruthValue Fact(string id) => TryFact(id, out var fact) ? fact.Truth : AITruthValue.Unknown;
    public bool IsCurrent(AIBoardState board) => board != null && board.Generation == BoardGeneration && board.ActorTeam == ActorTeam;
}
