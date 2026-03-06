using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ラフデザインに基づくUI全体をランタイムで生成する。
/// GameSystem プレハブに付けて、Inspectorで参照を設定する。
/// </summary>
public class UIBuilder : MonoBehaviour
{
    // ====================================================
    //  Inspector 参照
    // ====================================================
    [Header("ゲームシステム参照")]
    [SerializeField] private TurnGenerater turnGenerater;
    [SerializeField] private APSystem apSystem;
    [SerializeField] private FactionState factionState;
    [SerializeField] private UnitClick unitClick;

    // ====================================================
    //  生成後のUI参照（外部からアクセス用）
    // ====================================================
    [HideInInspector] public TopBarUI topBarUI;
    [HideInInspector] public UnitPanelUI unitPanelUI;
    [HideInInspector] public SlidePanelUI slidePanelUI;
    [HideInInspector] public APPanelUI apPanelUI;

    // ====================================================
    //  色定義
    // ====================================================
    private static readonly Color PanelBg     = new Color(0.08f, 0.08f, 0.14f, 0.75f);
    private static readonly Color PanelBorder  = new Color(0.55f, 0.50f, 0.35f, 0.50f);
    private static readonly Color BtnNormal    = new Color(0.18f, 0.18f, 0.28f, 0.90f);
    private static readonly Color BtnHighlight = new Color(0.28f, 0.28f, 0.42f, 0.95f);
    private static readonly Color BtnPress     = new Color(0.12f, 0.12f, 0.20f, 1.00f);
    private static readonly Color BtnDisabled  = new Color(0.15f, 0.15f, 0.15f, 0.60f);
    private static readonly Color TextWhite    = Color.white;
    private static readonly Color APColor      = new Color(1f, 0.85f, 0.2f, 1f);

    // ====================================================
    //  公開メソッド: UI生成
    // ====================================================
    public void BuildUI()
    {
        // Canvas
        GameObject canvasObj = CreateCanvas();
        RectTransform root = CreatePanel(canvasObj.transform, "SafeAreaRoot",
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;

        // 各パネル生成
        BuildTopBar(root);
        BuildLeftMenu(root);
        BuildBottomUnitPanel(root);
        BuildAPPanel(root);
    }

    // ====================================================
    //  Canvas
    // ====================================================
    private GameObject CreateCanvas()
    {
        GameObject obj = new GameObject("GameUICanvas");
        Canvas canvas = obj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        CanvasScaler scaler = obj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        obj.AddComponent<GraphicRaycaster>();
        return obj;
    }

    // ====================================================
    //  上バー: 国ステータス / ターン / 制限時間 / メニュー
    // ====================================================
    private void BuildTopBar(RectTransform parent)
    {
        // 背景パネル (Top Stretch, Height=90)
        RectTransform bar = CreatePanel(parent, "TopBar",
            new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1f), Vector2.zero);
        bar.offsetMin = new Vector2(0, -90);
        bar.offsetMax = Vector2.zero;
        bar.GetComponent<Image>().color = PanelBg;

        // 枠線
        AddBorder(bar);

        // HorizontalLayoutGroup
        HorizontalLayoutGroup hlg = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.spacing = 30;
        hlg.padding = new RectOffset(20, 20, 8, 8);
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;
        hlg.childControlWidth = false;
        hlg.childControlHeight = true;

        // 国ステータス (資源サマリー)
        RectTransform nationBox = CreateChildBox(bar, "NationStatus", 400, 70);
        HorizontalLayoutGroup nhlg = nationBox.gameObject.AddComponent<HorizontalLayoutGroup>();
        nhlg.spacing = 12;
        nhlg.childAlignment = TextAnchor.MiddleLeft;
        nhlg.childForceExpandWidth = false;
        nhlg.childForceExpandHeight = true;
        nhlg.childControlWidth = false;
        nhlg.childControlHeight = true;

        CreateLabel(nationBox, "WoodLabel",   "木:100",  80, TextWhite, 18);
        CreateLabel(nationBox, "StoneLabel",  "石:100",  80, TextWhite, 18);
        CreateLabel(nationBox, "IronLabel",   "鉄:0",    70, TextWhite, 18);
        CreateLabel(nationBox, "BreadLabel",  "パン:60", 90, TextWhite, 18);
        CreateLabel(nationBox, "CitizenLabel","市民:5",  80, TextWhite, 18);

        // ターン表示
        TextMeshProUGUI turnText = CreateLabel(bar, "TurnText", "Turn 1", 140, TextWhite, 26);
        turnText.alignment = TextAlignmentOptions.Center;

        // 制限時間
        TextMeshProUGUI timerText = CreateLabel(bar, "TimerText", "00:00", 110, TextWhite, 22);
        timerText.alignment = TextAlignmentOptions.Center;

        // AP表示 (上バー内にも簡易表示)
        TextMeshProUGUI apText = CreateLabel(bar, "TopAPText", "AP 15", 100, APColor, 24);
        apText.alignment = TextAlignmentOptions.Center;
        apText.fontStyle = FontStyles.Bold;

        // メニューボタン
        CreateButton(bar, "MenuBtn", "メニュー", 120, 50, null);

        // TopBarUI スクリプトを付ける
        TopBarUI tbui = bar.gameObject.AddComponent<TopBarUI>();
        SetPrivateField(tbui, "turnText", turnText);
        SetPrivateField(tbui, "apText", apText);
        SetPrivateField(tbui, "turnGenerater", turnGenerater);
        SetPrivateField(tbui, "apSystem", apSystem);
        topBarUI = tbui;
    }

