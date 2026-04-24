using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 強敵（縄張り付き中立ボス）システム。
/// ゲーム開始時にマップ上のランダム位置へ1体配置する。
/// - 両陣営の領土および自身の縄張りが他システム（ダンジョン含む）と被らないように選ぶ
/// - 縄張り内に駒がいない間は被弾無効（Status.ApplyDamage で判定）
/// - 縄張り内に侵入した駒が離脱すると再度無敵になる
/// </summary>
public class WildBossSystem : MonoBehaviour
{
    public const int TerritoryRadius = 3;
    public const int WildBossLevel = 15; // 高Lvでレベル成長による強化を享受

    [Header("強敵プレハブ（StrangeKingを流用）")]
    [SerializeField] GameObject wildBossPrefab;

    [Header("配置親")]
    [SerializeField] Transform parent;

    private MapCreate mapcreate;
    private CrystalSystem crystalsystem;
    private TerritorySystem territorysystem;
    private DungeonSystem dungeonSystem;
    private UnitSetting unitSetting;

    public Status SpawnedBoss { get; private set; }

    public void Init(MapCreate mapcreate, CrystalSystem crystalsystem,
                     TerritorySystem territorysystem, DungeonSystem dungeonSystem,
                     UnitSetting unitSetting)
    {
        this.mapcreate = mapcreate;
        this.crystalsystem = crystalsystem;
        this.territorysystem = territorysystem;
        this.dungeonSystem = dungeonSystem;
        this.unitSetting = unitSetting;
    }

    /// <summary>
    /// ゲーム開始時に1体の強敵をマップに配置する。
    /// 両陣営の領土と自縄張りが被らない位置を優先し、候補が不足する場合は段階的に緩める。
    /// </summary>
    public void GenerateWildBoss()
    {
        if (mapcreate == null || crystalsystem == null) return;
        if (wildBossPrefab == null)
        {
            Debug.LogWarning("[WildBoss] プレハブ未割当のためスポーンをスキップ");
            return;
        }

        Vector3Int pcp = GridHelper.ToGrid(crystalsystem.PCP);
        Vector3Int ecp = GridHelper.ToGrid(crystalsystem.ECP);
        var setpos = mapcreate.SetPos;

        // 候補: 縄張り(3)+陣営領土(5)+α=8マス以上離す → 緩めて4まで
        int[] minDistances = { 10, 8, 6, 4 };
        List<Vector3> candidates = null;
        foreach (int minDist in minDistances)
        {
            candidates = setpos.Where(p =>
            {
                Vector3Int g = GridHelper.ToGrid(p);
                if (GridHelper.ChebyshevDistance(g, pcp) < minDist) return false;
                if (GridHelper.ChebyshevDistance(g, ecp) < minDist) return false;
                if (territorysystem != null && territorysystem.IsInAnyTerritory(g.x, g.z)) return false;
                if (IsNearDungeon(g, TerritoryRadius)) return false;
                return true;
            }).ToList();
            if (candidates.Count > 0) break;
        }

        if (candidates == null || candidates.Count == 0)
        {
            Debug.LogWarning("[WildBoss] 配置候補が見つからずスポーン中止");
            return;
        }

        Vector3 picked = candidates[Random.Range(0, candidates.Count)];
        SpawnAt(picked);
    }

    private bool IsNearDungeon(Vector3Int pos, int range)
    {
        if (dungeonSystem == null) return false;
        foreach (var d in dungeonSystem.Dungeons)
            if (GridHelper.ChebyshevDistance(d.Position, pos) <= range) return true;
        return false;
    }

    private void SpawnAt(Vector3 pos)
    {
        Transform spawnParent = parent != null ? parent : transform;
        GameObject obj = unitSetting != null
            ? unitSetting.SpawnUnit(wildBossPrefab, pos, spawnParent, WildBossLevel)
            : Instantiate(wildBossPrefab, pos, Quaternion.identity, spawnParent);

        var status = obj.GetComponentInChildren<Status>();
        if (status == null)
        {
            Debug.LogWarning("[WildBoss] Status未検出");
            return;
        }

        // 縄張りフラグを有効化
        status.isWildBoss = true;
        status.wildBossTerritoryCenter = new Vector3Int(Mathf.RoundToInt(pos.x), 0, Mathf.RoundToInt(pos.z));
        status.wildBossTerritoryRadius = TerritoryRadius;
        // 中立扱い（プレイヤー/敵どちらとも敵対）
        status.team = Team.Obstacle;
        status.passiveskill = PassiveSkill.StrangeKingAura;

        SpawnedBoss = status;
        Debug.Log($"[WildBoss] 強敵配置: {pos} 縄張り半径={TerritoryRadius} Lv{WildBossLevel}");
    }
}
