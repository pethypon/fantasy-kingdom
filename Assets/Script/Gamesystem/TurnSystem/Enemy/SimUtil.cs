using UnityEngine;

// =====================================================================
//  SimUtil — シミュレーション用ユーティリティ
// =====================================================================
public static class SimUtil
{
    /// <summary>Vector3Int間の距離（Vector3にキャストして計算）</summary>
    public static float Distance(Vector3Int a, Vector3Int b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>マンハッタン距離</summary>
    public static int Manhattan(Vector3Int a, Vector3Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.z - b.z);
    }
}
