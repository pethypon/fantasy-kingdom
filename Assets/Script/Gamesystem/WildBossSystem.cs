using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 強敵（縄張り付き中立ボス）システム。
/// 配置: ゲーム開始時にマップ上へ1体配置。両陣営の領土とダンジョンを避ける。
/// 無敵: 縄張り内に Player/Enemy の駒がいない間は被ダメージ0（Status.ApplyDamage で判定）。
/// AI: ターン毎に ProcessTurn(team) が呼ばれ、アーキタイプ固有の行動ルーチンを実行する。
/// </summary>
public class WildBossSystem : MonoBehaviour
{
    public const int TerritoryRadius = 1; // 3×3（チェビシェフ距離1）

    [Header("強敵プレハブ（StrangeKingを流用）")]
    [SerializeField] GameObject wildBossPrefab;

    [Header("配置親")]
    [SerializeField] Transform parent;

    [Header("縄張り表示")]
    [SerializeField] GameObject wildBossTerritoryPrefab;
    [SerializeField] Transform wildBossTerritoryParent;

    private MapCreate mapcreate;
    private CrystalSystem crystalsystem;
    private TerritorySystem territorysystem;
    private DungeonSystem dungeonSystem;
    private UnitSetting unitSetting;

    public Status SpawnedBoss { get; private set; }

    // 雷の魔導兵が設置した雷クリスタル群
    private readonly List<GameObject> _thunderCrystals = new List<GameObject>();

    // 縄張りタイル
    private readonly List<GameObject> _territoryTiles = new List<GameObject>();

    // ================================================================
    //  アーキタイプ別ステータスプロファイル（クリスタル級）
    //  BaseHP/ATK/DEF は他駒の Lv15 相当以上に設定
    // ================================================================
    struct Profile
    {
        public int HP, ATK, DEF, MaxAP;
        public string DisplayName;
    }

    // 成長率はメモ用（ボスはレベルスケールしないため現状未使用）
    // GhostKing:    ATK +0.34/Lv  HP +0.13/Lv  DEF +0.22/Lv
    // Dragon:       ATK +0.24/Lv  HP +0.15/Lv  DEF +0.18/Lv
    // RebelKnight:  ATK +0.30/Lv  HP +0.14/Lv  DEF +0.26/Lv
    // ThunderMagus: ATK +0.33/Lv  HP +0.12/Lv  DEF +0.20/Lv
    static readonly Dictionary<WildBossArchetype, Profile> Profiles = new Dictionary<WildBossArchetype, Profile>
    {
        { WildBossArchetype.GhostKing,    new Profile { DisplayName = "ゴーストキング",   HP = 6200, ATK = 26, DEF = 18, MaxAP = 23 } },
        { WildBossArchetype.Dragon,       new Profile { DisplayName = "ドラゴン",         HP = 8200, ATK = 32, DEF = 24, MaxAP = 20 } },
        { WildBossArchetype.RebelKnight,  new Profile { DisplayName = "反逆の騎士王",     HP = 7600, ATK = 28, DEF = 28, MaxAP = 15 } },
        { WildBossArchetype.ThunderMagus, new Profile { DisplayName = "雷の魔導兵",       HP = 7000, ATK = 30, DEF = 20, MaxAP = 20 } },
    };

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
    /// 縄張り範囲内の SetPos タイルにテリトリーオブジェクトを配置する。
    /// TerritorySystem と同じパターン（y -0.475f で地面に埋め込む）。
    /// </summary>
    void SpawnTerritoryTiles(Vector3 center)
    {
        if (wildBossTerritoryPrefab == null)
        {
            Debug.LogWarning("[WildBoss] 縄張りプレハブ未割当のためタイル設置スキップ");
            return;
        }
        if (mapcreate == null) return;
        Transform tParent = wildBossTerritoryParent != null ? wildBossTerritoryParent : transform;
        int cx = Mathf.RoundToInt(center.x);
        int cz = Mathf.RoundToInt(center.z);
        foreach (var p in mapcreate.SetPos)
        {
            int dx = Mathf.Abs(Mathf.RoundToInt(p.x) - cx);
            int dz = Mathf.Abs(Mathf.RoundToInt(p.z) - cz);
            if (Mathf.Max(dx, dz) > TerritoryRadius) continue;
            var tilePos = new Vector3(p.x, p.y - 0.475f, p.z);
            _territoryTiles.Add(Instantiate(wildBossTerritoryPrefab, tilePos, Quaternion.identity, tParent));
        }
        Debug.Log($"[WildBoss] 縄張りタイル設置 ({_territoryTiles.Count}マス)");
    }

