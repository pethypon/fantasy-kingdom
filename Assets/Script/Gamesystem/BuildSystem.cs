using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 建築システム: カーソル追従・設置可否判定・建築物の設置を管理する。
/// </summary>
public class BuildSystem : MonoBehaviour
{
    // ---- 外部参照（Init で注入） ----
    private TurnGenerater turngenerater;
    private TerritorySystem territorysystem;
    private APSystem apsystem;
    private FactionState factionState;
    private MoveGererater movegenerater;
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

    // ---- カーソル ----
    private GameObject cursorObj;
    private Renderer cursorRenderer;
    private Material cursorMaterial;
    private bool canPlace;
    private Vector3Int lastCursorPos;
    private bool cursorVisible;

    // ---- 色定義 ----
    private static readonly Color ColorValid   = new Color(0.5f, 1f, 0.5f, 0.5f);
    private static readonly Color ColorInvalid = new Color(1f, 0.3f, 0.3f, 0.5f);

    // ---- 建築物の親 ----
    [Header("建築物の親Transform")]
    [SerializeField] public Transform BuildingParent;

    // ---- Raycast レイヤー ----
    private int blockLayerMask;

    // ---- 設置済み建築物の位置管理 ----
    private HashSet<Vector3Int> buildingPositions = new HashSet<Vector3Int>();

