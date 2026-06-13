using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Fantasy Kingdom ブランドガイド
/// UI 全体で統一された配色・スタイルを定義する静的クラス。
/// 各 UI スクリプトはここから色・サイズを参照し、一貫したデザインを保つ。
/// </summary>
public static class BrandGuide
{
    // ==================================================================
    //  カラーパレット — 基本色
    // ==================================================================

    /// <summary>プライマリ（王国ゴールド）: タイトル・重要アクセント</summary>
    public static readonly Color Primary = new Color(0.85f, 0.72f, 0.35f, 1f);

    /// <summary>セカンダリ（ロイヤルブルー）: ボタン・リンク・選択状態</summary>
    public static readonly Color Secondary = new Color(0.30f, 0.55f, 0.85f, 1f);

    /// <summary>アクセント（エメラルド）: 成功・正の値</summary>
    public static readonly Color Accent = new Color(0.30f, 0.75f, 0.45f, 1f);

    /// <summary>警告（アンバー）: 注意・中間状態</summary>
    public static readonly Color Warning = new Color(0.90f, 0.70f, 0.20f, 1f);

    /// <summary>危険（クリムゾン）: エラー・攻撃・HP低下</summary>
    public static readonly Color Danger = new Color(0.85f, 0.25f, 0.22f, 1f);

    // ==================================================================
    //  パネル背景色
    // ==================================================================

    /// <summary>メインパネル背景: 高不透明度で視認性確保</summary>
    public static readonly Color PanelBg = new Color(0.05f, 0.05f, 0.08f, 0.94f);

    /// <summary>サブパネル背景: やや明るめ</summary>
    public static readonly Color PanelBgLight = new Color(0.08f, 0.08f, 0.12f, 0.94f);

    /// <summary>オーバーレイ背景: モーダル用</summary>
    public static readonly Color OverlayBg = new Color(0.02f, 0.02f, 0.04f, 0.70f);

    /// <summary>パネルボーダー色</summary>
    public static readonly Color Border = new Color(0.40f, 0.35f, 0.22f, 0.50f);

    /// <summary>薄いボーダー（区切り線用）</summary>
    public static readonly Color BorderLight = new Color(1f, 1f, 1f, 0.12f);

    /// <summary>ハイライトボーダー（選択・ホバー時）</summary>
    public static readonly Color BorderHighlight = new Color(0.85f, 0.72f, 0.35f, 0.60f);

    // ==================================================================
    //  テキストカラー
    // ==================================================================

    /// <summary>通常テキスト</summary>
    public static readonly Color TextPrimary = new Color(0.93f, 0.91f, 0.86f, 1f);

    /// <summary>補助テキスト（薄め）</summary>
    public static readonly Color TextSecondary = new Color(0.70f, 0.68f, 0.62f, 1f);

    /// <summary>無効テキスト（暗背景でも判読できるコントラストを確保）</summary>
    public static readonly Color TextDisabled = new Color(0.56f, 0.54f, 0.50f, 1f);

    /// <summary>ラベル色（項目名）</summary>
    public static readonly Color TextLabel = new Color(0.80f, 0.76f, 0.60f, 1f);

    // ==================================================================
    //  資源カテゴリ背景色（高コントラスト版）
    // ==================================================================

    public static readonly Color ResCatRaw = new Color(0.18f, 0.14f, 0.08f, 0.75f);
    public static readonly Color ResCatMineral = new Color(0.10f, 0.14f, 0.22f, 0.75f);
    public static readonly Color ResCatProcessed = new Color(0.14f, 0.10f, 0.22f, 0.75f);
    public static readonly Color ResCatFood = new Color(0.22f, 0.16f, 0.06f, 0.75f);
    public static readonly Color ResCatPopulation = new Color(0.08f, 0.20f, 0.10f, 0.75f);
    public static readonly Color ResCatCrystal = new Color(0.08f, 0.16f, 0.25f, 0.75f);

    // ==================================================================
    //  ボタン色
    // ==================================================================

    public static readonly Color BtnAttack = new Color(0.72f, 0.20f, 0.18f, 1f);
    public static readonly Color BtnSkill = new Color(0.22f, 0.38f, 0.72f, 1f);
    public static readonly Color BtnWait = new Color(0.38f, 0.38f, 0.38f, 1f);
    public static readonly Color BtnCancel = new Color(0.52f, 0.38f, 0.12f, 1f);
    public static readonly Color BtnBuild = new Color(0.20f, 0.48f, 0.22f, 1f);
    public static readonly Color BtnUnit = new Color(0.20f, 0.32f, 0.58f, 1f);
    public static readonly Color BtnMenu = new Color(0.28f, 0.28f, 0.32f, 1f);
    public static readonly Color BtnClose = new Color(0.62f, 0.15f, 0.15f, 1f);
    public static readonly Color BtnUpgrade = new Color(0.15f, 0.48f, 0.15f, 1f);
    public static readonly Color BtnDestroy = new Color(0.62f, 0.15f, 0.15f, 1f);
    public static readonly Color BtnDisabled = new Color(0.50f, 0.15f, 0.15f, 1f);

