using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class TerritorySystem : MonoBehaviour
{
    [Header("テリトリーオブジェクト")]
    [SerializeField] GameObject PlayerTerritory;
    [SerializeField] GameObject EnemyTerritory;

    [Header("テリトリー親オブジェクト")]
    public Transform Playerterritory;
    public Transform Enemyterritory;

    public List<Vector3> PTSetPos;
    public List<Vector3> ETSetPos;

    private const int TerritoryRadius = 3;

    public void Territory()
    {
        MapCreate mapcreate = GetComponent<MapCreate>();
        CrystalSystem crystalsystem = GetComponent<CrystalSystem>();
        var setpos = mapcreate.SetPos;
        Vector3 pcp = crystalsystem.PCP;
        Vector3 ecp = crystalsystem.ECP;

        // PCP 周辺の半径3マス以内を領地として設定
        PTSetPos = setpos.Where(p =>
        {
            float px = Mathf.Abs(p.x - pcp.x);
            float pz = Mathf.Abs(p.z - pcp.z);
            return px <= TerritoryRadius && pz <= TerritoryRadius && p != pcp;
        }).ToList();

        // ECP 周辺の半径3マス以内を領地として設定
        ETSetPos = setpos.Where(e =>
        {
            float ex = Mathf.Abs(e.x - ecp.x);
            float ez = Mathf.Abs(e.z - ecp.z);
            return ex <= TerritoryRadius && ez <= TerritoryRadius && e != ecp;
        }).ToList();

        SpawnTerritoryTiles(PTSetPos, PlayerTerritory, Playerterritory);
        SpawnTerritoryTiles(ETSetPos, EnemyTerritory, Enemyterritory);
    }

    private void SpawnTerritoryTiles(List<Vector3> positions, GameObject prefab, Transform parent)
    {
        for (int i = 0; i < positions.Count; i++)
        {
            Vector3 pos = positions[i];
            pos.y -= GameConstants.TerritoryYOffset;
            Instantiate(prefab, pos, Quaternion.identity, parent);
        }
        Debug.Log($"<color=#ffff00ff>[StartSetting]</color> 領地設置完了 ({positions.Count}マス)");
    }
}
