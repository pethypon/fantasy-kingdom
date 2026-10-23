using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>All legal player choices are available; AI candidate pruning is never reused here.</summary>
public sealed class SpecialSkillSelectionUI : MonoBehaviour
{
    static SpecialSkillSelectionUI instance;
    readonly List<Status> actors = new List<Status>();
    readonly List<Status> sacrifices = new List<Status>();
    readonly Dictionary<Status, TextMeshProUGUI> sacrificeLabels = new Dictionary<Status, TextMeshProUGUI>();
    readonly Dictionary<Status, Image> targetImages = new Dictionary<Status, Image>();
    Status caster, target;
    GameSystems systems;
    bool detailsOnly, lockedShot;
    int berserkerStage;
    GameObject overlay;
    TextMeshProUGUI description, feedback, ragePreview;
    Button confirm;
    float nextRefresh;

    public static void Show(Status actor, GameSystems gameSystems, bool inspect = false)
    {
        if (actor == null || gameSystems == null || UIBuilder.ScreenCanvas == null) return;
        CloseCurrent();
        var host = new GameObject("SpecialSkillSelectionUI");
        instance = host.AddComponent<SpecialSkillSelectionUI>();
        instance.Build(actor, gameSystems, inspect);
    }

    public static void CloseCurrent()
    {
        if (instance != null) instance.Close();
    }
    public static void ShowForSacrifices(Status caster, Status selectedTarget, GameSystems systems)
    {
        Show(caster, systems);
        if (instance == null) return;
        instance.target = selectedTarget;
        foreach (var entry in instance.targetImages)
        {
            var button = entry.Value != null ? entry.Value.GetComponent<Button>() : null;
            if (button != null) button.interactable = entry.Key == selectedTarget;
        }
        instance.RefreshSelection();
    }

    void OnDestroy()
    {
        if (overlay != null) Destroy(overlay);
        if (instance == this) instance = null;
    }

    void Close()
    {
        if (overlay != null) { Destroy(overlay); overlay = null; }
        if (instance == this) instance = null;
        Destroy(gameObject);
    }

    void Update()
    {
        if (caster == null || !caster.IsAlive || systems?.TurnGenerator == null)
        { Close(); return; }
        var turn = systems.TurnGenerator;
        if (!detailsOnly && (turn.DeveloperPlayerAIEnabled || !(turn.CurrentState is PlayerMove || turn.CurrentState is PlayerAttack)
            || turn.Context.SelectUnit != caster || caster.team != Team.Player))
        { Close(); return; }
        if (Keyboard.current?.escapeKey.wasPressedThisFrame == true || Mouse.current?.rightButton.wasPressedThisFrame == true)
        { Close(); return; }
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + .15f;
        if (detailsOnly) description.text = SpecialSkillPresentation.Details(caster, turn.Context.Turn);
        else RefreshSelection();
    }