    // ==================================================================
    //  初期化
    // ==================================================================
    public void Init(TurnGenerater turngenerater, TerritorySystem territorysystem,
                     APSystem apsystem, FactionState factionState,
                     MoveGererater movegenerater, MapCreate mapcreate)
    {
        this.turngenerater = turngenerater;
        this.territorysystem = territorysystem;
        this.apsystem = apsystem;
        this.factionState = factionState;
        this.movegenerater = movegenerater;
        this.mapcreate = mapcreate;

        blockLayerMask = LayerMask.GetMask("Block");

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

        // 建築物の親が無ければ作成
        if (BuildingParent == null)
        {
            var go = new GameObject("Buildings");
            BuildingParent = go.transform;
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
        cursorVisible = false;

        CreateCursor();
        Debug.Log($"[BuildSystem] 建築モード開始: {facility}");
    }

    // ==================================================================
    //  建築モード解除
    // ==================================================================
    public void CancelBuildMode()
    {
        IsActive = false;
        DestroyCursor();
        Debug.Log("[BuildSystem] 建築モード解除");
    }

    // ==================================================================
    //  カーソル更新（PlayerMove.Update から毎フレーム呼ばれる）
    // ==================================================================
    public void UpdateCursor()
    {
        if (!IsActive) return;
        if (cursorObj == null) return;

        if (!TryGetMouseRay(out Ray ray))
        {
            SetCursorVisible(false);
            return;
        }

        if (!Physics.Raycast(ray, out RaycastHit hit, 100f, blockLayerMask))
        {
            SetCursorVisible(false);
            return;
        }

        // Raycast のヒット位置をグリッドにスナップ
        Vector3Int gridPos = new Vector3Int(
            Mathf.RoundToInt(hit.point.x),
            Mathf.RoundToInt(hit.point.y),
            Mathf.RoundToInt(hit.point.z)
        );

        // SetPos 上の最も近い有効座標に合わせる
        Vector3Int snapped = SnapToSetPos(gridPos);

        bool isSubCrystal = FacilityData.IsSubCrystal(SelectedFacility);

        if (isSubCrystal)
        {
            // サブクリスタル: 領地外でもカーソルが追従する
            // 領地内の場合は領地端にクランプ（領地端についていく動作）
            if (IsInTerritory(snapped))
            {
                // 領地内: 最も近い領地外座標にクランプ
                Vector3Int clamped = ClampToOutsideTerritory(snapped);
                if (clamped.x == int.MinValue)
                {
                    // 全て領地内の場合はそのまま表示（赤表示）
                }
                else
                {
                    snapped = clamped;
                }
            }
        }
        else
        {
            // 通常建築物: 領地内チェック
            if (!IsInTerritory(snapped))
            {
                // 領地外: 最も近い領地座標にクランプ
                Vector3Int clamped = ClampToTerritory(snapped);
                if (clamped.x == int.MinValue)
                {
                    SetCursorVisible(false);
                    return;
                }
                snapped = clamped;
            }
        }

        // カーソル位置の更新
        lastCursorPos = snapped;
        cursorObj.transform.position = new Vector3(snapped.x, snapped.y, snapped.z);
        SetCursorVisible(true);

        // 設置可否の判定
        canPlace = CheckCanPlace(snapped);
        cursorMaterial.color = canPlace ? ColorValid : ColorInvalid;
    }

    // ==================================================================
    //  設置試行（左クリック時に呼ばれる）
    // ==================================================================
    public bool TryPlace()
    {
        if (!IsActive) return false;
        if (!canPlace) return false;
        if (!cursorVisible) return false;

        bool isSubCrystal = FacilityData.IsSubCrystal(SelectedFacility);

        if (isSubCrystal)
        {
            // サブクリスタル: 資源チェック（AP0, リソースコスト0, サブクリスタル1消費）
            if (factionState.GetSubCrystals(Team.Player) <= 0)
            {
                Debug.Log("[BuildSystem] サブクリスタル不足: 設置不可");
                return false;
            }
            if (factionState.GetSubCrystalCooldown(Team.Player) > 0)
            {
                Debug.Log("[BuildSystem] サブクリスタルクールダウン中: 設置不可");
                return false;
            }

            // 設置実行
            PlaceBuilding(lastCursorPos, SelectedFacility);

            // サブクリスタル消費 + クールダウン設定
            factionState.ModifySubCrystals(Team.Player, -1);
            factionState.SetSubCrystalCooldown(Team.Player, SubCrystalSystem.SubCrystalCooldownTurns);

            // 領地拡張
            if (subCrystalSystem != null)
            {
                // 最後に設置した建築物を取得
                var lastBuilding = BuildingParent.GetChild(BuildingParent.childCount - 1).gameObject;
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
            PlaceBuilding(lastCursorPos, SelectedFacility);

            // AP・リソース消費
            apsystem.ConsumeBuild(Team.Player, SelectedFacility, factionState);
        }

        // 建築モード解除
        CancelBuildMode();
        return true;
    }

    // ==================================================================
    //  内部: 設置可否チェック
    // ==================================================================
    private bool CheckCanPlace(Vector3Int pos)
    {
        bool isSubCrystal = FacilityData.IsSubCrystal(SelectedFacility);

        if (isSubCrystal)
        {
            // サブクリスタル: SubCrystalSystem に委譲
            if (subCrystalSystem == null) return false;
            return subCrystalSystem.CanPlaceSubCrystal(pos, Team.Player);
        }

        // 通常建築物: 領地内でなければ不可
        if (!IsInTerritory(pos)) return false;

        // クリスタル位置チェック
        Vector3 pcpVec = turngenerater.crystalsystem.PCP;
        Vector3Int pcp = new Vector3Int(
            Mathf.RoundToInt(pcpVec.x),
            Mathf.RoundToInt(pcpVec.y),
            Mathf.RoundToInt(pcpVec.z));
        if (pos == pcp) return false;

        Vector3 ecpVec = turngenerater.crystalsystem.ECP;
        Vector3Int ecp = new Vector3Int(
            Mathf.RoundToInt(ecpVec.x),
            Mathf.RoundToInt(ecpVec.y),
            Mathf.RoundToInt(ecpVec.z));
        if (pos == ecp) return false;

        // 既設の建築物チェック
        if (buildingPositions.Contains(pos)) return false;

        return true;
    }

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
                                   Quaternion.identity, BuildingParent);
        }
        else
        {
            // フォールバック: Cube 生成
            building = GameObject.CreatePrimitive(PrimitiveType.Cube);
            building.transform.position = new Vector3(pos.x, pos.y, pos.z);
            building.transform.SetParent(BuildingParent);
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

        // 壁の場合は UnitPointData に追加（全駒通過不可）
        if (FacilityData.IsWall(facility))
        {
            movegenerater.UnitPointData.Add(new Vector3(pos.x, pos.y, pos.z));
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
        if (target.team != Team.Player) return false;

        var facility = target.facilityKind;
        int currentLevel = Mathf.Max(1, target.Level);
        int maxLevel = FacilityData.GetMaxLevel(facility);
        if (currentLevel >= maxLevel) return false;

        var res = factionState.PlayerResources;
        int currentAP = factionState.GetAP(Team.Player);
        if (!FacilityData.CanUpgrade(res, currentAP, facility, currentLevel))
        {
            Debug.Log($"[BuildSystem] 強化不可: {facility} Lv{currentLevel} → Lv{currentLevel + 1}");
            return false;
        }

        // コスト消費
        FacilityData.ConsumeUpgrade(res, factionState.PlayerAP, facility, currentLevel + 1);

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

    // ==================================================================
    //  内部: 領地判定
    // ==================================================================
    private bool IsInTerritory(Vector3Int pos)
    {
        if (territorysystem.PTSetPos == null) return false;
        return territorysystem.PTSetPos.Any(p =>
            Mathf.RoundToInt(p.x) == pos.x && Mathf.RoundToInt(p.z) == pos.z);
    }

    // ==================================================================
    //  内部: 領地端へクランプ
    // ==================================================================
    private Vector3Int ClampToTerritory(Vector3Int pos)
    {
        if (territorysystem.PTSetPos == null || territorysystem.PTSetPos.Count == 0)
            return new Vector3Int(int.MinValue, 0, 0);

        float minDist = float.MaxValue;
        Vector3 closest = territorysystem.PTSetPos[0];

        foreach (var p in territorysystem.PTSetPos)
        {
            float dx = p.x - pos.x;
            float dz = p.z - pos.z;
            float dist = dx * dx + dz * dz;
            if (dist < minDist)
            {
                minDist = dist;
                closest = p;
            }
        }

        return new Vector3Int(
            Mathf.RoundToInt(closest.x),
            Mathf.RoundToInt(closest.y),
            Mathf.RoundToInt(closest.z));
    }

    // ==================================================================
    //  内部: 領地外の最も近い座標にクランプ（サブクリスタル用）
    // ==================================================================
    private Vector3Int ClampToOutsideTerritory(Vector3Int pos)
    {
        if (mapcreate.SetPos == null || mapcreate.SetPos.Count == 0)
            return new Vector3Int(int.MinValue, 0, 0);

        float minDist = float.MaxValue;
        Vector3 closest = Vector3.zero;
        bool found = false;

        foreach (var p in mapcreate.SetPos)
        {
            int px = Mathf.RoundToInt(p.x);
            int pz = Mathf.RoundToInt(p.z);

            // 領地内のマスはスキップ
            bool inTerritory = false;
            if (territorysystem.PTSetPos != null)
                inTerritory |= territorysystem.PTSetPos.Any(t =>
                    Mathf.RoundToInt(t.x) == px && Mathf.RoundToInt(t.z) == pz);
            if (territorysystem.ETSetPos != null)
                inTerritory |= territorysystem.ETSetPos.Any(t =>
                    Mathf.RoundToInt(t.x) == px && Mathf.RoundToInt(t.z) == pz);
            if (inTerritory) continue;

            float dx = p.x - pos.x;
            float dz = p.z - pos.z;
            float dist = dx * dx + dz * dz;
            if (dist < minDist)
            {
                minDist = dist;
                closest = p;
                found = true;
            }
        }

        if (!found)
            return new Vector3Int(int.MinValue, 0, 0);

        return new Vector3Int(
            Mathf.RoundToInt(closest.x),
            Mathf.RoundToInt(closest.y),
            Mathf.RoundToInt(closest.z));
    }

    // ==================================================================
    //  内部: SetPos 上の最も近い座標にスナップ
    // ==================================================================
    private Vector3Int SnapToSetPos(Vector3Int gridPos)
    {
        if (mapcreate.SetPos == null || mapcreate.SetPos.Count == 0)
            return gridPos;

        float minDist = float.MaxValue;
        Vector3 closest = mapcreate.SetPos[0];

        foreach (var p in mapcreate.SetPos)
        {
            float dx = p.x - gridPos.x;
            float dz = p.z - gridPos.z;
            float dist = dx * dx + dz * dz;
            if (dist < minDist)
            {
                minDist = dist;
                closest = p;
            }
        }

        return new Vector3Int(
            Mathf.RoundToInt(closest.x),
            Mathf.RoundToInt(closest.y),
            Mathf.RoundToInt(closest.z));
    }

    // ==================================================================
    //  内部: カーソル生成 / 破棄
    // ==================================================================
    private void CreateCursor()
    {
        if (cursorObj != null) DestroyCursor();

        cursorObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cursorObj.name = "BuildCursor";
        cursorObj.transform.localScale = new Vector3(0.95f, 0.95f, 0.95f);

        // コライダーを無効化（Raycast に干渉しないように）
        var col = cursorObj.GetComponent<Collider>();
        if (col != null) col.enabled = false;

        // 半透明マテリアル
        cursorRenderer = cursorObj.GetComponent<Renderer>();
        cursorMaterial = new Material(Shader.Find("Standard"));
        cursorMaterial.SetFloat("_Mode", 3); // Transparent
        cursorMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        cursorMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        cursorMaterial.SetInt("_ZWrite", 0);
        cursorMaterial.DisableKeyword("_ALPHATEST_ON");
        cursorMaterial.EnableKeyword("_ALPHABLEND_ON");
        cursorMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        cursorMaterial.renderQueue = 3000;
        cursorMaterial.color = ColorValid;
        cursorRenderer.material = cursorMaterial;

        SetCursorVisible(false);
    }

    private void DestroyCursor()
    {
        if (cursorObj != null)
        {
            Destroy(cursorMaterial);
            Destroy(cursorObj);
            cursorObj = null;
            cursorRenderer = null;
            cursorMaterial = null;
        }
    }

    private void SetCursorVisible(bool visible)
    {
        cursorVisible = visible;
        if (cursorObj != null)
            cursorObj.SetActive(visible);
    }

    // ==================================================================
    //  内部: マウス Ray 取得
    // ==================================================================
    private bool TryGetMouseRay(out Ray ray)
    {
        ray = default;
        if (Mouse.current == null) return false;
        Vector2 mousePos = Mouse.current.position.ReadValue();
        ray = Camera.main.ScreenPointToRay(mousePos);
        return true;
    }

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
}
