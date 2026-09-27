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
    private int cachedMaxHP = -1;
    private TextMeshProUGUI hpText;
    private Image hpFillImage;
    private TextMeshProUGUI lvText;
    private Canvas canvas;
    private Renderer unitRenderer; // 視界判定用

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
        ui.unitRenderer = unitObj.GetComponentInChildren<Renderer>();
        ui.Build();
        return ui;
    }

    private void Build()
    {
        var canvasGo = new GameObject("HeadUI", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        canvasGo.transform.localPosition = new Vector3(0, 1.05f, 0);
        canvasGo.transform.localScale = Vector3.one * 0.02f;
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 50;
        var rt = (RectTransform)canvasGo.transform;
        rt.sizeDelta = new Vector2(124, 34);
        rt.pivot = new Vector2(0.5f, 0);
        // Informational only: no raycaster or full-width backing plate.
        var theme = GameUITheme.Current;
        var badge = CreateGraphic("LevelBadge", canvasGo.transform, new Vector2(0, 0.5f),
            new Vector2(16, 0), new Vector2(32, 32), theme != null ? theme.levelBadge : null);
        badge.color = status.team == Team.Player ? new Color(0.8f, 0.92f, 1) : new Color(1, 0.65f, 0.55f);
        lvText = CreateText("LvText", badge.transform, "1", 16);
        lvText.fontStyle = FontStyles.Bold;
        lvText.alignment = TextAlignmentOptions.Center;
        UIFactory.StretchFill(lvText.rectTransform);
        lvText.margin = new Vector4(5, 5, 5, 5);

        var hpFrame = CreateGraphic("HPFrame", canvasGo.transform, new Vector2(1, 0.5f),
            new Vector2(-44, 0), new Vector2(88, 18), theme != null ? theme.healthFrame : null);
        var well = CreateGraphic("HPWell", hpFrame.transform, new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(64, 9), null);
        well.color = new Color(0.04f, 0.06f, 0.07f, 1);
        hpFillImage = CreateGraphic("HPFill", well.transform, new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(64, 9), theme != null ? theme.healthFill : null);
        hpFillImage.type = Image.Type.Filled;
        hpFillImage.fillMethod = Image.FillMethod.Horizontal;
        hpFillImage.fillOrigin = 0;
        hpText = CreateText("HPText", hpFrame.transform, "", 10);
        hpText.alignment = TextAlignmentOptions.Center;
        hpText.fontStyle = FontStyles.Bold;
        UIFactory.StretchFill(hpText.rectTransform);
        hpText.margin = new Vector4(11, 2, 11, 2);
        hpText.enableAutoSizing = true;
        hpText.fontSizeMin = 8;
        hpText.fontSizeMax = 10;
        Refresh();
    }

    private static Image CreateGraphic(string name, Transform parent, Vector2 anchor,
        Vector2 position, Vector2 size, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.raycastTarget = false;
        return image;
    }
    private void LateUpdate()
    {
        if (status == null || canvas == null) return;

        // 死亡駒は HandleDeathIfDead で SetActive(false) されるため
        // LateUpdate 自体が呼ばれなくなるが、HP==0 のまま親が active な
        // 過渡状態（毒ダメージ→VisionPoint まで）でも頭上UIを即時非表示にする。
        if (status.HP <= 0)
        {
            if (canvas.gameObject.activeSelf)
                canvas.gameObject.SetActive(false);
            return;
        }

        // 視界判定: ユニットの Renderer が非表示なら UI も隠す
        // VisionGenerator が敵ユニットの Renderer.enabled を切り替えるので、それに連動
        if (unitRenderer != null)
        {
            bool visible = unitRenderer.enabled;
            if (canvas.gameObject.activeSelf != visible)
                canvas.gameObject.SetActive(visible);

            if (!visible) return;
        }

        // カメラの方を向く（ビルボード）
        if (Camera.main != null)
        {
            canvas.transform.forward = Camera.main.transform.forward;
        }

        // 値が変わった時だけ更新
        if (cachedHP != status.HP || cachedMaxHP != status.MaxHP || cachedLevel != status.Level)
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        if (status == null) return;

        cachedHP = status.HP;
        cachedMaxHP = status.MaxHP;
        if (hpText != null)
            hpText.text = $"{cachedHP}/{Mathf.Max(1, cachedMaxHP)}";
        cachedLevel = status.Level;

        // Lv テキスト
        if (lvText != null)
            lvText.text = cachedLevel.ToString();

        // HP バー
        if (hpFillImage != null)
        {
            float ratio = Mathf.Clamp01((float)cachedHP / Mathf.Max(1, cachedMaxHP));
            hpFillImage.fillAmount = ratio;

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
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;

        // フォント読み込み
        var font = Resources.Load<TMP_FontAsset>("Fonts & Materials/NotoSansJP-VariableFont_wght SDF");
        if (font == null)
            font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font != null)
            tmp.font = font;

        return tmp;
    }
}
