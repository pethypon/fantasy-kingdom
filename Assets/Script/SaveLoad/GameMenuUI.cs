using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// =====================================================================
//  GameMenuUI — インゲームメニュー（ポーズメニュー）
//
//  TopBar の「メニュー」ボタンから開く。
//  セーブ / ロード / タイトルへ戻る / 閉じる を提供。
// =====================================================================
public class GameMenuUI : MonoBehaviour
{
    public static GameMenuUI Instance { get; private set; }

    private GameObject overlay;
    private GameObject slotPanel;
    private bool isSaveMode; // true=セーブ, false=ロード

    // ゲームシステム参照（セーブ用）
    private TurnGenerator turnGen;
    private FactionState factionState;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Init(TurnGenerator tg, FactionState fs)
    {
        turnGen = tg;
        factionState = fs;
    }

    // ================================================================
    //  メニュー開閉
    // ================================================================

    public void Toggle()
    {
        if (overlay != null && overlay.activeSelf)
            Close();
        else
            Open();
    }

    public void Open()
    {
        if (overlay != null) Destroy(overlay);
        BuildMenuUI();
    }

    public void Close()
    {
        if (slotPanel != null) { Destroy(slotPanel); slotPanel = null; }
        if (overlay != null) { Destroy(overlay); overlay = null; }
    }

    // ================================================================
    //  メニューUI構築
    // ================================================================

    private void BuildMenuUI()
    {
        var canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        // オーバーレイ背景（半透明）
        overlay = new GameObject("GameMenuOverlay", typeof(RectTransform));
        overlay.transform.SetParent(canvas.transform, false);
        overlay.transform.SetAsLastSibling();
        var rt = overlay.GetComponent<RectTransform>();
        StretchFill(rt);

        var bg = overlay.AddComponent<Image>();
        bg.color = BrandGuide.OverlayBg;

        // 中央パネル
        var panel = new GameObject("MenuPanel", typeof(RectTransform));
        panel.transform.SetParent(overlay.transform, false);
        var panelRT = panel.GetComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0.3f, 0.15f);
        panelRT.anchorMax = new Vector2(0.7f, 0.85f);
        panelRT.offsetMin = Vector2.zero;
        panelRT.offsetMax = Vector2.zero;

        var panelBg = panel.AddComponent<Image>();
        panelBg.color = BrandGuide.PanelBgLight;

        // ヘッダー
        var header = CreateTMP("MenuHeader", panel.transform, "メニュー", 32, BrandGuide.Primary);
        header.fontStyle = FontStyles.Bold;
        var headerRT = header.GetComponent<RectTransform>();
        headerRT.anchorMin = new Vector2(0, 0.88f);
        headerRT.anchorMax = new Vector2(1, 0.98f);
        headerRT.offsetMin = Vector2.zero;
        headerRT.offsetMax = Vector2.zero;

        // 脅威度表示
        var profile = SaveSystem.LoadProfile();
        var threatText = CreateTMP("ThreatInfo", panel.transform,
            $"脅威度: {profile.ThreatLevel}  ({GetTierName(profile.ThreatLevel)})",
            18, BrandGuide.TextLabel);
        var threatRT = threatText.GetComponent<RectTransform>();
        threatRT.anchorMin = new Vector2(0.05f, 0.80f);
        threatRT.anchorMax = new Vector2(0.95f, 0.88f);
        threatRT.offsetMin = Vector2.zero;
        threatRT.offsetMax = Vector2.zero;

        // ボタンエリア
        var btnArea = new GameObject("BtnArea", typeof(RectTransform));
        btnArea.transform.SetParent(panel.transform, false);
        var btnAreaRT = btnArea.GetComponent<RectTransform>();
        btnAreaRT.anchorMin = new Vector2(0.1f, 0.10f);
        btnAreaRT.anchorMax = new Vector2(0.9f, 0.78f);
        btnAreaRT.offsetMin = Vector2.zero;
        btnAreaRT.offsetMax = Vector2.zero;

        var vlg = btnArea.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 12;
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;

        // セーブ
        var saveBtn = CreateBtn("セーブ", btnArea.transform, BrandGuide.BtnBuild);
        saveBtn.onClick.AddListener(() => ShowSlotPanel(true));

        // ロード
        var loadBtn = CreateBtn("ロード", btnArea.transform, BrandGuide.BtnUnit);
        loadBtn.onClick.AddListener(() => ShowSlotPanel(false));

        // タイトルへ戻る
        var titleBtn = CreateBtn("タイトルへ戻る", btnArea.transform, BrandGuide.BtnCancel);
        titleBtn.onClick.AddListener(OnReturnToTitle);

