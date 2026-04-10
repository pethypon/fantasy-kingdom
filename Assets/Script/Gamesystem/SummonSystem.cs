using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ユニット召喚システム: カーソル追従・設置可否判定・ユニット生成を管理する。
/// </summary>
public class SummonSystem : MonoBehaviour
{
    // ---- 外部参照（Init で注入） ----
    private TurnGenerator turnGenerator;
    private TerritorySystem territorysystem;
    private APSystem apsystem;
    private FactionState factionState;
    private MoveGenerator moveGenerator;
    private MapCreate mapcreate;
    private UnitSetting unitset;
    private VisionGenerator visionGenerator;

    // ---- プレハブ（Inspector で割り当て） ----
    [Header("ユニットプレハブ（Inspector割当）")]
    [SerializeField] private SerializableUnitPrefab[] unitPrefabs;

    [System.Serializable]
    public struct SerializableUnitPrefab
    {
        public Kind kind;
        public GameObject prefab;
    }

    private Dictionary<Kind, GameObject> prefabMap;

    // ---- 召喚モード状態 ----
    public bool IsActive { get; private set; }
    public Kind SelectedKind { get; private set; }

    // ---- カーソル ----
    private GameObject cursorObj;
    private Renderer cursorRenderer;
    private Material cursorMaterial;
    private bool canPlace;
    private Vector3Int lastCursorPos;
    private bool cursorVisible;

    // ---- 色定義（BrandGuide に一元化） ----
    private static Color ColorValid   => BrandGuide.CursorSummonValid;
    private static Color ColorInvalid => BrandGuide.CursorSummonInvalid;

    // ---- Raycast レイヤー ----
    private int blockLayerMask;

    // ==================================================================
    //  初期化
    // ==================================================================
    public void Init(TurnGenerator turnGenerator, TerritorySystem territorysystem,
                     APSystem apsystem, FactionState factionState,
                     MoveGenerator moveGenerator, MapCreate mapcreate,
                     UnitSetting unitset, VisionGenerator visionGenerator)
    {
        this.turnGenerator = turnGenerator;
        this.territorysystem = territorysystem;
        this.apsystem = apsystem;
        this.factionState = factionState;
        this.moveGenerator = moveGenerator;
        this.mapcreate = mapcreate;
        this.unitset = unitset;
        this.visionGenerator = visionGenerator;

        blockLayerMask = LayerMask.GetMask("Block");

        // プレハブマップ構築
        prefabMap = new Dictionary<Kind, GameObject>();
        if (unitPrefabs != null)
        {
            foreach (var entry in unitPrefabs)
            {
                if (entry.prefab != null)
                    prefabMap[entry.kind] = entry.prefab;
            }
        }
    }

    // ==================================================================
    //  召喚モード開始 / 解除
    // ==================================================================
    public void StartSummonMode(Kind kind)
    {
        if (IsActive) CancelSummonMode();

        SelectedKind = kind;
        IsActive = true;
        canPlace = false;
        cursorVisible = false;

        CreateCursor();
        Debug.Log($"[SummonSystem] 召喚モード開始: {kind}");
    }

    public void CancelSummonMode()
    {
        IsActive = false;
        DestroyCursor();
        Debug.Log("[SummonSystem] 召喚モード解除");
    }

    // ==================================================================
    //  カーソル更新（PlayerMove.Update から毎フレーム呼ばれる）
    // ==================================================================
    public void UpdateCursor()
    {
        if (!IsActive || cursorObj == null) return;

        if (!TryGetMouseRay(out Ray ray))
        {
            SetCursorVisible(false);
            return;
        }

        if (!Physics.Raycast(ray, out RaycastHit hit, GameConstants.DefaultRayDistance, blockLayerMask))
        {
            SetCursorVisible(false);
            return;
        }

        Vector3Int gridPos = GridHelper.ToGrid(hit.point);
        Vector3Int snapped = mapcreate.SnapToSetPos(gridPos);

        if (!territorysystem.IsInTerritory(snapped, Team.Player))
        {
            Vector3Int clamped = territorysystem.ClampToTerritory(snapped, Team.Player);
            if (clamped.x == int.MinValue)
            {
                SetCursorVisible(false);
                return;
            }
            snapped = clamped;
        }

        lastCursorPos = snapped;
        cursorObj.transform.position = new Vector3(snapped.x, snapped.y, snapped.z);
        SetCursorVisible(true);

        canPlace = CheckCanPlace(snapped);
        cursorMaterial.color = canPlace ? ColorValid : ColorInvalid;
    }

