using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  AIThreatLevel — 脅威度システム (1〜100)
//
//  4帯構成:
//  ・脅威度 1〜10:  チュートリアル帯
//    目先の利益を優先しやすく、ミスも多め。
//    前線の穴、援護不足、危険判断の甘さが残る。3手先シミュレーション。
//  ・脅威度 11〜20: ノーマル帯
//    基本的な判断ができるようになり、露骨なミスが減る。
//    5手先シミュレーション。20で「普通の知能」完成ライン。
//  ・脅威度 21〜30: ハード帯
//    釣りに乗りにくい、孤立駒をカバー、撤退と再編が上手い、
//    防衛と攻撃の切り替えが上手い。8手先シミュレーション。
//  ・脅威度 31〜100: やりこみ帯
//    プレイヤーの勝ち筋を潰してくる領域。
//    成長型なら学習反映もより濃くなり、長く戦うほど嫌らしくなる。
//    31-40: 10手先、41-50: 15手先、51-100: 20手先シミュレーション。
//
//  進行:
//  ・Player勝利時のみ脅威度が進行
//  ・Player敗北時は進行しない
//  ・学習はPlayer勝利試合の立ち回りのみを対象とする
// =====================================================================
public class AIThreatLevel
{
    // ---- 定数: 帯の境界 ----
    public const int MinLevel = 1;
    public const int MaxLevel = 100;
    public const int TutorialEnd = 10;   // 1〜10:  チュートリアル帯
    public const int NormalEnd   = 20;   // 11〜20: ノーマル帯
    public const int HardEnd     = 30;   // 21〜30: ハード帯
    // 31〜100: やりこみ帯

    // ---- 状態 ----
    public int Level { get; private set; }

    // ---- 学習データ ----
    List<MatchAnalysis> _matchHistory = new List<MatchAnalysis>();
    const int MaxHistorySize = 20;

    // ---- 学習から得た重み調整 ----
    public float LearnedDefenseBias { get; private set; }
    public float LearnedEconomyBias { get; private set; }
    public float LearnedAggressionBias { get; private set; }

    public AIThreatLevel(int initialLevel = 1)
    {
        Level = Mathf.Clamp(initialLevel, MinLevel, MaxLevel);
        Debug.Log($"[AIThreatLevel] 初期脅威度={Level}  帯={GetTierName()}");
    }

    // ================================================================
    //  帯判定
    // ================================================================

    public bool IsTutorial => Level <= TutorialEnd;
    public bool IsNormal   => Level > TutorialEnd && Level <= NormalEnd;
    public bool IsHard     => Level > NormalEnd && Level <= HardEnd;
    public bool IsEndgame  => Level > HardEnd;

    public string GetTierName()
    {
        if (IsTutorial) return "チュートリアル帯";
        if (IsNormal)   return "ノーマル帯";
        if (IsHard)     return "ハード帯";
        return "やりこみ帯";
    }

    // ================================================================
    //  探索パラメータ
    // ================================================================

    /// <summary>探索エンジンを使うか（ノーマル帯以上で有効）</summary>
    public bool UseSearchEngine => Level > TutorialEnd;

    /// <summary>
    /// 探索の深さ（手先シミュレーション数）
    /// チュートリアル: 3, ノーマル: 5, ハード: 8,
    /// やりこみ31-40: 10, やりこみ41-50: 15, やりこみ51+: 20
    /// </summary>
    public int SearchDepth
    {
        get
        {
            if (Level <= TutorialEnd)  return 3;
            if (Level <= NormalEnd)    return 5;
            if (Level <= HardEnd)     return 8;
            if (Level <= 40)          return 10;
            if (Level <= 50)          return 15;
            return 20;
        }
    }

    /// <summary>探索の候補数上限（脅威度が高いほど多い候補を検討）</summary>
    public int SearchCandidateLimit
    {
        get
        {
            if (Level <= TutorialEnd)  return 3;
            if (Level <= NormalEnd)    return 6;
            if (Level <= HardEnd)     return 10;
            if (Level <= 50)          return 14;
            return 18;
        }
    }

    /// <summary>ロール再割当を使うか（ノーマル帯以上）</summary>
    public bool UseRoleAssignment => Level > TutorialEnd;

    // ================================================================
    //  行動品質パラメータ
    // ================================================================

