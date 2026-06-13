using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 攻撃型建築物のターン終了時自動攻撃を処理する。
/// 迫撃砲: x,z オフセット (0,-2),(0,-1),(0,1),(0,2),(-1,0),(-2,0),(1,0),(2,0) から
///         敵がいるマスを1つ選び、選んだマスの左右隣接2マスにも範囲攻撃。
/// 大砲:   自身を基準に半径1マス（8近傍）で敵がいるマスを1つ選んで攻撃。
/// </summary>
public class BuildingAttackSystem : MonoBehaviour
{
    private BuildSystem buildSystem;
    private MoveGenerator moveGererater;
    private UnitSetting unitSetting;
    private VisionGenerator visionGenerater;
    private MapCreate mapCreate;
    private CrystalSystem crystalSystem;
    private SubCrystalSystem subCrystalSystem;

    // 現在の攻撃チーム（ProcessAttacks で設定）
    private Team _currentAttackTeam;
    private Team _currentEnemyTeam;

    public void Init(BuildSystem buildSystem, MoveGenerator moveGererater,
                     UnitSetting unitSetting, VisionGenerator visionGenerater,
                     MapCreate mapCreate, CrystalSystem crystalSystem)
    {
        this.buildSystem = buildSystem;
        this.moveGererater = moveGererater;
        this.unitSetting = unitSetting;
        this.visionGenerater = visionGenerater;
        this.mapCreate = mapCreate;
        this.crystalSystem = crystalSystem;
    }

    public void SetSubCrystalSystem(SubCrystalSystem scs)
    {
        this.subCrystalSystem = scs;
    }

    // 迫撃砲の攻撃範囲オフセット（x,z）
    private static readonly Vector2Int[] MortarOffsets = new Vector2Int[]
    {
        new Vector2Int(0, -2), new Vector2Int(0, -1),
        new Vector2Int(0, 1),  new Vector2Int(0, 2),
        new Vector2Int(-1, 0), new Vector2Int(-2, 0),
        new Vector2Int(1, 0),  new Vector2Int(2, 0),
    };

    // 迫撃砲の爆発範囲オフセット（メインターゲットの左右2マス）
    private static readonly Vector2Int[] MortarSplashOffsets = new Vector2Int[]
    {
        new Vector2Int(-1, 0), new Vector2Int(1, 0),
        new Vector2Int(-2, 0), new Vector2Int(2, 0),
    };

