using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// サブクリスタルシステム: サブクリスタルの設置・領地拡張・破壊時の処理を管理する。
/// </summary>
public class SubCrystalSystem : MonoBehaviour
{
    // ---- 外部参照（Init で注入） ----
    private TurnGenerater turngenerater;
    private TerritorySystem territorysystem;
    private FactionState factionState;
    private MapCreate mapcreate;
    private BuildSystem buildsystem;
    private VisionGenerater visiongenerater;
    private MoveGererater movegenerater;
    private CrystalSystem crystalsystem;

    // ---- サブクリスタル領地データ ----
    // 各サブクリスタルが所有する領地位置のマッピング
    private Dictionary<GameObject, List<Vector3>> subCrystalTerritories = new Dictionary<GameObject, List<Vector3>>();
    // 各サブクリスタルが生成した領地タイルオブジェクト
    private Dictionary<GameObject, List<GameObject>> subCrystalTerritoryTiles = new Dictionary<GameObject, List<GameObject>>();

    // ---- 定数 ----
    public const int SubCrystalTerritoryRadius = 3;
    public const int SubCrystalCooldownTurns = 5;

    // ==================================================================
    //  初期化
    // ==================================================================
    public void Init(TurnGenerater turngenerater, TerritorySystem territorysystem,
                     FactionState factionState, MapCreate mapcreate,
                     BuildSystem buildsystem, VisionGenerater visiongenerater,
                     MoveGererater movegenerater, CrystalSystem crystalsystem)
    {
        this.turngenerater = turngenerater;
        this.territorysystem = territorysystem;
        this.factionState = factionState;
        this.mapcreate = mapcreate;
        this.buildsystem = buildsystem;
        this.visiongenerater = visiongenerater;
        this.movegenerater = movegenerater;
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
        List<Vector3> newTerritory = setpos.Where(p =>
        {
            float px = Mathf.Abs(p.x - pos.x);
            float pz = Mathf.Abs(p.z - pos.z);
            return px <= SubCrystalTerritoryRadius && pz <= SubCrystalTerritoryRadius;
        }).ToList();

        // 既存領地と重複しないものだけ追加
        List<Vector3> ptSetPos = team == Team.Player ? territorysystem.PTSetPos : territorysystem.ETSetPos;
        List<Vector3> addedPositions = new List<Vector3>();
        List<GameObject> addedTiles = new List<GameObject>();

        foreach (var tPos in newTerritory)
        {
            bool alreadyExists = ptSetPos.Any(p =>
                Mathf.RoundToInt(p.x) == Mathf.RoundToInt(tPos.x) &&
                Mathf.RoundToInt(p.z) == Mathf.RoundToInt(tPos.z));

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
    public void OnSubCrystalDestroyed(GameObject subCrystal, Team team)
    {
        if (subCrystal == null) return;

        // このサブクリスタルが追加した領地を削除
        if (subCrystalTerritories.TryGetValue(subCrystal, out var positions))
        {
            List<Vector3> ptSetPos = team == Team.Player ? territorysystem.PTSetPos : territorysystem.ETSetPos;
            foreach (var pos in positions)
            {
                ptSetPos.RemoveAll(p =>
                    Mathf.RoundToInt(p.x) == Mathf.RoundToInt(pos.x) &&
                    Mathf.RoundToInt(p.z) == Mathf.RoundToInt(pos.z));
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
        factionState.AddPendingReturn(team, SubCrystalCooldownTurns);

        Debug.Log($"[SubCrystalSystem] サブクリスタル破壊: 領地縮小、{SubCrystalCooldownTurns}ターン後に返却");
    }

    // ==================================================================
    //  建築物破壊（汎用）— サブクリスタル・通常建築物共用
    // ==================================================================
    public void DestroyBuilding(Status target)
    {
        if (target == null) return;

        Vector3 pos = target.transform.position;
        Vector3Int posInt = new Vector3Int(
            Mathf.RoundToInt(pos.x),
            Mathf.RoundToInt(pos.y),
            Mathf.RoundToInt(pos.z));

        // サブクリスタルの場合は領地も削除
        if (target.facilityKind == FacilityKind.SubCrystal)
        {
            OnSubCrystalDestroyed(target.gameObject, target.team);
        }

        // 壁の場合は UnitPointData から除去
        if (FacilityData.IsWall(target.facilityKind))
        {
            movegenerater.UnitPointData.RemoveAll(p =>
                Mathf.RoundToInt(p.x) == posInt.x &&
                Mathf.RoundToInt(p.y) == posInt.y &&
                Mathf.RoundToInt(p.z) == posInt.z);
        }

        // BuildSystem の設置済み位置から除去
        buildsystem.RemoveBuildingPosition(posInt);

        // GameObjectを非表示にして遠くに移動（再利用のため完全破壊はしない）
        target.gameObject.SetActive(false);
        target.transform.position = new Vector3(-1000, -1000, -1000);

        Debug.Log($"[SubCrystalSystem] 建築物破壊: {target.facilityKind} at ({posInt.x}, {posInt.y}, {posInt.z})");
    }

    // ==================================================================
    //  サブクリスタル設置可否チェック
    // ==================================================================
    public bool CanPlaceSubCrystal(Vector3Int pos, Team team)
    {
        // サブクリスタル資源チェック
        if (factionState.GetSubCrystals(team) <= 0) return false;

        // Player領地内は設置不可
        if (IsInAnyTerritory(pos)) return false;

        // サブクリスタルを中心とした半径1マスに領地がある場合も設置不可
        if (HasTerritoryInRadius1(pos)) return false;

        // 駒の視界内チェック
        if (!IsInPlayerVision(pos, team)) return false;

        // 既存建築物チェック
        if (buildsystem.HasBuildingAt(pos)) return false;

        // クリスタル位置チェック
        Vector3 pcpVec = crystalsystem.PCP;
        Vector3Int pcp = new Vector3Int(
            Mathf.RoundToInt(pcpVec.x),
            Mathf.RoundToInt(pcpVec.y),
            Mathf.RoundToInt(pcpVec.z));
        if (pos == pcp) return false;

        Vector3 ecpVec = crystalsystem.ECP;
        Vector3Int ecp = new Vector3Int(
            Mathf.RoundToInt(ecpVec.x),
            Mathf.RoundToInt(ecpVec.y),
            Mathf.RoundToInt(ecpVec.z));
        if (pos == ecp) return false;

        return true;
    }

    // ==================================================================
    //  領地チェック: Player領地とEnemy領地の両方をチェック
    // ==================================================================
    private bool IsInAnyTerritory(Vector3Int pos)
    {
        if (territorysystem.PTSetPos != null)
        {
            if (territorysystem.PTSetPos.Any(p =>
                Mathf.RoundToInt(p.x) == pos.x && Mathf.RoundToInt(p.z) == pos.z))
                return true;
        }
        if (territorysystem.ETSetPos != null)
        {
            if (territorysystem.ETSetPos.Any(p =>
                Mathf.RoundToInt(p.x) == pos.x && Mathf.RoundToInt(p.z) == pos.z))
                return true;
        }
        return false;
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
                Vector3Int checkPos = new Vector3Int(pos.x + dx, pos.y, pos.z + dz);
                if (IsInAnyTerritory(checkPos)) return true;
            }
        }
        return false;
    }

    // ==================================================================
    //  駒の視界内かチェック
    // ==================================================================
    private bool IsInPlayerVision(Vector3Int pos, Team team)
    {
        var visionXZ = new Vector3Int(pos.x, 0, pos.z);
        if (team == Team.Player)
            return visiongenerater.PlayerVisionBox != null &&
                   visiongenerater.PlayerVisionBox.Any(v => v.x == pos.x && v.z == pos.z);
        else
            return visiongenerater.EnemyVisionBox != null &&
                   visiongenerater.EnemyVisionBox.Any(v => v.x == pos.x && v.z == pos.z);
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
