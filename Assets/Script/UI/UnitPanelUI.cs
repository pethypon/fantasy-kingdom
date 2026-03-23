using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ユニット/建築物パネルUI：選択対象の情報表示 + 行動ボタン
/// ユニット: 攻撃・スキル・待機・キャンセル
/// 建築物: 攻撃(攻撃型のみ)・強化ボタン + 強化コスト表示
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

    [Header("強化UI（動的生成）")]
    private Button upgradeButton;
    private TextMeshProUGUI upgradeCostText;
    private GameObject upgradeArea;

    [Header("破壊UI（動的生成）")]
    private Button destroyButton;
    private GameObject destroyArea;

    [Header("参照")]
    [SerializeField] private TurnGenerater turnGenerater;
    [SerializeField] private APSystem apSystem;

    [Header("パネル本体")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private GameObject panelRoot;

    private Status currentUnit;
    private bool isBuilding;

    private void Awake()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
        if (panelRoot == null)
            panelRoot = gameObject;

        AutoFindChildren();
        AutoFindReferences();
        BuildUpgradeUI();
        BuildDestroyUI();

        Hide();
    }

    private void AutoFindChildren()
    {
        if (nameText == null) nameText = FindTMP("NameText");
        if (levelText == null) levelText = FindTMP("LevelText");
        if (hpText == null) hpText = FindTMP("HPText");
        if (atkText == null) atkText = FindTMP("ATKText");
        if (defText == null) defText = FindTMP("DEFText");
        if (kindText == null) kindText = FindTMP("KindText");
        if (passiveText == null) passiveText = FindTMP("PassiveText");
        if (attackButton == null) attackButton = FindButton("AttackBtn");
        if (skillButton == null) skillButton = FindButton("SkillBtn");
        if (waitButton == null) waitButton = FindButton("WaitBtn");
        if (cancelButton == null) cancelButton = FindButton("CancelBtn");
    }

    private void AutoFindReferences()
    {
        if (turnGenerater == null)
            turnGenerater = Object.FindFirstObjectByType<TurnGenerater>();
        if (apSystem == null)
            apSystem = Object.FindFirstObjectByType<APSystem>();
    }

    private TextMeshProUGUI FindTMP(string childName)
    {
        foreach (var tmp in GetComponentsInChildren<TextMeshProUGUI>(true))
            if (tmp.gameObject.name == childName) return tmp;
        return null;
    }

    private Button FindButton(string childName)
    {
        foreach (var btn in GetComponentsInChildren<Button>(true))
            if (btn.gameObject.name == childName) return btn;
        return null;
    }

    // --------------------------------------------------
    //  強化UIの動的生成
    // --------------------------------------------------
    private void BuildUpgradeUI()
    {
        // パネル本体の直下に強化エリアを生成（中央統計の下に配置）
        Transform parent = panelRoot != null ? panelRoot.transform : transform;

        // 強化エリア（パネル下部に配置）
        upgradeArea = new GameObject("UpgradeArea", typeof(RectTransform));
        upgradeArea.transform.SetParent(parent, false);
        var areaRT = upgradeArea.GetComponent<RectTransform>();
        areaRT.anchorMin = new Vector2(0.3f, 0);
        areaRT.anchorMax = new Vector2(0.85f, 0.33f);
        areaRT.offsetMin = new Vector2(4, 4);
        areaRT.offsetMax = new Vector2(-4, -2);

        // 強化ボタン
        var btnGo = new GameObject("UpgradeBtn", typeof(RectTransform));
        btnGo.transform.SetParent(upgradeArea.transform, false);
        var btnImg = btnGo.AddComponent<Image>();
        btnImg.color = new Color(0.15f, 0.45f, 0.15f, 1f);
        upgradeButton = btnGo.AddComponent<Button>();
        upgradeButton.targetGraphic = btnImg;
        var btnRT = btnGo.GetComponent<RectTransform>();
        btnRT.anchorMin = new Vector2(0, 0);
        btnRT.anchorMax = new Vector2(0.4f, 1);
        btnRT.offsetMin = Vector2.zero;
        btnRT.offsetMax = Vector2.zero;

        var btnLabel = new GameObject("Label", typeof(RectTransform));
        btnLabel.transform.SetParent(btnGo.transform, false);
        var btnTMP = btnLabel.AddComponent<TextMeshProUGUI>();
        btnTMP.text = "強化";
        btnTMP.fontSize = 16;
        btnTMP.alignment = TextAlignmentOptions.Center;
        btnTMP.color = Color.white;
        var lblRT = btnLabel.GetComponent<RectTransform>();
        lblRT.anchorMin = Vector2.zero;
        lblRT.anchorMax = Vector2.one;
        lblRT.offsetMin = Vector2.zero;
        lblRT.offsetMax = Vector2.zero;

        upgradeButton.onClick.AddListener(OnClickUpgrade);

        // コスト表示
        var costGo = new GameObject("UpgradeCost", typeof(RectTransform));
        costGo.transform.SetParent(upgradeArea.transform, false);
        upgradeCostText = costGo.AddComponent<TextMeshProUGUI>();
        upgradeCostText.fontSize = 15;
        upgradeCostText.alignment = TextAlignmentOptions.MidlineLeft;
        upgradeCostText.color = new Color(0.85f, 0.85f, 0.75f);
        upgradeCostText.textWrappingMode = TMPro.TextWrappingModes.Normal;
        var costRT = costGo.GetComponent<RectTransform>();
        costRT.anchorMin = new Vector2(0.42f, 0);
        costRT.anchorMax = new Vector2(1, 1);
        costRT.offsetMin = new Vector2(4, 0);
        costRT.offsetMax = Vector2.zero;

        upgradeArea.SetActive(false);
    }

    // --------------------------------------------------
    //  破壊UIの動的生成
    // --------------------------------------------------
    private void BuildDestroyUI()
    {
        Transform parent = panelRoot != null ? panelRoot.transform : transform;

        // 破壊エリア（右コマンドエリアの下半分に配置）
        destroyArea = new GameObject("DestroyArea", typeof(RectTransform));
        destroyArea.transform.SetParent(parent, false);
        var areaRT = destroyArea.GetComponent<RectTransform>();
        areaRT.anchorMin = new Vector2(0.6f, 0);
        areaRT.anchorMax = new Vector2(1f, 0.5f);
        areaRT.offsetMin = new Vector2(4, 8);
        areaRT.offsetMax = new Vector2(-10, -2);

        // 破壊ボタン
        var btnGo = new GameObject("DestroyBtn", typeof(RectTransform));
        btnGo.transform.SetParent(destroyArea.transform, false);
        var btnImg = btnGo.AddComponent<Image>();
        btnImg.color = new Color(0.6f, 0.15f, 0.15f, 1f);
        destroyButton = btnGo.AddComponent<Button>();
        destroyButton.targetGraphic = btnImg;
        var btnRT = btnGo.GetComponent<RectTransform>();
        btnRT.anchorMin = Vector2.zero;
        btnRT.anchorMax = Vector2.one;
        btnRT.offsetMin = Vector2.zero;
        btnRT.offsetMax = Vector2.zero;

        var btnLabel = new GameObject("Label", typeof(RectTransform));
        btnLabel.transform.SetParent(btnGo.transform, false);
        var btnTMP = btnLabel.AddComponent<TextMeshProUGUI>();
        btnTMP.text = "破壊";
        btnTMP.fontSize = 16;
        btnTMP.alignment = TextAlignmentOptions.Center;
        btnTMP.color = Color.white;
        var lblRT = btnLabel.GetComponent<RectTransform>();
        lblRT.anchorMin = Vector2.zero;
        lblRT.anchorMax = Vector2.one;
        lblRT.offsetMin = Vector2.zero;
        lblRT.offsetMax = Vector2.zero;

        destroyButton.onClick.AddListener(OnClickDestroy);
        destroyArea.SetActive(false);
    }

    // --------------------------------------------------
    //  外部から呼ぶ: 選択時
    // --------------------------------------------------
    public void Show(Status unit)
    {
        if (unit == null) return;
        currentUnit = unit;
        isBuilding = (unit.type == Type.Building || unit.type == Type.Wall);
        SetVisible(true);
        Refresh();
    }

    public void Hide()
    {
        currentUnit = null;
        isBuilding = false;
        SetVisible(false);
    }

    // --------------------------------------------------
    //  表示更新
    // --------------------------------------------------
    public void Refresh()
    {
        if (currentUnit == null) return;

        if (isBuilding)
            RefreshBuilding();
        else
            RefreshUnit();
    }

    private void RefreshUnit()
    {
        // 左: 基本情報
        string dirMark = currentUnit.direction == Direction.N ? " <color=#88CCFF>▲N</color>" : " <color=#FF8888>▼S</color>";
        if (nameText != null) nameText.text = KindNameJP.Get(currentUnit.kind) + dirMark;
        if (levelText != null) levelText.text = "Lv " + currentUnit.Level;
        if (hpText != null)
        {
            float hpRatio = currentUnit.MaxHP > 0 ? (float)currentUnit.HP / currentUnit.MaxHP : 0f;
            string hpColor = hpRatio > 0.5f ? "#AAFFAA" : hpRatio > 0.25f ? "#FFDD66" : "#FF5555";
            hpText.text = $"HP <color={hpColor}>{currentUnit.HP}</color>/{currentUnit.MaxHP}";
        }

        // 中央: ステータス
        if (atkText != null) atkText.text = "ATK " + currentUnit.ATK;
        if (defText != null) defText.text = "DEF " + currentUnit.DEF;
        if (kindText != null)
        {
            // スキル名を表示
            if (currentUnit.AssignedSkillId >= 0 && SkillData.Table.ContainsKey(currentUnit.AssignedSkillId))
            {
                var skill = SkillData.Table[currentUnit.AssignedSkillId];
                kindText.text = $"[{skill.Rarity}] {skill.Name} AP:{skill.APCost}";
            }
            else
            {
                kindText.text = currentUnit.kind.ToString();
            }
        }
        if (passiveText != null)
        {
            // 状態異常・バフの表示
            string effectStr = BuildEffectString(currentUnit);
            if (!string.IsNullOrEmpty(effectStr))
                passiveText.text = effectStr;
            else if (currentUnit.passiveskill != PassiveSkill.None)
                passiveText.text = currentUnit.passiveskill.ToString();
            else
                passiveText.text = "";
        }

        // 敵ユニットは情報のみ表示（操作ボタン非表示）
        bool isPlayer = currentUnit.team == Team.Player;
        SetButtonVisible(attackButton, isPlayer);
        SetButtonVisible(skillButton, isPlayer && currentUnit.AssignedSkillId >= 0);
        SetButtonVisible(waitButton, isPlayer);
        SetButtonVisible(cancelButton, true);
        if (upgradeArea != null) upgradeArea.SetActive(false);
        if (destroyArea != null) destroyArea.SetActive(false);

        if (isPlayer) UpdateUnitButtons();
    }

    private static string BuildEffectString(Status unit)
    {
        if (unit.ActiveEffects == null || unit.ActiveEffects.Count == 0) return "";
        var parts = new System.Collections.Generic.List<string>();
        foreach (var e in unit.ActiveEffects)
        {
            if (e.IsDebuff)
                parts.Add($"{e.debuffType}({e.remainingTurns}T)");
            else if (e.IsBuff)
                parts.Add($"{e.buffType}({e.remainingTurns}T)");
        }
        return string.Join(" ", parts);
    }

    private void RefreshBuilding()
    {
        var facility = currentUnit.facilityKind;
        string displayName = facility.ToString();
        if (FacilityData.Table.TryGetValue(facility, out var info))
            displayName = info.DisplayName;

        // 左: 基本情報
        if (nameText != null) nameText.text = displayName;
        if (levelText != null) levelText.text = "Lv " + currentUnit.Level;
        if (hpText != null)
        {
            float hpRatio = currentUnit.MaxHP > 0 ? (float)currentUnit.HP / currentUnit.MaxHP : 1f;
            string hpColor = hpRatio > 0.5f ? "#AAFFAA" : hpRatio > 0.25f ? "#FFDD66" : "#FF5555";
            hpText.text = $"HP <color={hpColor}>{currentUnit.HP}</color>/{currentUnit.MaxHP}";
        }

        // 中央: ステータス
        if (atkText != null) atkText.text = currentUnit.ATK > 0 ? "ATK " + currentUnit.ATK : "";
        if (defText != null) defText.text = currentUnit.DEF > 0 ? "DEF " + currentUnit.DEF : "";

        // 効果説明
        if (kindText != null)
            kindText.text = GetBuildingEffectText(facility, currentUnit.Level);
        if (passiveText != null)
            passiveText.text = "";

        // 建築物用ボタン表示
        bool isOffensive = FacilityData.IsOffensive(facility);
        SetButtonVisible(attackButton, isOffensive);
        SetButtonVisible(skillButton, false);
        SetButtonVisible(waitButton, false);

        if (isOffensive && attackButton != null)
            attackButton.interactable = true;

        // 強化UI
        UpdateUpgradeUI();

        // 破壊UI（Player建築物のみ表示、クリスタルは除外）
        if (destroyArea != null)
        {
            bool showDestroy = currentUnit.team == Team.Player
                && currentUnit.kind != Kind.Crystal;
            destroyArea.SetActive(showDestroy);
        }
    }

    private void UpdateUnitButtons()
    {
        if (currentUnit == null) return;

        bool canAttack = apSystem != null
            && apSystem.CanAct(Team.Player, APSystem.ActionType.Attack, currentUnit);

        if (attackButton != null)
        {
            attackButton.interactable = canAttack;
            UpdateButtonLabel(attackButton, "攻撃", GameConstants.BaseAttackAPCost, canAttack);
        }
        if (skillButton != null)
        {
            bool hasSkill = currentUnit.AssignedSkillId >= 0;
            int skillCost = 0;
            bool canSkill = false;
            if (hasSkill && SkillData.Table.TryGetValue(currentUnit.AssignedSkillId, out var skill))
            {
                skillCost = skill.APCost;
                canSkill = apSystem != null && apSystem.CanUseSkill(Team.Player, skillCost);
            }
            skillButton.interactable = canSkill;
            UpdateButtonLabel(skillButton, "スキル", skillCost, canSkill);
        }
        if (waitButton != null) waitButton.interactable = true;
    }

    /// <summary>ボタンラベルにAP消費量を付加し、不足時は色を変える</summary>
    private void UpdateButtonLabel(Button btn, string baseName, int apCost, bool canAfford)
    {
        var label = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (label == null) return;
        if (apCost > 0)
            label.text = $"{baseName} <size=80%><color={(canAfford ? "#AAFFAA" : "#FF6666")}>AP{apCost}</color></size>";
        else
            label.text = baseName;
    }

    private void UpdateUpgradeUI()
    {
        if (upgradeArea == null) return;
        if (currentUnit == null || !isBuilding)
        {
            upgradeArea.SetActive(false);
            return;
        }

        var facility = currentUnit.facilityKind;
        int currentLevel = Mathf.Max(1, currentUnit.Level);
        int maxLevel = FacilityData.GetMaxLevel(facility);

        if (currentLevel >= maxLevel)
        {
            upgradeArea.SetActive(false);
            return;
        }

        upgradeArea.SetActive(true);
        var nextData = FacilityData.GetLevel(facility, currentLevel + 1);

        // コスト文字列を構築
        string costStr = FormatUpgradeCost(nextData.UpgradeCost, nextData.UpgradeAP);
        if (upgradeCostText != null)
            upgradeCostText.text = costStr;

        // 強化可否
        if (upgradeButton != null)
        {
            var factionState = GetFactionState();
            bool canUpgrade = factionState != null &&
                FacilityData.CanUpgrade(
                    factionState.PlayerResources,
                    factionState.GetAP(Team.Player),
                    facility, currentLevel);
            upgradeButton.interactable = canUpgrade;

            var img = upgradeButton.GetComponent<Image>();
            if (img != null)
                img.color = canUpgrade
                    ? new Color(0.15f, 0.45f, 0.15f, 1f)
                    : new Color(0.4f, 0.15f, 0.15f, 1f);
        }
    }

    // --------------------------------------------------
    //  ボタン OnClick()
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

    public void OnClickUpgrade()
    {
        if (currentUnit == null || !isBuilding) return;
        if (turnGenerater == null || turnGenerater.buildsystem == null) return;

        if (turnGenerater.buildsystem.TryUpgrade(currentUnit))
        {
            Refresh();
        }
    }

    public void OnClickDestroy()
    {
        if (currentUnit == null || !isBuilding) return;
        if (turnGenerater == null) return;

        var subCrystalSystem = turnGenerater.subCrystalSystem;
        if (subCrystalSystem == null) return;

        subCrystalSystem.DestroyBuilding(currentUnit);
        Hide();
    }

    // --------------------------------------------------
    //  ヘルパー
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

    private void SetButtonVisible(Button btn, bool visible)
    {
        if (btn != null) btn.gameObject.SetActive(visible);
    }

    private FactionState GetFactionState()
    {
        if (apSystem == null) return null;
        // APSystem has _factionState private — use reflection-free approach via TurnGenerater
        // Find FactionState from scene
        return Object.FindFirstObjectByType<FactionState>();
    }

    private static string FormatUpgradeCost(FacilityData.ResourceCost cost, int ap)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (cost.Wood > 0) parts.Add($"木{cost.Wood}");
        if (cost.Stone > 0) parts.Add($"石{cost.Stone}");
        if (cost.Iron > 0) parts.Add($"鉄{cost.Iron}");
        if (cost.MagicOre > 0) parts.Add($"魔{cost.MagicOre}");
        if (cost.Water > 0) parts.Add($"水{cost.Water}");
        if (cost.Plank > 0) parts.Add($"板{cost.Plank}");
        if (cost.CutStone > 0) parts.Add($"切{cost.CutStone}");
        if (cost.Citizen > 0) parts.Add($"民{cost.Citizen}");
        if (ap > 0) parts.Add($"AP{ap}");
        return string.Join(" ", parts);
    }

    private static string GetBuildingEffectText(FacilityKind facility, int level)
    {
        var data = FacilityData.GetLevel(facility, level);
        switch (facility)
        {
            case FacilityKind.Field:
                return $"水2→小麦+{data.Output.Wheat}";
            case FacilityKind.Bakery:
                return $"小麦{data.Input.Wheat}+水{data.Input.Water}→パン{data.Output.Bread}";
            case FacilityKind.LoggingCamp:
                return $"木+{data.Output.Wood}";
            case FacilityKind.LumberMill:
                return $"木{data.Input.Wood}→板{data.Output.Plank}";
            case FacilityKind.Quarry:
                return $"石+{data.Output.Stone} 炭+{data.Output.Coal}";
            case FacilityKind.StoneWorks:
                return $"石{data.Input.Stone}→切石{data.Output.CutStone}";
            case FacilityKind.Mine:
                return $"鉄鉱+{data.Output.IronOre}";
            case FacilityKind.Smelter:
                return $"鉄鉱{data.Input.IronOre}+炭{data.Input.Coal}→鉄{data.Output.Iron}";
            case FacilityKind.Well:
                return $"水+{data.Output.Water}";
            case FacilityKind.Barracks:
                return $"経験値+{data.SpecialValue}%";
            case FacilityKind.House:
                return $"収容+{data.SpecialValue}";
            case FacilityKind.Warehouse:
                return $"容量+{data.SpecialValue}";
            case FacilityKind.SubCrystal:
                return "領地拡張 半径3";
            default:
                return "";
        }
    }
}
