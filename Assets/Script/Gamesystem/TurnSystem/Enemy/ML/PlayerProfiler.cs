using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// =====================================================================
//  PlayerProfiler — プレイヤー戦術プロファイリング（ARC Raiders式学習の核心）
//
//  プレイヤーの全行動を観測・記録し、以下を統計的に分析:
//  ・攻撃パターン（どこから、どの駒で、誰を狙うか）
//  ・防衛パターン（クリスタル周辺の配置傾向）
//  ・経済パターン（建設タイミング、優先施設）
//  ・移動パターン（進軍ルート、陣形の好み）
//  ・タイミングパターン（何ターン目に攻めるか）
//
//  出力: PlayerProfile（プレイヤーの癖を数値化した構造体）
//  永続化: 試合跨ぎで蓄積。戦えば戦うほど精度が上がる。
// =====================================================================
public class PlayerProfiler
{
    // ================================================================
    //  プレイヤープロファイル（統計から導出される「癖」の数値化）
    // ================================================================
    public class PlayerProfile
    {
        // ---- プレイスタイル分類 (0.0〜1.0) ----
        public float AggressionScore;    // 攻撃的か防御的か
        public float RushTendency;       // 序盤ラッシュ傾向
        public float TurtleTendency;     // 亀型（防衛固め）傾向
        public float FlankPreference;    // 側面攻撃の好み
        public float EconomyFocus;       // 経済重視度
        public float SkillReliance;      // スキル依存度

        // ---- 攻撃傾向 ----
        public float PreferredAttackRange;       // 好む攻撃距離
        public float TargetWeakestTendency;      // 弱い駒を狙う傾向
        public float TargetHighValueTendency;    // 高価値駒を狙う傾向
        public float CrystalFocusTendency;       // クリスタル直行傾向

        // ---- 時間軸パターン ----
        public float AverageAttackStartTurn;     // 平均攻撃開始ターン
        public float AverageArmySizeAtAttack;    // 攻撃時の平均軍団サイズ
        public float TurnPressurePattern;        // ターン経過による圧力変化率

        // ---- 空間パターン ----
        public Vector3 PreferredAttackDirection;       // 好む攻撃方向
        public float FormationSpread;                  // 陣形の広がり（密集 vs 分散）
        public Dictionary<Kind, float> UnitPreference; // 駒種別の使用頻度

        // ---- 信頼度 ----
        public int TotalObservations;    // 総観測回数
        public int MatchesObserved;      // 観測試合数
        public float Confidence => Mathf.Clamp01(TotalObservations / 200f);

        public PlayerProfile()
        {
            UnitPreference = new Dictionary<Kind, float>();
            PreferredAttackDirection = Vector3.forward;
        }
    }

    // ================================================================
    //  観測記録データ
    // ================================================================

    // 攻撃記録
    struct AttackRecord
    {
        public float Distance;
        public bool WasFlank;       // 側面/背後からの攻撃か
        public bool TargetWasWeak;  // 弱った駒を狙ったか
        public float TargetValueRatio; // ターゲットの価値比率
    }

    // 移動記録
    struct MoveRecord
    {
        public bool IsAdvance;          // 前進したか
    }

    // ターン終了時のスナップショット
    struct TurnSnapshot
    {
        public float FormationSpread;
        public bool DidAttack;
        public bool DidBuild;
    }

    // ---- 蓄積データ ----
    List<AttackRecord> _attackHistory = new List<AttackRecord>();
    List<MoveRecord> _moveHistory = new List<MoveRecord>();
    List<TurnSnapshot> _turnSnapshots = new List<TurnSnapshot>();

    // Sufficient statistics preserve the full existing history; profile updates no longer scan it.
    int flankAttacks, weakAttacks, advancingMoves, attackingTurns, buildingTurns, earlyAttackingTurns;
    float attackDistanceSum, targetValueSum, formationSpreadSum;

