using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ユニット召喚システム: BuildSystem と同じパターンでカーソル追従・設置可否判定・ユニット生成を管理する。
/// </summary>
public class SummonSystem : MonoBehaviour
{
    // ---- 外部参照（Init で注入） ----
    private TurnGenerater turngenerater;
    private TerritorySystem territorysystem;
    private APSystem apsystem;
    private FactionState factionState;
    private MoveGererater movegenerater;
    private MapCreate mapcreate;
    private UnitSetting unitset;
    private VisionGenerater visiongenerater;

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

    // ---- 色定義 ----
    private static readonly Color ColorValid   = new Color(0.5f, 0.5f, 1f, 0.5f);
    private static readonly Color ColorInvalid = new Color(1f, 0.3f, 0.3f, 0.5f);

    // ---- Raycast レイヤー ----
    private int blockLayerMask;

    // ==================================================================
    //  初期化
    // ==================================================================
    public void Init(TurnGenerater turngenerater, TerritorySystem territorysystem,
                     APSystem apsystem, FactionState factionState,
                     MoveGererater movegenerater, MapCreate mapcreate,
                     UnitSetting unitset, VisionGenerater visiongenerater)
    {
        this.turngenerater = turngenerater;
        this.territorysystem = territorysystem;
        this.apsystem = apsystem;
        this.factionState = factionState;
        this.movegenerater = movegenerater;
        this.mapcreate = mapcreate;
        this.unitset = unitset;
        this.visiongenerater = visiongenerater;

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
    //  召喚モード開始
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

    // ==================================================================
    //  召喚モード解除
    // ==================================================================
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

        Vector3Int gridPos = new Vector3Int(
            Mathf.RoundToInt(hit.point.x),
            Mathf.RoundToInt(hit.point.y),
            Mathf.RoundToInt(hit.point.z)
        );

        Vector3Int snapped = SnapToSetPos(gridPos);

        if (!IsInTerritory(snapped))
        {
            Vector3Int clamped = ClampToTerritory(snapped);
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
        if (!IsActive) return false;
        if (!canPlace) return false;
        if (!cursorVisible) return false;

        if (!CanSummon(Team.Player, SelectedKind))
        {
            Debug.Log("[SummonSystem] AP/リソース不足: 召喚不可");
            return false;
        }

        PlaceUnit(lastCursorPos, SelectedKind);
        ConsumeSummon(Team.Player, SelectedKind);
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
    //  AP・リソース消費
    // ==================================================================
    private void ConsumeSummon(Team team, Kind kind)
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
    //  内部: 設置可否チェック
    // ==================================================================
    private bool CheckCanPlace(Vector3Int pos)
    {
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

        // 既にユニットがいる場所には不可
        Vector3 posVec = new Vector3(pos.x, pos.y, pos.z);
        if (movegenerater.UnitPointData.Contains(posVec)) return false;

        return true;
    }

    // ==================================================================
    //  内部: ユニットの配置
    // ==================================================================
    private void PlaceUnit(Vector3Int pos, Kind kind)
    {
        GameObject prefab = null;

        // まずプレハブマップから取得
        if (prefabMap.TryGetValue(kind, out GameObject mapped) && mapped != null)
        {
            prefab = mapped;
        }

        Vector3 spawnPos = new Vector3(pos.x, pos.y, pos.z);

        if (prefab != null)
        {
            // SpawnUnit でプレハブを使って生成（UnitData 適用 + HeadUI 付与）
            var obj = unitset.SpawnUnit(prefab, spawnPos, unitset.PlayerUnit);
            var status = obj.GetComponentInChildren<Status>();
            if (status != null)
            {
                status.team = Team.Player;
                status.direction = Direction.N;
            }
        }
        else
        {
            // フォールバック: Sphere を生成
            var obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            obj.transform.position = spawnPos;
            obj.transform.SetParent(unitset.PlayerUnit);
            obj.name = kind.ToString();

            var renderer = obj.GetComponent<Renderer>();
            if (renderer != null)
            {
                var mat = new Material(Shader.Find("Standard"));
                mat.color = new Color(0.3f, 0.5f, 0.9f);
                renderer.material = mat;
            }

            var status = obj.AddComponent<Status>();
            status.kind = kind;
            status.team = Team.Player;
            status.type = Type.Unit;
            status.direction = Direction.N;

            if (unitset.UnitDataMap.TryGetValue(kind, out UnitData data))
                data.ApplyToStatus(status, 1);

            UnitHeadUI.Attach(obj);
        }

        // ユニット位置を記録
        movegenerater.UnitPointData.Add(new Vector3(pos.x, pos.y, pos.z));

        // 視界更新
        visiongenerater.VisionPoint(mapcreate, movegenerater, turngenerater.crystalsystem);

        Debug.Log($"[SummonSystem] {kind} を ({pos.x}, {pos.y}, {pos.z}) に召喚");
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
