using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ユニットの頭上に Lv と HP バーを表示する。
///
/// Screen Space Overlay 上にUIを表示することで、
/// 3Dユニットより常に手前へ描画する。
///
/// さらに、3D空間上で一定の大きさに見えるように
/// カメラのズーム・距離に応じてUIサイズを自動調整する。
/// </summary>
public class UnitHeadUI : MonoBehaviour
{
    // ============================================================
    // 参照
    // ============================================================

    private Status status;

    private TextMeshProUGUI hpText;
    private Image hpFillImage;
    private TextMeshProUGUI lvText;

    private Renderer unitRenderer;

    private Camera mainCamera;

    // Fantasy Kingdom の Screen Space Overlay Canvas
    private Canvas screenCanvas;

    // 実際に画面へ表示する頭上UI
    private GameObject headUIObject;
    private RectTransform headUIRect;


    // ============================================================
    // キャッシュ
    // ============================================================

    private int cachedHP = -1;
    private int cachedMaxHP = -1;
    private int cachedLevel = -1;


    // ============================================================
    // 設定
    // ============================================================

    /// <summary>
    /// ユニット原点から頭上UIを置く高さ。
    /// </summary>
    private const float HeadHeight = 1.05f;

    /// <summary>
    /// 元のWorldSpace UIの横幅。
    ///
    /// 元コード:
    /// 124 × 0.015 = 1.86 Unity単位
    ///
    /// この値を基準に、
    /// カメラから見た画面上のサイズを計算する。
    /// </summary>
    private const float HeadUIWorldWidth = 2.5f;

    /// <summary>
    /// UIの基準幅。
    /// </summary>
    private const float BaseUIWidth = 124f;

    /// <summary>
    /// UIの基準高さ。
    /// </summary>
    private const float BaseUIHeight = 34f;

    /// <summary>
    /// UIが極端に小さくならないようにする。
    /// </summary>
    private const float MinHeadUIScale = 0.50f;

    /// <summary>
    /// UIが極端に巨大にならないようにする。
    /// </summary>
    private const float MaxHeadUIScale = 3.00f;

    /// <summary>
    /// 頭上位置からさらに画面上方向へずらす量。
    /// </summary>
    private const float ScreenYOffset = 8f;


    // ============================================================
    // Attach
    // ============================================================

    /// <summary>
    /// ユニットに頭上UIをアタッチする。
    /// SpawnUnit などから呼び出す。
    /// </summary>
    public static UnitHeadUI Attach(GameObject unitObj)
    {
        if (unitObj == null) return null;
        var existing = unitObj.GetComponent<UnitHeadUI>();
        if (existing != null) return existing;
        if (unitObj == null)
            return null;

        var status =
            unitObj.GetComponentInChildren<Status>();

        if (status == null)
            return null;

        // Building / Crystal には表示しない
        if (status.type != Type.Unit)
            return null;

        var ui =
            unitObj.AddComponent<UnitHeadUI>();

        ui.status = status;

        ui.unitRenderer =
            unitObj.GetComponentInChildren<Renderer>();

        ui.Build();

        return ui;
    }


    // ============================================================
    // Build
    // ============================================================

