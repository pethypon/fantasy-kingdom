using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// =====================================================================
//  AIBoardState — 盤面情報の収集・提供
//  AICommanderが毎ターン生成し、各評価関数に渡す
//  ★ AIは敵駒の視界内の情報のみ取得可能（視界外のプレイヤー駒は見えない）
// =====================================================================
public class AIBoardState
{
    // ---- 参照 ----
    readonly MoveGenerator _moveGen;
    readonly AttackGenerator _attackPoint;
    readonly APSystem _apSystem;
    readonly UnitSetting _unitSet;
    readonly CrystalSystem _crystalSystem;
    readonly VisionGenerator _visionGen;
    readonly BuildSystem _buildSystem;
    readonly SummonSystem _summonSystem;
    readonly FactionState _factionState;
    readonly SubCrystalSystem _subCrystalSystem;

    // ---- 盤面データ ----
    public List<Status> AliveEnemyUnits { get; private set; }
    public List<Status> AlivePlayerUnits { get; private set; }
    public Vector3 PlayerCrystalPos { get; private set; }
    public Vector3 EnemyCrystalPos { get; private set; }
    public int EnemyAP { get; private set; }
    public int EnemyCrystalHP { get; private set; }
    public int EnemyCrystalMaxHP { get; private set; }
    public int PlayerCrystalHP { get; private set; }
    public bool PlayerCrystalVisible { get; private set; }

    // ---- 建築/召喚用データ ----
    public List<Vector3Int> BuildablePositions { get; private set; }
    public List<Vector3Int> SummonablePositions { get; private set; }
    public List<FacilityKind> AffordableBuildings { get; private set; }
    public List<Kind> AffordableUnits { get; private set; }
    public int EnemySubCrystals { get; private set; }
    public List<Vector3Int> SubCrystalPlaceable { get; private set; }

    // ---- 経済分析データ ----
    public FactionState.ResourceData EnemyResources { get; private set; }
    public Dictionary<FacilityKind, int> EnemyBuildingCounts { get; private set; }
    public int TurnCount { get; private set; }

    // ---- 地形・ダンジョン（後からAICommanderが注入） ----
    public DungeonSystem DungeonSystem { get; set; }
    public MapCreate MapCreate { get; set; }

    // ---- 索敵・Last Known Position データ（インスタンスフィールド化: 複数AI対応） ----
    // 最後にPlayerユニットを視認した位置とターン (key=ユニットinstanceID)
    readonly Dictionary<int, LastKnownInfo> _lastKnownPlayerPositions;
    // 最後にPlayerクリスタルを視認した位置とターン
    LastKnownInfo _lastKnownPlayerCrystal;
    // 前ターンでPlayerが見えていたか（初接敵検知用）
    bool _hadVisiblePlayersLastTurn;

    // 共有メモリ: 外部から注入することで試合を跨いだ蓄積も可能
    public static Dictionary<int, LastKnownInfo> CreateSharedMemory() => new Dictionary<int, LastKnownInfo>();
    // 今ターンで初めてPlayerを視認したか
    public bool IsFirstContact { get; private set; }

    public struct LastKnownInfo
    {
        public Vector3Int Position;
        public int Turn;
        public bool Valid;
    }

    public AIBoardState(
        MoveGenerator moveGen, AttackGenerator attackPoint,
        APSystem apSystem, UnitSetting unitSet,
        CrystalSystem crystalSystem, VisionGenerator visionGen,
        BuildSystem buildSystem = null, SummonSystem summonSystem = null,
        FactionState factionState = null, SubCrystalSystem subCrystalSystem = null,
        int turnCount = 1,
        Dictionary<int, LastKnownInfo> sharedMemory = null)
    {
        TurnCount = turnCount;
        _lastKnownPlayerPositions = sharedMemory ?? new Dictionary<int, LastKnownInfo>();
        _lastKnownPlayerCrystal = new LastKnownInfo { Position = Vector3Int.zero, Turn = -1, Valid = false };
        _moveGen = moveGen;
        _attackPoint = attackPoint;
        _apSystem = apSystem;
        _unitSet = unitSet;
        _crystalSystem = crystalSystem;
        _visionGen = visionGen;
        _buildSystem = buildSystem;
        _summonSystem = summonSystem;
        _factionState = factionState;
        _subCrystalSystem = subCrystalSystem;

        Refresh();
    }

