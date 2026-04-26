using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 駒・ルール説明書（取扱説明書）UI。
/// GameMenuUI から開く。タブで「駒」「ゲームルール」「アーティファクト」などを切替表示する。
/// コンテンツは静的データ（UnitStaticData, SkillData, FacilityData, GameConstants）から自動生成する。
/// </summary>
public class GameManualUI : MonoBehaviour
{
    public static GameManualUI Instance { get; private set; }

    private GameObject overlay;
    private TextMeshProUGUI bodyText;

    private enum Tab { Pieces, Rules, Artifacts, Terrain }
    private Tab currentTab = Tab.Pieces;

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    public static void ShowOrToggle()
    {
        if (Instance == null)
        {
            var go = new GameObject("GameManualUI");
            go.AddComponent<GameManualUI>();
        }
        Instance.Toggle();
    }

    public void Toggle()
    {
        if (overlay != null && overlay.activeSelf) Close();
        else Open();
    }

    public void Open()
    {
        if (overlay != null) Destroy(overlay);
        Build();
        RefreshBody();
    }

    public void Close()
    {
        if (overlay != null) { Destroy(overlay); overlay = null; }
    }

    // ================================================================
    //  UI構築
    // ================================================================
    private void Build()
    {
        var canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        overlay = new GameObject("GameManualOverlay", typeof(RectTransform));
        overlay.transform.SetParent(canvas.transform, false);
        overlay.transform.SetAsLastSibling();
        var rt = overlay.GetComponent<RectTransform>();
        StretchFill(rt);

        var bg = overlay.AddComponent<Image>();
        bg.color = BrandGuide.OverlayBg;

        // パネル
        var panel = new GameObject("ManualPanel", typeof(RectTransform));
        panel.transform.SetParent(overlay.transform, false);
        var panelRT = panel.GetComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0.15f, 0.10f);
        panelRT.anchorMax = new Vector2(0.85f, 0.90f);
        panelRT.offsetMin = Vector2.zero;
        panelRT.offsetMax = Vector2.zero;
        panel.AddComponent<Image>().color = BrandGuide.PanelBgLight;

        // ヘッダー
        var header = CreateTMP("ManualHeader", panel.transform, "駒・ルール説明書", 28, BrandGuide.Primary);
        header.fontStyle = FontStyles.Bold;
        var hrt = header.GetComponent<RectTransform>();
        hrt.anchorMin = new Vector2(0, 0.92f); hrt.anchorMax = new Vector2(1, 1f);
        hrt.offsetMin = Vector2.zero; hrt.offsetMax = Vector2.zero;

        // タブ
        var tabRow = new GameObject("TabRow", typeof(RectTransform));
        tabRow.transform.SetParent(panel.transform, false);
        var trt = tabRow.GetComponent<RectTransform>();
        trt.anchorMin = new Vector2(0.02f, 0.84f); trt.anchorMax = new Vector2(0.98f, 0.92f);
        trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
        var hlg = tabRow.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 8; hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = true;

        AddTabButton(tabRow.transform, "駒", Tab.Pieces);
        AddTabButton(tabRow.transform, "ルール", Tab.Rules);
        AddTabButton(tabRow.transform, "アーティファクト", Tab.Artifacts);
        AddTabButton(tabRow.transform, "地形・戦闘", Tab.Terrain);

        // 本文（スクロール）
        var scrollGO = new GameObject("ScrollView", typeof(RectTransform));
        scrollGO.transform.SetParent(panel.transform, false);
        var srt = scrollGO.GetComponent<RectTransform>();
        srt.anchorMin = new Vector2(0.03f, 0.08f); srt.anchorMax = new Vector2(0.97f, 0.82f);
        srt.offsetMin = Vector2.zero; srt.offsetMax = Vector2.zero;

        var scroll = scrollGO.AddComponent<ScrollRect>();
        scroll.horizontal = false;

        var viewport = new GameObject("Viewport", typeof(RectTransform));
        viewport.transform.SetParent(scrollGO.transform, false);
        var vrt = viewport.GetComponent<RectTransform>();
        StretchFill(vrt);
        viewport.AddComponent<RectMask2D>();
        scroll.viewport = vrt;

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
        var csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = crt;

