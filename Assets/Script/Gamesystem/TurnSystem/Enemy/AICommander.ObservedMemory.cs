using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Optional battle-local observation save. Contains no live objects, HP queries or Unity instance IDs.</summary>
[Serializable]
public sealed class AIObservedMemoryState
{
    public const int MaximumEntries = 128;
    public int Version = 1, SavedAtTurn;
    public Team Owner;
    public List<AIObservedMemoryEntry> Entries = new List<AIObservedMemoryEntry>();
}

[Serializable]
public sealed class AIObservedMemoryEntry
{
    public string LifeId;
    public bool IsMainCrystal, WasPrincipalCommander, HasPrevious;
    public Team ObservedTeam;
    public Kind Kind;
    public Type Type;
    public Direction Direction;
    public Vector3Int Position, PreviousPosition;
    public int Turn, PreviousTurn, ObservedAttackPower;
    public AIObservedActionProfile ActionProfile;
}

/// <summary>Immutable-by-convention value copy captured while an actor is legally observed.</summary>
[Serializable]
public sealed class AIObservedActionProfile
{
    public ActorActionType ActionType;
    public AIObservedTilePattern Movement, Attack, Vision;

    public static AIObservedActionProfile Capture(BoardActionProfile profile, AIObservedActionProfile previous = null)
    {
        if (profile == null) return null;
        if (previous != null && previous.ActionType == profile.actionType
            && AIObservedTilePattern.Matches(previous.Movement, profile.movement)
            && AIObservedTilePattern.Matches(previous.Attack, profile.attack)
            && AIObservedTilePattern.Matches(previous.Vision, profile.vision)) return previous;
        return new AIObservedActionProfile { ActionType = profile.actionType,
            Movement = AIObservedTilePattern.Capture(profile.movement), Attack = AIObservedTilePattern.Capture(profile.attack),
            Vision = AIObservedTilePattern.Capture(profile.vision) };
    }
    public AIObservedActionProfile Copy() => new AIObservedActionProfile { ActionType = ActionType,
        Movement = Movement?.Copy(), Attack = Attack?.Copy(), Vision = Vision?.Copy() };
    public bool IsValid => Enum.IsDefined(typeof(ActorActionType), ActionType)
        && (Movement == null || Movement.IsValid) && (Attack == null || Attack.IsValid) && (Vision == null || Vision.IsValid);
    public BoardActionProfile CreateRuntimeProfile()
    {
        var profile = ScriptableObject.CreateInstance<BoardActionProfile>();
        profile.hideFlags = HideFlags.HideAndDontSave; profile.name = "AI restored observed action profile";
        profile.actionType = ActionType; profile.movement = Movement?.CreatePattern();
        profile.attack = Attack?.CreatePattern(); profile.vision = Vision?.CreatePattern();
        return profile;
    }
}

[Serializable]
public sealed class AIObservedTilePattern
{
    public bool UseCustom, DirectionIndependent;
    public long Cells;
    public int OriginX, OriginZ;
    public static AIObservedTilePattern Capture(BoardTilePattern pattern) => pattern == null ? null
        : new AIObservedTilePattern { UseCustom = pattern.useCustom, DirectionIndependent = pattern.directionIndependent,
            Cells = pattern.cells, OriginX = pattern.originX, OriginZ = pattern.originZ };
    public static bool Matches(AIObservedTilePattern snapshot, BoardTilePattern pattern)
        => pattern == null ? snapshot == null : snapshot != null && snapshot.UseCustom == pattern.useCustom
            && snapshot.DirectionIndependent == pattern.directionIndependent && snapshot.Cells == pattern.cells
            && snapshot.OriginX == pattern.originX && snapshot.OriginZ == pattern.originZ;
    public AIObservedTilePattern Copy() => (AIObservedTilePattern)MemberwiseClone();
    public bool IsValid => OriginX >= 0 && OriginX < BoardTilePattern.Size && OriginZ >= 0 && OriginZ < BoardTilePattern.Size;
    public BoardTilePattern CreatePattern() => new BoardTilePattern { useCustom = UseCustom, cells = Cells,
        originX = OriginX, originZ = OriginZ, directionIndependent = DirectionIndependent };
}

