using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ランタイムでUI Canvas全体を構築し、各UIスクリプトを配置・接続する。
/// GameGenerater の Awake() 完了後に BuildUI() を呼ぶこと。
/// </summary>
public class UIBuilder : MonoBehaviour
{
    // GameGenerater から渡される参照
    private TurnGenerater turnGenerater;
    private APSystem apSystem;
    private FactionState factionState;

    // 外部からアクセスするUI参照
    [HideInInspector] public UnitPanelUI unitPanelUI;

    /// <summary>
    /// 全UIを構築する。GameGenerater.Awake() の最後に呼ぶ。
    /// </summary>
    public void BuildUI(TurnGenerater turn, APSystem ap, FactionState faction)
    {
        turnGenerater = turn;
        apSystem = ap;
        factionState = faction;

        // Canvas
        GameObject canvasGO = CreateCanvas("UICanvas");
        RectTransform canvasRect = canvasGO.GetComponent<RectTransform>();

        // SafeAreaRoot (全体を囲む)
        RectTransform safeArea = CreatePanel(canvasRect, "SafeAreaRoot", false);
        Stretch(safeArea);

        // TopBar
        BuildTopBar(safeArea);

        // ResourceBar
        BuildResourceBar(safeArea);

        // APPanel
        BuildAPPanel(safeArea);

        // UnitPanel (下部)
        BuildUnitPanel(safeArea);

        // SlidePanel (左メニュー) — 空の枠だけ用意
        BuildSlidePanel(safeArea);
    }

    // ======================================================
    //  TopBar: 画面上部にターン数を表示
    // ======================================================
    private void BuildTopBar(RectTransform parent)
    {
        RectTransform bar = CreatePanel(parent, "TopBar", true);
        SetAnchors(bar, 0f, 1f, 1f, 1f); // top-stretch
        bar.pivot = new Vector2(0.5f, 1f);
        bar.sizeDelta = new Vector2(0f, 40f);
        bar.anchoredPosition = Vector2.zero;
        bar.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.2f, 0.85f);

        // Turn text
        TextMeshProUGUI turnText = CreateTMP(bar, "TurnText", "Turn 0", 20,
            TextAlignmentOptions.MidlineLeft);
        Stretch(turnText.rectTransform);
        turnText.rectTransform.offsetMin = new Vector2(16f, 0f);

