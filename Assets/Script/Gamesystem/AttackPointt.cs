using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class AttackPointt : MonoBehaviour
{
    public PlayerMove.AttackMode attackmode;
    public List<Vector3> AttackP;
    public List<Vector3> setpos;

    [Header("ユニット座標")]
    [SerializeField] public HashSet<Vector3> unitdata;

    public Status obj;
    public Vector3 objp;
    public Vector3 attackpos;
    public RaycastHit targethit;

    [Header("マップクリエイト")]
    [SerializeField] public MapCreate mapcreate;

    [Header("プレイヤームーブ")]
    [SerializeField] public PlayerMove move;

    [Header("ムーブジェネレーター")]
    [SerializeField] public MoveGererater movegenerater;

    [Header("アタックポイント")]
    [SerializeField] public GameObject AttackPoint;

    [Header("アタックポイント親")]
    [SerializeField] public Transform APparent;

    // ─── 駒種ごとの攻撃範囲判定 ──────────────────────────────────────
    // dx = p.x - objp.x（符号付き）, dz = p.z - objp.z（符号付き）
    // Priest は未実装のためエントリなし（今後追加予定）
    // 新しい駒を追加する場合はここに1行追加するだけでよい
    static readonly Dictionary<Kind, Func<float, float, bool>> AttackPredicateMap =
        new Dictionary<Kind, Func<float, float, bool>>
    {
        // 前方3マス（横±1・直進）
        { Kind.King,        (dx, dz) => Mathf.Abs(dx) <= 1 && dz == 1 },

        // 前方3マス（Kingと同じ攻撃範囲）
        { Kind.Knight,      (dx, dz) => Mathf.Abs(dx) <= 1 && dz == 1 },

        // 前方直進2・3マス
        { Kind.Archer,      (dx, dz) => dx == 0 && (dz == 2 || dz == 3) },

        // 十字遠距離2マス
        { Kind.Magic,       (dx, dz) => (Mathf.Abs(dx) == 2 && dz == 0)
                                     || (dx == 0 && Mathf.Abs(dz) == 2) },

        // 前斜め±1マス
        { Kind.Assassin,    (dx, dz) => Mathf.Abs(dx) == 1 && dz == 1 },

        // 左右横1マス
        { Kind.Scout,       (dx, dz) => Mathf.Abs(dx) == 1 && dz == 0 },

        // 前直進1マス
        { Kind.Guardian,    (dx, dz) => dx == 0 && dz == 1 },

        // 前直進1・2マス
        { Kind.Crossbow,    (dx, dz) => dx == 0 && (dz == 1 || dz == 2) },

        // 左右横4マス
        { Kind.Magicsniper, (dx, dz) => Mathf.Abs(dx) == 4 && dz == 0 },

        // 前直進3マス
        { Kind.Bomber,      (dx, dz) => dx == 0 && dz == 3 },
    };

    // ─── 攻撃モードに応じたポイント生成 ─────────────────────────────
    public void AttackPointCall(Status Obj, Vector3 ObjP, PlayerMove move)
    {
        this.move = move;
        setpos = mapcreate.SetPos;
        attackmode = move.attackmode;

        switch (attackmode)
        {
            case PlayerMove.AttackMode.Normal:
                NormalAttackPData(Obj, ObjP);
                PointCreate();
                break;
            case PlayerMove.AttackMode.Skill:
                // 今後実装予定
                break;
        }
    }

    // ─── 攻撃ポイントオブジェクトの生成 ─────────────────────────────
    public void PointCreate()
    {
        for (int i = 0; i < AttackP.Count; i++)
        {
            Vector3 pos = AttackP[i];
            pos.y -= 0.17f;
            Instantiate(AttackPoint, pos, Quaternion.identity, APparent);
        }
    }

    // ─── 攻撃ポイントオブジェクトの削除 ─────────────────────────────
    public void AtkpDestroy()
    {
        foreach (Transform child in APparent)
        {
            Destroy(child.gameObject);
        }
        AttackP?.Clear();
    }

    // ─── 通常攻撃の攻撃範囲計算 ──────────────────────────────────────
    public void NormalAttackPData(Status Obj, Vector3 ObjP)
    {
        AttackP?.Clear();
        obj = Obj;
        objp = ObjP;
        movegenerater.UnitPointCore();
        unitdata = movegenerater.UnitPointData;

        if (!AttackPredicateMap.TryGetValue(obj.kind, out Func<float, float, bool> predicate))
        {
            Debug.Log($"[AttackPointt] Kind '{obj.kind}' の攻撃パターンは未実装です");
            return;
        }

        Vector3 ownCell = movegenerater.Cell(objp);
        Vector3 pcpCell = movegenerater.Cell(movegenerater.pcp);

        AttackP = setpos.Where(p =>
        {
            float dx = Mathf.RoundToInt(p.x - objp.x);
            float dz = Mathf.RoundToInt(p.z - objp.z);
            Vector3 cell = movegenerater.Cell(p);
            bool occupied = unitdata.Contains(cell);
            bool notSelf = cell != ownCell && cell != pcpCell;
            return occupied && notSelf && predicate(dx, dz);
        }).ToList();
    }
}