    public void GenerateWildBoss()
    {
        if (mapcreate == null || crystalsystem == null) return;

        // プレハブフォールバック: wildBossPrefab 未割当なら UnitSetting.StrangePiece を流用
        GameObject prefabToUse = wildBossPrefab;
        string prefabSource = "wildBossPrefab";
        if (prefabToUse == null)
        {
            prefabToUse = unitSetting != null ? unitSetting.StrangePiecePrefab : null;
            prefabSource = "StrangePiece(fallback)";
            if (prefabToUse == null)
            {
                Debug.LogError("[WildBoss] wildBossPrefab も StrangePiece も未割当のためスポーン不可");
                return;
            }
            Debug.LogWarning($"[WildBoss] wildBossPrefab 未割当 → StrangePiece からフォールバック生成");
        }

        Vector3Int pcp = GridHelper.ToGrid(crystalsystem.PCP);
        Vector3Int ecp = GridHelper.ToGrid(crystalsystem.ECP);
        var setpos = mapcreate.SetPos;

        // 段階1: 距離・領土・ダンジョン全制約で候補抽出（minDist緩和）
        int[] minDistances = { 10, 8, 6, 4 };
        List<Vector3> candidates = null;
        string fallbackReason = "strict";
        foreach (int minDist in minDistances)
        {
            candidates = new List<Vector3>();
            foreach (var p in setpos)
            {
                Vector3Int g = GridHelper.ToGrid(p);
                if (GridHelper.ChebyshevDistance(g, pcp) < minDist) continue;
                if (GridHelper.ChebyshevDistance(g, ecp) < minDist) continue;
                if (territorysystem != null && territorysystem.IsInAnyTerritory(g.x, g.z)) continue;
                if (IsNearDungeon(g, TerritoryRadius)) continue;
                candidates.Add(p);
            }
            if (candidates.Count > 0) break;
        }

        // 段階2: ダンジョン回避を解除して再試行
        if (candidates == null || candidates.Count == 0)
        {
            candidates = new List<Vector3>();
            foreach (var p in setpos)
            {
                Vector3Int g = GridHelper.ToGrid(p);
                if (GridHelper.ChebyshevDistance(g, pcp) < 4) continue;
                if (GridHelper.ChebyshevDistance(g, ecp) < 4) continue;
                if (territorysystem != null && territorysystem.IsInAnyTerritory(g.x, g.z)) continue;
                candidates.Add(p);
            }
            if (candidates.Count > 0) fallbackReason = "ignoreDungeon";
        }

        // 段階3: 領土回避も解除し、距離最小のみ要求
        if (candidates == null || candidates.Count == 0)
        {
            candidates = new List<Vector3>();
            foreach (var p in setpos)
            {
                Vector3Int g = GridHelper.ToGrid(p);
                if (GridHelper.ChebyshevDistance(g, pcp) < 2) continue;
                if (GridHelper.ChebyshevDistance(g, ecp) < 2) continue;
                candidates.Add(p);
            }
            if (candidates.Count > 0) fallbackReason = "ignoreTerritory";
        }

        // 段階4: 全制約解除 — SetPos 全体から選択（最終保証）
        if (candidates == null || candidates.Count == 0)
        {
            candidates = new List<Vector3>(setpos);
            fallbackReason = "anywhere";
        }

        if (candidates.Count == 0)
        {
            Debug.LogError("[WildBoss] SetPos 自体が空のためスポーン不可");
            return;
        }

        Vector3 picked = candidates[Random.Range(0, candidates.Count)];

        // ランダムアーキタイプ選択
        var archetypes = (WildBossArchetype[])System.Enum.GetValues(typeof(WildBossArchetype));
        var pool = new List<WildBossArchetype>(archetypes.Length);
        foreach (var a in archetypes) if (a != WildBossArchetype.None) pool.Add(a);
        if (pool.Count == 0) return;
        var archetype = pool[Random.Range(0, pool.Count)];

        Debug.Log($"[WildBoss] 出現: archetype={archetype}  pos={picked}  prefab={prefabSource}  fallback={fallbackReason}");
        SpawnAt(picked, archetype, prefabToUse);
    }