public partial class AICommander
{
    // Ownership: this commander; created only on load; at most 128. Never persisted as Unity objects.
    readonly List<BoardActionProfile> restoredObservationProfiles = new List<BoardActionProfile>(AIObservedMemoryState.MaximumEntries);
    readonly List<KeyValuePair<int, AIBoardState.LastKnownInfo>> observationSaveBuffer = new List<KeyValuePair<int, AIBoardState.LastKnownInfo>>(AIObservedMemoryState.MaximumEntries);
    public int RestoredObservationProfileCount => restoredObservationProfiles.Count;

    public AIObservedMemoryState CaptureObservedMemory()
    {
        var state = new AIObservedMemoryState { Owner = _actorTeam, SavedAtTurn = Math.Max(0, _turnCount) };
        observationSaveBuffer.Clear();
        try
        {
            foreach (var pair in _sharedMemory)
            {
                var memory = pair.Value; bool mainCrystal = pair.Key == int.MinValue;
                if (!ValidObservation(memory, mainCrystal, state.SavedAtTurn)) continue;
                int at = 0;
                while (at < observationSaveBuffer.Count && CompareObservations(observationSaveBuffer[at], pair) <= 0) at++;
                if (at >= AIObservedMemoryState.MaximumEntries) continue;
                observationSaveBuffer.Insert(at, pair);
                if (observationSaveBuffer.Count > AIObservedMemoryState.MaximumEntries) observationSaveBuffer.RemoveAt(AIObservedMemoryState.MaximumEntries);
            }
            foreach (var pair in observationSaveBuffer)
            {
                var memory = pair.Value;
                state.Entries.Add(new AIObservedMemoryEntry { LifeId = memory.LifeId, IsMainCrystal = pair.Key == int.MinValue,
                    ObservedTeam = memory.ObservedTeam, Kind = memory.Kind, Type = memory.Type, Direction = memory.Direction,
                    Position = memory.Position, Turn = memory.Turn, ObservedAttackPower = memory.ObservedAttackPower,
                    WasPrincipalCommander = memory.WasPrincipalCommander, PreviousPosition = memory.PreviousPosition,
                    PreviousTurn = memory.PreviousTurn, HasPrevious = memory.HasPrevious,
                    ActionProfile = memory.ActionProfileSnapshot?.Copy() });
            }
            return state;
        }
        finally { observationSaveBuffer.Clear(); }
    }

