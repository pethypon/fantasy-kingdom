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

    // ---- 駒ごとの攻撃範囲判定 ----
    // dx = p.x - objp.x（符号付き）, dz = p.z - objp.z（符号付き）
    // Priest は無攻撃のためエントリなし（今後追加予定）
    // 新しく追加する場合はここに1行追加するだけでよい
    static readonly Dictionary<Kind, Func<float, float, bool>> AttackPredicateMap =
        new Dictionary<Kind, Func<float, float, bool>>
    {
        // 前方3マス（左右1・正面）
        { Kind.King,        (dx, dz) => Mathf.Abs(dx) <= 1 && dz == 1 },

        // 前方3マス（Kingと同じ攻撃範囲）
        { Kind.Knight,      (dx, dz) => Mathf.Abs(dx) <= 1 && dz == 1 },

        // 前方直進2・3マス
        { Kind.Archer,      (dx, dz) => dx == 0 && (dz == 2 || dz == 3) },

        // 十字方向2マス
        { Kind.Magic,       (dx, dz) => (Mathf.Abs(dx) == 2 && dz == 0)
                                     || (dx == 0 && Mathf.Abs(dz) == 2) },

        // 前斜め1マス
        { Kind.Assassin,    (dx, dz) => Mathf.Abs(dx) == 1 && dz == 1 },

        // 左右各1マス
        { Kind.Scout,       (dx, dz) => Mathf.Abs(dx) == 1 && dz == 0 },

        // 前直進1マス
        { Kind.Guardian,    (dx, dz) => dx == 0 && dz == 1 },

        // 前直進1・2マス
        { Kind.Crossbow,    (dx, dz) => dx == 0 && (dz == 1 || dz == 2) },

        // 左右各4マス
        { Kind.Magicsniper, (dx, dz) => Mathf.Abs(dx) == 4 && dz == 0 },

        // 前直進3マス
        { Kind.Bomber,      (dx, dz) => dx == 0 && dz == 3 },

        // BOSS: 前方3マス+左右1マス（King同等）
        { Kind.Boss,        (dx, dz) => Mathf.Abs(dx) <= 1 && dz == 1 },
    };

    // ---- 攻撃モードに応じたポイント生成 ----
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
                SkillAttackPData(Obj, ObjP);
                PointCreate();
                break;
        }
    }

    // ---- 攻撃ポイントオブジェクトの生成 ----
    public void PointCreate()
    {
        for (int i = 0; i < AttackP.Count; i++)
        {
            Vector3 pos = AttackP[i];
            pos.y -= 0.17f;
            Instantiate(AttackPoint, pos, Quaternion.identity, APparent);
        }
    }

    // ---- 攻撃ポイントオブジェクトの削除 ----
    public void AtkpDestroy()
    {
        foreach (Transform child in APparent)
        {
            Destroy(child.gameObject);
        }
        AttackP?.Clear();
    }

    // ---- 駒ごとのスキル攻撃位置（仕様書準拠） ----
    static readonly Dictionary<Kind, Vector2Int[]> SkillAttackPositions =
        new Dictionary<Kind, Vector2Int[]>
    {
        { Kind.King,        new[] { new Vector2Int(-1,1), new Vector2Int(0,1), new Vector2Int(1,1) } },
        { Kind.Knight,      new[] { new Vector2Int(-1,1), new Vector2Int(0,1), new Vector2Int(1,1) } },
        { Kind.Archer,      new[] { new Vector2Int(0,2), new Vector2Int(0,3) } },
        { Kind.Magic,       new[] { new Vector2Int(-2,0), new Vector2Int(2,0), new Vector2Int(0,2), new Vector2Int(0,-2) } },
        { Kind.Assassin,    new[] { new Vector2Int(-1,1), new Vector2Int(1,1) } },
        { Kind.Scout,       new[] { new Vector2Int(-1,0), new Vector2Int(1,0) } },
        { Kind.Guardian,    new[] { new Vector2Int(0,1) } },
        { Kind.Crossbow,    new[] { new Vector2Int(0,1), new Vector2Int(0,2) } },
        { Kind.Magicsniper, new[] { new Vector2Int(-4,0), new Vector2Int(4,0) } },
        { Kind.Bomber,      new[] { new Vector2Int(0,3) } },

        // BOSS: King同等のスキル攻撃位置
        { Kind.Boss,        new[] { new Vector2Int(-1,1), new Vector2Int(0,1), new Vector2Int(1,1) } },
    };

    // ---- スキル固有の攻撃位置（ワイドスイング、ピアシングショットなど） ----
    static readonly Dictionary<int, Vector2Int[]> SkillFixedPositions =
        new Dictionary<int, Vector2Int[]>
    {
        // ワイドスイング(7): 周囲8マス
        { 7,  new[] { new Vector2Int(-1,1), new Vector2Int(0,1), new Vector2Int(1,1),
                      new Vector2Int(-1,0), new Vector2Int(1,0),
                      new Vector2Int(-1,-1), new Vector2Int(0,-1), new Vector2Int(1,-1) } },
        // ピアシングショット(8): 前方直線3マス
        { 8,  new[] { new Vector2Int(0,1), new Vector2Int(0,2), new Vector2Int(0,3) } },
        // ブレイクランス(22): 前方直線4マス
        { 22, new[] { new Vector2Int(0,1), new Vector2Int(0,2), new Vector2Int(0,3), new Vector2Int(0,4) } },
        // ペネトレイトレイン(37): 前方直線5マス
        { 37, new[] { new Vector2Int(0,1), new Vector2Int(0,2), new Vector2Int(0,3), new Vector2Int(0,4), new Vector2Int(0,5) } },
        // ワールドエッジ(48): 前方直線7マス
        { 48, new[] { new Vector2Int(0,1), new Vector2Int(0,2), new Vector2Int(0,3), new Vector2Int(0,4),
                      new Vector2Int(0,5), new Vector2Int(0,6), new Vector2Int(0,7) } },
    };

    // ---- スキル攻撃の攻撃範囲計算 ----
    public void SkillAttackPData(Status Obj, Vector3 ObjP)
    {
        AttackP?.Clear();
        obj = Obj;
        objp = ObjP;

        if (obj.AssignedSkillId < 0 || !SkillData.Table.ContainsKey(obj.AssignedSkillId))
        {
            Debug.Log("[AttackPointt] スキル未割り当て");
            return;
        }

        SkillData skill = SkillData.Table[obj.AssignedSkillId];
        movegenerater.UnitPointCore();
        unitdata = movegenerater.UnitPointData;

        // Self / SelfArea → 自分の位置のみ
        if (skill.Target == SkillTarget.Self || skill.Target == SkillTarget.SelfArea)
        {
            AttackP = new List<Vector3> { objp };
            return;
        }

        // 方向係数（南向きなら Z 反転）
        int dirZ = obj.direction == Direction.S ? -1 : 1;

        // 攻撃位置座標の決定
        Vector2Int[] offsets = null;

        // スキル固有座標が定義されていればそちらを優先
        if (SkillFixedPositions.ContainsKey(skill.Id))
        {
            offsets = SkillFixedPositions[skill.Id];
        }
        else
        {
            // 駒ごとの標準攻撃位置を使用
            if (SkillAttackPositions.ContainsKey(obj.kind))
                offsets = SkillAttackPositions[obj.kind];
        }

        if (offsets == null)
        {
            Debug.Log($"[AttackPointt] Kind '{obj.kind}' / Skill '{skill.Name}' の攻撃位置は未定義です");
            return;
        }

        // 攻撃位置をワールド座標に変換してマップ上の実際のタイル位置を探す
        AttackP = new List<Vector3>();
        Vector3 ownCell = movegenerater.Cell(objp);
        Vector3 pcpCell = movegenerater.Cell(movegenerater.pcp);

        foreach (Vector2Int off in offsets)
        {
            int wx = Mathf.RoundToInt(objp.x) + off.x;
            int wz = Mathf.RoundToInt(objp.z) + off.y * dirZ;

            // setpos からマッチするタイルを探す
            foreach (Vector3 p in setpos)
            {
                if (Mathf.RoundToInt(p.x) == wx && Mathf.RoundToInt(p.z) == wz)
                {
                    Vector3 cell = movegenerater.Cell(p);

                    switch (skill.Target)
                    {
                        case SkillTarget.EnemySingle:
                        case SkillTarget.EnemyOrBuilding:
                        case SkillTarget.LowHPEnemy:
                        case SkillTarget.FlyingEnemy:
                            // 敵がいるマスのみ表示
                            if (unitdata.Contains(cell) && cell != ownCell && cell != pcpCell)
                                AttackP.Add(p);
                            break;

                        case SkillTarget.AllySingle:
                            // 味方がいるマスのみ表示（自分除く）
                            if (unitdata.Contains(cell) && cell != ownCell)
                                AttackP.Add(p);
                            break;

                        case SkillTarget.DesignatedTile:
                        case SkillTarget.AdjacentCenter:
                        case SkillTarget.DirectionLine:
                        case SkillTarget.DesignatedRow:
                            // マス自体を表示（占有フィルタ不要）
                            AttackP.Add(p);
                            break;

                        default:
                            if (unitdata.Contains(cell) && cell != ownCell && cell != pcpCell)
                                AttackP.Add(p);
                            break;
                    }
                    break;
                }
            }
        }
    }

    // ---- 通常攻撃の攻撃範囲計算 ----
    public void NormalAttackPData(Status Obj, Vector3 ObjP)
    {
        AttackP?.Clear();
        obj = Obj;
        objp = ObjP;
        movegenerater.UnitPointCore();
        unitdata = movegenerater.UnitPointData;

        if (!AttackPredicateMap.TryGetValue(obj.kind, out Func<float, float, bool> predicate))
        {
            Debug.Log($"[AttackPointt] Kind '{obj.kind}' の攻撃パターンは未定義です");
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
