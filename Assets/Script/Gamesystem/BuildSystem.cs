using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 建築システム: カーソル追従・設置可否判定・建築物の設置を管理する。
/// </summary>
public class BuildSystem : MonoBehaviour
{
    // ---- 外部参照（Init で注入） ----
    private TurnGenerator turnGenerator;
    private TerritorySystem territorysystem;
    private APSystem apsystem;
    private FactionState factionState;
    private MoveGenerator moveGenerator;
    private MapCreate mapcreate;

    // ---- サブクリスタルシステム（後から注入） ----
    public SubCrystalSystem subCrystalSystem;

    // ---- プレハブ（Inspector で割り当て。未割当時は Cube フォールバック） ----
    [Header("建築物プレハブ（Inspector割当）")]
    [SerializeField] private SerializableFacilityPrefab[] facilityPrefabs;

    [System.Serializable]
    public struct SerializableFacilityPrefab
    {
        public FacilityKind kind;
        public GameObject prefab;
    }

    private Dictionary<FacilityKind, GameObject> prefabMap;

    // ---- 建築モード状態 ----
    public bool IsActive { get; private set; }
    public FacilityKind SelectedFacility { get; private set; }

    // ---- カーソル（BuildCursorController に委譲） ----
    private BuildCursorController cursor;
    private bool canPlace;

    // ---- 建築物の親（チーム別） ----
    [Header("建築物の親Transform（チーム別）")]
    public Transform PlayerBuildingParent;
    public Transform EnemyBuildingParent;

    /// <summary>指定チームの建築物親を返す</summary>
    public Transform GetBuildingParent(Team team)
        => team == Team.Player ? PlayerBuildingParent : EnemyBuildingParent;

    /// <summary>指定チーム・施設種別の設置済み数を返す</summary>
    public int GetBuildingCount(Team team, FacilityKind kind)
    {
        var parent = GetBuildingParent(team);
        if (parent == null) return 0;
        int count = 0;
        for (int i = 0; i < parent.childCount; i++)
        {
            var s = parent.GetChild(i).GetComponent<Status>();
            if (s != null && s.facilityKind == kind) count++;
        }
        return count;
    }

    // ---- 設置済み建築物の位置管理 ----
    private HashSet<Vector3Int> buildingPositions = new HashSet<Vector3Int>();

    // ---- 検証ロジック（BuildValidatorに委譲） ----
    private BuildValidator validator;

    // ==================================================================
    //  初期化
    // ==================================================================
    public void Init(TurnGenerator turnGenerator, TerritorySystem territorysystem,
                     APSystem apsystem, FactionState factionState,
                     MoveGenerator moveGenerator, MapCreate mapcreate)
    {
        this.turnGenerator = turnGenerator;
        this.territorysystem = territorysystem;
        this.apsystem = apsystem;
        this.factionState = factionState;
        this.moveGenerator = moveGenerator;
        this.mapcreate = mapcreate;

        // プレハブマップ構築
        prefabMap = new Dictionary<FacilityKind, GameObject>();
        if (facilityPrefabs != null)
        {
            foreach (var entry in facilityPrefabs)
            {
                if (entry.prefab != null)
                    prefabMap[entry.kind] = entry.prefab;
            }
        }

        // BuildValidator初期化
        validator = new BuildValidator(territorysystem, mapcreate, moveGenerator, buildingPositions);

        // カーソルコントローラー初期化
        cursor = new BuildCursorController();

        // 建築物の親が無ければ作成（チーム別）
        if (PlayerBuildingParent == null)
        {
            var go = new GameObject("PlayerBuildings");
            PlayerBuildingParent = go.transform;
        }
        if (EnemyBuildingParent == null)
        {
            var go = new GameObject("EnemyBuildings");
            EnemyBuildingParent = go.transform;
        }
    }

    // ==================================================================
    //  建築モード開始 / 解除
    // ==================================================================
    public void StartBuildMode(FacilityKind facility)
    {
        if (IsActive) CancelBuildMode();

        SelectedFacility = facility;
        IsActive = true;
        canPlace = false;

        cursor.Create();
        Debug.Log($"[BuildSystem] 建築モード開始: {facility}");
    }

    public void CancelBuildMode()
    {
        IsActive = false;
        cursor.Destroy();
        Debug.Log("[BuildSystem] 建築モード解除");
    }

