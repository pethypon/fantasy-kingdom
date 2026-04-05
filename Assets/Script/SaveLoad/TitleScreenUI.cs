using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// =====================================================================
//  TitleScreenUI — タイトル画面
//
//  ゲーム起動時に表示され、以下の選択肢を提供:
//  ・はじめから（新規ゲーム）
//  ・ロード（セーブデータから再開）
//  ・終了
//
//  UIBuilder の後、GameGenerator の前に実行される。
//  GameGenerator.Awake は TitleScreenUI が閉じるまで待機する。
// =====================================================================
[DefaultExecutionOrder(-50)]
public class TitleScreenUI : MonoBehaviour
{
    public static TitleScreenUI Instance { get; private set; }

    /// <summary>タイトル画面が表示中かどうか</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>ロードが選択されたスロット (-1 = 新規ゲーム)</summary>
    public int SelectedLoadSlot { get; private set; } = -1;

    private GameObject rootOverlay;
    private GameObject slotPanel;

    void Awake()
    {
        Instance = this;
        BuildTitleScreen();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ================================================================
    //  タイトル画面構築
    // ================================================================

    private void BuildTitleScreen()
    {
        var canvas = FindFirstCanvas();
        if (canvas == null)
        {
            Debug.LogError("[TitleScreen] Canvas が見つかりません");
            IsActive = false;
            return;
        }

        // 全画面オーバーレイ
        rootOverlay = new GameObject("TitleScreenOverlay", typeof(RectTransform));
        rootOverlay.transform.SetParent(canvas.transform, false);
        var rt = rootOverlay.GetComponent<RectTransform>();
        StretchFill(rt);
        // 最前面に表示
        rootOverlay.transform.SetAsLastSibling();

        var bg = rootOverlay.AddComponent<Image>();
        bg.color = new Color(0.02f, 0.02f, 0.04f, 1f);

        // タイトルテキスト
        var titleGo = CreateTMPObject("Title", rootOverlay.transform,
            "Fantasy Kingdom", 64, BrandGuide.Primary);
        titleGo.GetComponent<TextMeshProUGUI>().fontStyle = FontStyles.Bold;
        var titleRT = titleGo.GetComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0.1f, 0.60f);
        titleRT.anchorMax = new Vector2(0.9f, 0.85f);
        titleRT.offsetMin = Vector2.zero;
        titleRT.offsetMax = Vector2.zero;

        // サブタイトル
        var profile = SaveSystem.LoadProfile();
        string subText = $"脅威度: {profile.ThreatLevel}  戦績: {profile.TotalWins}勝 {profile.TotalLosses}敗";
        var subGo = CreateTMPObject("SubTitle", rootOverlay.transform,
            subText, 22, BrandGuide.TextSecondary);
        var subRT = subGo.GetComponent<RectTransform>();
        subRT.anchorMin = new Vector2(0.2f, 0.52f);
        subRT.anchorMax = new Vector2(0.8f, 0.60f);
        subRT.offsetMin = Vector2.zero;
        subRT.offsetMax = Vector2.zero;

        // ボタンエリア
        var btnArea = new GameObject("ButtonArea", typeof(RectTransform));
        btnArea.transform.SetParent(rootOverlay.transform, false);
        var btnAreaRT = btnArea.GetComponent<RectTransform>();
        btnAreaRT.anchorMin = new Vector2(0.3f, 0.15f);
        btnAreaRT.anchorMax = new Vector2(0.7f, 0.50f);
        btnAreaRT.offsetMin = Vector2.zero;
        btnAreaRT.offsetMax = Vector2.zero;

        var vlg = btnArea.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 16;
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;

        // はじめからボタン
        var newGameBtn = CreateMenuButton("はじめから", btnArea.transform,
            BrandGuide.BtnBuild);
        newGameBtn.onClick.AddListener(OnNewGame);

        // ロードボタン
        bool hasAnySave = SaveSystem.HasSaveData(0) || SaveSystem.HasSaveData(1) || SaveSystem.HasSaveData(2);
        var loadBtn = CreateMenuButton("ロード", btnArea.transform,
            hasAnySave ? BrandGuide.BtnUnit : BrandGuide.BtnWait);
        loadBtn.interactable = hasAnySave;
        loadBtn.onClick.AddListener(ShowLoadSlots);

        // 終了ボタン
        var quitBtn = CreateMenuButton("終了", btnArea.transform,
            BrandGuide.BtnAttack);
        quitBtn.onClick.AddListener(OnQuit);
    }

