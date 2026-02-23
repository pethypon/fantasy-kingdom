using System.Collections.Generic;
using UnityEngine;

public class MapCreate : MonoBehaviour
{
    [Header("視界タイル")]
    [SerializeField] private GameObject Fog;
    [SerializeField] private GameObject FogExploard;
    [SerializeField] private GameObject FogBoard;
    [SerializeField] private GameObject FogExploardBoard;

    [Header("Fog親オブジェクト")]
    [SerializeField] public Transform FogParent;
    [SerializeField] public Transform FogExploardParent;
    [SerializeField] public Transform FogBoardParent;
    [SerializeField] public Transform FogExploardBoardParent;

    [Header("土ブロック")]
    [SerializeField] public GameObject dirtPrefab;

    [Header("石ブロック")]
    [SerializeField] private GameObject stonePrefab;

    [Header("マップ親オブジェクト")]
    [SerializeField] private Transform MapBox;

    [Header("マップサイズ")]
    [SerializeField] public int maxX = 50;
    [SerializeField] public int maxY = 2;
    [SerializeField] public int minY = 0;
    [SerializeField] public int maxZ = 50;

    [Header("シード値")]
    [SerializeField] private float seedx, seedz;

    [Header("ノイズ設定")]
    [SerializeField] public float noiseScale = 0.1f;

    [Header("マップの平均的な高さ")]
    [SerializeField] public int AverageFoundation = 2;

    [Header("振れ幅")]
    [SerializeField] public int Amplitude = 6;

    // ─── Fog レイヤー設定 ────────────────────────────────────────────
    // Fog レイヤーを増減したい場合はここの配列に値を追加・削除するだけでよい
    // 値は maxY からの Y オフセット
    private static readonly float[] FogOffsets = { -1.0f, -2.0f };
    private static readonly float[] FogBoardOffsets = { -0.4f };

    private int[,] topY;
    public List<Vector3> SetPos;

    private void Awake()
    {
        SetPos = new List<Vector3>();
    }

    // ─── ノイズ生成 ───────────────────────────────────────────────────
    public void noisegenerater()
    {
        seedx = Random.Range(0f, 1_000_000f);
        seedz = Random.Range(0f, 1_000_000f);
        topY = new int[maxX, maxZ];

        for (int x = 0; x < maxX; x++)
        {
            for (int z = 0; z < maxZ; z++)
            {
                int perlin = Mathf.RoundToInt(
                    Mathf.PerlinNoise(x * noiseScale + seedx, z * noiseScale + seedz) * Amplitude);
                topY[x, z] = Mathf.Clamp(AverageFoundation + perlin, 0, maxY - 1);
            }
        }
    }

    // ─── マップ & Fog 構築 ───────────────────────────────────────────
    public void BuildTop()
    {
        for (int x = 0; x < maxX; x++)
        {
            for (int z = 0; z < maxZ; z++)
            {
                SpawnTerrain(x, z);
                SpawnFogTiles(x, z);
            }
        }
        Debug.Log("<color=#ffff00ff>[StartSetting]</color> マップ・Fog 完成");
    }

    // ─── 地形スポーン ────────────────────────────────────────────────
    private void SpawnTerrain(int x, int z)
    {
        int y = topY[x, z];
        Instantiate(dirtPrefab, new Vector3(x, y, z), Quaternion.identity, MapBox);
        SetPos.Add(new Vector3Int(x, y + 1, z));

        int downY = y - 1;
        if (downY >= minY)
            Instantiate(dirtPrefab, new Vector3(x, downY, z), Quaternion.identity, MapBox);
    }

    // ─── Fog スポーン ────────────────────────────────────────────────
    private void SpawnFogTiles(int x, int z)
    {
        foreach (float offset in FogOffsets)
        {
            SpawnFog(Fog, x, maxY + offset, z, FogParent);
            SpawnFog(FogExploard, x, maxY + offset, z, FogExploardParent);
        }
        foreach (float offset in FogBoardOffsets)
        {
            SpawnFog(FogBoard, x, maxY + offset, z, FogBoardParent);
            SpawnFog(FogExploardBoard, x, maxY + offset, z, FogExploardBoardParent);
        }
    }

    private void SpawnFog(GameObject prefab, int x, float y, int z, Transform parent)
    {
        if (y < minY) return;
        Instantiate(prefab, new Vector3(x, y, z), Quaternion.identity, parent);
    }

    // ─── 視界設定（今後実装予定） ────────────────────────────────────
    public void VisionSetting(MapCreate mapcreate)
    {
        // TODO: 視界計算の実装
        var VisionXZ = new HashSet<Vector3Int>();
    }
}
