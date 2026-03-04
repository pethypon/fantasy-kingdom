using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class UnitSetting : MonoBehaviour
{
    [Header("キング")]
    [SerializeField] GameObject KingPiece;
    [Header("異形")]
    [SerializeField] GameObject StrangePiece;

    [Header("ユニット配置親オブジェクト")]
    [SerializeField] public Transform PlayerUnit;
    [SerializeField] public Transform EnemyUnit;

    // ─── UnitData 管理（Dictionary 化） ─────────────────────────────
    // SerializeField を11個並べない理由：
    // 新しい Kind を追加するたびにフィールド追加とInspector設定の2手間が発生する。
    // Dictionary ならリストに1エントリ追加するだけで済む（設計原則2）。
    [System.Serializable]
    public class UnitDataEntry
    {
        public Kind kind;
        public UnitData data;
    }

    [Header("ユニットデータ（Kind別）")]
    [SerializeField] private List<UnitDataEntry> _unitDataList;

    // 外部からの読み取り専用（GameGenerater・BattleSystem・PlayerSummon が参照）
    public Dictionary<Kind, UnitData> UnitDataMap { get; private set; }

    // ─── 初期化 ──────────────────────────────────────────────────────
    private void Awake()
    {
        UnitDataMap = new Dictionary<Kind, UnitData>();
        foreach (var entry in _unitDataList)
        {
            if (entry.data != null)
                UnitDataMap[entry.kind] = entry.data;
            else
                Debug.LogWarning($"[UnitSetting] Kind:{entry.kind} のUnitDataがnullです");
        }
    }

    // ─── 共通生成メソッド ────────────────────────────────────────────

    /// <summary>
    /// ユニットを生成してステータスを即座に適用する。
    /// ゲーム中の新規生成はすべてこのメソッド経由で行う（駒の生成時の適用を担当）。
    /// </summary>
    public GameObject SpawnUnit(GameObject prefab, Vector3 pos,
                                Transform parent, int level = 1)
    {
        var obj = Instantiate(prefab, pos, Quaternion.identity, parent);

        var status = obj.GetComponentInChildren<Status>();
        if (status == null)
        {
            Debug.LogWarning($"[UnitSetting] {prefab.name} にStatusが見つかりません");
            return obj;
        }

        if (UnitDataMap.TryGetValue(status.kind, out UnitData data))
            data.ApplyToStatus(status, level);
        else
            Debug.LogWarning($"[UnitSetting] Kind:{status.kind} のUnitDataが未登録です");

        return obj;
    }

    // ─── ゲーム開始時のユニット配置 ─────────────────────────────────
    public void UnitSet()
    {
        // PCP、ECP、SetPos を取り出す
        CrystalSystem crystalsystem = GetComponent<CrystalSystem>();
        MapCreate mapcreate = GetComponent<MapCreate>();
        Vector3 pcp = crystalsystem.PCP;
        Vector3 ecp = crystalsystem.ECP;
        var setpos = mapcreate.SetPos;

        // 配置位置を絞る（KingPoint：PCP周辺1マス以内）
        var KingPoint = setpos.Where(p =>
        {
            float px = Mathf.Abs(p.x - pcp.x);
            float pz = Mathf.Abs(p.z - pcp.z);
            return px <= 1 && pz <= 1 && p != pcp;
        }).ToList();

        // 配置位置を絞る（StrangePoint：ECP周辺1マス以内）
        var StrangePoint = setpos.Where(p =>
        {
            float px = Mathf.Abs(p.x - ecp.x);
            float pz = Mathf.Abs(p.z - ecp.z);
            return px <= 1 && pz <= 1 && p != ecp;
        }).ToList();

        // Instantiate → SpawnUnit に置き換え（ステータス適用込み）
        Vector3 KP = KingPoint[Random.Range(0, KingPoint.Count)];
        SpawnUnit(KingPiece, KP, PlayerUnit);
        Debug.Log("<color=#ffff00ff>[StartSetting]</color>王設置");

        Vector3 SP = StrangePoint[Random.Range(0, StrangePoint.Count)];
        SpawnUnit(StrangePiece, SP, EnemyUnit);
        Debug.Log("<color=#ffff00ff>[StartSetting]</color>異形の王設置");
    }
}
