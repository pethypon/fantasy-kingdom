using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class MoveGererater : MonoBehaviour
{
    [SerializeField] public MapCreate mapcreate;
    [SerializeField] public TurnGenerater turngenerater;
    //[SerializeField] Status status;
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

    public void UnitPointCore()
    {
        UnitPointData.Clear();
        pcp = crystalsystem.PCP;
        ecp = crystalsystem.ECP;
        UnitPointData.Add(Cell(pcp));
        UnitPointData.Add(Cell(ecp));
        foreach (Status us in PlayerUnit.GetComponentsInChildren<Status>())
        {
            if (us.type == Type.Unit)
            {
                usp = us.transform.position;
                UnitPointData.Add(Cell(usp));
            }
        }

        foreach(Status us in EnemyUnit.GetComponentsInChildren<Status>())
        {
            if (us.type == Type.Unit)
            {
                usp = us.transform.position;
                UnitPointData.Add(Cell(usp));
            }
        }
        
        
    }
    public Vector3 Cell(Vector3 v)
    {
        return new Vector3(Mathf.RoundToInt(v.x), 0f,Mathf.RoundToInt(v.z));
    }
    public void MoveCore(Status Obj,Vector3 ObjP) 
    {
        
        setpos = mapcreate.SetPos;
        MoveUnitP.Clear();
        obj = Obj;
        objp = ObjP;
        switch (obj.kind)
        {
            case Kind.King:
                Debug.Log("<color=#00ff00ff>[Controller]</color>King");
                MoveUnitP = setpos.Where
                    (p =>
                    {
                        float px = Mathf.Abs(p.x - objp.x);
                        float pz = Mathf.Abs(p.z - objp.z);
                        bool upc = UnitPointData.Contains(Cell(p));
                        bool truex = px <= 1;
                        bool truez = pz <= 1;

                        return (truex && truez) && !upc; 
                    }
                    ).ToList();
                MoveCreate();
                
                break;

            case Kind.Knight:
                Debug.Log("<color=#00ff00ff>[Controller]</color>Knight");
                MoveUnitP = setpos.Where
                    (p =>
                    {
                    float px = Mathf.Abs(p.x - objp.x);
                    float pz = Mathf.Abs(p.z - objp.z);
                    bool upc = UnitPointData.Contains(Cell(p));
                        bool truex = px <= 1 && px >= 1;
                    bool truez = pz <= 1 && pz >= 1;
                    return (px + pz) == 1 && !upc;
                    }
                    ).ToList();

                MoveCreate();
                break;

            case Kind.Archer:
                Debug.Log("<color=#00ff00ff>[Controller]</color>Archer");
                MoveUnitP = setpos.Where
                    (p =>
                    {
                        float px = Mathf.Abs(p.x - objp.x);
                        float pz = Mathf.Abs(p.z - objp.z);
                        bool upc = UnitPointData.Contains(Cell(p));
                        bool truex = px <= 1 && px >= 1;
                        bool truez = pz <= 1 && pz >= 1;
                        return (truex && truez) && !upc;
                    }
                    ).ToList();

                MoveCreate();
                break;

            case Kind.Magic:
                Debug.Log("<color=#00ff00ff>[Controller]</color>Archer");
                MoveUnitP = setpos.Where
                    (p =>
                    {
                        float px = Mathf.Abs(p.x - objp.x);
                        float pz = Mathf.Abs(p.z - objp.z);
                        bool upc = UnitPointData.Contains(Cell(p));
                        bool truex = px <= 1 && px >= 1;
                        bool truez = pz <= 1 && pz >= 1;
                        return (truex && truez) && !upc;
                    }
                    ).ToList();

                MoveCreate();
                break;

            case Kind.Assassin:
                Debug.Log("<color=#00ff00ff>[Controller]</color>Assassin");
                MoveUnitP = setpos.Where
                    (p =>
                    {
                        float px = Mathf.Abs(p.x - objp.x);
                        float pz = Mathf.Abs(p.z - objp.z);
                        bool upc = UnitPointData.Contains(Cell(p));
                        bool truex = Mathf.Abs(px) == 2 && Mathf.Abs(pz) == 1;
                        bool truez = Mathf.Abs(px) == 1 && Mathf.Abs(pz) == 2;
                        return (truex || truez) && !upc;
                    }
                    ).ToList();

                MoveCreate();
                break;
                

            case Kind.Scout:
                Debug.Log("<color=#00ff00ff>[Controller]</color>Scout");
                MoveUnitP = setpos.Where
                   (p =>
                   {
                       float px = Mathf.Abs(p.x - objp.x);
                       float pz = Mathf.Abs(p.z - objp.z);
                       bool upc = UnitPointData.Contains(Cell(p));
                       bool truex = (px == 1 && pz <= 1)  || (px == 0 && pz == 2);
                       return (truex) && !upc;
                   }
                   ).ToList();

                MoveCreate();
                break;

            case Kind.Priest:
                Debug.Log("<color=#00ff00ff>[Controller]</color>Priest");
                MoveUnitP = setpos.Where
                   (p =>
                   {
                       float px = Mathf.RoundToInt(p.x - objp.x);
                       float pz = Mathf.RoundToInt(p.z - objp.z);
                       bool upc = UnitPointData.Contains(Cell(p));
                       bool truex = (Mathf.Abs(px) == 1 && pz == 1) || (px == 0 && pz == -1);
                       return (truex) && !upc;
                   }
                   ).ToList();

                MoveCreate();
                break;

            case Kind.Guardian:
                Debug.Log("<color=#00ff00ff>[Controller]</color>Guardian");
                MoveUnitP = setpos.Where
                    (p =>
                    {
                        float px = Mathf.RoundToInt(p.x - objp.x);
                        float pz = Mathf.RoundToInt(p.z - objp.z);
                        bool upc = UnitPointData.Contains(Cell(p));
                        bool move = Mathf.Abs(px) <= 2 && Mathf.Abs(pz) == 0 || Mathf.Abs(px) == 0 && Mathf.Abs(pz) == 1;
                        return (move) && !upc;
                    }
                    ).ToList();

                MoveCreate();
                break;

            case Kind.Crossbow:
                Debug.Log("<color=#00ff00ff>[Controller]</color>Crossbow");
                MoveUnitP = setpos.Where
                    (p =>
                    {
                        float px = Mathf.RoundToInt(p.x - objp.x);
                        float pz = Mathf.RoundToInt(p.z - objp.z);
                        bool upc = UnitPointData.Contains(Cell(p));
                        bool move = (px == 0) && (pz == 1 || pz == 2 || pz == 3);
                        bool back = (Mathf.Abs(px) == 1) && (pz == -1);
                        bool No = p != objp && !upc;
                        return (move || back) && No && !upc;
                    }
                    ).ToList();

                MoveCreate();
                break;

            case Kind.Magicsniper:
                Debug.Log("<color=#00ff00ff>[Controller]</color>Magicsniper");
                MoveUnitP = setpos.Where
                    (p =>
                    {
                        float px = Mathf.RoundToInt(p.x - objp.x);
                        float pz = Mathf.RoundToInt(p.z - objp.z);
                        bool upc = UnitPointData.Contains(Cell(p));
                        bool move = px == 1 && pz == 1;
                        bool back = px == -3 && pz == -1 || px == 3 && pz == -1;
                        return (move || back) && !upc;
                    }
                    ).ToList();
                MoveCreate();
                break;

            case Kind.Bomber:
                Debug.Log("<color=#00ff00ff>[Controller]</color>Bomber");
                MoveUnitP = setpos.Where
                    (p =>
                    {
                        float px = Mathf.RoundToInt(p.x - objp.x);
                        float pz = Mathf.RoundToInt(p.z - objp.z);
                        bool upc = UnitPointData.Contains(Cell(p));
                        bool flont = px == -1 && pz == 1 || px == 2 && pz == 2;
                        bool back = px == 1 && pz == -1 || px == -2 && pz == -2;
                        return (flont || back) &&!upc;
                    }
                    ).ToList();
                MoveCreate();
                break;
                
        }
    }
    public void MoveReset() 
    {
        foreach(Transform child in Move.transform)
        {
            GameObject.Destroy(child.gameObject);
        }
    }
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
    public void MoveUpdate(Vector3 OldCell,Vector3 NewCell)
    {
        UnitPointData.Add(Cell(NewCell));
        UnitPointData.Remove(Cell(OldCell));
        
    }
}
