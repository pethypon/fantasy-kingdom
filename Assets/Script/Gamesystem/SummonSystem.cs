using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ユニット召喚システム: カーソル追従・設置可否判定・ユニット生成を管理する。
/// </summary>
public class SummonSystem : MonoBehaviour
{
    private void OnDestroy() => _cursor?.Destroy();
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
    public UnitData SelectedDefinition { get; private set; }

    // ---- カーソル（BuildCursorController に委譲） ----
    private BuildCursorController _cursor;
    private bool canPlace;

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

        _cursor = new BuildCursorController(
            PrimitiveType.Sphere,
            new Vector3(0.8f, 0.8f, 0.8f),
            "SummonCursor",
            BrandGuide.CursorSummonValid,
            BrandGuide.CursorSummonInvalid);

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
        UnitAuthoringCatalog.Load()?.Apply(null, prefabMap);
    }

    // ==================================================================
    //  召喚モード開始 / 解除
    // ==================================================================
    public void StartSummonMode(Kind kind)
    {
        if (unitset == null || unitset.UnitDataMap == null || !unitset.UnitDataMap.TryGetValue(kind, out UnitData data)) return;
        StartSummonMode(data);
    }

    public void StartSummonMode(UnitData data)
    {
        if (turnGenerator != null && turnGenerator.DeveloperPlayerAIEnabled) return;
        if (data == null || !data.IsValidForAuthoring || !data.availableToPlayer) return;
        if (IsActive) CancelSummonMode();

        SelectedDefinition = data;
        SelectedKind = data.kind;
        IsActive = true;
        canPlace = false;

        _cursor.Create();
        Debug.Log($"[SummonSystem] 召喚モード開始: {data.DisplayName}");
    }

    public void CancelSummonMode()
    {
        IsActive = false;
        SelectedDefinition = null;
        _cursor?.Destroy();
        Debug.Log("[SummonSystem] 召喚モード解除");
    }

    // ==================================================================
    //  カーソル更新（PlayerMove.Update から毎フレーム呼ばれる）
    // ==================================================================
    public void UpdateCursor()
    {
        if (!IsActive) return;

        if (!_cursor.TryGetGridPosition(out Vector3Int gridPos))
        {
            _cursor.SetVisible(false);
            return;
        }

        Vector3Int snapped = mapcreate.SnapToSetPos(gridPos);

        if (!territorysystem.IsInTerritory(snapped, Team.Player))
        {
            Vector3Int clamped = territorysystem.ClampToTerritory(snapped, Team.Player);
            if (clamped.x == int.MinValue)
            {
                _cursor.SetVisible(false);
                return;
            }
            snapped = clamped;
        }

        canPlace = CheckCanPlace(snapped);
        _cursor.UpdatePosition(snapped, canPlace);
    }

    // ==================================================================
    //  設置試行（左クリック時に呼ばれる）
    // ==================================================================
    public bool TryPlace()
    {
        if (!IsActive || !canPlace || !_cursor.IsVisible) return false;

        if (!CanSummon(Team.Player, SelectedDefinition))
        {
            Debug.Log("[SummonSystem] AP/リソース不足: 召喚不可");
            return false;
        }

        Vector3Int pos = _cursor.LastPosition;
        if (InstantiateUnit(pos, SelectedKind, Team.Player, SelectedDefinition) == null) return false;
        ConsumeSummonResources(Team.Player, SelectedDefinition);

        // ML観測: プレイヤーの召喚をMLシステムに記録
        NotifyMLObservation(pos);

        CancelSummonMode();
        return true;
    }

    // ==================================================================
    //  AP・リソースチェック
    // ==================================================================
    public bool CanSummon(Team team, Kind kind)
    {
        if (unitset == null || unitset.UnitDataMap == null || !unitset.UnitDataMap.TryGetValue(kind, out UnitData data)) return false;
        return CanSummon(team, data);
    }

    public bool CanSummon(Team team, UnitData data)
    {
        if ((team != Team.Player && team != Team.Enemy) || data == null || !data.IsValidForAuthoring
            || !data.AvailableFor(team) || factionState == null) return false;
        if (factionState.GetAP(team) < data.costAP) return false;

        var res = team == Team.Player ? factionState.PlayerResources : factionState.EnemyResources;
        return res.Wood     >= data.costWood
            && res.Stone    >= data.costStone
            && res.Iron     >= data.costIron
            && res.MagicOre >= data.costMagic
            && res.Water    >= data.costWater
            && res.Bread    >= data.costBread
            && res.Citizen  >= data.costCitizen;
    }

    // ==================================================================
    //  リソース消費（共通）
    // ==================================================================
    private void ConsumeSummonResources(Team team, Kind kind)
    {
        if (!unitset.UnitDataMap.TryGetValue(kind, out UnitData data)) return;
        ConsumeSummonResources(team, data);
    }

    private void ConsumeSummonResources(Team team, UnitData data)
    {
        if (data == null) return;

        factionState.ModifyAP(team, -data.costAP);

        var res = team == Team.Player ? factionState.PlayerResources : factionState.EnemyResources;
        res.Wood     -= data.costWood;
        res.Stone    -= data.costStone;
        res.Iron     -= data.costIron;
        res.MagicOre -= data.costMagic;
        res.Water    -= data.costWater;
        res.Bread    -= data.costBread;
        res.Citizen  -= data.costCitizen;

        Debug.Log($"[SummonSystem] {team} / Summon({data.DisplayName})  AP:{data.costAP}  残AP:{factionState.GetAP(team)}");
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
        if (moveGenerator.IsOccupied(posVec)) return false;

        return true;
    }

    // ==================================================================
    //  ユニット生成（共通処理）
    // ==================================================================

    /// <summary>ユニットを指定位置に生成する。プレイヤー・AI共通のコアロジック。</summary>
    private Status InstantiateUnit(Vector3Int pos, Kind kind, Team team, UnitData definition = null)
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

        Status spawnedStatus;
        if (definition != null && !string.IsNullOrWhiteSpace(definition.definitionId))
        {
            var obj = unitset.SpawnUnit(definition, spawnPos, parent, initialTeam: team);
            spawnedStatus = obj != null ? obj.GetComponentInChildren<Status>() : null;
        }
        else if (prefab != null)
        {
            var obj = unitset.SpawnUnit(prefab, spawnPos, parent, initialKind: kind, initialTeam: team, authoredData: definition);
            spawnedStatus = obj != null ? obj.GetComponentInChildren<Status>() : null;
            if (spawnedStatus != null)
            {
                spawnedStatus.team = team;
                spawnedStatus.direction = dir;
            }
        }
        else
        {
            spawnedStatus = CreateFallbackUnit(spawnPos, kind, team, dir, parent, definition);
        }

        if (spawnedStatus == null) return null;

        // プレイヤー召喚時はスキル3択UIで上書き
        if (team == Team.Player && (spawnedStatus.GrowthData == null || !spawnedStatus.GrowthData.useAuthoredAbilities))
            SkillData.AssignFixedSkill(spawnedStatus);

        // UnitRegistry へ登録（壁遮蔽判定・ボスAI等が参照する）
        if (spawnedStatus != null)
            UnitRegistry.Instance?.Register(spawnedStatus);

        // ユニット位置を記録
        moveGenerator.AddOccupied(GridHelper.ToUnitPoint(pos));

        // 視界更新（新規ユニット追加で盤面が変化したため同フレームキャッシュを無効化）
        visionGenerator.MarkVisionDirty();
        visionGenerator.VisionPoint(mapcreate, moveGenerator, turnGenerator.Systems.CrystalSystem);

        Debug.Log($"[SummonSystem] {kind} を ({pos.x}, {Mathf.RoundToInt(spawnY)}, {pos.z}) に召喚 ({team})");
        MatchStats.Instance?.RecordSummon(team);
        return spawnedStatus;
    }

    /// <summary>プレハブ未割当時のフォールバックユニットを生成する</summary>
    private Status CreateFallbackUnit(Vector3 spawnPos, Kind kind, Team team,
                                       Direction dir, Transform parent, UnitData definition = null)
    {
        var obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        obj.transform.position = spawnPos;
        obj.transform.SetParent(parent);
        obj.name = kind.ToString();

        var renderer = obj.GetComponent<Renderer>();
        if (renderer != null)
        {
            PrimitiveMaterialBinding.Apply(renderer, BrandGuide.GetUnitFallbackColor(team));
        }

        var status = obj.AddComponent<Status>();
        status.kind = kind;
        status.team = team;
        status.type = Type.Unit;
        status.direction = dir;

        UnitData data = definition;
        if (data != null || unitset.UnitDataMap.TryGetValue(kind, out data))
        { data.ApplyToStatus(status, 1); data.InitializeAbilities(status); }

        UnitHeadUI.Attach(obj);
        return status;
    }

    // ==================================================================
    //  ロード復元用: コスト消費・スキル選択UIなしのユニット生成
    // ==================================================================

    /// <summary>
    /// セーブデータ復元用にユニットを生成する。
    /// AP/資源を消費せず、スキル選択UIも表示しない（ステータスは呼び出し元が上書きする）。
    /// </summary>
    public Status SpawnUnitForLoad(Kind kind, Team team, Vector3 worldPos, string definitionId = null)
    {
        Transform parent = team == Team.Player ? unitset.PlayerUnit : unitset.EnemyUnit;
        Direction dir = team == Team.Player ? Direction.N : Direction.S;

        if (!string.IsNullOrWhiteSpace(definitionId))
        {
            var data = unitset.GetDefinitionById(definitionId);
            if (data == null)
            {
                Debug.LogWarning($"[SummonSystem] 保存された駒の設定が見つかりません: {definitionId}");
                return null;
            }
            var obj = unitset.SpawnUnit(data, worldPos, parent, initialTeam: team);
            var authored = obj != null ? obj.GetComponentInChildren<Status>() : null;
            if (authored != null)
            {
                UnitRegistry.Instance?.Register(authored);
                moveGenerator.AddOccupied(GridHelper.ToUnitPoint(GridHelper.ToGrid(worldPos)));
            }
            return authored;
        }

        GameObject prefab = null;
        if (prefabMap != null && prefabMap.TryGetValue(kind, out GameObject mapped) && mapped != null)
            prefab = mapped;

        Status status;
        if (prefab != null)
        {
            var obj = unitset.SpawnUnit(prefab, worldPos, parent);
            status = obj.GetComponentInChildren<Status>();
            if (status != null)
            {
                status.team = team;
                status.direction = dir;
            }
        }
        else
        {
            status = CreateFallbackUnit(worldPos, kind, team, dir, parent);
        }

        if (status != null)
        {
            UnitRegistry.Instance?.Register(status);
            moveGenerator.AddOccupied(GridHelper.ToUnitPoint(GridHelper.ToGrid(worldPos)));
        }

        return status;
    }

    // ==================================================================
    //  AI用: カーソル不要の直接召喚
    // ==================================================================

    /// <summary>AI用: 指定位置にユニットを召喚する（カーソル・UI不要）</summary>
    public bool AISummonUnit(Vector3Int pos, Kind kind, Team team)
    {
        if (!CanSummon(team, kind)) return false;
        if (!CheckCanPlaceForTeam(pos, team)) return false;

        if (InstantiateUnit(pos, kind, team) == null) return false;
        ConsumeSummonResources(team, kind);

        Debug.Log($"[SummonSystem] AI({team}) {kind} を ({pos.x},{pos.y},{pos.z}) に召喚");
        return true;
    }

    /// <summary>Authoring tools and AI may summon a specific ID without changing role defaults.</summary>
    public bool AISummonUnit(Vector3Int pos, UnitData data, Team team)
    {
        if (!CanSummon(team, data) || !CheckCanPlaceForTeam(pos, team)) return false;
        if (InstantiateUnit(pos, data.kind, team, data) == null) return false;
        ConsumeSummonResources(team, data);
        return true;
    }

    /// <summary>AI用: 指定チームの領地内で召喚可能な位置一覧を返す</summary>
    public List<Vector3Int> AIGetSummonablePositions(Team team)
    {
        var result = new List<Vector3Int>();
        CollectAISummonablePositions(team, result);
        return result;
    }

    /// <summary>Reuses caller-owned storage without rebuilding a whole-map height dictionary.</summary>
    public void CollectAISummonablePositions(Team team, List<Vector3Int> result)
    {
        if (result == null) throw new System.ArgumentNullException(nameof(result));
        result.Clear();
        var territory = territorysystem.GetTerritory(team);
        if (territory == null) return;

        foreach (var p in territory)
        {
            var pGrid = GridHelper.ToGridXZ(p);
            if (!mapcreate.TryGetHeight(pGrid.x, pGrid.z, out float height)) continue;
            var pos = new Vector3Int(pGrid.x, Mathf.RoundToInt(height), pGrid.z);
            if (CheckCanPlaceForTeam(pos, team))
                result.Add(pos);
        }
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
        if (turnGenerator != null && !turnGenerator.DeveloperPlayerAIWasUsed && turnGenerator.Systems.AICommander != null)
        {
            Vector3 summonPos = new Vector3(pos.x, 0, pos.z);
            turnGenerator.Systems.AICommander.MLIntegration?.ObservePlayerBuild(summonPos, turnGenerator.Context.Turn);
        }
    }

}
