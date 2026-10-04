using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Serializable match state, never a global profile. Stable IDs and RNG survive loading.</summary>
[Serializable]
public sealed class ThirdFactionDirectorState
{
    public int Version = 1, CurrentIP, LastProcessedTurn = -1;
    public int LastAnyEventTurn = -10000, LastMajorEventTurn = -10000;
    public int RandomState = 19790317, NextActorId = 1;
    public ThirdFactionDirectorMode Mode;
    public ThirdFactionConflictPrediction Prediction = new ThirdFactionConflictPrediction();
    public List<ThirdFactionEventStamp> Events = new List<ThirdFactionEventStamp>();
    public List<ThirdFactionHistory> History = new List<ThirdFactionHistory>();
    public List<ThirdFactionObjective> Objectives = new List<ThirdFactionObjective>();
    public List<ThirdFactionActiveEvent> ActiveEvents = new List<ThirdFactionActiveEvent>();
    public FactionState.APData MonsterAP = new FactionState.APData();
    public FactionState.APData IntruderAP = new FactionState.APData();
    public int BattleCount, ObjectiveDamage, TerritoryChanges;
    public int PredictionStartedTurn = -1;
    public int StrongEnemyBaseRadius;
    public int NextRandom(int exclusiveMax)
    {
        if (exclusiveMax <= 0) return 0;
        uint x = unchecked((uint)(RandomState == 0 ? 19790317 : RandomState));
        x ^= x << 13; x ^= x >> 17; x ^= x << 5;
        RandomState = unchecked((int)x);
        return (int)(x % (uint)exclusiveMax);
    }
}
[Serializable] public sealed class ThirdFactionEventStamp
{
    public string Id;
    public ThirdFactionEventCategory Category;
    public int Turn, RepeatCount;
}
[Serializable] public sealed class ThirdFactionObjective
{
    public ThirdFactionObjectiveKind Kind;
    public Vector3Int TargetCell;
    public int ActorId, TargetId, Priority = 1, ExpireTurn;
}
[Serializable] public sealed class ThirdFactionActiveEvent
{
    public string EventId;
    public int StartedTurn, ExpireTurn;
    public List<int> Actors = new List<int>();
}
[Serializable] public sealed class ThirdFactionHistory
{
    public int Turn, Battles, ObjectiveDamage, Territory;
    public float PlayerPower, EnemyPower, PlayerEconomy, EnemyEconomy, ObjectiveHP;
}
[Serializable] public sealed class ThirdFactionConflictPrediction
{
    public float Probability;
    public int EstimatedTurns;
    public string Kind = "none";
    public Vector3Int Focus;
}

/// <summary>IP reservations are independent of FactionState AP. Dispose refunds any uncommitted spend.</summary>
public sealed class ThirdFactionIPSystem
{
    readonly ThirdFactionDirectorState state;
    bool reserved;
    public int Current => Mathf.Clamp(state.CurrentIP, 0, ThirdFactionDirectorConfig.MaxIP);
    public ThirdFactionIPSystem(ThirdFactionDirectorState value) { state = value; state.CurrentIP = Current; }
    public void BeginTurn() { if (reserved) throw new InvalidOperationException("Active IP reservation"); state.CurrentIP = Mathf.Min(50, Current + 5); }
    public Reservation TryReserve(int cost)
    {
        if (reserved || cost < 0 || cost > 50 || cost > Current) return null;
        reserved = true; state.CurrentIP = Current - cost;
        return new Reservation(this, cost);
    }
    public sealed class Reservation : IDisposable
    {
        ThirdFactionIPSystem owner;
        readonly int cost;
        bool committed;
        internal Reservation(ThirdFactionIPSystem value, int amount) { owner = value; cost = amount; }
        public void Commit() { if (owner == null) throw new ObjectDisposedException(nameof(Reservation)); committed = true; }
        public void Dispose()
        {
            if (owner == null) return;
            if (!committed) owner.state.CurrentIP = Mathf.Min(50, owner.Current + cost);
            owner.reserved = false; owner = null;
        }
    }
}
