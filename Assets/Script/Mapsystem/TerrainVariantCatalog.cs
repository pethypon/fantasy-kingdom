using UnityEngine;

/// <summary>Visual variants only: selection never consumes the gameplay random stream.</summary>
[CreateAssetMenu(menuName = "Fantasy Kingdom/Terrain Variant Catalog")]
public sealed class TerrainVariantCatalog : ScriptableObject
{
    [Tooltip("草原（高さ1）。空欄は無視します。1マス1×1、中心原点のPrefab。")]
    public GameObject[] grass = new GameObject[0];
    [Tooltip("低山（高さ2）")]
    public GameObject[] lowMountain = new GameObject[0];
    [Tooltip("高山（高さ3）")]
    public GameObject[] highMountain = new GameObject[0];

    public GameObject Select(int level, int x, int z, float seedX, float seedZ, GameObject fallback)
    {
        var list = level >= 2 ? highMountain : level == 1 ? lowMountain : grass;
        if (list == null || list.Length == 0) return fallback;
        int count = 0;
        foreach (var item in list) if (item != null) count++;
        if (count == 0) return fallback;
        uint hash;
        unchecked
        {
            hash = (uint)x * 73856093u ^ (uint)z * 19349663u ^ (uint)level * 83492791u;
            hash ^= (uint)Mathf.RoundToInt(seedX) * 2654435761u ^ (uint)Mathf.RoundToInt(seedZ);
            hash ^= hash >> 16; hash *= 0x7feb352du; hash ^= hash >> 15;
        }
        int index = (int)(hash % (uint)count);
        foreach (var item in list) if (item != null && index-- == 0) return item;
        return fallback;
    }
}