    // ---- 盤面情報を最新に更新 ----
    public void Refresh()
    {
        _moveGen.UnitPointCore();

        AliveEnemyUnits = CollectUnits(_unitSet.EnemyUnit, Team.Enemy);

        var allPlayerUnits = CollectUnits(_unitSet.PlayerUnit, Team.Player);
        AlivePlayerUnits = FilterByEnemyVision(allPlayerUnits);

        PlayerCrystalPos = _crystalSystem.PCP;
        EnemyCrystalPos = _crystalSystem.ECP;
        EnemyAP = _apSystem.GetAP(Team.Enemy);

        // クリスタルは CrystalSystem の親オブジェクト配下にある
        var eCrystal = FindCrystal(_crystalSystem.Enemycrystal);
        EnemyCrystalHP = eCrystal != null ? eCrystal.HP : 0;
        EnemyCrystalMaxHP = eCrystal != null ? eCrystal.MaxHP : 1;

        PlayerCrystalVisible = IsCellInEnemyVision(PlayerCrystalPos);
        if (PlayerCrystalVisible)
        {
            var pCrystal = FindCrystal(_crystalSystem.Playercrystal);
            PlayerCrystalHP = pCrystal != null ? pCrystal.HP : 0;
        }
        else
        {
            PlayerCrystalHP = -1;
        }

        // 索敵データの更新（Last Known Position）
        RefreshLastKnownData();

        // 建築/召喚情報を更新
        RefreshEconomyData();
    }

    // ---- Enum.GetValues()キャッシュ（GC回避） ----
    static readonly FacilityKind[] _allFacilityKinds = (FacilityKind[])Enum.GetValues(typeof(FacilityKind));
    static readonly Kind[] _summonKinds = { Kind.Knight, Kind.Archer, Kind.Magic, Kind.Assassin,
                                            Kind.Scout, Kind.Priest, Kind.Guardian, Kind.Crossbow,
                                            Kind.Magicsniper, Kind.Bomber };

    // ---- 建築/召喚可能情報を更新 ----
    void RefreshEconomyData()
    {
        // 建築可能位置
        if (_buildSystem != null)
            BuildablePositions = _buildSystem.AIGetBuildablePositions(Team.Enemy);
        else
        {
            if (BuildablePositions == null) BuildablePositions = new List<Vector3Int>();
            else BuildablePositions.Clear();
        }

        // 召喚可能位置
        if (_summonSystem != null)
            SummonablePositions = _summonSystem.AIGetSummonablePositions(Team.Enemy);
        else
        {
            if (SummonablePositions == null) SummonablePositions = new List<Vector3Int>();
            else SummonablePositions.Clear();
        }

        // 購入可能な建築物（リスト再利用）
        if (AffordableBuildings == null) AffordableBuildings = new List<FacilityKind>();
        else AffordableBuildings.Clear();
        if (_buildSystem != null && _factionState != null)
        {
            foreach (var fk in _allFacilityKinds)
            {
                if (_apSystem.CanBuild(Team.Enemy, fk, _factionState))
                    AffordableBuildings.Add(fk);
            }
        }

        // 召喚可能なユニット種（リスト再利用）
        if (AffordableUnits == null) AffordableUnits = new List<Kind>();
        else AffordableUnits.Clear();
        if (_summonSystem != null)
        {
            foreach (var k in _summonKinds)
            {
                if (_summonSystem.CanSummon(Team.Enemy, k))
                    AffordableUnits.Add(k);
            }
        }

        // 経済分析データ
        EnemyResources = _factionState != null ? _factionState.EnemyResources : null;
        EnemyBuildingCounts = CountBuildings();

        // サブクリスタル残り
        EnemySubCrystals = _factionState != null ? _factionState.GetSubCrystals(Team.Enemy) : 0;

        // サブクリスタル設置可能位置（リスト再利用）
        if (SubCrystalPlaceable == null) SubCrystalPlaceable = new List<Vector3Int>();
        else SubCrystalPlaceable.Clear();
        if (_subCrystalSystem != null && EnemySubCrystals > 0 && _moveGen != null)
        {
            foreach (var sp in _moveGen.mapcreate.SetPos)
            {
                var pos = ToCell(sp);
                if (_subCrystalSystem.CanPlaceSubCrystal(pos, Team.Enemy))
                {
                    SubCrystalPlaceable.Add(pos);
                    if (SubCrystalPlaceable.Count >= 5) break;
                }
            }
        }
    }

