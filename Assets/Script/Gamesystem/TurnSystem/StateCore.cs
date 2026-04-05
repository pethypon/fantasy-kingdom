/// <summary>
/// ターン状態マシンの状態インターフェース。
/// 各状態は TurnGenerator から GameSystems / GameContext にアクセスする。
///
/// ステート遷移:
///   PlayerStart → PlayerMove → PlayerAttack → PlayerMove
///                                               ↓ (ターン終了)
///   EnemyStart → EnemyMove → PlayerStart
/// </summary>
public interface StateCore
{
    /// <summary>ステート開始時に呼ばれる。初期化処理を行う。</summary>
    void Entry() { }

    /// <summary>毎フレーム呼ばれる。入力処理・ステート更新を行う。</summary>
    void Update() { }

    /// <summary>ステート終了時に呼ばれる。クリーンアップ処理を行う。</summary>
    void Exit() { }
}

/// <summary>
/// TurnGenerator への参照を保持するステート基底クラス。
/// 各ステートはこのクラスを継承して GameSystems / GameContext に
/// 簡潔にアクセスできる。コンストラクタは TurnGenerator のみ。
/// </summary>
public abstract class TurnState : StateCore
{
    protected readonly TurnGenerator Turn;
    protected GameSystems Systems => Turn.Systems;
    protected GameContext Context => Turn.Context;

    protected TurnState(TurnGenerator turn)
    {
        Turn = turn;
    }

    public virtual void Entry() { }
    public virtual void Update() { }
    public virtual void Exit() { }

    /// <summary>視界の再計算（ショートハンド）</summary>
    protected void RefreshVision()
    {
        Systems.RefreshVision();
    }
}
