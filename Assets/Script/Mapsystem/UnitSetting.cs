using System.Collections.Generic;
using UnityEngine;

public class UnitSetting : MonoBehaviour
{
    [Header("キング")]
    [SerializeField] GameObject KingPiece;
    [Header("異形")]
    [SerializeField] GameObject StrangePiece;
    [Header("敵の初期指揮官の能力")]
    [Tooltip("初期指揮官専用の能力。通常のBossや第三陣営の設定は変更しません。")]
    [SerializeField] UnitData initialEnemyCommanderData;

    /// <summary>異形の王プレハブへの読み取り公開（WildBoss フォールバック用）</summary>
    public GameObject StrangePiecePrefab => StrangePiece;

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
    public IReadOnlyList<UnitData> AuthoredDefinitions => authoredDefinitions;
    readonly List<UnitData> authoredDefinitions = new List<UnitData>();
    UnitAuthoringCatalog authoringCatalog;
    readonly Dictionary<Kind, UnitData> baselineDefinitions = new Dictionary<Kind, UnitData>();
    readonly List<Status> refreshActors = new List<Status>(64);
    int contentRevision;
    public UnitData GetDefinitionById(string id) => authoringCatalog?.GetById(id);

    private readonly Dictionary<Kind, GameObject> authoredPrefabs = new Dictionary<Kind, GameObject>();
    private readonly List<UnitData> ownedDefaults = new List<UnitData>();
    public bool TryGetAuthoredPrefab(Kind kind, out GameObject prefab) => authoredPrefabs.TryGetValue(kind, out prefab);
    private void OnDestroy()
    {
        foreach (var data in ownedDefaults) if (data != null) Destroy(data);
        ownedDefaults.Clear();
    }

    // ==== 初期化 ====
    private void Awake()
    {
        UnitDataMap = new Dictionary<Kind, UnitData>();

        // Inspector で設定された ScriptableObject を登録
        if (_unitDataList != null)
        {
            foreach (var entry in _unitDataList)
            {
                if (entry != null && entry.data != null)
                    UnitDataMap[entry.kind] = entry.data;
            }
        }

        // UnitStaticData に定義があるが UnitDataMap に未登録の Kind を自動補完
        foreach (var kvp in UnitStaticData.Table)
        {
            if (!UnitDataMap.ContainsKey(kvp.Key))
            {
                UnitDataMap[kvp.Key] = UnitStaticData.CreateUnitData(kvp.Key);
                ownedDefaults.Add(UnitDataMap[kvp.Key]);
            }
        }
        foreach (var pair in UnitDataMap) baselineDefinitions[pair.Key] = pair.Value;
        ReloadAuthoringDefinitions();
    }

    public void ReloadAuthoringDefinitions()
    {
        if (UnitDataMap == null) return;
        UnitDataMap.Clear(); foreach (var pair in baselineDefinitions) UnitDataMap[pair.Key] = pair.Value;
        authoredPrefabs.Clear(); authoredDefinitions.Clear(); authoringCatalog = UnitAuthoringCatalog.Load();
        authoringCatalog?.Apply(UnitDataMap, authoredPrefabs);
        if (authoringCatalog != null) authoredDefinitions.AddRange(authoringCatalog.EnumerateStandaloneDefinitions());
        contentRevision = GameContentRevision.Revision;
    }

    void Update()
    {
        if (contentRevision != GameContentRevision.Revision) RefreshRuntimeBalance();
    }

    UnitData ResolveInitialCommanderData(Team team)
    {
        Kind kind = team == Team.Player ? Kind.King : Kind.Boss;
        // Catalog role replacements take precedence over the initial-slot default.
        if (authoredPrefabs.ContainsKey(kind) && UnitDataMap.TryGetValue(kind, out var replacement)
            && replacement != null && replacement.IsValidForAuthoring) return replacement;
        if (team == Team.Enemy && initialEnemyCommanderData != null && initialEnemyCommanderData.IsValidForAuthoring)
            return initialEnemyCommanderData;
        return UnitDataMap.TryGetValue(kind, out var fallback) ? fallback : null;
    }