    // ---- 索敵データ更新 ----
    void RefreshLastKnownData()
    {
        // 初接敵検知: 前ターンでは見えていなかったのに今ターンで見えた
        bool hasVisiblePlayers = AlivePlayerUnits.Count > 0;
        IsFirstContact = hasVisiblePlayers && !_hadVisiblePlayersLastTurn;
        _hadVisiblePlayersLastTurn = hasVisiblePlayers;

        // 視界内のPlayerユニットの位置を記録
        foreach (var pu in AlivePlayerUnits)
        {
            if (pu == null || !pu.gameObject.activeInHierarchy) continue;
            int id = pu.GetInstanceID();
            _lastKnownPlayerPositions[id] = new LastKnownInfo
            {
                Position = ToCell(pu.transform.position),
                Turn = TurnCount,
                Valid = true
            };
        }

        // Playerクリスタルを視認したら記録
        if (PlayerCrystalVisible)
        {
            _lastKnownPlayerCrystal = new LastKnownInfo
            {
                Position = ToCell(PlayerCrystalPos),
                Turn = TurnCount,
                Valid = true
            };
        }
    }

    /// <summary>最後に見たPlayerユニットの位置リスト（信頼度付き: 0=古い 1=新鮮）</summary>
    public List<(Vector3Int pos, float reliability)> GetLastKnownPlayerPositions()
    {
        var result = new List<(Vector3Int, float)>();
        foreach (var kvp in _lastKnownPlayerPositions)
        {
            if (!kvp.Value.Valid) continue;
            int age = TurnCount - kvp.Value.Turn;
            float reliability = Mathf.Clamp01(1f - age * 0.15f); // 7ターンで信頼度0
            if (reliability > 0f)
                result.Add((kvp.Value.Position, reliability));
        }
        return result;
    }

    /// <summary>Playerクリスタルの最終視認情報（未視認ならValid=false）</summary>
    public LastKnownInfo GetLastKnownPlayerCrystal() => _lastKnownPlayerCrystal;

    /// <summary>Playerクリスタル座標を「確定目標」として使ってよいか</summary>
    public bool CanUsePlayerCrystalAsTarget()
    {
        return PlayerCrystalVisible;
    }

    /// <summary>未探索方向の概算ベクトル → AIBoardQuery に委譲</summary>
    public Vector3 GetUnexploredDirection()
        => AIBoardQuery.GetUnexploredDirection(this, _visionGen);

    // ---- 駒の有利度 → AIBoardQuery に委譲 ----
    public float GetAdvantageRatio() => AIBoardQuery.GetAdvantageRatio(this);

    // ---- 移動可能マス ----
    public List<Vector3> GetValidMoves(Status unit)
    {
        var unitPos = unit.transform.position;
        var result = new List<Vector3>();
        _moveGen.MoveCore(unit, unitPos);
        result.AddRange(_moveGen.MovePositions);
        _moveGen.MoveReset();
        return result;
    }

    // ---- 攻撃対象（視界内のみ） ----
    public List<Status> GetAttackTargets(Status unit)
    {
        var unitPos = unit.transform.position;
        var targets = new List<Status>();

        _attackPoint.NormalAttackPData(unit, unitPos);
        if (_attackPoint.AttackP == null) return targets;

        foreach (var pos in _attackPoint.AttackP)
        {
            var cell = _moveGen.Cell(pos);
            foreach (var pu in AlivePlayerUnits)
            {
                if (pu == null || !pu.gameObject.activeInHierarchy) continue;
                var puCell = _moveGen.Cell(pu.transform.position);
                if (puCell == cell) { targets.Add(pu); break; }
            }

            if (PlayerCrystalVisible)
            {
                var pcpCell = _moveGen.Cell(PlayerCrystalPos);
                if (pcpCell == cell)
                {
                    var crystal = FindCrystal(_unitSet.PlayerUnit);
                    if (crystal != null && crystal.HP > 0)
                        targets.Add(crystal);
                }
            }
        }

        _attackPoint.AtkpDestroy();
        return targets;
    }

