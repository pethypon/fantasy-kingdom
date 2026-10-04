using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// =====================================================================
//  AIBoardState — 盤面情報の収集・提供
//  AICommanderが毎ターン生成し、各評価関数に渡す
//  ★ AIは敵駒の視界内の情報のみ取得可能（視界外のプレイヤー駒は見えない）
// =====================================================================
public partial class AIBoardState
{
    internal readonly Dictionary<(Vector3,Status),float> NearestAllyCache = new Dictionary<(Vector3,Status),float>();
    internal readonly Dictionary<(Vector3,Status,float),int> AllyDensityCache = new Dictionary<(Vector3,Status,float),int>();
    internal readonly Dictionary<(Vector3,float),bool> HealerCache = new Dictionary<(Vector3,float),bool>();
    internal readonly Dictionary<(Vector3,Status,int),int> CounterCache = new Dictionary<(Vector3,Status,int),int>();
    public int Generation { get; private set; }
    public readonly ObservedUnitIndex Allies = new ObservedUnitIndex(), Enemies = new ObservedUnitIndex();
    readonly HashSet<Vector3Int> knownCells = new HashSet<Vector3Int>();
    readonly List<Vector3> knownTerrain = new List<Vector3>();
    int terrainGeneration = -1;
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

    /// <summary>The real faction controlled by this board. Legacy Enemy* names mean own side; Player* mean opponents.</summary>
    public Team ActorTeam { get; }
    public AIStrategicGovernor Governor { get; set; }
    StrategicEconomyForecast productionForecast;
    public StrategicProductionDemand ProductionDemand
    {
        get
        {
            if (Governor == null)
            {
                if (productionForecast == null) productionForecast = new StrategicEconomyForecast();
                return productionForecast.Diagnose(this);
            }
            Governor.Evaluate(this);
            return Governor.ProductionDemand;
        }
    }
    public UnitData ResolveUnitDefinition(Kind kind) => _unitSet != null && _unitSet.UnitDataMap.TryGetValue(kind,out var data) ? data : null;
    public bool CanSummonDefinition(UnitData data) => _summonSystem != null && _summonSystem.CanSummon(ActorTeam,data);
    public int NationStarvationCounter => _factionState?.GetNation(ActorTeam)?.StarvationCounter ?? 0;
    public int NationTurnsAlive => _factionState?.GetNation(ActorTeam)?.TurnsAlive ?? TurnCount;
    public int CitizenCapacity => _factionState?.GetCitizenCap(ActorTeam) ?? 5;
    public Team OpponentTeam => ActorTeam == Team.Player ? Team.Enemy : Team.Player;
    public Transform OwnUnitParent => _unitSet == null ? null : ActorTeam == Team.Player ? _unitSet.PlayerUnit : _unitSet.EnemyUnit;
    public Transform OpponentUnitParent => _unitSet == null ? null : ActorTeam == Team.Player ? _unitSet.EnemyUnit : _unitSet.PlayerUnit;
    public Transform OwnCrystalParent => _crystalSystem == null ? null : ActorTeam == Team.Player ? _crystalSystem.Playercrystal : _crystalSystem.Enemycrystal;
    public Transform OpponentCrystalParent => _crystalSystem == null ? null : ActorTeam == Team.Player ? _crystalSystem.Enemycrystal : _crystalSystem.Playercrystal;
    public IReadOnlyCollection<Vector3Int> OwnVisionCells => _visionGen == null ? Array.Empty<Vector3Int>()
        : ActorTeam == Team.Player ? _visionGen.PlayerVisionBox : _visionGen.EnemyVisionBox;
    public IReadOnlyCollection<Vector3Int> OwnExploredCells => _visionGen == null ? Array.Empty<Vector3Int>()
        : ActorTeam == Team.Player ? _visionGen.PlayerExplored : _visionGen.EnemyExplored;
    Vector3 OwnCrystalPosition => ActorTeam == Team.Player ? _crystalSystem.PCP : _crystalSystem.ECP;
    Vector3 OpponentCrystalPosition => ActorTeam == Team.Player ? _crystalSystem.ECP : _crystalSystem.PCP;