    bool IsNearDungeon(Vector3Int pos, int range)
    {
        if (dungeonSystem == null) return false;
        foreach (var d in dungeonSystem.Dungeons)
            if (GridHelper.ChebyshevDistance(d.Position, pos) <= range) return true;
        return false;
    }

    void SpawnAt(Vector3 pos, WildBossArchetype archetype, GameObject prefab = null)
    {
        if (prefab == null) prefab = wildBossPrefab;
        if (prefab == null)
        {
            Debug.LogError("[WildBoss] SpawnAt: prefab が null");
            return;
        }
        Transform spawnParent = parent != null ? parent : transform;
        GameObject obj = unitSetting != null
            ? unitSetting.SpawnUnit(prefab, pos, spawnParent, 1)
            : Instantiate(prefab, pos, Quaternion.identity, spawnParent);

        var status = obj.GetComponentInChildren<Status>();
        if (status == null)
        {
            Debug.LogWarning("[WildBoss] Status未検出");
            return;
        }

        if (!Profiles.TryGetValue(archetype, out var prof))
        {
            Debug.LogWarning($"[WildBoss] プロファイル未定義: {archetype}");
            return;
        }

        // 縄張り設定
        status.isWildBoss = true;
        status.wildBossTerritoryCenter = new Vector3Int(Mathf.RoundToInt(pos.x), 0, Mathf.RoundToInt(pos.z));
        status.wildBossTerritoryRadius = TerritoryRadius;
        status.wildBossArchetype = archetype;
        status.wildBossMaxAP = prof.MaxAP;
        status.wildBossAP = prof.MaxAP;

        // クリスタル級ステータスを直接適用（レベルスケールは使わない）
        status.MaxHP = prof.HP;
        status.HP = prof.HP;
        status.ATK = prof.ATK;
        status.DEF = prof.DEF;
        status.Level = 15;

        // 中立扱い（Team.Obstacle だと AI 判定を阻害するため、
        // 物理的には Enemy チームにしつつ「縄張り固有AIで動かす」運用）
        // 上の spawn で Enemy にチーム上書きされるため、ここでも再指定
        status.team = Team.Obstacle;
        status.passiveskill = PassiveSkill.StrangeKingAura;

        if (obj.name != null && !obj.name.Contains(prof.DisplayName))
            obj.name = $"WildBoss_{prof.DisplayName}";

        SpawnedBoss = status;
        SpawnTerritoryTiles(pos);
        Debug.Log($"[WildBoss] 配置: {prof.DisplayName} at {pos} HP{prof.HP} ATK{prof.ATK} DEF{prof.DEF} AP{prof.MaxAP}");
    }

