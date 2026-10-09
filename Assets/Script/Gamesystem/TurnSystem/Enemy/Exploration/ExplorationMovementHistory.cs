using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One sample per turn; movement actions in the same turn replace the current sample.</summary>
public sealed class ExplorationMovementHistory
{
    readonly ExplorationHistorySample[] samples;
    int first;
    int count;
    readonly HashSet<Vector3Int> unique = new HashSet<Vector3Int>();
    public int Count => count;
    public int NewlyRevealedInWindow { get; private set; }
    public int UniqueCellsInWindow { get; private set; }
    public float DistanceProgressInWindow { get; private set; }
    public bool RepeatedCellInWindow { get; private set; }

    public ExplorationMovementHistory(int capacity) { samples = new ExplorationHistorySample[Mathf.Clamp(capacity, 2, 64)]; }
    public void Add(Vector3Int position, int turn, int newlyRevealed, float distance)
    {
        if (count > 0)
        {
            int last = (first + count - 1) % samples.Length;
            if (samples[last].Turn == turn)
            {
                samples[last].Cell = position;
                samples[last].NewlyRevealed += Mathf.Max(0, newlyRevealed);
                samples[last].Distance = distance;
                Recalculate(); return;
            }
        }
        while (count > 0 && turn - samples[first].Turn >= samples.Length) { first = (first + 1) % samples.Length; count--; }
        int index = (first + count) % samples.Length;
        if (count == samples.Length) { first = (first + 1) % samples.Length; }
        else count++;
        samples[index] = new ExplorationHistorySample { Cell = position, Turn = turn, NewlyRevealed = Mathf.Max(0, newlyRevealed), Distance = distance };
        Recalculate();
    }
    public void Clear() { first = count = 0; Recalculate(); }
    /// <summary>Changing an information basis is not progress; retain position/reveal evidence for loops.</summary>
    public void RebaseDistances(float distance)
    {
        for (int i = 0; i < count; i++) samples[(first + i) % samples.Length].Distance = distance;
        Recalculate();
    }
    public List<ExplorationHistorySample> Capture()
    {
        var result = new List<ExplorationHistorySample>(count);
        for (int i = 0; i < count; i++) result.Add(samples[(first + i) % samples.Length]);
        return result;
    }
    public void Restore(List<ExplorationHistorySample> state)
    {
        Clear(); if (state == null) return;
        for (int i = Mathf.Max(0, state.Count - samples.Length); i < state.Count; i++)
            Add(state[i].Cell, state[i].Turn, state[i].NewlyRevealed, state[i].Distance);
    }
    public float DistanceFromRecentArea(Vector3Int cell)
    {
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++) nearest = Mathf.Min(nearest, Distance(samples[(first + i) % samples.Length].Cell, cell));
        return float.IsInfinity(nearest) ? 0 : nearest;
    }
    public float DirectionNovelty(Vector3Int origin, Vector3Int target)
    {
        if (count < 2) return 1;
        var wanted = (Vector3)(target - origin); wanted.y = 0;
        var heading = (Vector3)(samples[(first + count - 1) % samples.Length].Cell - samples[first].Cell); heading.y = 0;
        return heading.sqrMagnitude < .01f ? 1f : Mathf.Clamp01((1f - Vector3.Dot(wanted.normalized, heading.normalized)) * .5f);
    }
    void Recalculate()
    {
        unique.Clear(); NewlyRevealedInWindow = 0;
        for (int i = 0; i < count; i++)
        {
            var sample = samples[(first + i) % samples.Length];
            unique.Add(sample.Cell); NewlyRevealedInWindow += sample.NewlyRevealed;
        }
        UniqueCellsInWindow = unique.Count;
        RepeatedCellInWindow = count > unique.Count;
        DistanceProgressInWindow = count > 1 ? samples[first].Distance - samples[(first + count - 1) % samples.Length].Distance : 0;
    }
    static float Distance(Vector3Int a, Vector3Int b) => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.z - b.z));
}
