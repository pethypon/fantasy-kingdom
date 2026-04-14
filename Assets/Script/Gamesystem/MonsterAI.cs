using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 魔物陣営の行動AI。
/// - 視界内にPlayer/Enemyユニットがいたら追跡・攻撃
/// - いなければランダム移動
/// - ボスは縄張り内を巡回し、侵入者を攻撃
/// </summary>
public class MonsterAI
{
    private readonly MonsterSystem monsterSystem;
    private readonly MoveGenerator moveGenerator;
    private readonly MapCreate mapCreate;
    private readonly UnitSetting unitSetting;

    /// <summary>魔物の視界半径</summary>
    private const int MonsterVisionRange = 3;
    /// <summary>ボスの視界半径</summary>
    private const int BossVisionRange = 5;

    public MonsterAI(MonsterSystem monsterSystem, MoveGenerator moveGenerator,
                     MapCreate mapCreate, UnitSetting unitSetting)
    {
        this.monsterSystem = monsterSystem;
        this.moveGenerator = moveGenerator;
        this.mapCreate = mapCreate;
        this.unitSetting = unitSetting;
    }

    /// <summary>
    /// 全魔物の行動を実行する。
    /// MonsterTurn から呼ばれる。
    /// </summary>
    public void ExecuteAllActions()
    {
        var monsters = monsterSystem.GetAliveMonsters();
        foreach (var monster in monsters)
        {
            if (monster == null || monster.HP <= 0) continue;
            ExecuteOneUnit(monster);
        }
    }

    private void ExecuteOneUnit(Status monster)
    {
        Vector3Int myPos = GridHelper.ToGridXZ(monster.transform.position);
        bool isBoss = monster.IsBoss;
        int visionRange = isBoss ? BossVisionRange : MonsterVisionRange;

        // 1. 視界内の敵（Player + Enemy）を探す
        Status nearestTarget = FindNearestTarget(myPos, visionRange);

        if (nearestTarget != null)
        {
            Vector3Int targetPos = GridHelper.ToGridXZ(nearestTarget.transform.position);
            int dist = GridHelper.ChebyshevDistance(myPos, targetPos);

            // 隣接していたら攻撃
            if (dist <= 1)
            {
                PerformAttack(monster, nearestTarget);
                return;
            }

            // 追跡: ターゲットに向かって1歩移動
            Vector3Int moveTarget = GetStepToward(myPos, targetPos, monster);
            if (moveTarget != myPos)
            {
                MoveUnit(monster, myPos, moveTarget);
                return;
            }
        }

        // 2. ボスは縄張り外に出たら戻る
        if (isBoss && !monsterSystem.IsInBossTerritory(myPos))
        {
            Vector3Int homeStep = GetStepToward(myPos, monsterSystem.BossTerritoryCenter, monster);
            if (homeStep != myPos)
            {
                MoveUnit(monster, myPos, homeStep);
                return;
            }
        }

        // 3. ランダム移動
        RandomMove(monster, myPos);
    }

    /// <summary>視界範囲内で最も近いPlayer/Enemyユニットを探す</summary>
    private Status FindNearestTarget(Vector3Int myPos, int range)
    {
        Status nearest = null;
        int nearestDist = int.MaxValue;

        // Player ユニットを走査
        ScanTeamForTarget(unitSetting.PlayerUnit, myPos, range, ref nearest, ref nearestDist);
        // Enemy ユニットを走査
        ScanTeamForTarget(unitSetting.EnemyUnit, myPos, range, ref nearest, ref nearestDist);

        return nearest;
    }

    private void ScanTeamForTarget(Transform parent, Vector3Int myPos, int range,
                                    ref Status nearest, ref int nearestDist)
    {
        if (parent == null) return;
        foreach (Transform child in parent)
        {
            if (child == null || !child.gameObject.activeInHierarchy) continue;
            var s = child.GetComponentInChildren<Status>();
            if (s == null || s.HP <= 0) continue;

            Vector3Int pos = GridHelper.ToGridXZ(s.transform.position);
            int dist = GridHelper.ChebyshevDistance(myPos, pos);

            if (dist <= range && dist < nearestDist)
            {
                nearestDist = dist;
                nearest = s;
            }
        }
    }