    // ================================================================
    //  ターン処理（WildBossState から呼ばれる専用ターン）
    // ================================================================
    /// <summary>
    /// 強敵の専用ターン。
    /// ターン順: プレイヤー → 異形（敵） → [魔物] → 強敵 → プレイヤー。
    /// 縄張り内にプレイヤー・敵・魔物がいれば行動し、いなければスキップ。
    /// </summary>
    public void ProcessTurn()
    {
        if (SpawnedBoss == null || !SpawnedBoss.IsAlive) return;

        SpawnedBoss.wildBossTurnCounter++;
        SpawnedBoss.wildBossAP = SpawnedBoss.wildBossMaxAP; // AP リフィル

        // カウンタベースのバフ消費
        if (SpawnedBoss.wildBossCounterTurns > 0) SpawnedBoss.wildBossCounterTurns--;
        if (SpawnedBoss.wildBossAtkBuffTurns > 0) SpawnedBoss.wildBossAtkBuffTurns--;

        // 仕様3.12: 脅威度遷移（HP50%以下で 25→75 へ昇格、一度のみ）
        if (SpawnedBoss.wildBossCurrentThreat < 75 && SpawnedBoss.HPRatio <= 0.5f)
        {
            SpawnedBoss.wildBossCurrentThreat = 75;
            Debug.Log($"[WildBoss] 脅威度上昇: 25 → 75 (HP={SpawnedBoss.HP}/{SpawnedBoss.MaxHP}) archetype={SpawnedBoss.wildBossArchetype}");
        }

        // 縄張り内にプレイヤー/敵/魔物がいなければスキップ
        if (!HasAnyIntruder()) return;

        switch (SpawnedBoss.wildBossArchetype)
        {
            case WildBossArchetype.GhostKing:    TurnGhostKing(); break;
            case WildBossArchetype.Dragon:       TurnDragon(); break;
            case WildBossArchetype.RebelKnight:  TurnRebelKnight(); break;
            case WildBossArchetype.ThunderMagus: TurnThunderMagus(); break;
        }
    }

    /// <summary>仕様3.12: HP50%以下で 25→75 へ昇格した「激怒モード」かを判定。</summary>
    bool IsHighThreat() => SpawnedBoss != null && SpawnedBoss.wildBossCurrentThreat >= 75;

    // ================================================================
    //  アーキタイプ別AI
    // ================================================================

    /// <summary>
    /// ゴーストキング:
    /// 2ターン毎: テレポート(3AP) → 視界外から攻撃+Blind付与(5AP) → デコイ召喚(10AP)。
    /// 奇数ターン: 通常攻撃(3AP)。
    /// </summary>
    void TurnGhostKing()
    {
        var targets = GetIntruders();
        if (targets.Count == 0) return;


        if (IsHighThreat())
        {
            // 仕様3.12: 高脅威時は周期無視で高期待値行動を毎ターン優先
            // テレポート → 視界外攻撃+NarrowVision → デコイ補充 → 通常攻撃で AP 使い切り
            if (TrySpendAP(3)) TeleportInTerritory();
            if (TrySpendAP(5)) AttackFromOutsideVision(targets);
            if (CountDecoys() == 0 && TrySpendAP(10)) SummonGhostDecoy();
            if (TrySpendAP(3))
                AttackTarget(targets[Random.Range(0, targets.Count)]);
            return;
        }

        if (SpawnedBoss.wildBossTurnCounter % 2 == 0)
        {
            // 2ターン毎のサイクル
            if (TrySpendAP(3)) TeleportInTerritory();
            if (TrySpendAP(5)) AttackFromOutsideVision(targets);
            if (CountDecoys() == 0 && TrySpendAP(10)) SummonGhostDecoy();
        }
        else
        {
            // 奇数ターン: 通常攻撃
            if (TrySpendAP(3))
                AttackTarget(targets[Random.Range(0, targets.Count)]);
        }
    }

    /// <summary>
    /// ドラゴン: 4ターン毎に炎ブレス(10AP)=縄張り内全体攻撃。
    /// 非発動ターンは 50% でATK1.25倍バフ(5AP) / 50% で前方3マス攻撃(4AP)。
    /// </summary>
    void TurnDragon()
    {

        if (IsHighThreat())
        {
            // 仕様3.12: 高脅威時は周期無視で炎ブレス優先 → 余ったAPで前方攻撃
            if (TrySpendAP(10)) FireBreathAll();
            if (TrySpendAP(4)) AttackFrontLine(3);
            return;
        }

        if (SpawnedBoss.wildBossTurnCounter % 4 == 0 && TrySpendAP(10))
        {
            FireBreathAll();
            return;
        }

        if (Random.value < 0.5f && TrySpendAP(5))
        {
            SpawnedBoss.wildBossAtkBuffTurns = 2; // 2ターン有効
            Debug.Log("[WildBoss/Dragon] 咆哮: ATK×1.25（2ターン）");
        }
        else if (TrySpendAP(4))
        {
            AttackFrontLine(3);
        }
    }

