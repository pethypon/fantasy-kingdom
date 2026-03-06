using System.Collections.Generic;
using UnityEngine;

public class GameGenerater : MonoBehaviour
{
    [SerializeField] MapCreate _MapCreate;
    [SerializeField] CrystalSystem _CrystalSystem;
    [SerializeField] TerritorySystem _TerritorySystem;
    [SerializeField] UnitSetting _UnitSetting;
    [SerializeField] MoveGererater _MoveGenerater;
    [SerializeField] VisionGenerater _VisionGenerater;
    [SerializeField] APSystem _APSystem;
    [SerializeField] TurnGenerater _TurnGenerater;

    [Header("Crystal 親オブジェクト")]
    [SerializeField] Transform _PlayerCrystal;
    [SerializeField] Transform _EnemyCrystal;

    // 収集した Crystal 子（他スクリプトから参照可）
    [HideInInspector] public List<GameObject> PlayerCrystalChildren = new List<GameObject>();
    [HideInInspector] public List<GameObject> EnemyCrystalChildren = new List<GameObject>();

    void Awake()
    {
        // ── 地形・クリスタル生成 ─────────────────────────────────────
        _MapCreate.noisegenerater();
        _MapCreate.BuildTop();
        _CrystalSystem.CrystalCore();

        // ── ユニット配置（SpawnUnit 内で UnitData を適用） ────────────
        _UnitSetting.UnitSet();

        // ── ゲーム開始時：シーン上の全ユニットに UnitData を適用 ──────
        // UnitSet() で Awake が間に合わないケースや Prefab 直置きに対応するため
        // SpawnUnit() が担当できていない駒もここで補完する
        ApplyAllUnitData(_UnitSetting.PlayerUnit);
        ApplyAllUnitData(_UnitSetting.EnemyUnit);

        // ── 領地・移動・視界の構築 ───────────────────────────────────
        // ApplyAllUnitData の後に実行する理由：
        // Territory・Move・Vision の計算はステータス適用後に行うため
        _TerritorySystem.Territory();
        _MoveGenerater.UnitPointCore();
        _VisionGenerater.VisionPoint(_MapCreate, _MoveGenerater, _CrystalSystem);

        // ── Crystal 子オブジェクトを収集 ─────────────────────────────
        CollectChildren(_PlayerCrystal, PlayerCrystalChildren);
        CollectChildren(_EnemyCrystal, EnemyCrystalChildren);

        // ── FactionState を APSystem に注入 ──────────────────────────
        FactionState factionState = _PlayerCrystal.GetComponentInChildren<FactionState>();
        if (factionState == null)
            Debug.LogError("[GameGenerater] FactionState が PlayerCrystal の子に見つかりません");
        _APSystem.Init(factionState);

        // ── 初期資源設定（GameReference 準拠） ───────────────────────
        if (factionState != null)
            InitResources(factionState.PlayerResources);

        // ── ターン開始 ───────────────────────────────────────────────
        _TurnGenerater.StartFirstTurn();
    }

    // ─────────────────────────────────────────────────────────────────
    //  ゲーム開始時の全ユニット適用
    //  ゲーム中の生成は UnitSetting.SpawnUnit() が担当するため、
    //  ここでは「開始時点でシーン上に存在する駒」だけを対象にする
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 指定の親オブジェクト配下の全ユニットに UnitData を適用する。
    /// ゲーム開始時の一括適用を担当（生成時は UnitSetting.SpawnUnit() が担当）。
    /// </summary>
    private void ApplyAllUnitData(Transform unitParent)
    {
        foreach (Status status in unitParent.GetComponentsInChildren<Status>())
        {
            // MovePoint 等のユニット以外は除外
            if (status.type != Type.Unit) continue;

            if (_UnitSetting.UnitDataMap.TryGetValue(status.kind, out UnitData data))
                data.ApplyToStatus(status, status.Level);  // Lv はデフォルト Lv1
            else
                Debug.LogWarning($"[GameGenerater] Kind:{status.kind} のUnitDataが未登録です");
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  初期資源設定
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 初期配布資源をセットする（GameReference 準拠）。
    /// 数値はマジックナンバーにしない（設計原則5）。
    /// </summary>
    private void InitResources(FactionState.ResourceData res)
    {
        const int InitWood = 100;
        const int InitStone = 100;
        const int InitWater = 50;
        const int InitPlank = 50;
        const int InitCutStone = 50;
        const int InitBread = 60;
        const int InitCitizen = 5;

        res.Wood = InitWood;
        res.Stone = InitStone;
        res.Water = InitWater;
        res.Plank = InitPlank;
        res.CutStone = InitCutStone;
        res.Bread = InitBread;
        res.Citizen = InitCitizen;
    }

    // ─────────────────────────────────────────────────────────────────
    //  ユーティリティ
    // ─────────────────────────────────────────────────────────────────
    private void CollectChildren(Transform parent, List<GameObject> result)
    {
        result.Clear();
        foreach (Transform child in parent)
            result.Add(child.gameObject);
    }
}
