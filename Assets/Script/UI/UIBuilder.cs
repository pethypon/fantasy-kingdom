using System.Collections.Generic;
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
    public BuildSystem BuildSystem { get; private set; }
    public SummonSystem SummonSystem { get; private set; }

    private Canvas canvas;
    private TMP_FontAsset defaultFont;

    // 建築ボタン管理用
    private readonly List<(Button btn, Image bg, FacilityKind kind)> buildButtons
        = new List<(Button, Image, FacilityKind)>();
    // 召喚ボタン管理用
    private readonly List<(Button btn, Image bg, Kind kind)> summonButtons
        = new List<(Button, Image, Kind)>();
    private APSystem cachedAPSystem;
    private FactionState cachedFactionState;
    private UnitSetting cachedUnitSetting;

    private void Awake()
    {
        defaultFont = LoadDefaultFont();
        SetAsTMPFallback(defaultFont);
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

        AddImage(bar.gameObject, new Color(0.06f, 0.06f, 0.08f, 0.92f));

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

        // カテゴリ別の背景色
        Color rawC  = new Color(0.20f, 0.16f, 0.10f, 0.6f);
        Color minC  = new Color(0.13f, 0.16f, 0.22f, 0.6f);
        Color proC  = new Color(0.16f, 0.13f, 0.22f, 0.6f);
        Color fooC  = new Color(0.22f, 0.18f, 0.08f, 0.6f);
        Color popC  = new Color(0.10f, 0.22f, 0.13f, 0.6f);

        string[] r1Names  = { "Wood",  "Stone", "Coal",   "IronOre", "Iron",     "MagicOre" };
        Color[]  r1Colors = { rawC,    rawC,    minC,     minC,      proC,       proC };
        Color scC  = new Color(0.10f, 0.18f, 0.25f, 0.6f); // サブクリスタル（シアン系）
        string[] r2Names  = { "Wheat", "Bread", "Water",  "Plank",   "CutStone", "Citizen", "SubCrystal" };
        Color[]  r2Colors = { fooC,    fooC,    rawC,     proC,      proC,       popC,      scC };

        for (int i = 0; i < r1Names.Length; i++)
            CreateResourceCell(r1Names[i], row1, r1Colors[i]);
        for (int i = 0; i < r2Names.Length; i++)
            CreateResourceCell(r2Names[i], row2, r2Colors[i]);

        ResourceBar = resArea.gameObject.AddComponent<ResourceBarUI>();

        // ============ 区切り線 ============
        var sep1 = new GameObject("Sep1", typeof(RectTransform));
        sep1.transform.SetParent(bar, false);
        var sep1Img = sep1.AddComponent<Image>();
        sep1Img.color = new Color(1f, 1f, 1f, 0.15f);
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
        iconImg.color = new Color(0.3f, 0.6f, 0.9f, 1f);
        var iconRT = iconGo.GetComponent<RectTransform>();
        iconRT.sizeDelta = new Vector2(24, 24);
        var iconLE = iconGo.AddComponent<LayoutElement>();
        iconLE.preferredWidth = 24;
        iconLE.preferredHeight = 24;

        // ターンテキスト
        var turnText = CreateTMP("TurnText", turnArea, "Turn 0", 28);
        turnText.alignment = TextAlignmentOptions.MidlineLeft;
        var turnTextLE = turnText.gameObject.AddComponent<LayoutElement>();
        turnTextLE.preferredWidth = 100;
        turnTextLE.preferredHeight = 40;

        // ============ 区切り線 ============
        var sep2 = new GameObject("Sep2", typeof(RectTransform));
        sep2.transform.SetParent(bar, false);
        var sep2Img = sep2.AddComponent<Image>();
        sep2Img.color = new Color(1f, 1f, 1f, 0.15f);
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

        // 右エリア内を HorizontalLayoutGroup で中央寄せ
        var rightHLG = rightArea.gameObject.AddComponent<HorizontalLayoutGroup>();
        rightHLG.childAlignment = TextAnchor.MiddleCenter;
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
        timerBg.AddComponent<Image>().color = new Color(0.05f, 0.05f, 0.08f, 0.8f);
        StretchFill(timerBg.GetComponent<RectTransform>());

        // バー本体
        var timerFill = new GameObject("TimerFill", typeof(RectTransform));
        timerFill.transform.SetParent(timerArea.transform, false);
        timerFill.AddComponent<Image>().color = new Color(0.2f, 0.55f, 0.8f, 0.9f);
        var fillRT = timerFill.GetComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = new Vector2(2, 2);
        fillRT.offsetMax = new Vector2(-2, -2);

        // 制限時間テキスト
        var timerText = CreateTMP("TimerText", timerArea.transform, "制限時間", 20);
        timerText.alignment = TextAlignmentOptions.Center;
        StretchFill(timerText.GetComponent<RectTransform>());

        // メニューボタン
        var menuBtn = CreateButton("MenuButton", rightArea, "メニュー", 20,
            new Color(0.25f, 0.25f, 0.30f, 1f));
        var menuBtnRT = menuBtn.GetComponent<RectTransform>();
        menuBtnRT.sizeDelta = new Vector2(120, 44);

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

        var tmp = CreateTMP(name, cell.transform, name + ": 0", 20);
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.richText = true;
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
        var buildRoot = CreateBuildScrollView("BuildScrollView", panel);
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
            new Vector2(800, 180));
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

    /// <summary>
    /// BuildSystem と FactionState の参照を受け取り、建築ボタンの有効/無効を制御可能にする。
    /// GameGenerater.Awake() から呼ばれる。
    /// </summary>
    public void InitBuildButtons(BuildSystem bs, APSystem ap, FactionState fs)
    {
        BuildSystem = bs;
        cachedAPSystem = ap;
        cachedFactionState = fs;

        // スライドパネルが開かれるたびにボタン状態を更新
        if (SlidePanel != null)
            SlidePanel.OnBuildPanelOpened += RefreshBuildButtons;
    }

    /// <summary>
    /// 建築ボタンの有効/無効・色を更新する。
    /// </summary>
    public void RefreshBuildButtons()
    {
        if (cachedAPSystem == null || cachedFactionState == null) return;

        foreach (var (btn, bg, kind) in buildButtons)
        {
            bool canBuild;
            if (FacilityData.IsSubCrystal(kind))
            {
                canBuild = cachedFactionState.GetSubCrystals(Team.Player) > 0;
            }
            else
            {
                canBuild = cachedAPSystem.CanBuild(Team.Player, kind, cachedFactionState);
            }
            btn.interactable = canBuild;
            if (FacilityData.IsSubCrystal(kind))
            {
                bg.color = canBuild
                    ? new Color(0.15f, 0.30f, 0.45f, 1f)
                    : new Color(0.5f, 0.15f, 0.15f, 1f);
            }
            else
            {
                bg.color = canBuild
                    ? new Color(0.2f, 0.35f, 0.2f, 1f)
                    : new Color(0.5f, 0.15f, 0.15f, 1f);
            }
        }
    }

    /// <summary>
    /// SummonSystem と UnitSetting の参照を受け取り、召喚ボタンを制御可能にする。
    /// GameGenerater.Awake() から呼ばれる。
    /// </summary>
    public void InitSummonButtons(SummonSystem ss, APSystem ap, FactionState fs, UnitSetting us)
    {
        SummonSystem = ss;
        cachedAPSystem = ap;
        cachedFactionState = fs;
        cachedUnitSetting = us;

        if (SlidePanel != null)
            SlidePanel.OnUnitPanelOpened += RefreshSummonButtons;
    }

    /// <summary>
    /// 召喚ボタンの有効/無効・色を更新する。
    /// </summary>
    public void RefreshSummonButtons()
    {
        if (SummonSystem == null || cachedUnitSetting == null) return;

        foreach (var (btn, bg, kind) in summonButtons)
        {
            bool canSummon = SummonSystem.CanSummon(Team.Player, kind);
            btn.interactable = canSummon;
            bg.color = canSummon
                ? new Color(0.2f, 0.25f, 0.45f, 1f)
                : new Color(0.5f, 0.15f, 0.15f, 1f);
        }
    }

    // ==================================================================
    //  BuildScrollView（建築物一覧を生成）
    // ==================================================================
    private GameObject CreateBuildScrollView(string name, RectTransform parent)
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

        // Viewport を右側にスクロールバー分の余白
        vpRT.offsetMax = new Vector2(-10, 0);

        // ScrollRect
        var sr = go.AddComponent<ScrollRect>();
        sr.viewport = vpRT;
        sr.content = contentRT;
        sr.horizontal = false;
        sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;

        // 縦スクロールバー
        sr.verticalScrollbar = CreateVerticalScrollbar(go.transform);
        sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;

        // 建築物ボタン + コスト表示を生成
        foreach (var kvp in FacilityData.Table)
        {
            var facility = kvp.Key;
            var info = kvp.Value;

            // ---- 行コンテナ（ボタン + コスト） ----
            var row = new GameObject("Row_" + facility, typeof(RectTransform));
            row.transform.SetParent(content.transform, false);
            var rowLE = row.AddComponent<LayoutElement>();
            rowLE.preferredHeight = 80;

            var rowVLG = row.AddComponent<VerticalLayoutGroup>();
            rowVLG.spacing = 2;
            rowVLG.childControlWidth = true;
            rowVLG.childControlHeight = true;
            rowVLG.childForceExpandWidth = true;
            rowVLG.childForceExpandHeight = false;

            // ---- ボタン ----
            bool isSubCrystal = FacilityData.IsSubCrystal(facility);
            string label = isSubCrystal
                ? $"{info.DisplayName}  副晶x1"
                : $"{info.DisplayName}  AP:{info.APCost}";
            var btn = CreateButton("Build_" + facility, row.transform,
                label, 18, isSubCrystal
                    ? new Color(0.15f, 0.30f, 0.45f, 1f)
                    : new Color(0.2f, 0.35f, 0.2f, 1f));
            var btnLE = btn.gameObject.AddComponent<LayoutElement>();
            btnLE.preferredHeight = 40;

            var bg = btn.GetComponent<Image>();
            buildButtons.Add((btn, bg, facility));

            FacilityKind captured = facility;
            btn.onClick.AddListener(() => OnBuildButtonClicked(captured));

            // ---- コスト表示 ----
            string costStr = isSubCrystal ? "サブクリスタル1消費 (破壊後5T返却)" : FormatBuildCost(info.BuildCost);
            var costTMP = CreateTMP("Cost_" + facility, row.transform, costStr, 15);
            costTMP.color = new Color(0.7f, 0.7f, 0.6f);
            costTMP.alignment = TextAlignmentOptions.MidlineLeft;
            costTMP.enableWordWrapping = false;
            costTMP.overflowMode = TextOverflowModes.Ellipsis;
            var costLE = costTMP.gameObject.AddComponent<LayoutElement>();
            costLE.preferredHeight = 28;
            var costRT = costTMP.GetComponent<RectTransform>();
            costRT.offsetMin = new Vector2(8, 0);
        }

        return go;
    }

    private static string FormatBuildCost(FacilityData.ResourceCost c)
    {
        var p = new System.Collections.Generic.List<string>();
        if (c.Wood > 0) p.Add($"木{c.Wood}");
        if (c.Stone > 0) p.Add($"石{c.Stone}");
        if (c.Iron > 0) p.Add($"鉄{c.Iron}");
        if (c.MagicOre > 0) p.Add($"魔{c.MagicOre}");
        if (c.Water > 0) p.Add($"水{c.Water}");
        if (c.Plank > 0) p.Add($"板{c.Plank}");
        if (c.CutStone > 0) p.Add($"切{c.CutStone}");
        if (c.Citizen > 0) p.Add($"民{c.Citizen}");
        return string.Join(" ", p);
    }

    private void OnBuildButtonClicked(FacilityKind facility)
    {
        if (BuildSystem == null) return;

        // パネルを閉じる
        if (SlidePanel != null) SlidePanel.ClosePanel();

        // 建築モード開始
        BuildSystem.StartBuildMode(facility);

        // PlayerMove の BuildMode を有効にする
        var turnGen = Object.FindFirstObjectByType<TurnGenerater>();
        if (turnGen != null)
        {
            // 現在のステートが PlayerMove であることを前提にフラグを設定
            // BuildSystem.IsActive で PlayerMove.Update 内で判定する
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

    // 召喚可能なユニット種別（Crystal/King/壁は除外）
    private static readonly Kind[] SummonableKinds = {
        Kind.Knight, Kind.Archer, Kind.Magic, Kind.Assassin,
        Kind.Scout, Kind.Priest, Kind.Guardian, Kind.Crossbow,
        Kind.Magicsniper, Kind.Bomber,
    };

    private static readonly System.Collections.Generic.Dictionary<Kind, string> KindDisplayNames
        = new System.Collections.Generic.Dictionary<Kind, string>
    {
        { Kind.Knight,      "騎士" },
        { Kind.Archer,      "弓兵" },
        { Kind.Magic,       "魔法使い" },
        { Kind.Assassin,    "暗殺者" },
        { Kind.Scout,       "斥候" },
        { Kind.Priest,      "司祭" },
        { Kind.Guardian,    "守護者" },
        { Kind.Crossbow,    "弩兵" },
        { Kind.Magicsniper, "魔法狙撃" },
        { Kind.Bomber,      "爆撃手" },
    };

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

        // Viewport を右側にスクロールバー分の余白
        vpRT.offsetMax = new Vector2(-10, 0);

        // ScrollRect
        var sr = go.AddComponent<ScrollRect>();
        sr.viewport = vpRT;
        sr.content = contentRT;
        sr.horizontal = false;
        sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;

        // 縦スクロールバー
        sr.verticalScrollbar = CreateVerticalScrollbar(go.transform);
        sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;

        // ユニット召喚ボタンを生成
        foreach (var kind in SummonableKinds)
        {
            string displayName = KindDisplayNames.TryGetValue(kind, out string dn) ? dn : kind.ToString();
            string label = $"{displayName}";
            var btn = CreateButton("Summon_" + kind, content.transform,
                label, 14, new Color(0.2f, 0.25f, 0.45f, 1f));

            var le = btn.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 36;

            var bg = btn.GetComponent<Image>();
            summonButtons.Add((btn, bg, kind));

            Kind captured = kind;
            btn.onClick.AddListener(() => OnSummonButtonClicked(captured));
        }

        return go;
    }

    private void OnSummonButtonClicked(Kind kind)
    {
        if (SummonSystem == null) return;

        if (SlidePanel != null) SlidePanel.ClosePanel();

        SummonSystem.StartSummonMode(kind);
    }

    private Scrollbar CreateVerticalScrollbar(Transform parent)
    {
        // スクロールバー本体
        var sbGo = new GameObject("Scrollbar", typeof(RectTransform));
        sbGo.transform.SetParent(parent, false);
        var sbRT = sbGo.GetComponent<RectTransform>();
        sbRT.anchorMin = new Vector2(1, 0);
        sbRT.anchorMax = new Vector2(1, 1);
        sbRT.pivot = new Vector2(1, 0.5f);
        sbRT.sizeDelta = new Vector2(8, 0);
        sbRT.anchoredPosition = Vector2.zero;

        var sbImg = sbGo.AddComponent<Image>();
        sbImg.color = new Color(0.1f, 0.1f, 0.1f, 0.4f);

        // スライドエリア
        var slideArea = new GameObject("SlidingArea", typeof(RectTransform));
        slideArea.transform.SetParent(sbGo.transform, false);
        var saRT = slideArea.GetComponent<RectTransform>();
        StretchFill(saRT);

        // ハンドル
        var handle = new GameObject("Handle", typeof(RectTransform));
        handle.transform.SetParent(slideArea.transform, false);
        var hRT = handle.GetComponent<RectTransform>();
        StretchFill(hRT);

        var hImg = handle.AddComponent<Image>();
        hImg.color = new Color(0.5f, 0.5f, 0.55f, 0.7f);

        // Scrollbar コンポーネント
        var sb = sbGo.AddComponent<Scrollbar>();
        sb.handleRect = hRT;
        sb.targetGraphic = hImg;
        sb.direction = Scrollbar.Direction.BottomToTop;

        // ハンドルのホバー色設定
        var colors = sb.colors;
        colors.normalColor = new Color(0.5f, 0.5f, 0.55f, 0.7f);
        colors.highlightedColor = new Color(0.65f, 0.65f, 0.7f, 0.85f);
        colors.pressedColor = new Color(0.4f, 0.4f, 0.45f, 0.9f);
        sb.colors = colors;

        return sb;
    }

    private Image AddImage(GameObject go, Color color)
    {
        var img = go.GetComponent<Image>();
        if (img == null) img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    private void AddHorizontalLayout(GameObject go, float spacing = 4)
    {
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = spacing;
        hlg.padding = new RectOffset(2, 2, 0, 0);
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;
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
        // NotoSansJP SDF を優先読み込み（日本語対応）
        var font = Resources.Load<TMP_FontAsset>("Fonts & Materials/NotoSansJP-VariableFont_wght SDF");
        if (font != null) return font;

        // SDF アセットがない場合、TTF から動的に生成
        var ttf = Resources.Load<Font>("Fonts & Materials/NotoSansJP-VariableFont_wght");
        if (ttf != null)
        {
            font = TMP_FontAsset.CreateFontAsset(ttf);
            if (font != null)
            {
                font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                return font;
            }
        }

        // フォールバック: LiberationSans SDF
        font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font == null)
            font = TMP_Settings.defaultFontAsset;
        return font;
    }

    private static void SetAsTMPFallback(TMP_FontAsset font)
    {
        if (font == null) return;
        var settings = TMP_Settings.defaultFontAsset;
        if (settings == null || settings == font) return;
        if (settings.fallbackFontAssetTable == null)
            settings.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset>();
        if (!settings.fallbackFontAssetTable.Contains(font))
            settings.fallbackFontAssetTable.Add(font);
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
