using System.Collections.Generic;
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

    private const int TerritoryRadius = 5;

    public void Territory()
    {
        MapCreate mapcreate = GetComponent<MapCreate>();
        CrystalSystem crystalsystem = GetComponent<CrystalSystem>();
        var setpos = mapcreate.SetPos;
        Vector3 pcp = crystalsystem.PCP;
        Vector3 ecp = crystalsystem.ECP;

        PTSetPos = new List<Vector3>();
        ETSetPos = new List<Vector3>();
        foreach (var p in setpos)
        {
            float px = Mathf.Abs(p.x - pcp.x);
            float pz = Mathf.Abs(p.z - pcp.z);
            if (px <= TerritoryRadius && pz <= TerritoryRadius && p != pcp)
                PTSetPos.Add(p);

            float ex = Mathf.Abs(p.x - ecp.x);
            float ez = Mathf.Abs(p.z - ecp.z);
            if (ex <= TerritoryRadius && ez <= TerritoryRadius && p != ecp)
                ETSetPos.Add(p);
        }

        SpawnTerritoryTiles(PTSetPos, PlayerTerritory, Playerterritory);
        SpawnTerritoryTiles(ETSetPos, EnemyTerritory, Enemyterritory);
    }

    private void SpawnTerritoryTiles(List<Vector3> positions, GameObject prefab, Transform parent)
    {
        for (int i = 0; i < positions.Count; i++)
        {
            Vector3 pos = positions[i];
            pos.y -= 0.475f;
            Instantiate(prefab, pos, Quaternion.identity, parent);
        }
        Debug.Log($"<color=#ffff00ff>[StartSetting]</color> 領地設置完了 ({positions.Count}マス)");
    }

    // ==================================================================
    //  クエリメソッド（外部システムからの Feature Envy を解消）
    // ==================================================================

    /// <summary>指定チームの領地座標リストを返す</summary>
    public List<Vector3> GetTerritory(Team team)
    {
        return team == Team.Player ? PTSetPos : ETSetPos;
    }

    /// <summary>指定座標が指定チームの領地内にあるかを判定する</summary>
    public bool IsInTerritory(Vector3Int pos, Team team)
    {
        var territory = GetTerritory(team);
        return GridHelper.ContainsXZ(territory, pos);
    }

    /// <summary>指定座標がいずれかのチームの領地内にあるかを判定する</summary>
    public bool IsInAnyTerritory(int x, int z)
    {
        return GridHelper.ContainsXZ(PTSetPos, x, z)
            || GridHelper.ContainsXZ(ETSetPos, x, z);
    }

    /// <summary>指定座標に最も近い領地内座標を返す（見つからなければ int.MinValue）</summary>
    public Vector3Int ClampToTerritory(Vector3Int pos, Team team)
    {
        var territory = GetTerritory(team);
        if (territory == null || territory.Count == 0)
            return new Vector3Int(int.MinValue, 0, 0);

        return GridHelper.SnapToNearest(territory, pos);
    }
}
