using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameEndState : TurnState
{
    private GameResult _result;

    public GameEndState(TurnGenerator turn, GameResult result) : base(turn)
    {
        _result = result;
    }

    public override void Entry()
    {
        Debug.Log($"[GameEnd] ゲーム終了 結果: {_result}");

        if (Systems.TimerSystem != null)
            Systems.TimerSystem.StopTurn();

        EnemyTurnBannerUI.Hide();
        if (Systems.InputHintUI != null)
            Systems.InputHintUI.SetHints(InputHintUI.Hints.GameEnd);

        Systems.MoveGenerator.MoveReset();
        Systems.AttackGenerator.AtkpDestroy();

        UpdateThreatLevel();
        BuildGameEndUI();
    }

    private void UpdateThreatLevel()
    {
        bool isWin = _result == GameResult.Win || _result == GameResult.TimeUpWin;
        bool isLose = _result == GameResult.Lose || _result == GameResult.TimeUpLose;

        if (isWin)
        {
            int newLevel = SaveSystem.IncrementThreatLevel();
            Debug.Log($"[GameEnd] 勝利！ 脅威度+1 → {newLevel}");

            if (Systems.AICommander != null)
            {
                var analysis = new MatchAnalysis { TurnsPlayed = Context.Turn, PrimaryFailure = FailureReason.Unknown };
                Systems.AICommander.RecordMatchResult(true, analysis);
            }
        }
        else if (isLose)
        {
            SaveSystem.RecordLoss();
            Debug.Log("[GameEnd] 敗北 → 脅威度据え置き");
        }
    }

    public override void Update() { }
    public override void Exit() { }

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

        // 脅威度テキスト
        var profile = SaveSystem.LoadProfile();
        var threatGo = new GameObject("ThreatText", typeof(RectTransform));
        threatGo.transform.SetParent(panel.transform, false);
        var threatRT = threatGo.GetComponent<RectTransform>();
        threatRT.anchorMin = new Vector2(0, 0.25f);
        threatRT.anchorMax = new Vector2(1, 0.35f);
        threatRT.offsetMin = new Vector2(20, 0);
        threatRT.offsetMax = new Vector2(-20, 0);
        var threatTMP = threatGo.AddComponent<TextMeshProUGUI>();
        bool isWin = _result == GameResult.Win || _result == GameResult.TimeUpWin;
        threatTMP.text = isWin
            ? $"脅威度: {profile.ThreatLevel} (↑)"
            : $"脅威度: {profile.ThreatLevel}";
        threatTMP.fontSize = 20;
        threatTMP.alignment = TextAlignmentOptions.Center;
        threatTMP.color = new Color(0.7f, 0.7f, 0.5f);

        // リトライボタン
        var retryGo = CreateEndButton(panel.transform, "リトライ",
            new Vector2(0.03f, 0.05f), new Vector2(0.33f, 0.22f),
            new Color(0.2f, 0.45f, 0.2f, 1f));
        retryGo.GetComponent<Button>().onClick.AddListener(() =>
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex));

        // タイトルへボタン
        var titleBtnGo = CreateEndButton(panel.transform, "タイトルへ",
            new Vector2(0.36f, 0.05f), new Vector2(0.64f, 0.22f),
            new Color(0.3f, 0.3f, 0.5f, 1f));
        titleBtnGo.GetComponent<Button>().onClick.AddListener(() =>
        {
            GameMenuUI.ReturnToTitle = true;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        });

        // 終了ボタン
        var quitGo = new GameObject("QuitButton", typeof(RectTransform));
        quitGo.transform.SetParent(panel.transform, false);
        var quitRT = quitGo.GetComponent<RectTransform>();
        quitRT.anchorMin = new Vector2(0.67f, 0.05f);
        quitRT.anchorMax = new Vector2(0.97f, 0.22f);
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

    private GameObject CreateEndButton(Transform parent, string label,
        Vector2 anchorMin, Vector2 anchorMax, Color bgColor)
    {
        var go = new GameObject(label + "Button", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.color = bgColor;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, false);
        var labelRT = labelGo.GetComponent<RectTransform>();
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;
        var tmp = labelGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 22;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;

        return go;
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
