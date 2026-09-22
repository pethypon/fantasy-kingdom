using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
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
    [FormerlySerializedAs("turnGenerater")]
    [SerializeField] private TurnGenerator turnGenerator;
    [SerializeField] private APSystem apSystem;
    private FactionState factionState;

    [Header("パネル本体")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private GameObject panelRoot;

    private Status currentUnit;
    private bool isBuilding;
    private bool previewOnly;
    private float nextRefresh;
    private TextMeshProUGUI previewHint;
    private TextMeshProUGUI headingText;
    private void LateUpdate()
    {
        if (currentUnit == null || !currentUnit.IsAlive)
        {
            if (canvasGroup != null && canvasGroup.alpha > 0) Hide();
            return;
        }
        if (HasSelection && Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + 0.15f;
            Refresh();
        }
    }
    public bool HasSelection => currentUnit != null && !previewOnly;

    public void Preview(Status unit)
    {
        if (HasSelection) return;
        if (unit == null) { Hide(); return; }
        currentUnit = unit;
        previewOnly = true;
        isBuilding = unit.type == Type.Building || unit.type == Type.Wall;
        SetVisible(true);
        Refresh();
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    // HPバー（動的生成）
    private Image hpBarFill;

    private void Awake()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
        if (panelRoot == null)
            panelRoot = gameObject;

        AutoFindChildren();
        headingText = FindTMP("StatusHeadingText");
        AutoFindReferences();
        BuildUpgradeUI();
        BuildDestroyUI();
        BuildHPBar();
        previewHint = UIFactory.CreateTMP("PreviewHint", transform.Find("RightCommands"),
            "プレビュー\nクリックで選択", BrandGuide.FontHud, UIFactory.LoadDefaultFont());
        UIFactory.StretchFill(previewHint.rectTransform);
        previewHint.color = BrandGuide.TextSecondary;
        previewHint.raycastTarget = false;
        previewHint.gameObject.SetActive(false);

        Hide();
    }

    // --------------------------------------------------
    //  HPバーの動的生成（HPテキスト直下の細いゲージ）
    // --------------------------------------------------
    private void BuildHPBar()
    {
        if (hpText == null) return;

        var barBg = new GameObject("HPBarBg", typeof(RectTransform));
        barBg.transform.SetParent(hpText.transform, false);
        var bgImg = barBg.AddComponent<Image>();
        bgImg.color = new Color(0.15f, 0.15f, 0.15f, 0.9f);
        bgImg.raycastTarget = false;
        var bgRT = barBg.GetComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(0, 0);
        bgRT.anchorMax = new Vector2(1, 0);
        bgRT.pivot = new Vector2(0.5f, 0);
        bgRT.offsetMin = new Vector2(0, 0);
        bgRT.offsetMax = new Vector2(0, 6);

        var barFillGo = new GameObject("HPBarFill", typeof(RectTransform));
        barFillGo.transform.SetParent(barBg.transform, false);
        hpBarFill = barFillGo.AddComponent<Image>();
        hpBarFill.raycastTarget = false;
        hpBarFill.type = Image.Type.Filled;
        hpBarFill.fillMethod = Image.FillMethod.Horizontal;
        hpBarFill.fillOrigin = 0;
        // Filled タイプには sprite が必要なため白テクスチャを生成
        hpBarFill.sprite = Sprite.Create(Texture2D.whiteTexture,
            new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
        var fillRT = barFillGo.GetComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = new Vector2(1, 1);
        fillRT.offsetMax = new Vector2(-1, -1);
    }

    /// <summary>HPバーの量と色を更新する</summary>
    private void UpdateHPBar(float ratio)
    {
        if (hpBarFill == null) return;
        hpBarFill.fillAmount = Mathf.Clamp01(ratio);
        hpBarFill.color = BrandGuide.HPColor(ratio);
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
        if (turnGenerator == null)
            turnGenerator = Object.FindFirstObjectByType<TurnGenerator>();
        if (apSystem == null)
            apSystem = Object.FindFirstObjectByType<APSystem>();
        if (factionState == null)
            factionState = Object.FindFirstObjectByType<FactionState>();
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
        areaRT.anchorMin = new Vector2(0.6f, 0.38f);
        areaRT.anchorMax = new Vector2(1f, 0.72f);
        areaRT.offsetMin = new Vector2(4, 4);
        areaRT.offsetMax = new Vector2(-4, -2);

        // 強化ボタン
        var btnGo = new GameObject("UpgradeBtn", typeof(RectTransform));
        btnGo.transform.SetParent(upgradeArea.transform, false);
        var btnImg = btnGo.AddComponent<Image>();
        btnImg.color = BrandGuide.BtnUpgrade;
        upgradeButton = btnGo.AddComponent<Button>();
        BrandGuide.ApplyButtonStyle(upgradeButton, BrandGuide.BtnUpgrade);
        var btnRT = btnGo.GetComponent<RectTransform>();
        btnRT.anchorMin = new Vector2(0, 0);
        btnRT.anchorMax = new Vector2(0.34f, 1);
        btnRT.offsetMin = Vector2.zero;
        btnRT.offsetMax = Vector2.zero;

        var btnLabel = new GameObject("Label", typeof(RectTransform));
        btnLabel.transform.SetParent(btnGo.transform, false);
        var btnTMP = btnLabel.AddComponent<TextMeshProUGUI>();
        btnTMP.text = "強化";
        btnTMP.fontSize = BrandGuide.FontHud;
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
        upgradeCostText.fontSize = 24;
        upgradeCostText.alignment = TextAlignmentOptions.MidlineLeft;
        upgradeCostText.color = BrandGuide.TextSecondary;
        upgradeCostText.textWrappingMode = TMPro.TextWrappingModes.Normal;
        var costRT = costGo.GetComponent<RectTransform>();
        costRT.anchorMin = new Vector2(0.36f, 0);
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
        areaRT.anchorMax = new Vector2(1f, 0.34f);
        areaRT.offsetMin = new Vector2(4, 8);
        areaRT.offsetMax = new Vector2(-10, -2);

        // 破壊ボタン
        var btnGo = new GameObject("DestroyBtn", typeof(RectTransform));
        btnGo.transform.SetParent(destroyArea.transform, false);
        var btnImg = btnGo.AddComponent<Image>();
        btnImg.color = BrandGuide.BtnDestroy;
        destroyButton = btnGo.AddComponent<Button>();
        BrandGuide.ApplyButtonStyle(destroyButton, BrandGuide.BtnDestroy);
        var btnRT = btnGo.GetComponent<RectTransform>();
        btnRT.anchorMin = Vector2.zero;
        btnRT.anchorMax = Vector2.one;
        btnRT.offsetMin = Vector2.zero;
        btnRT.offsetMax = Vector2.zero;

        var btnLabel = new GameObject("Label", typeof(RectTransform));
        btnLabel.transform.SetParent(btnGo.transform, false);
        var btnTMP = btnLabel.AddComponent<TextMeshProUGUI>();
        btnTMP.text = "破壊";
        btnTMP.fontSize = BrandGuide.FontHud;
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
        previewOnly = false;
        isBuilding = (unit.type == Type.Building || unit.type == Type.Wall);
        SetVisible(true);
        Refresh();
    }

    public void Hide()
    {
        currentUnit = null;
        previewOnly = false;
        isBuilding = false;
        SetVisible(false);
        if (previewHint != null) previewHint.gameObject.SetActive(false);
    }

    // --------------------------------------------------
    //  表示更新
    // --------------------------------------------------
    public void Refresh()
    {
        if (currentUnit == null) return;

        RefreshHeading();
        if (isBuilding)
            RefreshBuilding();
        else
            RefreshUnit();
        if (previewHint != null) previewHint.gameObject.SetActive(previewOnly);
        if (previewOnly)
        {
            SetButtonVisible(attackButton, false);
            SetButtonVisible(skillButton, false);
            SetButtonVisible(waitButton, false);
            SetButtonVisible(cancelButton, false);
            upgradeArea.SetActive(false);
            destroyArea.SetActive(false);
        }
    }

    private void RefreshHeading()
    {
        if (headingText == null) return;
        string team = currentUnit.team switch
        {
            Team.Player => "味方", Team.Enemy => "敵軍", Team.Monster => "魔物",
            Team.Intruder => "乱入者", Team.Obstacle => "強敵", _ => "中立"
        };
        string mode = previewOnly ? "プレビュー" : "選択中";
        if (!previewOnly && turnGenerator != null && turnGenerator.Context.SelectUnit == currentUnit
            && turnGenerator.CurrentState is PlayerAttack attack)
            mode = attack.attackmode == PlayerMove.AttackMode.Skill ? "スキル対象を選択" : "攻撃対象を選択";
        headingText.text = $"{team}  /  {(isBuilding ? "施設" : "ユニット")}    ・    {mode}";
        headingText.color = currentUnit.team == Team.Player ? BrandGuide.TeamPlayer : BrandGuide.TextPrimary;
    }

    private void RefreshUnit()
    {
        // 左: 基本情報
        string nCol = ColorUtility.ToHtmlStringRGB(BrandGuide.TeamPlayer);
        string sCol = ColorUtility.ToHtmlStringRGB(BrandGuide.TeamEnemy);
        string dirMark = currentUnit.direction == Direction.N ? $" <color=#{nCol}>▲N</color>" : $" <color=#{sCol}>▼S</color>";
        if (nameText != null) nameText.text = KindNameJP.Get(currentUnit.kind) + dirMark;
        if (levelText != null) levelText.text = "Lv " + currentUnit.Level;
        if (hpText != null)
        {
            float hpRatio = currentUnit.MaxHP > 0 ? (float)currentUnit.HP / currentUnit.MaxHP : 0f;
            string hpColor = BrandGuide.HPColorHex(hpRatio);
            hpText.text = $"HP <color={hpColor}><b>{currentUnit.HP}</b></color>/{currentUnit.MaxHP}";
            UpdateHPBar(hpRatio);
        }

        if (levelText != null)
        {
            int remaining = Mathf.Max(0, Status.XPRequiredForLevel(currentUnit.Level + 1) - currentUnit.Experience);
            levelText.text = currentUnit.Level >= 10 ? "Lv 10  <size=75%>MAX</size>"
                : $"Lv {currentUnit.Level}\n<size=75%>次のレベルまで {remaining} XP</size>";
        }
        // 中央: ステータス
        if (atkText != null) atkText.text = "攻撃力  <b>" + currentUnit.ATK + "</b>";
        if (defText != null) defText.text = "防御力  <b>" + currentUnit.DEF + "</b>";
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
                kindText.text = KindNameJP.Get(currentUnit.kind);
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

        UIFactory.SetAnchors(attackButton.GetComponent<RectTransform>(), 0, 0.5f, 0.5f, 1);
        UIFactory.SetAnchors(cancelButton.GetComponent<RectTransform>(), 0.5f, 0, 1, 0.5f);
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
            string hpColor = BrandGuide.HPColorHex(hpRatio);
            hpText.text = $"HP <color={hpColor}><b>{currentUnit.HP}</b></color>/{currentUnit.MaxHP}";
            UpdateHPBar(hpRatio);
        }

        // 中央: ステータス
        if (atkText != null) atkText.text = currentUnit.ATK > 0 ? "攻撃力  <b>" + currentUnit.ATK + "</b>" : "";
        if (defText != null) defText.text = currentUnit.DEF > 0 ? "防御力  <b>" + currentUnit.DEF + "</b>" : "";

        // 効果説明
        if (kindText != null)
            kindText.text = GetBuildingEffectText(facility, currentUnit.Level);
        if (passiveText != null)
            passiveText.text = "";

        // 建築物用ボタン表示
        bool isOffensive = FacilityData.IsOffensive(facility);
        SetButtonVisible(attackButton, isOffensive && currentUnit.team == Team.Player);
        SetButtonVisible(cancelButton, true);
        UIFactory.SetAnchors(attackButton.GetComponent<RectTransform>(), 0, 0.76f, 0.5f, 1);
        UIFactory.SetAnchors(cancelButton.GetComponent<RectTransform>(), 0.5f, 0.76f, 1, 1);
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

        bool canAttack = !StatusEffectSystem.IsStunned(currentUnit) && apSystem != null
            && apSystem.CanAct(Team.Player, APSystem.ActionType.Attack, currentUnit);

        if (attackButton != null)
        {
            attackButton.interactable = canAttack;
            UpdateButtonLabel(attackButton, turnGenerator != null && turnGenerator.CurrentState is PlayerAttack attack && attack.attackmode == PlayerMove.AttackMode.Normal ? "攻撃モード" : "攻撃 <size=70%>[1]</size>", GameConstants.BaseAttackAPCost, canAttack);
        }
        if (skillButton != null)
        {
            bool hasSkill = currentUnit.AssignedSkillId >= 0;
            int skillCost = 0;
            bool canSkill = false;
            if (hasSkill && SkillData.Table.TryGetValue(currentUnit.AssignedSkillId, out var skill))
            {
                skillCost = skill.APCost;
                canSkill = !StatusEffectSystem.IsStunned(currentUnit) && apSystem != null && apSystem.CanUseSkill(Team.Player, skillCost);
            }
            skillButton.interactable = canSkill;
            UpdateButtonLabel(skillButton, turnGenerator != null && turnGenerator.CurrentState is PlayerAttack skillAttack && skillAttack.attackmode == PlayerMove.AttackMode.Skill ? "スキルモード" : "スキル <size=70%>[2]</size>", skillCost, canSkill);
        }
        if (waitButton != null) waitButton.interactable = true;
    }

    /// <summary>ボタンラベルにAP消費量を付加し、不足時は色を変える</summary>
    private void UpdateButtonLabel(Button btn, string baseName, int apCost, bool canAfford)
    {
        var label = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (label == null) return;
        if (apCost > 0)
            label.text = $"{baseName}\n<size=90%><color={BrandGuide.APCostColorHex(canAfford)}>AP{apCost}</color></size>";
        else
            label.text = baseName;
    }

    private void UpdateUpgradeUI()
    {
        if (upgradeArea == null) return;
        if (currentUnit == null || !isBuilding || currentUnit.team != Team.Player)
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
                    ? BrandGuide.BtnUpgrade
                    : BrandGuide.BtnDisabled;
        }
    }

    // --------------------------------------------------
    //  ボタン OnClick()
    // --------------------------------------------------
    private bool CanIssueCommand()
    {
        return !previewOnly && currentUnit != null && currentUnit.team == Team.Player
            && turnGenerator.Context.SelectUnit == currentUnit
            && (turnGenerator.CurrentState is PlayerMove || turnGenerator.CurrentState is PlayerAttack);
    }

    public void OnClickAttack()
    {
        if (turnGenerator == null) return;
        if (!CanIssueCommand()) return;
        turnGenerator.Context.QueueUICommand(GameContext.UICommand.Attack);
    }

    public void OnClickSkill()
    {
        if (turnGenerator == null) return;
        if (!CanIssueCommand()) return;
        turnGenerator.Context.QueueUICommand(GameContext.UICommand.Skill);
    }

    public void OnClickWait()
    {
        // Wait = このユニットのアクションを終了して選択解除。右クリック（Cancel）と同等。
        if (turnGenerator != null)
            turnGenerator.Context.QueueUICommand(GameContext.UICommand.Cancel);
        Hide();
    }

    public void OnClickCancel()
    {
        if (turnGenerator == null) return;
        turnGenerator.Context.QueueUICommand(GameContext.UICommand.Cancel);
        Hide();
    }

    public void OnClickUpgrade()
    {
        if (currentUnit == null || !isBuilding || currentUnit.team != Team.Player) return;
        if (turnGenerator == null || turnGenerator.Systems.BuildSystem == null) return;

        if (turnGenerator.Systems.BuildSystem.TryUpgrade(currentUnit))
        {
            Refresh();
        }
    }

    public void OnClickDestroy()
    {
        if (currentUnit == null || !isBuilding || currentUnit.team != Team.Player) return;
        if (turnGenerator == null) return;

        var subCrystalSystem = turnGenerator.Systems.SubCrystalSystem;
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
        if (factionState == null)
            factionState = Object.FindFirstObjectByType<FactionState>();
        return factionState;
    }

    private static string FormatUpgradeCost(FacilityData.ResourceCost cost, int ap)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (cost.Wood > 0) parts.Add($"木{cost.Wood}");
        if (cost.Stone > 0) parts.Add($"石{cost.Stone}");
        if (cost.Iron > 0) parts.Add($"鉄{cost.Iron}");
        if (cost.MagicOre > 0) parts.Add($"魔{cost.MagicOre}");
        if (cost.Water > 0) parts.Add($"水{cost.Water}");
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
            case FacilityKind.Quarry:
                return $"石+{data.Output.Stone}";
            case FacilityKind.Mine:
                return $"鉄+{data.Output.Iron} 魔石(確率)";
            case FacilityKind.Well:
                return $"水+{data.Output.Water}";
            case FacilityKind.Barracks:
                return $"経験値+{data.SpecialValue}%";
            case FacilityKind.House:
            case FacilityKind.LuxuryHouse:
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
