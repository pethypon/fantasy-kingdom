using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CrystalSystem : MonoBehaviour
{
    [Header("クリスタル")]
    [SerializeField] GameObject PlayerCrystal;
    [SerializeField] GameObject EnemyCrystal;
    public int CrystalDistanceXmin = 1;
    public int CrystalDistanceXmax = 25;
    public int CrystalDistanceZmin = 1;
    public int CrystalDistanceZmax = 25;

    [Header("クリスタル配置")]
    private int Pci;
    private int Eci;

    [Header("クリスタル親オブジェクト")]
    [SerializeField] Transform Playercrystal;
    [SerializeField] Transform Enemycrystal;

    private List<Vector3> _SetPos;
    private List<Vector3> PlayerSetPos;
    private List<Vector3> EnemySetPos;
    private int maxx;
    private int maxz;
    public Vector3 PCP;
    public Vector3 ECP;

   
    public void CrystalCore() 
    {
        //MapCreateからSetPosを持ってくる
        MapCreate mapcreate = GetComponent<MapCreate>();
        _SetPos = mapcreate.SetPos;
        maxx = mapcreate.maxX;
        maxz = mapcreate.maxZ;

        PlayerSetPos = _SetPos.Where
            (p =>
            p.x >= 6 && p.x <= maxx - 6 && p.z >= 6 && p.z <= maxz - 6

               
            ).ToList();
        //SetPosのListからランダムで取り出して自陣のクリスタルを設置する
        Pci = Random.Range(0, PlayerSetPos.Count);
        PCP = PlayerSetPos[Pci];
        Instantiate(PlayerCrystal, PCP, Quaternion.identity,Playercrystal);
        Debug.Log("<color=#ffff00ff>[StartSetting]</color>クリスタル設置完了");
        _SetPos.Remove(PCP);

        EnemySetPos = _SetPos.Where
       (p =>
       {
           float px = Mathf.Abs(p.x - PCP.x);
           float pz = Mathf.Abs(p.z - PCP.z);
           bool truex = px >= CrystalDistanceXmin && px >= CrystalDistanceXmax && p.x >= 6 && p.x <= maxx - 6;
           bool truez = pz >= CrystalDistanceZmin && pz >= CrystalDistanceZmax && p.z >= 6 &&p.z <= maxz - 6;

           return truex && truez;

       }
       ).ToList();
        Eci = Random.Range(0, EnemySetPos.Count);
        ECP = EnemySetPos[Eci];
        Instantiate(EnemyCrystal, ECP, Quaternion.identity,Enemycrystal);
        Debug.Log("<color=#ffff00ff>[StartSetting]</color>敵クリスタル設置完了"); 
        if (EnemySetPos.Count == 0) 
        {
            EnemySetPos = _SetPos.Where
       (p =>
       {
           float px = Mathf.Abs(p.x - PCP.x);
           float pz = Mathf.Abs(p.z - PCP.z);
           bool truex = px >= CrystalDistanceXmin -1 && px >= CrystalDistanceXmax - 2 && p.x >= 5 && p.x <= maxx - 5;
           bool truez = pz >= CrystalDistanceZmin -1 && pz >= CrystalDistanceZmax - 2 && p.z >= 5 && p.z <= maxz - 5;

           return truex && truez;

       }
       ).ToList();
            Eci = Random.Range(0, EnemySetPos.Count);
            ECP = EnemySetPos[Eci];
            Instantiate(EnemyCrystal, ECP, Quaternion.identity);
            Debug.Log("<color=#ffff00ff>[StartSetting]</color>敵クリスタル設置完了");
        }

        else if (EnemySetPos.Count == 0)
        {
            EnemySetPos = _SetPos.Where
       (p =>
       {
           float px = Mathf.Abs(p.x - PCP.x);
           float pz = Mathf.Abs(p.z - PCP.z);
           bool truex = px >= CrystalDistanceXmin - 1 && px >= CrystalDistanceXmax - 4 && p.x >= 5 && p.x <= maxx - 5;
           bool truez = pz >= CrystalDistanceZmin - 1 && pz >= CrystalDistanceZmax - 4 && p.z >= 5 && p.z <= maxz - 5;

           return truex && truez;

       }
       ).ToList();
            Eci = Random.Range(0, EnemySetPos.Count);
            ECP = EnemySetPos[Eci];
            Instantiate(EnemyCrystal, ECP, Quaternion.identity);
            Debug.Log("<color=#ffff00ff>[StartSetting]</color>敵クリスタル設置完了");
        }

        else if (EnemySetPos.Count == 0)
        {
            EnemySetPos = _SetPos.Where
       (p =>
       {
           float px = Mathf.Abs(p.x - PCP.x);
           float pz = Mathf.Abs(p.z - PCP.z);
           bool truex = px >= CrystalDistanceXmin - 1 && px >= CrystalDistanceXmax - 6 && p.x >= 5 && p.x <= maxx - 5;
           bool truez = pz >= CrystalDistanceZmin - 1 && pz >= CrystalDistanceZmax - 6 && p.z >= 5 && p.z <= maxz - 5;

           return truex && truez;

       }
       ).ToList();
            Eci = Random.Range(0, EnemySetPos.Count);
            ECP = EnemySetPos[Eci];
            Instantiate(EnemyCrystal, ECP, Quaternion.identity);
            Debug.Log("<color=#ffff00ff>[StartSetting]</color>敵クリスタル設置完了");
        }
    }
}