    void Build(Status actor, GameSystems gameSystems, bool inspect)
    {
        caster = actor; systems = gameSystems; detailsOnly = inspect;
        var font = UIFactory.LoadDefaultFont();
        var root = UIFactory.CreatePanel("SpecialSkillOverlay", UIBuilder.ScreenCanvas.transform,
            Vector2.zero, Vector2.one, new Vector2(.5f, .5f), Vector2.zero);
        UIFactory.StretchFill(root); overlay = root.gameObject;
        // Independent HUD canvases (log, AP preview, hints) must not draw or receive clicks above the modal.
        var modalCanvas = root.gameObject.AddComponent<Canvas>();
        modalCanvas.overrideSorting = true; modalCanvas.sortingOrder = 250;
        root.gameObject.AddComponent<GraphicRaycaster>();
        root.SetAsLastSibling();
        UIFactory.AddImage(root.gameObject, new Color(0, 0, 0, .8f));
        var panel = UIFactory.CreatePanel("SpecialSkillPanel", root, new Vector2(.1f, .12f),
            new Vector2(.9f, .9f), new Vector2(.5f, .5f), Vector2.zero);
        panel.offsetMin = Vector2.zero; panel.offsetMax = Vector2.zero;
        UIFactory.AddImage(panel.gameObject, BrandGuide.PanelBg);
        BrandGuide.AddPanelBorder(panel);
        var title = UIFactory.CreateTMP("Title", panel,
            KindNameJP.Get(caster) + "　" + SpecialSkillPresentation.Name(SpecialSkillRuntime.Definition(caster)), 28, font);
        UIFactory.SetAnchors(title, .025f, .89f, .88f, .99f);
        title.alignment = TextAlignmentOptions.MidlineLeft;
        var close = UIFactory.CreateButton("Close", panel, "閉じる", 22, BrandGuide.BtnCancel, font);
        UIFactory.SetAnchors(close.GetComponent<RectTransform>(), .88f, .90f, .98f, .98f);
        close.onClick.AddListener(Close);

        if (inspect)
        {
            var content = ScrollArea("StateDetails", panel, .025f, .045f, .975f, .875f);
            description = TextRow(content, "Details", SpecialSkillPresentation.Details(caster, systems.TurnGenerator.Context.Turn), 26);
            return;
        }

        var definition = SpecialSkillRuntime.Definition(caster);
        lockedShot = SpecialSkillRuntime.TryGetSniperTarget(caster, out var lockedTarget);
        description = UIFactory.CreateTMP("Description", panel, SpecialSkillPresentation.Description(definition), 20, font);
        UIFactory.SetAnchors(description, .025f, .64f, .975f, .875f);
        description.alignment = TextAlignmentOptions.TopLeft;
        description.textWrappingMode = TextWrappingModes.Normal;
        description.enableAutoSizing = true; description.fontSizeMin = 16; description.fontSizeMax = 20;
        bool ritual = definition != null && definition.ability == UnitSpecialSkill.MagicRitual;
        bool rage = definition != null && definition.ability == UnitSpecialSkill.BerserkerRage;
        var targets = ScrollArea("LegalTargets", panel, .025f, .19f, ritual ? .49f : .975f, .625f);
        TextRow(targets, "TargetHint", rage ? "発動するたびに、次の1段階へ強化します" : "対象を選択してください（すべての合法対象）", 20);
        CombatRegistry.Collect(actors);
        if (rage)
        {
            target = caster;
            berserkerStage = Mathf.Clamp(SpecialSkillRuntime.State(caster).berserkerStage, 0, 5) + 1;
            if (berserkerStage <= 5)
            {
                string attack = definition.HasCurve(definition.berserkerAttackCurve)
                    ? Mathf.Clamp(definition.berserkerAttackCurve.Evaluate(berserkerStage), 1, SpecialSkillDefinition.BerserkerMaximum).ToString("0.##") : "未設定";
                ragePreview = TextRow(targets, "RageProgress",
                    $"現在{berserkerStage - 1}段階 → 次は{berserkerStage}段階\n使用AP：{SpecialSkillRuntime.GetAPCost(caster, berserkerStage)}\n強化後：攻撃×{attack}"
                    + "　通常攻撃AP＋1", 22);
            }
            else ragePreview = TextRow(targets, "RageProgress", "現在5段階（最大）。通常攻撃で強化を使い切るまで再使用できません。", 22);
        }
        else if (lockedShot)
        {
            target = lockedTarget;
            TextRow(targets, "LockedTarget", "ロック対象：" + KindNameJP.Get(target) + "\n" + SpecialSkillPresentation.Details(caster, systems.TurnGenerator.Context.Turn), 20);
        }
        else foreach (var candidate in actors)
        {
            if (candidate == null || !SpecialSkillRuntime.IsLegalTarget(caster, candidate, out _)) continue;
            var selected = candidate;
            var button = RowButton(targets, "Target", Label(candidate));
            targetImages[candidate] = button.GetComponent<Image>();
            button.onClick.AddListener(() => { target = selected; RefreshSelection(); });
        }
        if (targetImages.Count == 1)
            foreach (var candidate in targetImages.Keys) target = candidate;
        if (targetImages.Count == 0 && !lockedShot && !rage) TextRow(targets, "NoTargets", "現在、指定できる対象がいません。", 20);

        if (ritual)
        {
            var choices = ScrollArea("Sacrifices", panel, .51f, .19f, .975f, .625f);
            TextRow(choices, "SacrificeHint", "供儀対象を複数選択（何体選ぶかは任意）", 20);
            foreach (var candidate in actors)
            {
                if (candidate == null || !SpecialSkillRuntime.IsLegalSacrifice(caster, candidate, out _)) continue;
                var selected = candidate;
                var button = RowButton(choices, "Sacrifice", "[ ] " + Label(candidate));
                sacrificeLabels[candidate] = button.GetComponentInChildren<TextMeshProUGUI>();
                button.onClick.AddListener(() =>
                {
                    if (!sacrifices.Remove(selected)) sacrifices.Add(selected);
                    RefreshSelection();
                });
            }
            if (sacrificeLabels.Count == 0) TextRow(choices, "NoSacrifices", "供儀できる味方がいません。", 20);
        }

        feedback = UIFactory.CreateTMP("Feedback", panel, string.Empty, 20, font);
        UIFactory.SetAnchors(feedback, .025f, .025f, .72f, .175f);
        feedback.alignment = TextAlignmentOptions.MidlineLeft;
        feedback.textWrappingMode = TextWrappingModes.Normal;
        confirm = UIFactory.CreateButton("Confirm", panel, lockedShot ? "ロック対象へ射撃" : "この内容で発動", 24, BrandGuide.BtnSkill, font);
        UIFactory.SetAnchors(confirm.GetComponent<RectTransform>(), .74f, .045f, .975f, .16f);
        confirm.onClick.AddListener(Confirm);
        RefreshSelection();
    }