    // ---- 試合中カウンター ----
    int _currentTurnAttacks;
    int _currentTurnDamage;
    int _firstAttackTurn = -1;
    Dictionary<Kind, int> _unitUsageCount = new Dictionary<Kind, int>();

    // ---- 累積統計（試合跨ぎ） ----
    int _totalMatches;
    int _totalAttackRecords;
    int _totalMoveRecords;
    float _avgFirstAttackTurn;
    float _avgRushArmySize;

    // ---- 方向統計 ----
    Dictionary<Vector3Int, int> _attackDirectionFreq = new Dictionary<Vector3Int, int>();
    Dictionary<Vector3Int, int> _playerDefenseHeatmap = new Dictionary<Vector3Int, int>();
    Dictionary<Vector3Int, int> _playerAttackHeatmap = new Dictionary<Vector3Int, int>();

    // ---- 出力 ----
    PlayerProfile _profile = new PlayerProfile();
    public PlayerProfile Profile => _profile;

    // ================================================================
    //  初期化
    // ================================================================
    public PlayerProfiler()
    {
        _profile = new PlayerProfile();
    }

    // ================================================================
    //  観測 — プレイヤーの各行動をAIが盗み見る
    // ================================================================

    /// <summary>プレイヤーの攻撃を観測</summary>
    public void ObserveAttack(Status attacker, Status target, Vector3 enemyCrystalPos, int turn)
    {
        if (attacker == null || target == null) return;

        var aPos = ToCell(attacker.transform.position);
        var tPos = ToCell(target.transform.position);
        float dist = Vector3.Distance(attacker.transform.position, target.transform.position);

        // 側面攻撃判定（ターゲットの視界外からの攻撃）
        bool flank = target.VisionCell != null && !target.VisionCell.Contains(aPos);

        // 弱い駒を狙ったか
        bool targetWeak = target.MaxHP > 0 && (float)target.HP / target.MaxHP < 0.5f;

        // ターゲットの価値比率
        float targetValue = AIBoardEvaluator.GetPieceValuePublic(target);

        _attackHistory.Add(new AttackRecord
        {
            Distance = dist,
            WasFlank = flank,
            TargetWasWeak = targetWeak,
            TargetValueRatio = targetValue / 100f
        });

        if (flank) flankAttacks++;
        if (targetWeak) weakAttacks++;
        attackDistanceSum += dist; targetValueSum += targetValue / 100f;

        // 攻撃方向の頻度記録
        var direction = new Vector3Int(
            Mathf.Clamp(tPos.x - aPos.x, -1, 1), 0,
            Mathf.Clamp(tPos.z - aPos.z, -1, 1));
        IncrementDict(_attackDirectionFreq, direction);

        // 攻撃位置ヒートマップ
        IncrementDict(_playerAttackHeatmap, tPos);

        // 初回攻撃ターン記録
        if (_firstAttackTurn < 0) _firstAttackTurn = turn;

        _currentTurnAttacks++;
        _totalAttackRecords++;

        IncrementDict(_unitUsageCount, attacker.kind);
    }

    /// <summary>プレイヤーの移動を観測</summary>
    public void ObserveMove(Status unit, Vector3 from, Vector3 to, Vector3 enemyCrystalPos, int turn)
    {
        if (unit == null) return;

        float distBefore = Vector3.Distance(from, enemyCrystalPos);
        float distAfter = Vector3.Distance(to, enemyCrystalPos);

        _moveHistory.Add(new MoveRecord
        {
            IsAdvance = distAfter < distBefore
        });

        if (distAfter < distBefore) advancingMoves++;
        _totalMoveRecords++;
        IncrementDict(_unitUsageCount, unit.kind);
    }

    /// <summary>プレイヤーの建築を観測</summary>
    public void ObserveBuild(int turn)
    {
        // ターンスナップショットで記録
    }

