using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  CounterStrategyEngine — プレイヤープロファイルに対するカウンター戦略
//
//  ARC Raiders式の核心: プレイヤーの癖を見抜いてカウンターする。
//  PlayerProfiler が分析した傾向に対して最適な対策を導出し、
//  行動評価スコアに反映する。
//
//  カウンター原理:
//  ・ラッシュプレイヤー → 序盤防衛強化、壁建設、遅延型経済
//  ・亀プレイヤー → 経済で上回る、側面奇襲、長期包囲
//  ・側面攻撃好き → 視界カバー強化、背面防衛
//  ・クリスタル直行 → クリスタル周囲要塞化、迎撃配置
//  ・経済重視 → 速攻で経済圧殺
//  ・弱い駒狙い → 弱駒を囮にして待ち伏せ
// =====================================================================
public class CounterStrategyEngine
{
    // ================================================================
    //  カウンター方針
    // ================================================================
    public enum CounterPlan
    {
        None,                  // プロファイル未確定
        AntiRush,              // 対ラッシュ: 序盤防衛重視
        AntiTurtle,            // 対亀: 経済競争 + 奇襲
        AntiFlank,             // 対側面: 視界・背面カバー
        AntiCrystalRush,       // 対クリスタル直行: 要塞化
        AntiEconomy,           // 対経済: 速攻で圧殺
        AntiBait,              // 対囮: 罠回避 + 全体把握
        Adaptive               // 複合: 複数の対策を混合
    }

    // ================================================================
    //  カウンタースコア修正テーブル
    // ================================================================
    public struct CounterModifiers
    {
        public float AttackBonus;       // 攻撃行動へのボーナス
        public float DefenseBonus;      // 防衛行動へのボーナス
        public float BuildBonus;        // 建築行動へのボーナス
        public float RetreatBonus;      // 撤退行動へのボーナス
        public float SurroundBonus;     // 包囲行動へのボーナス
        public float SummonBonus;       // 召喚行動へのボーナス
        public float WallBuildBonus;    // 壁建築へのボーナス
        public float ScoutBonus;        // 偵察行動へのボーナス
        public float SkillBonus;        // スキル使用へのボーナス

        // 位置関連修正
        public float CrystalGuardBonus;   // クリスタル周辺配置ボーナス
        public float FlankCoverBonus;     // 側面カバー位置ボーナス
        public float AggressiveAdvance;   // 積極的前進ボーナス

        // 駒種別の優先度修正
        public float PreferGuardian;      // Guardian優先度
        public float PreferScout;         // Scout優先度
        public float PreferAssassin;      // Assassin優先度（奇襲用）

        public override string ToString()
            => $"ATK{AttackBonus:+0.0;-0.0} DEF{DefenseBonus:+0.0;-0.0} " +
               $"BLD{BuildBonus:+0.0;-0.0} SUR{SurroundBonus:+0.0;-0.0} " +
               $"CG{CrystalGuardBonus:+0.0;-0.0} ADV{AggressiveAdvance:+0.0;-0.0}";
    }

    // ---- 状態 ----
    CounterPlan _currentPlan = CounterPlan.None;
    CounterModifiers _modifiers;
    float _planConfidence = 0f;

    // ---- マルチプラン・ブレンド ----
    Dictionary<CounterPlan, float> _planScores = new Dictionary<CounterPlan, float>();
    Dictionary<CounterPlan, float> _planEffectiveness = new Dictionary<CounterPlan, float>();
    CounterPlan _secondaryPlan = CounterPlan.None;
    float _blendRatio = 0f;  // 0=プライマリのみ, 1=50/50ブレンド

    // ---- 危険位置の予測 ----
    HashSet<Vector3Int> _predictedDangerZones = new HashSet<Vector3Int>();
    HashSet<Vector3Int> _predictedSafeZones = new HashSet<Vector3Int>();