    /// <summary>
    /// 学習反映率 (0.0〜1.0)
    /// チュートリアル: 0, ノーマル: 0.3, ハード: 0.6, やりこみ: 0.8〜1.0
    /// </summary>
    public float LearningRate
    {
        get
        {
            if (Level < TutorialEnd)   return 0f;
            if (Level == TutorialEnd)  return 0.1f;
            if (Level <= NormalEnd)    return 0.3f;
            if (Level <= HardEnd)     return 0.6f;
            // やりこみ帯: 31で0.8、100で1.0
            return Mathf.Lerp(0.8f, 1.0f, (Level - HardEnd) / (float)(MaxLevel - HardEnd));
        }
    }

    /// <summary>
    /// 戦略選択の質 (0.0〜1.0)
    /// 低いほどサブオプティマルな戦略を選びやすい
    /// </summary>
    public float StrategyQuality
    {
        get
        {
            if (Level <= TutorialEnd)
                return 0.3f + (Level / (float)TutorialEnd) * 0.3f; // 0.3〜0.6
            if (Level <= NormalEnd)
                return 0.7f + ((Level - TutorialEnd) / (float)(NormalEnd - TutorialEnd)) * 0.2f; // 0.7〜0.9
            return Mathf.Clamp01(0.9f + (Level - NormalEnd) / (float)(MaxLevel - NormalEnd) * 0.1f); // 0.9〜1.0
        }
    }

    /// <summary>
    /// チュートリアル帯の行動制限: 攻撃を控える強さ (1.0=最大, 0.0=制限なし)
    /// </summary>
    public float TutorialPassivity
    {
        get
        {
            if (!IsTutorial) return 0f;
            return 1f - (Level / (float)TutorialEnd);
        }
    }

    /// <summary>
    /// ミス率: ランダムに最善手を外す確率 (0.0〜1.0)
    /// チュートリアル: 0.5〜0.15, ノーマル: 0.1〜0.0, ハード以上: 0.0
    /// </summary>
    public float MistakeRate
    {
        get
        {
            if (Level <= TutorialEnd)
                return Mathf.Lerp(0.5f, 0.15f, (Level - 1f) / (TutorialEnd - 1f));
            if (Level <= NormalEnd)
                return Mathf.Lerp(0.1f, 0f, (Level - TutorialEnd - 1f) / (NormalEnd - TutorialEnd - 1f));
            return 0f;
        }
    }

    /// <summary>
    /// 危険評価の正確性 (0.0〜1.0)
    /// 低いほど脅威を過小評価する
    /// </summary>
    public float DangerAccuracy
    {
        get
        {
            if (Level <= TutorialEnd)
                return Mathf.Lerp(0.3f, 0.6f, (Level - 1f) / (TutorialEnd - 1f));
            if (Level <= NormalEnd)
                return Mathf.Lerp(0.7f, 0.9f, (Level - TutorialEnd - 1f) / (NormalEnd - TutorialEnd - 1f));
            if (Level <= HardEnd)
                return Mathf.Lerp(0.9f, 1.0f, (Level - NormalEnd - 1f) / (HardEnd - NormalEnd - 1f));
            return 1f;
        }
    }

    /// <summary>
    /// 援護・カバー能力 (0.0〜1.0)
    /// 低いほど味方の孤立を放置する
    /// </summary>
    public float SupportAbility
    {
        get
        {
            if (Level <= TutorialEnd)
                return Mathf.Lerp(0.2f, 0.4f, (Level - 1f) / (TutorialEnd - 1f));
            if (Level <= NormalEnd)
                return Mathf.Lerp(0.5f, 0.8f, (Level - TutorialEnd - 1f) / (NormalEnd - TutorialEnd - 1f));
            if (Level <= HardEnd)
                return Mathf.Lerp(0.8f, 1.0f, (Level - NormalEnd - 1f) / (HardEnd - NormalEnd - 1f));
            return 1f;
        }
    }

    /// <summary>
    /// 経済判断の深さ (0.0〜1.0)
    /// 低いほど経済管理が雑
    /// </summary>
    public float EconomyDepth
    {
        get
        {
            if (Level <= TutorialEnd)
                return Mathf.Lerp(0.2f, 0.4f, (Level - 1f) / (TutorialEnd - 1f));
            if (Level <= NormalEnd)
                return Mathf.Lerp(0.5f, 0.85f, (Level - TutorialEnd - 1f) / (NormalEnd - TutorialEnd - 1f));
            return Mathf.Clamp01(0.85f + (Level - NormalEnd) / (float)(MaxLevel - NormalEnd) * 0.15f);
        }
    }