    // ---- 盤面データ ----
    // Live board-owned views, updated in place by Refresh. Copy explicitly when a snapshot is needed.
    public List<Status> AliveEnemyUnits { get; private set; } = new List<Status>(32);
    public List<Status> AlivePlayerUnits { get; private set; } = new List<Status>(32);
    readonly List<Status> allPlayerUnits = new List<Status>(32);
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

    public bool IsTerrainKnown(Vector3 cell)
    {
        return knownCells.Contains(GridHelper.ToGridXZ(cell));
    }

    public List<Vector3> KnownTerrain()
    {
        if (terrainGeneration == Generation) return knownTerrain;
        terrainGeneration = Generation; knownTerrain.Clear();
        foreach (var cell in _moveGen.mapcreate.SetPos) if (IsTerrainKnown(cell)) knownTerrain.Add(cell);
        return knownTerrain;
    }

    readonly List<DungeonSystem.DungeonInfo> observedDungeons = new List<DungeonSystem.DungeonInfo>();
    int dungeonGeneration = -1;
    DungeonSystem observedDungeonSource;
    public IReadOnlyList<DungeonSystem.DungeonInfo> ObservedDungeons()
    {
        if (dungeonGeneration == Generation && observedDungeonSource == DungeonSystem) return observedDungeons;
        dungeonGeneration = Generation; observedDungeonSource = DungeonSystem;
        observedDungeons.Clear();
        if (DungeonSystem != null)
            foreach (var d in DungeonSystem.Dungeons)
                if (IsCellInEnemyVision(d.Position)) observedDungeons.Add(d);
        return observedDungeons;
    }