    // ---- アクセサ ----
    public CounterPlan CurrentPlan => _currentPlan;
    public CounterPlan SecondaryPlan => _secondaryPlan;
    public CounterModifiers Modifiers => _modifiers;
    public float PlanConfidence => _planConfidence;
    public HashSet<Vector3Int> PredictedDangerZones => _predictedDangerZones;

    // ================================================================
    //  プロファイルからカウンター方針を決定
    //  各ターン開始時に呼ばれる
    // ================================================================
    public void DecideCounterPlan(PlayerProfiler.PlayerProfile profile, AIBoardState board, int turn)
    {
        if (profile == null || profile.Confidence < 0.1f)
        {
            _currentPlan = CounterPlan.None;
            _secondaryPlan = CounterPlan.None;
            _modifiers = new CounterModifiers();
            _planConfidence = 0f;
            return;
        }

        _planConfidence = profile.Confidence;

        // ---- 全プランのスコアを算出（マルチプラン・ブレンド） ----
        _planScores.Clear();
        _planScores[CounterPlan.AntiRush] = profile.RushTendency;
        _planScores[CounterPlan.AntiTurtle] = profile.TurtleTendency;
        _planScores[CounterPlan.AntiFlank] = profile.FlankPreference;
        _planScores[CounterPlan.AntiCrystalRush] = profile.CrystalFocusTendency;
        _planScores[CounterPlan.AntiEconomy] = profile.EconomyFocus;
        _planScores[CounterPlan.AntiBait] = profile.TargetWeakestTendency;
        _planScores[CounterPlan.Adaptive] = 0.25f; // ベースラインスコア

        // 有効性履歴があれば反映（過去に効いたプランを優遇）
        foreach (var kvp in _planEffectiveness)
        {
            if (_planScores.ContainsKey(kvp.Key))
                _planScores[kvp.Key] = _planScores[kvp.Key] * 0.7f + kvp.Value * 0.3f;
        }

        // ---- 上位2プランを選択してブレンド ----
        CounterPlan bestPlan = CounterPlan.Adaptive;
        CounterPlan secondPlan = CounterPlan.None;
        float bestScore = 0f;
        float secondScore = 0f;

        foreach (var kvp in _planScores)
        {
            if (kvp.Value > bestScore)
            {
                secondScore = bestScore;
                secondPlan = bestPlan;
                bestScore = kvp.Value;
                bestPlan = kvp.Key;
            }
            else if (kvp.Value > secondScore)
            {
                secondScore = kvp.Value;
                secondPlan = kvp.Key;
            }
        }

        // 閾値チェック: 最低0.3以上のスコアが必要
        if (bestScore < 0.3f)
            bestPlan = CounterPlan.Adaptive;

        _currentPlan = bestPlan;
        _secondaryPlan = secondPlan;

        // ブレンド比率: 2位が1位に近いほどブレンドを強める
        _blendRatio = (bestScore > 0.01f) ? Mathf.Clamp01(secondScore / bestScore) * 0.4f : 0f;

        // プライマリ+セカンダリの修正値をブレンド
        var primaryMod = BuildModifiers(bestPlan, profile, board, turn);
        if (_blendRatio > 0.05f && secondPlan != CounterPlan.None)
        {
            var secondaryMod = BuildModifiers(secondPlan, profile, board, turn);
            _modifiers = BlendModifiers(primaryMod, secondaryMod, _blendRatio);
        }
        else
        {
            _modifiers = primaryMod;
        }

        // 危険ゾーン予測
        PredictDangerZones(profile, board);

        Debug.Log($"[CounterStrategy] 方針={bestPlan}({bestScore:F2})+{secondPlan}({secondScore:F2})  " +
                  $"ブレンド={_blendRatio:F2}  信頼度={_planConfidence:F2}  修正={_modifiers}");
    }

