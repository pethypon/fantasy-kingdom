using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 建築・召喚スクロールビューの生成とボタン管理を担当するクラス。
/// UIBuilder から生成され、保持される。
/// </summary>
public partial class BuildSummonUIBuilder
{
    readonly Dictionary<FacilityKind,TextMeshProUGUI> buildCostLabels = new Dictionary<FacilityKind,TextMeshProUGUI>();
    readonly Dictionary<FacilityKind,TextMeshProUGUI> buildTitles = new Dictionary<FacilityKind,TextMeshProUGUI>();
    GameObject buildContent, summonContent;
    // 召喚可能なユニット種別（Crystal/King/壁は除外）
    public static readonly Kind[] SummonableKinds = UnitRoster.SummonableKinds;

    public static readonly Dictionary<Kind, string> KindDisplayNames
        = new Dictionary<Kind, string>
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
        { Kind.WhiteMage,   "白魔導士" },
        { Kind.HolyKnight,  "聖騎士" },
        { Kind.Samurai,     "侍" },
        { Kind.Ninja,       "忍者" },
        { Kind.Berserker,   "狂戦士" },
        { Kind.Navigator,   "航海士" },
    };

    // 建築ボタン管理用
    private readonly List<(Button btn, Image bg, FacilityKind kind)> buildButtons
        = new List<(Button, Image, FacilityKind)>();
    // 召喚ボタン管理用
    private readonly List<(Button btn, Image bg, Kind kind, TextMeshProUGUI title, TextMeshProUGUI cost)> summonButtons
        = new List<(Button, Image, Kind, TextMeshProUGUI, TextMeshProUGUI)>();

    private APSystem cachedAPSystem;
    private FactionState cachedFactionState;
    private UnitSetting cachedUnitSetting;

    private readonly TMP_FontAsset defaultFont;

    // UIBuilder が保持する参照（ボタンクリック時に使用）
    private BuildSystem buildSystem;
    private SummonSystem summonSystem;
    private SlidePanelUI slidePanel;

    public BuildSummonUIBuilder(TMP_FontAsset font)
    {
        defaultFont = font;
    }

    /// <summary>
    /// UIBuilder から SlidePanel 参照を設定する。
    /// </summary>
    public void SetSlidePanel(SlidePanelUI panel)
    {
        slidePanel = panel;
    }

    // ==================================================================
    //  ScrollView 共通スキャフォールド
    // ==================================================================
    // Returns (scrollViewRoot, contentGameObject)
    private static (GameObject root, GameObject content) CreateBaseScrollView(string name, RectTransform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        UIFactory.StretchFill(go.GetComponent<RectTransform>());

        var scrollImg = go.AddComponent<Image>();
        scrollImg.color = new Color(0.12f, 0.12f, 0.12f, 0.5f);

        var viewport = new GameObject("Viewport", typeof(RectTransform));
        viewport.transform.SetParent(go.transform, false);
        var vpRT = viewport.GetComponent<RectTransform>();
        UIFactory.StretchFill(vpRT);
        var vpImg = viewport.AddComponent<Image>();
        vpImg.color = Color.white;
        var mask = viewport.AddComponent<Mask>();
        mask.showMaskGraphic = false;

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
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        vpRT.offsetMax = new Vector2(-34, 0);

        var sr = go.AddComponent<ScrollRect>();
        sr.viewport = vpRT;
        sr.content = contentRT;
        sr.horizontal = false;
        sr.vertical = true;
        sr.scrollSensitivity = 42f;
        sr.movementType = ScrollRect.MovementType.Clamped;

        sr.verticalScrollbar = UIFactory.CreateVerticalScrollbar(go.transform);
        sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

        return (go, content);
    }

    // ==================================================================
    //  BuildScrollView（建築物一覧を生成）
    // ==================================================================
    public GameObject CreateBuildScrollView(string name, RectTransform parent)
    {
        buildButtons.Clear(); // 再生成時に破棄済みボタン参照が残らないようにする
        buildCostLabels.Clear();
        buildTitles.Clear();
        authoredBuildButtons.Clear();
        var (go, content) = CreateBaseScrollView(name, parent);
        buildContent = content;

        // 建築物ボタン + コスト表示を生成
        foreach (var kvp in FacilityData.Table)
        {
            var facility = kvp.Key;
            var info = kvp.Value;

            // ---- 行コンテナ（ボタン + コスト） ----
            var row = new GameObject("Row_" + facility, typeof(RectTransform));
            row.transform.SetParent(content.transform, false);
            var rowLE = row.AddComponent<LayoutElement>();
            rowLE.minHeight = 104;

            var rowVLG = row.AddComponent<VerticalLayoutGroup>();
            rowVLG.spacing = 2;
            rowVLG.childControlWidth = true;
            rowVLG.childControlHeight = true;
            rowVLG.childForceExpandWidth = true;
            rowVLG.childForceExpandHeight = false;

            // ---- ボタン ----
            bool isSubCrystal = FacilityData.IsSubCrystal(facility);
            string label = isSubCrystal
                ? info.DisplayName + "  副晶x1" + (info.APCost > 0 ? "  AP:" + info.APCost : string.Empty)
                : $"{info.DisplayName}  AP:{info.APCost}";
            var btn = UIFactory.CreateButton("Build_" + facility, row.transform,
                label, BrandGuide.FontHud, isSubCrystal
                    ? BrandGuide.BtnSubCrystalEnabled
                    : BrandGuide.BtnBuildEnabled, defaultFont);
            var btnLE = btn.gameObject.AddComponent<LayoutElement>();
            btnLE.preferredHeight = 56;

            var bg = btn.GetComponent<Image>();
            buildButtons.Add((btn, bg, facility));
            buildTitles[facility] = btn.GetComponentInChildren<TextMeshProUGUI>();

            FacilityKind captured = facility;
            btn.onClick.AddListener(() => OnBuildButtonClicked(captured));

            // ---- コスト表示 ----
            string costStr = isSubCrystal ? "サブクリスタル1消費 (破壊後5T返却) " + FormatBuildCost(info.BuildCost) : FormatBuildCost(info.BuildCost);
            var costTMP = UIFactory.CreateTMP("Cost_" + facility, row.transform, costStr, BrandGuide.FontHudCaption, defaultFont);
            buildCostLabels[facility] = costTMP;
            costTMP.color = BrandGuide.TextSecondary;
            costTMP.alignment = TextAlignmentOptions.MidlineLeft;
            costTMP.textWrappingMode = TMPro.TextWrappingModes.Normal;
            // 長いコストは折り返し、読み取れる文字サイズを保つ
            costTMP.enableAutoSizing = true;
            costTMP.fontSizeMin = 20;
            costTMP.fontSizeMax = BrandGuide.FontHudCaption;
            var costLE = costTMP.gameObject.AddComponent<LayoutElement>();
            costLE.minHeight = 42;
            var costRT = costTMP.GetComponent<RectTransform>();
            costRT.offsetMin = new Vector2(8, 0);
        }

        CreateAuthoredBuildRows(content);
        return go;
    }

    public static string FormatBuildCost(FacilityData.ResourceCost c)
    {
        var p = new List<string>();
        if (c.Wood > 0) p.Add($"木{c.Wood}");
        if (c.Stone > 0) p.Add($"石{c.Stone}");
        if (c.Iron > 0) p.Add($"鉄{c.Iron}");
        if (c.MagicOre > 0) p.Add($"魔{c.MagicOre}");
        if (c.Water > 0) p.Add($"水{c.Water}");
        if (c.Citizen > 0) p.Add($"民{c.Citizen}");
        return string.Join(" ", p);
    }

    private void OnBuildButtonClicked(FacilityKind facility)
    {
        if (buildSystem == null) return;

        if (slidePanel != null) slidePanel.ClosePanel();
        buildSystem.StartBuildMode(facility);
    }

    // ==================================================================
    //  ScrollView（召喚ユニット一覧を生成）
    // ==================================================================
    public GameObject CreateScrollView(string name, RectTransform parent)
    {
        summonButtons.Clear(); // 再生成時に破棄済みボタン参照が残らないようにする
        authoredSummonButtons.Clear();
        var (go, content) = CreateBaseScrollView(name, parent);
        summonContent = content;

        foreach (var kind in SummonableKinds)
        {
            string displayName = KindDisplayNames.TryGetValue(kind, out string dn) ? dn : kind.ToString();
            var row = CreateSummonRow("Row_" + kind, content.transform, "Summon_" + kind,
                "Cost_" + kind, displayName, ResolveSummonDefinition(kind));
            var btn = row.button;
            summonButtons.Add((btn, btn.GetComponent<Image>(), kind, row.title, row.cost));

            Kind captured = kind;
            btn.onClick.AddListener(() => OnSummonButtonClicked(captured));
        }

        CreateAuthoredSummonButtons(content.transform);
        return go;
    }

    private void OnSummonButtonClicked(Kind kind)
    {
        if (summonSystem == null) return;

        if (slidePanel != null) slidePanel.ClosePanel();

        summonSystem.StartSummonMode(kind);
    }

    // ==================================================================
    //  ボタン初期化・更新
    // ==================================================================

    /// <summary>
    /// BuildSystem と FactionState の参照を受け取り、建築ボタンの有効/無効を制御可能にする。
    /// </summary>
    public void InitBuildButtons(BuildSystem bs, APSystem ap, FactionState fs)
    {
        buildSystem = bs;
        cachedAPSystem = ap;
        cachedFactionState = fs;

        // スライドパネルが開かれるたびにボタン状態を更新（再Init時の二重購読を防止）
        if (slidePanel != null)
        {
            slidePanel.OnBuildPanelOpened -= RefreshBuildButtons;
            slidePanel.OnBuildPanelOpened += RefreshBuildButtons;
        }
        RefreshBuildButtons();
    }

    /// <summary>
    /// 建築ボタンの有効/無効・色を更新する。
    /// </summary>
    public void RefreshBuildButtons()
    {
        if (cachedAPSystem == null || cachedFactionState == null) return;

        foreach (var (btn, bg, kind) in buildButtons)
        {
            if (btn == null) continue;
            bool canBuild;
            if (FacilityData.IsSubCrystal(kind))
            {
                canBuild = cachedFactionState.GetSubCrystals(Team.Player) > 0 && cachedAPSystem.CanBuild(Team.Player, kind, cachedFactionState);
            }
            else
            {
                canBuild = cachedAPSystem.CanBuild(Team.Player, kind, cachedFactionState);
            }
            btn.interactable = canBuild;
            if (buildCostLabels.TryGetValue(kind,out var costLabel) && FacilityData.Table.TryGetValue(kind,out var info))
            {
                string cost=FacilityData.IsSubCrystal(kind) ? "サブクリスタル1消費 " + FormatBuildCost(info.BuildCost) : FormatBuildCost(info.BuildCost);
                cost += FormatBuildingDetails(kind, FacilityData.GetLevel(kind, 1));
                if (buildTitles.TryGetValue(kind, out var title) && title != null)
                    title.text = FacilityData.IsSubCrystal(kind) ? info.DisplayName + "  副晶x1" + (info.APCost > 0 ? "  AP:" + info.APCost : string.Empty)
                        : info.DisplayName + "  AP:" + info.APCost;
                string reason=BuildValidator.CostFailure(kind,cachedFactionState);
                costLabel.text=reason==null ? cost : cost+"\n<color=#FFB7A3>"+reason+"</color>";
            }
            if (FacilityData.IsSubCrystal(kind))
            {
                bg.color = canBuild
                    ? BrandGuide.BtnSubCrystalEnabled
                    : BrandGuide.BtnDisabled;
            }
            else
            {
                bg.color = canBuild
                    ? BrandGuide.BtnBuildEnabled
                    : BrandGuide.BtnDisabled;
            }
        }
        RefreshAuthoredBuildButtons();
    }

    /// <summary>
    /// SummonSystem と UnitSetting の参照を受け取り、召喚ボタンを制御可能にする。
    /// </summary>
    public void InitSummonButtons(SummonSystem ss, APSystem ap, FactionState fs, UnitSetting us)
    {
        summonSystem = ss;
        cachedAPSystem = ap;
        cachedFactionState = fs;
        cachedUnitSetting = us;

        // 再Init時の二重購読を防止
        if (slidePanel != null)
        {
            slidePanel.OnUnitPanelOpened -= RefreshSummonButtons;
            slidePanel.OnUnitPanelOpened += RefreshSummonButtons;
        }
        RefreshSummonButtons();
    }

    /// <summary>
    /// 召喚ボタンの有効/無効・色を更新する。
    /// </summary>
    public void RefreshSummonButtons()
    {
        if (summonSystem == null || cachedUnitSetting == null) return;

        foreach (var (btn, bg, kind, title, cost) in summonButtons)
        {
            if (btn == null) continue;
            var data = ResolveSummonDefinition(kind);
            string displayName = data != null && !string.IsNullOrWhiteSpace(data.displayName) ? data.DisplayName
                : KindDisplayNames.TryGetValue(kind, out var name) ? name : kind.ToString();
            RefreshSummonRow(btn, bg, title, cost, data, displayName);
        }
        RefreshAuthoredSummonButtons();
    }
    public void RefreshContentRows()
    {
        if (buildContent != null)
        { RemoveAuthoredRows(buildContent.transform, "AuthoredBuilding_"); CreateAuthoredBuildRows(buildContent); }
        if (summonContent != null)
        { RemoveAuthoredRows(summonContent.transform, "AuthoredUnit_"); authoredSummonButtons.Clear(); CreateAuthoredSummonButtons(summonContent.transform); }
        RefreshBuildButtons(); RefreshSummonButtons();
    }
    static void RemoveAuthoredRows(Transform content, string prefix)
    {
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            var child = content.GetChild(i);
            if (!child.name.StartsWith(prefix, System.StringComparison.Ordinal)) continue;
            child.gameObject.SetActive(false); Object.Destroy(child.gameObject);
        }
    }
    public static string FormatBuildingDetails(FacilityKind kind, FacilityData.FacilityLevelData recipe)
    {
        var parts = new List<string>();
        if (kind == FacilityKind.House || kind == FacilityKind.LuxuryHouse) parts.Add("市民上限 +" + recipe.SpecialValue);
        if (kind == FacilityKind.Warehouse) parts.Add("資源容量 +" + recipe.SpecialValue);
        if (kind == FacilityKind.Barracks) parts.Add("経験値 +" + recipe.SpecialValue + "%");
        if (!recipe.Output.IsEmpty) parts.Add("生産 " + FormatProductionBundle(recipe.Output) + " / " + FacilityData.ProductionInterval(recipe) + "ターン");
        if (recipe.BonusChance1 > 0 && !recipe.BonusOutput1.IsEmpty)
            parts.Add("確率生産 " + FormatProductionBundle(recipe.BonusOutput1) + "（" + (recipe.BonusChance1 * 100).ToString("0.#") + "%）");
        if (recipe.BonusChance2 > 0 && !recipe.BonusOutput2.IsEmpty)
            parts.Add("確率生産 " + FormatProductionBundle(recipe.BonusOutput2) + "（" + (recipe.BonusChance2 * 100).ToString("0.#") + "%）");
        if (!recipe.Input.IsEmpty) parts.Add("生産時消費 " + FormatProductionBundle(recipe.Input));
        if (!recipe.Maintenance.IsEmpty) parts.Add("維持 " + FormatProductionBundle(recipe.Maintenance));
        return parts.Count == 0 ? string.Empty : "\n" + string.Join("\n", parts);
    }
    public static string FormatProductionBundle(FacilityData.ProductionBundle value)
    {
        var parts = new List<string>();
        if (value.Wood > 0) parts.Add("木" + value.Wood); if (value.Stone > 0) parts.Add("石" + value.Stone);
        if (value.Iron > 0) parts.Add("鉄" + value.Iron); if (value.MagicOre > 0) parts.Add("魔石" + value.MagicOre);
        if (value.Wheat > 0) parts.Add("小麦" + value.Wheat); if (value.Bread > 0) parts.Add("パン" + value.Bread);
        if (value.Water > 0) parts.Add("水" + value.Water); if (value.Citizen > 0) parts.Add("市民" + value.Citizen);
        return string.Join(" ", parts);
    }
}