    /// <summary>
    /// 釣り耐性 (0.0〜1.0) — ハード帯以上で高くなる
    /// 低いほど誘い出しに乗りやすい
    /// </summary>
    public float BaitResistance
    {
        get
        {
            if (Level <= TutorialEnd)  return 0.1f;
            if (Level <= NormalEnd)    return 0.3f;
            if (Level <= HardEnd)
                return Mathf.Lerp(0.6f, 0.9f, (Level - NormalEnd - 1f) / (HardEnd - NormalEnd - 1f));
            return Mathf.Clamp01(0.9f + (Level - HardEnd) / (float)(MaxLevel - HardEnd) * 0.1f);
        }
    }

    /// <summary>
    /// 戦略切り替え速度 (0.0〜1.0)
    /// やりこみ帯で高くなり、即座に攻守を切り替える
    /// </summary>
    public float StrategySwitchSpeed
    {
        get
        {
            if (Level <= TutorialEnd)  return 0.2f;
            if (Level <= NormalEnd)    return 0.4f;
            if (Level <= HardEnd)     return 0.7f;
            return Mathf.Lerp(0.8f, 1.0f, (Level - HardEnd) / (float)(MaxLevel - HardEnd));
        }
    }

    // ================================================================
    //  機械学習パラメータ（脅威���20以降で使用）
    // ================================================================

    /// <summary>ML機械学習が有効かどうか</summary>
    public bool UseMLBrain => Level >= NormalEnd;

    /// <summary>
    /// MLオンライン学習が有効かどうか（脅威度50以上でターン中に即座に学習）
    /// </summary>
    public bool UseOnlineLearning => Level >= 50;

    /// <summary>
    /// ML探索統合が有効かどうか（脅威度30以上で3手先探索にML評価を統合）
    /// </summary>
    public bool UseMLSearchIntegration => Level >= HardEnd;

    /// <summary>
    /// MLスコアの影響力倍率 (0.0〜2.0)
    /// 脅威度が高いほどMLの判断を重視する
    /// </summary>
    public float MLInfluence
    {
        get
        {
            if (Level < NormalEnd) return 0f;
            if (Level <= 25) return 0.3f;
            if (Level <= 35) return 0.5f;
            if (Level <= 50) return 0.8f;
            if (Level <= 70) return 1.2f;
            return Mathf.Lerp(1.5f, 2.0f, (Level - 70f) / (MaxLevel - 70f));
        }
    }

    // ================================================================
    //  試合結果の記録と脅威度進行
    // ================================================================

    public void RecordMatchResult(bool playerWon, MatchAnalysis analysis)
    {
        if (playerWon)
        {
            int increment = CalcLevelIncrement(analysis);
            int oldLevel = Level;
            Level = Mathf.Clamp(Level + increment, MinLevel, MaxLevel);

            _matchHistory.Add(analysis);
            if (_matchHistory.Count > MaxHistorySize)
                _matchHistory.RemoveAt(0);

            UpdateLearnedBiases();

            Debug.Log($"[AIThreatLevel] Player勝利 → 脅威度{oldLevel}→{Level} (+{increment})  " +
                      $"帯={GetTierName()}  崩壊原因={analysis.PrimaryFailure}");
        }
        else
        {
            Debug.Log($"[AIThreatLevel] Player敗北 → 脅威度据え置き ({Level})");
        }
    }

    int CalcLevelIncrement(MatchAnalysis analysis)
    {
        int inc = 1;
        if (analysis.TurnsPlayed < 10) inc = 3;
        else if (analysis.TurnsPlayed < 20) inc = 2;

        if (analysis.PrimaryFailure == FailureReason.EconomyCollapse)
            inc += 1;

        return Mathf.Max(1, inc);
    }

