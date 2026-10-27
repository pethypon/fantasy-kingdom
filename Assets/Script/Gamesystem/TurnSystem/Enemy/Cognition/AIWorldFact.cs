using System;
using UnityEngine;

/// <summary>Missing evidence is distinct from a condition that was observed to be false.</summary>
public enum AITruthValue { Unknown = 0, False = 1, True = 2 }
public enum AIWorldFactSource { Unknown, CurrentObservation, PastObservation, Belief }

public readonly struct AIWorldFact
{
    public readonly string Id, TargetLifeId, ExpirationPolicy;
    public readonly AIWorldFactSource Source;
    public readonly int ObservedTurn;
    public readonly float Confidence;
    public readonly AITruthValue Truth;
    public readonly Vector3 Position;
    public readonly bool HasPosition;
    public readonly float Value;

    public AIWorldFact(string id, AITruthValue truth, AIWorldFactSource source, int observedTurn,
        float confidence = 1, string targetLifeId = null, Vector3 position = default,
        bool hasPosition = false, float value = 0, string expirationPolicy = "generation")
    {
        Id = id ?? string.Empty; TargetLifeId = targetLifeId ?? string.Empty;
        bool finite = Finite(confidence) && Finite(value) && (!hasPosition || Finite(position.x) && Finite(position.y) && Finite(position.z));
        Truth = finite && Enum.IsDefined(typeof(AITruthValue), truth) ? truth : AITruthValue.Unknown;
        Source = finite && Enum.IsDefined(typeof(AIWorldFactSource), source) ? source : AIWorldFactSource.Unknown;
        ObservedTurn = observedTurn; Confidence = finite ? Mathf.Clamp01(confidence) : 0;
        Position = finite ? position : default; HasPosition = finite && hasPosition;
        Value = finite ? value : 0; ExpirationPolicy = expirationPolicy ?? "generation";
    }

    public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    public static AITruthValue And(AITruthValue left, AITruthValue right)
        => left == AITruthValue.False || right == AITruthValue.False ? AITruthValue.False
            : left == AITruthValue.True && right == AITruthValue.True ? AITruthValue.True : AITruthValue.Unknown;
    public static AITruthValue Or(AITruthValue left, AITruthValue right)
        => left == AITruthValue.True || right == AITruthValue.True ? AITruthValue.True
            : left == AITruthValue.False && right == AITruthValue.False ? AITruthValue.False : AITruthValue.Unknown;
}