    /// <summary>プレイヤーの駒配置を観測（ターン開始時に全駒の位置を記録）</summary>
    public void ObserveFormation(List<Status> playerUnits, Vector3 enemyCrystalPos, int turn)
    {
        if (playerUnits == null || playerUnits.Count == 0) return;

        // 各駒の位置を防衛ヒートマップに記録
        foreach (var u in playerUnits)
        {
            if (u == null || !u.gameObject.activeInHierarchy) continue;
            IncrementDict(_playerDefenseHeatmap, ToCell(u.transform.position));
        }

        // 陣形の広がりを計算
        Vector3 centroid = Vector3.zero;
        int count = 0;
        foreach (var u in playerUnits)
        {
            if (u == null || !u.gameObject.activeInHierarchy) continue;
            centroid += u.transform.position;
            count++;
        }
        if (count == 0) return;
        centroid /= count;

        float spread = 0f;
        float avgDist = 0f;
        foreach (var u in playerUnits)
        {
            if (u == null || !u.gameObject.activeInHierarchy) continue;
            spread += Vector3.Distance(u.transform.position, centroid);
            avgDist += Vector3.Distance(u.transform.position, enemyCrystalPos);
        }
        spread /= count;
        avgDist /= count;

        _turnSnapshots.Add(new TurnSnapshot
        {
            FormationSpread = spread,
            DidAttack = _currentTurnAttacks > 0,
            DidBuild = false,
        });

        if (_currentTurnAttacks > 0) attackingTurns++;
        formationSpreadSum += spread;
        int newMidpoint = _turnSnapshots.Count / 2;
        if (_turnSnapshots.Count % 2 == 0 && _turnSnapshots[newMidpoint - 1].DidAttack) earlyAttackingTurns++;

        // ターンカウンターリセット
        _currentTurnAttacks = 0;
        _currentTurnDamage = 0;
    }

