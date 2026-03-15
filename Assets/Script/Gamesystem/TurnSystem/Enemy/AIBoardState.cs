using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// =====================================================================
//  AIBoardState — 盤面情報の収集・提供
//  AICommanderが毎ターン生成し、各評価関数に渡す
// =====================================================================
public class AIBoardState
{
    // ---- 参照 ----
    readonly MoveGererater _moveGen;
    readonly AttackPointt _attackPoint;
    readonly APSystem _apSystem;
    readonly UnitSetting _unitSet;
    readonly CrystalSystem _crystalSystem;

    // ---- 盤面データ ----
    public List<Status> AliveEnemyUnits { get; private set; }
    public List<Status> AlivePlayerUnits { get; private set; }
    public Vector3 PlayerCrystalPos { get; private set; }
    public Vector3 EnemyCrystalPos { get; private set; }
    public int EnemyAP { get; private set; }
    public int EnemyCrystalHP { get; private set; }
    public int EnemyCrystalMaxHP { get; private set; }
    public int PlayerCrystalHP { get; private set; }

    public AIBoardState(
        MoveGererater moveGen, AttackPointt attackPoint,
        APSystem apSystem, UnitSetting unitSet,
        CrystalSystem crystalSystem)
    {
        _moveGen = moveGen;
        _attackPoint = attackPoint;
        _apSystem = apSystem;
        _unitSet = unitSet;
        _crystalSystem = crystalSystem;

        Refresh();
    }

    // ---- 盤面情報を最新に更新 ----
    public void Refresh()
    {
        _moveGen.UnitPointCore();

        AliveEnemyUnits = CollectUnits(_unitSet.EnemyUnit, Team.Enemy);
        AlivePlayerUnits = CollectUnits(_unitSet.PlayerUnit, Team.Player);

        PlayerCrystalPos = _crystalSystem.PCP;
        EnemyCrystalPos = _crystalSystem.ECP;
        EnemyAP = _apSystem.GetAP(Team.Enemy);

        // クリスタルHP
        var eCrystal = FindCrystal(_unitSet.EnemyUnit);
        EnemyCrystalHP = eCrystal != null ? eCrystal.HP : 0;
        EnemyCrystalMaxHP = eCrystal != null ? eCrystal.MaxHP : 1;
        var pCrystal = FindCrystal(_unitSet.PlayerUnit);
        PlayerCrystalHP = pCrystal != null ? pCrystal.HP : 0;
    }

    // ---- 駒の有利度（正なら敵有利、負ならプレイヤー有利）----
    public float GetAdvantageRatio()
    {
        int enemyPower = AliveEnemyUnits.Sum(u => u.HP + u.ATK);
        int playerPower = AlivePlayerUnits.Sum(u => u.HP + u.ATK);
        if (playerPower + enemyPower == 0) return 0f;
        return (enemyPower - playerPower) / (float)(enemyPower + playerPower);
    }

    // ---- 指定駒の移動可能マス一覧 ----
    public List<Vector3> GetValidMoves(Status unit)
    {
        // MoveGererater.MoveCore を使って移動先を計算
        // MoveCreate() は呼ばず、データだけ取得
        var setpos = _moveGen.mapcreate.SetPos;
        var unitPos = unit.transform.position;

        // MovePredicateMapはprivateなので、MoveGereraterのMoveCore()を呼んでデータを取る
        // ただしMoveCore()はMoveCreate()も呼ぶので、代わりに直接計算
        var occupied = _moveGen.UnitPointData;
        var result = new List<Vector3>();

        // MoveGererater.MoveCore のロジックを再利用
        _moveGen.MoveCore(unit, unitPos);
        result.AddRange(_moveGen.MoveUnitP);
        _moveGen.MoveReset(); // 生成されたオブジェクトを即破棄

        return result;
    }

    // ---- 指定駒の攻撃可能な敵一覧 ----
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
            // この位置にいるプレイヤー駒を探す
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
            // クリスタルチェック
            var pcpCell = _moveGen.Cell(PlayerCrystalPos);
            if (pcpCell == cell)
            {
                // クリスタルのStatusを探す
                var crystal = FindCrystal(_unitSet.PlayerUnit);
                if (crystal != null && crystal.HP > 0)
                    targets.Add(crystal);
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
