using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  AIThreatLevel — 脅威度システム (1〜100)
//
//  仕様:
//  ・脅威度1〜20はチュートリアル帯（簡易戦略・保守的行動）
//  ・脅威度20で通常知能に到達
//  ・Player勝利時のみ次の脅威度へ進行
//  ・Player敗北時は進行しない
//  ・学習はPlayer勝利試合の立ち回りのみを対象とする
//
//  段階開放:
//  ・1〜19:  チュートリアル帯 — 簡易戦略・保守的行動・先読みなし
//  ・20〜39: 通常知能 — 基本戦略・標準評価
//  ・40〜59: 3手先探索を本格導入
//  ・60〜79: ロール再編と全体指揮を強化
//  ・80〜100: 学習反映率と局面適応を最大化
// =====================================================================
public class AIThreatLevel
{
    // ---- 定数 ----
    public const int MinLevel = 1;
    public const int MaxLevel = 100;
    const int TutorialEnd = 19;
    const int NormalStart = 20;
    const int SearchStart = 40;
    const int AdvancedStart = 60;
    const int MasterStart = 80;

    // ---- 状態 ----
    public int Level { get; private set; }

    // ---- 学習データ: Playerに負けた試合から構造化データ ----
    // 「何に負けたか」を記録
    List<MatchAnalysis> _matchHistory = new List<MatchAnalysis>();
    const int MaxHistorySize = 20;

    // ---- 学習から得た重み調整 ----
    public float LearnedDefenseBias { get; private set; }    // 防衛重視度
    public float LearnedEconomyBias { get; private set; }    // 経済重視度
    public float LearnedAggressionBias { get; private set; } // 攻撃重視度

    public AIThreatLevel(int initialLevel = 1)
    {
        Level = Mathf.Clamp(initialLevel, MinLevel, MaxLevel);
        Debug.Log($"[AIThreatLevel] 初期脅威度={Level}  段階={GetStageName()}");
    }

    // ================================================================
    //  段階判定
    // ================================================================

    public bool IsTutorial       => Level <= TutorialEnd;
    public bool IsNormal         => Level >= NormalStart && Level < SearchStart;
    public bool IsSearchEnabled  => Level >= SearchStart;
    public bool IsAdvanced       => Level >= AdvancedStart;
    public bool IsMaster         => Level >= MasterStart;

    public string GetStageName()
    {
        if (IsTutorial) return "チュートリアル";
        if (IsNormal) return "通常知能";
        if (Level < AdvancedStart) return "探索導入";
        if (Level < MasterStart) return "上級指揮";
        return "マスター";
    }

    // ================================================================
    //  能力パラメータ（脅威度に応じた段階開放）
    // ================================================================

    /// <summary>3手先探索を使うか</summary>
    public bool UseSearchEngine => Level >= SearchStart;

    /// <summary>探索の深さ（1〜3）</summary>
    public int SearchDepth
    {
        get
        {
            if (Level < SearchStart) return 1;
            if (Level < AdvancedStart) return 2;
            return 3;
        }
    }

    /// <summary>探索の候補数上限（脅威度が高いほど多い候補を検討）</summary>
    public int SearchCandidateLimit
    {
        get
        {
            if (Level < SearchStart) return 3;
            if (Level < AdvancedStart) return 5;
            if (Level < MasterStart) return 8;
            return 12;
        }
    }

    /// <summary>ロール再割当を使うか</summary>
    public bool UseRoleAssignment => Level >= NormalStart;

    /// <summary>学習反映率 (0.0〜1.0)</summary>
    public float LearningRate
    {
        get
        {
            if (Level < NormalStart) return 0f;
            if (Level < SearchStart) return 0.3f;
            if (Level < AdvancedStart) return 0.5f;
            if (Level < MasterStart) return 0.7f;
            return 1.0f;
        }
    }

    /// <summary>戦略選択の質（低脅威度では最適でない戦略を選びやすい）</summary>
    public float StrategyQuality
    {
        get
        {
            if (Level < NormalStart) return 0.4f + (Level / (float)NormalStart) * 0.3f;
            return Mathf.Clamp01(0.7f + (Level - NormalStart) / 80f * 0.3f);
        }
    }

