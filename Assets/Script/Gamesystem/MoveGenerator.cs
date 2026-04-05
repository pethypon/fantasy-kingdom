using System;
using System.Collections.Generic;
using UnityEngine;

public class MoveGenerator : MonoBehaviour
{
    public MapCreate mapcreate;
    public TurnGenerator turnGenerator;

    [Header("移動位置表示のオブジェクト")]
    public GameObject MovePoint;

    [Header("ムーブ親オブジェクト")]
    public Transform Move;

    [Header("ユニット座標")]
    public HashSet<Vector3> UnitPointData = new HashSet<Vector3>();

    [Header("クリスタルシステム")]
    [SerializeField] CrystalSystem crystalsystem;

    [Header("ユニットセッティング")]
    [SerializeField] UnitSetting unitsetting;

    [Header("ユニットボックス")]
    [SerializeField] Transform PlayerUnit;
    [SerializeField] Transform EnemyUnit;

    public List<Vector3> setpos;
    public List<Vector3> MoveUnitP;
    public Vector3 objp;
    private Status obj;
    public Vector3 pcp;
    public Vector3 ecp;
    public Vector3 usp;

    // ---- グリッド座標への丸め ----
    public Vector3 Cell(Vector3 v)
    {
        return GameConstants.ToCell(v);
    }

    // ---- ユニット占有座標の更新 ----
    public void UnitPointCore()
    {
        UnitPointData.Clear();
        pcp = crystalsystem.PCP;
        ecp = crystalsystem.ECP;
        UnitPointData.Add(Cell(pcp));
        UnitPointData.Add(Cell(ecp));

        CollectUnitPositions(PlayerUnit);
        CollectUnitPositions(EnemyUnit);
    }

    private void CollectUnitPositions(Transform parent)
    {
        if (parent == null) return;
        foreach (Transform child in parent)
        {
            if (child == null) continue;
            Status us = child.GetComponentInChildren<Status>();
            if (us == null || us.type != Type.Unit) continue;
            UnitPointData.Add(Cell(us.transform.position));
        }
    }

    // ---- 移動範囲の計算とオブジェクト生成 ----
    public void MoveCore(Status Obj, Vector3 ObjP)
    {
        setpos = mapcreate.SetPos;
        MoveUnitP.Clear();
        obj = Obj;
        objp = ObjP;

        if (!MovePatterns.Map.TryGetValue(obj.kind, out Func<float, float, bool> predicate))
        {
            Debug.LogWarning($"[MoveGenerator] Kind '{obj.kind}' の移動パターンが未定義です");
            return;
        }

        // LINQ排除: for ループで直接フィルタリング
        bool dirIndependent = MovePatterns.DirectionIndependent.Contains(obj.kind);
        int dirZ = MovePatterns.DirZ(obj.direction);

        for (int i = 0, count = setpos.Count; i < count; i++)
        {
            Vector3 p = setpos[i];
            float dx = p.x - objp.x;
            float dz = p.z - objp.z;

            // 方向依存の駒は dz を反転して判定
            float checkDz = dirIndependent ? dz : dz * dirZ;

            if (!predicate(dx, checkDz)) continue;
            if (UnitPointData.Contains(Cell(p))) continue;

            MoveUnitP.Add(p);
        }

        MoveCreate();
    }

    // ---- 移動ポイントオブジェクトの生成（ObjectPool使用） ----
    public void MoveCreate()
    {
        var pool = ObjectPool.Instance;
        for (int i = 0; i < MoveUnitP.Count; i++)
        {
            Vector3 pos = MoveUnitP[i];
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
        UnitPointData.Add(Cell(NewCell));
        UnitPointData.Remove(Cell(OldCell));
    }
}