    /// <summary>プラン有効性を記録（試合終了時にMLIntegrationから呼ぶ）</summary>
    public void RecordPlanEffectiveness(bool aiWon)
    {
        float delta = aiWon ? 0.1f : -0.1f;
        if (_planEffectiveness.ContainsKey(_currentPlan))
            _planEffectiveness[_currentPlan] = Mathf.Clamp01(_planEffectiveness[_currentPlan] + delta);
        else
            _planEffectiveness[_currentPlan] = Mathf.Clamp01(0.5f + delta);
    }

    /// <summary>2つの修正値をブレンド</summary>
    CounterModifiers BlendModifiers(CounterModifiers a, CounterModifiers b, float ratio)
    {
        float r1 = 1f - ratio;
        float r2 = ratio;
        return new CounterModifiers
        {
            AttackBonus = a.AttackBonus * r1 + b.AttackBonus * r2,
            DefenseBonus = a.DefenseBonus * r1 + b.DefenseBonus * r2,
            BuildBonus = a.BuildBonus * r1 + b.BuildBonus * r2,
            RetreatBonus = a.RetreatBonus * r1 + b.RetreatBonus * r2,
            SurroundBonus = a.SurroundBonus * r1 + b.SurroundBonus * r2,
            SummonBonus = a.SummonBonus * r1 + b.SummonBonus * r2,
            WallBuildBonus = a.WallBuildBonus * r1 + b.WallBuildBonus * r2,
            ScoutBonus = a.ScoutBonus * r1 + b.ScoutBonus * r2,
            SkillBonus = a.SkillBonus * r1 + b.SkillBonus * r2,
            CrystalGuardBonus = a.CrystalGuardBonus * r1 + b.CrystalGuardBonus * r2,
            FlankCoverBonus = a.FlankCoverBonus * r1 + b.FlankCoverBonus * r2,
            AggressiveAdvance = a.AggressiveAdvance * r1 + b.AggressiveAdvance * r2,
            PreferGuardian = a.PreferGuardian * r1 + b.PreferGuardian * r2,
            PreferScout = a.PreferScout * r1 + b.PreferScout * r2,
            PreferAssassin = a.PreferAssassin * r1 + b.PreferAssassin * r2,
        };
    }

