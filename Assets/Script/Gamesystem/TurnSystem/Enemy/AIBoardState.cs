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

    public AIBoardState(
        MoveGererater moveGen, AttackPointt attackPoint,
        APSystem apSystem, UnitSetting unitSet,
        CrystalSystem crystalSystem, VisionGenerater visionGen,
        BuildSystem buildSystem = null, SummonSystem summonSystem = null,
        FactionState factionState = null, SubCrystalSystem subCrystalSystem = null)
    {
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

        var eCrystal = FindCrystal(_unitSet.EnemyUnit);
        EnemyCrystalHP = eCrystal != null ? eCrystal.HP : 0;
        EnemyCrystalMaxHP = eCrystal != null ? eCrystal.MaxHP : 1;

        PlayerCrystalVisible = IsCellInEnemyVision(PlayerCrystalPos);
        if (PlayerCrystalVisible)
        {
            var pCrystal = FindCrystal(_unitSet.PlayerUnit);
            PlayerCrystalHP = pCrystal != null ? pCrystal.HP : 0;
        }
        else
        {
            PlayerCrystalHP = -1;
        }

        // 建築/召喚情報を更新
        RefreshEconomyData();
    }

    // ---- 建築/召喚可能情報を更新 ----
    void RefreshEconomyData()
    {
        // 建築可能位置
        if (_buildSystem != null)
            BuildablePositions = _buildSystem.AIGetBuildablePositions(Team.Enemy);
        else
            BuildablePositions = new List<Vector3Int>();

        // 召喚可能位置
        if (_summonSystem != null)
            SummonablePositions = _summonSystem.AIGetSummonablePositions(Team.Enemy);
        else
            SummonablePositions = new List<Vector3Int>();

        // 購入可能な建築物
        AffordableBuildings = new List<FacilityKind>();
        if (_buildSystem != null && _factionState != null)
        {
            foreach (FacilityKind fk in Enum.GetValues(typeof(FacilityKind)))
            {
                if (_apSystem.CanBuild(Team.Enemy, fk, _factionState))
                    AffordableBuildings.Add(fk);
            }
        }

        // 召喚可能なユニット種
        AffordableUnits = new List<Kind>();
        if (_summonSystem != null)
        {
            Kind[] summonKinds = { Kind.Knight, Kind.Archer, Kind.Magic, Kind.Assassin,
                                    Kind.Scout, Kind.Priest, Kind.Guardian, Kind.Crossbow,
                                    Kind.Magicsniper, Kind.Bomber };
            foreach (var k in summonKinds)
            {
                if (_summonSystem.CanSummon(Team.Enemy, k))
                    AffordableUnits.Add(k);
            }
        }

        // サブクリスタル残り
        EnemySubCrystals = _factionState != null ? _factionState.GetSubCrystals(Team.Enemy) : 0;

        // サブクリスタル設置可能位置
        SubCrystalPlaceable = new List<Vector3Int>();
        if (_subCrystalSystem != null && EnemySubCrystals > 0 && _moveGen != null)
        {
            foreach (var sp in _moveGen.mapcreate.SetPos)
            {
                var pos = new Vector3Int(
                    Mathf.RoundToInt(sp.x),
                    Mathf.RoundToInt(sp.y),
                    Mathf.RoundToInt(sp.z));
                if (_subCrystalSystem.CanPlaceSubCrystal(pos, Team.Enemy))
                {
                    SubCrystalPlaceable.Add(pos);
                    if (SubCrystalPlaceable.Count >= 5) break; // 候補は最大5位置
                }
            }
        }
    }

    // ---- 駒の有利度 ----
    public float GetAdvantageRatio()
    {
        int enemyPower = AliveEnemyUnits.Sum(u => u.HP + u.ATK);
        int playerPower = AlivePlayerUnits.Sum(u => u.HP + u.ATK);
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
                // 範囲スキル：攻撃範囲内の座標を取得し、敵が含まれる位置を返す
                _attackPoint.SkillAttackPData(unit, unitPos);
                if (_attackPoint.AttackP != null)
                {
                    foreach (var pos in _attackPoint.AttackP)
                    {
                        // 仮ターゲットとして位置情報のみ保持（実際の範囲はSkillSystem.GetAreaPositionsで計算）
                        var cell = _moveGen.Cell(pos);
                        // 範囲内に敵がいるかチェック
                        var center = new Vector3Int(Mathf.RoundToInt(pos.x), Mathf.RoundToInt(pos.y), Mathf.RoundToInt(pos.z));
                        var areaCells = SkillSystem.GetAreaPositions(skill.Area, center, unit.direction);
                        bool hasEnemy = false;
                        foreach (var ac in areaCells)
                        {
                            foreach (var pu in AlivePlayerUnits)
                            {
                                if (pu == null || !pu.gameObject.activeInHierarchy) continue;
                                int px = Mathf.RoundToInt(pu.transform.position.x);
                                int pz = Mathf.RoundToInt(pu.transform.position.z);
                                if (ac.x == px && ac.z == pz) { hasEnemy = true; break; }
                            }
                            if (hasEnemy) break;
                        }
                        if (hasEnemy)
                        {
                            // ダミーのStatusは返せないので、最初に見つかった範囲内のPlayerUnitを返す
                            foreach (var ac in areaCells)
                            {
                                foreach (var pu in AlivePlayerUnits)
                                {
                                    if (pu == null || !pu.gameObject.activeInHierarchy) continue;
                                    int px = Mathf.RoundToInt(pu.transform.position.x);
                                    int pz = Mathf.RoundToInt(pu.transform.position.z);
                                    if (ac.x == px && ac.z == pz) { targets.Add(pu); break; }
                                }
                                if (targets.Count > 0) break;
                            }
                        }
                    }
                }
                _attackPoint.AtkpDestroy();
                break;
        }

        return targets;
    }

    // ---- スキル範囲内の敵ユニット収集 ----
    public List<Status> GetEnemiesInSkillArea(Status unit, SkillData skill, Vector3 targetPos)
    {
        var enemies = new List<Status>();
        var center = new Vector3Int(Mathf.RoundToInt(targetPos.x), Mathf.RoundToInt(targetPos.y), Mathf.RoundToInt(targetPos.z));
        var areaCells = SkillSystem.GetAreaPositions(skill.Area, center, unit.direction);

        foreach (var ac in areaCells)
        {
            foreach (var pu in AlivePlayerUnits)
            {
                if (pu == null || !pu.gameObject.activeInHierarchy) continue;
                int px = Mathf.RoundToInt(pu.transform.position.x);
                int pz = Mathf.RoundToInt(pu.transform.position.z);
                if (ac.x == px && ac.z == pz) enemies.Add(pu);
            }
        }
        return enemies;
    }

    // ---- スキル範囲内の味方ユニット収集 ----
    public List<Status> GetAlliesInSkillArea(Status unit, SkillData skill, Vector3 targetPos)
    {
        var allies = new List<Status>();
        var center = new Vector3Int(Mathf.RoundToInt(targetPos.x), Mathf.RoundToInt(targetPos.y), Mathf.RoundToInt(targetPos.z));
        var areaCells = SkillSystem.GetAreaPositions(skill.Area, center, unit.direction);

        foreach (var ac in areaCells)
        {
            foreach (var ally in AliveEnemyUnits)
            {
                if (ally == null || !ally.gameObject.activeInHierarchy) continue;
                int ax = Mathf.RoundToInt(ally.transform.position.x);
                int az = Mathf.RoundToInt(ally.transform.position.z);
                if (ac.x == ax && ac.z == az) allies.Add(ally);
            }
        }
        return allies;
    }

    // ---- 味方の最寄り駒距離 ----
    public float GetNearestAllyDist(Vector3 pos, Status self)
    {
        float nearest = float.MaxValue;
        foreach (var u in AliveEnemyUnits)
        {
            if (u == null || !u.gameObject.activeInHierarchy || u == self) continue;
            float d = Vector3.Distance(pos, u.transform.position);
            if (d < nearest) nearest = d;
        }
        return nearest;
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
        if (_visionGen == null || _visionGen.EnemyVisionBox == null)
            return allPlayerUnits;

        var visible = new List<Status>();
        foreach (var unit in allPlayerUnits)
        {
            if (unit == null || !unit.gameObject.activeInHierarchy) continue;
            if (IsCellInEnemyVision(unit.transform.position))
                visible.Add(unit);
        }
        return visible;
    }

    bool IsCellInEnemyVision(Vector3 worldPos)
    {
        if (_visionGen == null || _visionGen.EnemyVisionBox == null) return true;
        int x = Mathf.RoundToInt(worldPos.x);
        int z = Mathf.RoundToInt(worldPos.z);
        foreach (var v in _visionGen.EnemyVisionBox)
        {
            if (v.x == x && v.z == z) return true;
        }
        return false;
    }

    // ================================================================
    //  ヘルパー
    // ================================================================
    List<Status> CollectUnits(Transform parent, Team team)
    {
        var list = new List<Status>();
        if (parent == null) return list;
        foreach (Status s in parent.GetComponentsInChildren<Status>())
        {
            if (!s.gameObject.activeInHierarchy) continue;
            if (s.team == team && s.type == Type.Unit)
                list.Add(s);
        }
        return list;
    }

    Status FindCrystal(Transform parent)
    {
        if (parent == null) return null;
        foreach (Status s in parent.GetComponentsInChildren<Status>(true))
        {
            if (s.kind == Kind.Crystal) return s;
        }
        return null;
    }
}
