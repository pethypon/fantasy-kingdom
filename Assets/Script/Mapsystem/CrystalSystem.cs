using System.Collections.Generic;
using UnityEngine;

public class CrystalSystem : MonoBehaviour
{
    [Header("クリスタル駒")]
    [SerializeField] private GameObject PlayerCrystal;
    [SerializeField] private GameObject EnemyCrystal;

    [Header("クリスタル間距離")]
    public int CrystalDistanceXmin = 1;
    public int CrystalDistanceXmax = 8;
    public int CrystalDistanceZmin = 1;
    public int CrystalDistanceZmax = 8;

    [Header("クリスタル親オブジェクト")]
    public Transform Playercrystal;
    public Transform Enemycrystal;

    public Vector3 PCP;
    public Vector3 ECP;

    /// <summary> クリスタルの基本HP </summary>
    public const int CrystalHP = 15000;

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
        mapcreate.ExcludePlacementCell(ECP);
    }

    /// <summary>セーブデータから指定位置にクリスタルを配置する</summary>
    public void CrystalCore(Vector3 savedPCP, Vector3 savedECP)
    {
        MapCreate mapcreate = GetComponent<MapCreate>();
        _SetPos = mapcreate.SetPos;
        maxx = mapcreate.maxX;
        maxz = mapcreate.maxZ;

        PCP = savedPCP;
        var pObj = Instantiate(PlayerCrystal, PCP, Quaternion.identity, Playercrystal);
        ConfigureCrystal(pObj, Team.Player);
        GetComponent<MapCreate>().ExcludePlacementCell(PCP);

        ECP = savedECP;
        var eObj = Instantiate(EnemyCrystal, ECP, Quaternion.identity, Enemycrystal);
        ConfigureCrystal(eObj, Team.Enemy);
        GetComponent<MapCreate>().ExcludePlacementCell(ECP);

        Debug.Log($"[CrystalSystem] セーブから復元: PCP={PCP} ECP={ECP}");
    }

    // ==== プレイヤークリスタル配置 ====
    private void PlacePlayerCrystal()
    {
        // 段階的に margin を緩めて候補抽出（小マップで margin=6 が厳しすぎる場合の保険）
        int[] margins = { 6, 4, 2, 0 };
        List<Vector3> candidates = null;
        foreach (int margin in margins)
        {
            candidates = new List<Vector3>();
            foreach (var p in _SetPos)
                if (p.x >= margin && p.x <= maxx - margin && p.z >= margin && p.z <= maxz - margin)
                    candidates.Add(p);
            if (candidates.Count > 0) break;
        }

        if (candidates == null || candidates.Count == 0)
        {
            Debug.LogError("[CrystalSystem] プレイヤークリスタルの配置候補が見つかりませんでした (SetPos が空)");
            return;
        }

        PCP = candidates[Random.Range(0, candidates.Count)];
        var pObj = Instantiate(PlayerCrystal, PCP, Quaternion.identity, Playercrystal);
        ConfigureCrystal(pObj, Team.Player);
        GetComponent<MapCreate>().ExcludePlacementCell(PCP);
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
            ConfigureCrystal(eObj, Team.Enemy);
            Debug.Log($"<color=#ffff00ff>[StartSetting]</color> 敵クリスタル設置完了 (relax={relax})");
            return;
        }

        // 最終フォールバック: 距離・margin 全制約を解除して PCP と異なる任意の位置に配置。
        // 35×35 等の小マップで設定値が大きすぎる場合の保険。
        var fallback = new List<Vector3>();
        foreach (var p in _SetPos)
            if (p != PCP) fallback.Add(p);

        if (fallback.Count > 0)
        {
            ECP = fallback[Random.Range(0, fallback.Count)];
            var eObj = Instantiate(EnemyCrystal, ECP, Quaternion.identity, Enemycrystal);
            ConfigureCrystal(eObj, Team.Enemy);
            Debug.LogWarning($"[CrystalSystem] 通常制約で配置不可 → 全制約解除でフォールバック配置 ECP={ECP}");
            return;
        }

        Debug.LogError("[CrystalSystem] 敵クリスタルの配置候補が見つかりませんでした (SetPos が空)");
    }

    /// <summary> クリスタルの HP を CrystalHP 定数に設定する。 </summary>
    /// <summary>Custom visual prefabs also receive the same crystal identity and hit points.</summary>
    public static void ConfigureCrystal(GameObject crystal, Team team)
    {
        var status = crystal.GetComponentInChildren<Status>();
        if (status == null) status = crystal.AddComponent<Status>();
        status.kind = Kind.Crystal;
        status.type = Type.Unit;
        status.team = team;
        status.direction = team == Team.Player ? Direction.N : Direction.S;
        status.HP = status.MaxHP = CrystalHP;
        status.ATK = status.DEF = 0;
        status.Level = 1;
        // A pedestal collider alone does not cover the crystal above it.
        if (crystal.GetComponent<BoxCollider>() == null)
        {
            var renderers = crystal.GetComponentsInChildren<Renderer>();
            var bounds = new Bounds(Vector3.zero, Vector3.one * .5f);
            foreach (var renderer in renderers)
            {
                var b = renderer.localBounds;
                for (int i=0;i<8;i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                    bounds.Encapsulate(crystal.transform.InverseTransformPoint(renderer.transform.TransformPoint(corner)));
                }
            }
            var collider = crystal.AddComponent<BoxCollider>();
            collider.center = bounds.center; collider.size = bounds.size;
        }
    }

    private List<Vector3> GetEnemyCandidates(float minDistX, float minDistZ, int margin)
    {
        var result = new List<Vector3>();
        foreach (var p in _SetPos)
        {
            float dx = Mathf.Abs(p.x - PCP.x);
            float dz = Mathf.Abs(p.z - PCP.z);
            if (dx < minDistX || dz < minDistZ) continue;
            if (p.x < margin || p.x > maxx - margin) continue;
            if (p.z < margin || p.z > maxz - margin) continue;
            result.Add(p);
        }
        return result;
    }
}
