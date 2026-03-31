using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VisionGenerater : MonoBehaviour
{
    public List<Vector3> _setpos;

    // Player が現在見えるマス
    public HashSet<Vector3Int> PlayerVisionBox;
    // Player が一度見たマス
    public HashSet<Vector3Int> PlayerExploard;

    public List<Status> playerunitbox;

    // Enemy が現在見えるマス
    public HashSet<Vector3Int> EnemyVisionBox;
    // Enemy が一度見たマス
    public HashSet<Vector3Int> EnemyExploard;

    public List<Status> enemyunitbox;

    [Header("マップクリエイト")]
    [SerializeField] MapCreate mapcreate;

    [Header("ムーブジェネレーター")]
    [SerializeField] MoveGererater movegenerater;

    [Header("プレイヤームーブ")]
    [SerializeField] PlayerMove playermove;

    [Header("クリスタルシステム")]
    [SerializeField] CrystalSystem crystalsystem;

    [Header("テリトリーシステム")]
    [SerializeField] TerritorySystem territorysystem;

    [Header("ユニットボックス")]
    [SerializeField] Transform PlayerUnit;
    [SerializeField] Transform EnemyUnit;

    [Header("建築物の親（チーム別）")]
    private Transform _playerBuildingParent;
    private Transform _enemyBuildingParent;

    public void SetBuildingParents(Transform playerParent, Transform enemyParent)
    {
        _playerBuildingParent = playerParent;
        _enemyBuildingParent = enemyParent;
    }

    int blockLayerMask;

    // ================================================================
    //  視界キャッシュ（dirty flag パターン）
    //  同一フレーム内の重複再計算を防止し、パフォーマンスを向上させる。
    // ================================================================
    private int _lastVisionFrame = -1;
    private bool _visionDirty = true;

    /// <summary>視界データを無効化する（次回VisionPoint呼び出しで再計算される）</summary>
    public void MarkVisionDirty()
    {
        _visionDirty = true;
    }

    // VisionSetting で使い回す HashSet（GC圧削減）
    private readonly HashSet<Vector3Int> _reusableVisionXZ = new HashSet<Vector3Int>();
    private readonly HashSet<Vector3Int> _reusableExploardXZ = new HashSet<Vector3Int>();

    // 駒の種類ごとの視界データ（Dictionaryで一元管理）
    static readonly Dictionary<Kind, Vector3Int[]> VisionDataMap = new Dictionary<Kind, Vector3Int[]>
    {
        { Kind.Crystal,     RangeVisionBox(-3, 3, -1, 0, -3, 3, true) },
        { Kind.King,        VisionBox(-1, 1, -1, 0, 0, 2, true) },
        { Kind.Knight,      VisionBox(-1, 1, -1, 0, 0, 2, true) },
        { Kind.Archer,      VisionBox(-1, 1, -1, 0, 0, 3, true) },
        { Kind.Magic,       RangeVisionBox(-2, 2, -1, 0, -2, 2, true) },
        { Kind.Assassin,    VisionBox(-1, 1, -1, 0, -1, 2, true) },
        { Kind.Scout,       RangeVisionBox(-2, 2, -1, 0, -2, 2, true) },
        { Kind.Priest,      RangeVisionBox(-1, 1, -1, 0, -1, 1, true) },
        { Kind.Guardian,    RangeVisionBox(-1, 1, -1, 0, 0, 2, true) },
        { Kind.Crossbow,    RangeVisionBox(-1, 1, -1, 0, 0, 2, true) },
        { Kind.Magicsniper, RangeVisionBox(-4, 4, -1, 0, -1, 1, true) },
        { Kind.Bomber,      VisionBox(-1, 1, -1, 0, 0, 3, true) },
        { Kind.SubCrystal,  RangeVisionBox(-2, 2, -1, 0, -2, 2, true) },

        // BOSS: 広めの視界（指揮官として周囲を広く把握）
        { Kind.Boss,        RangeVisionBox(-3, 3, -1, 0, -3, 3, true) },
    };

    static Vector3Int[] VisionBox
        (
            int minx, int maxx,
            int miny, int maxy,
            int minz, int maxz,
            bool PiecePosition
        )
    {
        var list = new List<Vector3Int>();
        for (int visionpointx = minx; visionpointx <= maxx; visionpointx++)
            for (int visionpointy = miny; visionpointy <= maxy; visionpointy++)
                for (int visionpointz = minz; visionpointz <= maxz; visionpointz++)
                {
                    if (!PiecePosition && visionpointx == 0 && visionpointy == 0 && visionpointz == 0) continue;
                    if (visionpointz == 0 && (visionpointx == -1 || visionpointx == 1)) continue;
                    list.Add(new Vector3Int(visionpointx, visionpointy, visionpointz));
                }
        return list.ToArray();
    }

    static Vector3Int[] RangeVisionBox
        (
            int minx, int maxx,
            int miny, int maxy,
            int minz, int maxz,
            bool PiecePosition
        )
    {
        var list = new List<Vector3Int>();
        for (int visionpointx = minx; visionpointx <= maxx; visionpointx++)
            for (int visionpointy = miny; visionpointy <= maxy; visionpointy++)
                for (int visionpointz = minz; visionpointz <= maxz; visionpointz++)
                {
                    if (!PiecePosition && visionpointx == 0 && visionpointy == 0 && visionpointz == 0) continue;
                    list.Add(new Vector3Int(visionpointx, visionpointy, visionpointz));
                }
        return list.ToArray();
    }

    public void Awake()
    {
        blockLayerMask = LayerMask.GetMask("Block");

        if (PlayerVisionBox == null) PlayerVisionBox = new HashSet<Vector3Int>();
        if (PlayerExploard == null) PlayerExploard = new HashSet<Vector3Int>();
        if (EnemyVisionBox == null) EnemyVisionBox = new HashSet<Vector3Int>();
        if (EnemyExploard == null) EnemyExploard = new HashSet<Vector3Int>();
    }

    // ==== ヘルパーメソッド ====

    /// <summary>
    /// Transformの子からStatusコンポーネントを収集してリストに入れる
    /// </summary>
    void CollectStatuses(Transform parent, List<Status> result)
    {
        result.Clear();
        foreach (Transform child in parent)
        {
            if (child == null) continue;
            Status status = child.GetComponentInChildren<Status>();
            if (status != null)
            {
                result.Add(status);
            }
        }
    }

    /// <summary>
    /// StatusのVisionCellを初期化→視界計算し、結果をtargetSetにマージする
    /// </summary>
    void CalculateAndMergeVision(Status status, MapCreate mapcreate, CrystalSystem crystalsystem, HashSet<Vector3Int> targetSet)
    {
        if (status == null) return;

        if (status.VisionCell == null)
        {
            status.VisionCell = new HashSet<Vector3Int>();
        }

        status.VisionCell.Clear();
        VisionCreate(status, mapcreate, crystalsystem);
        targetSet.UnionWith(status.VisionCell);
    }

    /// <summary>
    /// 駒の向きに応じてオフセットを変換する
    /// </summary>
    static Vector3Int ApplyDirection(Vector3Int offset, Direction direction)
    {
        switch (direction)
        {
            case Direction.S:
                return new Vector3Int(-offset.x, offset.y, -offset.z);
            case Direction.N:
            default:
                return offset;
        }
    }

    /// <summary>
    /// Crystal用：上方向からRaycastで地表の高さを取得し視界セルに追加する
    /// </summary>
    void RaycastCrystalVision(Status status, MapCreate mapcreate, int px, int py, int pz)
    {
        Vector3 goal = new Vector3(px, py, pz) + Vector3.up * 0.5f;
        float startHigh = mapcreate.maxY + 10f;
        Vector3 visionstart = goal + Vector3.up * startHigh;
        float distance = startHigh + 20f;

        if (Physics.Raycast(visionstart, Vector3.down, out var hit, distance, blockLayerMask))
        {
            int hitx = Mathf.RoundToInt(hit.collider.transform.position.x);
            int hitz = Mathf.RoundToInt(hit.collider.transform.position.z);

            if (hitx == px && hitz == pz)
            {
                int hity = Mathf.RoundToInt(hit.collider.transform.position.y);
                status.VisionCell.Add(new Vector3Int(px, hity, pz));
            }
            else
            {
                status.VisionCell.Add(new Vector3Int(px, 0, pz));
            }
        }
        else
        {
            status.VisionCell.Add(new Vector3Int(px, 0, pz));
        }
    }

    /// <summary>
    /// Crystal以外：駒位置から目標へ直線Raycastし障害物がなければ視界セルに追加する
    /// </summary>
    void RaycastDirectVision(Status status, int statusX, int statusY, int statusZ, int px, int py, int pz)
    {
        Vector3 start = new Vector3(statusX, statusY, statusZ) + Vector3.up * 0.5f;
        Vector3 goal = new Vector3(px, py, pz) + Vector3.up * 0.5f;
        Vector3 direction = goal - start;
        float distance = direction.magnitude;

        if (distance <= 0.001f)
        {
            status.VisionCell.Add(new Vector3Int(px, py, pz));
            return;
        }

        if (Physics.Raycast(start, direction.normalized, out var hit, distance, blockLayerMask))
        {
            int hitx = Mathf.RoundToInt(hit.collider.transform.position.x);
            int hity = Mathf.RoundToInt(hit.collider.transform.position.y);
            int hitz = Mathf.RoundToInt(hit.collider.transform.position.z);

            if (px == hitx && py == hity && pz == hitz)
            {
                status.VisionCell.Add(new Vector3Int(px, py, pz));
            }
        }
        else
        {
            status.VisionCell.Add(new Vector3Int(px, py, pz));
        }
    }

    /// <summary>
    /// Fogオブジェクトの表示/非表示を制御する
    /// showOnExploard=false: 視界外かつ未探索のとき表示（完全な霧）
    /// showOnExploard=true:  視界外かつ探索済みのとき表示（探索済み霧）
    /// </summary>
    void SetFogVisibility(Transform parent, HashSet<Vector3Int> visionXZ, HashSet<Vector3Int> exploardXZ, bool showOnExploard)
    {
        if (parent == null) return;

        foreach (Transform Temporary in parent)
        {
            int x = Mathf.RoundToInt(Temporary.position.x);
            int z = Mathf.RoundToInt(Temporary.position.z);
            var data = new Vector3Int(x, 0, z);

            bool nowVision = visionXZ.Contains(data);
            bool nowExploard = exploardXZ.Contains(data);

            if (showOnExploard)
            {
                Temporary.gameObject.SetActive(!nowVision && nowExploard);
            }
            else
            {
                Temporary.gameObject.SetActive(!nowVision && !nowExploard);
            }
        }
    }

    /// <summary>
    /// 視界内かどうかでRendererの有効/無効を切り替える
    /// </summary>
    void SetRendererVisibility(IEnumerable targets, HashSet<Vector3Int> visionXZ)
    {
        if (targets == null) return;

        foreach (Transform Temporary in targets)
        {
            int x = Mathf.RoundToInt(Temporary.position.x);
            int z = Mathf.RoundToInt(Temporary.position.z);
            var data = new Vector3Int(x, 0, z);

            bool visible = visionXZ.Contains(data);
            foreach (var renderer in Temporary.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = visible;
            }
            // WorldSpace Canvas（HeadUI等）も連動して表示/非表示にする
            foreach (var canvas in Temporary.GetComponentsInChildren<Canvas>(true))
            {
                canvas.gameObject.SetActive(visible);
            }
        }
    }

    // ==== メインメソッド ====

    public void VisionPoint(MapCreate mapcreate, MoveGererater movegenerater, CrystalSystem crystalsystem)
    {
        if (mapcreate == null || movegenerater == null || crystalsystem == null)
        {
            Debug.LogWarning("[VisionGenerater] VisionPoint に null 引数が渡されました");
            return;
        }

        // 同一フレーム内での重複再計算を防止
        int currentFrame = Time.frameCount;
        if (!_visionDirty && _lastVisionFrame == currentFrame)
            return;

        _lastVisionFrame = currentFrame;
        _visionDirty = false;

        this.mapcreate = mapcreate;
        this.movegenerater = movegenerater;
        this.crystalsystem = crystalsystem;

        if (PlayerVisionBox != null) PlayerVisionBox.Clear();
        if (EnemyVisionBox != null) EnemyVisionBox.Clear();

        _setpos = mapcreate.SetPos;

        // Player、Enemyの駒のStatusを収集する
        if (playerunitbox == null) playerunitbox = new List<Status>();
        CollectStatuses(PlayerUnit, playerunitbox);

        if (enemyunitbox == null) enemyunitbox = new List<Status>();
        CollectStatuses(EnemyUnit, enemyunitbox);

        // Player駒の視界計算
        foreach (Status status in playerunitbox)
        {
            CalculateAndMergeVision(status, mapcreate, crystalsystem, PlayerVisionBox);
        }

        // Playerクリスタル駒の視界計算
        foreach (Transform Temporary in crystalsystem.Playercrystal)
        {
            Status status = Temporary.GetComponentInChildren<Status>();
            CalculateAndMergeVision(status, mapcreate, crystalsystem, PlayerVisionBox);
        }

        // Player建築物（サブクリスタル）の視界計算
        if (_playerBuildingParent != null)
        {
            foreach (Transform child in _playerBuildingParent)
            {
                if (child == null || !child.gameObject.activeInHierarchy) continue;
                Status bStatus = child.GetComponent<Status>();
                if (bStatus != null && bStatus.kind == Kind.SubCrystal)
                {
                    CalculateAndMergeVision(bStatus, mapcreate, crystalsystem, PlayerVisionBox);
                }
            }
        }

        // Enemy駒の視界計算（AI視界制限に使用）
        foreach (Status status in enemyunitbox)
        {
            CalculateAndMergeVision(status, mapcreate, crystalsystem, EnemyVisionBox);
        }

        // Enemyクリスタル駒の視界計算
        foreach (Transform Temporary in crystalsystem.Enemycrystal)
        {
            Status status = Temporary.GetComponentInChildren<Status>();
            CalculateAndMergeVision(status, mapcreate, crystalsystem, EnemyVisionBox);
        }

        // Enemy建築物（サブクリスタル）の視界計算
        if (_enemyBuildingParent != null)
        {
            foreach (Transform child in _enemyBuildingParent)
            {
                if (child == null || !child.gameObject.activeInHierarchy) continue;
                Status bStatus = child.GetComponent<Status>();
                if (bStatus != null && bStatus.kind == Kind.SubCrystal)
                {
                    CalculateAndMergeVision(bStatus, mapcreate, crystalsystem, EnemyVisionBox);
                }
            }
        }

        // VisionBoxをExploardに入れる
        PlayerExploard.UnionWith(PlayerVisionBox);
        EnemyExploard.UnionWith(EnemyVisionBox);
        VisionSetting(mapcreate);
    }

    public void VisionCreate(Status status, MapCreate mapcreate, CrystalSystem crystalsystem)
    {
        if (!VisionDataMap.TryGetValue(status.kind, out Vector3Int[] visionData))
        {
            return;
        }

        int statusX = Mathf.RoundToInt(status.transform.position.x);
        int statusY = Mathf.RoundToInt(status.transform.position.y);
        int statusZ = Mathf.RoundToInt(status.transform.position.z);

        // Special Ability: 監視眼（視界+1）+ StatusEffect の視界修飾
        int visionBonus = SpecialAbilitySystem.GetVisionBonus(status)
                        + StatusEffectSystem.GetVisionModifier(status);

        foreach (Vector3Int p in visionData)
        {
            Vector3Int directionP = ApplyDirection(p, status.direction);

            int px = statusX + directionP.x;
            int py = statusY + directionP.y;
            int pz = statusZ + directionP.z;

            if (px < 0 || px >= mapcreate.maxX) continue;
            if (pz < 0 || pz >= mapcreate.maxZ) continue;
            if (py < mapcreate.minY || py > mapcreate.maxY) continue;

            if (status.kind == Kind.Crystal || status.kind == Kind.SubCrystal)
            {
                RaycastCrystalVision(status, mapcreate, px, py, pz);
            }
            else
            {
                RaycastDirectVision(status, statusX, statusY, statusZ, px, py, pz);
            }
        }

        // Special Ability: 監視眼 — 視界ボーナス分の追加マスを計算
        if (visionBonus > 0)
        {
            ApplyVisionBonus(status, mapcreate, statusX, statusY, statusZ, visionBonus);
        }
    }

    /// <summary>
    /// 視界ボーナス分の追加マスを既存視界の外周に拡張する
    /// </summary>
    private void ApplyVisionBonus(Status status, MapCreate mapcreate,
                                   int statusX, int statusY, int statusZ, int bonus)
    {
        // 既存視界セルの外周1マスを追加（bonus回繰り返し）
        var currentVision = new HashSet<Vector3Int>(status.VisionCell);
        for (int b = 0; b < bonus; b++)
        {
            var expansion = new HashSet<Vector3Int>();
            foreach (var cell in currentVision)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int nx = cell.x + dx;
                        int nz = cell.z + dz;
                        if (nx < 0 || nx >= mapcreate.maxX) continue;
                        if (nz < 0 || nz >= mapcreate.maxZ) continue;

                        var newCell = new Vector3Int(nx, cell.y, nz);
                        if (!status.VisionCell.Contains(newCell))
                        {
                            // Raycast で遮蔽チェック
                            RaycastDirectVision(status, statusX, statusY, statusZ, nx, cell.y, nz);
                            if (status.VisionCell.Contains(newCell))
                                expansion.Add(newCell);
                        }
                    }
                }
            }
            currentVision.UnionWith(expansion);
        }
    }

    public void VisionSetting(MapCreate mapcreate)
    {
        if (mapcreate == null) return;

        // 再利用可能な HashSet でGC圧を削減
        _reusableVisionXZ.Clear();
        _reusableExploardXZ.Clear();

        foreach (var Temporary in PlayerVisionBox)
        {
            _reusableVisionXZ.Add(new Vector3Int(Temporary.x, 0, Temporary.z));
        }
        foreach (var Temporary in PlayerExploard)
        {
            _reusableExploardXZ.Add(new Vector3Int(Temporary.x, 0, Temporary.z));
        }

        var playervisionXZ = _reusableVisionXZ;
        var playerexploardXZ = _reusableExploardXZ;

        // Fog表示制御（視界外かつ未探索 → 完全な霧を表示）
        SetFogVisibility(mapcreate.FogParent, playervisionXZ, playerexploardXZ, false);
        SetFogVisibility(mapcreate.FogBoardParent, playervisionXZ, playerexploardXZ, false);

        // Fog表示制御（視界外かつ探索済み → 探索済み霧を表示）
        SetFogVisibility(mapcreate.FogExploardBoardParent, playervisionXZ, playerexploardXZ, true);
        SetFogVisibility(mapcreate.FogExploardParent, playervisionXZ, playerexploardXZ, true);

        // 駒の視界外の敵を見えないようにする
        SetRendererVisibility(EnemyUnit, playervisionXZ);
        SetRendererVisibility(crystalsystem.Enemycrystal, playervisionXZ);
        SetRendererVisibility(territorysystem.Enemyterritory, playervisionXZ);

        // 敵建築物をプレイヤー視界外で非表示にする
        if (_enemyBuildingParent != null)
        {
            SetRendererVisibility(_enemyBuildingParent, playervisionXZ);
        }
    }
}
