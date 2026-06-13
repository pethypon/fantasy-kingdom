using System;
using System.Collections.Generic;
using UnityEngine;

public class MoveGenerator : MonoBehaviour
{
    public MapCreate mapcreate;
    public TurnGenerator turnGenerator;

    // 壁占有の再収集用（BuildSystem.Init から注入される）
    [NonSerialized] public BuildSystem BuildSystemRef;

    [Header("移動位置表示のオブジェクト")]
    public GameObject MovePoint;

    [Header("ムーブ親オブジェクト")]
    public Transform Move;

    [Header("クリスタルシステム")]
    [SerializeField] CrystalSystem crystalsystem;

    [Header("ユニットセッティング")]
    [SerializeField] UnitSetting unitsetting;

    [Header("ユニットボックス")]
    [SerializeField] Transform PlayerUnit;
    [SerializeField] Transform EnemyUnit;

    // ---- 内部状態（カプセル化） ----
    private readonly HashSet<Vector3> _unitPoints = new HashSet<Vector3>();
    private readonly List<Vector3> _movePositions = new List<Vector3>();
    private List<Vector3> _setpos;
    private Vector3 _objp;
    private Status _obj;
    private Vector3 _playerCrystalPos;
    private Vector3 _enemyCrystalPos;

    // ---- 読み取り専用プロパティ ----
    /// <summary>プレイヤークリスタル座標</summary>
    public Vector3 PlayerCrystalPos => _playerCrystalPos;
    /// <summary>敵クリスタル座標</summary>
    public Vector3 EnemyCrystalPos => _enemyCrystalPos;
    /// <summary>移動可能位置の読み取り専用ビュー</summary>
    public IReadOnlyList<Vector3> MovePositions => _movePositions;

    // ---- UnitPointData の操作メソッド ----
    /// <summary>指定セル座標が占有されているか</summary>
    public bool IsOccupied(Vector3 cellPos) => _unitPoints.Contains(cellPos);
    /// <summary>占有セルを追加する</summary>
    public void AddOccupied(Vector3 cellPos) => _unitPoints.Add(cellPos);
    /// <summary>占有セルを除去する</summary>
    public bool RemoveOccupied(Vector3 cellPos) => _unitPoints.Remove(cellPos);
    /// <summary>条件に合致する占有セルを除去する</summary>
    public int RemoveOccupiedWhere(System.Predicate<Vector3> predicate) => _unitPoints.RemoveWhere(predicate);

    // ---- グリッド座標への丸め ----
    public Vector3 Cell(Vector3 v)
    {
        return GameConstants.ToCell(v);
    }

    // ---- ユニット占有座標の更新 ----
    public void UnitPointCore()
    {
        _unitPoints.Clear();
        _playerCrystalPos = crystalsystem.PCP;
        _enemyCrystalPos = crystalsystem.ECP;
        _unitPoints.Add(Cell(_playerCrystalPos));
        _unitPoints.Add(Cell(_enemyCrystalPos));

        CollectUnitPositions(PlayerUnit);
        CollectUnitPositions(EnemyUnit);

        // 壁は AddOccupied で個別追加されるだけだと再計算時に消えてしまうため、
        // 建築物親からも毎回収集して通過不可を維持する
        var buildSystem = BuildSystemRef != null ? BuildSystemRef
            : (turnGenerator != null ? turnGenerator.Systems.BuildSystem : null);
        if (buildSystem != null)
        {
            CollectWallPositions(buildSystem.PlayerBuildingParent);
            CollectWallPositions(buildSystem.EnemyBuildingParent);
        }
    }

    private void CollectUnitPositions(Transform parent)
    {
        if (parent == null) return;
        foreach (Transform child in parent)
        {
            if (child == null) continue;
            Status us = child.GetComponentInChildren<Status>();
            if (us == null || us.type != Type.Unit) continue;
            _unitPoints.Add(Cell(us.transform.position));
        }
    }

    private void CollectWallPositions(Transform buildingParent)
    {
        if (buildingParent == null) return;
        foreach (Transform child in buildingParent)
        {
            if (child == null || !child.gameObject.activeSelf) continue;
            Status st = child.GetComponent<Status>();
            if (st == null || st.type != Type.Wall || st.HP <= 0) continue;
            _unitPoints.Add(Cell(st.transform.position));
        }
    }

    // ---- 移動範囲の計算とオブジェクト生成 ----
    public void MoveCore(Status Obj, Vector3 ObjP)
    {
        _setpos = mapcreate.SetPos;
        _movePositions.Clear();
        _obj = Obj;
        _objp = ObjP;

        if (!MovePatterns.Map.TryGetValue(_obj.kind, out Func<float, float, bool> predicate))
        {
            Debug.LogWarning($"[MoveGenerator] Kind '{_obj.kind}' の移動パターンが未定義です");
            return;
        }

        // LINQ排除: for ループで直接フィルタリング
        bool dirIndependent = MovePatterns.DirectionIndependent.Contains(_obj.kind);
        int dirZ = MovePatterns.DirZ(_obj.direction);

        for (int i = 0, count = _setpos.Count; i < count; i++)
        {
            Vector3 p = _setpos[i];
            float dx = p.x - _objp.x;
            float dz = p.z - _objp.z;

            // 方向依存の駒は dz を反転して判定
            float checkDz = dirIndependent ? dz : dz * dirZ;

            if (!predicate(dx, checkDz)) continue;
            if (_unitPoints.Contains(Cell(p))) continue;

            _movePositions.Add(p);
        }

        MoveCreate();
    }

    // ---- 移動ポイントオブジェクトの生成（ObjectPool使用） ----
    public void MoveCreate()
    {
        var pool = ObjectPool.Instance;
        for (int i = 0; i < _movePositions.Count; i++)
        {
            Vector3 pos = _movePositions[i];
            pos.y -= GameConstants.MovePointYOffset;

            if (pool != null)
                pool.Get(MovePoint, pos, Quaternion.identity, Move);
            else
                Instantiate(MovePoint, pos, Quaternion.identity, Move);
        }
    }

    // ---- 移動ポイントオブジェクトの削除（ObjectPool使用） ----
    public void MoveReset()
    {
        var pool = ObjectPool.Instance;
        if (pool != null)
        {
            pool.ReturnAllChildren(Move);
        }
        else
        {
            foreach (Transform child in Move.transform)
            {
                Destroy(child.gameObject);
            }
        }
    }

    // ---- UnitPointData の更新 ----
    public void MoveUpdate(Vector3 OldCell, Vector3 NewCell)
    {
        _unitPoints.Add(Cell(NewCell));
        _unitPoints.Remove(Cell(OldCell));
    }
}
