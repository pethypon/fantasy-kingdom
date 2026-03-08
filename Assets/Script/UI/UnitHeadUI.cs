using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ユニットの頭上に Lv と HP バーを表示する WorldSpace Canvas。
/// Status コンポーネントと同じ GameObject（またはその親）にアタッチされる。
/// </summary>
public class UnitHeadUI : MonoBehaviour
{
    private Status status;
    private int maxHP;
    private Image hpFillImage;
    private TextMeshProUGUI lvText;
    private Canvas canvas;

    // 前フレームの値をキャッシュして変更時のみ更新
    private int cachedHP = -1;
    private int cachedLevel = -1;

    /// <summary>
    /// ユニットに頭上UIをアタッチする。SpawnUnit から呼ぶ。
    /// </summary>
    public static UnitHeadUI Attach(GameObject unitObj)
    {
        var status = unitObj.GetComponentInChildren<Status>();
        if (status == null) return null;

        // Building / Crystal には表示しない
        if (status.type != Type.Unit) return null;

        var ui = unitObj.AddComponent<UnitHeadUI>();
        ui.status = status;
        ui.maxHP = Mathf.Max(1, status.HP);
        ui.Build();
        return ui;
    }

    private void Build()
    {
        // ---- WorldSpace Canvas ----
        var canvasGo = new GameObject("HeadUI");
        canvasGo.transform.SetParent(transform, false);
        canvasGo.transform.localPosition = new Vector3(0, 2.2f, 0);
        canvasGo.transform.localScale = Vector3.one * 0.02f;

        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 50;

        var rt = canvasGo.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(120, 50);
        rt.pivot = new Vector2(0.5f, 0);

        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();

        // ---- Root レイアウト (横並び: Lvサークル + HPバー) ----
        var root = new GameObject("Root", typeof(RectTransform));
        root.transform.SetParent(canvasGo.transform, false);
        var rootRT = root.GetComponent<RectTransform>();
        rootRT.anchorMin = Vector2.zero;
        rootRT.anchorMax = Vector2.one;
        rootRT.offsetMin = Vector2.zero;
        rootRT.offsetMax = Vector2.zero;

        var hlg = root.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.spacing = 4;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;

        // ---- Lv サークル ----
        var lvCircle = new GameObject("LvCircle", typeof(RectTransform));
        lvCircle.transform.SetParent(root.transform, false);
        var circleRT = lvCircle.GetComponent<RectTransform>();
        circleRT.sizeDelta = new Vector2(32, 32);

        var circleImg = lvCircle.AddComponent<Image>();
        bool isPlayer = status.team == Team.Player;
        circleImg.color = isPlayer
            ? new Color(0.2f, 0.5f, 0.85f, 0.95f)
            : new Color(0.75f, 0.2f, 0.2f, 0.95f);

        var circleLE = lvCircle.AddComponent<LayoutElement>();
        circleLE.preferredWidth = 32;
        circleLE.preferredHeight = 32;

        lvText = CreateText("LvText", lvCircle.transform, "1", 18);
        lvText.alignment = TextAlignmentOptions.Center;
        lvText.fontStyle = FontStyles.Bold;
        var lvRT = lvText.GetComponent<RectTransform>();
        lvRT.anchorMin = Vector2.zero;
        lvRT.anchorMax = Vector2.one;
        lvRT.offsetMin = Vector2.zero;
        lvRT.offsetMax = Vector2.zero;

        // ---- HP バー ----
        var hpBar = new GameObject("HPBar", typeof(RectTransform));
        hpBar.transform.SetParent(root.transform, false);
        var hpBarRT = hpBar.GetComponent<RectTransform>();
        hpBarRT.sizeDelta = new Vector2(70, 16);

        var hpBarLE = hpBar.AddComponent<LayoutElement>();
        hpBarLE.preferredWidth = 70;
        hpBarLE.preferredHeight = 16;

        // 背景
        var hpBg = new GameObject("HPBg", typeof(RectTransform));
        hpBg.transform.SetParent(hpBar.transform, false);
        var bgImg = hpBg.AddComponent<Image>();
        bgImg.color = new Color(0.15f, 0.15f, 0.15f, 0.9f);
        var bgRT = hpBg.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;

        // 塗り
        var hpFill = new GameObject("HPFill", typeof(RectTransform));
        hpFill.transform.SetParent(hpBar.transform, false);
        hpFillImage = hpFill.AddComponent<Image>();
        hpFillImage.color = new Color(0.2f, 0.8f, 0.25f, 0.95f);
        var fillRT = hpFill.GetComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = new Vector2(1, 1);
        fillRT.pivot = new Vector2(0, 0.5f);
        fillRT.offsetMin = new Vector2(1, 1);
        fillRT.offsetMax = new Vector2(-1, -1);

        // HP テキスト
        var hpText = CreateText("HPText", hpBar.transform, "HP", 10);
        hpText.alignment = TextAlignmentOptions.Center;
        var hpTextRT = hpText.GetComponent<RectTransform>();
        hpTextRT.anchorMin = Vector2.zero;
        hpTextRT.anchorMax = Vector2.one;
        hpTextRT.offsetMin = Vector2.zero;
        hpTextRT.offsetMax = Vector2.zero;

        Refresh();
    }

    private void LateUpdate()
    {
        if (status == null) return;

        // カメラの方を向く（ビルボード）
        if (canvas != null && Camera.main != null)
        {
            canvas.transform.forward = Camera.main.transform.forward;
        }

        // 値が変わった時だけ更新
        if (cachedHP != status.HP || cachedLevel != status.Level)
        {
            Refresh();
        }
    }

    /// <summary>
    /// maxHP を再設定する（レベルアップ時など）。
    /// </summary>
    public void ResetMaxHP(int newMax)
    {
        maxHP = Mathf.Max(1, newMax);
        Refresh();
    }

    private void Refresh()
    {
        if (status == null) return;

        cachedHP = status.HP;
        cachedLevel = status.Level;

        // Lv テキスト
        if (lvText != null)
            lvText.text = cachedLevel.ToString();

        // HP バー
        if (hpFillImage != null)
        {
            float ratio = Mathf.Clamp01((float)cachedHP / maxHP);
            var fillRT = hpFillImage.GetComponent<RectTransform>();
            fillRT.anchorMax = new Vector2(ratio, 1);

            // HP に応じて色を変える
            if (ratio > 0.5f)
                hpFillImage.color = new Color(0.2f, 0.8f, 0.25f, 0.95f);
            else if (ratio > 0.25f)
                hpFillImage.color = new Color(0.9f, 0.7f, 0.1f, 0.95f);
            else
                hpFillImage.color = new Color(0.85f, 0.15f, 0.15f, 0.95f);
        }
    }

    private TextMeshProUGUI CreateText(string name, Transform parent, string text, float fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = Color.white;
        tmp.enableWordWrapping = false;

        // フォント読み込み
        var font = Resources.Load<TMP_FontAsset>("Fonts & Materials/NotoSansJP-VariableFont_wght SDF");
        if (font == null)
            font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font != null)
            tmp.font = font;

        return tmp;
    }
}
