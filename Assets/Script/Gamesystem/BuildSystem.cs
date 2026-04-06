using System.Collections.Generic;
using System.Linq;
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
    //  建築モード開始
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

    // ==================================================================
    //  建築モード解除
    // ==================================================================
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

        // SetPos 上の最も近い有効座標に合わせる
        Vector3Int snapped = SnapToSetPos(gridPos);

        bool isSubCrystal = FacilityData.IsSubCrystal(SelectedFacility);

        if (isSubCrystal)
        {
            // サブクリスタル: 領地外でもカーソルが追従する
            // 領地内の場合は領地端にクランプ
            if (IsInTerritory(snapped))
            {
                Vector3Int clamped = ClampToOutsideTerritory(snapped);
                if (clamped.x != int.MinValue)
                    snapped = clamped;
            }
        }
        else
        {
            // 通常建築物: 領地内チェック
            if (!IsInTerritory(snapped))
            {
                Vector3Int clamped = ClampToTerritory(snapped);
                if (clamped.x == int.MinValue)
                {
                    cursor.SetVisible(false);
                    return;
                }
                snapped = clamped;
            }
        }

        // 設置可否の判定 & カーソル更新
        canPlace = CheckCanPlace(snapped);
        cursor.UpdatePosition(snapped, canPlace);
    }

    // ==================================================================
    //  設置試行（左クリック時に呼ばれる）
    // ==================================================================
    public bool TryPlace()
    {
        if (!IsActive) return false;
        if (!canPlace) return false;
        if (!cursor.IsVisible) return false;

        bool isSubCrystal = FacilityData.IsSubCrystal(SelectedFacility);

        if (isSubCrystal)
        {
            // サブクリスタル: 資源チェック（AP0, リソースコスト0, サブクリスタル1消費）
            if (factionState.GetSubCrystals(Team.Player) <= 0)
            {
                Debug.Log("[BuildSystem] サブクリスタル不足: 設置不可");
                return false;
            }

            // 設置実行
            PlaceBuilding(cursor.LastPosition, SelectedFacility);

            // サブクリスタル消費（クールダウンなし、破壊時に5ターン後返却）
            factionState.ModifySubCrystals(Team.Player, -1);

            // 領地拡張
            if (subCrystalSystem != null)
            {
                // 最後に設置した建築物を取得
                var lastBuilding = PlayerBuildingParent.GetChild(PlayerBuildingParent.childCount - 1).gameObject;
                subCrystalSystem.ExpandTerritory(lastBuilding, Team.Player);
            }
        }
        else
        {
            // 通常建築物: AP・リソースの再チェック
            if (!apsystem.CanBuild(Team.Player, SelectedFacility, factionState))
            {
                Debug.Log("[BuildSystem] AP/リソース不足: 設置不可");
                return false;
            }

            // 設置実行
            PlaceBuilding(cursor.LastPosition, SelectedFacility);

            // AP・リソース消費
            apsystem.ConsumeBuild(Team.Player, SelectedFacility, factionState);
        }

        // ---- ML観測: プレイヤーの建築をMLシステムに記録 ----
        if (turnGenerator != null && turnGenerator.Systems.AICommander != null)
        {
            Vector3 buildPos = new Vector3(cursor.LastPosition.x, 0, cursor.LastPosition.z);
            turnGenerator.Systems.AICommander.MLIntegration.ObservePlayerBuild(buildPos, turnGenerator.Context.Turn);
        }

        // 建築モード解除
        CancelBuildMode();
        return true;
    }

    // 設置可否チェック → BuildValidator に委譲
    private bool CheckCanPlace(Vector3Int pos)
        => validator.CheckCanPlace(pos, SelectedFacility, subCrystalSystem, turnGenerator.Systems.CrystalSystem);

    // ==================================================================
    //  内部: 建築物の配置
    // ==================================================================
    private void PlaceBuilding(Vector3Int pos, FacilityKind facility)
    {
        if (!FacilityData.Table.TryGetValue(facility, out var info)) return;

        GameObject building;

        if (prefabMap.TryGetValue(facility, out GameObject prefab) && prefab != null)
        {
            building = Instantiate(prefab, new Vector3(pos.x, pos.y, pos.z),
                                   Quaternion.identity, PlayerBuildingParent);
        }
        else
        {
            // フォールバック: Cube 生成
            building = GameObject.CreatePrimitive(PrimitiveType.Cube);
            building.transform.position = new Vector3(pos.x, pos.y, pos.z);
            building.transform.SetParent(PlayerBuildingParent);
            building.name = facility.ToString();

            // 壁はグレー、サブクリスタルはシアン、その他は茶色系
            var renderer = building.GetComponent<Renderer>();
            if (renderer != null)
            {
                var mat = new Material(Shader.Find("Standard"));
                mat.color = FacilityData.IsWall(facility)
                    ? new Color(0.6f, 0.6f, 0.6f)
                    : FacilityData.IsSubCrystal(facility)
                    ? new Color(0.3f, 0.7f, 0.9f)
                    : new Color(0.6f, 0.4f, 0.2f);
                renderer.material = mat;
            }
        }

        // Status コンポーネントを付与
        var status = building.GetComponent<Status>();
        if (status == null) status = building.AddComponent<Status>();

        status.kind = FacilityData.ToUnitKind(facility);
        status.team = Team.Player;
        status.type = FacilityData.IsWall(facility) ? Type.Wall : Type.Building;
        status.direction = Direction.N;
        status.HP = info.HP;
        status.DEF = info.DEF;
        status.ATK = info.ATK;
        status.Level = 1;
        status.facilityKind = facility;

        // Block レイヤーに設定
        building.layer = LayerMask.NameToLayer("Block");

        // 建築物位置を記録
        buildingPositions.Add(pos);

        // 壁の場合は UnitPointData に追加（全駒通過不可、Y=0で管理）
        if (FacilityData.IsWall(facility))
        {
            moveGenerator.UnitPointData.Add(new Vector3(pos.x, 0f, pos.z));
        }

        Debug.Log($"[BuildSystem] {info.DisplayName} を ({pos.x}, {pos.y}, {pos.z}) に設置");
    }

    // ==================================================================
    //  建築物の強化
    // ==================================================================

    /// <summary>
    /// 指定の建築物を1レベル強化する。
    /// </summary>
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

        // レベルアップ
        target.Level = currentLevel + 1;

        // ステータス更新
        var newData = FacilityData.GetLevel(facility, target.Level);
        target.HP = newData.HP;
        target.DEF = newData.DEF;
        target.ATK = newData.ATK;

        Debug.Log($"[BuildSystem] 強化完了: {facility} Lv{currentLevel} → Lv{target.Level}");
        return true;
    }

    // 領地判定 → BuildValidator に委譲
    private bool IsInTerritory(Vector3Int pos)
        => validator.IsInTerritory(pos);

    // 領地端クランプ → BuildValidator に委譲
    private Vector3Int ClampToTerritory(Vector3Int pos)
        => validator.ClampToTerritory(pos);

    // 領地外クランプ → BuildValidator に委譲
    private Vector3Int ClampToOutsideTerritory(Vector3Int pos)
        => validator.ClampToOutsideTerritory(pos);

    // SetPosスナップ → BuildValidator に委譲
    private Vector3Int SnapToSetPos(Vector3Int gridPos)
        => validator.SnapToSetPos(gridPos);

    // ==================================================================
    //  外部: 建築物位置管理
    // ==================================================================
    public void RemoveBuildingPosition(Vector3Int pos)
    {
        buildingPositions.Remove(pos);
    }

    public bool HasBuildingAt(Vector3Int pos)
    {
        return buildingPositions.Contains(pos);
    }

    // ==================================================================
    //  AI用: カーソル不要の直接建築
    // ==================================================================

    /// <summary>
    /// AI用: 指定位置に建築物を設置する（カーソル・UI不要）
    /// </summary>
    public bool AIPlaceBuilding(Vector3Int pos, FacilityKind facility, Team team)
    {
        if (!FacilityData.Table.TryGetValue(facility, out var info))
        {
            Debug.LogWarning($"[BuildSystem] AIPlaceBuilding失敗: {facility} FacilityDataに未登録");
            return false;
        }

        // AP・リソースチェック
        if (!apsystem.CanBuild(team, facility, factionState))
        {
            Debug.Log($"[BuildSystem] AIPlaceBuilding失敗: {facility} CanBuild=false (AP={apsystem.GetAP(team)} 必要={info.APCost})");
            return false;
        }

        // 設置可否チェック（サブクリスタル or 通常建築）
        bool isSubCrystal = FacilityData.IsSubCrystal(facility);
        if (isSubCrystal)
        {
            if (subCrystalSystem == null) return false;
            if (!subCrystalSystem.CanPlaceSubCrystal(pos, team)) return false;
        }
        else
        {
            if (!AICheckCanPlace(pos, team))
            {
                Debug.Log($"[BuildSystem] AIPlaceBuilding失敗: {facility} @({pos.x},{pos.y},{pos.z}) AICheckCanPlace=false");
                return false;
            }
        }

        // AP・リソース消費
        apsystem.ConsumeBuild(team, facility, factionState);

        // 建築物を配置
        AIPlaceBuildingInternal(pos, facility, team);

        // サブクリスタルの場合は領地拡張
        if (isSubCrystal && subCrystalSystem != null)
        {
            // サブクリスタル在庫を消費
            factionState.ModifySubCrystals(team, -1);

            // 最後に配置されたオブジェクトを取得して領地拡張
            Transform teamParent = GetBuildingParent(team);
            if (teamParent.childCount > 0)
            {
                var lastChild = teamParent.GetChild(teamParent.childCount - 1);
                subCrystalSystem.ExpandTerritory(lastChild.gameObject, team);
            }
        }

        Debug.Log($"[BuildSystem] AI({team}) {info.DisplayName} を ({pos.x},{pos.y},{pos.z}) に設置");
        return true;
    }

    /// <summary>
    /// AI用: 設置可否チェック（任意チーム対応）
    /// </summary>
    private bool AICheckCanPlace(Vector3Int pos, Team team)
    {
        // 領地チェック（チーム別）
        var territory = team == Team.Player ? territorysystem.PTSetPos : territorysystem.ETSetPos;
        if (territory == null)
        {
            Debug.LogWarning($"[BuildSystem] AICheckCanPlace: territory==null (team={team})");
            return false;
        }
        bool inTerritory = territory.Any(p =>
            Mathf.RoundToInt(p.x) == pos.x && Mathf.RoundToInt(p.z) == pos.z);
        if (!inTerritory)
        {
            Debug.Log($"[BuildSystem] AICheckCanPlace: ({pos.x},{pos.z}) は領地外 (領地数={territory.Count})");
            return false;
        }

        // クリスタル位置チェック（XZのみで比較）
        Vector3 pcpVec = turnGenerator.Systems.CrystalSystem.PCP;
        if (Mathf.RoundToInt(pcpVec.x) == pos.x && Mathf.RoundToInt(pcpVec.z) == pos.z)
        {
            Debug.Log($"[BuildSystem] AICheckCanPlace: ({pos.x},{pos.z}) はPlayerクリスタル位置");
            return false;
        }

        Vector3 ecpVec = turnGenerator.Systems.CrystalSystem.ECP;
        if (Mathf.RoundToInt(ecpVec.x) == pos.x && Mathf.RoundToInt(ecpVec.z) == pos.z)
        {
            Debug.Log($"[BuildSystem] AICheckCanPlace: ({pos.x},{pos.z}) はEnemyクリスタル位置");
            return false;
        }

        // 建物位置チェック（XZのみで比較）
        bool hasBuildingXZ = buildingPositions.Any(bp => bp.x == pos.x && bp.z == pos.z);
        if (hasBuildingXZ)
        {
            Debug.Log($"[BuildSystem] AICheckCanPlace: ({pos.x},{pos.z}) に既存建物あり");
            return false;
        }

        return true;
    }

    // 最後にAIが配置した建物（SubCrystal領地拡張用）
    private GameObject _lastPlacedBuilding;

    /// <summary>AIが最後に配置した建物を取得</summary>
    public GameObject GetLastPlacedBuilding() => _lastPlacedBuilding;

    /// <summary>
    /// AI用: 実際の配置処理（任意チーム対応）
    /// SetPosから正しいY座標を取得して配置する
    /// </summary>
    private void AIPlaceBuildingInternal(Vector3Int pos, FacilityKind facility, Team team)
    {
        if (!FacilityData.Table.TryGetValue(facility, out var info)) return;

        // SetPosから正しいY座標を取得（SetPos.y = 地形高さ+1 = ユニット配置高さ）
        float placeY = pos.y;
        foreach (var sp in mapcreate.SetPos)
        {
            if (Mathf.RoundToInt(sp.x) == pos.x && Mathf.RoundToInt(sp.z) == pos.z)
            {
                placeY = sp.y;
                break;
            }
        }

        Transform parent = GetBuildingParent(team);
        GameObject building;

        if (prefabMap != null && prefabMap.TryGetValue(facility, out GameObject prefab) && prefab != null)
        {
            building = Instantiate(prefab, new Vector3(pos.x, placeY, pos.z),
                                   Quaternion.identity, parent);
        }
        else
        {
            building = GameObject.CreatePrimitive(PrimitiveType.Cube);
            building.transform.position = new Vector3(pos.x, placeY, pos.z);
            building.transform.SetParent(parent);
            building.name = facility.ToString();

            var renderer = building.GetComponent<Renderer>();
            if (renderer != null)
            {
                var mat = new Material(Shader.Find("Standard"));
                mat.color = FacilityData.IsWall(facility)
                    ? new Color(0.6f, 0.6f, 0.6f)
                    : FacilityData.IsSubCrystal(facility)
                    ? new Color(0.3f, 0.7f, 0.9f)
                    : new Color(0.6f, 0.4f, 0.2f);
                renderer.material = mat;
            }
        }

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

        building.layer = LayerMask.NameToLayer("Block");

        buildingPositions.Add(pos);
        _lastPlacedBuilding = building;

        if (FacilityData.IsWall(facility))
        {
            moveGenerator.UnitPointData.Add(new Vector3(pos.x, 0f, pos.z));
        }
    }

    /// <summary>
    /// AI用: 指定チームの領地内で建築可能な位置一覧を返す
    /// SetPosからY座標を取得して正しい高さで返す
    /// </summary>
    public List<Vector3Int> AIGetBuildablePositions(Team team)
    {
        var result = new List<Vector3Int>();
        var territory = team == Team.Player ? territorysystem.PTSetPos : territorysystem.ETSetPos;
        if (territory == null) return result;

        // SetPosをXZ→Yのルックアップ用に変換
        var setposLookup = new Dictionary<(int, int), int>();
        foreach (var sp in mapcreate.SetPos)
        {
            var key = (Mathf.RoundToInt(sp.x), Mathf.RoundToInt(sp.z));
            if (!setposLookup.ContainsKey(key))
                setposLookup[key] = Mathf.RoundToInt(sp.y);
        }

        foreach (var p in territory)
        {
            int px = Mathf.RoundToInt(p.x);
            int pz = Mathf.RoundToInt(p.z);
            // SetPosのY座標を使う
            if (!setposLookup.TryGetValue((px, pz), out int py)) continue;
            var pos = new Vector3Int(px, py, pz);
            if (AICheckCanPlace(pos, team))
                result.Add(pos);
        }
        return result;
    }
}
