using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ダンジョンシステム: マップ上にランダムで2箇所のダンジョンを配置する。
/// サブクリスタルで起動し、共有10ターンタイマー中に占有し続けるとアーティファクトを獲得。
/// 支配変更でも進捗を保持し、報酬後は次の共有10ラウンドへ進む。
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
        [System.NonSerialized] public GameObject Marker;    // 視覚表示用

        /// <summary>後方互換: SharedRemainingTurns から派生する占有進捗 (0〜ClaimTurns)</summary>
        public int ClaimProgress => ClaimTurns - SharedRemainingTurns;

        // ---- ダンジョン内モンスター（踏破阻害要素） ----
        public int MonsterHP = 0; // Legacy field; control no longer requires a unit battle.
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

    /// <summary>ダンジョンマーカーの専用親コンテナ（VisionGeneratorからの視界制御用）。Init で生成。</summary>
    public Transform MarkerParent { get; private set; }

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

        // マーカー専用コンテナを生成（VisionGenerator が一括で表示制御するため）
        if (MarkerParent == null)
        {
            var holder = new GameObject("DungeonMarkers");
            holder.transform.SetParent(transform, false);
            MarkerParent = holder.transform;
        }
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

    private void Update()
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame || Camera.main == null) return;
        if (UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;
        if (!Physics.Raycast(Camera.main.ScreenPointToRay(mouse.position.ReadValue()), out var hit, GameConstants.DefaultRayDistance)) return;
        var vision = GetComponent<VisionGenerator>();
        foreach (var dungeon in _dungeons)
        {
            if (!GridHelper.MatchXZ(hit.point, dungeon.Position) || vision == null
                || !vision.IsInVisionXZ(Team.Player, dungeon.Position)) continue;
            RefreshController(dungeon);
            string owner = dungeon.ClaimingTeam == Team.Player ? "プレイヤー" : dungeon.ClaimingTeam == Team.Enemy ? "異形の軍勢" : "無支配・停止中";
            ToastMessageUI.Show($"ダンジョン {dungeon.ClaimProgress}/{ClaimTurns} — {owner}", ToastMessageUI.MessageType.Info, 4f);
        }
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
        if (MarkerParent != null) marker.transform.SetParent(MarkerParent, false);
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
    public int LastProcessedRound { get; private set; } = -1;

    // Exactly one shared tick after the previous full round, at PlayerStart.
    public void ProcessTurn(Team team, int round)
    {
        if (team != Team.Player || round <= LastProcessedRound) return;
        LastProcessedRound = round;
        foreach (var d in _dungeons)
        {
            RefreshController(d);
            if (!AdvanceCycle(d)) continue;
            GrantArtifact(d.ClaimingTeam, d.Reward);
            if (MatchStats.Instance != null && d.ClaimingTeam == Team.Player)
            {
                MatchStats.Instance.DungeonsClaimed++;
                MatchStats.Instance.ArtifactsAcquired++;
            }
            if (d.ClaimingTeam == Team.Player) AchievementSystem.GetOrCreate().OnDungeonCleared();
            d.Reward = RollArtifact();
        }
    }

    public static bool AdvanceCycle(DungeonInfo d)
    {
        if (d.Contested || d.ClaimingTeam == Team.None) return false;
        d.Cleared = false;
        d.SharedRemainingTurns = Mathf.Clamp(d.SharedRemainingTurns, 1, ClaimTurns) - 1;
        if (d.SharedRemainingTurns > 0) return false;
        d.SharedRemainingTurns = ClaimTurns;
        return true;
    }

    private bool HasSubCrystalControl(Team team, Vector3Int pos)
    {
        var parent = buildsystem != null ? buildsystem.GetBuildingParent(team) : null;
        if (parent == null) return false;
        foreach (var building in parent.GetComponentsInChildren<Status>())
            if (building.IsAlive && building.facilityKind == FacilityKind.SubCrystal
                && GridHelper.ChebyshevDistance(building.GridPosition, pos) <= SubCrystalSystem.SubCrystalTerritoryRadius)
                return true;
        return false;
    }

    private void RefreshController(DungeonInfo d)
    {
        bool player = HasSubCrystalControl(Team.Player, d.Position);
        bool enemy = HasSubCrystalControl(Team.Enemy, d.Position);
        d.Contested = player && enemy;
        d.ClaimingTeam = player == enemy ? Team.None : player ? Team.Player : Team.Enemy;
    }

    public void ActivateFromSubCrystal(Vector3Int subCrystalPos, Team team)
    {
        foreach (var d in _dungeons) RefreshController(d);
    }

    public void RestoreDungeons(List<DungeonInfo> saved, int lastRound)
    {
        if (saved == null) return; // Older saves had no dungeon snapshot.
        foreach (var d in _dungeons) if (d.Marker != null) Destroy(d.Marker);
        _dungeons.Clear();
        foreach (var d in saved)
        {
            d.Cleared = false;
            d.SharedRemainingTurns = Mathf.Clamp(d.SharedRemainingTurns, 1, ClaimTurns);
            d.Marker = CreateMarker(d.Position);
            _dungeons.Add(d);
            RefreshController(d);
        }
        LastProcessedRound = lastRound;
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
        s.ApplyHeal(hpBonus);
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