    void UpdateLearnedBiases()
    {
        if (_matchHistory.Count == 0) return;

        float defenseCount = 0f;
        float economyCount = 0f;
        float aggressCount = 0f;

        foreach (var m in _matchHistory)
        {
            switch (m.PrimaryFailure)
            {
                case FailureReason.CrystalDestroyed:
                case FailureReason.DefenseBreached:
                    defenseCount += 1f;
                    break;
                case FailureReason.EconomyCollapse:
                    economyCount += 1f;
                    break;
                case FailureReason.UnitWipeout:
                case FailureReason.OverextensionPunished:
                    aggressCount += 1f;
                    break;
            }
        }

        float total = _matchHistory.Count;
        LearnedDefenseBias = (defenseCount / total) * LearningRate;
        LearnedEconomyBias = (economyCount / total) * LearningRate;
        LearnedAggressionBias = (aggressCount / total) * LearningRate;

        Debug.Log($"[AIThreatLevel] 学習バイアス更新: 防衛={LearnedDefenseBias:F2} " +
                  $"経済={LearnedEconomyBias:F2} 攻撃={LearnedAggressionBias:F2}");
    }

    // ================================================================
    //  脅威度に応じた戦略評価補正
    // ================================================================

    public float GetThreatBonus(AIAction action, AIBoardState board)
    {
        float bonus = 0f;

        // ---- チュートリアル帯: 攻撃を控え、建築・撤退を優先 ----
        if (IsTutorial)
        {
            float passivity = TutorialPassivity;
            if (action.ActionType == AIActionType.Attack)
                bonus -= passivity * 15f;
            if (action.ActionType == AIActionType.SkillUse)
                bonus -= passivity * 10f;
            if (action.ActionType == AIActionType.Build)
                bonus += passivity * 5f;
            if (action.ActionType == AIActionType.Retreat)
                bonus += passivity * 5f;
        }

        // ---- 援護・カバーボーナス（能力に応じてスケーリング） ----
        if (action.ActionType == AIActionType.Support || action.ActionType == AIActionType.DefenseRepos)
            bonus *= SupportAbility;

        // ---- 危険評価スケーリング（低いほど脅威を軽視） ----
        if (action.ActionType == AIActionType.Retreat && DangerAccuracy < 1f)
        {
            // 危険を過小評価して撤退を選びにくくする
            bonus -= (1f - DangerAccuracy) * 10f;
        }

        // ---- 釣り耐性（低いほど目先の利益に飛びつく） ----
        if (action.ActionType == AIActionType.Attack && BaitResistance < 0.5f)
        {
            // 孤立した敵への攻撃にボーナス（罠かもしれないのに飛びつく）
            if (action.TargetUnit != null && action.Unit != null)
            {
                float dist = Vector3.Distance(action.Unit.transform.position, board.EnemyCrystalPos);
                if (dist > 10f) // 自陣から遠い場合
                    bonus += (0.5f - BaitResistance) * 8f;
            }
        }

        // ---- 学習バイアス適用 ----
        if (LearningRate > 0f)
        {
            if (action.ActionType == AIActionType.DefenseRepos || action.ActionType == AIActionType.Retreat)
                bonus += LearnedDefenseBias * 20f;
            if (action.ActionType == AIActionType.Build && action.Facility != FacilityKind.SubCrystal)
            {
                if (FacilityData.IsWall(action.Facility))
                    bonus += LearnedDefenseBias * 15f;
            }

            if (action.ActionType == AIActionType.Build && !FacilityData.IsWall(action.Facility)
                && !FacilityData.IsOffensive(action.Facility))
                bonus += LearnedEconomyBias * 15f;

            if (action.ActionType == AIActionType.Attack && action.TargetUnit != null)
            {
                int dmg = Mathf.Max(0, 1 + (action.Unit.ATK / 6) +
                    ((action.Unit.ATK / 2) - (action.TargetUnit.DEF / 4)));
                if (dmg >= action.TargetUnit.HP)
                    bonus += LearnedAggressionBias * 10f;
            }
        }

        return bonus;
    }
}

// =====================================================================
//  MatchAnalysis — 試合分析データ
// =====================================================================
public class MatchAnalysis
{
    public int TurnsPlayed;
    public FailureReason PrimaryFailure;

    public int FrontlineBreachTurn;
    public int EconomyCollapseTurn;
    public Vector3Int BreachPosition;
    public List<Kind> LostUnitKinds;
    public string PlayerTacticEstimate;

    public MatchAnalysis()
    {
        LostUnitKinds = new List<Kind>();
        PlayerTacticEstimate = "unknown";
    }
}

// =====================================================================
//  FailureReason — 敗因分類
// =====================================================================
public enum FailureReason
{
    CrystalDestroyed,
    DefenseBreached,
    EconomyCollapse,
    UnitWipeout,
    OverextensionPunished,
    Unknown
}
