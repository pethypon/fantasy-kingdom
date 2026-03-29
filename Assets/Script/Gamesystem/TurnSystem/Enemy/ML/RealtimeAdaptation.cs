using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  RealtimeAdaptation — 試合中リアルタイム適応システム
//
//  ARC Raiders式の核心: 同じ試合内でプレイヤーの行動に適応する。
//  「さっきこうされたから、次はこう対応する」をリアルタイムで行う。
//
//  3段階の適応:
//  1. 即時反応 — 直前の脅威に対する緊急対応
//  2. パターン検知 — 同一試合内で繰り返されるパターンの検出
//  3. 戦術シフト — 試合の流れに応じた戦略の動的変更
// =====================================================================
public class RealtimeAdaptation
{
    // ================================================================
    //  即時脅威トラッカー
    // ================================================================
    struct ThreatEvent
    {
        public int Turn;
        public Vector3Int Position;
        public Kind AttackerKind;
        public float Severity;      // 0.0 (軽微) ～ 1.0 (致命的)
        public ThreatType Type;
    }

    enum ThreatType
    {
        AttackOnUnit,
        AttackOnCrystal,
        AttackOnBuilding,
        Flanking,
        Encirclement,
        SkillAttack
    }

    // ================================================================
    //  試合内パターン検知
    // ================================================================
    struct PatternDetection
    {
        public string PatternId;
        public int OccurrenceCount;
        public int LastSeenTurn;
        public float CounterPriority;  // 対策の優先度
    }

    // ---- 蓄積データ（同一試合内のみ） ----
    List<ThreatEvent> _recentThreats = new List<ThreatEvent>();
    Dictionary<string, PatternDetection> _detectedPatterns = new Dictionary<string, PatternDetection>();

    // ---- 適応状態 ----
    float _urgencyLevel = 0f;          // 0.0 (平常) ～ 1.0 (緊急)
    float _defenseUrgency = 0f;        // 防衛緊急度
    float _counterAttackWindow = 0f;   // 反撃チャンス

    // ---- ゾーンアラート ----
    Dictionary<Vector3Int, float> _hotZones = new Dictionary<Vector3Int, float>();  // 攻撃が集中する位置

    // ---- 適応修正 ----
    public struct AdaptiveModifiers
    {
        public float DefenseUrgency;         // 防衛行動ボーナス
        public float CounterAttackBonus;     // 反撃ボーナス
        public float RetreatUrgency;         // 撤退ボーナス
        public float HealPriority;           // 回復優先度
        public float WallReinforce;          // 壁補強ボーナス
        public float ScoutUrgency;           // 偵察急務
        public float ReformationBonus;       // 陣形再編ボーナス
        public float GuardCrystalBonus;      // クリスタル防衛ボーナス
        public float AvoidHotZoneBonus;      // ホットゾーン回避
        public float ExploitWeaknessBonus;   // 弱点突きボーナス
    }

    AdaptiveModifiers _currentModifiers;
    public AdaptiveModifiers CurrentModifiers => _currentModifiers;

    // ================================================================
    //  脅威の通知（プレイヤーの行動が観測されるたびに呼ぶ）
    // ================================================================

    /// <summary>プレイヤーがAI駒を攻撃した</summary>
    public void OnPlayerAttackedUnit(Status target, Status attacker, int damage, int turn)
    {
        if (target == null || attacker == null) return;

        float severity = (float)damage / Mathf.Max(1, target.MaxHP);
        bool isFlanking = target.VisionCell != null &&
            !target.VisionCell.Contains(new Vector3Int(
                Mathf.RoundToInt(attacker.transform.position.x), 0,
                Mathf.RoundToInt(attacker.transform.position.z)));

        var threat = new ThreatEvent
        {
            Turn = turn,
            Position = ToCell(target.transform.position),
            AttackerKind = attacker.kind,
            Severity = severity,
            Type = isFlanking ? ThreatType.Flanking : ThreatType.AttackOnUnit
        };
        _recentThreats.Add(threat);
        IncrementHotZone(threat.Position, severity);

        // パターン検知
        DetectPattern($"attack_{attacker.kind}_{ToCell(attacker.transform.position)}", turn);
        if (isFlanking)
            DetectPattern("flanking_attack", turn);

        // 緊急度更新
        _urgencyLevel = Mathf.Min(1f, _urgencyLevel + severity * 0.3f);
        _defenseUrgency = Mathf.Min(1f, _defenseUrgency + severity * 0.4f);

        // 撃破された場合
        if (target.HP - damage <= 0)
        {
            _urgencyLevel = Mathf.Min(1f, _urgencyLevel + 0.2f);
            DetectPattern("unit_lost", turn);
        }
    }

    /// <summary>プレイヤーがクリスタルを攻撃した</summary>
    public void OnPlayerAttackedCrystal(int damage, int maxHP, int turn)
    {
        float severity = (float)damage / Mathf.Max(1, maxHP);

        _recentThreats.Add(new ThreatEvent
        {
            Turn = turn,
            Position = Vector3Int.zero, // クリスタル位置は別途取得
            Severity = severity,
            Type = ThreatType.AttackOnCrystal
        });

        // クリスタル攻撃は最高緊急度
        _urgencyLevel = Mathf.Min(1f, _urgencyLevel + severity * 0.5f);
        _defenseUrgency = 1f;
        DetectPattern("crystal_attack", turn);
    }

