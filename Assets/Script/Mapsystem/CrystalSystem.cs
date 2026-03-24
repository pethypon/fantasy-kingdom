using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CrystalSystem : MonoBehaviour
{
    [Header("クリスタル駒")]
    [SerializeField] private GameObject PlayerCrystal;
    [SerializeField] private GameObject EnemyCrystal;

    [Header("クリスタル間距離")]
    public int CrystalDistanceXmin = 1;
    public int CrystalDistanceXmax = 10;
    public int CrystalDistanceZmin = 1;
    public int CrystalDistanceZmax = 10;

    [Header("クリスタル親オブジェクト")]
    public Transform Playercrystal;
    public Transform Enemycrystal;

    public Vector3 PCP;
    public Vector3 ECP;

    /// <summary> クリスタルの基本HP </summary>
    public const int CrystalHP = 30000;

    private List<Vector3> _SetPos;
    private int maxx;
    private int maxz;

    // ==== フォールバック設定 ====
    // 配置に失敗した場合、距離を段階的に緩和して再試行する
    // 段階を増やしたい場合はここに値を追加するだけでよい
    // 値は CrystalDistanceMax からのオフセット
    private static readonly int[] DistanceRelaxation = { 0, 2, 4, 6 };

    // ==== メインエントリ ====
    public void CrystalCore()
    {
        MapCreate mapcreate = GetComponent<MapCreate>();
        _SetPos = mapcreate.SetPos;
        maxx = mapcreate.maxX;
        maxz = mapcreate.maxZ;

        PlacePlayerCrystal();
        PlaceEnemyCrystal();
    }

    // ==== プレイヤークリスタル配置 ====
    private void PlacePlayerCrystal()
    {
        var candidates = _SetPos.Where(p =>
            p.x >= 6 && p.x <= maxx - 6 &&
            p.z >= 6 && p.z <= maxz - 6
        ).ToList();

        if (candidates.Count == 0)
        {
            Debug.LogError("[CrystalSystem] プレイヤークリスタルの配置候補が見つかりませんでした");
            return;
        }

        PCP = candidates[Random.Range(0, candidates.Count)];
        var pObj = Instantiate(PlayerCrystal, PCP, Quaternion.identity, Playercrystal);
        ApplyCrystalHP(pObj);
        _SetPos.Remove(PCP);
        Debug.Log("<color=#ffff00ff>[StartSetting]</color> プレイヤークリスタル設置完了");
    }

    // ==== 敵クリスタル配置（距離条件を段階的に緩和してリトライ） ====
    private void PlaceEnemyCrystal()
    {
        foreach (int relax in DistanceRelaxation)
        {
            int margin = relax == 0 ? 6 : 5;
            var candidates = GetEnemyCandidates(
                CrystalDistanceXmax - relax,
                CrystalDistanceZmax - relax,
                margin);

            if (candidates.Count == 0) continue;

            ECP = candidates[Random.Range(0, candidates.Count)];
            var eObj = Instantiate(EnemyCrystal, ECP, Quaternion.identity, Enemycrystal);
            ApplyCrystalHP(eObj);
            Debug.Log("<color=#ffff00ff>[StartSetting]</color> 敵クリスタル設置完了");
            return;
        }

        Debug.LogError("[CrystalSystem] 敵クリスタルの配置候補が見つかりませんでした");
    }

    /// <summary> クリスタルの HP を CrystalHP 定数に設定する。 </summary>
    private void ApplyCrystalHP(GameObject crystalObj)
    {
        var status = crystalObj.GetComponentInChildren<Status>();
        if (status != null)
        {
            status.HP = CrystalHP;
            status.MaxHP = CrystalHP;
        }
    }

    private List<Vector3> GetEnemyCandidates(float minDistX, float minDistZ, int margin)
    {
        return _SetPos.Where(p =>
        {
            float dx = Mathf.Abs(p.x - PCP.x);
            float dz = Mathf.Abs(p.z - PCP.z);
            bool inBoundsX = p.x >= margin && p.x <= maxx - margin;
            bool inBoundsZ = p.z >= margin && p.z <= maxz - margin;
            return dx >= minDistX && dz >= minDistZ && inBoundsX && inBoundsZ;
        }).ToList();
    }
}