    // ---- スキル攻撃対象 ----
    public List<Status> GetSkillTargets(Status unit, SkillData skill)
    {
        var targets = new List<Status>();
        if (skill == null || unit.AssignedSkillId < 0) return targets;

        var unitPos = unit.transform.position;
        int dirZ = unit.direction == Direction.S ? -1 : 1;

        switch (skill.Target)
        {
            case SkillTarget.Self:
            case SkillTarget.SelfArea:
                targets.Add(unit);
                break;

            case SkillTarget.AllySingle:
                // 味方ユニットから対象選択
                foreach (var ally in AliveEnemyUnits)
                {
                    if (ally == null || !ally.gameObject.activeInHierarchy || ally == unit) continue;
                    float dist = Vector3.Distance(unitPos, ally.transform.position);
                    if (dist <= 4f) targets.Add(ally);
                }
                break;

            case SkillTarget.EnemySingle:
            case SkillTarget.EnemyOrBuilding:
            case SkillTarget.LowHPEnemy:
            case SkillTarget.FlyingEnemy:
                // スキル攻撃範囲を計算して敵を検索
                _attackPoint.SkillAttackPData(unit, unitPos);
                if (_attackPoint.AttackP != null)
                {
                    foreach (var pos in _attackPoint.AttackP)
                    {
                        var cell = _moveGen.Cell(pos);
                        foreach (var pu in AlivePlayerUnits)
                        {
                            if (pu == null || !pu.gameObject.activeInHierarchy) continue;
                            var puCell = _moveGen.Cell(pu.transform.position);
                            if (puCell == cell) { targets.Add(pu); break; }
                        }
                    }
                }
                _attackPoint.AtkpDestroy();
                break;

            case SkillTarget.DesignatedTile:
            case SkillTarget.AdjacentCenter:
            case SkillTarget.DirectionLine:
            case SkillTarget.DesignatedRow:
                // 範囲スキル：攻撃範囲内に敵が含まれる位置を返す
                _attackPoint.SkillAttackPData(unit, unitPos);
                if (_attackPoint.AttackP != null)
                {
                    foreach (var pos in _attackPoint.AttackP)
                    {
                        var center = ToCell(pos);
                        var areaCells = SkillSystem.GetAreaPositions(skill.Area, center, unit.direction);
                        areaCells = SkillSystem.FilterLineSkillBlocked(areaCells, skill.Area, center, MapCreate, _moveGen);
                        // 範囲内の最初のプレイヤーユニットを探す
                        Status foundTarget = FindFirstUnitInArea(areaCells, AlivePlayerUnits);
                        if (foundTarget != null)
                            targets.Add(foundTarget);
                    }
                }
                _attackPoint.AtkpDestroy();
                break;
        }

        return targets;
    }

    // ---- スキル範囲内の敵ユニット収集 ----
    public List<Status> GetEnemiesInSkillArea(Status unit, SkillData skill, Vector3 targetPos)
        => CollectUnitsInArea(skill, targetPos, unit.direction, AlivePlayerUnits);

    // ---- スキル範囲内の味方ユニット収集 ----
    public List<Status> GetAlliesInSkillArea(Status unit, SkillData skill, Vector3 targetPos)
        => CollectUnitsInArea(skill, targetPos, unit.direction, AliveEnemyUnits);

    List<Status> CollectUnitsInArea(SkillData skill, Vector3 targetPos, Direction dir, List<Status> candidates)
        => AIBoardQuery.CollectUnitsInArea(skill, targetPos, dir, candidates);

    // ---- 味方の最寄り駒距離 → AIBoardQuery に委譲 ----
    public float GetNearestAllyDist(Vector3 pos, Status self)
        => AIBoardQuery.GetNearestAllyDist(this, pos, self);