    // ====================================================
    //  左メニュー: 建築 / ユニット制作 のスライドパネル
    // ====================================================
    private void BuildLeftMenu(RectTransform parent)
    {
        // タブボタン列 (Left Stretch, Width=100)
        RectTransform tabColumn = CreatePanel(parent, "LeftMenuTabs",
            new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero);
        tabColumn.offsetMin = new Vector2(0, 100);
        tabColumn.offsetMax = new Vector2(100, -100);
        tabColumn.GetComponent<Image>().color = new Color(0, 0, 0, 0); // 透明

        VerticalLayoutGroup vlg = tabColumn.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 10;
        vlg.padding = new RectOffset(5, 5, 10, 10);
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;

        // スライドパネル本体 (左端、幅350、初期は画面外)
        RectTransform slidePanel = CreatePanel(parent, "SlidePanel",
            new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero);
        slidePanel.offsetMin = new Vector2(100, 100);
        slidePanel.offsetMax = new Vector2(450, -100);
        slidePanel.GetComponent<Image>().color = PanelBg;
        AddBorder(slidePanel);

        // スライドパネルの中身
        // ヘッダー
        RectTransform header = CreatePanel(slidePanel, "SlideHeader",
            new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero);
        header.offsetMin = new Vector2(0, -50);
        header.offsetMax = Vector2.zero;
        header.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.22f, 0.9f);

        TextMeshProUGUI headerTitle = CreateTMP(header, "HeaderTitle", "建築", 24);
        headerTitle.alignment = TextAlignmentOptions.Center;
        RectTransform titleRT = headerTitle.GetComponent<RectTransform>();
        SetStretch(titleRT);

        // 閉じるボタン
        RectTransform closeBtn = CreateButton(header, "CloseBtn", "×", 40, 40, null);
        RectTransform closeBtnRT = closeBtn.GetComponent<RectTransform>();
        closeBtnRT.anchorMin = new Vector2(1, 0.5f);
        closeBtnRT.anchorMax = new Vector2(1, 0.5f);
        closeBtnRT.pivot = new Vector2(1, 0.5f);
        closeBtnRT.anchoredPosition = new Vector2(-5, 0);

        // 建築一覧
        RectTransform buildRoot = CreateScrollArea(slidePanel, "BuildScrollView", 50);
        CreateBuildItems(buildRoot.Find("Content") as RectTransform);

        // ユニット生成一覧
        RectTransform unitRoot = CreateScrollArea(slidePanel, "UnitScrollView", 50);
        CreateUnitItems(unitRoot.Find("Content") as RectTransform);
        unitRoot.gameObject.SetActive(false);

        // SlidePanelUI スクリプト
        SlidePanelUI spui = slidePanel.gameObject.AddComponent<SlidePanelUI>();
        SetPrivateField(spui, "panelRect", slidePanel);
        SetPrivateField(spui, "buildRoot", buildRoot.gameObject);
        SetPrivateField(spui, "unitRoot", unitRoot.gameObject);
        SetPrivateField(spui, "closedX", -350f);
        SetPrivateField(spui, "openX", 100f);
        SetPrivateField(spui, "slideSpeed", 12f);
        slidePanelUI = spui;

