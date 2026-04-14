using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 第3陣営「魔物」の管理システム。
/// - ゲーム開始時にマップ上に魔物をスポーン
/// - 10ターンごとに補充（前回の半分以下に減っていたら追加）
/// - 強敵ボス（Lv50相当）を1体スポーン（縄張り持ち）
/// - 魔物のレベルはPlayer/Enemyユニットの平均Lvに応じてスケール
/// </summary>
public class MonsterSystem : MonoBehaviour
{
    // ---- 外部参照（Init で注入） ----
    private UnitSetting unitSetting;
    private MapCreate mapCreate;
    private MoveGenerator moveGenerator;
    private CrystalSystem crystalSystem;

    // ---- 設定定数 ----
    /// <summary>初期スポーン数（雑魚）</summary>
    public const int InitialSpawnCount = 5;
    /// <summary>補充間隔（ターン）</summary>
    public const int ReinforceInterval = 10;
    /// <summary>補充条件: この割合以下に減ったら補充</summary>
    public const float ReinforceThreshold = 0.5f;
    /// <summary>強敵ボスのレベル</summary>
    public const int BossLevel = 50;
    /// <summary>強敵の縄張り半径（最小）</summary>
    public const int BossTerritoryMin = 2;
    /// <summary>強敵の縄張り半径（最大）</summary>
    public const int BossTerritoryMax = 6;

    // ---- 状態 ----
    /// <summary>前回補充時の魔物数</summary>
    private int _lastReinforceCount;
    /// <summary>前回補充ターン</summary>
    private int _lastReinforceTurn;

    // ---- ボス情報 ----
    /// <summary>ボスの縄張り中心</summary>
    public Vector3Int BossTerritoryCenter { get; private set; }
    /// <summary>ボスの縄張り半径</summary>
    public int BossTerritoryRadius { get; private set; }
    /// <summary>ボスユニットの参照</summary>
    public Status BossUnit { get; private set; }

    // ================================================================
    //  初期化
    // ================================================================
    public void Init(UnitSetting unitSetting, MapCreate mapCreate,
                     MoveGenerator moveGenerator, CrystalSystem crystalSystem)
    {
        this.unitSetting = unitSetting;
        this.mapCreate = mapCreate;
        this.moveGenerator = moveGenerator;
        this.crystalSystem = crystalSystem;
    }

    // ================================================================
    //  ゲーム開始時のスポーン
    // ================================================================
    public void SpawnInitialMonsters()
    {
        if (unitSetting == null || unitSetting.MonsterUnit == null)
        {
            Debug.LogWarning("[MonsterSystem] MonsterUnit 親オブジェクトが未設定です");
            return;
        }

        int level = CalcMonsterLevel();
        var spawnPositions = GetMonsterSpawnCandidates();

        // 雑魚魔物をスポーン
        int spawned = 0;
        for (int i = 0; i < InitialSpawnCount && spawnPositions.Count > 0; i++)
        {
            int idx = Random.Range(0, spawnPositions.Count);
            Vector3 pos = spawnPositions[idx];
            spawnPositions.RemoveAt(idx);

            SpawnMonsterUnit(pos, level, false);
            spawned++;
        }

        _lastReinforceCount = spawned;
        _lastReinforceTurn = 0;

        Debug.Log($"[MonsterSystem] 初期魔物 {spawned}体 スポーン (Lv{level})");

        // 強敵ボスをスポーン
        SpawnBoss(spawnPositions);
    }