    public void RestoreObservedMemory(AIObservedMemoryState saved)
    {
        ReleaseObservedMemoryProfiles(); _sharedMemory.Clear();
        if (saved == null || saved.Version != 1 || saved.Owner != _actorTeam || saved.SavedAtTurn < 0
            || saved.SavedAtTurn > _turnCount || saved.Entries == null) return;
        // Reject oversize payloads as one optional diagnostic section; never allocate from an unbounded count.
        if (saved.Entries.Count > AIObservedMemoryState.MaximumEntries) return;
        var identities = new HashSet<string>(StringComparer.Ordinal);
        int synthetic = int.MinValue + 1;
        bool crystalRestored = false;
        foreach (var entry in saved.Entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.LifeId) || entry.LifeId.Length > 128
                || !identities.Add(entry.LifeId) || entry.IsMainCrystal && crystalRestored) continue;
            var memory = new AIBoardState.LastKnownInfo { LifeId = entry.LifeId, ObservedTeam = entry.ObservedTeam,
                Position = entry.Position, Kind = entry.Kind, Type = entry.Type, Direction = entry.Direction,
                ObservedAttackPower = entry.ObservedAttackPower, Turn = entry.Turn, Valid = true,
                WasPrincipalCommander = entry.WasPrincipalCommander, HasPrevious = entry.HasPrevious,
                PreviousPosition = entry.PreviousPosition, PreviousTurn = entry.PreviousTurn,
                ActionProfileSnapshot = entry.ActionProfile?.Copy() };
            if (!ValidObservation(memory, entry.IsMainCrystal, _turnCount)) continue;
            if (memory.HasPrevious && (!InsideObservedPosition(memory.PreviousPosition) || memory.PreviousTurn < 0 || memory.PreviousTurn > memory.Turn))
            { memory.HasPrevious = false; memory.PreviousPosition = default; memory.PreviousTurn = 0; }
            if (memory.ActionProfileSnapshot != null)
            {
                memory.ActionProfile = memory.ActionProfileSnapshot.CreateRuntimeProfile();
                restoredObservationProfiles.Add(memory.ActionProfile);
            }
            if (entry.IsMainCrystal) { _sharedMemory[int.MinValue] = memory; crystalRestored = true; }
            else _sharedMemory[synthetic++] = memory;
        }
        // Existing transient board caches may still point at a previous loaded snapshot.
        // The next normal board construction will consume this memory and current legitimate visibility.
        _board = null;
    }

    bool ValidObservation(AIBoardState.LastKnownInfo memory, bool mainCrystal, int now)
    {
        if (!memory.Valid || string.IsNullOrWhiteSpace(memory.LifeId) || memory.LifeId.Length > 128
            || !Enum.IsDefined(typeof(Team), memory.ObservedTeam) || memory.ObservedTeam == _actorTeam
            || !Enum.IsDefined(typeof(Kind), memory.Kind) || !Enum.IsDefined(typeof(Type), memory.Type)
            || !Enum.IsDefined(typeof(Direction), memory.Direction) || memory.ObservedAttackPower < 0
            || memory.Turn < 0 || memory.Turn > now || !InsideObservedPosition(memory.Position)
            || memory.ActionProfileSnapshot != null && !memory.ActionProfileSnapshot.IsValid) return false;
        if (mainCrystal) return memory.Kind == Kind.Crystal && memory.ObservedTeam == (_actorTeam == Team.Player ? Team.Enemy : Team.Player);
        if ((long)now - memory.Turn >= 7) return false;
        return !memory.WasPrincipalCommander || memory.ObservedTeam == (_actorTeam == Team.Player ? Team.Enemy : Team.Player) && memory.Type == Type.Unit;
    }

    bool InsideObservedPosition(Vector3Int position)
    {
        int width = _mapCreate != null ? _mapCreate.maxX : SaveSystem.MaximumMapDimension;
        int depth = _mapCreate != null ? _mapCreate.maxZ : SaveSystem.MaximumMapDimension;
        int minimumY = _mapCreate != null ? _mapCreate.minY : -1024;
        int maximumY = _mapCreate != null ? _mapCreate.maxY : 1024;
        return position.x >= 0 && position.x < width && position.z >= 0 && position.z < depth
            && position.y >= minimumY && position.y <= maximumY;
    }

    static int CompareObservations(KeyValuePair<int, AIBoardState.LastKnownInfo> left, KeyValuePair<int, AIBoardState.LastKnownInfo> right)
    {
        int main = (right.Key == int.MinValue).CompareTo(left.Key == int.MinValue); if (main != 0) return main;
        int commander = right.Value.WasPrincipalCommander.CompareTo(left.Value.WasPrincipalCommander); if (commander != 0) return commander;
        int turn = right.Value.Turn.CompareTo(left.Value.Turn); return turn != 0 ? turn : string.CompareOrdinal(left.Value.LifeId, right.Value.LifeId);
    }

    /// <summary>Called on re-load and by the existing battle/scene release boundary. Does not erase observation values.</summary>
    public void ReleaseObservedMemoryProfiles()
    {
        if (restoredObservationProfiles.Count == 0) return;
        observationSaveBuffer.Clear();
        foreach (var pair in _sharedMemory)
            if (pair.Value.ActionProfile != null && restoredObservationProfiles.Contains(pair.Value.ActionProfile)) observationSaveBuffer.Add(pair);
        foreach (var pair in observationSaveBuffer)
        { var memory = pair.Value; memory.ActionProfile = null; _sharedMemory[pair.Key] = memory; }
        observationSaveBuffer.Clear();
        foreach (var profile in restoredObservationProfiles)
            if (profile != null) UnityEngine.Object.DestroyImmediate(profile); // Internal runtime SO only, never an asset.
        restoredObservationProfiles.Clear();
    }
}