        // 閉じる
        var closeBtn = CreateBtn("閉じる", btnArea.transform, BrandGuide.BtnWait);
        closeBtn.onClick.AddListener(Close);
    }

    // ================================================================
    //  スロット選択パネル
    // ================================================================

    private void ShowSlotPanel(bool saveMode)
    {
        isSaveMode = saveMode;
        if (slotPanel != null) Destroy(slotPanel);

        slotPanel = new GameObject("SlotPanel", typeof(RectTransform));
        slotPanel.transform.SetParent(overlay.transform, false);
        slotPanel.transform.SetAsLastSibling();
        var rt = slotPanel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.2f, 0.20f);
        rt.anchorMax = new Vector2(0.8f, 0.80f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var bg = slotPanel.AddComponent<Image>();
        bg.color = BrandGuide.PanelBg;

        // ヘッダー
        string headerText = saveMode ? "セーブスロット選択" : "ロードスロット選択";
        var header = CreateTMP("SlotHeader", slotPanel.transform, headerText, 26, Color.white);
        var headerRT = header.GetComponent<RectTransform>();
        headerRT.anchorMin = new Vector2(0, 0.88f);
        headerRT.anchorMax = new Vector2(1, 1);
        headerRT.offsetMin = Vector2.zero;
        headerRT.offsetMax = Vector2.zero;

        // スロットボタン
        for (int i = 0; i < 3; i++)
        {
            int slot = i;
            string summary = SaveSystem.GetSlotSummary(slot);
            bool hasData = SaveSystem.HasSaveData(slot);

            Color btnColor;
            if (saveMode)
                btnColor = BrandGuide.BtnBuildEnabled;
            else
                btnColor = hasData ? BrandGuide.BtnUnit : BrandGuide.BtnWait;

            var btn = CreateSlotBtn($"スロット{i + 1}: {summary}", slotPanel.transform, btnColor);

            if (!saveMode) btn.interactable = hasData;

            var btnRT = btn.GetComponent<RectTransform>();
            float yMax = 0.82f - i * 0.22f;
            float yMin = yMax - 0.18f;
            btnRT.anchorMin = new Vector2(0.05f, yMin);
            btnRT.anchorMax = new Vector2(0.95f, yMax);
            btnRT.offsetMin = Vector2.zero;
            btnRT.offsetMax = Vector2.zero;

            btn.onClick.AddListener(() => OnSlotSelected(slot));
        }

        // 戻るボタン
        var backBtn = CreateSlotBtn("戻る", slotPanel.transform, BrandGuide.BtnCancel);
        var backRT = backBtn.GetComponent<RectTransform>();
        backRT.anchorMin = new Vector2(0.3f, 0.03f);
        backRT.anchorMax = new Vector2(0.7f, 0.13f);
        backRT.offsetMin = Vector2.zero;
        backRT.offsetMax = Vector2.zero;
        backBtn.onClick.AddListener(() => { Destroy(slotPanel); slotPanel = null; });
    }

    private void OnSlotSelected(int slot)
    {
        if (isSaveMode)
        {
            // セーブ実行
            if (turnGen != null && factionState != null)
            {
                var data = SaveSystem.CollectGameState(
                    turnGen, factionState,
                    turnGen.timerSystem,
                    turnGen.visionGenerator,
                    turnGen.aiCommander);
                SaveSystem.SaveGame(slot, data);
                ToastMessageUI.Show($"スロット{slot + 1}にセーブしました", ToastMessageUI.MessageType.Info, 3f);
            }
            Destroy(slotPanel);
            slotPanel = null;
        }
        else
        {
            // ロード実行 → シーン再読み込み
            var data = SaveSystem.LoadGame(slot);
            if (data != null)
            {
                // ロードフラグをstaticに保存（シーン遷移後に参照）
                PendingLoadSlot = slot;
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
        }
    }

    private void OnReturnToTitle()
    {
        // タイトル画面フラグを立ててシーン再読み込み
        ReturnToTitle = true;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // ================================================================
    //  Static フラグ（シーン遷移をまたぐ）
    // ================================================================

    /// <summary>ロード待ちのスロット番号（-1 = なし）</summary>
    public static int PendingLoadSlot = -1;

    /// <summary>タイトルへ戻る要求</summary>
    public static bool ReturnToTitle = false;

    // ================================================================
    //  ヘルパー
    // ================================================================

    private string GetTierName(int level)
    {
        if (level <= AIThreatLevel.TutorialEnd) return "チュートリアル帯";
        if (level <= AIThreatLevel.NormalEnd) return "ノーマル帯";
        if (level <= AIThreatLevel.HardEnd) return "ハード帯";
        return "やりこみ帯";
    }

    private TextMeshProUGUI CreateTMP(string name, Transform parent, string text, float size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;
        return tmp;
    }

    private Button CreateBtn(string label, Transform parent, Color bgColor)
    {
        var go = new GameObject(label + "Btn", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 50;

        var img = go.AddComponent<Image>();
        img.color = bgColor;

        var btn = go.AddComponent<Button>();
        BrandGuide.ApplyButtonStyle(btn, bgColor);

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, false);
        StretchFill(labelGo.GetComponent<RectTransform>());
        var tmp = labelGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 22;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = BrandGuide.TextPrimary;
        tmp.fontStyle = FontStyles.Bold;

        return btn;
    }

    private Button CreateSlotBtn(string label, Transform parent, Color bgColor)
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
        tmp.fontSize = 18;
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