    private void Build()
    {
        mainCamera = Camera.main;

        // ========================================================
        // Screen Space Canvas取得
        // ========================================================

        screenCanvas =
            UIBuilder.ScreenCanvas;

        if (screenCanvas == null)
        {
            Debug.LogError(
                "[UnitHeadUI] UIBuilder.ScreenCanvas が見つかりません。"
            );

            return;
        }


        // ========================================================
        // HeadUI本体
        // ========================================================

        headUIObject =
            new GameObject(
                "HeadUI",
                typeof(RectTransform)
            );

        // ユニットの子ではなく
        // ScreenSpace Canvasの子にする
        var headLayer = screenCanvas.transform.Find("UnitHeads");
        if (headLayer == null) {
            var container = new GameObject("UnitHeads",typeof(RectTransform));
            headLayer=container.transform; headLayer.SetParent(screenCanvas.transform,false);
            UIFactory.StretchFill(container.GetComponent<RectTransform>());
            headLayer.SetAsFirstSibling();
        }
        headUIObject.transform.SetParent(headLayer,false);

        // 他の通常HUDより下に置きつつ、
        // 3Dオブジェクトよりは必ず前
        headUIObject.transform.SetAsFirstSibling();


        headUIRect =
            headUIObject.GetComponent<RectTransform>();

        headUIRect.sizeDelta =
            new Vector2(
                BaseUIWidth,
                BaseUIHeight
            );

        headUIRect.anchorMin =
            new Vector2(0.5f, 0.5f);

        headUIRect.anchorMax =
            new Vector2(0.5f, 0.5f);

        headUIRect.pivot =
            new Vector2(0.5f, 0.5f);

        headUIRect.localScale =
            Vector3.one;


        var theme =
            GameUITheme.Current;


        // ========================================================
        // Lvメダル
        // ========================================================

        var badge =
            CreateGraphic(
                "LevelBadge",
                headUIObject.transform,

                // 左中央
                new Vector2(0f, 0.5f),

                // 位置
                new Vector2(16f, 0f),

                // サイズ
                new Vector2(32f, 32f),

                theme != null
                    ? theme.levelBadge
                    : null
            );


        badge.color =
            status.team == Team.Player
                ? new Color(
                    0.8f,
                    0.92f,
                    1f,
                    1f
                )
                : new Color(
                    1f,
                    0.65f,
                    0.55f,
                    1f
                );


        lvText =
            CreateText(
                "LvText",
                badge.transform,
                "1",
                16f
            );


        lvText.fontStyle =
            FontStyles.Bold;

        lvText.alignment =
            TextAlignmentOptions.Center;

        UIFactory.StretchFill(
            lvText.rectTransform
        );

        lvText.margin =
            new Vector4(
                5f,
                5f,
                5f,
                5f
            );


        // ========================================================
        // HPフレーム
        // ========================================================

        var hpFrame =
            CreateGraphic(
                "HPFrame",
                headUIObject.transform,

                // 右中央
                new Vector2(1f, 0.5f),

                // 位置
                new Vector2(-44f, 0f),

                // サイズ
                new Vector2(88f, 18f),

                theme != null
                    ? theme.healthFrame
                    : null
            );


        // ========================================================
        // HP背景
        // ========================================================

        var well =
            CreateGraphic(
                "HPWell",
                hpFrame.transform,

                new Vector2(0.5f, 0.5f),

                Vector2.zero,

                new Vector2(
                    64f,
                    9f
                ),

                null
            );


        well.color =
            new Color(
                0.04f,
                0.06f,
                0.07f,
                1f
            );


        // ========================================================
        // HPゲージ
        // ========================================================

        hpFillImage =
            CreateGraphic(
                "HPFill",
                well.transform,

                new Vector2(
                    0.5f,
                    0.5f
                ),

                Vector2.zero,

                new Vector2(
                    64f,
                    9f
                ),

                theme != null
                    ? theme.healthFill
                    : null
            );


        hpFillImage.type =
            Image.Type.Filled;

        hpFillImage.fillMethod =
            Image.FillMethod.Horizontal;

        hpFillImage.fillOrigin = 0;


        // ========================================================
        // HP数字
        // ========================================================

        hpText =
            CreateText(
                "HPText",
                hpFrame.transform,
                "",
                10f
            );


        hpText.alignment =
            TextAlignmentOptions.Center;

        hpText.fontStyle =
            FontStyles.Bold;


        UIFactory.StretchFill(
            hpText.rectTransform
        );


        hpText.margin =
            new Vector4(
                11f,
                2f,
                11f,
                2f
            );


        hpText.enableAutoSizing = true;

        hpText.fontSizeMin = 8f;
        hpText.fontSizeMax = 10f;


        // 初期値反映
        Refresh();
    }


    // ============================================================
    // LateUpdate
    // ============================================================

