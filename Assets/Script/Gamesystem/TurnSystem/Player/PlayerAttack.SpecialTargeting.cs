using System.Collections.Generic;
using UnityEngine;

public partial class PlayerAttack
{
    readonly List<Status> specialTargets = new List<Status>();
    readonly List<Vector3> specialRange = new List<Vector3>(), specialEffect = new List<Vector3>();
    readonly HashSet<Vector3Int> specialRangeKeys = new HashSet<Vector3Int>();
    readonly RaycastHit[] specialHits = new RaycastHit[48];
    SkillTargetRangeView specialView;
    Status hoveredSpecial;
    float nextSpecialPreview;
    bool fixedSkillMode;
    public bool IsFixedSkillTargeting => fixedSkillMode;
    public IReadOnlyList<Vector3> SpecialRangeCells => specialRange;
    public IReadOnlyList<Vector3> SpecialEffectCells => specialEffect;

    void BeginFixedSkillTargeting()
    {
        fixedSkillMode = true;
        var caster = move.SelectedUnit; var definition = SpecialSkillRuntime.Definition(caster);
        CombatRegistry.Collect(specialTargets);
        bool locked = SpecialSkillRuntime.TryGetSniperTarget(caster, out var lockedTarget);
        specialTargets.RemoveAll(target => locked ? target != lockedTarget : !SpecialSkillRuntime.IsLegalTarget(caster, target, out _));
        if (IsSelfSkill(definition.ability) && !specialTargets.Contains(caster)) specialTargets.Add(caster);
        if (locked && !specialTargets.Contains(lockedTarget)) specialTargets.Add(lockedTarget);
        if (definition.ability == UnitSpecialSkill.NinjaSwap || locked)
        { foreach (var target in specialTargets) AddRange(target.transform.position); }
        else if (IsSelfSkill(definition.ability))
        {
            CollectEffectCells(caster, specialEffect);
            foreach (var cell in specialEffect) AddRange(cell);
            AddRange(caster.transform.position);
        }
        else if (definition.ability == UnitSpecialSkill.HeavyWarriorBreak || definition.ability == UnitSpecialSkill.DragonKnightPierce)
        {
            foreach (var cell in caster.VisionCell)
                if (cell != caster.GridPosition && GridHelper.ChebyshevDistance(caster.GridPosition, cell) <= 1
                    && Systems.MapCreate.TryGetHeight(cell.x, cell.z, out float height))
                    AddRange(new Vector3(cell.x, height, cell.z));
            foreach (var target in specialTargets) AddRange(target.transform.position);
        }
        else
        {
            foreach (var cell in caster.VisionCell)
                if (Systems.MapCreate.TryGetHeight(cell.x, cell.z, out float height)) AddRange(new Vector3(cell.x, height, cell.z));
            foreach (var target in specialTargets) AddRange(target.transform.position);
        }
        Systems.AttackGenerator.AttackP = new List<Vector3>(specialRange);
        var host = new GameObject("Skill target range"); specialView = host.AddComponent<SkillTargetRangeView>();
        specialView.Show(specialRange, specialEffect);
        UpdateSpecialHint(IsSelfSkill(definition.ability) ? caster : null);
    }

    static bool IsSelfSkill(UnitSpecialSkill skill) => SpecialSkillRuntime.UsesAutomaticTarget(skill);

    void AddRange(Vector3 position)
    { if (specialRangeKeys.Add(GridHelper.ToGridXZ(position))) specialRange.Add(position); }

    void CollectEffectCells(Status target, List<Vector3> output)
    {
        output.Clear(); var caster = move.SelectedUnit; var definition = SpecialSkillRuntime.Definition(caster);
        if (caster == null || target == null || definition == null) return;
        var center = target.transform.position;
        if (definition.ability == UnitSpecialSkill.HolyKnightSanctuary)
        {
            foreach (var offset in definition.holyKnightArea) AddEffect(center + new Vector3(offset.x, 0, offset.y), output);
        }
        else if (definition.ability == UnitSpecialSkill.KnightBreakthrough)
        {
            var previous = center;
            for (int step = 1; step <= 3; step++)
            {
                var next = center + new Vector3(0, 0, step * MovePatterns.DirZ(caster.direction));
                var key = GridHelper.ToGridXZ(next);
                if (!Systems.MapCreate.TryGetHeight(key.x, key.z, out float height)) break;
                next.y = height;
                if (!Systems.MapCreate.CanTraverse(previous, next)) break;
                AddEffect(next, output); previous = next;
            }
        }
        else if (definition.ability == UnitSpecialSkill.ScoutVisionGuard)
        { foreach (var cell in caster.VisionCell) AddEffect(cell, output); }
        else if (definition.ability == UnitSpecialSkill.ArcherStationary || definition.ability == UnitSpecialSkill.PriestPurification
            || definition.ability == UnitSpecialSkill.LifeBomb)
        {
            int radius = definition.ability == UnitSpecialSkill.LifeBomb ? Mathf.Max(0, definition.bombExplosionRadius) : 2;
            foreach (var cell in Systems.MapCreate.SetPos)
                if (GridHelper.ChebyshevDistance(center, cell) <= radius) AddEffect(cell, output);
        }
        else AddEffect(center, output);
    }
    void AddEffect(Vector3 position, List<Vector3> output)
    {
        var cell = GridHelper.ToGridXZ(position);
        if (Systems.MapCreate.TryGetHeight(cell.x, cell.z, out float height))
            output.Add(new Vector3(cell.x, height, cell.z));
    }