    /// <summary>
    /// 反逆の騎士王:
    /// 2ターン毎: 反撃付与(1.5倍返し, 5AP) + 親衛騎士がいなければ最大2体召喚(10AP)。
    /// HP50%以下でPhase2: 反撃→3倍返し・親衛隊上限4体・脅威度引き上げ。
    /// </summary>
    void TurnRebelKnight()
    {
        CheckRebelKnightPhase2();

        int maxGuards = SpawnedBoss.wildBossPhase2Active ? 4 : 2;

        if (IsHighThreat())
        {
            // 仕様3.12: 高脅威時は反撃と親衛隊召喚を毎ターン同時試行
            if (TrySpendAP(5))
            {
                SpawnedBoss.wildBossCounterTurns = 2;
                string mult = SpawnedBoss.wildBossPhase2Active ? "3.0" : "1.5";
                Debug.Log($"[WildBoss/RebelKnight] 反撃の誓い（2ターン, 返しダメ×{mult}, 高脅威）");
            }
            int curHigh = CountGuardKnights();
            if (curHigh < maxGuards && TrySpendAP(10))
                SummonGuardKnights(maxGuards - curHigh);
            return;
        }

        if (SpawnedBoss.wildBossTurnCounter % 2 == 0 && TrySpendAP(5))
        {
            SpawnedBoss.wildBossCounterTurns = 2;
            string mult = SpawnedBoss.wildBossPhase2Active ? "3.0" : "1.5";
            Debug.Log($"[WildBoss/RebelKnight] 反撃の誓い（2ターン, 返しダメ×{mult}）");
        }

        int current = CountGuardKnights();
        if (current < maxGuards && TrySpendAP(10))
        {
            SummonGuardKnights(maxGuards - current);
        }
    }

    void CheckRebelKnightPhase2()
    {
        if (SpawnedBoss.wildBossPhase2Active) return;
        if (SpawnedBoss.HPRatio >= 0.5f) return;

        SpawnedBoss.wildBossPhase2Active = true;
        Debug.Log("[WildBoss/RebelKnight] ★Phase2突入★ HP50%以下: 反撃3倍・親衛4体・脅威度50");
    }

    /// <summary>
    /// 雷の魔導兵: 毎ターン雷クリスタル設置(3AP)。
    /// 5ターン毎に最後のクリスタル位置へ高速移動し、全クリスタル起爆(10AP, 半径1攻撃)。
    /// それ以外のターンは縄張り内ランダム3体に雷(ATK×1.25)を落とす。
    /// </summary>
    void TurnThunderMagus()
    {

        // 毎ターン設置
        if (TrySpendAP(3)) PlaceThunderCrystal();

        if (IsHighThreat())
        {
            // 仕様3.12: 高脅威時は周期無視で起爆優先 → 余ったAPで雷撃
            if (_thunderCrystals.Count > 0 && TrySpendAP(10)) DetonateThunderCrystals();
            if (TrySpendAP(5)) ThunderStrikeRandom(3);
            return;
        }

        if (SpawnedBoss.wildBossTurnCounter % 5 == 0 && _thunderCrystals.Count > 0 && TrySpendAP(10))
        {
            DetonateThunderCrystals();
        }
        else if (TrySpendAP(5))
        {
            ThunderStrikeRandom(3);
        }
    }

    // ================================================================
    //  行動ヘルパー
    // ================================================================

    bool TrySpendAP(int cost)
    {
        if (SpawnedBoss.wildBossAP < cost) return false;
        SpawnedBoss.wildBossAP -= cost;
        return true;
    }

    List<Status> GetIntruders()
    {
        var list = new List<Status>();
        var reg = UnitRegistry.Instance;
        if (reg == null) return list;
        CollectInTerritory(reg.PlayerUnits, list);
        CollectInTerritory(reg.EnemyUnits, list);
        return list;
    }