        // TopBarUI スクリプト
        TopBarUI topBarUI = bar.gameObject.AddComponent<TopBarUI>();
        SetPrivateField(topBarUI, "turnText", turnText);
        SetPrivateField(topBarUI, "turnGenerater", turnGenerater);
    }

    // ======================================================
    //  ResourceBar: TopBarの下に資源を2行で表示
    // ======================================================
    private void BuildResourceBar(RectTransform parent)
    {
        RectTransform bar = CreatePanel(parent, "ResourceBar", true);
        SetAnchors(bar, 0f, 1f, 1f, 1f); // top-stretch
        bar.pivot = new Vector2(0.5f, 1f);
        bar.sizeDelta = new Vector2(0f, 60f);
        bar.anchoredPosition = new Vector2(0f, -40f); // TopBarの下
        bar.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.15f, 0.75f);

        VerticalLayoutGroup vlg = bar.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = true;
        vlg.padding = new RectOffset(8, 8, 2, 2);
        vlg.spacing = 0f;

        // Row1
        RectTransform row1 = CreateLayoutRow(bar, "Row1");
        TextMeshProUGUI woodText = CreateTMP(row1, "WoodText", "木材: 0", 13);
        TextMeshProUGUI stoneText = CreateTMP(row1, "StoneText", "石材: 0", 13);
        TextMeshProUGUI coalText = CreateTMP(row1, "CoalText", "石炭: 0", 13);
        TextMeshProUGUI ironOreText = CreateTMP(row1, "IronOreText", "鉄鉱: 0", 13);
        TextMeshProUGUI ironText = CreateTMP(row1, "IronText", "鉄: 0", 13);
        TextMeshProUGUI magicOreText = CreateTMP(row1, "MagicOreText", "魔石: 0", 13);

        // Row2
        RectTransform row2 = CreateLayoutRow(bar, "Row2");
        TextMeshProUGUI wheatText = CreateTMP(row2, "WheatText", "小麦: 0", 13);
        TextMeshProUGUI breadText = CreateTMP(row2, "BreadText", "パン: 0", 13);
        TextMeshProUGUI waterText = CreateTMP(row2, "WaterText", "水: 0", 13);
        TextMeshProUGUI plankText = CreateTMP(row2, "PlankText", "板材: 0", 13);
        TextMeshProUGUI cutStoneText = CreateTMP(row2, "CutStoneText", "切石: 0", 13);
        TextMeshProUGUI citizenText = CreateTMP(row2, "CitizenText", "市民: 0", 13);

        // ResourceBarUI スクリプト
        ResourceBarUI resUI = bar.gameObject.AddComponent<ResourceBarUI>();
        SetPrivateField(resUI, "woodText", woodText);
        SetPrivateField(resUI, "stoneText", stoneText);
        SetPrivateField(resUI, "coalText", coalText);
        SetPrivateField(resUI, "ironOreText", ironOreText);
        SetPrivateField(resUI, "ironText", ironText);
        SetPrivateField(resUI, "magicOreText", magicOreText);
        SetPrivateField(resUI, "wheatText", wheatText);
        SetPrivateField(resUI, "breadText", breadText);
        SetPrivateField(resUI, "waterText", waterText);
        SetPrivateField(resUI, "plankText", plankText);
        SetPrivateField(resUI, "cutStoneText", cutStoneText);
        SetPrivateField(resUI, "citizenText", citizenText);
        SetPrivateField(resUI, "factionState", factionState);
    }

    // ======================================================
    //  APPanel: 右上にAP表示
    // ======================================================
    private void BuildAPPanel(RectTransform parent)
    {
        RectTransform panel = CreatePanel(parent, "APPanel", true);
        SetAnchors(panel, 1f, 1f, 1f, 1f); // top-right
        panel.pivot = new Vector2(1f, 1f);
        panel.sizeDelta = new Vector2(120f, 50f);
        panel.anchoredPosition = new Vector2(-8f, -8f);
        panel.GetComponent<Image>().color = new Color(0.15f, 0.2f, 0.35f, 0.85f);

        // APテキスト
        TextMeshProUGUI apText = CreateTMP(panel, "APText", "AP", 18,
            TextAlignmentOptions.Center);
        Stretch(apText.rectTransform);

        // APPanelUI スクリプト
        APPanelUI apUI = panel.gameObject.AddComponent<APPanelUI>();
        SetPrivateField(apUI, "apText", apText);
        SetPrivateField(apUI, "factionState", factionState);
    }

    // ======================================================
    //  UnitPanel: 画面下部にユニット情報表示
    // ======================================================
    private void BuildUnitPanel(RectTransform parent)
    {
        RectTransform panel = CreatePanel(parent, "BottomUnitPanel", true);
        SetAnchors(panel, 0f, 0f, 1f, 0f); // bottom-stretch
        panel.pivot = new Vector2(0.5f, 0f);
        panel.sizeDelta = new Vector2(0f, 100f);
        panel.anchoredPosition = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(0.08f, 0.08f, 0.15f, 0.9f);

        HorizontalLayoutGroup hlg = panel.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;
        hlg.padding = new RectOffset(12, 12, 8, 8);
        hlg.spacing = 16f;

        // 左: 基本情報
        RectTransform leftInfo = CreatePanel(panel, "LeftInfo", false);
        VerticalLayoutGroup leftVlg = leftInfo.gameObject.AddComponent<VerticalLayoutGroup>();
        leftVlg.childForceExpandWidth = true;
        leftVlg.childForceExpandHeight = true;
        TextMeshProUGUI nameText = CreateTMP(leftInfo, "NameText", "", 18);
        TextMeshProUGUI levelText = CreateTMP(leftInfo, "LevelText", "", 14);
        TextMeshProUGUI hpText = CreateTMP(leftInfo, "HPText", "", 14);

        // 中央: ステータス
        RectTransform centerStats = CreatePanel(panel, "CenterStats", false);
        VerticalLayoutGroup centerVlg = centerStats.gameObject.AddComponent<VerticalLayoutGroup>();
        centerVlg.childForceExpandWidth = true;
        centerVlg.childForceExpandHeight = true;
        TextMeshProUGUI atkText = CreateTMP(centerStats, "ATKText", "", 14);
        TextMeshProUGUI defText = CreateTMP(centerStats, "DEFText", "", 14);
        TextMeshProUGUI kindText = CreateTMP(centerStats, "KindText", "", 14);
        TextMeshProUGUI passiveText = CreateTMP(centerStats, "PassiveText", "", 12);

        // 右: ボタン
        RectTransform rightCmd = CreatePanel(panel, "RightCommands", false);
        GridLayoutGroup glg = rightCmd.gameObject.AddComponent<GridLayoutGroup>();
        glg.cellSize = new Vector2(80f, 32f);
        glg.spacing = new Vector2(8f, 4f);
        glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        glg.constraintCount = 2;
        glg.childAlignment = TextAnchor.MiddleCenter;

        Button attackBtn = CreateButton(rightCmd, "AttackBtn", "攻撃");
        Button skillBtn = CreateButton(rightCmd, "SkillBtn", "スキル");
        Button waitBtn = CreateButton(rightCmd, "WaitBtn", "待機");
        Button cancelBtn = CreateButton(rightCmd, "CancelBtn", "戻る");

        // CanvasGroup (表示/非表示用)
        CanvasGroup cg = panel.gameObject.AddComponent<CanvasGroup>();

        // UnitPanelUI スクリプト
        UnitPanelUI upUI = panel.gameObject.AddComponent<UnitPanelUI>();
        SetPrivateField(upUI, "nameText", nameText);
        SetPrivateField(upUI, "levelText", levelText);
        SetPrivateField(upUI, "hpText", hpText);
        SetPrivateField(upUI, "atkText", atkText);
        SetPrivateField(upUI, "defText", defText);
        SetPrivateField(upUI, "kindText", kindText);
        SetPrivateField(upUI, "passiveText", passiveText);
        SetPrivateField(upUI, "attackButton", attackBtn);
        SetPrivateField(upUI, "skillButton", skillBtn);
        SetPrivateField(upUI, "waitButton", waitBtn);
        SetPrivateField(upUI, "cancelButton", cancelBtn);
        SetPrivateField(upUI, "turnGenerater", turnGenerater);
        SetPrivateField(upUI, "apSystem", apSystem);
        SetPrivateField(upUI, "canvasGroup", cg);
        SetPrivateField(upUI, "panelRoot", panel.gameObject);

        // ボタンイベント登録
        attackBtn.onClick.AddListener(upUI.OnClickAttack);
        skillBtn.onClick.AddListener(upUI.OnClickSkill);
        waitBtn.onClick.AddListener(upUI.OnClickWait);
        cancelBtn.onClick.AddListener(upUI.OnClickCancel);

        unitPanelUI = upUI;
    }

    // ======================================================
    //  SlidePanel: 左側スライドメニューの枠
    // ======================================================
    private void BuildSlidePanel(RectTransform parent)
    {
        // LeftMenuRoot
        RectTransform menuRoot = CreatePanel(parent, "LeftMenuRoot", false);
        SetAnchors(menuRoot, 0f, 0f, 0f, 1f); // left-stretch
        menuRoot.pivot = new Vector2(0f, 0.5f);
        menuRoot.sizeDelta = new Vector2(350f, 0f);
        menuRoot.anchoredPosition = Vector2.zero;

        // SlidePanel 本体
        RectTransform slidePanel = CreatePanel(menuRoot, "SlidePanel", true);
        Stretch(slidePanel);
        slidePanel.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.18f, 0.9f);

        // 中身 (空の ScrollView 枠だけ)
        RectTransform buildRoot = CreatePanel(slidePanel, "BuildScrollView", false);
        Stretch(buildRoot);
        RectTransform unitRoot = CreatePanel(slidePanel, "UnitScrollView", false);
        Stretch(unitRoot);

        SlidePanelUI spUI = slidePanel.gameObject.AddComponent<SlidePanelUI>();
        SetPrivateField(spUI, "panelRect", slidePanel);
        SetPrivateField(spUI, "buildRoot", buildRoot.gameObject);
        SetPrivateField(spUI, "unitRoot", unitRoot.gameObject);
    }

    // ======================================================
    //  ヘルパー
    // ======================================================

    private static GameObject CreateCanvas(string name)
    {
        GameObject go = new GameObject(name);
        Canvas canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        go.AddComponent<GraphicRaycaster>();
        return go;
    }

    private static RectTransform CreatePanel(RectTransform parent, string name, bool addImage)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        if (addImage)
        {
            Image img = go.AddComponent<Image>();
            img.color = Color.clear;
        }
        return rt;
    }

    private static RectTransform CreateLayoutRow(RectTransform parent, string name)
    {
        RectTransform row = CreatePanel(parent, name, false);
        HorizontalLayoutGroup hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;
        hlg.spacing = 4f;
        return row;
    }

    private static TextMeshProUGUI CreateTMP(RectTransform parent, string name,
        string text, float fontSize,
        TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = align;
        tmp.color = Color.white;
        tmp.enableAutoSizing = false;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        return tmp;
    }

    private static Button CreateButton(RectTransform parent, string name, string label)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        Image bg = go.AddComponent<Image>();
        bg.color = new Color(0.25f, 0.3f, 0.45f, 1f);

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = bg;

        // ボタンラベル
        TextMeshProUGUI txt = CreateTMP(
            go.GetComponent<RectTransform>(), "Label", label, 14,
            TextAlignmentOptions.Center);
        Stretch(txt.rectTransform);

        return btn;
    }

    // ストレッチ (親いっぱいに広げる)
    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void SetAnchors(RectTransform rt,
        float minX, float minY, float maxX, float maxY)
    {
        rt.anchorMin = new Vector2(minX, minY);
        rt.anchorMax = new Vector2(maxX, maxY);
    }

    /// <summary>
    /// [SerializeField] private フィールドにリフレクションで値を代入する。
    /// </summary>
    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var type = target.GetType();
        var field = type.GetField(fieldName,
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);
        if (field != null)
            field.SetValue(target, value);
        else
            Debug.LogWarning($"[UIBuilder] Field '{fieldName}' not found on {type.Name}");
    }
}