    /// <summary>チュートリアル帯の行動制限: 攻撃を控える確率</summary>
    public float TutorialPassivity
    {
        get
        {
            if (!IsTutorial) return 0f;
            return 1f - (Level / (float)NormalStart);
        }
    }

    // ================================================================
    //  試合結果の記録と脅威度進行
    // ================================================================

    /// <summary>
    /// 試合結果を記録する。
    /// Player勝利時のみ脅威度が進行し、学習データが蓄積される。
    /// </summary>
    public void RecordMatchResult(bool playerWon, MatchAnalysis analysis)
    {
        if (playerWon)
        {
            // Player勝利 → 脅威度を上げる
            int increment = CalcLevelIncrement(analysis);
            int oldLevel = Level;
            Level = Mathf.Clamp(Level + increment, MinLevel, MaxLevel);

            // 学習データを蓄積（Player勝利試合のみ）
            _matchHistory.Add(analysis);
            if (_matchHistory.Count > MaxHistorySize)
                _matchHistory.RemoveAt(0);

            // 学習データから重み更新
            UpdateLearnedBiases();

            Debug.Log($"[AIThreatLevel] Player勝利 → 脅威度{oldLevel}→{Level} (+{increment})  " +
                      $"段階={GetStageName()}  " +
                      $"崩壊原因={analysis.PrimaryFailure}");
        }
        else
        {
            Debug.Log($"[AIThreatLevel] Player敗北 → 脅威度据え置き ({Level})");
        }
    }

    int CalcLevelIncrement(MatchAnalysis analysis)
    {
        // 基本1、早期敗北なら2、完敗なら3
        int inc = 1;
        if (analysis.TurnsPlayed < 10) inc = 3;
        else if (analysis.TurnsPlayed < 20) inc = 2;

        // 経済崩壊が原因なら経済学習で大きく進む
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

    /// <summary>脅威度と学習に基づく行動補正</summary>
    public float GetThreatBonus(AIAction action, AIBoardState board)
    {
        float bonus = 0f;

        // チュートリアル帯: 攻撃を控える
        if (IsTutorial)
        {
            if (action.ActionType == AIActionType.Attack)
                bonus -= TutorialPassivity * 15f;
            if (action.ActionType == AIActionType.SkillUse)
                bonus -= TutorialPassivity * 10f;
            // 防衛・建築を優先
            if (action.ActionType == AIActionType.Build)
                bonus += TutorialPassivity * 5f;
            if (action.ActionType == AIActionType.Retreat)
                bonus += TutorialPassivity * 5f;
        }

        // 学習バイアス適用
        if (LearningRate > 0f)
        {
            // 防衛バイアス: 防衛系行動を加点
            if (action.ActionType == AIActionType.DefenseRepos || action.ActionType == AIActionType.Retreat)
                bonus += LearnedDefenseBias * 20f;
            if (action.ActionType == AIActionType.Build && action.Facility != FacilityKind.SubCrystal)
            {
                if (FacilityData.IsWall(action.Facility))
                    bonus += LearnedDefenseBias * 15f;
            }

            // 経済バイアス: 経済系行動を加点
            if (action.ActionType == AIActionType.Build && !FacilityData.IsWall(action.Facility)
                && !FacilityData.IsOffensive(action.Facility))
                bonus += LearnedEconomyBias * 15f;

            // 攻撃バイアス: 慎重な攻撃を加点（無謀でなく効率的な攻撃）
            if (action.ActionType == AIActionType.Attack && action.TargetUnit != null)
            {
                // 確殺可能な場合のみ攻撃を推奨
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
//  「何に負けたか」を構造化して記録する
// =====================================================================
public class MatchAnalysis
{
    public int TurnsPlayed;
    public FailureReason PrimaryFailure;

    // 崩壊詳細
    public int FrontlineBreachTurn;     // 前線が崩壊したターン
    public int EconomyCollapseTurn;     // 経済が崩壊したターン
    public Vector3Int BreachPosition;   // 突破された位置
    public List<Kind> LostUnitKinds;    // 失った駒種
    public string PlayerTacticEstimate; // 推定されたPlayerの戦術

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
    CrystalDestroyed,       // クリスタル破壊
    DefenseBreached,        // 防衛線突破
    EconomyCollapse,        // 経済崩壊（資源枯渇）
    UnitWipeout,            // 全滅
    OverextensionPunished,  // 攻め急ぎによる崩壊
    Unknown
}
