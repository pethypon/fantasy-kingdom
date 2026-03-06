using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class MoveGererater : MonoBehaviour
{
    [SerializeField] public MapCreate mapcreate;
    [SerializeField] public TurnGenerater turngenerater;

    [Header("移動位置表示のオブジェクト")]
    [SerializeField] public GameObject MovePoint;

    [Header("ムーブ親オブジェクト")]
    [SerializeField] public Transform Move;

    [Header("ユニット座標")]
    [SerializeField] public HashSet<Vector3> UnitPointData = new HashSet<Vector3>();

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

    // ---- 駒ごとの移動判定 ----
    // dx = p.x - objp.x（符号付き）, dz = p.z - objp.z（符号付き）
    // 新しく追加する場合はここに1行追加するだけでよい
    static readonly Dictionary<Kind, Func<float, float, bool>> MovePredicateMap =
        new Dictionary<Kind, Func<float, float, bool>>
    {
        // 全方向1マス
        { Kind.King,        (dx, dz) => Mathf.Abs(dx) <= 1
                                     && Mathf.Abs(dz) <= 1 },

        // 上下左右1マス
        { Kind.Knight,      (dx, dz) => Mathf.Abs(dx) + Mathf.Abs(dz) == 1 },

        // 斜め1マス
        { Kind.Archer,      (dx, dz) => Mathf.Abs(dx) == 1
                                     && Mathf.Abs(dz) == 1 },

        // 斜め1マス（Archerと同じ移動）
        { Kind.Magic,       (dx, dz) => Mathf.Abs(dx) == 1
                                     && Mathf.Abs(dz) == 1 },

        // ナイト跳び（2×1 or 1×2）
        { Kind.Assassin,    (dx, dz) => (Mathf.Abs(dx) == 2 && Mathf.Abs(dz) == 1)
                                     || (Mathf.Abs(dx) == 1 && Mathf.Abs(dz) == 2) },

        // 左右1＋前後1 or 直進2
        { Kind.Scout,       (dx, dz) => (Mathf.Abs(dx) == 1 && Mathf.Abs(dz) <= 1)
                                     || (dx == 0 && Mathf.Abs(dz) == 2) },

        // 前斜め1 or 後方直進1（符号注意：方向付き）
        { Kind.Priest,      (dx, dz) => (Mathf.Abs(dx) == 1 && dz == 1)
                                     || (dx == 0 && dz == -1) },

        // 左右2マス or 前後1マス
        { Kind.Guardian,    (dx, dz) => (Mathf.Abs(dx) <= 2 && dz == 0)
                                     || (dx == 0 && Mathf.Abs(dz) == 1) },

        // 前直進1-3マス or 斜め後ろ1マス（方向付き）
        { Kind.Crossbow,    (dx, dz) => (dx == 0 && (dz == 1 || dz == 2 || dz == 3))
                                     || (Mathf.Abs(dx) == 1 && dz == -1) },

        // 右前斜め1 or 左右各3マス（方向付き）
        { Kind.Magicsniper, (dx, dz) => (dx == 1 && dz == 1)
                                     || (Mathf.Abs(dx) == 3 && dz == -1) },

        // 斜め前後2パターン（方向付き）
        { Kind.Bomber,      (dx, dz) => (dx == -1 && dz ==  1) || (dx ==  2 && dz ==  2)
                                     || (dx ==  1 && dz == -1) || (dx == -2 && dz == -2) },
    };

    // ---- ユニット占有座標の更新 ----
    public void UnitPointCore()
    {
        UnitPointData.Clear();
        pcp = crystalsystem.PCP;
        ecp = crystalsystem.ECP;
        UnitPointData.Add(Cell(pcp));
        UnitPointData.Add(Cell(ecp));

        foreach (Status us in PlayerUnit.GetComponentsInChildren<Status>())
        {
            if (us.type != Type.Unit) continue;
            usp = us.transform.position;
            UnitPointData.Add(Cell(usp));
        }
        foreach (Status us in EnemyUnit.GetComponentsInChildren<Status>())
        {
            if (us.type != Type.Unit) continue;
            usp = us.transform.position;
            UnitPointData.Add(Cell(usp));
        }
    }

    // ---- グリッド座標への丸め ----
    public Vector3 Cell(Vector3 v)
    {
        return new Vector3(Mathf.RoundToInt(v.x), 0f, Mathf.RoundToInt(v.z));
    }

    // ---- 移動範囲の計算とオブジェクト生成 ----
    public void MoveCore(Status Obj, Vector3 ObjP)
    {
        setpos = mapcreate.SetPos;
        MoveUnitP.Clear();
        obj = Obj;
        objp = ObjP;

        if (!MovePredicateMap.TryGetValue(obj.kind, out Func<float, float, bool> predicate))
        {
            Debug.LogWarning($"[MoveGererater] Kind '{obj.kind}' の移動パターンが未定義です");
            return;
        }

        Debug.Log($"<color=#00ff00ff>[Controller]</color>{obj.kind}");

        MoveUnitP = setpos.Where(p =>
        {
            float dx = p.x - objp.x;
            float dz = p.z - objp.z;
            bool occupied = UnitPointData.Contains(Cell(p));
            return predicate(dx, dz) && !occupied;
        }).ToList();

        MoveCreate();
    }

    // ---- 移動ポイントオブジェクトの生成 ----
    public void MoveCreate()
    {
        for (int i = 0; i < MoveUnitP.Count; i++)
        {
            Vector3 pos = MoveUnitP[i];
            pos.y -= 0.47f;
            Instantiate(MovePoint, pos, Quaternion.identity, Move);
            Debug.Log("<color=#00ff00ff>[Controller]</color>MovePoint");
        }
    }

    // ---- 移動ポイントオブジェクトの削除 ----
    public void MoveReset()
    {
        foreach (Transform child in Move.transform)
        {
            Destroy(child.gameObject);
        }
    }

    // ---- UnitPointData の更新 ----
    public void MoveUpdate(Vector3 OldCell, Vector3 NewCell)
    {
        UnitPointData.Add(Cell(NewCell));
        UnitPointData.Remove(Cell(OldCell));
    }
}