    // ================================================================
    //  プロファイル更新 — 蓄積データから統計的にプロファイルを再計算
    //  試合終了時、またはN ターンごとに呼ぶ
    // ================================================================
    public void UpdateProfile()
    {
        int totalObs = _attackHistory.Count + _moveHistory.Count + _turnSnapshots.Count;
        _profile.TotalObservations = totalObs;
        _profile.MatchesObserved = _totalMatches;

        if (totalObs < 5) return; // データ不足

        // ---- 攻撃性スコア ----
        if (_turnSnapshots.Count > 0)
        {
            _profile.AggressionScore = (float)attackingTurns / _turnSnapshots.Count;
        }

        // ---- ラッシュ傾向 ----
        if (_firstAttackTurn > 0)
        {
            // 10ターン以内に攻撃開始 → ラッシュ傾向高
            _profile.RushTendency = Mathf.Clamp01(1f - (_firstAttackTurn - 1f) / 15f);
        }

        // ---- 亀傾向 ----
        if (_turnSnapshots.Count > 0)
        {
            float avgSpread = formationSpreadSum / _turnSnapshots.Count;
            // 密集 + 攻撃少 = 亀
            _profile.TurtleTendency = Mathf.Clamp01((1f - _profile.AggressionScore) *
                Mathf.Clamp01(1f - avgSpread / 8f));
        }

        // ---- 側面攻撃の好み ----
        if (_attackHistory.Count > 0)
        {
            _profile.FlankPreference = (float)flankAttacks / _attackHistory.Count;
        }

        // ---- 好む攻撃距離 ----
        if (_attackHistory.Count > 0)
        {
            _profile.PreferredAttackRange = attackDistanceSum / _attackHistory.Count;
        }

        // ---- 弱い駒を狙う傾向 ----
        if (_attackHistory.Count > 0)
        {
            _profile.TargetWeakestTendency = (float)weakAttacks / _attackHistory.Count;
        }

        // ---- 高価値ターゲット傾向 ----
        if (_attackHistory.Count > 0)
        {
            _profile.TargetHighValueTendency = targetValueSum / _attackHistory.Count;
        }

        // ---- クリスタル直行傾向 ----
        if (_moveHistory.Count > 0)
        {
            _profile.CrystalFocusTendency = (float)advancingMoves / _moveHistory.Count;
        }

        // ---- 平均攻撃開始ターン ----
        _profile.AverageAttackStartTurn = _firstAttackTurn > 0 ? _firstAttackTurn : 15f;

        // ---- 陣形の広がり ----
        if (_turnSnapshots.Count > 0)
        {
            _profile.FormationSpread = formationSpreadSum / _turnSnapshots.Count;
        }

        // ---- 好む攻撃方向 ----
        if (_attackDirectionFreq.Count > 0)
        {
            Vector3Int bestDir = Vector3Int.zero;
            int bestCount = 0;
            foreach (var kvp in _attackDirectionFreq)
            {
                if (kvp.Value > bestCount)
                {
                    bestCount = kvp.Value;
                    bestDir = kvp.Key;
                }
            }
            _profile.PreferredAttackDirection = new Vector3(bestDir.x, 0, bestDir.z);
        }

        // ---- スキル依存度 ----
        // （スキル使用の観測は別途拡張可能）
        _profile.SkillReliance = 0.3f; // デフォルト

        // ---- 駒種別使用頻度 ----
        int totalUsage = 0;
        foreach (var kvp in _unitUsageCount) totalUsage += kvp.Value;
        if (totalUsage > 0)
        {
            _profile.UnitPreference.Clear();
            foreach (var kvp in _unitUsageCount)
                _profile.UnitPreference[kvp.Key] = (float)kvp.Value / totalUsage;
        }

        // ---- 経済重視度 ----
        if (_turnSnapshots.Count > 0)
        {
            _profile.EconomyFocus = (float)buildingTurns / _turnSnapshots.Count;
        }

        // ---- ターン圧力パターン ----
        if (_turnSnapshots.Count >= 5)
        {
            int midpoint = _turnSnapshots.Count / 2;
            float earlyRate = (float)earlyAttackingTurns / midpoint;
            float lateRate = (float)(attackingTurns - earlyAttackingTurns) / (_turnSnapshots.Count - midpoint);
            _profile.TurnPressurePattern = lateRate - earlyRate;
        }

        { if (DevelopmentLog.IsEnabled(AILogLevel.Verbose)) DevelopmentLog.Log($"[PlayerProfiler] プロファイル更新: " +
                  $"攻撃性={_profile.AggressionScore:F2} " +
                  $"ラッシュ={_profile.RushTendency:F2} " +
                  $"亀={_profile.TurtleTendency:F2} " +
                  $"側面={_profile.FlankPreference:F2} " +
                  $"クリスタル直行={_profile.CrystalFocusTendency:F2} " +
                  $"信頼度={_profile.Confidence:F2} " +
                  $"観測数={totalObs}"); }
    }

    // ================================================================
    //  試合終了処理
    // ================================================================
    public void OnMatchEnd()
    {
        _totalMatches++;
        UpdateProfile();

        // 試合中データをリセット（プロファイルは保持、生データはクリア）
        // 古い試合のデータが新しい試合のプロファイリングを汚染するのを防止
        _firstAttackTurn = -1;
        _currentTurnAttacks = 0;
        _currentTurnDamage = 0;

        // 生データを半分だけ保持（最新の半分のみ残す）
        // これにより長期傾向を維持しつつ、プレイヤーのスタイル変化に追従
        if (_attackHistory.Count > 50)
            _attackHistory.RemoveRange(0, _attackHistory.Count / 2);
        if (_moveHistory.Count > 50)
            _moveHistory.RemoveRange(0, _moveHistory.Count / 2);
        if (_turnSnapshots.Count > 30)
            _turnSnapshots.RemoveRange(0, _turnSnapshots.Count / 2);
        RebuildHistoryStatistics();
    }

