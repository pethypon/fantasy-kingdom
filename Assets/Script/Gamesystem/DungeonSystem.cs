using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// ダンジョンシステム: マップ上にランダムで2箇所のダンジョンを配置する。
/// サブクリスタルで起動し、共有10ターンタイマー中に占有し続けるとアーティファクトを獲得。
/// 敵チームの駒が同じダンジョンに侵入するとタイマーはリセットされ競合状態となる。
/// </summary>
public class DungeonSystem : MonoBehaviour
{
    public enum Artifact
    {
        None,
        CrystalShard,  // クリスタル最大HP+1000
        WarBanner,     // 全ユニット ATK+2
        IronAegis,     // 全ユニット DEF+2
        ManaFocus,     // 毎ターンMagicOre+3
        PhoenixFeather // クリスタルシールド再発動可
    }

    [System.Serializable]
    public class DungeonInfo
    {
        public Vector3Int Position;
        public Team ClaimingTeam = Team.None;
        public int ClaimProgress;    // 0〜ClaimTurns
        public bool Contested;       // 双方が同一ダンジョンに存在
        public bool Cleared;         // アーティファクト獲得済み
        public Artifact Reward = Artifact.None;
        public GameObject Marker;    // 視覚表示用
    }

    [Header("定数")]
    public const int DungeonCount = 2;
    public const int ClaimTurns = 10;

    public IReadOnlyList<DungeonInfo> Dungeons => _dungeons;
    private readonly List<DungeonInfo> _dungeons = new List<DungeonInfo>();

    private MapCreate mapcreate;
    private CrystalSystem crystalsystem;
    private TerritorySystem territorysystem;
    private FactionState factionState;
    private UnitSetting unitSetting;
    private BuildSystem buildsystem;

    public void Init(MapCreate mapcreate, CrystalSystem crystalsystem,
                     TerritorySystem territorysystem, FactionState factionState,
                     UnitSetting unitSetting, BuildSystem buildsystem)
    {
        this.mapcreate = mapcreate;
        this.crystalsystem = crystalsystem;
        this.territorysystem = territorysystem;
        this.factionState = factionState;
        this.unitSetting = unitSetting;
        this.buildsystem = buildsystem;
    }

    // ==================================================================
    //  マップ配置
    // ==================================================================
    public void GenerateDungeons()
    {
        _dungeons.Clear();
        if (mapcreate == null || crystalsystem == null) return;

        var setpos = mapcreate.SetPos;
        Vector3Int pcp = GridHelper.ToGrid(crystalsystem.PCP);
        Vector3Int ecp = GridHelper.ToGrid(crystalsystem.ECP);

        // 両クリスタルから十分離れた候補を抽出（最小距離6）
        var candidates = setpos
            .Select(v => GridHelper.ToGrid(v))
            .Where(p => GridHelper.ChebyshevDistance(p, pcp) >= 6
                     && GridHelper.ChebyshevDistance(p, ecp) >= 6)
            .Where(p => !territorysystem.IsInAnyTerritory(p.x, p.z))
            .ToList();

        if (candidates.Count < DungeonCount)
        {
            Debug.LogWarning("[DungeonSystem] ダンジョン候補が不足しています");
            candidates = setpos.Select(v => GridHelper.ToGrid(v)).ToList();
        }

        for (int i = 0; i < DungeonCount && candidates.Count > 0; i++)
        {
            int idx = Random.Range(0, candidates.Count);
            Vector3Int picked = candidates[idx];
            candidates.RemoveAt(idx);

            // 既存ダンジョンから最低距離4
            if (_dungeons.Any(d => GridHelper.ChebyshevDistance(d.Position, picked) < 4))
            {
                i--;
                continue;
            }

            var info = new DungeonInfo
            {
                Position = picked,
                Reward = RollArtifact(),
                Marker = CreateMarker(picked)
            };
            _dungeons.Add(info);
            Debug.Log($"[DungeonSystem] ダンジョン配置: {picked} 報酬={info.Reward}");
        }
    }

    private Artifact RollArtifact()
    {
        var values = (Artifact[])System.Enum.GetValues(typeof(Artifact));
        // None を除外
        var pool = values.Where(a => a != Artifact.None).ToArray();
        return pool[Random.Range(0, pool.Length)];
    }

    private GameObject CreateMarker(Vector3Int pos)
    {
        var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marker.name = $"Dungeon_{pos.x}_{pos.z}";
        marker.transform.position = new Vector3(pos.x, pos.y + 0.2f, pos.z);
        marker.transform.localScale = new Vector3(0.6f, 0.4f, 0.6f);
        var col = marker.GetComponent<Collider>();
        if (col != null) col.enabled = false;
        var renderer = marker.GetComponent<Renderer>();
        if (renderer != null)
        {
            var mat = new Material(Shader.Find("Standard"));
            mat.color = new Color(0.6f, 0.2f, 0.8f, 1f);
            renderer.material = mat;
        }
        return marker;
    }

