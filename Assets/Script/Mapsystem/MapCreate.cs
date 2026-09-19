using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// マップサイズのプリセット。Custom はインスペクタの maxX/maxZ をそのまま使用。
/// セーブロード時はプリセットでなく保存値を優先するため、ロード側で Custom 強制。
/// </summary>
public enum MapPresetSize { Small35, Normal40, Custom }

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
    [Tooltip("プリセット適用: Awake時に Small35/Normal40 のサイズを maxX/maxZ へ反映する。Customは手動値を維持")]
    public MapPresetSize preset = MapPresetSize.Small35;
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

    // Older saves retain their original terrain layout.
    public bool UseR1Terrain { get; set; } = true;
    private bool[,] rivers;
    private int[,] topY;
    private readonly HashSet<Vector3Int> excludedPlacementCells = new HashSet<Vector3Int>();
    public List<Vector3> SetPos;

    private void Awake()
    {
        SetPos = new List<Vector3>();
        ApplyPreset();
    }

    /// <summary>
    /// プリセット値を maxX/maxZ に反映する。Custom の場合はインスペクタ値を維持。
    /// セーブロード時に保存値を優先したい場合は preset = Custom にしてから maxX/maxZ を上書きすること。
    /// </summary>
    public void ApplyPreset()
    {
        switch (preset)
        {
            case MapPresetSize.Small35:
                maxX = 35; maxZ = 35;
                break;
            case MapPresetSize.Normal40:
                maxX = 40; maxZ = 40;
                break;
            case MapPresetSize.Custom:
                // 何もしない（インスペクタ値を維持）
                break;
        }
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
        rivers = new bool[maxX, maxZ];
        if (UseR1Terrain) maxY = 4;

        for (int x = 0; x < maxX; x++)
        {
            for (int z = 0; z < maxZ; z++)
            {
                float noise = Mathf.PerlinNoise(x * noiseScale + seedx, z * noiseScale + seedz);
                int perlin = Mathf.RoundToInt(noise * Amplitude);
                topY[x, z] = UseR1Terrain ? Mathf.Clamp(Mathf.FloorToInt(noise * 3f), 0, 2)
                    : Mathf.Clamp(AverageFoundation + perlin, 0, maxY - 1);
                if (UseR1Terrain)
                {
                    float riverLine = Mathf.PerlinNoise(z * noiseScale + seedz, seedx) * (maxX - 1);
                    rivers[x, z] = Mathf.Abs(x - riverLine) < 0.7f;
                    if (rivers[x, z]) topY[x, z] = 0;
                }
            }
        }
    }

    // ==== マップ & Fog 構築 ====
    public void BuildTop()
    {
        excludedPlacementCells.Clear();
        SetPos.Clear();
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
        var terrain = Instantiate(dirtPrefab, new Vector3(x, y, z), Quaternion.identity, MapBox);
        if (IsRiver(x, z))
        {
            var renderer = terrain.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                block.SetColor("_Color", new Color(0.2f, 0.5f, 0.7f));
                block.SetColor("_BaseColor", new Color(0.2f, 0.5f, 0.7f));
                renderer.SetPropertyBlock(block);
            }
        }
        if (!IsHighMountain(x, z)) SetPos.Add(new Vector3Int(x, y + 1, z));

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

    public bool IsRiver(int x, int z) => rivers != null && x >= 0 && z >= 0 && x < maxX && z < maxZ && rivers[x, z];

    public bool IsHighMountain(int x, int z) => UseR1Terrain && topY != null
        && x >= 0 && z >= 0 && x < maxX && z < maxZ && topY[x, z] + 1 == 3;

    // Supercover grid traversal: corner-touching high mountains also block a shot.
    public bool HasClearTerrainLine(Vector3 from, Vector3 to, bool allowMountainEndpoint = false)
    {
        var a = GridHelper.ToGridXZ(from);
        var b = GridHelper.ToGridXZ(to);
        int x = a.x, z = a.z;
        int nx = Mathf.Abs(b.x - x), nz = Mathf.Abs(b.z - z);
        int sx = System.Math.Sign(b.x - x), sz = System.Math.Sign(b.z - z);
        int ix = 0, iz = 0;
        while (ix < nx || iz < nz)
        {
            int decision = (1 + 2 * ix) * nz - (1 + 2 * iz) * nx;
            if (decision == 0)
            {
                if (IsHighMountain(x + sx, z) || IsHighMountain(x, z + sz)) return false;
                x += sx; z += sz; ix++; iz++;
            }
            else if (decision < 0) { x += sx; ix++; }
            else { z += sz; iz++; }
            if (IsHighMountain(x, z) && !(allowMountainEndpoint && x == b.x && z == b.z)) return false;
        }
        return !IsHighMountain(a.x, a.z);
    }

    /// <summary>指定 XZ 座標に通行可能なタイルが存在するかを判定する</summary>
    public bool HasTileAt(int x, int z)
    {
        return TryGetHeight(x, z, out _);
    }

    /// <summary>指定 XZ 座標の配置高さ（SetPos.y）を取得する</summary>
    public bool TryGetHeight(int x, int z, out float y)
    {
        if (topY == null) return GridHelper.TryGetHeight(SetPos, x, z, out y);
        y = 0f;
        if (excludedPlacementCells.Contains(new Vector3Int(x, 0, z))) return false;
        if (x < 0 || z < 0 || x >= topY.GetLength(0) || z >= topY.GetLength(1) || IsHighMountain(x, z)) return false;
        y = topY[x, z] + 1;
        return true;
    }

    /// <summary>クリスタル等の予約セルを配置候補と高さ検索の両方から除外する。</summary>
    public void ExcludePlacementCell(Vector3 position)
    {
        var cell = GridHelper.ToGridXZ(position);
        excludedPlacementCells.Add(cell);
        SetPos?.RemoveAll(p => GridHelper.ToGridXZ(p) == cell);
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
