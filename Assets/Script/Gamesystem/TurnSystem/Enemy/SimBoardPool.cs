using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  SimBoardPool — SimBoardState / SimUnit のオブジェクトプール
//  探索ノードごとの GC 負荷を削減する
// =====================================================================
public static class SimBoardPool
{
    static readonly Stack<SimBoardState> _boardPool = new Stack<SimBoardState>(64);
    static readonly Stack<SimUnit> _unitPool = new Stack<SimUnit>(256);
    static readonly Stack<List<SimUnit>> _unitListPool = new Stack<List<SimUnit>>(64);
    static readonly Stack<Dictionary<FacilityKind, int>> _dictPool = new Stack<Dictionary<FacilityKind, int>>(128);
    static readonly Stack<HashSet<Vector3Int>> _hashSetPool = new Stack<HashSet<Vector3Int>>(64);

    public static SimBoardState RentBoard()
    {
        return _boardPool.Count > 0 ? _boardPool.Pop() : new SimBoardState();
    }

    public static void ReturnBoard(SimBoardState board)
    {
        if (board == null) return;
        // ユニットを個別にプールに返却
        if (board.Units != null)
        {
            for (int i = 0; i < board.Units.Count; i++)
                ReturnUnit(board.Units[i]);
            ReturnUnitList(board.Units);
            board.Units = null;
        }
        if (board.EnemyBuildingCounts != null) { ReturnDict(board.EnemyBuildingCounts); board.EnemyBuildingCounts = null; }
        if (board.PlayerBuildingCounts != null) { ReturnDict(board.PlayerBuildingCounts); board.PlayerBuildingCounts = null; }
        board.ReturnOccupiedSet();
        _boardPool.Push(board);
    }

    public static SimUnit RentUnit()
    {
        if (_unitPool.Count > 0)
        {
            var u = _unitPool.Pop();
            u.Effects.Clear();
            return u;
        }
        return new SimUnit();
    }

    public static void ReturnUnit(SimUnit unit)
    {
        if (unit == null) return;
        _unitPool.Push(unit);
    }

    public static List<SimUnit> RentUnitList(int capacity)
    {
        if (_unitListPool.Count > 0)
        {
            var list = _unitListPool.Pop();
            list.Clear();
            return list;
        }
        return new List<SimUnit>(capacity);
    }

    public static void ReturnUnitList(List<SimUnit> list)
    {
        if (list == null) return;
        list.Clear();
        _unitListPool.Push(list);
    }

    public static Dictionary<FacilityKind, int> RentDict()
    {
        if (_dictPool.Count > 0)
        {
            var d = _dictPool.Pop();
            d.Clear();
            return d;
        }
        return new Dictionary<FacilityKind, int>();
    }

    public static void ReturnDict(Dictionary<FacilityKind, int> dict)
    {
        if (dict == null) return;
        dict.Clear();
        _dictPool.Push(dict);
    }

    public static HashSet<Vector3Int> RentHashSet()
    {
        if (_hashSetPool.Count > 0)
        {
            var s = _hashSetPool.Pop();
            s.Clear();
            return s;
        }
        return new HashSet<Vector3Int>();
    }

    public static void ReturnHashSet(HashSet<Vector3Int> set)
    {
        if (set == null) return;
        set.Clear();
        _hashSetPool.Push(set);
    }
}