    // ================================================================
    //  ターン処理: 補充判定
    // ================================================================
    /// <summary>
    /// ターン開始時に呼び出す。10ターン間隔で魔物を補充する。
    /// </summary>
    public void ProcessTurn(int turnCount)
    {
        if (unitSetting == null || unitSetting.MonsterUnit == null) return;

        // ボスが死んでいたら再スポーン
        if (BossUnit == null || !BossUnit.IsAlive)
        {
            var candidates = GetMonsterSpawnCandidates();
            if (candidates.Count > 0)
            {
                SpawnBoss(candidates);
                Debug.Log("[MonsterSystem] ボス再スポーン");
            }
        }

        // 10ターンごとに補充判定
        if (turnCount - _lastReinforceTurn < ReinforceInterval) return;

        int currentCount = CountAliveMonsters();
        if (currentCount > _lastReinforceCount * ReinforceThreshold) return;

        // 補充実行
        int level = CalcMonsterLevel();
        var spawnPositions = GetMonsterSpawnCandidates();
        int toSpawn = InitialSpawnCount - currentCount;
        int spawned = 0;

        for (int i = 0; i < toSpawn && spawnPositions.Count > 0; i++)
        {
            int idx = Random.Range(0, spawnPositions.Count);
            Vector3 pos = spawnPositions[idx];
            spawnPositions.RemoveAt(idx);

            SpawnMonsterUnit(pos, level, false);
            spawned++;
        }

        _lastReinforceCount = currentCount + spawned;
        _lastReinforceTurn = turnCount;

        Debug.Log($"[MonsterSystem] ターン{turnCount}: 魔物 {spawned}体 補充 (Lv{level}, 残存{currentCount})");
    }

    // ================================================================
    //  ボススポーン
    // ================================================================
    private void SpawnBoss(List<Vector3> candidates)
    {
        if (candidates.Count == 0) return;

        // クリスタルから十分離れた位置にスポーン
        Vector3 pcp = crystalSystem.PCP;
        Vector3 ecp = crystalSystem.ECP;

        var bossCandidates = candidates.Where(p =>
        {
            float distP = Mathf.Max(Mathf.Abs(p.x - pcp.x), Mathf.Abs(p.z - pcp.z));
            float distE = Mathf.Max(Mathf.Abs(p.x - ecp.x), Mathf.Abs(p.z - ecp.z));
            return distP >= 8 && distE >= 8; // 両陣営から8マス以上離れた場所
        }).ToList();

        // 十分離れた場所がなければ候補から適当に
        if (bossCandidates.Count == 0)
            bossCandidates = candidates;

        Vector3 bossPos = bossCandidates[Random.Range(0, bossCandidates.Count)];
        BossTerritoryCenter = GridHelper.ToGridXZ(bossPos);
        BossTerritoryRadius = Random.Range(BossTerritoryMin, BossTerritoryMax + 1);

        var obj = SpawnMonsterUnit(bossPos, BossLevel, true);
        if (obj != null)
        {
            BossUnit = obj.GetComponentInChildren<Status>();
            if (BossUnit != null)
                BossUnit.IsBoss = true;
        }

        Debug.Log($"[MonsterSystem] 強敵ボス スポーン Lv{BossLevel} at ({BossTerritoryCenter}) 縄張り半径{BossTerritoryRadius}");
    }

    // ================================================================
    //  魔物ユニット生成
    // ================================================================
    private GameObject SpawnMonsterUnit(Vector3 pos, int level, bool isBoss)
    {
        // フォールバック生成（プリミティブSphere）
        var obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        obj.transform.position = pos;
        obj.transform.SetParent(unitSetting.MonsterUnit);
        obj.name = isBoss ? "MonsterBoss" : "Monster";

        // ボスは大きめ
        if (isBoss)
            obj.transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);

        var renderer = obj.GetComponent<Renderer>();
        if (renderer != null)
        {
            var mat = new Material(Shader.Find("Standard"));
            mat.color = isBoss
                ? new Color(0.8f, 0.0f, 0.2f) // 赤紫（ボス）
                : BrandGuide.FallbackMonsterUnit; // 紫（雑魚）
            renderer.material = mat;
        }

        var status = obj.AddComponent<Status>();
        status.kind = isBoss ? Kind.Boss : Kind.Knight; // 雑魚はKnight型の行動パターン
        status.team = Team.Monster;
        status.type = Type.Unit;
        status.direction = Direction.S;

        // レベルに応じたステータス計算
        ApplyMonsterStats(status, level, isBoss);

        UnitHeadUI.Attach(obj);

        // 占有位置を登録
        moveGenerator.AddOccupied(new Vector3(pos.x, 0, pos.z));

        // レジストリに登録
        if (UnitRegistry.Instance != null)
            UnitRegistry.Instance.Register(status);

