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

    [Header("ユニットボックス")]
    [SerializeField] Transform PlayerUnit;
    [SerializeField] Transform EnemyUnit;

    // 現在 Attackable=true になっている駒のリスト（解除用に保持）
    private readonly List<Status> _markedTargets = new List<Status>();

    // ─── 駒種ごとの攻撃範囲判定 ──────────────────────────────────────
    // dx = p.x - objp.x（符号付き）, dz = p.z - objp.z（符号付き）
    // 新しい駒を追加する場合はここに1行追加するだけでよい
    public static readonly Dictionary<Kind, Func<float, float, bool>> AttackPredicateMap =
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

        // 隣接4マス（前後左右）の味方を回復対象とする
        { Kind.Priest,      (dx, dz) => (Mathf.Abs(dx) == 1 && dz == 0) || (dx == 0 && Mathf.Abs(dz) == 1) },
    };

    // ─── 攻撃モードに応じた対象マーク ───────────────────────────────
    public void AttackPointCall(Status Obj, Vector3 ObjP, PlayerMove move)
    {
        this.move = move;
        setpos = mapcreate.SetPos;
        attackmode = move.attackmode;

        switch (attackmode)
        {
            case PlayerMove.AttackMode.Normal:
                NormalAttackPData(Obj, ObjP);
                MarkTargets();
                break;
            case PlayerMove.AttackMode.Skill:
                // 今後実装予定
                break;
        }
    }

    // ─── 攻撃範囲内の駒に Attackable フラグを立てる ─────────────────
    private void MarkTargets()
    {
        bool isPriest = obj.kind == Kind.Priest;
        Transform searchParent = isPriest ? PlayerUnit : EnemyUnit;

        foreach (Status s in searchParent.GetComponentsInChildren<Status>())
        {
            if (s.type != Type.Unit) continue;
            Vector3 pos = s.transform.position;
            bool inRange = AttackP.Any(p => Mathf.RoundToInt(p.x) == Mathf.RoundToInt(pos.x)
                                         && Mathf.RoundToInt(p.z) == Mathf.RoundToInt(pos.z));
            if (inRange)
            {
                s.Attackable = true;
                _markedTargets.Add(s);
            }
        }

        Debug.Log($"[AttackPointt] {_markedTargets.Count}体の対象をマーク");
    }

    // ─── 全対象の Attackable フラグを解除する ───────────────────────
    public void ClearTargets()
    {
        foreach (Status s in _markedTargets)
        {
            if (s != null) s.Attackable = false;
        }
        _markedTargets.Clear();
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
            float dirDx = obj.direction == Direction.S ? -dx : dx;
            float dirDz = obj.direction == Direction.S ? -dz : dz;
            Vector3 cell = movegenerater.Cell(p);
            bool occupied = unitdata.Contains(cell);
            bool notSelf = cell != ownCell && cell != pcpCell;
            return occupied && notSelf && predicate(dirDx, dirDz);
        }).ToList();
    }
}
