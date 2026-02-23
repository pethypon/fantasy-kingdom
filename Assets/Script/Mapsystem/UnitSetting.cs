using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class UnitSetting : MonoBehaviour
{

    [Header("キング")]
    [SerializeField] GameObject KingPiece;

    [Header("異形")]
    [SerializeField] GameObject StrangePiece;

    [Header("ユニット配置親オブジェクト")]
    [SerializeField] public Transform PlayerUnit;
    [SerializeField] public Transform EnemyUnit;

    private Vector3 pcp;
    private Vector3 ecp;
    private List<Vector3> setpos;
    private List<Vector3> KingPoint;
    private List<Vector3> StrangePoint;
    private int kp;
    private int sp;
    private Vector3 KP;
    private Vector3 SP;
    public void UnitSet() 
    {
        //PCP、ECP、SetPosを取り出す
        CrystalSystem crystalsystem = GetComponent<CrystalSystem>();
        MapCreate mapcreate = GetComponent<MapCreate>();
        pcp = crystalsystem.PCP;
        ecp = crystalsystem.ECP;
        setpos = mapcreate.SetPos;

        //Whereで配置位置を絞る
        KingPoint = setpos.Where
            (p =>
            {
                float px = Mathf.Abs(p.x - pcp.x);
                float pz = Mathf.Abs(p.z - pcp.z);
                bool truex = px <= 1;
                bool truez = pz <= 1 && p != pcp;

                return truex && truez;
            }
            ).ToList();


        StrangePoint = setpos.Where
            (p =>
            {
               float px = Mathf.Abs(p.x - ecp.x);
               float pz = Mathf.Abs(p.z - ecp.z);
               bool truex = px <= 1;
               bool truez = pz <= 1 && p != ecp;

               return truex && truez;
            }
            ).ToList();

        kp = Random.Range(0,KingPoint.Count);
        KP = KingPoint[kp];
        Instantiate(KingPiece, KP, Quaternion.identity,PlayerUnit);
        Debug.Log("<color=#ffff00ff>[StartSetting]</color>王設置");

        sp = Random.Range(0,StrangePoint.Count);
        SP = StrangePoint[sp];
        Instantiate(StrangePiece,SP,Quaternion.identity,EnemyUnit);
        Debug.Log("<color=#ffff00ff>[StartSetting]</color>異形の王設置");

    }
}