    private void LateUpdate()
    {
        if (status == null || !status.gameObject.activeInHierarchy)
        {
            SetHeadUIVisible(false);
            return;
        }

        if (headUIObject == null)
            return;

        if (headUIRect == null)
            return;


        // ========================================================
        // 死亡時
        // ========================================================

        if (status.HP <= 0)
        {
            SetHeadUIVisible(false);
            return;
        }


        // ========================================================
        // 視界判定
        // ========================================================

        if (unitRenderer != null)
        {
            bool visible =
                unitRenderer.enabled;

            if (!visible)
            {
                SetHeadUIVisible(false);
                return;
            }
        }


        // ========================================================
        // Camera取得
        // ========================================================

        if (mainCamera == null)
        {
            mainCamera =
                Camera.main;

            if (mainCamera == null)
                return;
        }


        // ========================================================
        // ユニット頭上の3D座標
        // ========================================================

        Vector3 worldPosition =
            transform.TransformPoint(
                new Vector3(
                    0f,
                    HeadHeight,
                    0f
                )
            );


        // ========================================================
        // 画面座標へ変換
        // ========================================================

        Vector3 screenPosition =
            mainCamera.WorldToScreenPoint(
                worldPosition
            );


        // カメラより後ろにいる場合は消す
        if (screenPosition.z <= 0f)
        {
            SetHeadUIVisible(false);
            return;
        }


        SetHeadUIVisible(true);
        bool priority = UnitPanelUI.SelectedStatus == status;
        if (priority && headUIRect.GetSiblingIndex() != headUIRect.parent.childCount - 1)
            headUIRect.SetAsLastSibling();


        // ========================================================
        // カメラ距離 / Zoom に応じてUIサイズを変更
        // ========================================================

        float uiScale =
            CalculateHeadUIScale(
                worldPosition
            );


        var desiredScale = Vector3.one * uiScale;
        if (headUIRect.localScale != desiredScale)
            headUIRect.localScale = desiredScale;


        // ========================================================
        // 頭上へ配置
        // ========================================================

        float yOffset =
            ScreenYOffset *
            uiScale;


        var desiredPosition = new Vector3(
                screenPosition.x,
                screenPosition.y + yOffset,
                0f
            );
        if (headUIRect.position != desiredPosition)
            headUIRect.position = desiredPosition;


        // ========================================================
        // HP / Lv更新
        // ========================================================

        if (
            cachedHP != status.HP ||
            cachedMaxHP != status.MaxHP ||
            cachedLevel != status.Level
        )
        {
            Refresh();
        }
    }


    // ============================================================
    // UIサイズ計算
    // ============================================================

    /// <summary>
    /// 3D世界上で HeadUIWorldWidth の大きさが、
    /// 現在のカメラでは画面上何Pixelに見えるかを計算し、
    /// Screen Space UIのScaleへ変換する。
    ///
    /// これによって、
    ///
    /// カメラを近づける
    /// → 駒が大きくなる
    /// → UIも大きくなる
    ///
    /// カメラを遠ざける
    /// → 駒が小さくなる
    /// → UIも小さくなる
    ///
    /// という動作になる。
    /// </summary>
    private float CalculateHeadUIScale(
        Vector3 worldPosition
    )
    {
        if (mainCamera == null)
            return 1f;

        if (screenCanvas == null)
            return 1f;


        // UIの想定World幅の半分
        float halfWidth =
            HeadUIWorldWidth * 0.5f;


        // カメラから見て左右方向
        Vector3 cameraRight =
            mainCamera.transform.right;


        // World空間でUI左端相当
        Vector3 leftWorld =
            worldPosition
            - cameraRight * halfWidth;


        // World空間でUI右端相当
        Vector3 rightWorld =
            worldPosition
            + cameraRight * halfWidth;


        // Screen座標へ
        Vector3 leftScreen =
            mainCamera.WorldToScreenPoint(
                leftWorld
            );


        Vector3 rightScreen =
            mainCamera.WorldToScreenPoint(
                rightWorld
            );


        // 現在のカメラから見た
        // World幅1.86のPixelサイズ
        float desiredPixelWidth =
            Mathf.Abs(
                rightScreen.x
                - leftScreen.x
            );


        // CanvasScalerによって
        // UI座標が拡大縮小されているので補正
        float canvasScaleFactor =
            Mathf.Max(
                0.0001f,
                screenCanvas.scaleFactor
            );


        // Scale=1のHeadUIが
        // 実際に画面上で占めるPixel幅
        float basePixelWidth =
            BaseUIWidth
            * canvasScaleFactor;


        float scale =
            desiredPixelWidth
            / Mathf.Max(
                1f,
                basePixelWidth
            );


        // 極端な巨大化・極小化防止
        scale =
            Mathf.Clamp(
                scale,
                MinHeadUIScale,
                MaxHeadUIScale
            );


        return scale;
    }