        return obj;
    }

    /// <summary>魔物のステータスを設定する</summary>
    private void ApplyMonsterStats(Status status, int level, bool isBoss)
    {
        if (isBoss)
        {
            // Lv50の異形の王相当
            status.HP = 500;
            status.MaxHP = 500;
            status.ATK = 45;
            status.DEF = 30;
            status.Level = BossLevel;
        }
        else
        {
            // 雑魚: Knightベースにレベルスケーリング
            float growth = 0.15f;
            int baseHP = 25;
            int baseATK = 8;
            int baseDEF = 8;

            status.HP = Mathf.RoundToInt(baseHP + baseHP * growth * (level - 1));
            status.MaxHP = status.HP;
            status.ATK = Mathf.RoundToInt(baseATK + baseATK * growth * (level - 1));
            status.DEF = Mathf.RoundToInt(baseDEF + baseDEF * growth * (level - 1));
            status.Level = level;
        }
    }

    // ================================================================
    //  ユーティリティ
    // ================================================================

    /// <summary>Player/Enemyユニットの平均レベルを計算し、魔物レベルを返す</summary>
    private int CalcMonsterLevel()
    {
        int totalLevel = 0;
        int unitCount = 0;

        if (unitSetting.PlayerUnit != null)
        {
            foreach (Transform child in unitSetting.PlayerUnit)
            {
                var s = child.GetComponentInChildren<Status>();
                if (s != null && s.HP > 0 && s.type == Type.Unit)
                {
                    totalLevel += Mathf.Max(1, s.Level);
                    unitCount++;
                }
            }
        }

        if (unitSetting.EnemyUnit != null)
        {
            foreach (Transform child in unitSetting.EnemyUnit)
            {
                var s = child.GetComponentInChildren<Status>();
                if (s != null && s.HP > 0 && s.type == Type.Unit)
                {
                    totalLevel += Mathf.Max(1, s.Level);
                    unitCount++;
                }
            }
        }

        int avgLevel = unitCount > 0 ? Mathf.Max(1, totalLevel / unitCount) : 1;
        return avgLevel;
    }

    /// <summary>生存中の魔物ユニット数を返す</summary>
    public int CountAliveMonsters()
    {
        if (unitSetting.MonsterUnit == null) return 0;
        int count = 0;
        foreach (Transform child in unitSetting.MonsterUnit)
        {
            var s = child.GetComponentInChildren<Status>();
            if (s != null && s.HP > 0)
                count++;
        }
        return count;
    }

    /// <summary>
    /// 魔物のスポーン候補位置を取得する。
    /// Player/Enemyの領土外 かつ 未占有のタイル。
    /// </summary>
    private List<Vector3> GetMonsterSpawnCandidates()
    {
        var setpos = mapCreate.SetPos;
        Vector3 pcp = crystalSystem.PCP;
        Vector3 ecp = crystalSystem.ECP;

        return setpos.Where(p =>
        {
            // 両陣営の領土外（半径5以上離れた場所）
            float distP = Mathf.Max(Mathf.Abs(p.x - pcp.x), Mathf.Abs(p.z - pcp.z));
            float distE = Mathf.Max(Mathf.Abs(p.x - ecp.x), Mathf.Abs(p.z - ecp.z));
            if (distP <= 6 || distE <= 6) return false;

            // 未占有チェック
            Vector3 cellPos = new Vector3(Mathf.RoundToInt(p.x), 0, Mathf.RoundToInt(p.z));
            if (moveGenerator.IsOccupied(cellPos)) return false;

            return true;
        }).ToList();
    }

    /// <summary>ボスが自分の縄張り内にいるかチェック</summary>
    public bool IsInBossTerritory(Vector3Int pos)
    {
        int dx = Mathf.Abs(pos.x - BossTerritoryCenter.x);
        int dz = Mathf.Abs(pos.z - BossTerritoryCenter.z);
        return Mathf.Max(dx, dz) <= BossTerritoryRadius;
    }

    /// <summary>指定ユニットの全Monster一覧を取得（MonsterAI用）</summary>
    public List<Status> GetAliveMonsters()
    {
        var result = new List<Status>();
        if (unitSetting.MonsterUnit == null) return result;

        foreach (Transform child in unitSetting.MonsterUnit)
        {
            if (child == null || !child.gameObject.activeInHierarchy) continue;
            var s = child.GetComponentInChildren<Status>();
            if (s != null && s.HP > 0)
                result.Add(s);
        }
        return result;
    }
}