        bodyText = CreateTMP("ManualBody", content.transform, "", 16, BrandGuide.TextLabel);
        bodyText.alignment = TextAlignmentOptions.TopLeft;
        var brt = bodyText.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0, 0); brt.anchorMax = new Vector2(1, 1);
        brt.offsetMin = new Vector2(10, 10); brt.offsetMax = new Vector2(-10, -10);

        // 閉じる
        var closeRow = new GameObject("CloseRow", typeof(RectTransform));
        closeRow.transform.SetParent(panel.transform, false);
        var crt2 = closeRow.GetComponent<RectTransform>();
        crt2.anchorMin = new Vector2(0.35f, 0.01f); crt2.anchorMax = new Vector2(0.65f, 0.07f);
        crt2.offsetMin = Vector2.zero; crt2.offsetMax = Vector2.zero;
        var closeBtn = CreateBtn("閉じる", closeRow.transform, BrandGuide.BtnWait);
        closeBtn.onClick.AddListener(Close);
    }

    private void AddTabButton(Transform parent, string label, Tab tab)
    {
        var btn = CreateBtn(label, parent, BrandGuide.BtnBuild);
        btn.onClick.AddListener(() => { currentTab = tab; RefreshBody(); });
    }

    private void RefreshBody()
    {
        if (bodyText == null) return;
        switch (currentTab)
        {
            case Tab.Pieces:    bodyText.text = BuildPiecesText(); break;
            case Tab.Rules:     bodyText.text = BuildRulesText(); break;
            case Tab.Artifacts: bodyText.text = BuildArtifactsText(); break;
            case Tab.Terrain:   bodyText.text = BuildTerrainText(); break;
        }
    }

    // ================================================================
    //  コンテンツ生成（静的データから自動生成）
    // ================================================================
    private string BuildPiecesText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<b>■ 駒一覧</b>\n");
        foreach (var kvp in UnitStaticData.Table)
        {
            var k = kvp.Key; var info = kvp.Value;
            sb.AppendLine($"<b>{info.DisplayName}</b>  (Kind: {k})");
            sb.AppendLine($"  Lv1 ATK {info.BaseATK} / HP {info.BaseHP} / DEF {info.BaseDEF}");
            sb.AppendLine($"  成長率 ATK+{info.AtkGrowth*100:F0}% / HP+{info.HpGrowth*100:F0}% / DEF+{info.DefGrowth*100:F0}% / Lv");
            sb.AppendLine();
        }

        sb.AppendLine("<b>■ 特殊駒</b>");
        sb.AppendLine("  <b>クリスタル</b>: 最大HP 10000。破壊されたら敗北。被弾時に反撃を行う");
        sb.AppendLine("  <b>異形の王</b>: Lv1ステータスはキング相当だが成長率が大幅に高く、専用パッシブにより");
        sb.AppendLine("    被ダメ-40%・ダメージの20%を吸血・HP50%以下でATK+25%となる");
        sb.AppendLine("  <b>強敵（縄張りボス）</b>: 縄張り外では被弾無効。縄張り内に駒が入ると攻撃可能");
        return sb.ToString();
    }

    private string BuildRulesText()
    {
        return
"<b>■ 基本ルール</b>\n" +
"  ターン制のチェス風戦略ゲーム。プレイヤーと敵が交互にターンを消費して駒を動かし、相手のクリスタルを破壊した側が勝利。\n\n" +
"<b>■ AP（行動力）</b>\n" +
$"  市民{EconomySystem.APPerCitizen}ごとに追加APを得る。移動{GameConstants.BaseMoveAPCost}・攻撃{GameConstants.BaseAttackAPCost}・建築/召喚でAPを消費する。\n\n" +
"<b>■ 経験値</b>\n" +
$"  Lv2に必要なXP: {GameConstants.XPRequiredLv2}、各レベルで ×{GameConstants.XPLevelMultiplier} 倍ずつ増加。\n" +
"  与ダメージがそのままXPとして獲得できる（倒さなくても蓄積）。兵舎ボーナスで+%が乗る。\n\n" +
"<b>■ クリスタル反撃</b>\n" +
$"  クリスタルが攻撃された時、領土内の敵のうち最も近い1体に相手MaxHPの{GameConstants.CrystalCounterDamageRatio*100:F0}%ダメージ。\n" +
$"  シールド発動後は最大{GameConstants.CrystalCounterTargetsAfterShield}体まで拡大する。DEF無視の固定ダメージ。\n\n" +
"<b>■ 指揮官バフ（キング）</b>\n" +
$"  自軍キングから{GameConstants.KingAuraRange}マス以内の味方はATK+{(GameConstants.KingAuraATKBonus-1)*100:F0}%、DEF+{(GameConstants.KingAuraDEFBonus-1)*100:F0}%。\n\n" +
"<b>■ ダンジョン</b>\n" +
$"  マップに2箇所ランダム生成。サブクリスタルで起動し、{DungeonSystem.ClaimTurns}ターン占有でアーティファクトを獲得。\n" +
"  両陣営が同じダンジョンに侵入するとタイマー停止（競合状態）。";
    }

    private string BuildArtifactsText()
    {
        return
"<b>■ アーティファクト（ダンジョン報酬）</b>\n\n" +
"  <b>クリスタルシャード</b>: 自軍クリスタル最大HP +1000\n" +
"  <b>ウォーバナー</b>: 全ユニット ATK +2\n" +
"  <b>アイアンイージス</b>: 全ユニット DEF +2\n" +
"  <b>マナフォーカス</b>: 毎ターン 魔法鉱石 +3\n" +
"  <b>フェニックスフェザー</b>: クリスタルシールドを再発動可能にする";
    }

    private string BuildTerrainText()
    {
        return
"<b>■ 高低差ルール</b>\n\n" +
$"  <b>低地→高台への攻撃</b>: ダメージ×{GameConstants.LowToHighAttackBonus} （高台側が有利）\n" +
$"  <b>高台からの遠距離攻撃（Y-1対象）</b>: ダメージ+{(GameConstants.HighGroundRangedBonus-1)*100:F0}%\n" +
$"  <b>範囲スキル対象が高台</b>: ダメージ×{GameConstants.AreaSkillHighTargetMod} （-20%）\n" +
$"  <b>範囲スキル対象が低地</b>: ダメージ×{GameConstants.AreaSkillLowTargetMod} （+10%）\n" +
"  <b>高台ユニット</b>: 視界+1マス（周囲1マス平均 +1 以上）\n\n" +
"<b>■ 直線スキルの遮蔽</b>\n" +
$"  壁（WoodWall/StoneWall）または高低差{GameConstants.LineSkillBlockYDiff}以上のタイルがあると、それ以降のマスには当たらない。\n\n" +
"<b>■ パッシブ・背面攻撃</b>\n" +
$"  背面攻撃ダメージ +{(GameConstants.BackAttackBonus-1)*100:F0}%、Assassinationパッシブでさらに×{GameConstants.AssassinationBackAttackBonus}。\n" +
$"  HunterEyes: 視界内敵へ +{(GameConstants.HunterEyesDamageBonus-1)*100:F0}%  /  Destroyer: 建物・クリスタルへ +{(GameConstants.DestroyerBuildingBonus-1)*100:F0}%\n" +
$"  Sniper: 距離{GameConstants.SniperMinRange}以上で+{(GameConstants.SniperLongRangeBonus-1)*100:F0}%  /  Impregnable: 被ダメ×{GameConstants.ImpregnableDamageReduction}";
    }

    // ================================================================
    //  UI ヘルパー（GameMenuUI と同じ書式）
    // ================================================================
    private static void StretchFill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private static TextMeshProUGUI CreateTMP(string name, Transform parent, string text, int size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = size; tmp.color = color;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        return tmp;
    }

    private static Button CreateBtn(string label, Transform parent, Color color)
    {
        var go = new GameObject("Btn_" + label, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>(); img.color = color;
        var btn = go.AddComponent<Button>();
        var txt = CreateTMP("Label", go.transform, label, 16, Color.white);
        txt.alignment = TextAlignmentOptions.Center;
        var trt = txt.GetComponent<RectTransform>();
        StretchFill(trt);
        var le = go.AddComponent<LayoutElement>(); le.minHeight = 40;
        return btn;
    }
}