    GameObject SpawnInitialCommander(Team team, Vector3 position, Transform parent)
    {
        Kind kind = team == Team.Player ? Kind.King : Kind.Boss;
        GameObject prefab = team == Team.Player ? KingPiece : StrangePiece;
        if (authoredPrefabs.TryGetValue(kind, out var replacement)) prefab = replacement;
        var obj = SpawnUnit(prefab, position, parent, initialKind: kind, initialTeam: team,
            authoredData: ResolveInitialCommanderData(team));
        var actor = obj != null ? obj.GetComponentInChildren<Status>() : null;
        if (actor != null) actor.IsPrimaryCommander = true;
        return obj;
    }

    public void RefreshRuntimeBalance()
    {
        ReloadAuthoringDefinitions();
        CombatRegistry.Collect(refreshActors);
        foreach (var actor in refreshActors)
        {
            if (actor == null || actor.isWildBoss || actor.team == Team.Obstacle) continue;
            if (actor.type == Type.Unit)
            {
                UnitData latest = null;
                if (actor.IsPrimaryCommander && (actor.team == Team.Player || actor.team == Team.Enemy))
                    latest = ResolveInitialCommanderData(actor.team);
                else if (!string.IsNullOrEmpty(actor.unitDefinitionId)) latest = GetDefinitionById(actor.unitDefinitionId);
                else UnitDataMap.TryGetValue(actor.kind, out latest);
                if (latest != null)
                {
                    actor.GrowthData = latest;
                    if (actor.IsPrimaryCommander) actor.unitDefinitionId = latest.definitionId ?? string.Empty;
                }
                actor.GrowthData?.ApplyAbilityIdentity(actor);
            }
            else if (!string.IsNullOrEmpty(actor.authoredFacilityId)) actor.AuthoredFacility = FacilityAuthoringCatalog.Loaded?.Find(actor.authoredFacilityId);
            StatusBalanceRefresh.ApplyLatestDefinition(actor);
            BoardActorPresentation.Apply(actor);
        }
        refreshActors.Clear();
        SpecialSkillRuntime.InvalidateRules();
        var turn = GetComponent<TurnGenerator>();
        if (turn == null) turn = Object.FindFirstObjectByType<TurnGenerator>();
        if (turn == null) return;
        turn.Systems.EconomySystem?.RecalculateCapacity(Team.Player);
        turn.Systems.EconomySystem?.RecalculateCapacity(Team.Enemy);
        turn.Systems.VisionGenerator?.MarkVisionDirty();
        turn.Systems.RefreshVision();
    }

    // ==== 共通生成メソッド ====

    /// <summary>
    /// ユニットを生成してステータスを即座に適用する。
    /// ゲーム中の新規生成はすべてこのメソッド経由で行う（駒の生成と適用を担保）。
    /// </summary>
    public GameObject SpawnUnit(GameObject prefab, Vector3 pos,
                                Transform parent, int level = 1, Kind? initialKind = null, Team? initialTeam = null,
                                UnitData authoredData = null, bool runtimeSpawn = false)
    {
        if (authoredData == null && initialKind.HasValue && authoredPrefabs.TryGetValue(initialKind.Value, out var authored)) prefab = authored;
        if (prefab == null) { Debug.LogError("[UnitSetting] Missing unit prefab"); return null; }
        var obj = Instantiate(prefab, pos, Quaternion.identity, parent);

        var status = obj.GetComponentInChildren<Status>();
        // Initial slots define gameplay identity even when the supplied prefab is only a model.
        if (status == null && initialKind.HasValue) status = obj.AddComponent<Status>();
        if (status == null)
        {
            Debug.LogWarning($"[UnitSetting] {prefab.name} にStatusがありません");
            return obj;
        }

        status.IsPrimaryCommander = false;

        // Authored models may keep their actor component on an offset child.
        if (authoredData != null) obj.transform.position += pos - status.transform.position;

        if (initialKind.HasValue) { status.kind = initialKind.Value; status.type = Type.Unit; }
        if (initialTeam.HasValue)
        {
            status.team = initialTeam.Value;
            status.direction = status.team == Team.Player ? Direction.N : Direction.S;
        }
        if (initialKind.HasValue && obj.GetComponentInChildren<Collider>() == null)
        {
            var collider = obj.AddComponent<BoxCollider>();
            collider.center = Vector3.up * .35f; collider.size = new Vector3(.8f,1f,.8f);
        }

        UnitData data = authoredData;
        if (data != null || UnitDataMap.TryGetValue(status.kind, out data))
            data.ApplyToStatus(status, level);
        else
            Debug.LogWarning($"[UnitSetting] Kind:{status.kind} のUnitDataが未登録です");

        if (data != null) data.InitializeAbilities(status);
        else { SkillData.AssignFixedSkill(status); SpecialAbilityData.AssignFixed(status); }

        // 頭上UI（Lv + HP）をアタッチ
        if (runtimeSpawn) status.MarkRuntimeSpawn(obj);
        BoardActorPresentation.Apply(status, obj);
        UnitHeadUI.Attach(obj);

        return obj;
    }