        // タブボタン作成 → SlidePanelUI に接続
        RectTransform buildTabBtn = CreateButton(tabColumn, "BuildTab", "建築", 90, 70, null);
        buildTabBtn.GetComponent<Button>().onClick.AddListener(() => spui.ToggleBuildPanel());

        RectTransform unitTabBtn = CreateButton(tabColumn, "UnitTab", "制作", 90, 70, null);
        unitTabBtn.GetComponent<Button>().onClick.AddListener(() => spui.ToggleUnitPanel());

        // 閉じるボタン接続
        closeBtn.GetComponent<Button>().onClick.AddListener(() => spui.ClosePanel());
    }

    // ====================================================
    //  下ユニットパネル: ユニットクリック時に出る
    // ====================================================
    private void BuildBottomUnitPanel(RectTransform parent)
    {
        // 背景パネル (Bottom Center)
        RectTransform panel = CreatePanel(parent, "BottomUnitPanel",
            new Vector2(0.15f, 0), new Vector2(0.85f, 0), new Vector2(0.5f, 0), Vector2.zero);
        panel.offsetMin = new Vector2(0, 0);
        panel.offsetMax = new Vector2(0, 220);
        panel.GetComponent<Image>().color = PanelBg;
        AddBorder(panel);

        // CanvasGroup (表示/非表示用)
        CanvasGroup cg = panel.gameObject.AddComponent<CanvasGroup>();

        // HorizontalLayoutGroup で3分割
        HorizontalLayoutGroup hlg = panel.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10;
        hlg.padding = new RectOffset(15, 15, 10, 10);
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;

        // === 左: 基本情報 ===
        RectTransform leftInfo = CreateChildBox(panel, "LeftInfo", 0, 0);
        leftInfo.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1.2f;
        VerticalLayoutGroup leftVlg = leftInfo.gameObject.AddComponent<VerticalLayoutGroup>();
        leftVlg.spacing = 4;
        leftVlg.childAlignment = TextAnchor.MiddleCenter;
        leftVlg.childForceExpandHeight = false;
        leftVlg.childControlHeight = false;
        leftVlg.childForceExpandWidth = true;
        leftVlg.childControlWidth = true;

        // アイコン代わりの丸枠
        RectTransform iconBg = CreateChildBox(leftInfo, "UnitIcon", 80, 80);
        iconBg.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.35f, 0.8f);

        TextMeshProUGUI nameText = CreateLabel(leftInfo, "NameText", "---", 0, TextWhite, 24);
        nameText.alignment = TextAlignmentOptions.Center;
        nameText.GetComponent<LayoutElement>().preferredHeight = 30;

        TextMeshProUGUI levelText = CreateLabel(leftInfo, "LevelText", "Lv 1", 0, TextWhite, 20);
        levelText.alignment = TextAlignmentOptions.Center;
        levelText.GetComponent<LayoutElement>().preferredHeight = 26;

        TextMeshProUGUI hpText = CreateLabel(leftInfo, "HPText", "HP --", 0,
            new Color(0.3f, 1f, 0.3f), 20);
        hpText.alignment = TextAlignmentOptions.Center;
        hpText.GetComponent<LayoutElement>().preferredHeight = 26;

        // === 中央: ステータス ===
        RectTransform centerStats = CreateChildBox(panel, "CenterStats", 0, 0);
        centerStats.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        VerticalLayoutGroup centerVlg = centerStats.gameObject.AddComponent<VerticalLayoutGroup>();
        centerVlg.spacing = 4;
        centerVlg.childAlignment = TextAnchor.MiddleLeft;
        centerVlg.childForceExpandHeight = false;
        centerVlg.childControlHeight = false;
        centerVlg.childForceExpandWidth = true;
        centerVlg.childControlWidth = true;

        TextMeshProUGUI atkText = CreateLabel(centerStats, "ATKText", "ATK --", 0, TextWhite, 20);
        atkText.GetComponent<LayoutElement>().preferredHeight = 28;
        TextMeshProUGUI defText = CreateLabel(centerStats, "DEFText", "DEF --", 0, TextWhite, 20);
        defText.GetComponent<LayoutElement>().preferredHeight = 28;
        TextMeshProUGUI kindText = CreateLabel(centerStats, "KindText", "---", 0, TextWhite, 18);
        kindText.GetComponent<LayoutElement>().preferredHeight = 26;
        TextMeshProUGUI passiveText = CreateLabel(centerStats, "PassiveText", "", 0,
            new Color(0.8f, 0.7f, 1f), 16);
        passiveText.GetComponent<LayoutElement>().preferredHeight = 24;

        // === 右: 行動ボタン ===
        RectTransform rightCmds = CreateChildBox(panel, "RightCommands", 0, 0);
        rightCmds.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        GridLayoutGroup glg = rightCmds.gameObject.AddComponent<GridLayoutGroup>();
        glg.cellSize = new Vector2(120, 42);
        glg.spacing = new Vector2(8, 6);
        glg.startCorner = GridLayoutGroup.Corner.UpperLeft;
        glg.startAxis = GridLayoutGroup.Axis.Horizontal;
        glg.childAlignment = TextAnchor.MiddleCenter;
        glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        glg.constraintCount = 2;

        RectTransform attackBtn = CreateButton(rightCmds, "AttackBtn", "攻撃", 120, 42, null);
        RectTransform skillBtn  = CreateButton(rightCmds, "SkillBtn",  "スキル", 120, 42, null);
        RectTransform waitBtn   = CreateButton(rightCmds, "WaitBtn",   "待機", 120, 42, null);
        RectTransform cancelBtn = CreateButton(rightCmds, "CancelBtn", "戻る", 120, 42, null);

        // UnitPanelUI スクリプト
        UnitPanelUI upui = panel.gameObject.AddComponent<UnitPanelUI>();
        SetPrivateField(upui, "nameText", nameText);
        SetPrivateField(upui, "levelText", levelText);
        SetPrivateField(upui, "hpText", hpText);
        SetPrivateField(upui, "atkText", atkText);
        SetPrivateField(upui, "defText", defText);
        SetPrivateField(upui, "kindText", kindText);
        SetPrivateField(upui, "passiveText", passiveText);
        SetPrivateField(upui, "attackButton", attackBtn.GetComponent<Button>());
        SetPrivateField(upui, "skillButton", skillBtn.GetComponent<Button>());
        SetPrivateField(upui, "waitButton", waitBtn.GetComponent<Button>());
        SetPrivateField(upui, "cancelButton", cancelBtn.GetComponent<Button>());
        SetPrivateField(upui, "turnGenerater", turnGenerater);
        SetPrivateField(upui, "apSystem", apSystem);
        SetPrivateField(upui, "canvasGroup", cg);
        unitPanelUI = upui;

        // ボタン接続
        attackBtn.GetComponent<Button>().onClick.AddListener(() => upui.OnClickAttack());
        skillBtn.GetComponent<Button>().onClick.AddListener(() => upui.OnClickSkill());
        waitBtn.GetComponent<Button>().onClick.AddListener(() => upui.OnClickWait());
        cancelBtn.GetComponent<Button>().onClick.AddListener(() => upui.OnClickCancel());
    }

    // ====================================================
    //  右下AP: 円形表示
    // ====================================================
    private void BuildAPPanel(RectTransform parent)
    {
        // 背景 (Bottom Right, 130x130)
        RectTransform apPanel = CreatePanel(parent, "APPanel",
            new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), Vector2.zero);
        apPanel.sizeDelta = new Vector2(130, 130);
        apPanel.anchoredPosition = new Vector2(-30, 30);
        apPanel.GetComponent<Image>().color = new Color(0.06f, 0.06f, 0.12f, 0.85f);

        // 円形背景
        RectTransform backCircle = CreatePanel(apPanel, "BackCircle",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero);
        backCircle.offsetMin = new Vector2(5, 5);
        backCircle.offsetMax = new Vector2(-5, -5);
        Image backImg = backCircle.GetComponent<Image>();
        backImg.color = new Color(0.15f, 0.15f, 0.25f, 0.9f);

        // Fillゲージ
        RectTransform fillCircle = CreatePanel(apPanel, "FillCircle",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero);
        fillCircle.offsetMin = new Vector2(5, 5);
        fillCircle.offsetMax = new Vector2(-5, -5);
        Image fillImg = fillCircle.GetComponent<Image>();
        fillImg.color = APColor;
        fillImg.type = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Radial360;
        fillImg.fillOrigin = (int)Image.Origin360.Top;
        fillImg.fillClockwise = true;
        fillImg.fillAmount = 1f;

        // APラベル
        TextMeshProUGUI apLabel = CreateTMP(apPanel, "APLabel", "AP", 16);
        apLabel.alignment = TextAlignmentOptions.Center;
        apLabel.color = new Color(0.8f, 0.8f, 0.8f);
        RectTransform labelRT = apLabel.GetComponent<RectTransform>();
        labelRT.anchorMin = new Vector2(0, 0.55f);
        labelRT.anchorMax = new Vector2(1, 0.8f);
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;

        // AP数値
        TextMeshProUGUI apText = CreateTMP(apPanel, "APValueText", "15 / 15", 22);
        apText.alignment = TextAlignmentOptions.Center;
        apText.color = TextWhite;
        apText.fontStyle = FontStyles.Bold;
        RectTransform valRT = apText.GetComponent<RectTransform>();
        valRT.anchorMin = new Vector2(0, 0.2f);
        valRT.anchorMax = new Vector2(1, 0.55f);
        valRT.offsetMin = Vector2.zero;
        valRT.offsetMax = Vector2.zero;

        // APPanelUI スクリプト
        APPanelUI apui = apPanel.gameObject.AddComponent<APPanelUI>();
        SetPrivateField(apui, "apText", apText);
        SetPrivateField(apui, "fillImage", fillImg);
        SetPrivateField(apui, "factionState", factionState);
        apPanelUI = apui;
    }

    // ====================================================
    //  建築項目の仮データ
    // ====================================================
    private void CreateBuildItems(RectTransform content)
    {
        string[] buildings = { "畑", "パン屋", "伐採所", "製材所", "採石場", "石切り場",
                               "鉱山", "製錬所", "兵舎", "家", "井戸", "倉庫", "木の壁", "石の壁" };
        foreach (string name in buildings)
        {
            CreateListItem(content, name);
        }
    }

    private void CreateUnitItems(RectTransform content)
    {
        string[] units = { "Knight", "Archer", "Magic", "Assassin", "Scout",
                           "Priest", "Guardian", "Crossbow", "Magicsniper", "Bomber" };
        foreach (string name in units)
        {
            CreateListItem(content, name);
        }
    }

    private void CreateListItem(RectTransform parent, string label)
    {
        RectTransform item = CreateButton(parent, label + "Item", label, 0, 50, null);
        LayoutElement le = item.gameObject.AddComponent<LayoutElement>();
        le.minHeight = 50;
        le.flexibleWidth = 1;
    }

    // ====================================================
    //  ScrollView 生成
    // ====================================================
    private RectTransform CreateScrollArea(RectTransform parent, string name, float topOffset)
    {
        GameObject scrollObj = new GameObject(name);
        scrollObj.transform.SetParent(parent, false);
        RectTransform scrollRT = scrollObj.AddComponent<RectTransform>();
        scrollRT.anchorMin = new Vector2(0, 0);
        scrollRT.anchorMax = new Vector2(1, 1);
        scrollRT.offsetMin = new Vector2(5, 5);
        scrollRT.offsetMax = new Vector2(-5, -topOffset);

        Image scrollBg = scrollObj.AddComponent<Image>();
        scrollBg.color = new Color(0, 0, 0, 0.01f);
        scrollBg.raycastTarget = true;

        ScrollRect sr = scrollObj.AddComponent<ScrollRect>();
        sr.horizontal = false;
        sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;

        // Viewport
        GameObject viewportObj = new GameObject("Viewport");
        viewportObj.transform.SetParent(scrollObj.transform, false);
        RectTransform viewportRT = viewportObj.AddComponent<RectTransform>();
        SetStretch(viewportRT);
        viewportObj.AddComponent<Image>().color = Color.clear;
        viewportObj.AddComponent<RectMask2D>();
        sr.viewport = viewportRT;

        // Content
        GameObject contentObj = new GameObject("Content");
        contentObj.transform.SetParent(viewportObj.transform, false);
        RectTransform contentRT = contentObj.AddComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0, 1);
        contentRT.anchorMax = new Vector2(1, 1);
        contentRT.pivot = new Vector2(0.5f, 1);
        contentRT.sizeDelta = new Vector2(0, 0);

        VerticalLayoutGroup clg = contentObj.AddComponent<VerticalLayoutGroup>();
        clg.spacing = 4;
        clg.padding = new RectOffset(4, 4, 4, 4);
        clg.childForceExpandWidth = true;
        clg.childForceExpandHeight = false;
        clg.childControlWidth = true;
        clg.childControlHeight = false;

        ContentSizeFitter csf = contentObj.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        sr.content = contentRT;

        return scrollRT;
    }

    // ====================================================
    //  ヘルパー: パネル生成
    // ====================================================
    private RectTransform CreatePanel(Transform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;

        Image img = obj.AddComponent<Image>();
        img.color = Color.clear;
        img.raycastTarget = false;

        return rt;
    }

    private RectTransform CreateChildBox(RectTransform parent, string name, float width, float height)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, height);

        Image img = obj.AddComponent<Image>();
        img.color = Color.clear;
        img.raycastTarget = false;

        return rt;
    }

    // ====================================================
    //  ヘルパー: テキスト生成
    // ====================================================
    private TextMeshProUGUI CreateTMP(RectTransform parent, string name, string text, float fontSize)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(200, 30);

        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = TextWhite;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.raycastTarget = false;

        return tmp;
    }

    private TextMeshProUGUI CreateLabel(RectTransform parent, string name, string text,
        float width, Color color, float fontSize)
    {
        TextMeshProUGUI tmp = CreateTMP(parent, name, text, fontSize);
        tmp.color = color;
        RectTransform rt = tmp.GetComponent<RectTransform>();
        if (width > 0)
        {
            rt.sizeDelta = new Vector2(width, 30);
            LayoutElement le = tmp.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
        }
        return tmp;
    }

    // ====================================================
    //  ヘルパー: ボタン生成
    // ====================================================
    private RectTransform CreateButton(RectTransform parent, string name, string label,
        float width, float height, System.Action onClick)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, height);

        Image img = obj.AddComponent<Image>();
        img.color = BtnNormal;

        Button btn = obj.AddComponent<Button>();
        ColorBlock cb = btn.colors;
        cb.normalColor = BtnNormal;
        cb.highlightedColor = BtnHighlight;
        cb.pressedColor = BtnPress;
        cb.disabledColor = BtnDisabled;
        cb.fadeDuration = 0.1f;
        btn.colors = cb;
        btn.targetGraphic = img;

        if (onClick != null)
            btn.onClick.AddListener(() => onClick());

        // ボタンラベル
        TextMeshProUGUI tmp = CreateTMP(rt, name + "Label", label, 18);
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        SetStretch(tmp.GetComponent<RectTransform>());

        return rt;
    }

    // ====================================================
    //  ヘルパー: 枠線
    // ====================================================
    private void AddBorder(RectTransform parent)
    {
        GameObject borderObj = new GameObject("Border");
        borderObj.transform.SetParent(parent, false);
        RectTransform brt = borderObj.AddComponent<RectTransform>();
        SetStretch(brt);

        Outline outline = borderObj.AddComponent<Outline>();
        outline.effectColor = PanelBorder;
        outline.effectDistance = new Vector2(2, -2);

        Image borderImg = borderObj.AddComponent<Image>();
        borderImg.color = Color.clear;
        borderImg.raycastTarget = false;
    }

    // ====================================================
    //  ヘルパー: RectTransform操作
    // ====================================================
    private void SetStretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // ====================================================
    //  ヘルパー: SerializeField にリフレクションで値を設定
    // ====================================================
    private void SetPrivateField(object target, string fieldName, object value)
    {
        System.Type type = target.GetType();
        System.Reflection.FieldInfo fi = type.GetField(fieldName,
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);
        if (fi != null)
            fi.SetValue(target, value);
        else
            Debug.LogWarning($"[UIBuilder] フィールドが見つかりません: {type.Name}.{fieldName}");
    }
}