    // ==================================================================
    //  カーソル更新（PlayerMove.Update から毎フレーム呼ばれる）
    // ==================================================================
    public void UpdateCursor()
    {
        if (!IsActive) return;

        if (!cursor.TryGetGridPosition(out Vector3Int gridPos))
        {
            cursor.SetVisible(false);
            return;
        }

        Vector3Int snapped = mapcreate.SnapToSetPos(gridPos);
        bool isSubCrystal = FacilityData.IsSubCrystal(SelectedFacility);

        if (isSubCrystal)
        {
            // サブクリスタル: 領地内の場合は領地外にクランプ
            if (territorysystem.IsInTerritory(snapped, Team.Player))
            {
                Vector3Int clamped = validator.ClampToOutsideTerritory(snapped);
                if (clamped.x != int.MinValue)
                    snapped = clamped;
            }
        }
        else
        {
            // 通常建築物: 領地内チェック
            if (!territorysystem.IsInTerritory(snapped, Team.Player))
            {
                Vector3Int clamped = territorysystem.ClampToTerritory(snapped, Team.Player);
                if (clamped.x == int.MinValue)
                {
                    cursor.SetVisible(false);
                    return;
                }
                snapped = clamped;
            }
        }

        canPlace = CheckCanPlace(snapped);
        cursor.UpdatePosition(snapped, canPlace);
    }

    // ==================================================================
    //  設置試行（左クリック時に呼ばれる）
    // ==================================================================
    public bool TryPlace()
    {
        if (!IsActive || !canPlace || !cursor.IsVisible) return false;

        bool isSubCrystal = FacilityData.IsSubCrystal(SelectedFacility);

        if (isSubCrystal)
        {
            if (factionState.GetSubCrystals(Team.Player) <= 0)
            {
                Debug.Log("[BuildSystem] サブクリスタル不足: 設置不可");
                return false;
            }

            InstantiateBuilding(cursor.LastPosition, SelectedFacility, Team.Player);
            factionState.ModifySubCrystals(Team.Player, -1);

            if (subCrystalSystem != null)
            {
                var lastBuilding = PlayerBuildingParent.GetChild(PlayerBuildingParent.childCount - 1).gameObject;
                subCrystalSystem.ExpandTerritory(lastBuilding, Team.Player);
            }
        }
        else
        {
            if (!apsystem.CanBuild(Team.Player, SelectedFacility, factionState))
            {
                Debug.Log("[BuildSystem] AP/リソース不足: 設置不可");
                return false;
            }

            InstantiateBuilding(cursor.LastPosition, SelectedFacility, Team.Player);
            apsystem.ConsumeBuild(Team.Player, SelectedFacility, factionState);
        }

        // ML観測: プレイヤーの建築をMLシステムに記録
        NotifyMLObservation(cursor.LastPosition);

        CancelBuildMode();
        return true;
    }

    // 設置可否チェック → BuildValidator に委譲
    private bool CheckCanPlace(Vector3Int pos)
        => validator.CheckCanPlace(pos, SelectedFacility, subCrystalSystem, turnGenerator.Systems.CrystalSystem);

    // ==================================================================
    //  建築物のインスタンス生成（共通処理）
    // ==================================================================

    /// <summary>
    /// 建築物を指定位置に生成する。プレイヤー・AI共通のコアロジック。
    /// SetPos から正しい Y 座標を取得して配置する。
    /// </summary>
    private GameObject InstantiateBuilding(Vector3Int pos, FacilityKind facility, Team team)
    {
        if (!FacilityData.Table.TryGetValue(facility, out var info)) return null;

        // SetPos から正しい Y 座標を取得
        float placeY = pos.y;
        if (mapcreate.TryGetHeight(pos.x, pos.z, out float height))
            placeY = height;

        Vector3 worldPos = new Vector3(pos.x, placeY, pos.z);
        Transform parent = GetBuildingParent(team);

        // プレハブ or フォールバック生成
        GameObject building;
        if (prefabMap != null && prefabMap.TryGetValue(facility, out GameObject prefab) && prefab != null)
        {
            building = Instantiate(prefab, worldPos, Quaternion.identity, parent);
        }
        else
        {
            building = CreateFallbackBuilding(worldPos, facility, parent);
        }

        // Status コンポーネントを設定
        ConfigureBuildingStatus(building, facility, team, info);

        // Block レイヤーに設定
        building.layer = LayerMask.NameToLayer("Block");

        // 建築物位置を記録
        buildingPositions.Add(pos);

        // 壁の場合は UnitPointData に追加（全駒通過不可）
        if (FacilityData.IsWall(facility))
        {
            moveGenerator.UnitPointData.Add(GridHelper.ToUnitPoint(pos));
        }

        Debug.Log($"[BuildSystem] {info.DisplayName} を ({pos.x}, {Mathf.RoundToInt(placeY)}, {pos.z}) に設置 ({team})");
        return building;
    }

