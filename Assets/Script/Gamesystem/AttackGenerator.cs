using System;
using System.Collections.Generic;
using UnityEngine;

public class AttackGenerator : MonoBehaviour
{
    public PlayerMove.AttackMode attackmode;
    public List<Vector3> AttackP;
    public List<Vector3> setpos;

    [Header("ユニット座標")]
    public HashSet<Vector3> unitdata;

    public Status obj;
    public Vector3 objp;
    public Vector3 attackpos;
    public RaycastHit targethit;

    [Header("マップクリエイト")]
    public MapCreate mapcreate;

    [Header("プレイヤームーブ")]
    public PlayerMove move;

    [Header("ムーブジェネレーター")]
    public MoveGenerator moveGenerator;

    [Header("アタックポイント")]
    public GameObject AttackPoint;

    [Header("アタックポイント親")]
    public Transform APparent;

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

    // ---- 攻撃ポイントオブジェクトの生成（ObjectPool使用） ----
    public void PointCreate()
    {
        var pool = ObjectPool.Instance;
        for (int i = 0; i < AttackP.Count; i++)
        {
            Vector3 pos = AttackP[i];
            pos.y -= GameConstants.AttackPointYOffset;

            if (pool != null)
                pool.Get(AttackPoint, pos, Quaternion.identity, APparent);
            else
                Instantiate(AttackPoint, pos, Quaternion.identity, APparent);
        }
    }

    // ---- 攻撃ポイントオブジェクトの削除（ObjectPool使用） ----
    public void AtkpDestroy()
    {
        var pool = ObjectPool.Instance;
        if (pool != null)
        {
            pool.ReturnAllChildren(APparent);
        }
        else
        {
            foreach (Transform child in APparent)
            {
                Destroy(child.gameObject);
            }
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
            Debug.Log("[AttackGenerator] スキル未割り当て");
            return;
        }

        SkillData skill = SkillData.Table[obj.AssignedSkillId];
        moveGenerator.UnitPointCore();
        unitdata = moveGenerator.UnitPointData;

        // Self / SelfArea → 自分の位置のみ
        if (skill.Target == SkillTarget.Self || skill.Target == SkillTarget.SelfArea)
        {
            AttackP = new List<Vector3> { objp };
            return;
        }

        // 方向係数（南向きなら Z 反転）
        int dirZ = MovePatterns.DirZ(obj.direction);

        // 攻撃位置座標の決定（共有パターンを使用）
        Vector2Int[] offsets = null;

        if (AttackPatterns.SkillFixedPositions.ContainsKey(skill.Id))
        {
            offsets = AttackPatterns.SkillFixedPositions[skill.Id];
        }
        else if (AttackPatterns.SkillAttackPositions.ContainsKey(obj.kind))
        {
            offsets = AttackPatterns.SkillAttackPositions[obj.kind];
        }

        if (offsets == null)
        {
            Debug.Log($"[AttackGenerator] Kind '{obj.kind}' / Skill '{skill.Name}' の攻撃位置は未定義です");
            return;
        }

        AttackP = new List<Vector3>();
        Vector3 ownCell = moveGenerator.Cell(objp);
        Vector3 pcpCell = moveGenerator.Cell(moveGenerator.pcp);

        foreach (Vector2Int off in offsets)
        {
            int wx = Mathf.RoundToInt(objp.x) + off.x;
            int wz = Mathf.RoundToInt(objp.z) + off.y * dirZ;

            // setpos からマッチするタイルを探す
            for (int i = 0, count = setpos.Count; i < count; i++)
            {
                Vector3 p = setpos[i];
                if (Mathf.RoundToInt(p.x) != wx || Mathf.RoundToInt(p.z) != wz)
                    continue;

                Vector3 cell = moveGenerator.Cell(p);

                switch (skill.Target)
                {
                    case SkillTarget.EnemySingle:
                    case SkillTarget.EnemyOrBuilding:
                    case SkillTarget.LowHPEnemy:
                    case SkillTarget.FlyingEnemy:
                        if (unitdata.Contains(cell) && cell != ownCell && cell != pcpCell)
                            AttackP.Add(p);
                        break;

                    case SkillTarget.AllySingle:
                        if (unitdata.Contains(cell) && cell != ownCell)
                            AttackP.Add(p);
                        break;

                    case SkillTarget.DesignatedTile:
                    case SkillTarget.AdjacentCenter:
                    case SkillTarget.DirectionLine:
                    case SkillTarget.DesignatedRow:
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

    // ---- 通常攻撃の攻撃範囲計算（LINQ排除） ----
    public void NormalAttackPData(Status Obj, Vector3 ObjP)
    {
        AttackP?.Clear();
        obj = Obj;
        objp = ObjP;
        moveGenerator.UnitPointCore();
        unitdata = moveGenerator.UnitPointData;

        if (!AttackPatterns.NormalMap.TryGetValue(obj.kind, out Func<float, float, bool> predicate))
        {
            Debug.Log($"[AttackGenerator] Kind '{obj.kind}' の攻撃パターンは未定義です");
            return;
        }

        Vector3 ownCell = moveGenerator.Cell(objp);
        Vector3 pcpCell = moveGenerator.Cell(moveGenerator.pcp);
        bool dirIndependent = AttackPatterns.DirectionIndependent.Contains(obj.kind);
        int dirZ = MovePatterns.DirZ(obj.direction);

        AttackP = new List<Vector3>();
        for (int i = 0, count = setpos.Count; i < count; i++)
        {
            Vector3 p = setpos[i];
            float dx = Mathf.RoundToInt(p.x - objp.x);
            float dz = Mathf.RoundToInt(p.z - objp.z);

            float checkDz = dirIndependent ? dz : dz * dirZ;

            Vector3 cell = moveGenerator.Cell(p);
            if (!unitdata.Contains(cell)) continue;
            if (cell == ownCell || cell == pcpCell) continue;
            if (!predicate(dx, checkDz)) continue;

            AttackP.Add(p);
        }
    }
}
