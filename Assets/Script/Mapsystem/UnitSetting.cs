using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class UnitSetting : MonoBehaviour
{
    [Header("キング")]
    [SerializeField] GameObject KingPiece;
    [Header("異形")]
    [SerializeField] GameObject StrangePiece;

    [Header("初期配置ユニット（両陣営共通プレハブ）")]
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
        var usedPlayer = new HashSet<Vector3>();
        var usedEnemy = new HashSet<Vector3>();

        Vector3 KP = KingPoint[Random.Range(0, KingPoint.Count)];
        SpawnUnit(KingPiece, KP, PlayerUnit);
        usedPlayer.Add(KP);
        Debug.Log("<color=#ffff00ff>[StartSetting]</color>王設置");

        Vector3 SP = StrangePoint[Random.Range(0, StrangePoint.Count)];
        SpawnUnit(StrangePiece, SP, EnemyUnit);
        usedEnemy.Add(SP);
        Debug.Log("<color=#ffff00ff>[StartSetting]</color>異形の王設置");

        // 初期配置：ナイト・アーチャー・斥候を両陣営に召喚（領土内5マス以内）
        SpawnInitialSquad(setpos, pcp, PlayerUnit, Team.Player, usedPlayer);
        SpawnInitialSquad(setpos, ecp, EnemyUnit, Team.Enemy, usedEnemy);
    }

    /// <summary>
    /// 指定陣営のクリスタル周辺（Chebyshev距離5以内）にナイト/アーチャー/斥候を召喚する。
    /// 指揮官の位置と被らないように usedPositions で重複を避ける。
    /// </summary>
    private void SpawnInitialSquad(List<Vector3> setpos, Vector3 crystalPos,
                                   Transform parent, Team team,
                                   HashSet<Vector3> usedPositions)
    {
        // 領土範囲（Chebyshev距離5）内の配置候補
        var candidates = setpos.Where(p =>
        {
            float dx = Mathf.Abs(p.x - crystalPos.x);
            float dz = Mathf.Abs(p.z - crystalPos.z);
            return dx <= 5 && dz <= 5 && p != crystalPos && !usedPositions.Contains(p);
        }).ToList();

        var pieces = new (GameObject prefab, Kind kind)[]
        {
            (KnightPiece, Kind.Knight),
            (ArcherPiece, Kind.Archer),
            (ScoutPiece,  Kind.Scout),
        };

        // 敵陣営が北を向くよう調整（Player=N, Enemy=S）
        Direction dir = team == Team.Player ? Direction.N : Direction.S;

        foreach (var (prefab, kind) in pieces)
        {
            if (prefab == null)
            {
                Debug.LogWarning($"[UnitSetting] {kind} のプレハブが未割当のためスキップ");
                continue;
            }
            if (candidates.Count == 0)
            {
                Debug.LogWarning($"[UnitSetting] {team} の {kind} 配置位置が不足しています");
                break;
            }

            int idx = Random.Range(0, candidates.Count);
            Vector3 pos = candidates[idx];
            candidates.RemoveAt(idx);
            usedPositions.Add(pos);

            var obj = SpawnUnit(prefab, pos, parent);
            var status = obj.GetComponentInChildren<Status>();
            if (status != null)
            {
                status.team = team;
                status.direction = dir;
            }
            Debug.Log($"<color=#ffff00ff>[StartSetting]</color>{team} {kind} 設置 @({pos.x},{pos.z})");
        }
    }
}
