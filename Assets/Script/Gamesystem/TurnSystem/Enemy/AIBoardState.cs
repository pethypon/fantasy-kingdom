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
    readonly MoveGererater _moveGen;
    readonly AttackPointt _attackPoint;
    readonly APSystem _apSystem;
    readonly UnitSetting _unitSet;
    readonly CrystalSystem _crystalSystem;
    readonly VisionGenerater _visionGen;
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

    // ---- 索敵・Last Known Position データ ----
    // 最後にPlayerユニットを視認した位置とターン (key=ユニットinstanceID)
    static Dictionary<int, LastKnownInfo> _lastKnownPlayerPositions = new Dictionary<int, LastKnownInfo>();
    // 最後にPlayerクリスタルを視認した位置とターン
    static LastKnownInfo _lastKnownPlayerCrystal = new LastKnownInfo { Position = Vector3Int.zero, Turn = -1, Valid = false };
    // 前ターンでPlayerが見えていたか（初接敵検知用）
    static bool _hadVisiblePlayersLastTurn = false;
    // 今ターンで初めてPlayerを視認したか
    public bool IsFirstContact { get; private set; }

    public struct LastKnownInfo
    {
        public Vector3Int Position;
        public int Turn;
        public bool Valid;
    }

    public AIBoardState(
        MoveGererater moveGen, AttackPointt attackPoint,
        APSystem apSystem, UnitSetting unitSet,
        CrystalSystem crystalSystem, VisionGenerater visionGen,
        BuildSystem buildSystem = null, SummonSystem summonSystem = null,
        FactionState factionState = null, SubCrystalSystem subCrystalSystem = null,
        int turnCount = 1)
    {
        TurnCount = turnCount;
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

    /// <summary>未探索方向の概算ベクトル（自陣クリスタルから見て未探索が多い方向）</summary>
    public Vector3 GetUnexploredDirection()
    {
        if (_visionGen == null || _visionGen.EnemyExploard == null || _moveGen == null)
            return Vector3.forward;

        Vector3 center = EnemyCrystalPos;
        Vector3 bestDir = Vector3.zero;
        float bestScore = 0f;

        // 8方向を調べて最も未探索セルが多い方向を返す
        Vector3[] dirs = {
            Vector3.forward, Vector3.back, Vector3.left, Vector3.right,
            new Vector3(1,0,1).normalized, new Vector3(1,0,-1).normalized,
            new Vector3(-1,0,1).normalized, new Vector3(-1,0,-1).normalized
        };

        foreach (var dir in dirs)
        {
            int unexplored = 0;
            for (int step = 2; step <= 8; step += 2)
            {
                var checkPos = center + dir * step;
                var cell = new Vector3Int(Mathf.RoundToInt(checkPos.x), 0, Mathf.RoundToInt(checkPos.z));
                if (!_visionGen.EnemyExploard.Contains(cell))
                    unexplored++;
            }
            if (unexplored > bestScore)
            {
                bestScore = unexplored;
                bestDir = dir;
            }
        }

        return bestDir == Vector3.zero ? Vector3.forward : bestDir;
    }

    // ---- 駒の有利度（LINQなし高速版） ----
    public float GetAdvantageRatio()
    {
        int enemyPower = 0;
        for (int i = 0; i < AliveEnemyUnits.Count; i++)
            enemyPower += AliveEnemyUnits[i].HP + AliveEnemyUnits[i].ATK;
        int playerPower = 0;
        for (int i = 0; i < AlivePlayerUnits.Count; i++)
            playerPower += AlivePlayerUnits[i].HP + AlivePlayerUnits[i].ATK;
        if (playerPower + enemyPower == 0) return 0f;
        return (enemyPower - playerPower) / (float)(enemyPower + playerPower);
    }

    // ---- 移動可能マス ----
    public List<Vector3> GetValidMoves(Status unit)
    {
        var unitPos = unit.transform.position;
        var result = new List<Vector3>();
        _moveGen.MoveCore(unit, unitPos);
        result.AddRange(_moveGen.MoveUnitP);
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

    /// <summary>スキル範囲内のユニットを収集する共通メソッド</summary>
    List<Status> CollectUnitsInArea(SkillData skill, Vector3 targetPos, Direction dir, List<Status> candidates)
    {
        var result = new List<Status>();
        var center = ToCell(targetPos);
        var areaCells = SkillSystem.GetAreaPositions(skill.Area, center, dir);

        foreach (var ac in areaCells)
        {
            foreach (var u in candidates)
            {
                if (u == null || !u.gameObject.activeInHierarchy) continue;
                int ux = Mathf.RoundToInt(u.transform.position.x);
                int uz = Mathf.RoundToInt(u.transform.position.z);
                if (SameCellXZ(ac, ux, uz)) result.Add(u);
            }
        }
        return result;
    }

    // ---- 味方の最寄り駒距離 ----
    public float GetNearestAllyDist(Vector3 pos, Status self)
    {
        float nearestSqr = float.MaxValue;
        foreach (var u in AliveEnemyUnits)
        {
            if (u == null || !u.gameObject.activeInHierarchy || u == self) continue;
            float sqr = (pos - u.transform.position).sqrMagnitude;
            if (sqr < nearestSqr) nearestSqr = sqr;
        }
        return nearestSqr < float.MaxValue ? Mathf.Sqrt(nearestSqr) : float.MaxValue;
    }

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

        int x = Mathf.RoundToInt(worldPos.x);
        int z = Mathf.RoundToInt(worldPos.z);
        return _visionXZCache.Contains(PackXZ(x, z));
    }

    // ================================================================
    //  座標ヘルパー
    // ================================================================

    /// <summary>ワールド座標をセル座標(Vector3Int)に変換</summary>
    public static Vector3Int ToCell(Vector3 worldPos)
        => new Vector3Int(Mathf.RoundToInt(worldPos.x), Mathf.RoundToInt(worldPos.y), Mathf.RoundToInt(worldPos.z));

    /// <summary>2つのセル座標が同一のXZ位置かどうか</summary>
    static bool SameCellXZ(Vector3Int a, int x, int z) => a.x == x && a.z == z;

    /// <summary>範囲セル内に存在する最初のユニットを返す（見つからなければnull）</summary>
    static Status FindFirstUnitInArea(List<Vector3Int> areaCells, List<Status> units)
    {
        foreach (var ac in areaCells)
        {
            foreach (var u in units)
            {
                if (u == null || !u.gameObject.activeInHierarchy) continue;
                int ux = Mathf.RoundToInt(u.transform.position.x);
                int uz = Mathf.RoundToInt(u.transform.position.z);
                if (SameCellXZ(ac, ux, uz)) return u;
            }
        }
        return null;
    }

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
            if (_buildingCountsCache.ContainsKey(s.facilityKind))
                _buildingCountsCache[s.facilityKind]++;
            else
                _buildingCountsCache[s.facilityKind] = 1;
        }
        return _buildingCountsCache;
    }

    public int GetBuildingCount(FacilityKind kind)
    {
        return EnemyBuildingCounts.TryGetValue(kind, out int c) ? c : 0;
    }

    /// <summary>
    /// 生産チェーンの不足を判定。
    /// 原料が足りないのに加工施設を建てても無意味なので、
    /// 上流の建物が存在するかチェックする。
    /// </summary>
    public bool HasUpstreamProducer(FacilityKind facility)
    {
        switch (facility)
        {
            // Bakery は Field(小麦)と Well(水)が必要
            case FacilityKind.Bakery:
                return GetBuildingCount(FacilityKind.Field) > 0 &&
                       GetBuildingCount(FacilityKind.Well) > 0;
            // LumberMill は LoggingCamp(木材)が必要
            case FacilityKind.LumberMill:
                return GetBuildingCount(FacilityKind.LoggingCamp) > 0;
            // StoneWorks は Quarry(石材)が必要
            case FacilityKind.StoneWorks:
                return GetBuildingCount(FacilityKind.Quarry) > 0;
            // Smelter は Mine(鉄鉱石+石炭)が必要
            case FacilityKind.Smelter:
                return GetBuildingCount(FacilityKind.Mine) > 0;
            // Field は Well(水)が必要
            case FacilityKind.Field:
                return GetBuildingCount(FacilityKind.Well) > 0;
            default:
                return true; // 上流不要
        }
    }

    /// <summary>
    /// 生産チェーンの数量不足を判定。
    /// 「上流施設が1棟あるか」ではなく「需要に対して生産量が足りるか」を判断する。
    /// 戻り値: 不足施設のFacilityKindリスト（優先度順）
    /// </summary>
    public List<FacilityKind> DiagnoseProductionChainDeficit()
    {
        var needed = new List<FacilityKind>();
        if (EnemyResources == null) return needed;

        var res = EnemyResources;

        // パン不足 → 最優先
        if (res.Bread <= 15)
        {
            if (GetBuildingCount(FacilityKind.Bakery) == 0)
            {
                // Bakery建設に必要な上流を先にチェック
                if (GetBuildingCount(FacilityKind.Field) == 0)
                    needed.Add(FacilityKind.Field);
                if (GetBuildingCount(FacilityKind.Well) == 0)
                    needed.Add(FacilityKind.Well);
                needed.Add(FacilityKind.Bakery);
            }
            else
            {
                // Bakeryはあるが小麦/水が足りない
                if (res.Wheat <= 10 && GetBuildingCount(FacilityKind.Field) < 2)
                    needed.Add(FacilityKind.Field);
                if (res.Water <= 10 && GetBuildingCount(FacilityKind.Well) < 2)
                    needed.Add(FacilityKind.Well);
                // Bakery追加も検討
                if (res.Wheat > 20 && res.Water > 10 && GetBuildingCount(FacilityKind.Bakery) < 2)
                    needed.Add(FacilityKind.Bakery);
            }
        }

        // 加工材不足 → Plank
        if (res.Plank <= 10)
        {
            if (GetBuildingCount(FacilityKind.LoggingCamp) == 0)
                needed.Add(FacilityKind.LoggingCamp);
            if (GetBuildingCount(FacilityKind.LumberMill) == 0 && GetBuildingCount(FacilityKind.LoggingCamp) > 0)
                needed.Add(FacilityKind.LumberMill);
        }

        // CutStone不足
        if (res.CutStone <= 10)
        {
            if (GetBuildingCount(FacilityKind.Quarry) == 0)
                needed.Add(FacilityKind.Quarry);
            if (GetBuildingCount(FacilityKind.StoneWorks) == 0 && GetBuildingCount(FacilityKind.Quarry) > 0)
                needed.Add(FacilityKind.StoneWorks);
        }

        // Iron不足
        if (res.Iron <= 5)
        {
            if (GetBuildingCount(FacilityKind.Mine) == 0)
                needed.Add(FacilityKind.Mine);
            if (GetBuildingCount(FacilityKind.Smelter) == 0 && GetBuildingCount(FacilityKind.Mine) > 0)
                needed.Add(FacilityKind.Smelter);
        }

        // 市民不足
        if (res.Citizen <= 1)
            needed.Add(FacilityKind.House);

        // 基礎資源の基本確保（まだ1棟もない場合）
        if (GetBuildingCount(FacilityKind.Well) == 0 && !needed.Contains(FacilityKind.Well))
            needed.Add(FacilityKind.Well);
        if (GetBuildingCount(FacilityKind.LoggingCamp) == 0 && !needed.Contains(FacilityKind.LoggingCamp))
            needed.Add(FacilityKind.LoggingCamp);
        if (GetBuildingCount(FacilityKind.Quarry) == 0 && !needed.Contains(FacilityKind.Quarry))
            needed.Add(FacilityKind.Quarry);
        if (GetBuildingCount(FacilityKind.Field) == 0 && !needed.Contains(FacilityKind.Field))
            needed.Add(FacilityKind.Field);

        return needed;
    }

    // ================================================================
    //  次ターン反撃圏の危険度評価
    // ================================================================

    /// <summary>
    /// あるマスに駒が立ったとき、次ターンにプレイヤーから受ける予想ダメージ合計。
    /// 視界内のプレイヤー駒それぞれについて、攻撃が届くかを簡易判定する。
    /// </summary>
    public int EstimateCounterDamageAt(Vector3 pos, Status self)
    {
        int totalDmg = 0;
        foreach (var pu in AlivePlayerUnits)
        {
            if (pu == null || !pu.gameObject.activeInHierarchy) continue;
            float maxRange = EstimateAttackRange(pu) + 1.5f; // 移動+攻撃マージン
            float sqrThreshold = maxRange * maxRange;
            if ((pos - pu.transform.position).sqrMagnitude > sqrThreshold) continue;
            int dmg = Mathf.Max(0, 1 + (pu.ATK / 6) + ((pu.ATK / 2) - (self.DEF / 4)));
            totalDmg += dmg;
        }
        return totalDmg;
    }

    /// <summary>駒種から大まかな攻撃射程を返す</summary>
    static float EstimateAttackRange(Status unit)
    {
        switch (unit.kind)
        {
            case Kind.Archer:      return 3f;
            case Kind.Magic:       return 2f;
            case Kind.Crossbow:    return 2f;
            case Kind.Magicsniper: return 4f;
            case Kind.Bomber:      return 3f;
            default:               return 1.5f;
        }
    }

    /// <summary>指定位置から一定距離内の味方ユニット数</summary>
    public int CountAlliesNear(Vector3 pos, Status self, float radius)
    {
        int count = 0;
        float sqrRadius = radius * radius;
        foreach (var u in AliveEnemyUnits)
        {
            if (u == null || !u.gameObject.activeInHierarchy || u == self) continue;
            if ((pos - u.transform.position).sqrMagnitude <= sqrRadius) count++;
        }
        return count;
    }

    /// <summary>指定位置に到達可能な味方ヒーラーがいるか</summary>
    public bool HasHealerInRange(Vector3 pos, float range)
    {
        float sqrRange = range * range;
        foreach (var u in AliveEnemyUnits)
        {
            if (u == null || !u.gameObject.activeInHierarchy) continue;
            if (u.AssignedSkillId < 0) continue;
            if (!SkillData.Table.TryGetValue(u.AssignedSkillId, out var skill)) continue;
            if (skill.FixedHeal > 0 && (pos - u.transform.position).sqrMagnitude <= sqrRange)
                return true;
        }
        return false;
    }

    /// <summary>指定位置の近くに壁/防衛建築があるか</summary>
    public bool HasDefensiveStructureNear(Vector3 pos, float range)
    {
        if (_buildSystem == null) return false;
        Transform parent = _buildSystem.GetBuildingParent(Team.Enemy);
        if (parent == null) return false;
        float sqrRange = range * range;
        foreach (Transform child in parent)
        {
            var s = child.GetComponent<Status>();
            if (s == null || s.HP <= 0) continue;
            if (s.facilityKind == FacilityKind.WoodWall || s.facilityKind == FacilityKind.StoneWall
                || s.facilityKind == FacilityKind.Mortar || s.facilityKind == FacilityKind.Cannon)
            {
                if ((pos - child.position).sqrMagnitude <= sqrRange) return true;
            }
        }
        return false;
    }

    // ================================================================
    //  索敵・偵察用
    // ================================================================

    /// <summary>
    /// ある位置に駒を置いた場合、新たに探索されるマス数を概算する。
    /// Scout等の偵察ユニットが未探索エリアへ向かうべきかの判断に使用。
    /// </summary>
    public int EstimateNewVisionCells(Vector3 pos)
    {
        if (_visionGen == null || _visionGen.EnemyExploard == null) return 0;

        int newCells = 0;
        var c = ToCell(pos);
        int cx = c.x;
        int cz = c.z;
        // Scoutの視界範囲（-2~+2 x -2~+2）を概算チェック
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dz = -2; dz <= 2; dz++)
            {
                var cell = new Vector3Int(cx + dx, 0, cz + dz);
                if (!_visionGen.EnemyExploard.Contains(cell))
                    newCells++;
            }
        }
        return newCells;
    }

    /// <summary>探索済み面積の割合（0～1）</summary>
    public float GetExplorationRatio()
    {
        if (_visionGen == null || _visionGen.EnemyExploard == null || _moveGen == null) return 1f;
        int totalTiles = _moveGen.mapcreate.SetPos.Count;
        if (totalTiles == 0) return 1f;
        return (float)_visionGen.EnemyExploard.Count / totalTiles;
    }

    /// <summary>経済余剰スコア: 維持費に余裕があるかの指標 (0=ギリギリ 1=余裕)</summary>
    public float GetEconomicSurplus()
    {
        if (EnemyResources == null) return 0f;
        // パン・鉄・木を総合的に判断。各30以上で余裕あり
        float breadSurplus = Mathf.Clamp01(EnemyResources.Bread / 30f);
        float ironSurplus = Mathf.Clamp01(EnemyResources.Iron / 20f);
        float woodSurplus = Mathf.Clamp01(EnemyResources.Wood / 20f);
        return (breadSurplus + ironSurplus + woodSurplus) / 3f;
    }

    /// <summary>
    /// 資源のボトルネック度を返す（0〜1、高いほど不足）。
    /// AIが「何を建てるべきか」の判断に使用。30以下で不足感、0で最大不足。
    /// </summary>
    public float GetResourceScarcity(string resourceName)
    {
        if (EnemyResources == null) return 0f;
        int amount = GetResourceAmount(resourceName);
        return amount < 0 ? 0f : Mathf.Clamp01(1f - amount / 30f);
    }

    /// <summary>資源名から現在量を取得（不明なら-1）</summary>
    int GetResourceAmount(string resourceName)
    {
        if (EnemyResources == null) return -1;
        switch (resourceName)
        {
            case "Wood":     return EnemyResources.Wood;
            case "Stone":    return EnemyResources.Stone;
            case "Water":    return EnemyResources.Water;
            case "Wheat":    return EnemyResources.Wheat;
            case "Bread":    return EnemyResources.Bread;
            case "Plank":    return EnemyResources.Plank;
            case "CutStone": return EnemyResources.CutStone;
            case "IronOre":  return EnemyResources.IronOre;
            case "Iron":     return EnemyResources.Iron;
            case "Coal":     return EnemyResources.Coal;
            case "MagicOre": return EnemyResources.MagicOre;
            default:         return -1;
        }
    }
}