    void RefreshSelection()
    {
        if (confirm == null) return;
        if (lockedShot)
        {
            bool legal = SpecialSkillRuntime.TryGetSniperTarget(caster, out var current) && current == target
                && SpecialSkillRuntime.CanNormalAttackTarget(caster, target);
            bool ap = systems.APSystem != null && systems.APSystem.CanAct(Team.Player, APSystem.ActionType.Attack, caster);
            confirm.interactable = legal && ap && !StatusEffectSystem.IsStunned(caster);
            feedback.text = !legal ? "ロック対象は射撃できない状態です。" : !ap ? "通常攻撃のAPが不足しています。"
                : "ロック対象だけを通常攻撃します。距離・視界・通常射程に制限されません。";
            return;
        }
        var definition = SpecialSkillRuntime.Definition(caster);
        bool rage = definition != null && definition.ability == UnitSpecialSkill.BerserkerRage;
        bool canUse = SpecialSkillRuntime.CanUse(caster, systems, berserkerStage, out string reason);
        bool validTarget = target != null && SpecialSkillRuntime.IsLegalTarget(caster, target, out _);
        foreach (var entry in targetImages)
            if (entry.Value != null) entry.Value.color = entry.Key == target ? BrandGuide.BtnUpgrade : BrandGuide.BtnSkill;
        int total = 0;
        bool validSacrifices = true;
        foreach (var actor in sacrifices)
        {
            if (!SpecialSkillRuntime.IsLegalSacrifice(caster, actor, out _)) { validSacrifices = false; continue; }
            total = (int)System.Math.Min(int.MaxValue, (long)total + SpecialSkillRuntime.GetSacrificeAmount(actor));
        }
        foreach (var entry in sacrificeLabels)
            if (entry.Value != null) entry.Value.text = (sacrifices.Contains(entry.Key) ? "[選択] " : "[ ] ") + Label(entry.Key)
                + $"\n直接HP削減：{SpecialSkillRuntime.GetSacrificeAmount(entry.Key)}";
        bool ritual = definition != null && definition.ability == UnitSpecialSkill.MagicRitual;
        if (ritual && (sacrifices.Count == 0 || sacrifices.Count > definition.sacrificeLimit)) validSacrifices = false;
        confirm.interactable = canUse && validTarget && validSacrifices;
        feedback.text = !canUse ? reason : !validTarget ? "対象を選択してください。"
            : rage ? $"現在{berserkerStage - 1}段階 → 次は{berserkerStage}段階　使用AP：{SpecialSkillRuntime.GetAPCost(caster, berserkerStage)}\n確定すると1段階強化します。通常攻撃AP＋1。"
            : ritual ? $"供儀 {sacrifices.Count}/{definition.sacrificeLimit}体　削減HP合計：{total}\n供儀したHPは戻りません。確定前に対象を確認してください。"
            : "対象：" + KindNameJP.Get(target) + "\n確定するとAPを消費して能力を発動します。";
        if (ritual && !validSacrifices && canUse) feedback.text += "\n合法な供儀対象を人数上限内で1体以上選んでください。";
    }

    void Confirm()
    {
        if (systems?.TurnGenerator == null || systems.TurnGenerator.DeveloperPlayerAIEnabled || !(systems.TurnGenerator.CurrentState is PlayerMove || systems.TurnGenerator.CurrentState is PlayerAttack)
            || systems.TurnGenerator.Context.SelectUnit != caster) { Close(); return; }
        if (lockedShot) { ConfirmLockedShot(); return; }
        if (!SpecialSkillRuntime.TryExecute(new SpecialSkillCommand(caster, target, new List<Status>(sacrifices), berserkerStage), systems, out var reason))
        { feedback.text = reason; confirm.interactable = false; return; }
        var actor = caster;
        var turn = systems.TurnGenerator;
        if (turn.IsGameOver) { Close(); return; }
        if (!turn.IsGameOver && turn.CurrentState is PlayerAttack attack)
        {
            attack.Reset();
            turn.ChangeState(attack.move);
        }
        if (actor != null && actor.IsAlive)
        {
            systems.TurnGenerator.Context.OldCell = actor.transform.position;
            if (turn.CurrentState is PlayerMove move)
            { move.SelectedUnit = actor; move.SelectedUnitPosition = actor.transform.position; move.MenuSwitch = true; }
            systems.MoveGenerator?.MoveReset();
            systems.MoveGenerator?.MoveCore(actor, actor.transform.position);
            systems.UnitPanelUI?.Refresh();
        }
        else
        {
            if (!turn.IsGameOver && turn.CurrentState is PlayerMove move) move.Reset();
            if (!turn.IsGameOver) turn.Context.ClearSelection();
            systems.MoveGenerator?.MoveReset();
            systems.UnitPanelUI?.Hide();
        }
        Close();
    }

