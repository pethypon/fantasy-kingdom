using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// サブクリスタルシステム: サブクリスタルの設置・領地拡張・破壊時の処理を管理する。
/// </summary>
public class SubCrystalSystem : MonoBehaviour
{
    // ---- サブクリスタル破壊報酬（ランダム） ----
    public enum RewardType { Bread, Wood, Stone, Iron }
    public const int RewardAmount = 50;
    // ---- 外部参照（Init で注入） ----
    private TurnGenerator turnGenerator;
    private TerritorySystem territorysystem;
    private FactionState factionState;
    private MapCreate mapcreate;
    private BuildSystem buildsystem;
    private VisionGenerator visionGenerator;
    private MoveGenerator moveGenerator;
    private CrystalSystem crystalsystem;

    // ---- サブクリスタル領地データ ----
    // 各サブクリスタルが所有する領地位置のマッピング
    private Dictionary<GameObject, List<Vector3>> subCrystalTerritories = new Dictionary<GameObject, List<Vector3>>();
    // 各サブクリスタルが生成した領地タイルオブジェクト
    private Dictionary<GameObject, List<GameObject>> subCrystalTerritoryTiles = new Dictionary<GameObject, List<GameObject>>();

    // ---- 定数 ----
    public const int SubCrystalTerritoryRadius = 1;
    public const int SubCrystalCooldownTurns = 5;

    /// <summary>毎ターン開始時に呼ばれ、返却待ちタイマーを進める</summary>
    public void TickPendingReturns(Team team)
    {
        if (factionState == null)
        {
            Debug.LogWarning("[SubCrystalSystem] TickPendingReturns: factionState が null です");
            return;
        }
        var list = team == Team.Player ? factionState.PlayerPendingReturns : factionState.EnemyPendingReturns;
        if (list.Count > 0)
            Debug.Log($"[SubCrystalSystem] TickPendingReturns({team}): 返却待ち{list.Count}件, 値=[{string.Join(",", list)}]");
        factionState.TickPendingReturns(team);
    }

    // ==================================================================
    //  初期化
    // ==================================================================
    public void Init(TurnGenerator turnGenerator, TerritorySystem territorysystem,
                     FactionState factionState, MapCreate mapcreate,
                     BuildSystem buildsystem, VisionGenerator visionGenerator,
                     MoveGenerator moveGenerator, CrystalSystem crystalsystem)
    {
        this.turnGenerator = turnGenerator;
        this.territorysystem = territorysystem;
        this.factionState = factionState;
        this.mapcreate = mapcreate;
        this.buildsystem = buildsystem;
        this.visionGenerator = visionGenerator;
        this.moveGenerator = moveGenerator;
        this.crystalsystem = crystalsystem;
    }

    // ==================================================================
    //  サブクリスタル設置時の領地拡張
    // ==================================================================
    public void ExpandTerritory(GameObject subCrystal, Team team)
    {
        if (subCrystal == null) return;

        Vector3 pos = subCrystal.transform.position;
        var setpos = mapcreate.SetPos;

        // サブクリスタル周辺の半径3マス以内を新領地として追加
        var newTerritory = new List<Vector3>();
        foreach (var p in setpos)
        {
            if (Mathf.Abs(p.x - pos.x) <= SubCrystalTerritoryRadius &&
                Mathf.Abs(p.z - pos.z) <= SubCrystalTerritoryRadius)
                newTerritory.Add(p);
        }

        // 既存領地と重複しないものだけ追加
        List<Vector3> ptSetPos = territorysystem.GetTerritory(team);
        List<Vector3> addedPositions = new List<Vector3>();
        List<GameObject> addedTiles = new List<GameObject>();

        foreach (var tPos in newTerritory)
        {
            bool alreadyExists = GridHelper.ContainsXZ(ptSetPos, GridHelper.ToGrid(tPos));

            if (!alreadyExists)
            {
                ptSetPos.Add(tPos);
                addedPositions.Add(tPos);

                // 領地タイルを生成
                Vector3 tilePos = tPos;
                tilePos.y -= 0.475f;
                Transform parent = team == Team.Player
                    ? territorysystem.Playerterritory
                    : territorysystem.Enemyterritory;

                // TerritorySystem の Inspector で設定されたプレハブを使用
                // フォールバック: 緑の平面を生成
                GameObject tile = CreateTerritoryTile(tilePos, parent);
                addedTiles.Add(tile);
            }
        }

        subCrystalTerritories[subCrystal] = addedPositions;
        subCrystalTerritoryTiles[subCrystal] = addedTiles;

        Debug.Log($"[SubCrystalSystem] 領地拡張: {addedPositions.Count}マス追加 (計{ptSetPos.Count}マス)");
    }