    /// <summary>プレイヤーがAI建物を攻撃した</summary>
    public void OnPlayerAttackedBuilding(int turn)
    {
        _recentThreats.Add(new ThreatEvent
        {
            Turn = turn,
            Severity = 0.3f,
            Type = ThreatType.AttackOnBuilding
        });

        _urgencyLevel = Mathf.Min(1f, _urgencyLevel + 0.15f);
        DetectPattern("building_attack", turn);
    }

    /// <summary>プレイヤーがスキル攻撃を使った</summary>
    public void OnPlayerUsedSkill(Status attacker, int turn)
    {
        if (attacker == null) return;
        DetectPattern($"skill_{attacker.kind}", turn);
        _urgencyLevel = Mathf.Min(1f, _urgencyLevel + 0.1f);
    }

    /// <summary>プレイヤーが前進した（AI陣地に近づいた）</summary>
    public void OnPlayerAdvanced(float distanceClosed, int turn)
    {
        if (distanceClosed > 2f)
        {
            _urgencyLevel = Mathf.Min(1f, _urgencyLevel + 0.05f);
            DetectPattern("advance", turn);
        }
    }

    // ================================================================
    //  ターン開始時の適応計算
    //  蓄積された脅威情報から適応修正値を算出
    // ================================================================
    public void ComputeAdaptation(AIBoardState board, int currentTurn)
    {
        // 時間経過で緊急度を減衰
        _urgencyLevel *= 0.85f;
        _defenseUrgency *= 0.8f;
        DecayHotZones();

        // 古い脅威を除去（直近5ターン分のみ保持）
        _recentThreats.RemoveAll(t => currentTurn - t.Turn > 5);

        // ---- 即時脅威分析 ----
        int recentAttackCount = 0;
        int recentFlankCount = 0;
        int recentCrystalAttackCount = 0;
        float recentMaxSeverity = 0f;

        foreach (var t in _recentThreats)
        {
            if (currentTurn - t.Turn <= 2)
            {
                recentAttackCount++;
                if (t.Type == ThreatType.Flanking) recentFlankCount++;
                if (t.Type == ThreatType.AttackOnCrystal) recentCrystalAttackCount++;
                if (t.Severity > recentMaxSeverity) recentMaxSeverity = t.Severity;
            }
        }

        // ---- パターンベース分析 ----
        bool repeatedFlanking = IsPatternRepeating("flanking_attack", 2);
        bool repeatedCrystalAttack = IsPatternRepeating("crystal_attack", 2);
        bool unitLossStreak = IsPatternRepeating("unit_lost", 3);
        bool repeatedAdvance = IsPatternRepeating("advance", 3);

        // ---- 反撃チャンス検知 ----
        // プレイヤーの駒が分散している or 攻撃後の隙がある
        _counterAttackWindow = 0f;
        if (board.AlivePlayerUnits.Count > 0)
        {
            float avgDistance = 0f;
            int count = 0;
            foreach (var pu in board.AlivePlayerUnits)
            {
                if (pu == null || !pu.gameObject.activeInHierarchy) continue;
                foreach (var pu2 in board.AlivePlayerUnits)
                {
                    if (pu2 == null || pu2 == pu || !pu2.gameObject.activeInHierarchy) continue;
                    avgDistance += Vector3.Distance(pu.transform.position, pu2.transform.position);
                    count++;
                }
            }
            if (count > 0)
            {
                avgDistance /= count;
                // プレイヤー駒が分散しているほど反撃チャンス
                _counterAttackWindow = Mathf.Clamp01((avgDistance - 3f) / 10f);
            }
        }

        // ---- 適応修正値の計算 ----
        _currentModifiers = new AdaptiveModifiers
        {
            DefenseUrgency = _defenseUrgency * 20f,
            CounterAttackBonus = _counterAttackWindow * 15f,
            RetreatUrgency = unitLossStreak ? 15f : _urgencyLevel * 8f,
            HealPriority = recentMaxSeverity > 0.5f ? 12f : 0f,
            WallReinforce = repeatedCrystalAttack ? 20f : 0f,
            ScoutUrgency = recentFlankCount > 0 ? 15f : 0f,
            ReformationBonus = unitLossStreak ? 12f : 0f,
            GuardCrystalBonus = recentCrystalAttackCount > 0 ? 25f : _defenseUrgency * 10f,
            AvoidHotZoneBonus = _hotZones.Count > 0 ? 8f : 0f,
            ExploitWeaknessBonus = _counterAttackWindow > 0.5f ? 15f : 0f,
        };

        if (_urgencyLevel > 0.1f || _counterAttackWindow > 0.1f)
        {
            Debug.Log($"[RealtimeAdapt] 緊急度={_urgencyLevel:F2}  " +
                      $"防衛={_defenseUrgency:F2}  反撃窓={_counterAttackWindow:F2}  " +
                      $"直近脅威={recentAttackCount}  " +
                      $"パターン: 側面={repeatedFlanking} クリ攻={repeatedCrystalAttack} " +
                      $"損失={unitLossStreak}");
        }
    }

