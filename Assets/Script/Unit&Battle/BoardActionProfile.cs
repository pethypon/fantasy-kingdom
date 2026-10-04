using System;
using System.Collections.Generic;
using UnityEngine;

public enum ActorActionType { All, Combat, Support, Stationary, Passive }

/// <summary>Serialized 8×8 offset mask. Cached offsets avoid allocations in gameplay queries.</summary>
[Serializable]
public sealed class BoardTilePattern
{
    public const int Size = 8;
    public bool useCustom;
    public long cells;
    [Range(0, 7)] public int originX = 3, originZ = 3;
    public bool directionIndependent;
    [NonSerialized] Vector2Int[] cached;
    [NonSerialized] long cachedCells;
    [NonSerialized] int cachedX, cachedZ;

    public bool IsCellSet(int x, int z)
        => x >= 0 && x < Size && z >= 0 && z < Size && (unchecked((ulong)cells) & (1UL << (z * Size + x))) != 0;
    public void SetCell(int x, int z, bool enabled)
    {
        if (x < 0 || x >= Size || z < 0 || z >= Size) return;
        ulong bit = 1UL << (z * Size + x), value = unchecked((ulong)cells);
        cells = unchecked((long)(enabled ? value | bit : value & ~bit));
        cached = null;
    }
    public bool Contains(int dx, int dz, Direction direction)
        => IsCellSet(dx + Mathf.Clamp(originX, 0, 7),
            (directionIndependent || direction != Direction.S ? dz : -dz) + Mathf.Clamp(originZ, 0, 7));
    public IReadOnlyList<Vector2Int> Offsets
    {
        get
        {
            int x0 = Mathf.Clamp(originX, 0, 7), z0 = Mathf.Clamp(originZ, 0, 7);
            if (cached != null && cachedCells == cells && cachedX == x0 && cachedZ == z0) return cached;
            var result = new List<Vector2Int>(64);
            for (int z = 0; z < Size; z++) for (int x = 0; x < Size; x++)
                if (IsCellSet(x, z)) result.Add(new Vector2Int(x - x0, z - z0));
            cached = result.ToArray(); cachedCells = cells; cachedX = x0; cachedZ = z0;
            return cached;
        }
    }
}

[CreateAssetMenu(menuName = "Fantasy Kingdom/駒と建物/マス行動設定")]
public sealed class BoardActionProfile : ScriptableObject
{
    public string displayName = "新しい行動タイプ";
    public ActorActionType actionType;
    public BoardTilePattern movement = new BoardTilePattern();
    public BoardTilePattern attack = new BoardTilePattern();
    public BoardTilePattern vision = new BoardTilePattern();
    public bool CanMove => actionType != ActorActionType.Stationary && actionType != ActorActionType.Passive;
    public bool CanAttack => actionType != ActorActionType.Support && actionType != ActorActionType.Passive;
    public bool CanUseSkills => actionType != ActorActionType.Passive;
    public static BoardActionProfile For(Status actor)
        => actor == null ? null : actor.AuthoredFacility != null ? actor.AuthoredFacility.actionProfile : actor.GrowthData?.actionProfile;
}