    // ==================================================================
    //  設置試行（左クリック時に呼ばれる）
    // ==================================================================
    public bool TryPlace()
    {
        if (!IsActive || !canPlace || !cursorVisible) return false;

        if (!CanSummon(Team.Player, SelectedKind))
        {
            Debug.Log("[SummonSystem] AP/リソース不足: 召喚不可");
            return false;
        }

        InstantiateUnit(lastCursorPos, SelectedKind, Team.Player);
        ConsumeSummonResources(Team.Player, SelectedKind);

        // ML観測: プレイヤーの召喚をMLシステムに記録
        NotifyMLObservation(lastCursorPos);

        CancelSummonMode();
        return true;
    }

    // ==================================================================
    //  AP・リソースチェック
    // ==================================================================
    public bool CanSummon(Team team, Kind kind)
    {
        if (!unitset.UnitDataMap.TryGetValue(kind, out UnitData data)) return false;
        if (factionState.GetAP(team) < data.costAP) return false;

        var res = team == Team.Player ? factionState.PlayerResources : factionState.EnemyResources;
        return res.Wood     >= data.costWood
            && res.Stone    >= data.costStone
            && res.Iron     >= data.costIron
            && res.MagicOre >= data.costMagic
            && res.Water    >= data.costWater
            && res.Plank    >= data.costPlank
            && res.CutStone >= data.costCutStone
            && res.Bread    >= data.costBread
            && res.Citizen  >= data.costCitizen;
    }

    // ==================================================================
    //  リソース消費（共通）
    // ==================================================================
    private void ConsumeSummonResources(Team team, Kind kind)
    {
        if (!unitset.UnitDataMap.TryGetValue(kind, out UnitData data)) return;

        factionState.ModifyAP(team, -data.costAP);

        var res = team == Team.Player ? factionState.PlayerResources : factionState.EnemyResources;
        res.Wood     -= data.costWood;
        res.Stone    -= data.costStone;
        res.Iron     -= data.costIron;
        res.MagicOre -= data.costMagic;
        res.Water    -= data.costWater;
        res.Plank    -= data.costPlank;
        res.CutStone -= data.costCutStone;
        res.Bread    -= data.costBread;
        res.Citizen  -= data.costCitizen;

        Debug.Log($"[SummonSystem] {team} / Summon({kind})  AP:{data.costAP}  残AP:{factionState.GetAP(team)}");
    }

    // ==================================================================
    //  設置可否チェック（共通）
    // ==================================================================
    private bool CheckCanPlace(Vector3Int pos)
    {
        return CheckCanPlaceForTeam(pos, Team.Player);
    }

    /// <summary>指定チームでの設置可否を判定する（プレイヤー・AI共通）</summary>
    private bool CheckCanPlaceForTeam(Vector3Int pos, Team team)
    {
        if (!territorysystem.IsInTerritory(pos, team)) return false;
        if (!mapcreate.HasTileAt(pos.x, pos.z)) return false;
        if (IsCrystalPosition(pos)) return false;

        // 既にユニットがいる場所には不可（UnitPointData は Y=0 で管理）
        Vector3 posVec = GridHelper.ToUnitPoint(pos);
        if (moveGenerator.UnitPointData.Contains(posVec)) return false;

        return true;
    }

    // ==================================================================
    //  ユニット生成（共通処理）
    // ==================================================================