    // ==================================================================
    //  サブクリスタル破壊時のロジック
    // ==================================================================
    public void OnSubCrystalDestroyed(GameObject subCrystal, Team ownerTeam)
    {
        if (subCrystal == null) return;

        // このサブクリスタルが追加した領地を削除
        if (subCrystalTerritories.TryGetValue(subCrystal, out var positions))
        {
            List<Vector3> ptSetPos = territorysystem.GetTerritory(ownerTeam);
            foreach (var pos in positions)
            {
                Vector3Int posGrid = GridHelper.ToGrid(pos);
                ptSetPos.RemoveAll(p => GridHelper.MatchXZ(p, posGrid));
            }
            subCrystalTerritories.Remove(subCrystal);
        }

        // 領地タイルを破棄
        if (subCrystalTerritoryTiles.TryGetValue(subCrystal, out var tiles))
        {
            foreach (var tile in tiles)
            {
                if (tile != null) Destroy(tile);
            }
            subCrystalTerritoryTiles.Remove(subCrystal);
        }

        // 5ターン後にサブクリスタル資源を返却（即時回復ではない）
        if (factionState != null)
        {
            factionState.AddPendingReturn(ownerTeam, SubCrystalCooldownTurns);
            Debug.Log($"[SubCrystalSystem] サブクリスタル破壊: {ownerTeam} 領地縮小、{SubCrystalCooldownTurns}ターン後に返却 (PendingReturns={factionState.GetPendingReturnCount(ownerTeam)}, 現在の副晶={factionState.GetSubCrystals(ownerTeam)})");
        }
        else
        {
            Debug.LogError("[SubCrystalSystem] サブクリスタル破壊: factionState が null のため返却待ちを追加できません");
        }

        // 破壊報酬: 自陣以外の駒が壊した場合、破壊側にランダム報酬
        Team destroyerTeam = ownerTeam == Team.Player ? Team.Enemy : Team.Player;
        GrantDestructionReward(destroyerTeam);
    }

    /// <summary>
    /// サブクリスタルを自陣以外の駒が破壊した時のランダム報酬
    /// パン+50 / 木材+50 / 石+50 / 鉄+50 のいずれか
    /// </summary>
    private void GrantDestructionReward(Team rewardTeam)
    {
        if (factionState == null) return;

        var res = rewardTeam == Team.Player
            ? factionState.PlayerResources
            : factionState.EnemyResources;

        RewardType reward = (RewardType)Random.Range(0, 4);
        switch (reward)
        {
            case RewardType.Bread:
                res.Bread += RewardAmount;
                Debug.Log($"[SubCrystalSystem] {rewardTeam} 破壊報酬: パン +{RewardAmount}");
                break;
            case RewardType.Wood:
                res.Wood += RewardAmount;
                Debug.Log($"[SubCrystalSystem] {rewardTeam} 破壊報酬: 木材 +{RewardAmount}");
                break;
            case RewardType.Stone:
                res.Stone += RewardAmount;
                Debug.Log($"[SubCrystalSystem] {rewardTeam} 破壊報酬: 石 +{RewardAmount}");
                break;
            case RewardType.Iron:
                res.Iron += RewardAmount;
                Debug.Log($"[SubCrystalSystem] {rewardTeam} 破壊報酬: 鉄 +{RewardAmount}");
                break;
        }
    }