    public void PreviewSpecialTarget(Status target)
    {
        if (target != null && !specialTargets.Contains(target)) target = null;
        if (!fixedSkillMode || target == hoveredSpecial) return;
        hoveredSpecial = target;
        CollectEffectCells(target, specialEffect); specialView?.Show(specialRange, specialEffect); UpdateSpecialHint(target);
    }

    void UpdateSpecialHint(Status target)
    {
        var caster = move.SelectedUnit; var definition = SpecialSkillRuntime.Definition(caster);
        string description = SpecialSkillRuntime.TryGetSniperTarget(caster, out _) ? "ロック対象への通常射撃" : SpecialSkillPresentation.Name(definition);
        int ap = SpecialSkillRuntime.TryGetSniperTarget(caster, out _) ? Systems.APSystem.CalcCost(APSystem.ActionType.Attack, caster) : SpecialSkillRuntime.GetAPCost(caster);
        string cost = ap < 0 ? "未設定" : ap.ToString();
        string targetText = target != null ? KindNameJP.Get(target) : "対象にカーソルを合わせてください";
        Systems.InputHintUI?.SetHints($"<color=#D1ABFF>{description}　AP {cost}　紫：使用範囲／橙：効果範囲</color>\n{targetText}　[左クリック] 発動　[右クリック] 取消　[1] 通常攻撃");
    }

    void HandleFixedSkillTargeting()
    {
        var input = Turn.GetComponent<TurnInputHandler>();
        if (input != null && input.HasPointer && Camera.main != null && (Context.LeftClickDown || Time.unscaledTime >= nextSpecialPreview))
        {
            nextSpecialPreview = Time.unscaledTime + .08f;
            var ray = Camera.main.ScreenPointToRay(input.PointerPosition);
            var target = PickSpecialTarget(ray);
            if (!IsSelfSkill(SpecialSkillRuntime.Definition(move.SelectedUnit).ability)) PreviewSpecialTarget(target);
            if (Context.LeftClickDown && target != null) TryUseSpecialTarget(target);
        }
    }
    Status PickSpecialTarget(Ray ray)
    {
        int count = Physics.RaycastNonAlloc(ray, specialHits, 1000, ~0, QueryTriggerInteraction.Ignore);
        Status closest = null; float distance = float.MaxValue;
        bool self = IsSelfSkill(SpecialSkillRuntime.Definition(move.SelectedUnit).ability);
        for (int i = 0; i < count; i++)
        {
            var hit = specialHits[i]; var actor = hit.transform.GetComponentInParent<Status>();
            if (actor != null && specialTargets.Contains(actor) && hit.distance < distance) { closest = actor; distance = hit.distance; }
            if (self && specialRangeKeys.Contains(GridHelper.ToGridXZ(hit.point)) && hit.distance < distance)
            { closest = move.SelectedUnit; distance = hit.distance; }
            else if (actor == null)
            {
                var cell = GridHelper.ToGridXZ(hit.point);
                foreach (var target in specialTargets)
                    if (target.GridPosition == cell && hit.distance < distance) { closest = target; distance = hit.distance; }
            }
        }
        return closest;
    }
    public bool TryUseSpecialRay(Ray ray) => TryUseSpecialTarget(PickSpecialTarget(ray));

    public bool TryUseSpecialTarget(Status target)
    {
        if (!fixedSkillMode || Turn.CurrentState != this || Turn.DeveloperPlayerAIEnabled || target == null || !specialTargets.Contains(target)) return false;
        var caster = move.SelectedUnit;
        if (caster == null || !caster.IsAlive || caster.team != Team.Player || Context.SelectUnit != caster) return false;
        if (SpecialSkillRuntime.TryGetSniperTarget(caster, out var locked) && locked == target)
        {
            if (!SpecialSkillRuntime.CanNormalAttackTarget(caster, target) || !Systems.APSystem.CanAct(Team.Player, APSystem.ActionType.Attack, caster))
            { ToastMessageUI.Show("射撃できないか、APが不足しています", ToastMessageUI.MessageType.Warning); return false; }
            Systems.APSystem.Consume(Team.Player, APSystem.ActionType.Attack, caster);
            Systems.BattleSystem.SetTarget(target); Systems.BattleSystem.ProcessDamage(Turn);
        }
        else
        {
            if (!SpecialSkillRuntime.CanUse(caster, Systems, out var reason) || !SpecialSkillRuntime.IsLegalTarget(caster, target, out reason))
            { ToastMessageUI.Show(reason, ToastMessageUI.MessageType.Warning); return false; }
            if (SpecialSkillRuntime.Definition(caster).ability == UnitSpecialSkill.MagicRitual)
            { SpecialSkillSelectionUI.ShowForSacrifices(caster, target, Systems); return true; }
            if (!SpecialSkillRuntime.TryExecute(new SpecialSkillCommand(caster, target), Systems, out reason))
            { ToastMessageUI.Show(reason, ToastMessageUI.MessageType.Warning); return false; }
        }
        if (!Turn.IsGameOver && Turn.CurrentState == this) { AttackSuccess = true; HandleAttackSuccess(); }
        return true;
    }
    void EndFixedSkillTargeting()
    {
        if (specialView != null) { specialView.Hide(); Object.Destroy(specialView.gameObject); specialView = null; }
        specialTargets.Clear(); specialRange.Clear(); specialEffect.Clear(); specialRangeKeys.Clear();
    }
}