    void CollectInTerritory(IReadOnlyList<Status> src, List<Status> dst)
    {
        if (src == null) return;
        for (int i = 0; i < src.Count; i++)
        {
            var u = src[i];
            if (u == null || !u.IsAlive || u == SpawnedBoss) continue;
            if (u.team == Team.Obstacle) continue;
            if (!InTerritory(u.GridPosition)) continue;
            dst.Add(u);
        }
    }

    bool InTerritory(Vector3Int pos)
    {
        int dx = Mathf.Abs(pos.x - SpawnedBoss.wildBossTerritoryCenter.x);
        int dz = Mathf.Abs(pos.z - SpawnedBoss.wildBossTerritoryCenter.z);
        return Mathf.Max(dx, dz) <= SpawnedBoss.wildBossTerritoryRadius;
    }

    bool HasAnyIntruder()
    {
        var reg = UnitRegistry.Instance;
        if (reg == null) return false;
        return AnyInTerritory(reg.PlayerUnits) || AnyInTerritory(reg.EnemyUnits);
    }

    bool AnyInTerritory(IReadOnlyList<Status> list)
    {
        if (list == null) return false;
        for (int i = 0; i < list.Count; i++)
        {
            var u = list[i];
            if (u == null || !u.IsAlive || u == SpawnedBoss) continue;
            if (u.team == Team.Obstacle) continue;
            if (InTerritory(u.GridPosition)) return true;
        }
        return false;
    }

    void AttackTarget(Status target)
    {
        if (target == null || !target.IsAlive) return;
        int dmg = DamageCalculator.CalcNormal(SpawnedBoss, target);
        if (SpawnedBoss.wildBossAtkBuffTurns > 0) dmg = Mathf.RoundToInt(dmg * 1.25f);
        target.ApplyDamage(dmg);
        Debug.Log($"[WildBoss] 通常攻撃: {target.kind} に {dmg} dmg");
    }

    void AttackTargetWithDebuff(Status target, StatusEffectType debuff, int duration)
    {
        AttackTarget(target);
        if (target.IsAlive)
        {
            StatusEffectSystem.ApplyDebuff(target, debuff, duration);
            Debug.Log($"[WildBoss] デバフ付与: {debuff} ({duration}ターン)");
        }
    }

    void TeleportInTerritory()
    {
        var candidates = new List<Vector3>();
        if (mapcreate == null) return;
        foreach (var p in mapcreate.SetPos)
        {
            var g = GridHelper.ToGrid(p);
            if (InTerritory(g) && !IsOccupied(g)) candidates.Add(p);
        }
        if (candidates.Count == 0) return;
        var picked = candidates[Random.Range(0, candidates.Count)];
        SpawnedBoss.transform.position = picked;
        Debug.Log($"[WildBoss/GhostKing] テレポート → {picked}");
    }

    bool IsOccupied(Vector3Int pos)
    {
        var reg = UnitRegistry.Instance;
        if (reg == null) return false;
        return OccupiedIn(reg.PlayerUnits, pos) || OccupiedIn(reg.EnemyUnits, pos);
    }

    bool OccupiedIn(IReadOnlyList<Status> list, Vector3Int pos)
    {
        if (list == null) return false;
        for (int i = 0; i < list.Count; i++)
        {
            var u = list[i];
            if (u == null || !u.IsAlive) continue;
            var g = u.GridPosition;
            if (g.x == pos.x && g.z == pos.z) return true;
        }
        return false;
    }

    void FireBreathAll()
    {
        var targets = GetIntruders();
        Debug.Log($"[WildBoss/Dragon] 炎ブレス: {targets.Count}体に命中");
        foreach (var t in targets) AttackTarget(t);
    }

    void AttackFrontLine(int range)
    {
        var dir = SpawnedBoss.direction == Direction.S ? -1 : 1;
        var center = SpawnedBoss.GridPosition;
        var reg = UnitRegistry.Instance;
        if (reg == null) return;
        for (int i = 1; i <= range; i++)
        {
            var cell = new Vector3Int(center.x, 0, center.z + dir * i);
            var tgt = FindAt(reg.PlayerUnits, cell) ?? FindAt(reg.EnemyUnits, cell);
            if (tgt != null) { AttackTarget(tgt); return; }
        }
    }

