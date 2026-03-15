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

    // ---- 盤面データ ----
    public List<Status> AliveEnemyUnits { get; private set; }
    // ★ 視界内のプレイヤー駒のみ（視界外は含まない）
    public List<Status> AlivePlayerUnits { get; private set; }
    public Vector3 PlayerCrystalPos { get; private set; }
    public Vector3 EnemyCrystalPos { get; private set; }
    public int EnemyAP { get; private set; }
    public int EnemyCrystalHP { get; private set; }
    public int EnemyCrystalMaxHP { get; private set; }
    public int PlayerCrystalHP { get; private set; }
    // ★ プレイヤークリスタルが視界内にあるか
    public bool PlayerCrystalVisible { get; private set; }

    public AIBoardState(
        MoveGererater moveGen, AttackPointt attackPoint,
        APSystem apSystem, UnitSetting unitSet,
        CrystalSystem crystalSystem, VisionGenerater visionGen)
    {
        _moveGen = moveGen;
        _attackPoint = attackPoint;
        _apSystem = apSystem;
        _unitSet = unitSet;
        _crystalSystem = crystalSystem;
        _visionGen = visionGen;

        Refresh();
    }

    // ---- 盤面情報を最新に更新 ----
    public void Refresh()
    {
        _moveGen.UnitPointCore();

        AliveEnemyUnits = CollectUnits(_unitSet.EnemyUnit, Team.Enemy);

        // ★ 全プレイヤー駒を取得した後、視界フィルタを適用
        var allPlayerUnits = CollectUnits(_unitSet.PlayerUnit, Team.Player);
        AlivePlayerUnits = FilterByEnemyVision(allPlayerUnits);

        PlayerCrystalPos = _crystalSystem.PCP;
        EnemyCrystalPos = _crystalSystem.ECP;
        EnemyAP = _apSystem.GetAP(Team.Enemy);

        // クリスタルHP
        var eCrystal = FindCrystal(_unitSet.EnemyUnit);
        EnemyCrystalHP = eCrystal != null ? eCrystal.HP : 0;
        EnemyCrystalMaxHP = eCrystal != null ? eCrystal.MaxHP : 1;

        // ★ プレイヤークリスタルは視界内にある場合のみHP情報を取得
        PlayerCrystalVisible = IsCellInEnemyVision(PlayerCrystalPos);
        if (PlayerCrystalVisible)
        {
            var pCrystal = FindCrystal(_unitSet.PlayerUnit);
            PlayerCrystalHP = pCrystal != null ? pCrystal.HP : 0;
        }
        else
        {
            PlayerCrystalHP = -1; // 不明
        }
    }

    // ---- 駒の有利度（視界内の情報のみで判断）----
    public float GetAdvantageRatio()
    {
        int enemyPower = AliveEnemyUnits.Sum(u => u.HP + u.ATK);
        // ★ 視界内のプレイヤー駒のみで計算（見えない敵は計算に入らない）
        int playerPower = AlivePlayerUnits.Sum(u => u.HP + u.ATK);
        if (playerPower + enemyPower == 0) return 0f;
        return (enemyPower - playerPower) / (float)(enemyPower + playerPower);
    }

    // ---- 指定駒の移動可能マス一覧 ----
    public List<Vector3> GetValidMoves(Status unit)
    {
        var unitPos = unit.transform.position;
        var result = new List<Vector3>();

        _moveGen.MoveCore(unit, unitPos);
        result.AddRange(_moveGen.MoveUnitP);
        _moveGen.MoveReset(); // 生成されたオブジェクトを即破棄

        return result;
    }

    // ---- 指定駒の攻撃可能な敵一覧（★ 視界内のみ） ----
    public List<Status> GetAttackTargets(Status unit)
    {
        var unitPos = unit.transform.position;
        var targets = new List<Status>();

        // 通常攻撃範囲を計算
        _attackPoint.NormalAttackPData(unit, unitPos);
        if (_attackPoint.AttackP == null) return targets;

        foreach (var pos in _attackPoint.AttackP)
        {
            var cell = _moveGen.Cell(pos);

            // ★ 視界内のプレイヤー駒のみを攻撃対象にする
            foreach (var pu in AlivePlayerUnits)
            {
                if (pu == null || !pu.gameObject.activeInHierarchy) continue;
                var puCell = _moveGen.Cell(pu.transform.position);
                if (puCell == cell)
                {
                    targets.Add(pu);
                    break;
                }
            }

            // ★ クリスタルも視界内にある場合のみ攻撃可能
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

        _attackPoint.AtkpDestroy(); // 生成されたオブジェクトを破棄
        return targets;
    }

    // ---- コスト計算 ----
    public int CalcMoveCost(Status unit, Vector3 dest)
    {
        return _apSystem.CalcCost(APSystem.ActionType.Move, unit,
            unit.transform.position, dest);
    }

    public int CalcAttackCost(Status unit)
    {
        return _apSystem.CalcCost(APSystem.ActionType.Attack, unit);
    }

    // ---- AP消費 ----
    public void ConsumeMove(Status unit, Vector3 dest)
    {
        _apSystem.Consume(Team.Enemy, APSystem.ActionType.Move, unit,
            unit.transform.position, dest);
        EnemyAP = _apSystem.GetAP(Team.Enemy);
    }

    public void ConsumeAttack(Status unit)
    {
        _apSystem.Consume(Team.Enemy, APSystem.ActionType.Attack, unit);
        EnemyAP = _apSystem.GetAP(Team.Enemy);
    }

    // ================================================================
    //  視界フィルタリング
    // ================================================================

    // ★ 敵の視界内にいるプレイヤー駒だけを返す
    List<Status> FilterByEnemyVision(List<Status> allPlayerUnits)
    {
        if (_visionGen == null || _visionGen.EnemyVisionBox == null)
            return allPlayerUnits; // フォールバック: 視界計算未済なら全部見える

        var visible = new List<Status>();
        foreach (var unit in allPlayerUnits)
        {
            if (unit == null || !unit.gameObject.activeInHierarchy) continue;
            if (IsCellInEnemyVision(unit.transform.position))
                visible.Add(unit);
        }
        return visible;
    }

    // ★ 指定位置が敵の視界内にあるか判定
    bool IsCellInEnemyVision(Vector3 worldPos)
    {
        if (_visionGen == null || _visionGen.EnemyVisionBox == null) return true;

        int x = Mathf.RoundToInt(worldPos.x);
        int z = Mathf.RoundToInt(worldPos.z);
        // EnemyVisionBoxはY座標も含むが、XZ一致で判定
        var cellXZ = new Vector3Int(x, 0, z);

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