    // ================================================================
    //  カウンター修正値の構築
    // ================================================================
    CounterModifiers BuildModifiers(CounterPlan plan, PlayerProfiler.PlayerProfile profile,
        AIBoardState board, int turn)
    {
        var m = new CounterModifiers();
        float c = _planConfidence;  // 信頼度でスケーリング

        switch (plan)
        {
            case CounterPlan.AntiRush:
                // 序盤防衛を強化、壁を建てる、前線を下げる
                m.DefenseBonus = 20f * c;
                m.WallBuildBonus = 25f * c;
                m.RetreatBonus = 10f * c;
                m.CrystalGuardBonus = 15f * c;
                m.AttackBonus = -5f * c;      // 攻撃を控える
                m.AggressiveAdvance = -10f * c; // 無理に前に出ない
                m.PreferGuardian = 15f * c;    // Guardianを優先召喚
                // ラッシュ開始予測ターンに合わせて防衛準備
                if (turn < profile.AverageAttackStartTurn)
                {
                    m.BuildBonus = 15f * c;     // 攻められる前に建てる
                    m.SummonBonus = 10f * c;    // 兵を揃える
                }
                break;

            case CounterPlan.AntiTurtle:
                // 経済で勝る + 奇襲部隊を用意
                m.BuildBonus = 15f * c;
                m.AttackBonus = -5f * c;      // 無理攻めしない
                m.SurroundBonus = 20f * c;     // 包囲で崩す
                m.PreferAssassin = 20f * c;    // 奇襲ユニット優先
                m.AggressiveAdvance = 5f * c;
                m.ScoutBonus = 15f * c;        // 偵察で弱点を探す
                // ターン経過で徐々に攻撃的に
                if (turn > 20)
                {
                    m.AttackBonus = 15f * c;
                    m.SurroundBonus = 25f * c;
                }
                break;

            case CounterPlan.AntiFlank:
                // 視界を広げ、側面をカバー
                m.ScoutBonus = 20f * c;
                m.FlankCoverBonus = 20f * c;
                m.DefenseBonus = 10f * c;
                m.PreferScout = 15f * c;       // Scout増員
                // プレイヤーの好む攻撃方向の反対側を強化
                m.CrystalGuardBonus = 10f * c;
                break;

            case CounterPlan.AntiCrystalRush:
                // クリスタル周囲を要塞化
                m.CrystalGuardBonus = 25f * c;
                m.WallBuildBonus = 25f * c;
                m.DefenseBonus = 20f * c;
                m.PreferGuardian = 20f * c;
                m.SummonBonus = 10f * c;       // 防衛兵力を増やす
                m.RetreatBonus = 5f * c;       // 前線を維持しつつ
                break;

            case CounterPlan.AntiEconomy:
                // 速攻で経済を壊す
                m.AttackBonus = 25f * c;
                m.AggressiveAdvance = 20f * c;
                m.SurroundBonus = 15f * c;
                m.BuildBonus = -10f * c;       // 建築は後回し
                m.PreferAssassin = 15f * c;    // 速い駒で奇襲
                // 建物を優先ターゲットにするボーナス
                m.SkillBonus = 10f * c;
                break;

            case CounterPlan.AntiBait:
                // 囮に乗らない、全体状況を把握
                m.ScoutBonus = 15f * c;
                m.RetreatBonus = 10f * c;
                m.AttackBonus = -10f * c;       // 突出しない
                m.SurroundBonus = 15f * c;      // 包囲で確実に
                m.CrystalGuardBonus = 10f * c;
                break;

            case CounterPlan.Adaptive:
                // 複合: 各傾向に応じて少しずつ対策
                m.DefenseBonus = profile.AggressionScore * 10f * c;
                m.AttackBonus = profile.TurtleTendency * 10f * c;
                m.WallBuildBonus = profile.CrystalFocusTendency * 10f * c;
                m.ScoutBonus = profile.FlankPreference * 10f * c;
                m.AggressiveAdvance = profile.EconomyFocus * 10f * c;
                m.CrystalGuardBonus = profile.CrystalFocusTendency * 10f * c;
                break;
        }

        return m;
    }

    // ================================================================
    //  危険ゾーン予測
    //  プレイヤーの攻撃パターンから次に攻撃される可能性の高い位置を予測
    // ================================================================
    void PredictDangerZones(PlayerProfiler.PlayerProfile profile, AIBoardState board)
    {
        _predictedDangerZones.Clear();
        _predictedSafeZones.Clear();

        // プレイヤーの好む攻撃方向から、クリスタルへの攻撃ルートを予測
        var attackDir = profile.PreferredAttackDirection;
        if (attackDir != Vector3.zero)
        {
            var crystalCell = new Vector3Int(
                Mathf.RoundToInt(board.EnemyCrystalPos.x), 0,
                Mathf.RoundToInt(board.EnemyCrystalPos.z));

            // 攻撃方向からクリスタルまでのライン上を危険ゾーンに
            for (int i = 1; i <= 8; i++)
            {
                var dangerCell = crystalCell + new Vector3Int(
                    Mathf.RoundToInt(attackDir.x * i), 0,
                    Mathf.RoundToInt(attackDir.z * i));
                _predictedDangerZones.Add(dangerCell);
            }

            // 攻撃方向と逆側を安全ゾーンに
            for (int i = 1; i <= 5; i++)
            {
                var safeCell = crystalCell + new Vector3Int(
                    Mathf.RoundToInt(-attackDir.x * i), 0,
                    Mathf.RoundToInt(-attackDir.z * i));
                _predictedSafeZones.Add(safeCell);
            }
        }
    }