    /// <summary>建築可能（サブクリスタル）</summary>
    public static readonly Color BtnSubCrystalEnabled = new Color(0.15f, 0.32f, 0.48f, 1f);
    /// <summary>建築可能（通常施設）</summary>
    public static readonly Color BtnBuildEnabled = new Color(0.20f, 0.38f, 0.22f, 1f);
    /// <summary>召喚可能</summary>
    public static readonly Color BtnSummonEnabled = new Color(0.22f, 0.28f, 0.48f, 1f);

    // ==================================================================
    //  ダメージ / フィードバック色
    // ==================================================================

    public static readonly Color FeedbackDamage = new Color(1f, 0.28f, 0.22f);
    public static readonly Color FeedbackHeal = new Color(0.35f, 1f, 0.45f);
    public static readonly Color FeedbackShield = new Color(0.50f, 0.82f, 1f);
    public static readonly Color FeedbackKill = new Color(1f, 0.90f, 0.15f);
    public static readonly Color FeedbackMiss = new Color(0.60f, 0.60f, 0.60f);

    // ==================================================================
    //  タイマーバー色
    // ==================================================================

    public static readonly Color TimerNormal = new Color(0.22f, 0.58f, 0.82f, 0.92f);
    public static readonly Color TimerWarning = new Color(0.82f, 0.62f, 0.22f, 0.92f);
    public static readonly Color TimerDanger = new Color(0.82f, 0.22f, 0.22f, 0.92f);
    public static readonly Color TimerBg = new Color(0.04f, 0.04f, 0.06f, 0.85f);

    // ==================================================================
    //  チーム色
    // ==================================================================

    public static readonly Color TeamPlayer = new Color(0.53f, 0.80f, 1f);
    public static readonly Color TeamEnemy = new Color(1f, 0.53f, 0.53f);

    // ==================================================================
    //  カーソル・配置プレビュー色
    // ==================================================================

    /// <summary>建築カーソル: 設置可能（緑半透明）</summary>
    public static readonly Color CursorBuildValid = new Color(0.5f, 1f, 0.5f, 0.5f);
    /// <summary>建築カーソル: 設置不可（赤半透明）</summary>
    public static readonly Color CursorBuildInvalid = new Color(1f, 0.3f, 0.3f, 0.5f);
    /// <summary>召喚カーソル: 設置可能（青半透明）</summary>
    public static readonly Color CursorSummonValid = new Color(0.5f, 0.5f, 1f, 0.5f);
    /// <summary>召喚カーソル: 設置不可（赤半透明）</summary>
    public static readonly Color CursorSummonInvalid = new Color(1f, 0.3f, 0.3f, 0.5f);

    // ==================================================================
    //  フォールバックオブジェクト色（プレハブ未割当時）
    // ==================================================================

    /// <summary>壁（フォールバック用グレー）</summary>
    public static readonly Color FallbackWall = new Color(0.6f, 0.6f, 0.6f);
    /// <summary>サブクリスタル（フォールバック用シアン）</summary>
    public static readonly Color FallbackSubCrystal = new Color(0.3f, 0.7f, 0.9f);
    /// <summary>通常建築物（フォールバック用ブラウン）</summary>
    public static readonly Color FallbackBuilding = new Color(0.6f, 0.4f, 0.2f);
    /// <summary>プレイヤーユニット（フォールバック用ブルー）</summary>
    public static readonly Color FallbackPlayerUnit = new Color(0.3f, 0.5f, 0.9f);
    /// <summary>敵ユニット（フォールバック用レッド）</summary>
    public static readonly Color FallbackEnemyUnit = new Color(0.9f, 0.3f, 0.3f);

    /// <summary>施設種別に応じたフォールバック色を返す</summary>
    public static Color GetFacilityFallbackColor(FacilityKind facility)
    {
        if (FacilityData.IsWall(facility)) return FallbackWall;
        if (FacilityData.IsSubCrystal(facility)) return FallbackSubCrystal;
        return FallbackBuilding;
    }

