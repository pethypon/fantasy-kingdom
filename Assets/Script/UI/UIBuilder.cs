using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI をランタイムで生成し、ゲームシステムへ自動接続する。
/// GameGenerater.Awake() より先に実行する必要があるため ExecutionOrder を -100 に設定。
/// シーン上の任意の GameObject に付けるだけで動作する。
/// </summary>
[DefaultExecutionOrder(-100)]
public class UIBuilder : MonoBehaviour
{
    // ---- 生成済みUI参照（外部から取得可能） ----
    public UnitPanelUI UnitPanel { get; private set; }
    public SlidePanelUI SlidePanel { get; private set; }
    public TopBarUI TopBar { get; private set; }
    public APPanelUI APPanel { get; private set; }
    public ResourceBarUI ResourceBar { get; private set; }

    private Canvas canvas;
    private TMP_FontAsset defaultFont;

    private void Awake()
    {
        defaultFont = LoadDefaultFont();
        BuildCanvas();
        BuildTopBar();
        BuildResourceBar();
        BuildLeftMenu();
        BuildBottomUnitPanel();
        BuildAPPanel();
        WireToGameSystems();
    }

    // ==================================================================
    //  Canvas
    // ==================================================================
    private void BuildCanvas()
    {
        var go = new GameObject("UICanvas");
        go.transform.SetParent(transform);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        go.AddComponent<GraphicRaycaster>();
    }

    // ==================================================================
    //  TopBar (ターン表示)
    // ==================================================================
    private void BuildTopBar()
    {
        var bar = CreatePanel("TopBar", canvas.transform,
            new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
            new Vector2(0, 40));
        bar.anchoredPosition = Vector2.zero;

        AddImage(bar.gameObject, new Color(0.1f, 0.1f, 0.1f, 0.85f));

        var turnText = CreateTMP("TurnText", bar, "Turn 0", 24);
        StretchFill(turnText.GetComponent<RectTransform>());

        TopBar = bar.gameObject.AddComponent<TopBarUI>();
    }

    // ==================================================================
    //  ResourceBar (資源表示)
    // ==================================================================
    private void BuildResourceBar()
    {
        var bar = CreatePanel("ResourceBar", canvas.transform,
            new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
            new Vector2(0, 70));
        bar.anchoredPosition = new Vector2(0, -40);

        AddImage(bar.gameObject, new Color(0.12f, 0.12f, 0.12f, 0.75f));

        // Row1
        var row1 = CreatePanel("Row1", bar,
            new Vector2(0, 0.5f), new Vector2(1, 1), new Vector2(0.5f, 0.5f),
            Vector2.zero);
        StretchFill(row1);
        row1.anchorMin = new Vector2(0, 0.5f);
        row1.anchorMax = new Vector2(1, 1);
        row1.offsetMin = new Vector2(4, 0);
        row1.offsetMax = new Vector2(-4, 0);

        // Row2
        var row2 = CreatePanel("Row2", bar,
            new Vector2(0, 0), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero);
        StretchFill(row2);
        row2.anchorMin = new Vector2(0, 0);
        row2.anchorMax = new Vector2(1, 0.5f);
        row2.offsetMin = new Vector2(4, 0);
        row2.offsetMax = new Vector2(-4, 0);

        string[] row1Labels = { "Wood", "Stone", "Coal", "IronOre", "Iron", "MagicOre" };
        string[] row2Labels = { "Wheat", "Bread", "Water", "Plank", "CutStone", "Citizen" };

        var r1Texts = new TextMeshProUGUI[row1Labels.Length];
        var r2Texts = new TextMeshProUGUI[row2Labels.Length];

        for (int i = 0; i < row1Labels.Length; i++)
            r1Texts[i] = CreateTMP(row1Labels[i], row1, row1Labels[i] + ": 0", 18);
        for (int i = 0; i < row2Labels.Length; i++)
            r2Texts[i] = CreateTMP(row2Labels[i], row2, row2Labels[i] + ": 0", 18);

        ResourceBar = bar.gameObject.AddComponent<ResourceBarUI>();
    }

    // ==================================================================
    //  LeftMenu (建築 / ユニット生成 スライドパネル)
    // ==================================================================
    private void BuildLeftMenu()
    {
        // ---- LeftMenuRoot ----
        var root = CreatePanel("LeftMenuRoot", canvas.transform,
            new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f),
            new Vector2(200, 0));
        root.anchoredPosition = new Vector2(0, 0);
        StretchFill(root);
        root.anchorMin = new Vector2(0, 0);
        root.anchorMax = new Vector2(0, 1);
        root.offsetMin = new Vector2(0, 120);
        root.offsetMax = new Vector2(200, -120);

