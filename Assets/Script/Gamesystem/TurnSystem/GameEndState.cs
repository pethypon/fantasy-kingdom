using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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
        if (_turn.timerSystem != null)
            _turn.timerSystem.StopTurn();

        // UI片付け
        EnemyTurnBannerUI.Hide();
        if (_turn.inputHintUI != null)
            _turn.inputHintUI.SetHints(InputHintUI.Hints.GameEnd);

        // 残留ポイントをクリア
        _turn.movegenerater.MoveReset();
        _turn.attackpoint.AtkpDestroy();

        BuildGameEndUI();
    }

    // ==== 毎フレーム：何もしない（入力を受け付けない） ====
    public void Update()
    {
    }

    public void Exit() { }

    // ==== ゲーム終了UI構築 ====
    private void BuildGameEndUI()
    {
        var canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[GameEndState] Canvas が見つかりません");
            return;
        }

        // オーバーレイ背景
        var overlay = new GameObject("GameEndOverlay", typeof(RectTransform));
        overlay.transform.SetParent(canvas.transform, false);
        var overlayRT = overlay.GetComponent<RectTransform>();
        overlayRT.anchorMin = Vector2.zero;
        overlayRT.anchorMax = Vector2.one;
        overlayRT.offsetMin = Vector2.zero;
        overlayRT.offsetMax = Vector2.zero;
        var overlayImg = overlay.AddComponent<Image>();
        overlayImg.color = new Color(0f, 0f, 0f, 0.7f);

        // 中央パネル
        var panel = new GameObject("GameEndPanel", typeof(RectTransform));
        panel.transform.SetParent(overlay.transform, false);
        var panelRT = panel.GetComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.pivot = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta = new Vector2(600, 400);
        var panelImg = panel.AddComponent<Image>();
        panelImg.color = new Color(0.1f, 0.1f, 0.15f, 0.95f);

        // 結果テキスト
        string resultText = GetResultText();
        Color resultColor = GetResultColor();

        var titleGo = new GameObject("ResultTitle", typeof(RectTransform));
        titleGo.transform.SetParent(panel.transform, false);
        var titleRT = titleGo.GetComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0, 0.55f);
        titleRT.anchorMax = new Vector2(1, 0.95f);
        titleRT.offsetMin = new Vector2(20, 0);
        titleRT.offsetMax = new Vector2(-20, 0);
        var titleTMP = titleGo.AddComponent<TextMeshProUGUI>();
        titleTMP.text = resultText;
        titleTMP.fontSize = 48;
        titleTMP.alignment = TextAlignmentOptions.Center;
        titleTMP.color = resultColor;

        // 詳細テキスト
        string detailText = GetDetailText();
        var detailGo = new GameObject("ResultDetail", typeof(RectTransform));
        detailGo.transform.SetParent(panel.transform, false);
        var detailRT = detailGo.GetComponent<RectTransform>();
        detailRT.anchorMin = new Vector2(0, 0.30f);
        detailRT.anchorMax = new Vector2(1, 0.55f);
        detailRT.offsetMin = new Vector2(20, 0);
        detailRT.offsetMax = new Vector2(-20, 0);
        var detailTMP = detailGo.AddComponent<TextMeshProUGUI>();
        detailTMP.text = detailText;
        detailTMP.fontSize = 22;
        detailTMP.alignment = TextAlignmentOptions.Center;
        detailTMP.color = new Color(0.8f, 0.8f, 0.8f);

        // リトライボタン
        var retryGo = new GameObject("RetryButton", typeof(RectTransform));
        retryGo.transform.SetParent(panel.transform, false);
        var retryRT = retryGo.GetComponent<RectTransform>();
        retryRT.anchorMin = new Vector2(0.1f, 0.05f);
        retryRT.anchorMax = new Vector2(0.45f, 0.25f);
        retryRT.offsetMin = Vector2.zero;
        retryRT.offsetMax = Vector2.zero;
        var retryImg = retryGo.AddComponent<Image>();
        retryImg.color = new Color(0.2f, 0.45f, 0.2f, 1f);
        var retryBtn = retryGo.AddComponent<Button>();
        retryBtn.targetGraphic = retryImg;
        retryBtn.onClick.AddListener(() => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex));

        var retryLabel = new GameObject("Label", typeof(RectTransform));
        retryLabel.transform.SetParent(retryGo.transform, false);
        var retryLabelRT = retryLabel.GetComponent<RectTransform>();
        retryLabelRT.anchorMin = Vector2.zero;
        retryLabelRT.anchorMax = Vector2.one;
        retryLabelRT.offsetMin = Vector2.zero;
        retryLabelRT.offsetMax = Vector2.zero;
        var retryTMP = retryLabel.AddComponent<TextMeshProUGUI>();
        retryTMP.text = "リトライ";
        retryTMP.fontSize = 24;
        retryTMP.alignment = TextAlignmentOptions.Center;
        retryTMP.color = Color.white;

        // 終了ボタン
        var quitGo = new GameObject("QuitButton", typeof(RectTransform));
        quitGo.transform.SetParent(panel.transform, false);
        var quitRT = quitGo.GetComponent<RectTransform>();
        quitRT.anchorMin = new Vector2(0.55f, 0.05f);
        quitRT.anchorMax = new Vector2(0.9f, 0.25f);
        quitRT.offsetMin = Vector2.zero;
        quitRT.offsetMax = Vector2.zero;
        var quitImg = quitGo.AddComponent<Image>();
        quitImg.color = new Color(0.5f, 0.2f, 0.2f, 1f);
        var quitBtn = quitGo.AddComponent<Button>();
        quitBtn.targetGraphic = quitImg;
        quitBtn.onClick.AddListener(() =>
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        });

        var quitLabel = new GameObject("Label", typeof(RectTransform));
        quitLabel.transform.SetParent(quitGo.transform, false);
        var quitLabelRT = quitLabel.GetComponent<RectTransform>();
        quitLabelRT.anchorMin = Vector2.zero;
        quitLabelRT.anchorMax = Vector2.one;
        quitLabelRT.offsetMin = Vector2.zero;
        quitLabelRT.offsetMax = Vector2.zero;
        var quitTMP = quitLabel.AddComponent<TextMeshProUGUI>();
        quitTMP.text = "終了";
        quitTMP.fontSize = 24;
        quitTMP.alignment = TextAlignmentOptions.Center;
        quitTMP.color = Color.white;
    }

    private string GetResultText()
    {
        switch (_result)
        {
            case GameResult.Win: return "勝利";
            case GameResult.Lose: return "敗北";
            case GameResult.TimeUpWin: return "時間切れ 勝利";
            case GameResult.TimeUpLose: return "時間切れ 敗北";
            case GameResult.TimeUpDraw: return "時間切れ 引き分け";
            default: return "ゲーム終了";
        }
    }

    private Color GetResultColor()
    {
        switch (_result)
        {
            case GameResult.Win:
            case GameResult.TimeUpWin:
                return new Color(0.3f, 0.8f, 0.3f);
            case GameResult.Lose:
            case GameResult.TimeUpLose:
                return new Color(0.8f, 0.3f, 0.3f);
            case GameResult.TimeUpDraw:
                return new Color(0.8f, 0.8f, 0.3f);
            default:
                return Color.white;
        }
    }

    private string GetDetailText()
    {
        switch (_result)
        {
            case GameResult.Win:
                return "敵のクリスタルまたは王を破壊しました";
            case GameResult.Lose:
                return "自軍のクリスタルまたは王が破壊されました";
            case GameResult.TimeUpWin:
                return "持ち時間終了 - クリスタルHP優勢で勝利";
            case GameResult.TimeUpLose:
                return "持ち時間終了 - クリスタルHP劣勢で敗北";
            case GameResult.TimeUpDraw:
                return "持ち時間終了 - 引き分け";
            default:
                return "";
        }
    }
}
