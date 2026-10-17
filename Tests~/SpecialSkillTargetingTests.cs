#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public static class SpecialSkillTargetingTests
{
    static int passed;
    static void Check(bool value, string message) { if (!value) throw new Exception("[SkillTargeting] " + message); passed++; }
    public static void PlayMode(GameSystems systems)
    {
        passed = 0;
        var turn = systems.TurnGenerator; var originalState = turn.CurrentState; var originalSelection = turn.Context.SelectUnit;
        int originalAP = systems.FactionState.GetAP(Team.Player);
        var owned = new List<UnityEngine.Object>();
        try
        {
            var open = new List<Vector3>(); systems.MoveGenerator.UnitPointCore();
            foreach (var cell in systems.MapCreate.SetPos)
                if (!systems.MoveGenerator.IsOccupied(systems.MoveGenerator.Cell(cell))) { open.Add(cell); if (open.Count == 3) break; }
            Check(open.Count == 3, "fixture has free terrain cells");
            Status Actor(string name, Vector3 position)
            {
                var host = new GameObject(name); owned.Add(host); var actor = host.AddComponent<Status>();
                actor.team = Team.Player; actor.kind = Kind.WhiteMage; actor.type = Type.Unit; actor.Level = 100;
                actor.HP = actor.MaxHP = 1000; actor.transform.position = position;
                host.AddComponent<BoxCollider>().size = new Vector3(.7f, .7f, .7f);
                CombatRegistry.Register(actor); return actor;
            }
            var caster = Actor("Targeting caster", open[0]); var patient = Actor("Targeting patient", open[1]); patient.HP = 100;
            var outside = Actor("Targeting excluded patient", open[2]); outside.HP = 100;
            var data = ScriptableObject.CreateInstance<UnitData>(); owned.Add(data); caster.GrowthData = data;
            var definition = ScriptableObject.CreateInstance<SpecialSkillDefinition>(); owned.Add(definition); data.specialSkill = definition;
            definition.ability = UnitSpecialSkill.WhiteMageHeal; definition.apCost = 12; definition.cooldown = 0;
            caster.VisionCell = new HashSet<Vector3Int> { caster.GridPosition, patient.GridPosition };
            var move = new PlayerMove(turn); move.SelectedUnit = caster; move.SelectedUnitPosition = caster.transform.position; move.MenuSwitch = true;
            turn.ChangeState(move); turn.Context.SelectUnit = caster; systems.UnitPanelUI.Show(caster);
            systems.FactionState.SetAP(Team.Player, 30);
            Check(systems.UnitPanelUI.TryOpenSpecialShortcut(), "2-key shortcut enters map targeting");
            var attack = turn.CurrentState as PlayerAttack;
            Check(attack != null && attack.IsFixedSkillTargeting, "unique skill shares normal PlayerAttack state");
            Check(attack.SpecialRangeCells.Count > 0 && patient.HP == 100 && systems.FactionState.GetAP(Team.Player) == 30, "range display has no side effects");
            attack.PreviewSpecialTarget(patient);
            Check(attack.SpecialEffectCells.Count == 1 && GridHelper.ToGridXZ(attack.SpecialEffectCells[0]) == patient.GridPosition, "hover previews the actual single-target effect");
            Physics.SyncTransforms();
            Check(!attack.TryUseSpecialRay(new Ray(outside.transform.position + Vector3.up * 5, Vector3.down))
                && systems.FactionState.GetAP(Team.Player) == 30, "click outside legal skill targets cannot cast or spend AP");
            Check(attack.TryUseSpecialRay(new Ray(patient.transform.position + Vector3.up * 5, Vector3.down)), "world collider click uses the highlighted target");
            Check(patient.HP == 400 && systems.FactionState.GetAP(Team.Player) == 18, "click executes exactly thirty-percent healing and twelve AP");
            Check(turn.CurrentState is PlayerMove && turn.Context.SelectUnit == caster, "casting returns to the selected unit");
            Check(PlayerAttack.BeginSpecialTargeting(caster, systems), "can preview the next cast"); attack = (PlayerAttack)turn.CurrentState;
            systems.FactionState.SetAP(Team.Player, 1);
            Check(!attack.TryUseSpecialTarget(patient) && patient.HP == 400 && systems.FactionState.GetAP(Team.Player) == 1, "AP changes are revalidated at click time");
            turn.Context.RightClickDown = true; attack.Update(); turn.Context.RightClickDown = false;
            Check(turn.CurrentState is PlayerMove && systems.FactionState.GetAP(Team.Player) == 1, "right-click cancels without a cast");
            caster.team = Team.Enemy;
            Check(!PlayerAttack.BeginSpecialTargeting(caster, systems), "enemy actors cannot issue player skill commands"); caster.team = Team.Player;
            definition.ability = UnitSpecialSkill.MagicSniperLock; definition.apCost = 18;
            definition.sniperRecoilCurve = AnimationCurve.Linear(0, 0, 25, .15f);
            patient.team = Team.Enemy; caster.Special = new SpecialState { sniperTargetId = patient.ReflectionLifeId, sniperFireTurn = 0 };
            systems.FactionState.SetAP(Team.Player, 30);
            PlayerAttack.BeginSpecialTargeting(caster, systems); attack = (PlayerAttack)turn.CurrentState;
            StatusEffectSystem.ApplyDebuff(caster, StatusEffectType.Stun);
            Check(!attack.TryUseSpecialTarget(patient) && systems.FactionState.GetAP(Team.Player) == 30 && patient.HP == 400,
                "stunned locked sniper cannot lose AP to a rejected shot");
            caster.ActiveEffects.Clear();
            var supportProfile = ScriptableObject.CreateInstance<BoardActionProfile>(); owned.Add(supportProfile);
            supportProfile.actionType = ActorActionType.Support; data.actionProfile = supportProfile;
            Check(!attack.TryUseSpecialTarget(patient) && systems.FactionState.GetAP(Team.Player) == 30,
                "support-only action profile cannot pay for locked normal fire");
            data.actionProfile = null; caster.HP = 0;
            Check(!attack.TryUseSpecialTarget(patient) && systems.FactionState.GetAP(Team.Player) == 30,
                "dead caster cannot pay for a stale locked target selection");
            caster.HP = 1000; patient.team = Team.Player; attack.Reset(); turn.ChangeState(move);
            // Automatic actions use the same command entry as the button and physical 2 key, without a second map click.
            caster.transform.position = new Vector3(140000, 0, 140000);
            patient.transform.position = caster.transform.position + Vector3.right * 10;
            outside.transform.position = caster.transform.position + Vector3.forward * 10;
            caster.Special = new SpecialState(); definition.ability = UnitSpecialSkill.BerserkerRage; definition.ApplySpecifiedAPCost();
            definition.berserkerAttackCurve = AnimationCurve.Linear(0, 1, 5, 2.5f);
            definition.berserkerDefenceCurve = AnimationCurve.Linear(0, 1, 5, .5f);
            definition.berserkerAttackAPCurve = AnimationCurve.Linear(0, 1, 5, 1);
            systems.FactionState.SetAP(Team.Player, 30); systems.UnitPanelUI.Show(caster);
            systems.UnitPanelUI.OnClickSpecialSkill();
            Check(turn.CurrentState is PlayerMove && caster.Special.berserkerStage == 1 && systems.FactionState.GetAP(Team.Player) == 25,
                "self-buff button immediately casts exactly once without an enemy");
            Check(systems.UnitPanelUI.TryOpenSpecialShortcut() && caster.Special.berserkerStage == 2 && systems.FactionState.GetAP(Team.Player) == 17,
                "2-key entry immediately advances only the next stage for eight AP");
            systems.FactionState.SetAP(Team.Player, 7);
            Check(systems.UnitPanelUI.TryOpenSpecialShortcut() && caster.Special.berserkerStage == 2 && systems.FactionState.GetAP(Team.Player) == 7,
                "rejected automatic shortcut is consumed without fallback, casting or AP loss");
            definition.ability = UnitSpecialSkill.ArcherStationary; definition.apCost = 3;
            definition.stationaryAttackCurve = AnimationCurve.Linear(0, 1, 5, 1.75f); caster.Special = new SpecialState();
            Check(systems.UnitPanelUI.TryOpenSpecialShortcut() && caster.Special.archerEscapeTurns == 0 && systems.FactionState.GetAP(Team.Player) == 7,
                "area skill with no recipient does not activate or spend AP");
            var hostile = Actor("Automatic area hostile", caster.transform.position + Vector3.right); hostile.team = Team.Enemy;
            Check(systems.UnitPanelUI.TryOpenSpecialShortcut() && turn.CurrentState is PlayerMove && caster.Special.archerEscapeTurns == 2
                && caster.Special.archerSnapshotIds.Contains(hostile.ReflectionLifeId) && systems.FactionState.GetAP(Team.Player) == 4,
                "area skill with a recipient activates immediately once");
            Check(systems.UnitPanelUI.TryOpenSpecialShortcut() && systems.FactionState.GetAP(Team.Player) == 4,
                "repeated input cannot restart an active area effect");
            hostile.gameObject.SetActive(false); definition.ability = UnitSpecialSkill.PriestPurification; definition.apCost = 8;
            caster.Special = new SpecialState(); systems.FactionState.SetAP(Team.Player, 20);
            Check(systems.UnitPanelUI.TryOpenSpecialShortcut() && caster.IsAlive && systems.FactionState.GetAP(Team.Player) == 20,
                "purification does not count its dying caster as a recipient");
            patient.transform.position = caster.transform.position + Vector3.right;
            StatusEffectSystem.ApplyBuff(patient, BuffType.Offensive);
            Check(systems.UnitPanelUI.TryOpenSpecialShortcut() && !caster.IsAlive && patient.ActiveEffects.Count == 0
                && turn.CurrentState is PlayerMove && turn.Context.SelectUnit == null && systems.FactionState.GetAP(Team.Player) == 12,
                "suicide area cast clears effects and dead selection without leaving targeting UI");
            // Existing data-driven self skills use the same immediate dispatch.
            move.SelectedUnit = patient; move.SelectedUnitPosition = patient.transform.position; move.MenuSwitch = true;
            turn.Context.SelectUnit = patient; patient.AssignedSkillId = 3; systems.UnitPanelUI.Show(patient);
            int legacyCost = systems.APSystem.CalcSkillCost(SkillData.Table[3].APCost, patient);
            Check(systems.UnitPanelUI.TryOpenSpecialShortcut() && turn.CurrentState is PlayerMove
                && StatusEffectSystem.HasBuff(patient, BuffType.Defensive) && systems.FactionState.GetAP(Team.Player) == 12 - legacyCost,
                "legacy self skill also activates immediately through the 2-key entry");
            Debug.Log("[SkillTargeting] SUMMARY passed=" + passed);
        }
        finally
        {
            SpecialSkillSelectionUI.CloseCurrent(); turn.Context.RightClickDown = false;
            if (turn.CurrentState is PlayerAttack attack) attack.Reset();
            turn.ChangeState(originalState); turn.Context.SelectUnit = originalSelection;
            systems.FactionState.SetAP(Team.Player, originalAP);
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
            systems.MoveGenerator.UnitPointCore(); systems.RefreshVision();
        }
    }
}
#endif