    // ==================================================================
    //  毎ターン処理（占有判定・タイマー進行・アーティファクト獲得）
    // ==================================================================
    public void ProcessTurn(Team team)
    {
        if (_dungeons.Count == 0) return;
        if (unitSetting == null) return;

        foreach (var d in _dungeons)
        {
            if (d.Cleared) continue;

            bool playerPresent = HasTeamAt(Team.Player, d.Position);
            bool enemyPresent = HasTeamAt(Team.Enemy, d.Position);

            if (playerPresent && enemyPresent)
            {
                d.Contested = true;
                d.ClaimProgress = 0;
                d.ClaimingTeam = Team.None;
                Debug.Log($"[DungeonSystem] ダンジョン{d.Position} 競合！ タイマー停止");
                continue;
            }

            d.Contested = false;

            if (playerPresent)
            {
                if (d.ClaimingTeam != Team.Player)
                {
                    d.ClaimingTeam = Team.Player;
                    d.ClaimProgress = 0;
                }
                d.ClaimProgress++;
            }
            else if (enemyPresent)
            {
                if (d.ClaimingTeam != Team.Enemy)
                {
                    d.ClaimingTeam = Team.Enemy;
                    d.ClaimProgress = 0;
                }
                d.ClaimProgress++;
            }
            else
            {
                // 誰もいない → 進行は維持（減衰はさせない）
            }

            if (d.ClaimProgress >= ClaimTurns && d.ClaimingTeam != Team.None)
            {
                GrantArtifact(d.ClaimingTeam, d.Reward);
                d.Cleared = true;
                if (d.Marker != null) Destroy(d.Marker);
                Debug.Log($"[DungeonSystem] {d.ClaimingTeam} がダンジョン{d.Position}を制圧、{d.Reward}を獲得");
            }
        }
    }

    private bool HasTeamAt(Team team, Vector3Int pos)
    {
        if (unitSetting == null) return false;
        Transform parent = team == Team.Player ? unitSetting.PlayerUnit : unitSetting.EnemyUnit;
        if (parent == null) return false;
        foreach (Transform child in parent)
        {
            if (child == null || !child.gameObject.activeInHierarchy) continue;
            var s = child.GetComponent<Status>();
            if (s == null || !s.IsAlive) continue;
            var g = s.GridPosition;
            if (g.x == pos.x && g.z == pos.z) return true;
        }
        return false;
    }

    /// <summary>サブクリスタル配置時、近傍ダンジョンを起動（ClaimProgressを+2加速）する</summary>
    public void ActivateFromSubCrystal(Vector3Int subCrystalPos, Team team)
    {
        foreach (var d in _dungeons)
        {
            if (d.Cleared) continue;
            if (GridHelper.ChebyshevDistance(subCrystalPos, d.Position) <= 5)
            {
                if (d.ClaimingTeam == Team.None || d.ClaimingTeam == team)
                {
                    d.ClaimingTeam = team;
                    d.ClaimProgress = Mathf.Min(ClaimTurns - 1, d.ClaimProgress + 2);
                    Debug.Log($"[DungeonSystem] {team} のサブクリスタルがダンジョン{d.Position}を起動 (+2T)");
                }
            }
        }
    }

    // ==================================================================
    //  アーティファクト効果
    // ==================================================================
    private void GrantArtifact(Team team, Artifact art)
    {
        if (factionState == null) return;
        var res = factionState.GetResources(team);

        switch (art)
        {
            case Artifact.CrystalShard:
                BuffCrystal(team, 1000);
                break;
            case Artifact.WarBanner:
                BuffAllUnits(team, atkDelta: 2);
                break;
            case Artifact.IronAegis:
                BuffAllUnits(team, defDelta: 2);
                break;
            case Artifact.ManaFocus:
                res.MagicOre += 30;
                break;
            case Artifact.PhoenixFeather:
                ResetCrystalShield(team);
                break;
        }
    }

    private void BuffCrystal(Team team, int hpBonus)
    {
        if (crystalsystem == null) return;
        Transform parent = team == Team.Player ? crystalsystem.Playercrystal : crystalsystem.Enemycrystal;
        if (parent == null || parent.childCount == 0) return;
        var s = parent.GetChild(0).GetComponent<Status>();
        if (s == null) return;
        s.MaxHP += hpBonus;
        s.HP = Mathf.Min(s.MaxHP, s.HP + hpBonus);
    }

    private void BuffAllUnits(Team team, int atkDelta = 0, int defDelta = 0)
    {
        if (unitSetting == null) return;
        Transform parent = team == Team.Player ? unitSetting.PlayerUnit : unitSetting.EnemyUnit;
        if (parent == null) return;
        foreach (Transform child in parent)
        {
            var s = child.GetComponent<Status>();
            if (s == null || !s.IsAlive) continue;
            s.ATK += atkDelta;
            s.DEF += defDelta;
        }
    }

    private void ResetCrystalShield(Team team)
    {
        if (crystalsystem == null) return;
        Transform parent = team == Team.Player ? crystalsystem.Playercrystal : crystalsystem.Enemycrystal;
        if (parent == null || parent.childCount == 0) return;
        var s = parent.GetChild(0).GetComponent<Status>();
        if (s == null) return;
        s.ShieldActivated = false;
        s.ShieldTurns = 0;
    }

    /// <summary>指定座標がダンジョンかどうか（AI参照用）</summary>
    public bool IsDungeonAt(Vector3Int pos)
    {
        foreach (var d in _dungeons)
            if (!d.Cleared && d.Position == pos) return true;
        return false;
    }

    /// <summary>未制圧ダンジョン一覧（AI参照用）</summary>
    public List<DungeonInfo> GetActiveDungeons()
    {
        return _dungeons.Where(d => !d.Cleared).ToList();
    }
}
