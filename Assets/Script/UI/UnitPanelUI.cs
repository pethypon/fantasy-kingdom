using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ユニットパネルUI：選択ユニットの情報表示 + 攻撃・スキル・待機ボタン
/// Canvas > SafeAreaRoot > BottomUnitPanel に付ける
/// 駒をクリックして選択したときだけ表示される
///
/// 構成:
///   BottomUnitPanel
///     ├─ LeftInfo    (アイコン・名前・Lv・HP)
///     ├─ CenterStats (ATK・DEF・特性)
///     └─ RightCommands (攻撃・スキル・待機・キャンセル)
/// </summary>
public class UnitPanelUI : MonoBehaviour
{
    [Header("左: 基本情報")]
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI levelText;
    [SerializeField] private TextMeshProUGUI hpText;

    [Header("中央: ステータス")]
    [SerializeField] private TextMeshProUGUI atkText;
    [SerializeField] private TextMeshProUGUI defText;
    [SerializeField] private TextMeshProUGUI kindText;
    [SerializeField] private TextMeshProUGUI passiveText;

    [Header("右: 行動ボタン")]
    [SerializeField] private Button attackButton;
    [SerializeField] private Button skillButton;
    [SerializeField] private Button waitButton;
    [SerializeField] private Button cancelButton;

    [Header("参照")]
    [SerializeField] private TurnGenerater turnGenerater;
    [SerializeField] private APSystem apSystem;

    [Header("パネル本体")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private GameObject panelRoot;

    private Status currentUnit;

    private void Awake()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
        if (panelRoot == null)
            panelRoot = gameObject;

        Hide();
    }

    // --------------------------------------------------
    //  外部から呼ぶ: ユニット選択時
    // --------------------------------------------------
    public void Show(Status unit)
    {
        if (unit == null) return;
        currentUnit = unit;
        SetVisible(true);
        Refresh();
    }

    public void Hide()
    {
        currentUnit = null;
        SetVisible(false);
    }

    // --------------------------------------------------
    //  表示更新
    // --------------------------------------------------
    public void Refresh()
    {
        if (currentUnit == null) return;

        // 左: 基本情報
        if (nameText != null) nameText.text = currentUnit.kind.ToString();
        if (levelText != null) levelText.text = "Lv " + currentUnit.Level;
        if (hpText != null) hpText.text = "HP " + currentUnit.HP;

        // 中央: ステータス
        if (atkText != null) atkText.text = "ATK " + currentUnit.ATK;
        if (defText != null) defText.text = "DEF " + currentUnit.DEF;
        if (kindText != null) kindText.text = currentUnit.kind.ToString();
        if (passiveText != null)
        {
            passiveText.text = currentUnit.passiveskill == PassiveSkill.None
                ? ""
                : currentUnit.passiveskill.ToString();
        }

        UpdateButtons();
    }

    private void UpdateButtons()
    {
        if (currentUnit == null) return;

        bool canAttack = apSystem != null
            && apSystem.CanAct(Team.Player, APSystem.ActionType.Attack, currentUnit);

        if (attackButton != null) attackButton.interactable = canAttack;
        if (skillButton != null) skillButton.interactable = canAttack;
        if (waitButton != null) waitButton.interactable = true;
    }

    // --------------------------------------------------
    //  ボタン OnClick() から呼ぶ
    // --------------------------------------------------
    public void OnClickAttack()
    {
        if (turnGenerater == null) return;
        turnGenerater.SelectNormalDown = true;
    }

    public void OnClickSkill()
    {
        if (turnGenerater == null) return;
        turnGenerater.SelectSkillDown = true;
    }

    public void OnClickWait()
    {
        Hide();
    }

    public void OnClickCancel()
    {
        if (turnGenerater == null) return;
        turnGenerater.RightClickDown = true;
        Hide();
    }

    // --------------------------------------------------
    //  表示/非表示（SetActive + CanvasGroup 併用）
    // --------------------------------------------------
    private void SetVisible(bool visible)
    {
        if (panelRoot != null)
            panelRoot.SetActive(visible);

        if (canvasGroup != null)
        {
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;
        }
    }
}
