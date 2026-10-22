using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class UnitPanelUI
{
    Button specialSkillButton, specialStateButton;

    void BuildSpecialControls()
    {
        if (GetComponent<SpecialSkillWorldView>() == null) gameObject.AddComponent<SpecialSkillWorldView>();
        var right = transform.Find("RightCommands");
        if (right != null)
        {
            specialSkillButton = UIFactory.CreateButton("SpecialSkillBtn", right, "駒固有能力", BrandGuide.FontHud,
                BrandGuide.BtnSkill, UIFactory.LoadDefaultFont());
            BrandGuide.ApplyCommandStyle(specialSkillButton, new Color(.6f, .48f, .95f));
            var label = specialSkillButton.GetComponentInChildren<TextMeshProUGUI>();
            label.enableAutoSizing = true; label.fontSizeMin = 16; label.fontSizeMax = BrandGuide.FontHud;
            label.textWrappingMode = TextWrappingModes.Normal; label.raycastTarget = false;
            specialSkillButton.onClick.AddListener(OnClickSpecialSkill);
            specialSkillButton.gameObject.SetActive(false);
        }
        var heading = transform.Find("StatusHeading");
        if (heading != null)
        {
            specialStateButton = UIFactory.CreateButton("SpecialStateBtn", heading, "詳細", BrandGuide.FontHudCaption,
                BrandGuide.BtnSkill, UIFactory.LoadDefaultFont());
            UIFactory.SetAnchors(specialStateButton.GetComponent<RectTransform>(), .84f, 0, 1, 1);
            specialStateButton.onClick.AddListener(OnClickSpecialState);
            specialStateButton.gameObject.SetActive(false);
        }
    }

    void RefreshSpecialControls()
    {
        if (currentUnit == null) return;
        var definition = SpecialSkillRuntime.Definition(currentUnit);
        bool fixedIdentity = definition != null && definition.ability != UnitSpecialSkill.None;
        if (fixedIdentity)
        {
            SetButtonVisible(skillButton, false);
            // The fixed skill replaces the legacy assignment; show its actual name in the compact panel.
            if (kindText != null && !isBuilding) kindText.text = SpecialSkillPresentation.Name(definition);
        }
        bool hasDetails = currentUnit.type == Type.Unit || SpecialSkillPresentation.HasState(currentUnit);
        SetButtonVisible(specialStateButton, hasDetails);
        if (headingText != null) headingText.rectTransform.offsetMax = new Vector2(hasDetails ? -180 : -16, 0);
        bool activeAbility = definition != null && !definition.IsPassive && definition.ability != UnitSpecialSkill.None;
        bool showAction = activeAbility && !isBuilding && currentUnit.team == Team.Player && !previewOnly && !inspectionOnly;
        SetButtonVisible(specialSkillButton, showAction);
        if (showAction)
        {
            UIFactory.SetAnchors(attackButton.GetComponent<RectTransform>(), 0, .66f,
                !fixedIdentity && currentUnit.AssignedSkillId >= 0 ? .5f : 1, 1);
            if (skillButton != null) UIFactory.SetAnchors(skillButton.GetComponent<RectTransform>(), .5f, .66f, 1, 1);
            UIFactory.SetAnchors(specialSkillButton.GetComponent<RectTransform>(), 0, .33f, 1, .66f);
            if (waitButton != null) UIFactory.SetAnchors(waitButton.GetComponent<RectTransform>(), 0, 0, .5f, .33f);
            if (cancelButton != null) UIFactory.SetAnchors(cancelButton.GetComponent<RectTransform>(), .5f, 0, 1, .33f);
            bool usable = SpecialSkillRuntime.CanUse(currentUnit, turnGenerator?.Systems, out _);
            specialSkillButton.interactable = CanIssueCommand() && !turnGenerator.DeveloperPlayerAIEnabled;
            string label = SpecialSkillPresentation.Name(definition);
            label += definition.apCost < 0 || definition.cooldown < 0 ? "\n<size=75%>調整中</size>"
                : usable ? definition.ability == UnitSpecialSkill.BerserkerRage
                    ? $"\n<size=75%>[2] 次{(currentUnit.Special?.berserkerStage ?? 0) + 1}段階　AP {SpecialSkillRuntime.GetAPCost(currentUnit)}</size>"
                    : $"\n<size=75%>[2] AP {SpecialSkillRuntime.GetAPCost(currentUnit)}</size>"
                    : "\n<size=75%>準備待ち</size>";
            specialSkillButton.GetComponentInChildren<TextMeshProUGUI>().text = label;
        }
        else if (!isBuilding)
        {
            if (skillButton != null) UIFactory.SetAnchors(skillButton.GetComponent<RectTransform>(), .5f, .5f, 1, 1);
            if (waitButton != null) UIFactory.SetAnchors(waitButton.GetComponent<RectTransform>(), 0, 0, .5f, .5f);
        }
        string state = SpecialSkillPresentation.Summary(currentUnit);
        if (!string.IsNullOrEmpty(state) && passiveText != null && string.IsNullOrEmpty(passiveText.text)) passiveText.text = state;
    }

    public void OnClickSpecialSkill()
    {
        if (!CanIssueCommand() || turnGenerator.DeveloperPlayerAIEnabled) return;
        // Opening a disabled definition still explains which values the author must set.
        PlayerAttack.BeginSpecialTargeting(currentUnit, turnGenerator.Systems);
    }

    public bool TryOpenSpecialShortcut()
    {
        if (turnGenerator == null || turnGenerator.DeveloperPlayerAIEnabled || !CanIssueCommand()) return false;
        return PlayerAttack.BeginSpecialTargeting(currentUnit, turnGenerator.Systems);
    }

    public void OnClickSpecialState()
    {
        if (currentUnit == null || turnGenerator == null) return;
        SpecialSkillSelectionUI.Show(currentUnit, turnGenerator.Systems, true);
    }
}