        // ---- 建築ボタン ----
        var buildBtn = CreateButton("BuildOpenButton", root, "建築", 18,
            new Color(0.2f, 0.45f, 0.2f, 1f));
        var buildBtnRT = buildBtn.GetComponent<RectTransform>();
        buildBtnRT.anchorMin = new Vector2(0, 1);
        buildBtnRT.anchorMax = new Vector2(1, 1);
        buildBtnRT.pivot = new Vector2(0.5f, 1);
        buildBtnRT.sizeDelta = new Vector2(0, 40);
        buildBtnRT.anchoredPosition = Vector2.zero;

        // ---- ユニット生成ボタン ----
        var unitBtn = CreateButton("UnitOpenButton", root, "Unit制作", 18,
            new Color(0.2f, 0.3f, 0.55f, 1f));
        var unitBtnRT = unitBtn.GetComponent<RectTransform>();
        unitBtnRT.anchorMin = new Vector2(0, 1);
        unitBtnRT.anchorMax = new Vector2(1, 1);
        unitBtnRT.pivot = new Vector2(0.5f, 1);
        unitBtnRT.sizeDelta = new Vector2(0, 40);
        unitBtnRT.anchoredPosition = new Vector2(0, -40);

        // ---- SlidePanel (スライドする本体) ----
        var panel = CreatePanel("SlidePanel", root,
            new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f),
            new Vector2(350, 0));
        panel.anchoredPosition = new Vector2(-350, 0);
        panel.anchorMin = new Vector2(0, 0);
        panel.anchorMax = new Vector2(0, 1);
        panel.offsetMin = new Vector2(-350, 0);
        panel.offsetMax = new Vector2(0, -80);
        panel.pivot = new Vector2(0, 0.5f);
        panel.anchoredPosition = new Vector2(-350, 0);

        AddImage(panel.gameObject, new Color(0.15f, 0.15f, 0.15f, 0.92f));

        // ---- Header ----
        var header = CreatePanel("Header", panel,
            new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
            new Vector2(0, 36));
        header.anchoredPosition = Vector2.zero;
        AddImage(header.gameObject, new Color(0.1f, 0.1f, 0.1f, 1f));

        var headerTitle = CreateTMP("Title", header, "メニュー", 20);
        StretchFill(headerTitle.GetComponent<RectTransform>());

        var closeBtn = CreateButton("CloseButton", header, "×", 22,
            new Color(0.6f, 0.15f, 0.15f, 1f));
        var closeBtnRT = closeBtn.GetComponent<RectTransform>();
        closeBtnRT.anchorMin = new Vector2(1, 0);
        closeBtnRT.anchorMax = new Vector2(1, 1);
        closeBtnRT.pivot = new Vector2(1, 0.5f);
        closeBtnRT.sizeDelta = new Vector2(36, 0);
        closeBtnRT.anchoredPosition = Vector2.zero;

        // ---- BuildScrollView ----
        var buildRoot = CreateScrollView("BuildScrollView", panel);
        var buildRootRT = buildRoot.GetComponent<RectTransform>();
        StretchFill(buildRootRT);
        buildRootRT.offsetMin = new Vector2(4, 4);
        buildRootRT.offsetMax = new Vector2(-4, -40);
        buildRoot.SetActive(false);

        // ---- UnitScrollView ----
        var unitRoot = CreateScrollView("UnitScrollView", panel);
        var unitRootRT = unitRoot.GetComponent<RectTransform>();
        StretchFill(unitRootRT);
        unitRootRT.offsetMin = new Vector2(4, 4);
        unitRootRT.offsetMax = new Vector2(-4, -40);
        unitRoot.SetActive(false);

        // ---- SlidePanelUI コンポーネントを追加 ----
        SlidePanel = panel.gameObject.AddComponent<SlidePanelUI>();

        // ---- ボタンイベント接続 ----
        buildBtn.onClick.AddListener(SlidePanel.ToggleBuildPanel);
        unitBtn.onClick.AddListener(SlidePanel.ToggleUnitPanel);
        closeBtn.onClick.AddListener(SlidePanel.ClosePanel);
    }

    // ==================================================================
    //  BottomUnitPanel (選択ユニット情報 + 行動ボタン)
    // ==================================================================
    private void BuildBottomUnitPanel()
    {
        var panel = CreatePanel("BottomUnitPanel", canvas.transform,
            new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0),
            new Vector2(700, 120));
        panel.anchoredPosition = new Vector2(0, 10);

        AddImage(panel.gameObject, new Color(0.08f, 0.08f, 0.12f, 0.92f));

        var cg = panel.gameObject.AddComponent<CanvasGroup>();

        // ---- LeftInfo ----
        var left = CreatePanel("LeftInfo", panel,
            new Vector2(0, 0), new Vector2(0.3f, 1), new Vector2(0, 0.5f),
            Vector2.zero);
        StretchFill(left);
        left.anchorMin = new Vector2(0, 0);
        left.anchorMax = new Vector2(0.3f, 1);
        left.offsetMin = new Vector2(10, 8);
        left.offsetMax = new Vector2(0, -8);

        var nameText = CreateTMP("NameText", left, "---", 22);
        SetAnchors(nameText, 0, 0.66f, 1, 1);
        var levelText = CreateTMP("LevelText", left, "Lv 1", 18);
        SetAnchors(levelText, 0, 0.33f, 1, 0.66f);
        var hpText = CreateTMP("HPText", left, "HP 0", 18);
        SetAnchors(hpText, 0, 0, 1, 0.33f);

        // ---- CenterStats ----
        var center = CreatePanel("CenterStats", panel,
            new Vector2(0.3f, 0), new Vector2(0.6f, 1), new Vector2(0.5f, 0.5f),
            Vector2.zero);
        StretchFill(center);
        center.anchorMin = new Vector2(0.3f, 0);
        center.anchorMax = new Vector2(0.6f, 1);
        center.offsetMin = new Vector2(0, 8);
        center.offsetMax = new Vector2(0, -8);

        var atkText = CreateTMP("ATKText", center, "ATK 0", 18);
        SetAnchors(atkText, 0, 0.66f, 1, 1);
        var defText = CreateTMP("DEFText", center, "DEF 0", 18);
        SetAnchors(defText, 0, 0.33f, 1, 0.66f);
        var kindText = CreateTMP("KindText", center, "", 16);
        SetAnchors(kindText, 0, 0, 0.5f, 0.33f);
        var passiveText = CreateTMP("PassiveText", center, "", 16);
        SetAnchors(passiveText, 0.5f, 0, 1, 0.33f);

        // ---- RightCommands ----
        var right = CreatePanel("RightCommands", panel,
            new Vector2(0.6f, 0), new Vector2(1, 1), new Vector2(1, 0.5f),
            Vector2.zero);
        StretchFill(right);
        right.anchorMin = new Vector2(0.6f, 0);
        right.anchorMax = new Vector2(1, 1);
        right.offsetMin = new Vector2(4, 8);
        right.offsetMax = new Vector2(-10, -8);

        var attackBtn = CreateButton("AttackBtn", right, "攻撃", 16,
            new Color(0.7f, 0.2f, 0.2f, 1));
        SetAnchors(attackBtn.GetComponent<RectTransform>(), 0, 0.5f, 0.5f, 1);
        var skillBtn = CreateButton("SkillBtn", right, "スキル", 16,
            new Color(0.2f, 0.35f, 0.7f, 1));
        SetAnchors(skillBtn.GetComponent<RectTransform>(), 0.5f, 0.5f, 1, 1);
        var waitBtn = CreateButton("WaitBtn", right, "待機", 16,
            new Color(0.4f, 0.4f, 0.4f, 1));
        SetAnchors(waitBtn.GetComponent<RectTransform>(), 0, 0, 0.5f, 0.5f);
        var cancelBtn = CreateButton("CancelBtn", right, "取消", 16,
            new Color(0.5f, 0.35f, 0.1f, 1));
        SetAnchors(cancelBtn.GetComponent<RectTransform>(), 0.5f, 0, 1, 0.5f);

        // ---- UnitPanelUI コンポーネント追加 ----
        UnitPanel = panel.gameObject.AddComponent<UnitPanelUI>();

        // ボタンイベント接続
        attackBtn.onClick.AddListener(UnitPanel.OnClickAttack);
        skillBtn.onClick.AddListener(UnitPanel.OnClickSkill);
        waitBtn.onClick.AddListener(UnitPanel.OnClickWait);
        cancelBtn.onClick.AddListener(UnitPanel.OnClickCancel);
    }

    // ==================================================================
    //  APPanel (右下 AP 表示)
    // ==================================================================
    private void BuildAPPanel()
    {
        var panel = CreatePanel("APPanel", canvas.transform,
            new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0),
            new Vector2(160, 80));
        panel.anchoredPosition = new Vector2(-20, 20);

        AddImage(panel.gameObject, new Color(0.1f, 0.1f, 0.2f, 0.85f));

        var apText = CreateTMP("APText", panel, "0 / 0", 28);
        StretchFill(apText.GetComponent<RectTransform>());

        APPanel = panel.gameObject.AddComponent<APPanelUI>();
    }

    // ==================================================================
    //  ゲームシステムへの自動接続
    // ==================================================================
    private void WireToGameSystems()
    {
        var turnGen = Object.FindFirstObjectByType<TurnGenerater>();
        if (turnGen != null && UnitPanel != null)
        {
            turnGen.unitPanelUI = UnitPanel;
        }

        var gameGen = Object.FindFirstObjectByType<GameGenerater>();
        if (gameGen != null)
        {
            // GameGenerater の UI フィールドに値を注入
            SetSerializedField(gameGen, "_APPanelUI", APPanel);
            SetSerializedField(gameGen, "_ResourceBarUI", ResourceBar);
        }
    }

    // ==================================================================
    //  ヘルパーメソッド
    // ==================================================================

    private RectTransform CreatePanel(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.sizeDelta = sizeDelta;
        return rt;
    }

    private RectTransform CreatePanel(string name, RectTransform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta)
    {
        return CreatePanel(name, (Transform)parent, anchorMin, anchorMax, pivot, sizeDelta);
    }

    private TextMeshProUGUI CreateTMP(string name, RectTransform parent,
        string text, float fontSize)
    {
        return CreateTMP(name, (Transform)parent, text, fontSize);
    }

    private TextMeshProUGUI CreateTMP(string name, Transform parent,
        string text, float fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        if (defaultFont != null) tmp.font = defaultFont;
        return tmp;
    }

    private Button CreateButton(string name, RectTransform parent,
        string label, float fontSize, Color bgColor)
    {
        return CreateButton(name, (Transform)parent, label, fontSize, bgColor);
    }

    private Button CreateButton(string name, Transform parent,
        string label, float fontSize, Color bgColor)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var img = go.AddComponent<Image>();
        img.color = bgColor;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        var colors = btn.colors;
        colors.highlightedColor = bgColor * 1.2f;
        colors.pressedColor = bgColor * 0.8f;
        btn.colors = colors;

        var txt = CreateTMP("Label", go.transform, label, fontSize);
        StretchFill(txt.GetComponent<RectTransform>());

        return btn;
    }

    private GameObject CreateScrollView(string name, RectTransform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        StretchFill(rt);

        var scrollImg = go.AddComponent<Image>();
        scrollImg.color = new Color(0.12f, 0.12f, 0.12f, 0.5f);

        // Viewport
        var viewport = new GameObject("Viewport", typeof(RectTransform));
        viewport.transform.SetParent(go.transform, false);
        var vpRT = viewport.GetComponent<RectTransform>();
        StretchFill(vpRT);
        var vpImg = viewport.AddComponent<Image>();
        vpImg.color = Color.white;
        var mask = viewport.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        // Content
        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        var contentRT = content.GetComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0, 1);
        contentRT.anchorMax = new Vector2(1, 1);
        contentRT.pivot = new Vector2(0.5f, 1);
        contentRT.sizeDelta = new Vector2(0, 400);

        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 4;
        vlg.padding = new RectOffset(4, 4, 4, 4);
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // ScrollRect
        var sr = go.AddComponent<ScrollRect>();
        sr.viewport = vpRT;
        sr.content = contentRT;
        sr.horizontal = false;
        sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;

        // プレースホルダーテキスト
        var placeholder = CreateTMP("Placeholder", content.transform, "(準備中)", 16);
        var phLE = placeholder.gameObject.AddComponent<LayoutElement>();
        phLE.preferredHeight = 30;

        return go;
    }

    private Image AddImage(GameObject go, Color color)
    {
        var img = go.GetComponent<Image>();
        if (img == null) img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    private void StretchFill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private void SetAnchors(RectTransform rt, float xMin, float yMin, float xMax, float yMax)
    {
        rt.anchorMin = new Vector2(xMin, yMin);
        rt.anchorMax = new Vector2(xMax, yMax);
        rt.offsetMin = new Vector2(2, 2);
        rt.offsetMax = new Vector2(-2, -2);
    }

    private void SetAnchors(TextMeshProUGUI tmp, float xMin, float yMin, float xMax, float yMax)
    {
        SetAnchors(tmp.GetComponent<RectTransform>(), xMin, yMin, xMax, yMax);
    }

    private TMP_FontAsset LoadDefaultFont()
    {
        // TMP のデフォルトフォントを読み込む
        var font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font == null)
            font = TMP_Settings.defaultFontAsset;
        return font;
    }

    private static void SetSerializedField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName,
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public);
        if (field != null)
            field.SetValue(target, value);
    }
}