    /// <summary>プレハブ未割当時のフォールバック建築物を生成する</summary>
    private static GameObject CreateFallbackBuilding(Vector3 worldPos, FacilityKind facility, Transform parent)
    {
        var building = GameObject.CreatePrimitive(PrimitiveType.Cube);
        building.transform.position = worldPos;
        building.transform.SetParent(parent);
        building.name = facility.ToString();

        var renderer = building.GetComponent<Renderer>();
        if (renderer != null)
        {
            var mat = new Material(Shader.Find("Standard"));
            mat.color = BrandGuide.GetFacilityFallbackColor(facility);
            renderer.material = mat;
        }
        return building;
    }

    /// <summary>建築物の Status コンポーネントを設定する</summary>
    private static void ConfigureBuildingStatus(GameObject building, FacilityKind facility,
                                                 Team team, FacilityInfo info)
    {
        var status = building.GetComponent<Status>();
        if (status == null) status = building.AddComponent<Status>();

        status.kind = FacilityData.ToUnitKind(facility);
        status.team = team;
        status.type = FacilityData.IsWall(facility) ? Type.Wall : Type.Building;
        status.direction = team == Team.Player ? Direction.N : Direction.S;
        status.HP = info.HP;
        status.DEF = info.DEF;
        status.ATK = info.ATK;
        status.Level = 1;
        status.facilityKind = facility;
    }

    // ==================================================================
    //  建築物の強化
    // ==================================================================
    public bool TryUpgrade(Status target)
    {
        if (target == null) return false;
        if (target.type != Type.Building && target.type != Type.Wall) return false;

        Team team = target.team;
        if (team != Team.Player && team != Team.Enemy) return false;

        var facility = target.facilityKind;
        int currentLevel = Mathf.Max(1, target.Level);
        int maxLevel = FacilityData.GetMaxLevel(facility);
        if (currentLevel >= maxLevel) return false;

        var res = team == Team.Player ? factionState.PlayerResources : factionState.EnemyResources;
        int currentAP = factionState.GetAP(team);
        if (!FacilityData.CanUpgrade(res, currentAP, facility, currentLevel))
        {
            Debug.Log($"[BuildSystem] 強化不可: {facility} Lv{currentLevel} → Lv{currentLevel + 1}");
            return false;
        }

        // コスト消費
        var apData = team == Team.Player ? factionState.PlayerAP : factionState.EnemyAP;
        FacilityData.ConsumeUpgrade(res, apData, facility, currentLevel + 1);

        // レベルアップ & ステータス更新
        target.Level = currentLevel + 1;
        var newData = FacilityData.GetLevel(facility, target.Level);
        target.HP = newData.HP;
        target.DEF = newData.DEF;
        target.ATK = newData.ATK;

        Debug.Log($"[BuildSystem] 強化完了: {facility} Lv{currentLevel} → Lv{target.Level}");
        return true;
    }

    // ==================================================================
    //  建築物位置管理
    // ==================================================================
    public void RemoveBuildingPosition(Vector3Int pos) => buildingPositions.Remove(pos);
    public bool HasBuildingAt(Vector3Int pos) => buildingPositions.Contains(pos);

    // ==================================================================
    //  AI用: カーソル不要の直接建築
    // ==================================================================