    Status FindAt(IReadOnlyList<Status> list, Vector3Int pos)
    {
        if (list == null) return null;
        for (int i = 0; i < list.Count; i++)
        {
            var u = list[i];
            if (u == null || !u.IsAlive) continue;
            var g = u.GridPosition;
            if (g.x == pos.x && g.z == pos.z) return u;
        }
        return null;
    }

    // ---- ゴーストキング ----
    readonly List<Status> _ghostDecoys = new List<Status>();

    int CountDecoys()
    {
        _ghostDecoys.RemoveAll(d => d == null || !d.IsAlive);
        return _ghostDecoys.Count;
    }

    /// <summary>縄張り内を高速移動後、ターゲットの視界外から攻撃+Blind付与（重複不可）。</summary>
    void AttackFromOutsideVision(List<Status> targets)
    {
        if (targets.Count == 0) return;

        var bossPos = SpawnedBoss.GridPosition;

        // ターゲットの VisionCell にボス位置が含まれない（=視界外）ものを優先
        var blindTargets = new List<Status>();
        foreach (var t in targets)
        {
            if (t.VisionCell == null || !t.VisionCell.Contains(bossPos))
                blindTargets.Add(t);
        }

        var chosen = blindTargets.Count > 0
            ? blindTargets[Random.Range(0, blindTargets.Count)]
            : targets[Random.Range(0, targets.Count)];

        AttackTarget(chosen);

        // NarrowVision付与（強敵専用デバフ。Blindと同効果だが識別を分離して仕様3.11準拠）
        if (chosen.IsAlive)
        {
            bool applied = StatusEffectSystem.ApplyDebuff(chosen, StatusEffectType.NarrowVision, 1);
            if (applied) Debug.Log($"[WildBoss/GhostKing] 視界縮小付与: {chosen.kind}（1T）");
        }
    }

    /// <summary>一発で死ぬゴーストキングのデコイを隣接マスに召喚。ATKはゴーストキングと同値。</summary>
    void SummonGhostDecoy()
    {
        var adj = FindFreeAdjacentCell();
        if (!adj.HasValue) return;

        var g = adj.Value;
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = "GhostDecoy";
        go.transform.SetParent(parent != null ? parent : transform);
        go.transform.position = new Vector3(g.x, g.y, g.z);
        go.transform.localScale = new Vector3(0.9f, 1.1f, 0.9f);
        var r = go.GetComponent<Renderer>();
        if (r != null)
        {
            var m = new Material(Shader.Find("Standard"));
            m.color = new Color(0.6f, 0.8f, 1f, 0.7f); // 半透明ブルー
            r.material = m;
        }
        var s = go.AddComponent<Status>();
        s.kind = Kind.King;
        s.team = Team.Obstacle;
        s.type = Type.Unit;
        s.MaxHP = 1;
        s.HP = 1;
        s.ATK = SpawnedBoss.ATK; // ゴーストキングと同じ攻撃力
        s.DEF = 0;
        s.Level = SpawnedBoss.Level;
        _ghostDecoys.Add(s);
        Debug.Log($"[WildBoss/GhostKing] デコイ召喚 at {g} ATK={s.ATK}");
    }

    // ---- 反逆の騎士王 ----
    readonly List<Status> _guardKnights = new List<Status>();
    int CountGuardKnights()
    {
        _guardKnights.RemoveAll(k => k == null || !k.IsAlive);
        return _guardKnights.Count;
    }

    void SummonGuardKnights(int n)
    {
        for (int i = 0; i < n; i++)
        {
            var adj = FindFreeAdjacentCell();
            if (!adj.HasValue) break;
            var g = adj.Value;
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "GuardKnight";
            go.transform.SetParent(parent != null ? parent : transform);
            go.transform.position = new Vector3(g.x, g.y, g.z);
            go.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
            var r = go.GetComponent<Renderer>();
            if (r != null) { var m = new Material(Shader.Find("Standard")); m.color = new Color(0.5f, 0.1f, 0.1f); r.material = m; }
            var s = go.AddComponent<Status>();
            s.kind = Kind.Knight;
            s.team = Team.Obstacle;
            s.type = Type.Unit;
            s.MaxHP = 400; s.HP = 400; s.ATK = 18; s.DEF = 18; s.Level = 10;
            _guardKnights.Add(s);
            Debug.Log($"[WildBoss/RebelKnight] 親衛騎士召喚 at {g}");
        }
    }

