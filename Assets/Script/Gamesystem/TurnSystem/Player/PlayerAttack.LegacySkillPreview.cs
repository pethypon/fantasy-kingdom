using UnityEngine;

public partial class PlayerAttack
{
    bool legacySkillPreview;
    Vector3Int lastLegacyCell = new Vector3Int(int.MinValue, 0, 0);
    void BeginLegacySkillPreview()
    {
        var caster = move.SelectedUnit;
        if (caster == null || !SkillData.Table.TryGetValue(caster.AssignedSkillId, out var skill)) return;
        legacySkillPreview = true; specialRange.AddRange(Systems.AttackGenerator.AttackP);
        var host = new GameObject("Skill target range"); specialView = host.AddComponent<SkillTargetRangeView>();
        if (skill.Target == SkillTarget.Self || skill.Target == SkillTarget.SelfArea) PreviewLegacyCell(caster.transform.position, skill);
        else specialView.Show(specialRange, specialEffect);
        Systems.InputHintUI?.SetHints($"<color=#D1ABFF>{skill.Name}　AP {Systems.APSystem.CalcSkillCost(skill.APCost, caster)}　紫：使用範囲／橙：効果範囲</color>\n[左クリック] 範囲内の対象へ発動　[右クリック] 取消　[1] 通常攻撃");
    }
    void PreviewLegacyCell(Vector3 center, SkillData skill)
    {
        if (skill.Target == SkillTarget.DirectionLine || skill.Target == SkillTarget.DesignatedRow)
            center = move.SelectedUnit.transform.position;
        var cell = GridHelper.ToGridXZ(center); if (cell == lastLegacyCell) return; lastLegacyCell = cell;
        specialEffect.Clear();
        var cells = SkillSystem.GetAreaPositions(skill.Area, cell, move.SelectedUnit.direction);
        cells = SkillSystem.FilterLineSkillBlocked(cells, skill.Area, cell, Systems.MapCreate, Systems.MoveGenerator);
        foreach (var position in cells) AddEffect(position, specialEffect);
        specialView?.Show(specialRange, specialEffect);
    }
    void TickLegacySkillPreview()
    {
        if (!legacySkillPreview || Time.unscaledTime < nextSpecialPreview) return;
        nextSpecialPreview = Time.unscaledTime + .08f;
        var input = Turn.GetComponent<TurnInputHandler>();
        if (input == null || !input.HasPointer || Camera.main == null || !SkillData.Table.TryGetValue(move.SelectedUnit.AssignedSkillId, out var skill)
            || skill.Target == SkillTarget.Self || skill.Target == SkillTarget.SelfArea) return;
        if (Physics.Raycast(Camera.main.ScreenPointToRay(input.PointerPosition), out var hit, 1000))
        {
            var actor = hit.transform.GetComponentInParent<Status>();
            var position = actor != null && actor.type <= Type.Wall ? actor.transform.position : hit.point;
            foreach (var cell in specialRange)
                if (GridHelper.ToGridXZ(position) == GridHelper.ToGridXZ(cell)) { PreviewLegacyCell(cell, skill); return; }
        }
        if (specialEffect.Count > 0) { specialEffect.Clear(); lastLegacyCell.x = int.MinValue; specialView?.Show(specialRange, specialEffect); }
    }
}
