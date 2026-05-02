using System.Collections.Generic;
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
        public int SharedRemainingTurns = ClaimTurns; // 共有タイマー残ターン (10→0)。陣営変更時も引き継がれる
        public bool Contested;       // 双方が同一ダンジョンに存在
        public bool Cleared;         // アーティファクト獲得済み
        public Artifact Reward = Artifact.None;
        public GameObject Marker;    // 視覚表示用

        /// <summary>後方互換: SharedRemainingTurns から派生する占有進捗 (0〜ClaimTurns)</summary>
        public int ClaimProgress => ClaimTurns - SharedRemainingTurns;

        // ---- ダンジョン内モンスター（踏破阻害要素） ----
        public int MonsterHP = MonsterMaxHP;
        public int MonsterATK = MonsterATKValue;
        public bool MonsterAlive => MonsterHP > 0;
    }

    [Header("定数")]
    public const int DungeonCount = 2;
    public const int ClaimTurns = 10;
    public const int MonsterMaxHP = 120;
    public const int MonsterATKValue = 25;
    public const int UnitAttackDamagePerTurn = 30;

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

        // 両クリスタルから十分離れた領土外候補を抽出。
        // マップサイズ/領土半径に応じて段階的に最小距離を緩める。
        List<Vector3Int> candidates = null;
        int[] minDistances = { 6, 5, 4, 3 };
        foreach (int minDist in minDistances)
        {
            candidates = new List<Vector3Int>();
            foreach (var v in setpos)
            {
                var p = GridHelper.ToGrid(v);
                if (GridHelper.ChebyshevDistance(p, pcp) < minDist) continue;
                if (GridHelper.ChebyshevDistance(p, ecp) < minDist) continue;
                if (territorysystem.IsInAnyTerritory(p.x, p.z)) continue;
                candidates.Add(p);
            }
            if (candidates.Count >= DungeonCount) break;
        }

        if (candidates == null || candidates.Count < DungeonCount)
        {
            Debug.LogWarning("[DungeonSystem] ダンジョン候補が不足（全SetPosへフォールバック）");
            candidates = new List<Vector3Int>(setpos.Count);
            foreach (var v in setpos) candidates.Add(GridHelper.ToGrid(v));
        }

        for (int i = 0; i < DungeonCount && candidates.Count > 0; i++)
        {
            int idx = Random.Range(0, candidates.Count);
            Vector3Int picked = candidates[idx];
            candidates.RemoveAt(idx);

            // 既存ダンジョンから最低距離3（候補枯渇時は許容）
            if (candidates.Count > 0
                && DungeonTooClose(picked, 3))
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

    private bool DungeonTooClose(Vector3Int pos, int minDist)
    {
        for (int i = 0; i < _dungeons.Count; i++)
            if (GridHelper.ChebyshevDistance(_dungeons[i].Position, pos) < minDist) return true;
        return false;
    }

    private Artifact RollArtifact()
    {
        var values = (Artifact[])System.Enum.GetValues(typeof(Artifact));
        var pool = new List<Artifact>(values.Length);
        foreach (var a in values) if (a != Artifact.None) pool.Add(a);
        if (pool.Count == 0) return Artifact.None;
        return pool[Random.Range(0, pool.Count)];
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
                d.ClaimingTeam = Team.None;
                // 競合時: タイマー停止（残ターンは引き継ぎ）
                Debug.Log($"[DungeonSystem] ダンジョン{d.Position} 競合！ タイマー停止 残り={d.SharedRemainingTurns}T");
                continue;
            }

            d.Contested = false;

            // モンスター討伐フェーズ: 未討伐なら先にHPを削る（占有タイマーは進めない）
            if (d.MonsterAlive && (playerPresent || enemyPresent))
            {
                Team invader = playerPresent ? Team.Player : Team.Enemy;
                int dealt = Mathf.Min(UnitAttackDamagePerTurn, d.MonsterHP);
                d.MonsterHP -= dealt;
                Debug.Log($"[DungeonSystem] {invader} がダンジョン{d.Position}のモンスターに{dealt}ダメージ(残{d.MonsterHP})");

                // 反撃: 踏み入ったユニット群にATKダメージを分配
                CounterMonsterAttack(invader, d.Position, d.MonsterATK);

                if (d.MonsterAlive) continue; // まだ生きている → 占有開始しない
                Debug.Log($"[DungeonSystem] ダンジョン{d.Position}のモンスター討伐！ 占有開始可能");
            }

            // 残りターン減少型: 占有チームを設定（変更時もタイマー引き継ぎ）
            if (playerPresent)
            {
                if (d.ClaimingTeam != Team.Player)
                {
                    Debug.Log($"[DungeonSystem] ダンジョン{d.Position} 奪取: {d.ClaimingTeam}→Player remainingTurns={d.SharedRemainingTurns}");
                    d.ClaimingTeam = Team.Player;
                }
                d.SharedRemainingTurns = Mathf.Max(0, d.SharedRemainingTurns - 1);
            }
            else if (enemyPresent)
            {
                if (d.ClaimingTeam != Team.Enemy)
                {
                    Debug.Log($"[DungeonSystem] ダンジョン{d.Position} 奪取: {d.ClaimingTeam}→Enemy remainingTurns={d.SharedRemainingTurns}");
                    d.ClaimingTeam = Team.Enemy;
                }
                d.SharedRemainingTurns = Mathf.Max(0, d.SharedRemainingTurns - 1);
            }
            // 誰もいない場合はタイマー停止（残ターン保持）

            if (d.SharedRemainingTurns <= 0 && d.ClaimingTeam != Team.None)
            {
                GrantArtifact(d.ClaimingTeam, d.Reward);
                d.Cleared = true;
                if (d.Marker != null) Destroy(d.Marker);
                Debug.Log($"[DungeonSystem] {d.ClaimingTeam} がダンジョン{d.Position}を制圧、{d.Reward}を獲得");
                if (MatchStats.Instance != null && d.ClaimingTeam == Team.Player)
                {
                    MatchStats.Instance.DungeonsClaimed++;
                    MatchStats.Instance.ArtifactsAcquired++;
                }
                if (d.ClaimingTeam == Team.Player)
                    AchievementSystem.GetOrCreate().OnDungeonCleared();
            }
        }
    }

    /// <summary>ダンジョン内モンスターから踏破ユニットへの反撃処理</summary>
    private void CounterMonsterAttack(Team team, Vector3Int pos, int atk)
    {
        if (unitSetting == null) return;
        Transform parent = team == Team.Player ? unitSetting.PlayerUnit : unitSetting.EnemyUnit;
        if (parent == null) return;

        foreach (Transform child in parent)
        {
            if (child == null || !child.gameObject.activeInHierarchy) continue;
            var s = child.GetComponent<Status>();
            if (s == null || !s.IsAlive) continue;
            if (s.GridPosition.x != pos.x || s.GridPosition.z != pos.z) continue;

            int dmg = Mathf.Max(1, atk - s.DEF / 4);
            s.ApplyDamage(dmg);
            Debug.Log($"[DungeonSystem] モンスター反撃: {s.kind} に {dmg} ダメージ（残HP:{s.HP}）");
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

    /// <summary>サブクリスタル配置時、近傍ダンジョンを起動（残りタイマーを2T加速）する</summary>
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
                    d.SharedRemainingTurns = Mathf.Max(1, d.SharedRemainingTurns - 2);
                    Debug.Log($"[DungeonSystem] {team} のサブクリスタルがダンジョン{d.Position}を起動 (-2T → 残り{d.SharedRemainingTurns}T)");
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
        var result = new List<DungeonInfo>();
        for (int i = 0; i < _dungeons.Count; i++)
            if (!_dungeons[i].Cleared) result.Add(_dungeons[i]);
        return result;
    }
}
