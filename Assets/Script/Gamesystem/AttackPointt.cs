using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;

public class AttackPointt : MonoBehaviour
{
    public MapCreate mapcreate;
    public PlayerMove move;
    public MoveGererater movegenerater;
    public PlayerMove.AttackMode attackmode;
    public List<Vector3> AttackP;
    public List<Vector3> setpos;
    [Header("ユニット座標")]
    [SerializeField] public HashSet<Vector3> unitdata;
    public Status obj;
    public Vector3 objp;

    public void AttackPointCall(Status Obj,Vector3 ObjP,PlayerMove move)
    {
        this.move = move;
        AttackCall();
        attackmode = move.attackmode;
        switch(attackmode)
        {
            case PlayerMove.AttackMode.Normal:

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
