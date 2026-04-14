using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class UnitSetting : MonoBehaviour
{
    [Header("キング")]
    [SerializeField] GameObject KingPiece;
    [Header("異形")]
    [SerializeField] GameObject StrangePiece;

    [Header("初期ユニット（未設定ならフォールバック生成）")]
    [SerializeField] GameObject KnightPiece;
    [SerializeField] GameObject ArcherPiece;
    [SerializeField] GameObject ScoutPiece;

    [Header("ユニット配置親オブジェクト")]
    public Transform PlayerUnit;
    public Transform EnemyUnit;

    // ==== UnitData 管理（Dictionary 版） ====
    // SerializeField が11個並べない理由：
    // 新しい Kind を追加するたびにフィールド追加とInspector設定の2往復が必要になる。
    // Dictionary ならリストに1エントリ追加するだけで済む（設計原則2）。
    [System.Serializable]
    public class UnitDataEntry
    {
        public Kind kind;
        public UnitData data;
    }

    [Header("ユニットデータ（Kind別）")]
    [SerializeField] private List<UnitDataEntry> _unitDataList;

    // 外部からの読み取り用（GameGenerator・BattleSystem・PlayerSummon が参照）
    public Dictionary<Kind, UnitData> UnitDataMap { get; private set; }

    // ==== 初期化 ====
    private void Awake()
    {
        UnitDataMap = new Dictionary<Kind, UnitData>();

        // Inspector で設定された ScriptableObject を登録
        if (_unitDataList != null)
        {
            foreach (var entry in _unitDataList)
            {
                if (entry.data != null)
                    UnitDataMap[entry.kind] = entry.data;
            }
        }

        // UnitStaticData に定義があるが UnitDataMap に未登録の Kind を自動補完
        foreach (var kvp in UnitStaticData.Table)
        {
            if (!UnitDataMap.ContainsKey(kvp.Key))
            {
                UnitDataMap[kvp.Key] = UnitStaticData.CreateUnitData(kvp.Key);
            }
        }
    }

    // ==== 共通生成メソッド ====

    /// <summary>
    /// ユニットを生成してステータスを即座に適用する。
    /// ゲーム中の新規生成はすべてこのメソッド経由で行う（駒の生成と適用を担保）。
    /// </summary>
    public GameObject SpawnUnit(GameObject prefab, Vector3 pos,
                                Transform parent, int level = 1)
    {
        var obj = Instantiate(prefab, pos, Quaternion.identity, parent);

        var status = obj.GetComponentInChildren<Status>();
        if (status == null)
        {
            Debug.LogWarning($"[UnitSetting] {prefab.name} にStatusがありません");
            return obj;
        }

        if (UnitDataMap.TryGetValue(status.kind, out UnitData data))
            data.ApplyToStatus(status, level);
        else
            Debug.LogWarning($"[UnitSetting] Kind:{status.kind} のUnitDataが未登録です");

        // スキルをランダム配布
        SkillData.AssignRandomSkill(status);

        // Special Ability をランダム配布
        SpecialAbilityData.AssignRandom(status);

        // 頭上UI（Lv + HP）をアタッチ
        UnitHeadUI.Attach(obj);

        return obj;
    }

    // ==== ゲーム開始時のユニット配置 ====
    public void UnitSet()
    {
        // PCP、ECP、SetPos を取り出す
        CrystalSystem crystalsystem = GetComponent<CrystalSystem>();
        MapCreate mapcreate = GetComponent<MapCreate>();
        Vector3 pcp = crystalsystem.PCP;
        Vector3 ecp = crystalsystem.ECP;
        var setpos = mapcreate.SetPos;

        // 配置位置絞り込み（KingPoint：PCPから1マス以内）
        var KingPoint = setpos.Where(p =>
        {
            float px = Mathf.Abs(p.x - pcp.x);
            float pz = Mathf.Abs(p.z - pcp.z);
            return px <= 1 && pz <= 1 && p != pcp;
        }).ToList();

        // 配置位置絞り込み（StrangePoint：ECPから1マス以内）
        var StrangePoint = setpos.Where(p =>
        {
            float px = Mathf.Abs(p.x - ecp.x);
            float pz = Mathf.Abs(p.z - ecp.z);
            return px <= 1 && pz <= 1 && p != ecp;
        }).ToList();

        // Instantiate を SpawnUnit に置き換え（ステータス適用済み）
        Vector3 KP = KingPoint[Random.Range(0, KingPoint.Count)];
        SpawnUnit(KingPiece, KP, PlayerUnit);
        Debug.Log("<color=#ffff00ff>[StartSetting]</color>王設置");

        Vector3 SP = StrangePoint[Random.Range(0, StrangePoint.Count)];
        SpawnUnit(StrangePiece, SP, EnemyUnit);
        Debug.Log("<color=#ffff00ff>[StartSetting]</color>異形の王設置");

        // ==== 初期ユニット（騎士・弓・斥候）を領土内にランダム配置 ====
        const int territoryRadius = 5;
        var initialUnits = new (GameObject prefab, Kind kind)[]
        {
            (KnightPiece, Kind.Knight),
            (ArcherPiece, Kind.Archer),
            (ScoutPiece, Kind.Scout),
        };

        // プレイヤー側
        SpawnInitialUnits(initialUnits, pcp, KP, setpos, PlayerUnit, Team.Player, territoryRadius);
        // 敵側
        SpawnInitialUnits(initialUnits, ecp, SP, setpos, EnemyUnit, Team.Enemy, territoryRadius);
    }

    /// <summary>
    /// 初期ユニットを領土範囲内のランダム位置に配置する。
    /// </summary>
    private void SpawnInitialUnits(
        (GameObject prefab, Kind kind)[] units,
        Vector3 crystalPos, Vector3 kingPos,
        List<Vector3> setpos, Transform parent, Team team, int radius)
    {
        // 領土範囲内の候補位置を取得（クリスタル・キング位置を除外）
        var candidates = setpos.Where(p =>
        {
            float dx = Mathf.Abs(p.x - crystalPos.x);
            float dz = Mathf.Abs(p.z - crystalPos.z);
            return dx <= radius && dz <= radius && p != crystalPos && p != kingPos;
        }).ToList();

        var usedPositions = new List<Vector3>();
        Direction dir = team == Team.Player ? Direction.N : Direction.S;

        foreach (var (prefab, kind) in units)
        {
            // 使用済み位置を除外
            var available = candidates.Where(p => !usedPositions.Contains(p)).ToList();
            if (available.Count == 0)
            {
                Debug.LogWarning($"[UnitSetting] {kind} の配置候補がありません");
                continue;
            }

            Vector3 pos = available[Random.Range(0, available.Count)];
            usedPositions.Add(pos);

            if (prefab != null)
            {
                var obj = SpawnUnit(prefab, pos, parent);
                var status = obj.GetComponentInChildren<Status>();
                if (status != null)
                {
                    status.team = team;
                    status.direction = dir;
                }
            }
            else
            {
                // フォールバック: プレハブ未設定時はプリミティブで生成
                CreateFallbackUnit(pos, kind, team, dir, parent);
            }

            Debug.Log($"<color=#ffff00ff>[StartSetting]</color>{kind} 配置 ({team})");
        }
    }

    /// <summary>
    /// プレハブ未割当時のフォールバックユニットを生成する。
    /// </summary>
    private void CreateFallbackUnit(Vector3 pos, Kind kind, Team team,
                                     Direction dir, Transform parent)
    {
        var obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        obj.transform.position = pos;
        obj.transform.SetParent(parent);
        obj.name = kind.ToString();

        var renderer = obj.GetComponent<Renderer>();
        if (renderer != null)
        {
            var mat = new Material(Shader.Find("Standard"));
            mat.color = BrandGuide.GetUnitFallbackColor(team);
            renderer.material = mat;
        }

        var status = obj.AddComponent<Status>();
        status.kind = kind;
        status.team = team;
        status.type = Type.Unit;
        status.direction = dir;

        if (UnitDataMap.TryGetValue(kind, out UnitData data))
            data.ApplyToStatus(status, 1);

        SkillData.AssignRandomSkill(status);
        SpecialAbilityData.AssignRandom(status);
        UnitHeadUI.Attach(obj);
    }
}