    /// <summary>目標に向かって1歩移動する先を返す</summary>
    private Vector3Int GetStepToward(Vector3Int from, Vector3Int to, Status monster)
    {
        int dx = System.Math.Sign(to.x - from.x);
        int dz = System.Math.Sign(to.z - from.z);

        // 優先順: 斜め → X方向 → Z方向
        var candidates = new Vector3Int[]
        {
            new Vector3Int(from.x + dx, 0, from.z + dz),
            new Vector3Int(from.x + dx, 0, from.z),
            new Vector3Int(from.x, 0, from.z + dz),
        };

        foreach (var c in candidates)
        {
            if (c == from) continue;
            if (!IsValidTile(c)) continue;
            Vector3 cellPos = new Vector3(c.x, 0, c.z);
            if (moveGenerator.IsOccupied(cellPos)) continue;
            return c;
        }

        return from; // 移動不可
    }

    /// <summary>ランダムに1歩移動する</summary>
    private void RandomMove(Status monster, Vector3Int myPos)
    {
        // 隣接8方向からランダムに有効な1マスを選ぶ
        var dirs = new Vector3Int[]
        {
            new Vector3Int( 1, 0,  0), new Vector3Int(-1, 0,  0),
            new Vector3Int( 0, 0,  1), new Vector3Int( 0, 0, -1),
            new Vector3Int( 1, 0,  1), new Vector3Int( 1, 0, -1),
            new Vector3Int(-1, 0,  1), new Vector3Int(-1, 0, -1),
        };

        // ボスは縄張り内に留まる
        bool isBoss = monster.IsBoss;

        // シャッフル
        for (int i = dirs.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            var tmp = dirs[i]; dirs[i] = dirs[j]; dirs[j] = tmp;
        }

        foreach (var d in dirs)
        {
            var target = myPos + d;
            if (!IsValidTile(target)) continue;
            Vector3 cellPos = new Vector3(target.x, 0, target.z);
            if (moveGenerator.IsOccupied(cellPos)) continue;

            // ボスは縄張り外に出ない
            if (isBoss && !monsterSystem.IsInBossTerritory(target)) continue;

            MoveUnit(monster, myPos, target);
            return;
        }
    }

    /// <summary>ユニットを移動させる</summary>
    private void MoveUnit(Status monster, Vector3Int from, Vector3Int to)
    {
        // 占有解除 → 移動 → 占有追加
        Vector3 oldCell = new Vector3(from.x, 0, from.z);
        moveGenerator.RemoveOccupied(oldCell);

        // Y座標を取得
        float y = monster.transform.position.y;
        if (mapCreate.TryGetHeight(to.x, to.z, out float height))
            y = height;

        monster.transform.position = new Vector3(to.x, y, to.z);

        Vector3 newCell = new Vector3(to.x, 0, to.z);
        moveGenerator.AddOccupied(newCell);
    }

    /// <summary>攻撃を実行する</summary>
    private void PerformAttack(Status attacker, Status target)
    {
        int damage = DamageCalculator.CalcNormal(attacker, target);
        int actual = target.ApplyDamage(damage);

        Debug.Log($"[MonsterAI] {attacker.name} が {target.name}({target.team}) に {actual}ダメージ (残HP:{target.HP})");

        // 死亡処理
        if (target.HP <= 0)
        {
            target.gameObject.SetActive(false);

            // 占有解除
            Vector3 cellPos = new Vector3(
                Mathf.RoundToInt(target.transform.position.x), 0,
                Mathf.RoundToInt(target.transform.position.z));
            moveGenerator.RemoveOccupied(cellPos);

            if (UnitRegistry.Instance != null)
                UnitRegistry.Instance.Unregister(target);

            Debug.Log($"[MonsterAI] {target.name}({target.team}) を撃破！");
        }
    }

    /// <summary>タイルが有効かチェック</summary>
    private bool IsValidTile(Vector3Int pos)
    {
        return mapCreate.HasTileAt(pos.x, pos.z);
    }
}
