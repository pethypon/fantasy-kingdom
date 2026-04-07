// =====================================================================
//  AIConfig — AI モード切替のための静的設定
//
//  PureRule モードでは一切の機械学習処理（MLIntegration / AILearning の
//  オンライン学習）を経由しない純粋ルールAIとして動作する。再現性の高い
//  デバッグ・ユニットテスト時に使用する。
//
//  デフォルトは MLAssisted（従来挙動の維持）。
//  SerializeField からの切替は TurnGenerator.useMLAssistedAI を介して行う。
// =====================================================================
public enum AIMode
{
    PureRule,
    MLAssisted,
}

public static class AIConfig
{
    public static AIMode Mode = AIMode.MLAssisted;

    /// <summary>ML 関連の処理を実行してよいかどうか</summary>
    public static bool IsMLEnabled => Mode == AIMode.MLAssisted;
}