    // ================================================================
    //  ロードスロット選択画面
    // ================================================================

    private void ShowLoadSlots()
    {
        if (slotPanel != null) Object.Destroy(slotPanel);

        slotPanel = new GameObject("SlotPanel", typeof(RectTransform));
        slotPanel.transform.SetParent(rootOverlay.transform, false);
        var rt = slotPanel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.25f, 0.20f);
        rt.anchorMax = new Vector2(0.75f, 0.70f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var panelBg = slotPanel.AddComponent<Image>();
        panelBg.color = BrandGuide.PanelBgLight;

        // ヘッダー
        var header = CreateTMPObject("Header", slotPanel.transform,
            "ロードするスロットを選択", 24, Color.white);
        var headerRT = header.GetComponent<RectTransform>();
        headerRT.anchorMin = new Vector2(0, 0.85f);
        headerRT.anchorMax = new Vector2(1, 1);
        headerRT.offsetMin = new Vector2(10, 0);
        headerRT.offsetMax = new Vector2(-10, -5);

        // スロットボタン
        for (int i = 0; i < 3; i++)
        {
            int slot = i;
            string summary = SaveSystem.GetSlotSummary(slot);
            bool hasData = SaveSystem.HasSaveData(slot);

            var btn = CreateSlotButton($"Slot{i + 1}: {summary}", slotPanel.transform,
                hasData ? BrandGuide.BtnUnit : BrandGuide.BtnWait);
            btn.interactable = hasData;

            var btnRT = btn.GetComponent<RectTransform>();
            float yMax = 0.80f - i * 0.25f;
            float yMin = yMax - 0.20f;
            btnRT.anchorMin = new Vector2(0.05f, yMin);
            btnRT.anchorMax = new Vector2(0.95f, yMax);
            btnRT.offsetMin = Vector2.zero;
            btnRT.offsetMax = Vector2.zero;

            btn.onClick.AddListener(() => OnLoadSlot(slot));
        }

        // 戻るボタン
        var backBtn = CreateSlotButton("戻る", slotPanel.transform,
            BrandGuide.BtnCancel);
        var backRT = backBtn.GetComponent<RectTransform>();
        backRT.anchorMin = new Vector2(0.3f, 0.02f);
        backRT.anchorMax = new Vector2(0.7f, 0.12f);
        backRT.offsetMin = Vector2.zero;
        backRT.offsetMax = Vector2.zero;
        backBtn.onClick.AddListener(() => Object.Destroy(slotPanel));
    }

    // ================================================================
    //  ボタンコールバック
    // ================================================================

    private void OnNewGame()
    {
        SelectedLoadSlot = -1;
        Close();
    }

    private void OnLoadSlot(int slot)
    {
        SelectedLoadSlot = slot;
        Close();
    }

    private void OnQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void Close()
    {
        IsActive = false;
        if (rootOverlay != null) rootOverlay.SetActive(false);
        Debug.Log($"[TitleScreen] 閉じる: slot={SelectedLoadSlot}");
    }

    // ================================================================
    //  ヘルパー
    // ================================================================

    private Canvas FindFirstCanvas()
    {
        return Object.FindFirstObjectByType<Canvas>();
    }

    private GameObject CreateTMPObject(string name, Transform parent, string text, float size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;
        return go;
    }

    private Button CreateMenuButton(string label, Transform parent, Color bgColor)
    {
        var go = new GameObject(label + "Btn", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 60;

        var img = go.AddComponent<Image>();
        img.color = bgColor;

        var btn = go.AddComponent<Button>();
        BrandGuide.ApplyButtonStyle(btn, bgColor);

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, false);
        StretchFill(labelGo.GetComponent<RectTransform>());
        var tmp = labelGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = BrandGuide.FontHeader;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = BrandGuide.TextPrimary;
        tmp.fontStyle = FontStyles.Bold;

        return btn;
    }

    private Button CreateSlotButton(string label, Transform parent, Color bgColor)
    {
        var go = new GameObject(label, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var img = go.AddComponent<Image>();
        img.color = bgColor;

        var btn = go.AddComponent<Button>();
        BrandGuide.ApplyButtonStyle(btn, bgColor);

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, false);
        StretchFill(labelGo.GetComponent<RectTransform>());
        var tmp = labelGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = BrandGuide.FontBody;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = BrandGuide.TextPrimary;

        return btn;
    }

    private void StretchFill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