    // ================================================================
    //  行動への適用 — AIActionに対するカウンターボーナスを計算
    // ================================================================
    public float GetCounterBonus(AIAction action, AIBoardState board)
    {
        if (_currentPlan == CounterPlan.None || _planConfidence < 0.1f)
            return 0f;

        float bonus = 0f;

        // ---- 行動タイプ別ボーナス ----
        switch (action.ActionType)
        {
            case AIActionType.Attack:
                bonus += _modifiers.AttackBonus;
                if (action.Skill != null)
                    bonus += _modifiers.SkillBonus;
                break;

            case AIActionType.SkillUse:
                bonus += _modifiers.SkillBonus;
                bonus += _modifiers.AttackBonus * 0.5f;
                break;

            case AIActionType.Move:
                // 前進/後退によるボーナス
                if (action.Unit != null)
                {
                    float curDist = Vector3.Distance(action.Unit.transform.position, board.EnemyCrystalPos);
                    float newDist = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
                    if (newDist < curDist)
                        bonus += _modifiers.DefenseBonus * 0.3f; // 自陣に戻る
                    else
                        bonus += _modifiers.AggressiveAdvance * 0.3f; // 前進
                }
                // 危険ゾーン回避ボーナス
                var targetCell = new Vector3Int(
                    Mathf.RoundToInt(action.TargetPos.x), 0,
                    Mathf.RoundToInt(action.TargetPos.z));
                if (_predictedDangerZones.Contains(targetCell))
                    bonus -= 5f * _planConfidence; // 危険ゾーンへの移動を避ける
                break;

            case AIActionType.Retreat:
                bonus += _modifiers.RetreatBonus;
                break;

            case AIActionType.DefenseRepos:
                bonus += _modifiers.DefenseBonus;
                // クリスタル周辺への配置
                float distToCrystal = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
                if (distToCrystal < 4f)
                    bonus += _modifiers.CrystalGuardBonus;
                break;

            case AIActionType.Surround:
                bonus += _modifiers.SurroundBonus;
                break;

            case AIActionType.Support:
                bonus += _modifiers.DefenseBonus * 0.5f;
                break;

            case AIActionType.Build:
                bonus += _modifiers.BuildBonus;
                if (FacilityData.IsWall(action.Facility))
                    bonus += _modifiers.WallBuildBonus;
                break;

            case AIActionType.Summon:
                bonus += _modifiers.SummonBonus;
                // 駒種別優先度
                if (action.SummonKind == Kind.Guardian)
                    bonus += _modifiers.PreferGuardian;
                if (action.SummonKind == Kind.Scout)
                    bonus += _modifiers.PreferScout;
                if (action.SummonKind == Kind.Assassin)
                    bonus += _modifiers.PreferAssassin;
                break;
        }

        // ---- 側面カバーボーナス（対側面プランの場合） ----
        if (_modifiers.FlankCoverBonus > 0f && action.Unit != null &&
            (action.ActionType == AIActionType.Move || action.ActionType == AIActionType.DefenseRepos))
        {
            // 味方の背面をカバーする位置への移動にボーナス
            float flankCover = EstimateFlankCoverage(action.TargetPos, board);
            bonus += flankCover * _modifiers.FlankCoverBonus;
        }

        // 信頼度はBuildModifiers時点で既にスケーリング済み → 二重適用しない
        return bonus;
    }

    // ================================================================
    //  側面カバー度の推定
    // ================================================================
    float EstimateFlankCoverage(Vector3 pos, AIBoardState board)
    {
        int alliesNearby = 0;
        foreach (var u in board.AliveEnemyUnits)
        {
            if (u == null || !u.gameObject.activeInHierarchy) continue;
            if (Vector3.Distance(u.transform.position, pos) < 3f)
                alliesNearby++;
        }
        // 味方が近くにいる位置ほど側面カバーが高い
        return Mathf.Clamp01(alliesNearby / 3f);
    }
}
