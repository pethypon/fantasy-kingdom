using System.Collections.Generic;
using UnityEngine;

public class MapCreate : MonoBehaviour
{
<<<<<<< HEAD
    [Header("‹ŠEƒ^ƒCƒ‹")]
=======
    [Header("è¦–ç•Œã‚¿ã‚¤ãƒ«")]
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    [SerializeField] private GameObject Fog;
    [SerializeField] private GameObject FogExploard;
    [SerializeField] private GameObject FogBoard;
    [SerializeField] private GameObject FogExploardBoard;

<<<<<<< HEAD
    [Header("FogeƒIƒuƒWƒFƒNƒg")]
=======
    [Header("Fogè¦ªã‚ªãƒ–ã‚¸ã‚§ã‚¯ãƒˆ")]
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    [SerializeField] public Transform FogParent;
    [SerializeField] public Transform FogExploardParent;
    [SerializeField] public Transform FogBoardParent;
    [SerializeField] public Transform FogExploardBoardParent;

<<<<<<< HEAD
    [Header("“yƒuƒƒbƒN")]
    [SerializeField] public GameObject dirtPrefab;

    [Header("ÎƒuƒƒbƒN")]
    [SerializeField] private GameObject stonePrefab;

    [Header("ƒ}ƒbƒveƒIƒuƒWƒFƒNƒg")]
    [SerializeField] private Transform MapBox;

    [Header("ƒ}ƒbƒvƒTƒCƒY")]
=======
    [Header("åœŸãƒ–ãƒ­ãƒƒã‚¯")]
    [SerializeField] public GameObject dirtPrefab;

    [Header("çŸ³ãƒ–ãƒ­ãƒƒã‚¯")]
    [SerializeField] private GameObject stonePrefab;

    [Header("ãƒãƒƒãƒ—è¦ªã‚ªãƒ–ã‚¸ã‚§ã‚¯ãƒˆ")]
    [SerializeField] private Transform MapBox;

    [Header("ãƒãƒƒãƒ—ã‚µã‚¤ã‚º")]
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    [SerializeField] public int maxX = 50;
    [SerializeField] public int maxY = 2;
    [SerializeField] public int minY = 0;
    [SerializeField] public int maxZ = 50;

<<<<<<< HEAD
    [Header("ƒV[ƒh’l")]
=======
    [Header("ã‚·ãƒ¼ãƒ‰å€¤")]
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    [SerializeField] private float seedx, seedz;

    [Header("ãƒã‚¤ã‚ºè¨­å®š")]
    [SerializeField] public float noiseScale = 0.1f;

    [Header("ãƒãƒƒãƒ—ã®å¹³å‡çš„ãªé«˜ã•")]
    [SerializeField] public int AverageFoundation = 2;

    [Header("æŒ¯ã‚Œå¹…")]
    [SerializeField] public int Amplitude = 6;

<<<<<<< HEAD
    // „Ÿ„Ÿ„Ÿ Fog ƒŒƒCƒ„[İ’è „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
    // Fog ƒŒƒCƒ„[‚ğ‘Œ¸‚µ‚½‚¢ê‡‚Í‚±‚±‚Ì”z—ñ‚É’l‚ğ’Ç‰ÁEíœ‚·‚é‚¾‚¯‚Å‚æ‚¢
    // ’l‚Í maxY ‚©‚ç‚Ì Y ƒIƒtƒZƒbƒg
=======
    // Fog ãƒ¬ã‚¤ãƒ¤ãƒ¼ã‚’å¢—æ¸›ã—ãŸã„å ´åˆã¯ã“ã“ã®é…åˆ—ã«å€¤ã‚’è¿½åŠ ãƒ»å‰Šé™¤ã™ã‚‹ã ã‘ã§ã‚ˆã„
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    private static readonly float[] FogOffsets = { -1.0f, -2.0f };
    private static readonly float[] FogBoardOffsets = { -0.4f };

    private int[,] topY;
    public List<Vector3> SetPos;

    private void Awake()
    {
        SetPos = new List<Vector3>();
    }

<<<<<<< HEAD
    // „Ÿ„Ÿ„Ÿ ƒmƒCƒY¶¬ „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
=======
    // â”€â”€â”€ ãƒã‚¤ã‚ºç”Ÿæˆ â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
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

<<<<<<< HEAD
    // „Ÿ„Ÿ„Ÿ ƒ}ƒbƒv & Fog \’z „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
=======
    // â”€â”€â”€ ãƒãƒƒãƒ— & Fog æ§‹ç¯‰ â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
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
<<<<<<< HEAD
        Debug.Log("<color=#ffff00ff>[StartSetting]</color> ƒ}ƒbƒvEFog Š®¬");
    }

    // „Ÿ„Ÿ„Ÿ ’nŒ`ƒXƒ|[ƒ“ „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
=======
        Debug.Log("<color=#ffff00ff>[StartSetting]</color> ãƒãƒƒãƒ—ãƒ»Fog å®Œæˆ");
    }

    // â”€â”€â”€ åœ°å½¢ã‚¹ãƒãƒ¼ãƒ³ â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    private void SpawnTerrain(int x, int z)
    {
        int y = topY[x, z];
        Instantiate(dirtPrefab, new Vector3(x, y, z), Quaternion.identity, MapBox);
        SetPos.Add(new Vector3Int(x, y + 1, z));

        int downY = y - 1;
        if (downY >= minY)
            Instantiate(dirtPrefab, new Vector3(x, downY, z), Quaternion.identity, MapBox);
    }

<<<<<<< HEAD
    // „Ÿ„Ÿ„Ÿ Fog ƒXƒ|[ƒ“ „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
=======
    // â”€â”€â”€ Fog ã‚¹ãƒãƒ¼ãƒ³ â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
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

<<<<<<< HEAD
    // „Ÿ„Ÿ„Ÿ ‹ŠEİ’èi¡ŒãÀ‘•—\’èj „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
    public void VisionSetting(MapCreate mapcreate)
    {
        // TODO: ‹ŠEŒvZ‚ÌÀ‘•
=======
    public void VisionSetting(MapCreate mapcreate)
    {
        // TODO: è¦–ç•Œè¨ˆç®—ã®å®Ÿè£…
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
        var VisionXZ = new HashSet<Vector3Int>();
    }
}
