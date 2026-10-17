using System.Collections.Generic;
using UnityEngine;

public partial class PlayerAttack
{
    /// <summary>Handles both buttons and the 2 key. True also consumes a rejected command, preventing fallback/double casting.</summary>
    public static bool BeginSpecialTargeting(Status caster, GameSystems systems)
    {
        var turn = systems?.TurnGenerator;
        if (turn == null || turn.IsGameOver || turn.DeveloperPlayerAIEnabled || caster == null || !caster.IsAlive
            || caster.team != Team.Player || turn.Context.SelectUnit != caster) return false;
        var move = turn.CurrentState is PlayerMove player ? player : (turn.CurrentState as PlayerAttack)?.move;
        if (move == null) return false;
        var definition = SpecialSkillRuntime.Definition(caster);
        bool fixedSkill = definition != null && definition.ability != UnitSpecialSkill.None;
        SkillData legacy = null;
        bool legacyImmediate = !fixedSkill && SkillData.Table.TryGetValue(caster.AssignedSkillId, out legacy)
            && (legacy.Target == SkillTarget.Self || legacy.Target == SkillTarget.SelfArea);
        if (!fixedSkill && !legacyImmediate) return false;
        var profile = BoardActionProfile.For(caster);
        if (profile != null && !profile.CanUseSkills) return RejectSkillCommand("この駒の行動設定では使用できません");
        if (legacyImmediate) return UseAutomaticLegacySkill(caster, legacy, systems, move);
        if (SpecialSkillRuntime.UsesAutomaticTarget(definition.ability))
        {
            if (!SpecialSkillRuntime.CanUse(caster, systems, out var reason)
                || !SpecialSkillRuntime.IsLegalTarget(caster, caster, out reason)) return RejectSkillCommand(reason);
            if (!SpecialSkillRuntime.HasAutomaticSkillTarget(caster)) return RejectSkillCommand("効果範囲内に対象がいません");
            if (!SpecialSkillRuntime.TryExecute(new SpecialSkillCommand(caster, caster), systems, out reason)) return RejectSkillCommand(reason);
            RestoreSkillSelection(caster, systems, move);
            return true;
        }
        SpecialSkillSelectionUI.CloseCurrent();
        if (turn.CurrentState is PlayerAttack oldAttack) oldAttack.Reset();
        systems.MoveGenerator?.MoveReset(); move.SelectedUnit = caster; move.SelectedUnitPosition = caster.transform.position;
        move.CurrentAttackMode = PlayerMove.AttackMode.Skill;
        turn.ChangeState(new PlayerAttack(turn, move, PlayerMove.AttackMode.Skill)); return true;
    }

    static bool RejectSkillCommand(string reason)
    { ToastMessageUI.Show(reason, ToastMessageUI.MessageType.Warning); return true; }

    static void RestoreSkillSelection(Status caster, GameSystems systems, PlayerMove move)
    {
        var turn = systems.TurnGenerator;
        if (turn.IsGameOver) return;
        SpecialSkillSelectionUI.CloseCurrent();
        if (turn.CurrentState is PlayerAttack oldAttack) oldAttack.Reset();
        systems.MoveGenerator?.MoveReset(); move.ClickedUnit = null;
        if (caster != null && caster.IsAlive)
        {
            turn.Context.SelectUnit = caster; move.SelectedUnit = caster;
            move.SelectedUnitPosition = caster.transform.position; move.MenuSwitch = true;
        }
        else { move.Reset(); systems.UnitPanelUI?.Hide(); }
        turn.ChangeState(move);
        if (caster != null && caster.IsAlive)
        { systems.MoveGenerator?.MoveCore(caster, caster.transform.position); systems.UnitPanelUI?.Show(caster); }
    }

    static bool UseAutomaticLegacySkill(Status caster, SkillData skill, GameSystems systems, PlayerMove move)
    {
        if (systems.SkillSystem == null || systems.APSystem == null || !AttackPatterns.CanUseSkills(caster)
            || SpecialSkillRuntime.AttackBlocked(caster) || StatusEffectSystem.IsStunned(caster)
            || StatusEffectSystem.HasDebuff(caster, StatusEffectType.Seal)) return RejectSkillCommand("スキルを使用できない状態です");
        int cost = systems.APSystem.CalcSkillCost(skill.APCost, caster);
        if (!systems.APSystem.CanUseSkill(caster.team, cost)) return RejectSkillCommand("AP不足：スキルを使用できません");
        List<Status> allies = null, enemies = null;
        if (skill.Target == SkillTarget.SelfArea)
        {
            var center = caster.GridPosition;
            var positions = SkillSystem.FilterLineSkillBlocked(SkillSystem.GetAreaPositions(skill.Area, center, caster.direction),
                skill.Area, center, systems.MapCreate, systems.MoveGenerator);
            var cells = new HashSet<Vector3Int>(positions);
            allies = new List<Status>(); enemies = new List<Status>();
            var actors = new List<Status>(); CombatRegistry.Collect(actors);
            foreach (var actor in actors)
            {
                if (actor == null || !actor.IsAlive || !cells.Contains(actor.GridPosition)) continue;
                if (actor.team == caster.team && actor.type == Type.Unit) allies.Add(actor);
                else if (NeutralFactionSystem.AreHostile(caster, actor) && systems.VisionGenerator != null
                    && systems.VisionGenerator.IsActorVisibleTo(caster.team, actor)
                    && systems.MapCreate.CanAttackAcrossTerrain(caster, actor.transform.position)) enemies.Add(actor);
            }
            bool support = skill.FixedHeal > 0 || skill.GrantBuff != BuffType.None || skill.SpecialEffect == "LastSignal";
            if (!(support && allies.Count > 0) && !(skill.Multiplier > 0 && enemies.Count > 0))
                return RejectSkillCommand("効果範囲内に対象がいません");
        }
        systems.APSystem.ConsumeSkill(caster.team, skill.APCost, caster);
        if (skill.Target == SkillTarget.Self) systems.SkillSystem.ExecuteSkill(caster, caster, skill);
        else
        {
            if (skill.Multiplier > 0) systems.SkillSystem.ExecuteAreaSkill(caster, skill, enemies);
            if (!caster.IsAlive || systems.TurnGenerator.IsGameOver)
            { RestoreSkillSelection(caster, systems, move); return true; }
            allies.RemoveAll(ally => ally == null || !ally.IsAlive);
            if (skill.SpecialEffect == "LastSignal")
                foreach (var ally in allies) StatusEffectSystem.ApplyBuff(ally, BuffType.Offensive);
            else systems.SkillSystem.ExecuteAreaSupportSkill(caster, skill, allies);
        }
        RestoreSkillSelection(caster, systems, move);
        return true;
    }
}
