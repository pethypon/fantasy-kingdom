using System.Collections.Generic;
using UnityEngine;

public class MapCreate : MonoBehaviour
{
    [Header("霧タイル")]
    [SerializeField] private GameObject Fog;
    [SerializeField] private GameObject FogExploard;
    [SerializeField] private GameObject FogBoard;
    [SerializeField] private GameObject FogExploardBoard;

    [Header("Fog親オブジェクト")]
    public Transform FogParent;
    public Transform FogExploardParent;
    public Transform FogBoardParent;
    public Transform FogExploardBoardParent;

    [Header("土ブロック")]
    public GameObject dirtPrefab;

    [Header("石ブロック")]
    [SerializeField] private GameObject stonePrefab;

    [Header("マップ親オブジェクト")]
    [SerializeField] private Transform MapBox;

    [Header("マップサイズ")]
    public int maxX = 35;
    public int maxY = 2;
    public int minY = 0;
    public int maxZ = 35;

    [Header("シード値")]
    [SerializeField] private float seedx, seedz;

    [Header("ノイズ設定")]
    public float noiseScale = 0.1f;

    [Header("マップの平均的な高さ")]
    public int AverageFoundation = 2;

    [Header("振れ幅")]
    public int Amplitude = 6;

    // ==== Fog レイヤー設定 ====
    // Fog レイヤーを増減したい場合はこちらの配列に値を追加・削除するだけでよい
    // 値は maxY からの Y オフセット
    private static readonly float[] FogOffsets = { -1.0f, -2.0f };
    private static readonly float[] FogBoardOffsets = { -0.4f };

    private int[,] topY;
    public List<Vector3> SetPos;

    private void Awake()
    {
        SetPos = new List<Vector3>();
    }

    // ==== ノイズ生成 ====
    public void GenerateNoise()
    {
        seedx = Random.Range(0f, 1_000_000f);
        seedz = Random.Range(0f, 1_000_000f);
        GenerateHeightmap();
    }

    /// <summary>保存されたシードからノイズを再生成する</summary>
    public void GenerateNoise(float savedSeedX, float savedSeedZ)
    {
        seedx = savedSeedX;
        seedz = savedSeedZ;
        GenerateHeightmap();
    }

    /// <summary>現在のシード値を取得（セーブ用）</summary>
    public float SeedX => seedx;
    public float SeedZ => seedz;

    private void GenerateHeightmap()
    {
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

    // ==== マップ & Fog 構築 ====
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
        Debug.Log("<color=#ffff00ff>[StartSetting]</color> マップ・Fog 完了");
    }

    // ==== 地形スポーン ====
    private void SpawnTerrain(int x, int z)
    {
        int y = topY[x, z];
        Instantiate(dirtPrefab, new Vector3(x, y, z), Quaternion.identity, MapBox);
        SetPos.Add(new Vector3Int(x, y + 1, z));

        int downY = y - 1;
        if (downY >= minY)
            Instantiate(dirtPrefab, new Vector3(x, downY, z), Quaternion.identity, MapBox);
    }

    // ==== Fog スポーン ====
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

    private void SpawnFog(GameObject prefab, float x, float y, float z, Transform parent)
    {
        Instantiate(prefab, new Vector3(x, y, z), Quaternion.identity, parent);
    }

    // ==================================================================
    //  クエリメソッド（外部からの SetPos 直接操作を解消）
    // ==================================================================

    /// <summary>指定 XZ 座標にタイルが存在するかを判定する</summary>
    public bool HasTileAt(int x, int z)
    {
        return GridHelper.ExistsOnMap(SetPos, x, z);
    }

    /// <summary>指定 XZ 座標の配置高さ（SetPos.y）を取得する</summary>
    public bool TryGetHeight(int x, int z, out float y)
    {
        return GridHelper.TryGetHeight(SetPos, x, z, out y);
    }

    /// <summary>指定グリッド座標を SetPos 上の最も近い座標にスナップする</summary>
    public Vector3Int SnapToSetPos(Vector3Int gridPos)
    {
        return GridHelper.SnapToNearest(SetPos, gridPos);
    }

    /// <summary>SetPos の XZ→Y ルックアップ辞書を構築する</summary>
    public Dictionary<(int, int), int> BuildHeightLookup()
    {
        return GridHelper.BuildHeightLookup(SetPos);
    }
}
