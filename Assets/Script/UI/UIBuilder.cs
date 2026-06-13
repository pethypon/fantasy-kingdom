using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI をランタイムで生成し、ゲームシステムへ自動接続する。
/// GameGenerator.Awake() より先に実行する必要があるため ExecutionOrder を -100 に設定。
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
    public BuildSystem BuildSystem { get; private set; }
    public SummonSystem SummonSystem { get; private set; }

    private Canvas canvas;
    private TMP_FontAsset defaultFont;

    // 建築・召喚UIの生成とボタン管理を委譲
    private BuildSummonUIBuilder buildSummonUI;

    private void Awake()
    {
        defaultFont = LoadDefaultFont();
        SetAsTMPFallback(defaultFont);
        buildSummonUI = new BuildSummonUIBuilder(defaultFont);
        BuildCanvas();
        BuildTopBar();        // 1段バー: 資源(左) + ターン(中) + 制限時間・メニュー(右)
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
        // ScreenSpaceOverlay Canvas はルートに置く（親の Transform に影響されないようにする）
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
    //  TopBar — 1段バー: 資源(左) | ターン(中央) | 制限時間+メニュー(右)
    // ==================================================================
    private void BuildTopBar()
    {
        var bar = CreatePanel("TopBar", canvas.transform,
            new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
            new Vector2(0, 120));
        bar.anchoredPosition = Vector2.zero;

        AddImage(bar.gameObject, BrandGuide.PanelBg);
        BrandGuide.AddBottomBorder(bar);

        // ============ 左: 資源エリア (0% ~ 42%) ============
        var resArea = CreatePanel("ResourceBar", bar,
            new Vector2(0, 0), new Vector2(0.42f, 1), new Vector2(0, 0.5f),
            Vector2.zero);
        StretchFill(resArea);
        resArea.anchorMin = new Vector2(0, 0);
        resArea.anchorMax = new Vector2(0.42f, 1);
        resArea.offsetMin = new Vector2(8, 0);
        resArea.offsetMax = new Vector2(-4, 0);

        // 資源を2行で配置
        var row1 = CreatePanel("Row1", resArea,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero);
        StretchFill(row1);
        row1.anchorMin = new Vector2(0, 0.5f);
        row1.anchorMax = new Vector2(1, 1);
        row1.offsetMin = new Vector2(2, 4);
        row1.offsetMax = new Vector2(-2, -4);
        AddHorizontalLayout(row1.gameObject, 4);

        var row2 = CreatePanel("Row2", resArea,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero);
        StretchFill(row2);
        row2.anchorMin = new Vector2(0, 0);
        row2.anchorMax = new Vector2(1, 0.5f);
        row2.offsetMin = new Vector2(2, 4);
        row2.offsetMax = new Vector2(-2, -4);
        AddHorizontalLayout(row2.gameObject, 4);

        // カテゴリ別の背景色（BrandGuide から参照）
        Color rawC  = BrandGuide.ResCatRaw;
        Color minC  = BrandGuide.ResCatMineral;
        Color proC  = BrandGuide.ResCatProcessed;
        Color fooC  = BrandGuide.ResCatFood;
        Color popC  = BrandGuide.ResCatPopulation;

        string[] r1Names  = { "Wood",  "Stone", "Iron",     "MagicOre" };
        Color[]  r1Colors = { rawC,    rawC,    proC,       proC };
        Color scC  = BrandGuide.ResCatCrystal;
        string[] r2Names  = { "Wheat", "Bread", "Water",  "Citizen", "SubCrystal" };
        Color[]  r2Colors = { fooC,    fooC,    rawC,     popC,      scC };

        for (int i = 0; i < r1Names.Length; i++)
            CreateResourceCell(r1Names[i], row1, r1Colors[i]);
        for (int i = 0; i < r2Names.Length; i++)
            CreateResourceCell(r2Names[i], row2, r2Colors[i]);

        ResourceBar = resArea.gameObject.AddComponent<ResourceBarUI>();

        // ============ 区切り線 ============
        var sep1 = new GameObject("Sep1", typeof(RectTransform));
        sep1.transform.SetParent(bar, false);
        var sep1Img = sep1.AddComponent<Image>();
        sep1Img.color = BrandGuide.BorderLight;
        var sep1RT = sep1.GetComponent<RectTransform>();
        sep1RT.anchorMin = new Vector2(0.42f, 0);
        sep1RT.anchorMax = new Vector2(0.42f, 1);
        sep1RT.pivot = new Vector2(0.5f, 0.5f);
        sep1RT.sizeDelta = new Vector2(1, 0);
        sep1RT.offsetMin = new Vector2(-0.5f, 8);
        sep1RT.offsetMax = new Vector2(0.5f, -8);

        // ============ 中央: ターン表示 (42% ~ 56%) ============
        var turnArea = CreatePanel("TurnArea", bar,
            new Vector2(0.42f, 0), new Vector2(0.56f, 1), new Vector2(0.5f, 0.5f),
            Vector2.zero);
        StretchFill(turnArea);
        turnArea.anchorMin = new Vector2(0.42f, 0);
        turnArea.anchorMax = new Vector2(0.56f, 1);
        turnArea.offsetMin = new Vector2(10, 4);
        turnArea.offsetMax = new Vector2(-4, -4);

        // ターン内部をHorizontalLayoutGroupで中央揃え
        var turnHLG = turnArea.gameObject.AddComponent<HorizontalLayoutGroup>();
        turnHLG.childAlignment = TextAnchor.MiddleCenter;
        turnHLG.spacing = 8;
        turnHLG.childForceExpandWidth = false;
        turnHLG.childForceExpandHeight = false;
        turnHLG.childControlWidth = false;
        turnHLG.childControlHeight = false;

        // ターンアイコン（丸）
        var iconGo = new GameObject("TurnIcon", typeof(RectTransform));
        iconGo.transform.SetParent(turnArea, false);
        var iconImg = iconGo.AddComponent<Image>();
        iconImg.color = BrandGuide.Primary;
        var iconRT = iconGo.GetComponent<RectTransform>();
        iconRT.sizeDelta = new Vector2(24, 24);
        var iconLE = iconGo.AddComponent<LayoutElement>();
        iconLE.preferredWidth = 24;
        iconLE.preferredHeight = 24;

        // ターンテキスト
        var turnText = CreateTMP("TurnText", turnArea, "Turn 0", BrandGuide.FontHeader);
        turnText.color = BrandGuide.TextPrimary;
        turnText.fontStyle = FontStyles.Bold;
        turnText.alignment = TextAlignmentOptions.MidlineLeft;
        var turnTextLE = turnText.gameObject.AddComponent<LayoutElement>();
        turnTextLE.preferredWidth = 100;
        turnTextLE.preferredHeight = 40;

        // ============ 区切り線 ============
        var sep2 = new GameObject("Sep2", typeof(RectTransform));
        sep2.transform.SetParent(bar, false);
        var sep2Img = sep2.AddComponent<Image>();
        sep2Img.color = BrandGuide.BorderLight;
        var sep2RT = sep2.GetComponent<RectTransform>();
        sep2RT.anchorMin = new Vector2(0.56f, 0);
        sep2RT.anchorMax = new Vector2(0.56f, 1);
        sep2RT.pivot = new Vector2(0.5f, 0.5f);
        sep2RT.sizeDelta = new Vector2(1, 0);
        sep2RT.offsetMin = new Vector2(-0.5f, 8);
        sep2RT.offsetMax = new Vector2(0.5f, -8);

        // ============ 右: 制限時間 + メニュー (56% ~ 100%) ============
        var rightArea = CreatePanel("RightArea", bar,
            new Vector2(0.56f, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f),
            Vector2.zero);
        StretchFill(rightArea);
        rightArea.anchorMin = new Vector2(0.56f, 0);
        rightArea.anchorMax = new Vector2(1, 1);
        rightArea.offsetMin = new Vector2(6, 4);
        rightArea.offsetMax = new Vector2(-6, -4);

        // 右エリア内を HorizontalLayoutGroup で右寄せ
        // （中央寄せだとメニューが右端に届かず浮いて見えるため、資源の左詰めと対に揃える）
        var rightHLG = rightArea.gameObject.AddComponent<HorizontalLayoutGroup>();
        rightHLG.childAlignment = TextAnchor.MiddleRight;
        rightHLG.spacing = 12;
        rightHLG.childForceExpandWidth = false;
        rightHLG.childForceExpandHeight = false;
        rightHLG.childControlWidth = false;
        rightHLG.childControlHeight = false;

        // 制限時間バー
        var timerArea = new GameObject("TimerArea", typeof(RectTransform));
        timerArea.transform.SetParent(rightArea, false);
        var timerAreaRT = timerArea.GetComponent<RectTransform>();
        timerAreaRT.sizeDelta = new Vector2(300, 44);

        // バー背景
        var timerBg = new GameObject("TimerBg", typeof(RectTransform));
        timerBg.transform.SetParent(timerArea.transform, false);
        timerBg.AddComponent<Image>().color = BrandGuide.TimerBg;
        StretchFill(timerBg.GetComponent<RectTransform>());

        // バー本体
        var timerFill = new GameObject("TimerFill", typeof(RectTransform));
        timerFill.transform.SetParent(timerArea.transform, false);
        timerFill.AddComponent<Image>().color = BrandGuide.TimerNormal;
        var fillRT = timerFill.GetComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = new Vector2(2, 2);
        fillRT.offsetMax = new Vector2(-2, -2);

        // 制限時間テキスト
        var timerText = CreateTMP("TimerText", timerArea.transform, "制限時間", BrandGuide.FontBody);
        timerText.fontStyle = FontStyles.Bold;
        timerText.alignment = TextAlignmentOptions.Center;
        StretchFill(timerText.GetComponent<RectTransform>());

        // メニューボタン
        var menuBtn = CreateButton("MenuButton", rightArea, "メニュー", BrandGuide.FontBody,
            BrandGuide.BtnMenu);
        var menuBtnRT = menuBtn.GetComponent<RectTransform>();
        menuBtnRT.sizeDelta = new Vector2(120, 44);
        menuBtn.onClick.AddListener(() =>
        {
            if (GameMenuUI.Instance != null)
                GameMenuUI.Instance.Toggle();
        });

        TopBar = bar.gameObject.AddComponent<TopBarUI>();
    }

    private void CreateResourceCell(string name, RectTransform parent, Color bgColor)
    {
        var cell = new GameObject(name + "Cell", typeof(RectTransform));
        cell.transform.SetParent(parent, false);
        StretchFill(cell.GetComponent<RectTransform>());

        cell.AddComponent<Image>().color = bgColor;

        var le = cell.AddComponent<LayoutElement>();
        le.flexibleWidth = 1;
        le.minWidth = 30;

        var tmp = CreateTMP(name, cell.transform, name + ": 0", BrandGuide.FontBody);
        tmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.richText = true;
        tmp.color = BrandGuide.TextPrimary;
        var tmpRT = tmp.GetComponent<RectTransform>();
        StretchFill(tmpRT);
        tmpRT.offsetMin = new Vector2(6, 0);
        tmpRT.offsetMax = new Vector2(-4, 0);
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
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
        root.offsetMax = new Vector2(200, -130);

        // ---- 建築ボタン ----
        var buildBtn = CreateButton("BuildOpenButton", root, "建築", BrandGuide.FontCaption,
            BrandGuide.BtnBuild);
        var buildBtnRT = buildBtn.GetComponent<RectTransform>();
        buildBtnRT.anchorMin = new Vector2(0, 1);
        buildBtnRT.anchorMax = new Vector2(1, 1);
        buildBtnRT.pivot = new Vector2(0.5f, 1);
        buildBtnRT.sizeDelta = new Vector2(0, 40);
        buildBtnRT.anchoredPosition = Vector2.zero;

        // ---- ユニット生成ボタン ----
        var unitBtn = CreateButton("UnitOpenButton", root, "Unit制作", BrandGuide.FontCaption,
            BrandGuide.BtnUnit);
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

        AddImage(panel.gameObject, BrandGuide.PanelBgLight);
        BrandGuide.AddPanelBorder(panel);

        // ---- Header ----
        var header = CreatePanel("Header", panel,
            new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
            new Vector2(0, 36));
        header.anchoredPosition = Vector2.zero;
        AddImage(header.gameObject, new Color(0.08f, 0.08f, 0.10f, 1f));

        var headerTitle = CreateTMP("Title", header, "メニュー", BrandGuide.FontBody);
        headerTitle.color = BrandGuide.Primary;
        headerTitle.fontStyle = FontStyles.Bold;
        StretchFill(headerTitle.GetComponent<RectTransform>());

        var closeBtn = CreateButton("CloseButton", header, "×", 22,
            BrandGuide.BtnClose);
        var closeBtnRT = closeBtn.GetComponent<RectTransform>();
        closeBtnRT.anchorMin = new Vector2(1, 0);
        closeBtnRT.anchorMax = new Vector2(1, 1);
        closeBtnRT.pivot = new Vector2(1, 0.5f);
        closeBtnRT.sizeDelta = new Vector2(36, 0);
        closeBtnRT.anchoredPosition = Vector2.zero;

        // ---- BuildScrollView ----
        var buildRoot = buildSummonUI.CreateBuildScrollView("BuildScrollView", panel);
        var buildRootRT = buildRoot.GetComponent<RectTransform>();
        StretchFill(buildRootRT);
        buildRootRT.offsetMin = new Vector2(4, 4);
        buildRootRT.offsetMax = new Vector2(-4, -40);
        buildRoot.SetActive(false);

        // ---- UnitScrollView ----
        var unitRoot = buildSummonUI.CreateScrollView("UnitScrollView", panel);
        var unitRootRT = unitRoot.GetComponent<RectTransform>();
        StretchFill(unitRootRT);
        unitRootRT.offsetMin = new Vector2(4, 4);
        unitRootRT.offsetMax = new Vector2(-4, -40);
        unitRoot.SetActive(false);

        // ---- SlidePanelUI コンポーネントを追加 ----
        SlidePanel = panel.gameObject.AddComponent<SlidePanelUI>();
        buildSummonUI.SetSlidePanel(SlidePanel);

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
            new Vector2(800, 180));
        panel.anchoredPosition = new Vector2(0, 10);

        AddImage(panel.gameObject, BrandGuide.PanelBgLight);
        BrandGuide.AddTopBorder(panel);

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

        var nameText = CreateTMP("NameText", left, "---", 24);
        nameText.color = BrandGuide.Primary;
        nameText.fontStyle = FontStyles.Bold;
        SetAnchors(nameText, 0, 0.66f, 1, 1);
        var levelText = CreateTMP("LevelText", left, "Lv 1", BrandGuide.FontBody);
        levelText.color = BrandGuide.TextSecondary;
        SetAnchors(levelText, 0, 0.33f, 1, 0.66f);
        var hpText = CreateTMP("HPText", left, "HP 0", BrandGuide.FontBody);
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

        var atkText = CreateTMP("ATKText", center, "ATK 0", BrandGuide.FontBody);
        atkText.color = new Color(1f, 0.75f, 0.60f);
        SetAnchors(atkText, 0, 0.66f, 1, 1);
        var defText = CreateTMP("DEFText", center, "DEF 0", BrandGuide.FontBody);
        defText.color = new Color(0.60f, 0.82f, 1f);
        SetAnchors(defText, 0, 0.33f, 1, 0.66f);
        var kindText = CreateTMP("KindText", center, "", BrandGuide.FontCaption);
        kindText.color = BrandGuide.TextSecondary;
        SetAnchors(kindText, 0, 0, 0.5f, 0.33f);
        var passiveText = CreateTMP("PassiveText", center, "", BrandGuide.FontCaption);
        passiveText.color = BrandGuide.TextSecondary;
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

        var attackBtn = CreateButton("AttackBtn", right, "攻撃", BrandGuide.FontCaption,
            BrandGuide.BtnAttack);
        SetAnchors(attackBtn.GetComponent<RectTransform>(), 0, 0.5f, 0.5f, 1);
        var skillBtn = CreateButton("SkillBtn", right, "スキル", BrandGuide.FontCaption,
            BrandGuide.BtnSkill);
        SetAnchors(skillBtn.GetComponent<RectTransform>(), 0.5f, 0.5f, 1, 1);
        var waitBtn = CreateButton("WaitBtn", right, "待機", BrandGuide.FontCaption,
            BrandGuide.BtnWait);
        SetAnchors(waitBtn.GetComponent<RectTransform>(), 0, 0, 0.5f, 0.5f);
        var cancelBtn = CreateButton("CancelBtn", right, "取消", BrandGuide.FontCaption,
            BrandGuide.BtnCancel);
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

        AddImage(panel.gameObject, BrandGuide.PanelBg);
        BrandGuide.AddPanelBorder(panel);

        // APラベル
        var apLabel = CreateTMP("APLabel", panel, "AP", BrandGuide.FontSmall);
        apLabel.color = BrandGuide.TextLabel;
        apLabel.fontStyle = FontStyles.Bold;
        apLabel.alignment = TextAlignmentOptions.Center;
        var apLabelRT = apLabel.GetComponent<RectTransform>();
        apLabelRT.anchorMin = new Vector2(0, 0.7f);
        apLabelRT.anchorMax = new Vector2(1, 1);
        apLabelRT.offsetMin = new Vector2(4, 0);
        apLabelRT.offsetMax = new Vector2(-4, -2);

        var apText = CreateTMP("APText", panel, "0 / 0", 30);
        apText.fontStyle = FontStyles.Bold;
        var apTextRT = apText.GetComponent<RectTransform>();
        apTextRT.anchorMin = new Vector2(0, 0);
        apTextRT.anchorMax = new Vector2(1, 0.72f);
        apTextRT.offsetMin = new Vector2(4, 2);
        apTextRT.offsetMax = new Vector2(-4, 0);

        APPanel = panel.gameObject.AddComponent<APPanelUI>();
    }

    // ==================================================================
    //  ゲームシステムへの自動接続
    // ==================================================================
    private void WireToGameSystems()
    {
        var turnGen = Object.FindFirstObjectByType<TurnGenerator>();
        if (turnGen != null && UnitPanel != null)
        {
            turnGen.Systems.UnitPanelUI = UnitPanel;
        }

        var gameGen = Object.FindFirstObjectByType<GameGenerator>();
        if (gameGen != null)
        {
            // GameGenerator の UI フィールドに値を注入
            SetSerializedField(gameGen, "_APPanelUI", APPanel);
            SetSerializedField(gameGen, "_ResourceBarUI", ResourceBar);
        }
    }

    /// <summary>建築ボタン初期化 → BuildSummonUIBuilder に委譲</summary>
    public void InitBuildButtons(BuildSystem bs, APSystem ap, FactionState fs)
    {
        BuildSystem = bs;
        buildSummonUI.InitBuildButtons(bs, ap, fs);
    }

    /// <summary>召喚ボタン初期化 → BuildSummonUIBuilder に委譲</summary>
    public void InitSummonButtons(SummonSystem ss, APSystem ap, FactionState fs, UnitSetting us)
    {
        SummonSystem = ss;
        buildSummonUI.InitSummonButtons(ss, ap, fs, us);
    }


    // ==================================================================
    //  ヘルパー → UIFactory に委譲
    // ==================================================================

    private RectTransform CreatePanel(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta)
        => UIFactory.CreatePanel(name, parent, anchorMin, anchorMax, pivot, sizeDelta);

    private RectTransform CreatePanel(string name, RectTransform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta)
        => UIFactory.CreatePanel(name, parent, anchorMin, anchorMax, pivot, sizeDelta);

    private TextMeshProUGUI CreateTMP(string name, RectTransform parent, string text, float fontSize)
        => UIFactory.CreateTMP(name, parent, text, fontSize, defaultFont);

    private TextMeshProUGUI CreateTMP(string name, Transform parent, string text, float fontSize)
        => UIFactory.CreateTMP(name, parent, text, fontSize, defaultFont);

    private Button CreateButton(string name, RectTransform parent, string label, float fontSize, Color bgColor)
        => UIFactory.CreateButton(name, parent, label, fontSize, bgColor, defaultFont);

    private Button CreateButton(string name, Transform parent, string label, float fontSize, Color bgColor)
        => UIFactory.CreateButton(name, parent, label, fontSize, bgColor, defaultFont);

    private Scrollbar CreateVerticalScrollbar(Transform parent)
        => UIFactory.CreateVerticalScrollbar(parent);

    private Image AddImage(GameObject go, Color color)
        => UIFactory.AddImage(go, color);

    private void AddHorizontalLayout(GameObject go, float spacing = 4)
        => UIFactory.AddHorizontalLayout(go, spacing);

    private void StretchFill(RectTransform rt)
        => UIFactory.StretchFill(rt);

    private void SetAnchors(RectTransform rt, float xMin, float yMin, float xMax, float yMax)
        => UIFactory.SetAnchors(rt, xMin, yMin, xMax, yMax);

    private void SetAnchors(TextMeshProUGUI tmp, float xMin, float yMin, float xMax, float yMax)
        => UIFactory.SetAnchors(tmp, xMin, yMin, xMax, yMax);

    private TMP_FontAsset LoadDefaultFont()
        => UIFactory.LoadDefaultFont();

    private static void SetAsTMPFallback(TMP_FontAsset font)
        => UIFactory.SetAsTMPFallback(font);

    private static void SetSerializedField(object target, string fieldName, object value)
        => UIFactory.SetSerializedField(target, fieldName, value);
}
