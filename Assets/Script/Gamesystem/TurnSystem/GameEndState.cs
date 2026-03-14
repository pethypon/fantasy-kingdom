using UnityEngine;
using UnityEngine.SceneManagement;

public class GameEndState : StateCore
{
    private TurnGenerater _turn;
    private GameResult _result;

    public GameEndState(TurnGenerater turn, GameResult result)
    {
        _turn = turn;
        _result = result;
    }

    // ==== ステート開始：結果表示・操作停止 ====
    public void Entry()
    {
        Debug.Log($"[GameEnd] ゲーム終了 結果: {_result}");

        // タイマー停止
        if (_turn.timeLimitSystem != null)
            _turn.timeLimitSystem.IsRunning = false;

        // Game End UI を表示
        if (_turn.gameEndUI != null)
        {
            string title;
            string detail;

            switch (_result)
            {
                case GameResult.Win:
                    title = "勝利！";
                    detail = "敵の重要拠点を破壊した！";
                    break;
                case GameResult.Lose:
                    title = "敗北…";
                    detail = "自軍の重要拠点が破壊された…";
                    break;
                case GameResult.TimeDraw:
                    title = "引き分け";
                    detail = "時間切れ — 両陣営同等の戦力";
                    break;
                default:
                    title = "ゲーム終了";
                    detail = "";
                    break;
            }

            _turn.gameEndUI.Show(title, detail, _result);
        }
    }

    // ==== 毎フレーム：リトライ入力のみ受け付け ====
    public void Update()
    {
        // Enter でリトライ
        if (_turn.TurnEndDown)
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
    }

    public void Exit() { }
}
