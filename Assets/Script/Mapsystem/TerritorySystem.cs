using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


public class TerritorySystem : MonoBehaviour
{

    [Header("テリトリーオブジェクト")]
    [SerializeField] GameObject PlayerTerritory;
    [SerializeField] GameObject EnemyTerritory;

    [Header("テリトリー親オブジェクト")]
    [SerializeField] public Transform Playerterritory;
    [SerializeField] public Transform Enemyterritory;

    public List<Vector3> PTSetPos;
    public List<Vector3> ETSetPos;
    private List<Vector3> setpos;
    private Vector3 pcp;
    private Vector3 ecp;

    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

    }

    public void Territory()
    {
        MapCreate mapcreate = GetComponent<MapCreate>();
        CrystalSystem crystalsystem = GetComponent<CrystalSystem>();
        setpos = mapcreate.SetPos;
        pcp = crystalsystem.PCP;
        ecp = crystalsystem.ECP;

        // PCP周辺の条件を満たすところにおいて半径3の条件を考慮
        PTSetPos = setpos.Where
        (p =>
        {
            float px = Mathf.Abs(p.x - pcp.x);
            float pz = Mathf.Abs(p.z - pcp.z);
            bool truex = px <= 3;
            bool truez = pz <= 3 && p != pcp;


            return truex && truez;
        }
        ).ToList();

        // ECP周辺の条件を満たすところにおいて半径3の条件を考慮
        ETSetPos = setpos.Where
          (e =>
          {
          float ex = Mathf.Abs(e.x - ecp.x);
          float ez = Mathf.Abs(e.z - ecp.z);
          bool truex = ex <= 3;
          bool truez = ez <= 3 && e != ecp;
              return truex && truez;
          }
          ).ToList();

        for (int i = 0;i < PTSetPos.Count; i ++)
        {
            Vector3 pos = PTSetPos[i];
            pos.y -= 0.475f;
            Instantiate(PlayerTerritory, pos, Quaternion.identity,Playerterritory);
            Debug.Log("<color=#ffff00ff>[StartSetting]</color>設置完了");
        }

        for (int i = 0;i < ETSetPos.Count; i ++)
        {
            Vector3 pos = ETSetPos[i];
            pos.y -= 0.475f;
            Instantiate(EnemyTerritory,pos,Quaternion.identity,Enemyterritory);
            Debug.Log("<color=#ffff00ff>[StartSetting]</color>設置完了");
        }

    }
}