    // ---- 索敵・Last Known Position データ（インスタンスフィールド化: 複数AI対応） ----
    // 最後にPlayerユニットを視認した位置とターン (key=ユニットinstanceID)
    readonly Dictionary<int, LastKnownInfo> _lastKnownPlayerPositions;
    readonly List<int> expiredObservations = new List<int>();
    readonly List<(Vector3Int pos, float reliability)> rememberedPositions = new List<(Vector3Int, float)>();
    int rememberedGeneration = -1;
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
        public Kind Kind;
        public Type Type;
        public Direction Direction;
        public int ObservedAttackPower;
        public BoardActionProfile ActionProfile;
        public Vector3Int PreviousPosition;
        public int PreviousTurn;
        public bool HasPrevious;
    }

    public AIBoardState(
        MoveGenerator moveGen, AttackGenerator attackPoint,
        APSystem apSystem, UnitSetting unitSet,
        CrystalSystem crystalSystem, VisionGenerator visionGen,
        BuildSystem buildSystem = null, SummonSystem summonSystem = null,
        FactionState factionState = null, SubCrystalSystem subCrystalSystem = null,
        int turnCount = 1,
        Dictionary<int, LastKnownInfo> sharedMemory = null,
        Team actorTeam = Team.Enemy)
    {
        if (actorTeam != Team.Player && actorTeam != Team.Enemy)
            throw new ArgumentOutOfRangeException(nameof(actorTeam), "AI supports player or enemy factions.");
        ActorTeam = actorTeam;
        TurnCount = turnCount;
        _lastKnownPlayerPositions = sharedMemory ?? new Dictionary<int, LastKnownInfo>();
        _lastKnownPlayerCrystal = _lastKnownPlayerPositions.TryGetValue(int.MinValue, out var rememberedCrystal)
            ? rememberedCrystal : new LastKnownInfo { Position = Vector3Int.zero, Turn = -1, Valid = false };
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
        Generation++;
        NearestAllyCache.Clear(); AllyDensityCache.Clear(); HealerCache.Clear(); CounterCache.Clear();
        knownCells.Clear();
        if (_visionGen != null) foreach (var cell in OwnExploredCells) knownCells.Add(GridHelper.ToGridXZ(cell));
        if (_visionGen != null) foreach (var cell in OwnVisionCells) knownCells.Add(GridHelper.ToGridXZ(cell));
        _visionCacheVersion = -1; // Visibility can move without changing its cell count.
        _moveGen.UnitPointCore();

        CollectUnits(OwnUnitParent, ActorTeam, AliveEnemyUnits);
        CollectUnits(OpponentUnitParent, OpponentTeam, allPlayerUnits);
        FilterByEnemyVision(allPlayerUnits, AlivePlayerUnits);
        AppendVisibleHostiles(_moveGen.NeutralParent);
        AppendVisibleHostiles(_moveGen.ObstacleParent);
        AppendVisibleHostiles(_buildSystem != null ? _buildSystem.GetBuildingParent(OpponentTeam) : null);

        PlayerCrystalPos = _lastKnownPlayerCrystal.Valid ? (Vector3)_lastKnownPlayerCrystal.Position : Vector3.zero;
        EnemyCrystalPos = OwnCrystalPosition;
        EnemyAP = _apSystem.GetAP(ActorTeam);

        // クリスタルは CrystalSystem の親オブジェクト配下にある
        var eCrystal = FindCrystal(OwnCrystalParent);
        EnemyCrystalHP = eCrystal != null ? eCrystal.HP : 0;
        EnemyCrystalMaxHP = eCrystal != null ? eCrystal.MaxHP : 1;

        PlayerCrystalVisible = IsCellInEnemyVision(OpponentCrystalPosition);
        if (PlayerCrystalVisible)
        {
            PlayerCrystalPos = OpponentCrystalPosition;
            var pCrystal = FindCrystal(OpponentCrystalParent);
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
        Allies.Rebuild(AliveEnemyUnits); Enemies.Rebuild(AlivePlayerUnits);
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
        {
            if (BuildablePositions == null) BuildablePositions = new List<Vector3Int>();
            _buildSystem.CollectAIBuildablePositions(ActorTeam, BuildablePositions);
        }
        else
        {
            if (BuildablePositions == null) BuildablePositions = new List<Vector3Int>();
            else BuildablePositions.Clear();
        }

        // 召喚可能位置
        if (_summonSystem != null)
        {
            if (SummonablePositions == null) SummonablePositions = new List<Vector3Int>();
            _summonSystem.CollectAISummonablePositions(ActorTeam, SummonablePositions);
        }
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
                if (_apSystem.CanBuild(ActorTeam, fk, _factionState))
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
                if (_summonSystem.CanSummon(ActorTeam, k))
                    AffordableUnits.Add(k);
            }
        }

        // 経済分析データ
        EnemyResources = _factionState != null ? _factionState.GetResources(ActorTeam) : null;
        EnemyBuildingCounts = CountBuildings();

        // サブクリスタル残り
        EnemySubCrystals = _factionState != null ? _factionState.GetSubCrystals(ActorTeam) : 0;

        // サブクリスタル設置可能位置（リスト再利用）
        if (SubCrystalPlaceable == null) SubCrystalPlaceable = new List<Vector3Int>();
        else SubCrystalPlaceable.Clear();
        if (_subCrystalSystem != null && EnemySubCrystals > 0 && _moveGen != null && _visionGen != null && !ExpansionCommitted)
        {
            // Iterate only current vision, using the indexed height lookup instead of scanning the whole map.
            foreach (var cell in OwnVisionCells)
            {
                if (!_moveGen.mapcreate.TryGetHeight(cell.x, cell.z, out float height)) continue;
                var pos = ToCell(new Vector3(cell.x, height, cell.z));
                if (_subCrystalSystem.CanPlaceSubCrystal(pos, ActorTeam))
                {
                    SubCrystalPlaceable.Add(pos);
                }
            }
        }
    }

    // ---- 索敵データ更新 ----
    void RefreshLastKnownData()
    {
        expiredObservations.Clear();
        foreach (var entry in _lastKnownPlayerPositions)
            if (entry.Key != int.MinValue && TurnCount - entry.Value.Turn >= 7) expiredObservations.Add(entry.Key);
        foreach (int id in expiredObservations) _lastKnownPlayerPositions.Remove(id);
        // 初接敵検知: 前ターンでは見えていなかったのに今ターンで見えた
        bool hasVisiblePlayers = AlivePlayerUnits.Count > 0;
        IsFirstContact = hasVisiblePlayers && !_hadVisiblePlayersLastTurn;
        _hadVisiblePlayersLastTurn = hasVisiblePlayers;

        // 視界内のPlayerユニットの位置を記録
        foreach (var pu in AlivePlayerUnits)
        {
            if (pu == null || !pu.gameObject.activeInHierarchy) continue;
            int id = pu.GetInstanceID();
            _lastKnownPlayerPositions.TryGetValue(id, out var previous);
            var position = ToCell(pu.transform.position);
            bool moved = previous.Valid && previous.Position != position;
            _lastKnownPlayerPositions[id] = new LastKnownInfo
            {
                Position = position,
                Kind = pu.kind, Type = pu.type, Direction = pu.direction, ObservedAttackPower = Mathf.Max(0, pu.ATK), ActionProfile = BoardActionProfile.For(pu),
                PreviousPosition = moved ? previous.Position : previous.PreviousPosition,
                PreviousTurn = moved ? previous.Turn : previous.PreviousTurn,
                HasPrevious = moved || previous.HasPrevious,
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
                Kind = Kind.Crystal, Type = Type.Building,
                Turn = TurnCount,
                Valid = true
            };
            _lastKnownPlayerPositions[int.MinValue] = _lastKnownPlayerCrystal;
        }
    }

    /// <summary>最後に見たPlayerユニットの位置リスト（信頼度付き: 0=古い 1=新鮮）</summary>
    public List<(Vector3Int pos, float reliability)> GetLastKnownPlayerPositions()
    {
        if (ReconThreatLevel < 4) { rememberedPositions.Clear(); return rememberedPositions; }
        if (rememberedGeneration == Generation) return rememberedPositions;
        rememberedGeneration = Generation;
        var result = rememberedPositions;
        result.Clear();
        foreach (var kvp in _lastKnownPlayerPositions)
        {
            if (kvp.Key == int.MinValue || !kvp.Value.Valid) continue;
            int age = TurnCount - kvp.Value.Turn;
            if (ReconThreatLevel < 10 && age >= 3) continue;
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
        // Terrain discovery is information, not a movement restriction. Use the
        // same legal terrain as player movement; hidden units remain unobserved.
        foreach (var p in _moveGen.mapcreate.SetPos)
        {
            if (!MovePatterns.CanMove(unit, unit.direction, p.x - unitPos.x, p.z - unitPos.z)) continue;
            if (!_moveGen.mapcreate.CanTraverse(unitPos, p)) continue;
            bool occupied = GridHelper.MatchXZ(p, GridHelper.ToGrid(EnemyCrystalPos))
                || (CanUsePlayerCrystalAsTarget() && GridHelper.MatchXZ(p, GridHelper.ToGrid(PlayerCrystalPos)));
            foreach (var ally in AliveEnemyUnits) if (GridHelper.MatchXZ(p, ally.GridPosition)) occupied = true;
            foreach (var enemy in AlivePlayerUnits) if (GridHelper.MatchXZ(p, enemy.GridPosition)) occupied = true;
            if (!occupied) result.Add(p);
        }
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
                    var crystal = FindCrystal(OpponentCrystalParent);
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
        if (skill == null || !AttackPatterns.CanUseSkills(unit) || unit.AssignedSkillId < 0) return targets;

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
        _apSystem.ConsumeSkill(ActorTeam, apCost, unit);
        EnemyAP = _apSystem.GetAP(ActorTeam);
    }

    // ---- コスト ----
    public int CalcMoveCost(Status unit, Vector3 dest)
        => _apSystem.CalcCost(APSystem.ActionType.Move, unit, unit.transform.position, dest);

    public int CalcAttackCost(Status unit)
        => _apSystem.CalcCost(APSystem.ActionType.Attack, unit);

    public int CalcSkillCost(Status unit, SkillData skill)
        => _apSystem.CalcSkillCost(skill.APCost, unit);

    public bool CanBuildDefinition(FacilityDefinitionData definition)
        => definition != null && _buildSystem != null && _buildSystem.GetCostFailure(definition, ActorTeam) == null;

    // ---- AP消費 ----
    public void ConsumeMove(Status unit, Vector3 dest)
    {
        _apSystem.Consume(ActorTeam, APSystem.ActionType.Move, unit, unit.transform.position, dest);
        EnemyAP = _apSystem.GetAP(ActorTeam);
    }

    public void ConsumeAttack(Status unit)
    {
        _apSystem.Consume(ActorTeam, APSystem.ActionType.Attack, unit);
        EnemyAP = _apSystem.GetAP(ActorTeam);
    }

    public void RefreshAP()
    {
        EnemyAP = _apSystem.GetAP(ActorTeam);
    }

    // ================================================================
    //  視界フィルタリング
    // ================================================================
    void FilterByEnemyVision(List<Status> allPlayerUnits, List<Status> visible)
    {
        visible.Clear();
        // 視界システムが未初期化の場合は「何も見えない」とする（全公開バグ防止）
        if (_visionGen == null || OwnVisionCells == null)
            return;
        foreach (var unit in allPlayerUnits)
        {
            if (unit == null || !unit.gameObject.activeInHierarchy) continue;
            if (IsCellInEnemyVision(unit.transform.position))
                visible.Add(unit);
        }
    }

    // ---- 視界XZルックアップ用キャッシュ (O(1)化) ----
    HashSet<long> _visionXZCache;
    int _visionCacheVersion = -1; // EnemyVisionBoxのCount変化で無効化

    static long PackXZ(int x, int z) => ((long)x << 32) | (uint)z;

    bool IsCellInEnemyVision(Vector3 worldPos)
    {
        if (_visionGen == null || OwnVisionCells == null) return false;

        // キャッシュ再構築（VisionBoxが変更された場合のみ）
        int curCount = OwnVisionCells.Count;
        if (_visionXZCache == null || _visionCacheVersion != curCount)
        {
            if (_visionXZCache == null)
                _visionXZCache = new HashSet<long>(curCount);
            else
                _visionXZCache.Clear();
            foreach (var v in OwnVisionCells)
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
    readonly List<Status> _getCompBuffer = new List<Status>(32);

    void CollectUnits(Transform parent, Team team, List<Status> list)
    {
        list.Clear();
        if (parent == null) return;
        parent.GetComponentsInChildren<Status>(_getCompBuffer);
        for (int i = 0; i < _getCompBuffer.Count; i++)
        {
            var s = _getCompBuffer[i];
            if (!s.gameObject.activeInHierarchy) continue;
            if (s.HP <= 0) continue; // 撃破済みだが未非アクティブ化のユニットを除外
            if (s.team == team && s.type == Type.Unit)
                list.Add(s);
        }
    }

    void AppendVisibleHostiles(Transform parent)
    {
        if (parent == null) return;
        parent.GetComponentsInChildren(false, _getCompBuffer);
        foreach (var hostile in _getCompBuffer)
            if (hostile.IsAlive && hostile.team != ActorTeam && IsCellInEnemyVision(hostile.transform.position))
                AlivePlayerUnits.Add(hostile);
    }

    readonly List<Status> _crystalCompBuffer = new List<Status>(4);

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
        Transform parent = _buildSystem.GetBuildingParent(ActorTeam);
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
        => AIBoardQuery.HasDefensiveStructureNear(_buildSystem, pos, range, ActorTeam);

    // ================================================================
    //  索敵・偵察用
    // ================================================================

    /// <summary>新規探索マス数の概算 → AIBoardQuery に委譲</summary>
    public int EstimateNewVisionCells(Vector3 pos)
        => AIBoardQuery.EstimateNewVisionCells(_visionGen, pos, ActorTeam);

    /// <summary>探索済み面積の割合 → AIBoardQuery に委譲</summary>
    public float GetExplorationRatio()
        => AIBoardQuery.GetExplorationRatio(_visionGen, _moveGen?.mapcreate, ActorTeam);

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
