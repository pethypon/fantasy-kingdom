using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
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

    

    public void AttackPointCall(Status Obj,Vector3 ObjP,PlayerMove move)
    {
        this.move = move;
        AttackCall();
        attackmode = move.attackmode;
        switch(attackmode)
        {
            case PlayerMove.AttackMode.Normal:
                NormalAttackPData(Obj,ObjP);
                PointCreate();
            break;

            case PlayerMove.AttackMode.Skill:

            break;
        }

    }

    public void AttackCall()
    {
        setpos = mapcreate.SetPos;
        
    }

    public void PointCreate()
    {
        for (int i = 0 ; i < AttackP.Count; i++)
        {
            Vector3 pos = AttackP[i];
            pos.y -= 0.17f;
            Instantiate(AttackPoint, pos, Quaternion.identity,APparent);
        }
    }

    public void AtkpDestroy()
    {
        foreach(Transform child in APparent)
        {
            GameObject.Destroy(child.gameObject);
        }
        if(AttackP != null)
        {
            AttackP.Clear();
        }
        
    }


    public void NormalAttackPData(Status Obj, Vector3 ObjP)
    {
        if(AttackP != null)
        {
            AttackP.Clear();
        }
        obj = Obj;
        objp = ObjP;
        movegenerater.UnitPointCore();
        unitdata = movegenerater.UnitPointData;

        // 攻撃位置を全て求めてさらに敵がいる場所だけを割り出す
        switch (obj.kind)
        {
            case Kind.King:
                AttackP = setpos.Where
                (p =>
                {
                    float px = Mathf.RoundToInt(p.x - objp.x);
                    float pz = Mathf.RoundToInt(p.z - objp.z);
                    bool possiblepoint = unitdata.Contains(movegenerater.Cell(p));
                    bool notaimpoint = movegenerater.Cell(p) != movegenerater.Cell(objp) && movegenerater.Cell(p) != movegenerater.Cell(movegenerater.pcp);
                    bool aimpoint = px == -1 && pz == 1 || px == 0 && pz == 1 || px == 1 && pz == 1;
                    return possiblepoint && notaimpoint && aimpoint;
                }
                ).ToList();

                break;

            case Kind.Knight:
                AttackP = setpos.Where
                (p =>
                {
                    float px = Mathf.RoundToInt(p.x - objp.x);
                    float pz = Mathf.RoundToInt(p.z - objp.z);
                    bool possiblepoint = unitdata.Contains(movegenerater.Cell(p));
                    bool notaimpoint = movegenerater.Cell(p) != movegenerater.Cell(objp) && movegenerater.Cell(p) != movegenerater.Cell(movegenerater.pcp);
                    bool aimpoint = px == -1 && pz == 1 || px == 0 && pz == 1 || px == 1 && pz == 1;
                    return possiblepoint && notaimpoint && aimpoint;
                }
                ).ToList();

                break;

            case Kind.Archer:
                AttackP = setpos.Where
                (p =>
                {
                    float px = Mathf.RoundToInt(p.x - objp.x);
                    float pz = Mathf.RoundToInt(p.z - objp.z);
                    bool possiblepoint = unitdata.Contains(movegenerater.Cell(p));
                    bool notaimpoint = movegenerater.Cell(p) != movegenerater.Cell(objp) && movegenerater.Cell(p) != movegenerater.Cell(movegenerater.pcp);
                    bool aimpoint = px == 0 && pz == 2 || px == 0 && pz == 3;
                    return possiblepoint && notaimpoint && aimpoint;
                }
                ).ToList();
                break;

            case Kind.Magic:
                AttackP = setpos.Where
                (p =>
                {
                    float px = Mathf.RoundToInt(p.x - objp.x);
                    float pz = Mathf.RoundToInt(p.z - objp.z);
                    bool possiblepoint = unitdata.Contains(movegenerater.Cell(p));
                    bool notaimpoint = movegenerater.Cell(p) != movegenerater.Cell(objp) && movegenerater.Cell(p) != movegenerater.Cell(movegenerater.pcp);
                    bool aimpoint = px == -2 && pz == 0 || px == 2 && pz == 0 || px == 0 && pz == 2 || px == 0 && pz == -2;
                    return possiblepoint && notaimpoint && aimpoint;
                }
                ).ToList();
                break;

            case Kind.Assassin:
                AttackP = setpos.Where
                (p =>
                {
                    float px = Mathf.RoundToInt(p.x - objp.x);
                    float pz = Mathf.RoundToInt(p.z - objp.z);
                    bool possiblepoint = unitdata.Contains(movegenerater.Cell(p));
                    bool notaimpoint = movegenerater.Cell(p) != movegenerater.Cell(objp) && movegenerater.Cell(p) != movegenerater.Cell(movegenerater.pcp);
                    bool aimpoint = px == -1 && pz == 1 || px == 1 && pz == 1;
                    return possiblepoint && notaimpoint && aimpoint;
                }
                ).ToList();
                break;

            case Kind.Scout:
                AttackP = setpos.Where
               (p =>
               {
                   float px = Mathf.RoundToInt(p.x - objp.x);
                   float pz = Mathf.RoundToInt(p.z - objp.z);
                   bool possiblepoint = unitdata.Contains(movegenerater.Cell(p));
                   bool notaimpoint = movegenerater.Cell(p) != movegenerater.Cell(objp) && movegenerater.Cell(p) != movegenerater.Cell(movegenerater.pcp);
                   bool aimpoint = px == -1 && pz == 0 || px == 1 && pz == 0;
                   return possiblepoint && notaimpoint && aimpoint;
               }
               ).ToList();
                break;

            case Kind.Priest:
                //今後作る
                break;

            case Kind.Guardian:
                AttackP = setpos.Where
               (p =>
               {
                   float px = Mathf.RoundToInt(p.x - objp.x);
                   float pz = Mathf.RoundToInt(p.z - objp.z);
                   bool possiblepoint = unitdata.Contains(movegenerater.Cell(p));
                   bool notaimpoint = movegenerater.Cell(p) != movegenerater.Cell(objp) && movegenerater.Cell(p) != movegenerater.Cell(movegenerater.pcp);
                   bool aimpoint = px == 0 && pz == 1;
                   return possiblepoint && notaimpoint && aimpoint;
               }
               ).ToList();
                break;

            case Kind.Crossbow:
                AttackP = setpos.Where
               (p =>
               {
                   float px = Mathf.RoundToInt(p.x - objp.x);
                   float pz = Mathf.RoundToInt(p.z - objp.z);
                   bool possiblepoint = unitdata.Contains(movegenerater.Cell(p));
                   bool notaimpoint = movegenerater.Cell(p) != movegenerater.Cell(objp) && movegenerater.Cell(p) != movegenerater.Cell(movegenerater.pcp);
                   bool aimpoint = px == 0 && pz == 1 || px == 0 && pz == 2;
                   return possiblepoint && notaimpoint && aimpoint;
               }
               ).ToList();
                break;

            case Kind.Magicsniper:
                AttackP = setpos.Where
               (p =>
               {
                   float px = Mathf.RoundToInt(p.x - objp.x);
                   float pz = Mathf.RoundToInt(p.z - objp.z);
                   bool possiblepoint = unitdata.Contains(movegenerater.Cell(p));
                   bool notaimpoint = movegenerater.Cell(p) != movegenerater.Cell(objp) && movegenerater.Cell(p) != movegenerater.Cell(movegenerater.pcp);
                   bool aimpoint = px == -6 && pz == 0 || px == 6 && pz == 0;
                   return possiblepoint && notaimpoint && aimpoint;
               }
               ).ToList();
                break;

            case Kind.Bomber:
                AttackP = setpos.Where
               (p =>
               {
                   float px = Mathf.RoundToInt(p.x - objp.x);
                   float pz = Mathf.RoundToInt(p.z - objp.z);
                   bool possiblepoint = unitdata.Contains(movegenerater.Cell(p));
                   bool notaimpoint = movegenerater.Cell(p) != movegenerater.Cell(objp) && movegenerater.Cell(p) != movegenerater.Cell(movegenerater.pcp);
                   bool aimpoint = px == 0 && pz == -3;
                   return possiblepoint && notaimpoint && aimpoint;
               }
               ).ToList();
                break;



        }
    }
}