    /// <summary>Spawns a specific definition without replacing another unit's role defaults.</summary>
    public GameObject SpawnUnit(UnitData data, Vector3 pos, Transform parent, int level = 1, Team? initialTeam = null, bool runtimeSpawn = false)
    {
        if (data == null || !data.IsValidForAuthoring) return null;
        GameObject prefab = data.prefab;
        if (prefab == null) authoringCatalog?.TryGetPrefab(data, out prefab);
        if (prefab != null)
            return SpawnUnit(prefab, pos, parent, level, data.kind, initialTeam ?? data.defaultTeam, data, runtimeSpawn);

        // Data-only definitions still have a visible, selectable fallback model.
        var obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        obj.name = data.DisplayName;
        obj.transform.SetParent(parent, false);
        obj.transform.position = pos;
        var status = obj.AddComponent<Status>();
        status.kind = data.kind; status.type = Type.Unit; status.team = initialTeam ?? data.defaultTeam;
        status.direction = status.team == Team.Player ? Direction.N : Direction.S;
        data.ApplyToStatus(status, level); data.InitializeAbilities(status);
        PrimitiveMaterialBinding.Apply(obj.GetComponent<Renderer>(), BrandGuide.GetUnitFallbackColor(status.team));
        if (runtimeSpawn) status.MarkRuntimeSpawn(obj);
        BoardActorPresentation.Apply(status, obj);
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

        var KingPoint    = new List<Vector3>();
        var StrangePoint = new List<Vector3>();
        foreach (var p in setpos)
        {
            float pkx = Mathf.Abs(p.x - pcp.x);
            float pkz = Mathf.Abs(p.z - pcp.z);
            if (pkx <= 1 && pkz <= 1 && p != pcp) KingPoint.Add(p);

            float pex = Mathf.Abs(p.x - ecp.x);
            float pez = Mathf.Abs(p.z - ecp.z);
            if (pex <= 1 && pez <= 1 && p != ecp) StrangePoint.Add(p);
        }

        // Instantiate を SpawnUnit に置き換え（ステータス適用済み）
        var usedPlayer = new HashSet<Vector3>();
        var usedEnemy = new HashSet<Vector3>();

        if (KingPoint.Count == 0) { Debug.LogError("[UnitSetting] 王の配置候補なし"); return; }
        Vector3 KP = KingPoint[Random.Range(0, KingPoint.Count)];
        SpawnInitialCommander(Team.Player, KP, PlayerUnit);
        usedPlayer.Add(KP);
        Debug.Log("<color=#ffff00ff>[StartSetting]</color>王設置");

        if (StrangePoint.Count == 0) { Debug.LogError("[UnitSetting] 異形の王の配置候補なし"); return; }
        Vector3 SP = StrangePoint[Random.Range(0, StrangePoint.Count)];
        SpawnInitialCommander(Team.Enemy, SP, EnemyUnit);
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
        var candidates = new List<Vector3>();
        foreach (var p in setpos)
        {
            if (p == crystalPos || usedPositions.Contains(p)) continue;
            float dx = Mathf.Abs(p.x - crystalPos.x);
            float dz = Mathf.Abs(p.z - crystalPos.z);
            if (dx <= 5 && dz <= 5) candidates.Add(p);
        }

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

            var obj = SpawnUnit(prefab, pos, parent, initialKind: kind, initialTeam: team);
            var status = obj.GetComponentInChildren<Status>();
            if (status != null)
            {
                status.team = team;
                status.direction = dir;
                BoardActorAnimation.NotifyFacing(status);
            }
            Debug.Log($"<color=#ffff00ff>[StartSetting]</color>{team} {kind} 設置 @({pos.x},{pos.z})");
        }
    }
}
