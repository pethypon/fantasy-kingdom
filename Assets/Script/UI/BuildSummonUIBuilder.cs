using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 建築・召喚スクロールビューの生成とボタン管理を担当するクラス。
/// UIBuilder から生成され、保持される。
/// </summary>
public class BuildSummonUIBuilder
{
    // 召喚可能なユニット種別（Crystal/King/壁は除外）
    public static readonly Kind[] SummonableKinds = {
        Kind.Knight, Kind.Archer, Kind.Magic, Kind.Assassin,
        Kind.Scout, Kind.Priest, Kind.Guardian, Kind.Crossbow,
        Kind.Magicsniper, Kind.Bomber,
    };

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
    };

    // 建築ボタン管理用
    private readonly List<(Button btn, Image bg, FacilityKind kind)> buildButtons
        = new List<(Button, Image, FacilityKind)>();
    // 召喚ボタン管理用
    private readonly List<(Button btn, Image bg, Kind kind)> summonButtons
        = new List<(Button, Image, Kind)>();

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
    //  BuildScrollView（建築物一覧を生成）
    // ==================================================================
    public GameObject CreateBuildScrollView(string name, RectTransform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        UIFactory.StretchFill(rt);

        var scrollImg = go.AddComponent<Image>();
        scrollImg.color = new Color(0.12f, 0.12f, 0.12f, 0.5f);

        // Viewport
        var viewport = new GameObject("Viewport", typeof(RectTransform));
        viewport.transform.SetParent(go.transform, false);
        var vpRT = viewport.GetComponent<RectTransform>();
        UIFactory.StretchFill(vpRT);
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
        sr.verticalScrollbar = UIFactory.CreateVerticalScrollbar(go.transform);
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
            var btn = UIFactory.CreateButton("Build_" + facility, row.transform,
                label, BrandGuide.FontCaption, isSubCrystal
                    ? BrandGuide.BtnSubCrystalEnabled
                    : BrandGuide.BtnBuildEnabled, defaultFont);
            var btnLE = btn.gameObject.AddComponent<LayoutElement>();
            btnLE.preferredHeight = 40;

            var bg = btn.GetComponent<Image>();
            buildButtons.Add((btn, bg, facility));

            FacilityKind captured = facility;
            btn.onClick.AddListener(() => OnBuildButtonClicked(captured));

            // ---- コスト表示 ----
            string costStr = isSubCrystal ? "サブクリスタル1消費 (破壊後5T返却)" : FormatBuildCost(info.BuildCost);
            var costTMP = UIFactory.CreateTMP("Cost_" + facility, row.transform, costStr, BrandGuide.FontSmall + 2, defaultFont);
            costTMP.color = BrandGuide.TextSecondary;
            costTMP.alignment = TextAlignmentOptions.MidlineLeft;
            costTMP.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            costTMP.overflowMode = TextOverflowModes.Ellipsis;
            var costLE = costTMP.gameObject.AddComponent<LayoutElement>();
            costLE.preferredHeight = 28;
            var costRT = costTMP.GetComponent<RectTransform>();
            costRT.offsetMin = new Vector2(8, 0);
        }

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
        if (c.Plank > 0) p.Add($"板{c.Plank}");
        if (c.CutStone > 0) p.Add($"切{c.CutStone}");
        if (c.Citizen > 0) p.Add($"民{c.Citizen}");
        return string.Join(" ", p);
    }

    private void OnBuildButtonClicked(FacilityKind facility)
    {
        if (buildSystem == null) return;

        // パネルを閉じる
        if (slidePanel != null) slidePanel.ClosePanel();

        // 建築モード開始
        buildSystem.StartBuildMode(facility);

        // PlayerMove の BuildMode を有効にする
        var turnGen = Object.FindFirstObjectByType<TurnGenerator>();
        if (turnGen != null)
        {
            // 現在のステートが PlayerMove であることを前提にフラグを設定
            // BuildSystem.IsActive で PlayerMove.Update 内で判定する
        }
    }

    // ==================================================================
    //  ScrollView（召喚ユニット一覧を生成）
    // ==================================================================
    public GameObject CreateScrollView(string name, RectTransform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        UIFactory.StretchFill(rt);

        var scrollImg = go.AddComponent<Image>();
        scrollImg.color = new Color(0.12f, 0.12f, 0.12f, 0.5f);

        // Viewport
        var viewport = new GameObject("Viewport", typeof(RectTransform));
        viewport.transform.SetParent(go.transform, false);
        var vpRT = viewport.GetComponent<RectTransform>();
        UIFactory.StretchFill(vpRT);
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
        sr.verticalScrollbar = UIFactory.CreateVerticalScrollbar(go.transform);
        sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;

        // ユニット召喚ボタンを生成
        foreach (var kind in SummonableKinds)
        {
            string displayName = KindDisplayNames.TryGetValue(kind, out string dn) ? dn : kind.ToString();
            string label = $"{displayName}";
            var btn = UIFactory.CreateButton("Summon_" + kind, content.transform,
                label, BrandGuide.FontCaption, BrandGuide.BtnSummonEnabled, defaultFont);

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

        // スライドパネルが開かれるたびにボタン状態を更新
        if (slidePanel != null)
            slidePanel.OnBuildPanelOpened += RefreshBuildButtons;
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

        if (slidePanel != null)
            slidePanel.OnUnitPanelOpened += RefreshSummonButtons;
    }

    /// <summary>
    /// 召喚ボタンの有効/無効・色を更新する。
    /// </summary>
    public void RefreshSummonButtons()
    {
        if (summonSystem == null || cachedUnitSetting == null) return;

        foreach (var (btn, bg, kind) in summonButtons)
        {
            bool canSummon = summonSystem.CanSummon(Team.Player, kind);
            btn.interactable = canSummon;
            bg.color = canSummon
                ? BrandGuide.BtnSummonEnabled
                : BrandGuide.BtnDisabled;
        }
    }
}