    // ---- スキルAP消費 ----
    public void ConsumeSkill(Status unit, int apCost)
    {
        _apSystem.ConsumeSkill(Team.Enemy, apCost, unit);
        EnemyAP = _apSystem.GetAP(Team.Enemy);
    }

    // ---- コスト ----
    public int CalcMoveCost(Status unit, Vector3 dest)
        => _apSystem.CalcCost(APSystem.ActionType.Move, unit, unit.transform.position, dest);

    public int CalcAttackCost(Status unit)
        => _apSystem.CalcCost(APSystem.ActionType.Attack, unit);

    // ---- AP消費 ----
    public void ConsumeMove(Status unit, Vector3 dest)
    {
        _apSystem.Consume(Team.Enemy, APSystem.ActionType.Move, unit, unit.transform.position, dest);
        EnemyAP = _apSystem.GetAP(Team.Enemy);
    }

    public void ConsumeAttack(Status unit)
    {
        _apSystem.Consume(Team.Enemy, APSystem.ActionType.Attack, unit);
        EnemyAP = _apSystem.GetAP(Team.Enemy);
    }

    public void RefreshAP()
    {
        EnemyAP = _apSystem.GetAP(Team.Enemy);
    }

    // ================================================================
    //  視界フィルタリング
    // ================================================================
    List<Status> FilterByEnemyVision(List<Status> allPlayerUnits)
    {
        // 視界システムが未初期化の場合は「何も見えない」とする（全公開バグ防止）
        if (_visionGen == null || _visionGen.EnemyVisionBox == null)
            return new List<Status>();

        var visible = new List<Status>();
        foreach (var unit in allPlayerUnits)
        {
            if (unit == null || !unit.gameObject.activeInHierarchy) continue;
            if (IsCellInEnemyVision(unit.transform.position))
                visible.Add(unit);
        }
        return visible;
    }

    // ---- 視界XZルックアップ用キャッシュ (O(1)化) ----
    HashSet<long> _visionXZCache;
    int _visionCacheVersion = -1; // EnemyVisionBoxのCount変化で無効化

    static long PackXZ(int x, int z) => ((long)x << 32) | (uint)z;

    bool IsCellInEnemyVision(Vector3 worldPos)
    {
        if (_visionGen == null || _visionGen.EnemyVisionBox == null) return false;

        // キャッシュ再構築（VisionBoxが変更された場合のみ）
        int curCount = _visionGen.EnemyVisionBox.Count;
        if (_visionXZCache == null || _visionCacheVersion != curCount)
        {
            if (_visionXZCache == null)
                _visionXZCache = new HashSet<long>(curCount);
            else
                _visionXZCache.Clear();
            foreach (var v in _visionGen.EnemyVisionBox)
                _visionXZCache.Add(PackXZ(v.x, v.z));
            _visionCacheVersion = curCount;
        }

        var cell = GridHelper.ToGridXZ(worldPos);
        return _visionXZCache.Contains(PackXZ(cell.x, cell.z));
    }

    // ================================================================
    //  座標ヘルパー — GridHelper に委譲
    // ================================================================

    /// <summary>ワールド座標をセル座標に変換（GridHelper.ToGrid のエイリアス）</summary>
    public static Vector3Int ToCell(Vector3 worldPos)
        => GridHelper.ToGrid(worldPos);

    static Status FindFirstUnitInArea(List<Vector3Int> areaCells, List<Status> units)
        => AIBoardQuery.FindFirstUnitInArea(areaCells, units);

    // ================================================================
    //  ユニット収集ヘルパー
    //  GetComponentsInChildrenの結果をキャッシュして
    //  Transform走査 + コンポーネント取得のコストを削減
    // ================================================================
    static readonly List<Status> _getCompBuffer = new List<Status>(32);

    List<Status> CollectUnits(Transform parent, Team team)
    {
        var list = new List<Status>();
        if (parent == null) return list;
        parent.GetComponentsInChildren<Status>(_getCompBuffer);
        for (int i = 0; i < _getCompBuffer.Count; i++)
        {
            var s = _getCompBuffer[i];
            if (!s.gameObject.activeInHierarchy) continue;
            if (s.team == team && s.type == Type.Unit)
                list.Add(s);
        }
        return list;
    }

    static readonly List<Status> _crystalCompBuffer = new List<Status>(4);