    /// <summary>チームに応じたユニットフォールバック色を返す</summary>
    public static Color GetUnitFallbackColor(Team team)
    {
        return team == Team.Player ? FallbackPlayerUnit : FallbackEnemyUnit;
    }

    // ==================================================================
    //  フォントサイズ定義
    // ==================================================================

    public const float FontTitle = 36f;
    public const float FontHeader = 28f;
    public const float FontBody = 20f;
    public const float FontCaption = 16f;
    public const float FontSmall = 14f; // 可読性確保のため14未満にしない

    // ==================================================================
    //  ボタンスタイリング
    // ==================================================================

    /// <summary>ボタンに統一されたインタラクション色を適用する</summary>
    public static void ApplyButtonStyle(Button btn, Color baseColor)
    {
        if (btn == null) return;

        var img = btn.GetComponent<Image>();
        if (img != null) img.color = baseColor;

        btn.targetGraphic = img;

        var cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        cb.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        cb.selectedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
        cb.disabledColor = new Color(0.35f, 0.35f, 0.35f, 0.70f);
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.08f;
        btn.colors = cb;
    }

    // ==================================================================
    //  パネル装飾ヘルパー
    // ==================================================================

    /// <summary>パネルにゴールドのボーダー（上辺のみ）を追加する</summary>
    public static void AddTopBorder(RectTransform parent, float height = 2f)
    {
        var borderGo = new GameObject("TopBorder", typeof(RectTransform));
        borderGo.transform.SetParent(parent, false);
        var img = borderGo.AddComponent<Image>();
        img.color = Border;
        var rt = borderGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.sizeDelta = new Vector2(0, height);
        rt.anchoredPosition = Vector2.zero;
    }

    /// <summary>パネルに下辺ボーダーを追加する</summary>
    public static void AddBottomBorder(RectTransform parent, float height = 1f)
    {
        var borderGo = new GameObject("BottomBorder", typeof(RectTransform));
        borderGo.transform.SetParent(parent, false);
        var img = borderGo.AddComponent<Image>();
        img.color = Border;
        var rt = borderGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 0);
        rt.pivot = new Vector2(0.5f, 0);
        rt.sizeDelta = new Vector2(0, height);
        rt.anchoredPosition = Vector2.zero;
    }

    /// <summary>パネルの全辺にボーダーを追加する</summary>
    public static void AddPanelBorder(RectTransform parent, float thickness = 1.5f)
    {
        AddEdge(parent, "BorderTop", new Vector2(0, 1), new Vector2(1, 1),
            new Vector2(0.5f, 1), new Vector2(0, thickness));
        AddEdge(parent, "BorderBottom", new Vector2(0, 0), new Vector2(1, 0),
            new Vector2(0.5f, 0), new Vector2(0, thickness));
        AddEdge(parent, "BorderLeft", new Vector2(0, 0), new Vector2(0, 1),
            new Vector2(0, 0.5f), new Vector2(thickness, 0));
        AddEdge(parent, "BorderRight", new Vector2(1, 0), new Vector2(1, 1),
            new Vector2(1, 0.5f), new Vector2(thickness, 0));
    }

    private static void AddEdge(RectTransform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = Border;
        img.raycastTarget = false;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.sizeDelta = sizeDelta;
        rt.anchoredPosition = Vector2.zero;
    }

    // ==================================================================
    //  HP バー色ヘルパー
    // ==================================================================

    /// <summary>HP 割合に応じた色 hex を返す</summary>
    public static string HPColorHex(float ratio)
    {
        if (ratio > 0.5f) return "#AAFFAA";
        if (ratio > 0.25f) return "#FFD966";
        return "#FF5555";
    }

    /// <summary>HP 割合に応じた Color を返す</summary>
    public static Color HPColor(float ratio)
    {
        if (ratio > 0.5f) return new Color(0.67f, 1f, 0.67f);
        if (ratio > 0.25f) return new Color(1f, 0.85f, 0.40f);
        return new Color(1f, 0.33f, 0.33f);
    }

    // ==================================================================
    //  AP 表示ヘルパー
    // ==================================================================

    public static string APCostColorHex(bool canAfford)
    {
        return canAfford ? "#AAFFAA" : "#FF6666";
    }

    /// <summary>AP 残量に応じた色</summary>
    public static Color APColor(int current, int max)
    {
        if (max <= 0) return TextPrimary;
        float ratio = (float)current / max;
        if (ratio <= 0.15f) return Danger;
        if (ratio <= 0.35f) return Warning;
        return new Color(0.50f, 0.90f, 1f);
    }
}
