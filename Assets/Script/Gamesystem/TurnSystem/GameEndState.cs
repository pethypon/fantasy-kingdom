using UnityEngine;

public class GameEndState : StateCore
{
    private TurnGenerater _turn;
    private GameResult _result;

    public GameEndState(TurnGenerater turn, GameResult result)
    {
        _turn = turn;
        _result = result;
    }

    // ─── ステート開始：結果表示・操作停止 ─────────────────────────────
    public void Entry()
    {
        Debug.Log($"[GameEnd] ゲーム終了 ── {_result}");

        // TODO: リザルトUI表示
        // 例: UIManager.Instance.ShowResult(_result);
    }

    // ─── 毎フレーム：何もしない（入力を受け付けない） ─────────────────
    public void Update()
    {
        // 操作停止状態
    }

    public void Exit() { }
}