    Vector3? FindFreeAdjacentCell()
    {
        var c = SpawnedBoss.GridPosition;
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dz == 0) continue;
                var cell = new Vector3Int(c.x + dx, 0, c.z + dz);
                if (!InTerritory(cell)) continue;
                if (IsOccupied(cell)) continue;
                if (mapcreate != null && mapcreate.TryGetHeight(cell.x, cell.z, out float y))
                    return new Vector3(cell.x, y, cell.z);
            }
        return null;
    }

    // ---- 雷の魔導兵 ----
    void PlaceThunderCrystal()
    {
        var adj = FindFreeAdjacentCell();
        if (!adj.HasValue) return;
        var g = adj.Value;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "ThunderCrystal";
        go.transform.SetParent(parent != null ? parent : transform);
        go.transform.position = new Vector3(g.x, g.y + 0.3f, g.z);
        go.transform.localScale = new Vector3(0.4f, 0.6f, 0.4f);
        var col = go.GetComponent<Collider>(); if (col != null) col.enabled = false;
        var r = go.GetComponent<Renderer>();
        if (r != null) { var m = new Material(Shader.Find("Standard")); m.color = new Color(0.4f, 0.7f, 1f); r.material = m; }
        _thunderCrystals.Add(go);
        Debug.Log($"[WildBoss/ThunderMagus] 雷クリスタル設置 at {g}");
    }

    void DetonateThunderCrystals()
    {
        if (_thunderCrystals.Count == 0) return;
        var last = _thunderCrystals[_thunderCrystals.Count - 1];
        if (last != null) SpawnedBoss.transform.position = last.transform.position;

        var reg = UnitRegistry.Instance;
        foreach (var c in _thunderCrystals)
        {
            if (c == null) continue;
            var g = GridHelper.ToGridXZ(c.transform.position);
            // 半径1の全セルに攻撃
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    var cell = new Vector3Int(g.x + dx, 0, g.z + dz);
                    var t = FindAt(reg?.PlayerUnits, cell) ?? FindAt(reg?.EnemyUnits, cell);
                    if (t != null) AttackTarget(t);
                }
            Destroy(c);
        }
        _thunderCrystals.Clear();
        Debug.Log("[WildBoss/ThunderMagus] 雷一斉起爆");
    }

    void ThunderStrikeRandom(int count)
    {
        var intruders = GetIntruders();
        for (int i = 0; i < count && intruders.Count > 0; i++)
        {
            int idx = Random.Range(0, intruders.Count);
            var t = intruders[idx];
            intruders.RemoveAt(idx);
            int dmg = Mathf.RoundToInt(DamageCalculator.CalcNormal(SpawnedBoss, t) * 1.25f);
            t.ApplyDamage(dmg);
            Debug.Log($"[WildBoss/ThunderMagus] 雷直撃: {t.kind} に {dmg}");
        }
    }

    // ================================================================
    //  反撃（反逆の騎士王の反撃バフ発動時に BattleSystem から呼ばれる）
    // ================================================================
    public static void TryReflectDamage(Status target, Status attacker, int receivedDamage)
    {
        if (target == null || attacker == null) return;
        if (!target.isWildBoss) return;
        if (target.wildBossArchetype != WildBossArchetype.RebelKnight) return;
        if (target.wildBossCounterTurns <= 0) return;
        if (!attacker.IsAlive) return;
        // Phase2: 反撃3倍（1.5×2）、通常: 1.5倍
        float mult = target.wildBossPhase2Active ? 3.0f : 1.5f;
        int reflect = Mathf.RoundToInt(receivedDamage * mult);
        attacker.ApplyDamage(reflect);
        Debug.Log($"[WildBoss/RebelKnight] 反撃×{mult}: {attacker.kind} に {reflect} dmg");
    }
}
