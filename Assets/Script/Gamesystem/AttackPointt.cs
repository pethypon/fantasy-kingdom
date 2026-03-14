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

        Vector3 ownCell = movegenerater.Cell(objp);

        switch (skill.Target)
        {
            case SkillTarget.Self:
                // 自身対象: 自分の位置のみ
                AttackP = new System.Collections.Generic.List<Vector3> { objp };
                break;

            case SkillTarget.EnemySingle:
            case SkillTarget.EnemyOrBuilding:
            case SkillTarget.LowHPEnemy:
            case SkillTarget.FlyingEnemy:
                // 通常攻撃と同じ範囲判定を使用
                NormalAttackPData(Obj, ObjP);
                break;

            case SkillTarget.AllySingle:
                // 味方ユニットの位置を候補にする（周囲3マス以内）
                AttackP = new System.Collections.Generic.List<Vector3>();
                foreach (Vector3 p in setpos)
                {
                    float dx = Mathf.Abs(p.x - objp.x);
                    float dz = Mathf.Abs(p.z - objp.z);
                    if (dx <= 3 && dz <= 3)
                    {
                        Vector3 cell = movegenerater.Cell(p);
                        if (unitdata.Contains(cell) && cell != ownCell)
                            AttackP.Add(p);
                    }
                }
                break;

            case SkillTarget.DirectionLine:
            case SkillTarget.DesignatedRow:
                // 前方直線の占有マスを表示
                AttackP = new System.Collections.Generic.List<Vector3>();
                int lineLen = skill.Area == SkillAreaShape.Line3 ? 3 :
                              skill.Area == SkillAreaShape.Line4 ? 4 :
                              skill.Area == SkillAreaShape.Line5 ? 5 : 7;
                int dz2 = obj.direction == Direction.S ? -1 : 1;
                for (int i = 1; i <= lineLen; i++)
                {
                    Vector3 candidate = new Vector3(objp.x, objp.y, objp.z + dz2 * i);
                    foreach (Vector3 p in setpos)
                    {
                        if (Mathf.RoundToInt(p.x) == Mathf.RoundToInt(candidate.x) &&
                            Mathf.RoundToInt(p.z) == Mathf.RoundToInt(candidate.z))
                        {
                            AttackP.Add(p);
                            break;
                        }
                    }
                }
                break;

            case SkillTarget.DesignatedTile:
            case SkillTarget.AdjacentCenter:
                // 指定マス: 周囲一定範囲のマスを全て候補にする
                AttackP = new System.Collections.Generic.List<Vector3>();
                int range = skill.Area == SkillAreaShape.Area5x5 ? 5 :
                            skill.Area == SkillAreaShape.Area3x3 ? 4 :
                            skill.Area == SkillAreaShape.Area2x2 ? 3 :
                            skill.Area == SkillAreaShape.Cross2  ? 3 : 2;
                foreach (Vector3 p in setpos)
                {
                    float dx = Mathf.Abs(p.x - objp.x);
                    float dz = Mathf.Abs(p.z - objp.z);
                    if (dx <= range && dz <= range)
                        AttackP.Add(p);
                }
                break;

            case SkillTarget.SelfArea:
                // 自身中心範囲: 自分の位置のみ（実行時に範囲計算）
                AttackP = new System.Collections.Generic.List<Vector3> { objp };
                break;

            default:
                NormalAttackPData(Obj, ObjP);
                break;
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