    void ConfirmLockedShot()
    {
        var turn = systems.TurnGenerator;
        if (caster == null || !caster.IsAlive || StatusEffectSystem.IsStunned(caster)
            || !SpecialSkillRuntime.TryGetSniperTarget(caster, out var current) || current != target
            || !SpecialSkillRuntime.CanNormalAttackTarget(caster, target) || systems.APSystem == null || systems.BattleSystem == null
            || !systems.APSystem.CanAct(Team.Player, APSystem.ActionType.Attack, caster))
        { RefreshSelection(); return; }
        var previous = turn.Context.SelectUnit;
        var attacker = caster;
        long before = (long)target.HP + target.Barrier;
        systems.BattleSystem.SetTarget(target);
        try
        {
            turn.Context.SelectUnit = attacker;
            systems.APSystem.Consume(Team.Player, APSystem.ActionType.Attack, attacker);
            systems.BattleSystem.ProcessDamage(turn);
            int dealt = (int)System.Math.Min(int.MaxValue, System.Math.Max(0, before - target.HP - target.Barrier));
            ActionLogUI.LogAttack(KindNameJP.Get(attacker), KindNameJP.Get(target), dealt, !target.IsAlive);
        }
        finally
        {
            if (!turn.IsGameOver) turn.Context.SelectUnit = previous != null && previous.IsAlive ? previous : null;
        }
        if (!turn.IsGameOver)
        {
            if (turn.CurrentState is PlayerAttack attack)
            {
                if (attacker != null && attacker.IsAlive) attack.AttackSuccess = true;
                else { attack.Reset(); attack.move.Reset(); turn.ChangeState(attack.move); }
            }
            else if ((attacker == null || !attacker.IsAlive) && turn.CurrentState is PlayerMove move) move.Reset();
            systems.RefreshVision();
            if (attacker != null && attacker.IsAlive) systems.UnitPanelUI?.Refresh();
            else systems.UnitPanelUI?.Hide();
        }
        Close();
    }

    static string Label(Status actor) => actor == null ? "対象不在" : $"{KindNameJP.Get(actor)}　HP {actor.HP}/{actor.MaxHP}"
        + (BarrierSystem.UsesBarrier(actor) ? $"　バリア {actor.Barrier}/{actor.MaxBarrier}" : "")
        + $"　({actor.GridPosition.x},{actor.GridPosition.z})";

    static RectTransform ScrollArea(string name, RectTransform parent, float x0, float y0, float x1, float y1)
    {
        var root = UIFactory.CreatePanel(name, parent, Vector2.zero, Vector2.one, new Vector2(.5f, .5f), Vector2.zero);
        UIFactory.SetAnchors(root, x0, y0, x1, y1);
        UIFactory.AddImage(root.gameObject, new Color(.03f, .04f, .06f, .95f));
        var scroll = root.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.vertical = true; scroll.scrollSensitivity = 36; scroll.movementType = ScrollRect.MovementType.Clamped;
        var viewport = UIFactory.CreatePanel("Viewport", root, Vector2.zero, Vector2.one, new Vector2(.5f, .5f), Vector2.zero);
        UIFactory.StretchFill(viewport); viewport.offsetMax = new Vector2(-32, 0);
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = UIFactory.CreatePanel("Content", viewport, new Vector2(0, 1), new Vector2(1, 1), new Vector2(.5f, 1), Vector2.zero);
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 6; layout.padding = new RectOffset(8, 8, 8, 8);
        layout.childControlHeight = true; layout.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewport; scroll.content = content;
        scroll.verticalScrollbar = UIFactory.CreateVerticalScrollbar(root);
        return content;
    }
    static TextMeshProUGUI TextRow(RectTransform parent, string name, string text, float fontSize)
    {
        var label = UIFactory.CreateTMP(name, parent, text, fontSize, UIFactory.LoadDefaultFont());
        label.alignment = TextAlignmentOptions.TopLeft; label.textWrappingMode = TextWrappingModes.Normal;
        label.gameObject.AddComponent<LayoutElement>().minHeight = 36;
        return label;
    }
    static Button RowButton(RectTransform parent, string name, string text)
    {
        var button = UIFactory.CreateButton(name, parent, text, 20, BrandGuide.BtnSkill, UIFactory.LoadDefaultFont());
        var layout = button.gameObject.AddComponent<LayoutElement>(); layout.preferredHeight = 68; layout.minHeight = 68;
        var label = button.GetComponentInChildren<TextMeshProUGUI>(); label.textWrappingMode = TextWrappingModes.Normal;
        label.raycastTarget = false;
        return button;
    }
}