    Status FindCrystal(Transform parent)
    {
        if (parent == null) return null;
        parent.GetComponentsInChildren<Status>(true, _crystalCompBuffer);
        for (int i = 0; i < _crystalCompBuffer.Count; i++)
        {
            if (_crystalCompBuffer[i].kind == Kind.Crystal) return _crystalCompBuffer[i];
        }
        return null;
    }

    // ================================================================
    //  経済分析
    // ================================================================
    // 再利用辞書（GC削減）
    Dictionary<FacilityKind, int> _buildingCountsCache;

    Dictionary<FacilityKind, int> CountBuildings()
    {
        if (_buildingCountsCache == null)
            _buildingCountsCache = new Dictionary<FacilityKind, int>();
        else
            _buildingCountsCache.Clear();

        if (_buildSystem == null) return _buildingCountsCache;
        Transform parent = _buildSystem.GetBuildingParent(Team.Enemy);
        if (parent == null) return _buildingCountsCache;

        foreach (Transform child in parent)
        {
            var s = child.GetComponent<Status>();
            if (s == null || s.HP <= 0) continue;
            _buildingCountsCache.TryGetValue(s.facilityKind, out int cnt);
            _buildingCountsCache[s.facilityKind] = cnt + 1;
        }
        return _buildingCountsCache;
    }

    public int GetBuildingCount(FacilityKind kind)
        => AIBoardQuery.GetBuildingCount(this, kind);

    /// <summary>生産チェーンの不足判定 → AIBoardQuery に委譲</summary>
    public bool HasUpstreamProducer(FacilityKind facility)
        => AIBoardQuery.HasUpstreamProducer(this, facility);

    /// <summary>生産チェーンの数量不足判定 → AIBoardQuery に委譲</summary>
    public List<FacilityKind> DiagnoseProductionChainDeficit()
        => AIBoardQuery.DiagnoseProductionChainDeficit(this);

    // ================================================================
    //  次ターン反撃圏の危険度評価
    // ================================================================

    /// <summary>次ターン反撃圏の危険度 → AIBoardQuery に委譲</summary>
    public int EstimateCounterDamageAt(Vector3 pos, Status self)
        => AIBoardQuery.EstimateCounterDamageAt(this, pos, self);

    /// <summary>指定位置から一定距離内の味方ユニット数 → AIBoardQuery に委譲</summary>
    public int CountAlliesNear(Vector3 pos, Status self, float radius)
        => AIBoardQuery.CountAlliesNear(this, pos, self, radius);

    /// <summary>指定位置に到達可能な味方ヒーラーがいるか → AIBoardQuery に委譲</summary>
    public bool HasHealerInRange(Vector3 pos, float range)
        => AIBoardQuery.HasHealerInRange(this, pos, range);

    /// <summary>指定位置の近くに壁/防衛建築があるか → AIBoardQuery に委譲</summary>
    public bool HasDefensiveStructureNear(Vector3 pos, float range)
        => AIBoardQuery.HasDefensiveStructureNear(_buildSystem, pos, range);

    // ================================================================
    //  索敵・偵察用
    // ================================================================

    /// <summary>新規探索マス数の概算 → AIBoardQuery に委譲</summary>
    public int EstimateNewVisionCells(Vector3 pos)
        => AIBoardQuery.EstimateNewVisionCells(_visionGen, pos);

    /// <summary>探索済み面積の割合 → AIBoardQuery に委譲</summary>
    public float GetExplorationRatio()
        => AIBoardQuery.GetExplorationRatio(_visionGen, _moveGen?.mapcreate);

    /// <summary>経済余剰スコア → AIBoardQuery に委譲</summary>
    public float GetEconomicSurplus()
        => AIBoardQuery.GetEconomicSurplus(this);

    /// <summary>資源ボトルネック度 → AIBoardQuery に委譲</summary>
    public float GetResourceScarcity(string resourceName)
        => AIBoardQuery.GetResourceScarcity(this, resourceName);

    /// <summary>有効マップタイル判定 → AIBoardQuery に委譲</summary>
    public bool IsValidTile(Vector3 pos)
        => AIBoardQuery.IsValidTile(_moveGen, pos);
}
