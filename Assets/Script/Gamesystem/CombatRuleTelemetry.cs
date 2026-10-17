using System;
using UnityEngine;

/// <summary>Opt-in bounded diagnostics. Disabled play allocates no events or log strings.</summary>
public static class CombatRuleTelemetry
{
    public const int Capacity = 512;
    public static bool Enabled { get; set; }
    public struct Entry
    {
        public string Event, SourceEntity, TargetEntity, Cause;
        public int Turn, RequestedDamage, ActualHpLoss;
        public UnitSpecialSkill Ability;
        public Vector3 Position;
        public float CandidateValue;
        public bool KnownToAI, Chosen;
    }
    static readonly Entry[] entries = new Entry[Capacity];
    static int next, count;
    public static int Count => count;
    public static Entry GetOldest(int index)
    {
        if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
        return entries[(next - count + index + Capacity) % Capacity];
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Reset() { Array.Clear(entries, 0, entries.Length); next = count = 0; Enabled = false; }
    public static void Record(string eventName, Status source = null, Status target = null,
        string cause = null, int requestedDamage = 0, int actualHpLoss = 0,
        float candidateValue = 0, bool knownToAI = true, bool chosen = false)
    {
        if (!Enabled) return;
        entries[next] = new Entry { Event = eventName, SourceEntity = source?.ReflectionLifeId,
            TargetEntity = target?.ReflectionLifeId, Cause = cause,
            Turn = (source ?? target)?.RuntimeSystems?.TurnGenerator?.Context?.Turn ?? 0,
            Ability = SpecialSkillRuntime.Definition(source)?.ability ?? UnitSpecialSkill.None,
            Position = target != null ? target.transform.position : source != null ? source.transform.position : default,
            RequestedDamage = requestedDamage, ActualHpLoss = actualHpLoss,
            CandidateValue = candidateValue, KnownToAI = knownToAI, Chosen = chosen };
        next = (next + 1) % Capacity; count = Math.Min(Capacity, count + 1);
    }
}
