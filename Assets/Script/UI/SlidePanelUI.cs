using UnityEngine;

/// <summary>
/// 左スライドパネルUI：建築 / ユニット生成 の一覧を左から出し入れする
/// Canvas > SafeAreaRoot > LeftMenuRoot > SlidePanel に付ける
///
/// 構成:
///   LeftMenuRoot
///     ├─ BuildOpenButton   (建築タブ)
///     ├─ UnitOpenButton    (ユニット生成タブ)
///     └─ SlidePanel        ← このスクリプトを付ける
///         ├─ Header        (タイトル + 閉じるボタン)
///         ├─ BuildScrollView
///         └─ UnitScrollView
/// </summary>
public class SlidePanelUI : MonoBehaviour
{
    [Header("パネル本体の RectTransform")]
    [SerializeField] private RectTransform panelRect;

    [Header("中身の切り替え")]
    [SerializeField] private GameObject buildRoot;
    [SerializeField] private GameObject unitRoot;

    [Header("スライド設定")]
    [SerializeField] private float slideSpeed = 12f;
    [SerializeField] private float openX = 0f;
    [SerializeField] private float closedX = -350f;

    private Vector2 targetPos;
    private bool isOpen;

    private void Start()
    {
        targetPos = new Vector2(closedX, panelRect.anchoredPosition.y);
        panelRect.anchoredPosition = targetPos;

        if (buildRoot != null) buildRoot.SetActive(false);
        if (unitRoot != null) unitRoot.SetActive(false);
    }

    private void Update()
    {
        if (panelRect == null) return;

        panelRect.anchoredPosition = Vector2.Lerp(
            panelRect.anchoredPosition,
            targetPos,
            Time.deltaTime * slideSpeed
        );
    }

    // --------------------------------------------------
    //  建築パネルを開く / 閉じる
    // --------------------------------------------------
    public void ToggleBuildPanel()
    {
        if (isOpen && buildRoot != null && buildRoot.activeSelf)
        {
            ClosePanel();
            return;
        }
        OpenBuildPanel();
    }

    public void OpenBuildPanel()
    {
        if (buildRoot != null) buildRoot.SetActive(true);
        if (unitRoot != null) unitRoot.SetActive(false);
        Open();
    }

    // --------------------------------------------------
    //  ユニット生成パネルを開く / 閉じる
    // --------------------------------------------------
    public void ToggleUnitPanel()
    {
        if (isOpen && unitRoot != null && unitRoot.activeSelf)
        {
            ClosePanel();
            return;
        }
        OpenUnitPanel();
    }

    public void OpenUnitPanel()
    {
        if (buildRoot != null) buildRoot.SetActive(false);
        if (unitRoot != null) unitRoot.SetActive(true);
        Open();
    }

    // --------------------------------------------------
    //  共通
    // --------------------------------------------------
    public void ClosePanel()
    {
        isOpen = false;
        targetPos = new Vector2(closedX, panelRect.anchoredPosition.y);
    }

    private void Open()
    {
        isOpen = true;
        targetPos = new Vector2(openX, panelRect.anchoredPosition.y);
    }

    public bool IsOpen => isOpen;
}