    // ============================================================
    // 表示切り替え
    // ============================================================

    private void SetHeadUIVisible(
        bool visible
    )
    {
        if (headUIObject == null)
            return;

        if (
            headUIObject.activeSelf
            != visible
        )
        {
            headUIObject.SetActive(
                visible
            );
        }
    }


    // ============================================================
    // Refresh
    // ============================================================

    private void Refresh()
    {
        if (status == null)
            return;


        cachedHP =
            status.HP;

        cachedMaxHP =
            status.MaxHP;

        cachedLevel =
            status.Level;


        // ========================================================
        // HP数字
        // ========================================================

        if (hpText != null)
        {
            hpText.text =
                $"{cachedHP}/{Mathf.Max(1, cachedMaxHP)}";
        }


        // ========================================================
        // Lv
        // ========================================================

        if (lvText != null)
        {
            lvText.text =
                cachedLevel.ToString();
        }


        // ========================================================
        // HPゲージ
        // ========================================================

        if (hpFillImage != null)
        {
            float ratio =
                Mathf.Clamp01(
                    (float)cachedHP /
                    Mathf.Max(
                        1,
                        cachedMaxHP
                    )
                );


            hpFillImage.fillAmount =
                ratio;


            // HP 50%以上
            if (ratio > 0.5f)
            {
                hpFillImage.color =
                    new Color(
                        0.2f,
                        0.8f,
                        0.25f,
                        0.95f
                    );
            }

            // HP 25〜50%
            else if (ratio > 0.25f)
            {
                hpFillImage.color =
                    new Color(
                        0.9f,
                        0.7f,
                        0.1f,
                        0.95f
                    );
            }

            // HP 25%以下
            else
            {
                hpFillImage.color =
                    new Color(
                        0.85f,
                        0.15f,
                        0.15f,
                        0.95f
                    );
            }
        }
    }


    // ============================================================
    // Image作成
    // ============================================================

    private static Image CreateGraphic(
        string name,
        Transform parent,
        Vector2 anchor,
        Vector2 position,
        Vector2 size,
        Sprite sprite
    )
    {
        var go =
            new GameObject(
                name,
                typeof(RectTransform),
                typeof(Image)
            );


        go.transform.SetParent(
            parent,
            false
        );


        var rect =
            (RectTransform)go.transform;


        rect.anchorMin =
            anchor;

        rect.anchorMax =
            anchor;

        rect.anchoredPosition =
            position;

        rect.sizeDelta =
            size;


        var image =
            go.GetComponent<Image>();


        image.sprite =
            sprite;

        image.raycastTarget =
            false;


        return image;
    }


    // ============================================================
    // Text作成
    // ============================================================

    private TextMeshProUGUI CreateText(
        string name,
        Transform parent,
        string text,
        float fontSize
    )
    {
        var go =
            new GameObject(
                name,
                typeof(RectTransform)
            );


        go.transform.SetParent(
            parent,
            false
        );


        var tmp =
            go.AddComponent<TextMeshProUGUI>();


        tmp.text =
            text;

        tmp.fontSize =
            fontSize;

        tmp.color =
            Color.white;

        tmp.raycastTarget =
            false;

        tmp.textWrappingMode =
            TextWrappingModes.NoWrap;


        // ========================================================
        // フォント
        // ========================================================

        var font =
            Resources.Load<TMP_FontAsset>(
                "Fonts & Materials/NotoSansJP-VariableFont_wght SDF"
            );


        if (font == null)
        {
            font =
                Resources.Load<TMP_FontAsset>(
                    "Fonts & Materials/LiberationSans SDF"
                );
        }


        if (font != null)
        {
            tmp.font =
                font;
        }


        return tmp;
    }


    // ============================================================
    // Destroy
    // ============================================================

    private void OnDisable()
    {
        // Death and pooling disable the owner before LateUpdate can run.
        SetHeadUIVisible(false);
    }

    private void OnDestroy()
    {
        // HeadUIはユニットの子ではなく
        // ScreenCanvasの子になっているので、
        // ユニット死亡時に明示的に削除する。
        if (headUIObject != null)
        {
            Destroy(
                headUIObject
            );
        }
    }
}