    /// <summary>ユニットを指定位置に生成する。プレイヤー・AI共通のコアロジック。</summary>
    private void InstantiateUnit(Vector3Int pos, Kind kind, Team team)
    {
        Transform parent = team == Team.Player ? unitset.PlayerUnit : unitset.EnemyUnit;
        Direction dir = team == Team.Player ? Direction.N : Direction.S;

        // SetPos から正しい Y 座標を取得
        float spawnY = pos.y;
        if (mapcreate.TryGetHeight(pos.x, pos.z, out float height))
            spawnY = height;
        Vector3 spawnPos = new Vector3(pos.x, spawnY, pos.z);

        // プレハブ or フォールバック生成
        GameObject prefab = null;
        if (prefabMap != null && prefabMap.TryGetValue(kind, out GameObject mapped) && mapped != null)
            prefab = mapped;

        if (prefab != null)
        {
            var obj = unitset.SpawnUnit(prefab, spawnPos, parent);
            var status = obj.GetComponentInChildren<Status>();
            if (status != null)
            {
                status.team = team;
                status.direction = dir;
            }
        }
        else
        {
            CreateFallbackUnit(spawnPos, kind, team, dir, parent);
        }

        // ユニット位置を記録
        moveGenerator.UnitPointData.Add(GridHelper.ToUnitPoint(pos));

        // 視界更新
        visionGenerator.VisionPoint(mapcreate, moveGenerator, turnGenerator.Systems.CrystalSystem);

        Debug.Log($"[SummonSystem] {kind} を ({pos.x}, {Mathf.RoundToInt(spawnY)}, {pos.z}) に召喚 ({team})");
    }

    /// <summary>プレハブ未割当時のフォールバックユニットを生成する</summary>
    private void CreateFallbackUnit(Vector3 spawnPos, Kind kind, Team team,
                                     Direction dir, Transform parent)
    {
        var obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        obj.transform.position = spawnPos;
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

        if (unitset.UnitDataMap.TryGetValue(kind, out UnitData data))
            data.ApplyToStatus(status, 1);

        UnitHeadUI.Attach(obj);
    }

    // ==================================================================
    //  AI用: カーソル不要の直接召喚
    // ==================================================================

    /// <summary>AI用: 指定位置にユニットを召喚する（カーソル・UI不要）</summary>
    public bool AISummonUnit(Vector3Int pos, Kind kind, Team team)
    {
        if (!CanSummon(team, kind)) return false;
        if (!CheckCanPlaceForTeam(pos, team)) return false;

        ConsumeSummonResources(team, kind);
        InstantiateUnit(pos, kind, team);

        Debug.Log($"[SummonSystem] AI({team}) {kind} を ({pos.x},{pos.y},{pos.z}) に召喚");
        return true;
    }

    /// <summary>AI用: 指定チームの領地内で召喚可能な位置一覧を返す</summary>
    public List<Vector3Int> AIGetSummonablePositions(Team team)
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
            if (CheckCanPlaceForTeam(pos, team))
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

    /// <summary>ML観測: プレイヤーの召喚をMLシステムに記録</summary>
    private void NotifyMLObservation(Vector3Int pos)
    {
        if (turnGenerator != null && turnGenerator.Systems.AICommander != null)
        {
            Vector3 summonPos = new Vector3(pos.x, 0, pos.z);
            turnGenerator.Systems.AICommander.MLIntegration.ObservePlayerBuild(summonPos, turnGenerator.Context.Turn);
        }
    }

    // ==================================================================
    //  カーソル生成 / 破棄
    // ==================================================================
    private void CreateCursor()
    {
        if (cursorObj != null) DestroyCursor();

        cursorObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        cursorObj.name = "SummonCursor";
        cursorObj.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);

        var col = cursorObj.GetComponent<Collider>();
        if (col != null) col.enabled = false;

        cursorRenderer = cursorObj.GetComponent<Renderer>();
        cursorMaterial = new Material(Shader.Find("Standard"));
        cursorMaterial.SetFloat("_Mode", 3);
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

    private bool TryGetMouseRay(out Ray ray)
    {
        ray = default;
        if (Mouse.current == null) return false;
        Vector2 mousePos = Mouse.current.position.ReadValue();
        ray = Camera.main.ScreenPointToRay(mousePos);
        return true;
    }
}
