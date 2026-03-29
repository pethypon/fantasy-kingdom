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

        // ---- ML観測: プレイヤーの召喚をMLシステムに記録 ----
        if (turngenerater != null && turngenerater.aiCommander != null)
        {
            Vector3 summonPos = new Vector3(lastCursorPos.x, 0, lastCursorPos.z);
            turngenerater.aiCommander.MLIntegration.ObservePlayerBuild(summonPos, turngenerater.Turn);
        }

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
    //  SetPosを使って正確な位置判定を行う
    // ==================================================================
    private bool CheckCanPlace(Vector3Int pos)
    {
        if (!IsInTerritory(pos)) return false;

        // SetPosに存在するマスかチェック（有効なタイルのみ許可）
        bool onMap = mapcreate.SetPos.Any(p =>
            Mathf.RoundToInt(p.x) == pos.x && Mathf.RoundToInt(p.z) == pos.z);
        if (!onMap) return false;

        // クリスタル位置チェック（XZのみで比較）
        Vector3 pcpVec = turngenerater.crystalsystem.PCP;
        if (Mathf.RoundToInt(pcpVec.x) == pos.x && Mathf.RoundToInt(pcpVec.z) == pos.z) return false;

        Vector3 ecpVec = turngenerater.crystalsystem.ECP;
        if (Mathf.RoundToInt(ecpVec.x) == pos.x && Mathf.RoundToInt(ecpVec.z) == pos.z) return false;

        // 既にユニットがいる場所には不可（UnitPointDataはY=0で管理）
        Vector3 posVec = new Vector3(pos.x, 0f, pos.z);
        if (movegenerater.UnitPointData.Contains(posVec)) return false;

        return true;
    }

    // ==================================================================
    //  内部: ユニットの配置
    //  SetPosから正しいY座標を取得して配置する
    // ==================================================================
    private void PlaceUnit(Vector3Int pos, Kind kind)
    {
        GameObject prefab = null;

        // まずプレハブマップから取得
        if (prefabMap.TryGetValue(kind, out GameObject mapped) && mapped != null)
        {
            prefab = mapped;
        }

        // SetPosから正しいY座標を取得（SetPos.y = 地形高さ+1 = ユニット配置高さ）
        float spawnY = pos.y;
        foreach (var sp in mapcreate.SetPos)
        {
            if (Mathf.RoundToInt(sp.x) == pos.x && Mathf.RoundToInt(sp.z) == pos.z)
            {
                spawnY = sp.y;
                break;
            }
        }
        Vector3 spawnPos = new Vector3(pos.x, spawnY, pos.z);

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

        // ユニット位置を記録（UnitPointDataはY=0で管理）
        movegenerater.UnitPointData.Add(new Vector3(pos.x, 0f, pos.z));

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

    // ==================================================================
    //  AI用: カーソル不要の直接召喚
    // ==================================================================

    /// <summary>
    /// AI用: 指定位置にユニットを召喚する（カーソル・UI不要）
    /// </summary>
    public bool AISummonUnit(Vector3Int pos, Kind kind, Team team)
    {
        if (!CanSummon(team, kind)) return false;
        if (!AICheckCanPlace(pos, team)) return false;

        // リソース消費
        AIConsumeSummon(team, kind);

        // ユニット配置
        AIPlaceUnit(pos, kind, team);

        Debug.Log($"[SummonSystem] AI({team}) {kind} を ({pos.x},{pos.y},{pos.z}) に召喚");
        return true;
    }

    /// <summary>
    /// AI用: 設置可否チェック（任意チーム対応）
    /// SetPosを使って正確な位置判定を行う
    /// </summary>
    private bool AICheckCanPlace(Vector3Int pos, Team team)
    {
        var territory = team == Team.Player ? territorysystem.PTSetPos : territorysystem.ETSetPos;
        if (territory == null) return false;
        bool inTerritory = territory.Any(p =>
            Mathf.RoundToInt(p.x) == pos.x && Mathf.RoundToInt(p.z) == pos.z);
        if (!inTerritory) return false;

        // SetPosに存在するマスかチェック（有効なタイルのみ許可）
        bool onMap = mapcreate.SetPos.Any(p =>
            Mathf.RoundToInt(p.x) == pos.x && Mathf.RoundToInt(p.z) == pos.z);
        if (!onMap) return false;

        // クリスタル位置チェック（XZのみで比較）
        Vector3 pcpVec = turngenerater.crystalsystem.PCP;
        if (Mathf.RoundToInt(pcpVec.x) == pos.x && Mathf.RoundToInt(pcpVec.z) == pos.z) return false;

        Vector3 ecpVec = turngenerater.crystalsystem.ECP;
        if (Mathf.RoundToInt(ecpVec.x) == pos.x && Mathf.RoundToInt(ecpVec.z) == pos.z) return false;

        // UnitPointDataはY=0で管理されているため、Y=0で比較する
        Vector3 posVec = new Vector3(pos.x, 0f, pos.z);
        if (movegenerater.UnitPointData.Contains(posVec)) return false;

        return true;
    }

    /// <summary>
    /// AI用: リソース消費（public版 ConsumeSummon）
    /// </summary>
    private void AIConsumeSummon(Team team, Kind kind)
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
    }

    /// <summary>
    /// AI用: ユニット配置（任意チーム対応）
    /// SetPosから正しいY座標を取得して配置する
    /// </summary>
    private void AIPlaceUnit(Vector3Int pos, Kind kind, Team team)
    {
        Transform parent = team == Team.Player ? unitset.PlayerUnit : unitset.EnemyUnit;
        Direction dir = team == Team.Player ? Direction.N : Direction.S;

        GameObject prefab = null;
        if (prefabMap != null && prefabMap.TryGetValue(kind, out GameObject mapped) && mapped != null)
            prefab = mapped;

        // SetPosから正しいY座標を取得（SetPos.y = 地形高さ+1 = ユニット配置高さ）
        float spawnY = pos.y;
        foreach (var sp in mapcreate.SetPos)
        {
            if (Mathf.RoundToInt(sp.x) == pos.x && Mathf.RoundToInt(sp.z) == pos.z)
            {
                spawnY = sp.y;
                break;
            }
        }
        Vector3 spawnPos = new Vector3(pos.x, spawnY, pos.z);

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
            var obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            obj.transform.position = spawnPos;
            obj.transform.SetParent(parent);
            obj.name = kind.ToString();

            var renderer = obj.GetComponent<Renderer>();
            if (renderer != null)
            {
                var mat = new Material(Shader.Find("Standard"));
                mat.color = team == Team.Player
                    ? new Color(0.3f, 0.5f, 0.9f)
                    : new Color(0.9f, 0.3f, 0.3f);
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

        // UnitPointDataはY=0で管理
        movegenerater.UnitPointData.Add(new Vector3(pos.x, 0f, pos.z));
        visiongenerater.VisionPoint(mapcreate, movegenerater, turngenerater.crystalsystem);
    }

    /// <summary>
    /// AI用: 指定チームの領地内で召喚可能な位置一覧を返す
    /// SetPosからY座標を取得して正しい高さで返す
    /// </summary>
    public List<Vector3Int> AIGetSummonablePositions(Team team)
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
            // SetPosのY座標を使う（マップ上に存在するタイルのみ）
            if (!setposLookup.TryGetValue((px, pz), out int py)) continue;
            var pos = new Vector3Int(px, py, pz);
            if (AICheckCanPlace(pos, team))
                result.Add(pos);
        }
        return result;
    }
}