    // 大砲の攻撃範囲オフセット（半径1 = 8近傍）
    private static readonly Vector2Int[] CannonOffsets = new Vector2Int[]
    {
        new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1),
        new Vector2Int(-1,  0),                        new Vector2Int(1,  0),
        new Vector2Int(-1,  1), new Vector2Int(0,  1), new Vector2Int(1,  1),
    };

    // ターゲット探索用の再利用バッファ
    private readonly List<Status> _targetsBuffer = new List<Status>(16);

    /// <summary>
    /// 指定チームの攻撃型建築物による自動攻撃を実行する。
    /// ターン終了時、EconomySystem.ProcessTurn() の後に呼ぶ。
    /// </summary>
    public void ProcessAttacks(Team team)
    {
        if (buildSystem == null) return;
        Transform buildingParent = buildSystem.GetBuildingParent(team);
        if (buildingParent == null) return;

        // 攻撃チーム記録（自チームの視界内のみ攻撃可能）
        _currentAttackTeam = team;

        // 敵チーム判定
        _currentEnemyTeam = (team == Team.Player) ? Team.Enemy : Team.Player;

        foreach (Transform child in buildingParent)
        {
            var status = child.GetComponent<Status>();
            if (status == null) continue;
            if (status.HP <= 0) continue;

            if (status.facilityKind == FacilityKind.Mortar)
            {
                ProcessMortarAttack(status, _currentEnemyTeam);
            }
            else if (status.facilityKind == FacilityKind.Cannon)
            {
                ProcessCannonAttack(status, _currentEnemyTeam);
            }
        }
    }

    /// <summary>
    /// 迫撃砲の攻撃処理。
    /// 範囲内の敵がいるマスを1つ選び、そのマスと左右隣接2マスに範囲攻撃。
    /// </summary>
    private void ProcessMortarAttack(Status mortar, Team enemyTeam)
    {
        var baseGrid = GridHelper.ToGridXZ(moveGererater.Cell(mortar.transform.position));
        int bx = baseGrid.x;
        int bz = baseGrid.z;

        // 範囲内の敵ユニットを探す
        _targetsBuffer.Clear();
        foreach (var offset in MortarOffsets)
        {
            Vector3 checkPos = new Vector3(bx + offset.x, 0f, bz + offset.y);
            Status enemy = FindEnemyAtCell(checkPos, enemyTeam);
            if (enemy != null)
                _targetsBuffer.Add(enemy);
        }

        if (_targetsBuffer.Count == 0) return;

        // ランダムに1体選ぶ
        Status primaryTarget = _targetsBuffer[Random.Range(0, _targetsBuffer.Count)];
        var targetGrid = GridHelper.ToGridXZ(moveGererater.Cell(primaryTarget.transform.position));
        int tx = targetGrid.x;
        int tz = targetGrid.z;

        // メイン攻撃
        ApplyBuildingDamage(mortar, primaryTarget);

        // 左右隣接2マスにも攻撃（x±1, x±2）
        foreach (var splash in MortarSplashOffsets)
        {
            Vector3 splashPos = new Vector3(tx + splash.x, 0f, tz + splash.y);
            Status splashTarget = FindEnemyAtCell(splashPos, enemyTeam);
            if (splashTarget != null)
                ApplyBuildingDamage(mortar, splashTarget);
        }
    }

    /// <summary>
    /// 大砲の攻撃処理。
    /// 半径1マス（8近傍）で敵がいるマスを1つ選んで攻撃。
    /// </summary>
    private void ProcessCannonAttack(Status cannon, Team enemyTeam)
    {
        var baseGrid = GridHelper.ToGridXZ(moveGererater.Cell(cannon.transform.position));
        int bx = baseGrid.x;
        int bz = baseGrid.z;

        _targetsBuffer.Clear();
        foreach (var offset in CannonOffsets)
        {
            Vector3 checkPos = new Vector3(bx + offset.x, 0f, bz + offset.y);
            Status enemy = FindEnemyAtCell(checkPos, enemyTeam);
            if (enemy != null)
                _targetsBuffer.Add(enemy);
        }

        if (_targetsBuffer.Count == 0) return;

        Status target = _targetsBuffer[Random.Range(0, _targetsBuffer.Count)];
        ApplyBuildingDamage(cannon, target);
    }

    /// <summary>
    /// 指定セルにいる敵ユニット/建築物を探す。
    /// </summary>
    private Status FindEnemyAtCell(Vector3 cellPos, Team enemyTeam)
    {
        // 視界外のセルは攻撃対象にしない
        if (visionGenerater != null)
        {
            var cellKey = GridHelper.ToGridXZ(cellPos);
            if (!visionGenerater.IsInVision(_currentAttackTeam, cellKey)) return null;
        }

        var cellGrid = GridHelper.ToGridXZ(cellPos);
        int cx = cellGrid.x;
        int cz = cellGrid.z;

        // 敵ユニットを検索
        Transform enemyParent = (enemyTeam == Team.Enemy)
            ? unitSetting.EnemyUnit
            : unitSetting.PlayerUnit;

        Status found = FindStatusAtCell(enemyParent, cx, cz, enemyTeam);
        if (found != null) return found;

        // 敵の建築物も検索
        found = FindStatusAtCell(buildSystem.GetBuildingParent(enemyTeam), cx, cz);
        if (found != null) return found;

        // クリスタルも検索
        Vector3 targetCrystalPos = (enemyTeam == Team.Enemy)
            ? crystalSystem.ECP : crystalSystem.PCP;
        if (GridHelper.MatchXZ(targetCrystalPos, cx, cz))
        {
            Transform crystalParent = (enemyTeam == Team.Enemy)
                ? crystalSystem.Enemycrystal
                : crystalSystem.Playercrystal;
            if (crystalParent != null && crystalParent.childCount > 0)
            {
                var cst = crystalParent.GetChild(0).GetComponent<Status>();
                if (cst != null && cst.HP > 0)
                    return cst;
            }
        }

        return null;
    }

    /// <summary>指定セルの Transform 子から Status を検索する</summary>
    private Status FindStatusAtCell(Transform parent, int x, int z, Team? teamFilter = null)
    {
        if (parent == null) return null;
        foreach (Transform child in parent)
        {
            if (!child.gameObject.activeSelf) continue;
            var st = child.GetComponent<Status>();
            if (st == null || st.HP <= 0) continue;
            if (teamFilter.HasValue && st.team != teamFilter.Value) continue;
            Vector3 cell = moveGererater.Cell(child.position);
            if (GridHelper.MatchXZ(cell, x, z))
                return st;
        }
        return null;
    }

    /// <summary>
    /// 建築物によるダメージ適用。ATK - DEF（最低0）。HP0で撃破処理。
    /// </summary>
    private void ApplyBuildingDamage(Status attacker, Status target)
    {
        // シールド中はダメージ無効
        if (target.ShieldTurns > 0)
        {
            Debug.Log($"[BuildingAttack] {target.kind} はシールド中！ ダメージ無効（残り{target.ShieldTurns}ターン）");
            return;
        }

        int raw = Mathf.Max(0, attacker.ATK - target.DEF);

        // Special Ability: 致死ダメージ耐え（生還本能）— 他の攻撃経路と同等に判定する
        if (SpecialAbilitySystem.TrySurviveLethal(target, raw))
            return;

        int damage = target.ApplyDamage(raw);

        string attackerName = FacilityData.Table.TryGetValue(attacker.facilityKind, out var info)
            ? info.DisplayName : attacker.facilityKind.ToString();
        Debug.Log($"[BuildingAttack] {attackerName} → {target.kind} ({target.team})  DMG:{damage}  残HP:{target.HP}");

        if (!target.IsAlive)
            HandleTargetDeath(target);
    }

    /// <summary>
    /// 攻撃によるHP0処理。
    /// </summary>
    private void HandleTargetDeath(Status target)
    {
        Debug.Log($"[BuildingAttack] {target.team} の {target.kind} が建築物攻撃により撃破");

        // King / Crystal の撃破は勝敗確定 → GameEndState へ
        // （BattleSystem.CheckDeath 相当の判定。砲撃でも勝敗がつくようにする）
        if (target.TryTriggerGameEndIfDecisive()) return;

        // サブクリスタルの場合は SubCrystalSystem 経由で処理（報酬・領地削除含む）
        if (target.facilityKind == FacilityKind.SubCrystal && subCrystalSystem != null)
        {
            subCrystalSystem.DestroyBuilding(target);
        }
        else
        {
            Vector3 cellPos = moveGererater.Cell(target.transform.position);
            moveGererater.RemoveOccupied(cellPos);
            // ユニットの場合は Lv1 ステータスにリセットしてから非表示化
            if (target.type == Type.Unit) target.ResetToLv1();
            target.gameObject.SetActive(false);
        }

        if (visionGenerater != null)
            visionGenerater.VisionPoint(mapCreate, moveGererater, crystalSystem);
    }
}