    // ==================================================================
    //  建築物破壊（汎用）— サブクリスタル・通常建築物共用
    // ==================================================================
    public void DestroyBuilding(Status target)
    {
        if (target == null) return;

        Vector3Int posInt = GridHelper.ToGrid(target.transform.position);

        // サブクリスタルの場合は領地も削除
        if (target.facilityKind == FacilityKind.SubCrystal)
        {
            OnSubCrystalDestroyed(target.gameObject, target.team);
        }

        // 壁の場合は UnitPointData から除去
        if (FacilityData.IsWall(target.facilityKind))
        {
            moveGenerator.RemoveOccupiedWhere(p => GridHelper.ToGrid(p) == posInt);
        }

        // BuildSystem の設置済み位置から除去
        buildsystem.RemoveBuildingPosition(posInt);

        // GameObjectを非表示にして遠くに移動（再利用のため完全破壊はしない）
        target.gameObject.SetActive(false);
        target.transform.position = new Vector3(-1000, -1000, -1000);

        // SubCrystal は視界を提供するため、破壊後は確実に視界を再計算する。
        // BuildingAttackSystem からの呼び出しは後段で再計算するが、
        // UnitPanelUI の破壊ボタンなど他経路からの呼び出しでも漏れないよう
        // ここで一括して dirty 化＋再計算する。
        if (visionGenerator != null && mapcreate != null)
        {
            visionGenerator.MarkVisionDirty();
            visionGenerator.VisionPoint(mapcreate, moveGenerator, crystalsystem);
        }

        Debug.Log($"[SubCrystalSystem] 建築物破壊: {target.facilityKind} at ({posInt.x}, {posInt.y}, {posInt.z})");
    }

    // ==================================================================
    //  サブクリスタル設置可否チェック
    // ==================================================================
    public bool CanPlaceSubCrystal(Vector3Int pos, Team team)
    {
        if (factionState == null || factionState.GetSubCrystals(team) <= 0) return false;
        if (territorysystem == null || territorysystem.IsInAnyTerritory(pos.x, pos.z)) return false;
        if (HasTerritoryInRadius1(pos)) return false;
        if (!IsInTeamVision(pos, team)) return false;
        if (buildsystem.HasBuildingAt(pos)) return false;
        if (IsCrystalPosition(pos)) return false;

        return true;
    }

    // ==================================================================
    //  半径1マス以内に領地があるかチェック
    // ==================================================================
    private bool HasTerritoryInRadius1(Vector3Int pos)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                if (territorysystem.IsInAnyTerritory(pos.x + dx, pos.z + dz))
                    return true;
            }
        }
        return false;
    }

    /// <summary>指定座標がチームの視界内にあるかを判定する</summary>
    private bool IsInTeamVision(Vector3Int pos, Team team)
    {
        if (visionGenerator == null) return false;
        return visionGenerator.IsInVision(team, pos);
    }

    /// <summary>指定座標がクリスタル位置かどうかを判定する</summary>
    private bool IsCrystalPosition(Vector3Int pos)
    {
        return GridHelper.ToGrid(crystalsystem.PCP) == pos
            || GridHelper.ToGrid(crystalsystem.ECP) == pos;
    }

    // ==================================================================
    //  領地タイル生成ヘルパー
    // ==================================================================
    private GameObject CreateTerritoryTile(Vector3 pos, Transform parent)
    {
        // TerritorySystem の Inspector 設定プレハブと同じ見た目のタイルを生成
        var tile = GameObject.CreatePrimitive(PrimitiveType.Quad);
        tile.name = "SubCrystalTerritory";
        tile.transform.position = pos;
        tile.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        tile.transform.localScale = new Vector3(1f, 1f, 1f);
        tile.transform.SetParent(parent);

        // コライダーを無効化
        var col = tile.GetComponent<Collider>();
        if (col != null) col.enabled = false;

        // 半透明の色を設定
        var renderer = tile.GetComponent<Renderer>();
        if (renderer != null)
        {
            var mat = new Material(Shader.Find("Standard"));
            mat.SetFloat("_Mode", 3);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
            mat.color = new Color(0.2f, 0.6f, 1f, 0.3f);
            renderer.material = mat;
        }

        return tile;
    }
}