    /// <summary>AI用: 指定位置に建築物を設置する（カーソル・UI不要）</summary>
    public bool AIPlaceBuilding(Vector3Int pos, FacilityKind facility, Team team)
    {
        if (!FacilityData.Table.TryGetValue(facility, out var info))
        {
            Debug.LogWarning($"[BuildSystem] AIPlaceBuilding失敗: {facility} FacilityDataに未登録");
            return false;
        }

        if (!apsystem.CanBuild(team, facility, factionState))
        {
            Debug.Log($"[BuildSystem] AIPlaceBuilding失敗: {facility} CanBuild=false (AP={apsystem.GetAP(team)} 必要={info.APCost})");
            return false;
        }

        bool isSubCrystal = FacilityData.IsSubCrystal(facility);
        if (isSubCrystal)
        {
            if (subCrystalSystem == null) return false;
            if (!subCrystalSystem.CanPlaceSubCrystal(pos, team)) return false;
        }
        else
        {
            if (!AICheckCanPlace(pos, team)) return false;
        }

        // AP・リソース消費
        apsystem.ConsumeBuild(team, facility, factionState);

        // 建築物を配置（共通処理）
        var building = InstantiateBuilding(pos, facility, team);
        _lastPlacedBuilding = building;

        // サブクリスタルの場合は領地拡張
        if (isSubCrystal && subCrystalSystem != null)
        {
            factionState.ModifySubCrystals(team, -1);
            if (building != null)
                subCrystalSystem.ExpandTerritory(building, team);
        }

        Debug.Log($"[BuildSystem] AI({team}) {info.DisplayName} を ({pos.x},{pos.y},{pos.z}) に設置");
        return true;
    }

    /// <summary>AI用: 設置可否チェック（任意チーム対応）</summary>
    private bool AICheckCanPlace(Vector3Int pos, Team team)
    {
        // 領地チェック
        if (!territorysystem.IsInTerritory(pos, team))
        {
            Debug.Log($"[BuildSystem] AICheckCanPlace: ({pos.x},{pos.z}) は領地外");
            return false;
        }

        // クリスタル位置チェック
        if (IsCrystalPosition(pos))
        {
            Debug.Log($"[BuildSystem] AICheckCanPlace: ({pos.x},{pos.z}) はクリスタル位置");
            return false;
        }

        // 建物位置チェック
        if (HasBuildingAtXZ(pos))
        {
            Debug.Log($"[BuildSystem] AICheckCanPlace: ({pos.x},{pos.z}) に既存建物あり");
            return false;
        }

        return true;
    }

    // 最後にAIが配置した建物（SubCrystal領地拡張用）
    private GameObject _lastPlacedBuilding;
    public GameObject GetLastPlacedBuilding() => _lastPlacedBuilding;

    /// <summary>AI用: 指定チームの領地内で建築可能な位置一覧を返す</summary>
    public List<Vector3Int> AIGetBuildablePositions(Team team)
    {
        var result = new List<Vector3Int>();
        var territory = territorysystem.GetTerritory(team);
        if (territory == null) return result;

        var heightLookup = mapcreate.BuildHeightLookup();

        foreach (var p in territory)
        {
            int px = Mathf.RoundToInt(p.x);
            int pz = Mathf.RoundToInt(p.z);
            if (!heightLookup.TryGetValue((px, pz), out int py)) continue;
            var pos = new Vector3Int(px, py, pz);
            if (AICheckCanPlace(pos, team))
                result.Add(pos);
        }
        return result;
    }

    // ==================================================================
    //  内部ヘルパー
    // ==================================================================

    /// <summary>指定座標がクリスタル位置かどうかを判定する</summary>
    private bool IsCrystalPosition(Vector3Int pos)
    {
        var crystalSystem = turnGenerator.Systems.CrystalSystem;
        return GridHelper.MatchXZ(crystalSystem.PCP, pos)
            || GridHelper.MatchXZ(crystalSystem.ECP, pos);
    }

    /// <summary>指定座標に既存建築物があるかを判定する（XZ比較）</summary>
    private bool HasBuildingAtXZ(Vector3Int pos)
    {
        foreach (var bp in buildingPositions)
        {
            if (bp.x == pos.x && bp.z == pos.z) return true;
        }
        return false;
    }

    /// <summary>ML観測: プレイヤーの建築をMLシステムに記録</summary>
    private void NotifyMLObservation(Vector3Int pos)
    {
        if (turnGenerator != null && turnGenerator.Systems.AICommander != null)
        {
            Vector3 buildPos = new Vector3(pos.x, 0, pos.z);
            turnGenerator.Systems.AICommander.MLIntegration.ObservePlayerBuild(buildPos, turnGenerator.Context.Turn);
        }
    }
}