    void RebuildHistoryStatistics()
    {
        flankAttacks = weakAttacks = advancingMoves = attackingTurns = buildingTurns = earlyAttackingTurns = 0;
        attackDistanceSum = targetValueSum = formationSpreadSum = 0;
        foreach (var attack in _attackHistory)
        {
            if (attack.WasFlank) flankAttacks++;
            if (attack.TargetWasWeak) weakAttacks++;
            attackDistanceSum += attack.Distance; targetValueSum += attack.TargetValueRatio;
        }
        foreach (var move in _moveHistory) if (move.IsAdvance) advancingMoves++;
        int midpoint = _turnSnapshots.Count / 2;
        for (int i = 0; i < _turnSnapshots.Count; i++)
        {
            var snapshot = _turnSnapshots[i];
            if (snapshot.DidAttack) { attackingTurns++; if (i < midpoint) earlyAttackingTurns++; }
            if (snapshot.DidBuild) buildingTurns++;
            formationSpreadSum += snapshot.FormationSpread;
        }
    }

    // ================================================================
    //  ヒートマップ出力（カウンター戦略エンジンが使用）
    // ================================================================

    /// <summary>プレイヤーが頻繁に攻撃する位置のヒートマップ</summary>
    public Dictionary<Vector3Int, int> GetAttackHeatmap() => _playerAttackHeatmap;

    /// <summary>プレイヤーが頻繁に防衛する位置のヒートマップ</summary>
    public Dictionary<Vector3Int, int> GetDefenseHeatmap() => _playerDefenseHeatmap;

    /// <summary>プレイヤーの好む攻撃方向の頻度</summary>
    public Dictionary<Vector3Int, int> GetAttackDirectionFreq() => _attackDirectionFreq;

    // ================================================================
    //  プロファイルの特徴ベクトル化（MLBrainへの入力用）
    // ================================================================
    // キャッシュバッファ（GCゼロ化）
    readonly float[] _featureVectorCache = new float[16];

    public float[] ToFeatureVector()
    {
        _featureVectorCache[0]  = _profile.AggressionScore * 2f - 1f;
        _featureVectorCache[1]  = _profile.RushTendency * 2f - 1f;
        _featureVectorCache[2]  = _profile.TurtleTendency * 2f - 1f;
        _featureVectorCache[3]  = _profile.FlankPreference * 2f - 1f;
        _featureVectorCache[4]  = _profile.EconomyFocus * 2f - 1f;
        _featureVectorCache[5]  = _profile.SkillReliance * 2f - 1f;
        _featureVectorCache[6]  = Mathf.Clamp(_profile.PreferredAttackRange / 10f * 2f - 1f, -1f, 1f);
        _featureVectorCache[7]  = _profile.TargetWeakestTendency * 2f - 1f;
        _featureVectorCache[8]  = _profile.TargetHighValueTendency * 2f - 1f;
        _featureVectorCache[9]  = _profile.CrystalFocusTendency * 2f - 1f;
        _featureVectorCache[10] = Mathf.Clamp(_profile.AverageAttackStartTurn / 30f * 2f - 1f, -1f, 1f);
        _featureVectorCache[11] = _profile.TurnPressurePattern;
        _featureVectorCache[12] = _profile.FormationSpread / 10f * 2f - 1f;
        _featureVectorCache[13] = _profile.Confidence * 2f - 1f;
        _featureVectorCache[14] = _profile.PreferredAttackDirection.x;
        _featureVectorCache[15] = _profile.PreferredAttackDirection.z;
        return _featureVectorCache;
    }

    // ================================================================
    //  セーブ/ロード用
    // ================================================================
    public int SaveTotalMatches { get => _totalMatches; set => _totalMatches = value; }
    public float SaveAvgFirstAttackTurn { get => _avgFirstAttackTurn; set => _avgFirstAttackTurn = value; }
    public PlayerProfile GetProfileRef() => _profile;

    public void RestoreProfile(PlayerProfile p)
    {
        if (p != null) _profile = p;
    }

    // ================================================================
    //  ユーティリティ
    // ================================================================
    static Vector3Int ToCell(Vector3 v) => GridHelper.ToGridXZ(v);

    static void IncrementDict<TKey>(Dictionary<TKey, int> dict, TKey key)
    {
        dict[key] = dict.TryGetValue(key, out int count) ? count + 1 : 1;
    }
}