    // ================================================================
    //  行動への適用
    // ================================================================
    public float GetAdaptiveBonus(AIAction action, AIBoardState board)
    {
        float bonus = 0f;
        var m = _currentModifiers;

        switch (action.ActionType)
        {
            case AIActionType.Attack:
                bonus += m.CounterAttackBonus;
                bonus += m.ExploitWeaknessBonus;
                // 緊急時は攻撃を控える
                if (_urgencyLevel > 0.7f) bonus -= 10f;
                break;

            case AIActionType.SkillUse:
                if (action.Skill != null && action.Skill.FixedHeal > 0)
                    bonus += m.HealPriority;
                if (action.Skill != null && action.Skill.GrantBuff == BuffType.Defensive)
                    bonus += m.DefenseUrgency * 0.5f;
                bonus += m.CounterAttackBonus * 0.5f;
                break;

            case AIActionType.Retreat:
                bonus += m.RetreatUrgency;
                bonus += m.DefenseUrgency * 0.3f;
                break;

            case AIActionType.DefenseRepos:
                bonus += m.DefenseUrgency;
                bonus += m.ReformationBonus;
                // クリスタル周辺への配置
                if (action.Unit != null)
                {
                    float dist = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
                    if (dist < 4f) bonus += m.GuardCrystalBonus;
                }
                break;

            case AIActionType.Support:
                bonus += m.ReformationBonus;
                bonus += m.DefenseUrgency * 0.4f;
                break;

            case AIActionType.Move:
                // ホットゾーン回避
                var targetCell = ToCell(action.TargetPos);
                if (_hotZones.ContainsKey(targetCell))
                    bonus -= m.AvoidHotZoneBonus * _hotZones[targetCell];

                // 反撃チャンス時は前進を促す
                if (_counterAttackWindow > 0.5f && action.Unit != null)
                {
                    float curDist = Vector3.Distance(action.Unit.transform.position, board.PlayerCrystalPos);
                    float newDist = Vector3.Distance(action.TargetPos, board.PlayerCrystalPos);
                    if (newDist < curDist)
                        bonus += m.ExploitWeaknessBonus * 0.5f;
                }
                break;

            case AIActionType.Build:
                if (FacilityData.IsWall(action.Facility))
                    bonus += m.WallReinforce;
                break;

            case AIActionType.Surround:
                bonus += m.ExploitWeaknessBonus;
                bonus += m.CounterAttackBonus * 0.7f;
                break;
        }

        // Scout系ユニットの偵察ボーナス
        if (action.Unit != null && action.Unit.kind == Kind.Scout &&
            action.ActionType == AIActionType.Move)
        {
            bonus += m.ScoutUrgency;
        }

        return bonus;
    }

    // ================================================================
    //  試合終了リセット
    // ================================================================
    public void Reset()
    {
        _recentThreats.Clear();
        _detectedPatterns.Clear();
        _hotZones.Clear();
        _urgencyLevel = 0f;
        _defenseUrgency = 0f;
        _counterAttackWindow = 0f;
        _currentModifiers = new AdaptiveModifiers();
    }

    // ================================================================
    //  内部ヘルパー
    // ================================================================

    void DetectPattern(string patternId, int turn)
    {
        if (_detectedPatterns.TryGetValue(patternId, out var p))
        {
            p.OccurrenceCount++;
            p.LastSeenTurn = turn;
            p.CounterPriority = Mathf.Min(1f, p.OccurrenceCount * 0.25f);
            _detectedPatterns[patternId] = p;
        }
        else
        {
            _detectedPatterns[patternId] = new PatternDetection
            {
                PatternId = patternId,
                OccurrenceCount = 1,
                LastSeenTurn = turn,
                CounterPriority = 0.25f
            };
        }
    }

    bool IsPatternRepeating(string patternId, int minCount)
    {
        return _detectedPatterns.TryGetValue(patternId, out var p) &&
               p.OccurrenceCount >= minCount;
    }

    void IncrementHotZone(Vector3Int cell, float severity)
    {
        if (_hotZones.ContainsKey(cell))
            _hotZones[cell] = Mathf.Min(3f, _hotZones[cell] + severity);
        else
            _hotZones[cell] = severity;
    }

    void DecayHotZones()
    {
        var toRemove = new List<Vector3Int>();
        var keys = new List<Vector3Int>(_hotZones.Keys);
        foreach (var k in keys)
        {
            _hotZones[k] *= 0.7f;
            if (_hotZones[k] < 0.05f) toRemove.Add(k);
        }
        foreach (var k in toRemove) _hotZones.Remove(k);
    }

    static Vector3Int ToCell(Vector3 v)
        => new Vector3Int(Mathf.RoundToInt(v.x), 0, Mathf.RoundToInt(v.z));
}
