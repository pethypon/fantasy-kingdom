#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class PreviewActivationProbe : MonoBehaviour
{
    public int Activations;
    private void OnEnable() { Activations++; }
}

public static class InteractionMaintenanceTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(string name, bool ok)
    {
        if (!ok) throw new Exception("[InteractionMaintenance] FAIL " + name);
        Debug.Log("[InteractionMaintenance] PASS " + name);
    }
    static void Invoke(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, Private).Invoke(target, args);
    static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, Private).SetValue(target, value);

    public static void Run(GameSystems systems, TurnGenerator turn)
    {
        var root = new GameObject("Interaction maintenance fixture");
        var actor = root.AddComponent<Status>();
        actor.kind = Kind.Knight; actor.type = Type.Unit; actor.team = Team.Player;
        actor.HP = actor.MaxHP = 100;
        root.transform.position = new Vector3(10000, 1, 10000);
        var colliderObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        colliderObject.transform.SetParent(root.transform, false);
        colliderObject.transform.localPosition = new Vector3(2, 0, 0);
        var panel = systems.UnitPanelUI;
        var originalAP = systems.APSystem.GetAP(Team.Player);
        var originalSelection = turn.Context.SelectUnit;
        var originalRange = systems.AttackGenerator.AttackP.ToArray();
        var click = systems.UnitClick;
        PreviewActivationProbe probe = null;
        try
        {
            panel.Hide(); panel.Preview(actor);
            var button = panel.GetComponentsInChildren<Button>(true).First(b => b.name == "AttackBtn");
            probe = button.gameObject.AddComponent<PreviewActivationProbe>();
            panel.Refresh(); panel.Refresh();
            Check("preview refresh never temporarily enables action buttons", probe.Activations == 0);
            var hp = panel.GetComponentsInChildren<TextMeshProUGUI>(true).First(t => t.name == "HPText");
            hp.text = "unchanged until scheduled refresh";
            panel.Preview(actor);
            Check("same hover target skips immediate UI rebuild", hp.text == "unchanged until scheduled refresh");
            actor.HP = 73;
            Set(panel, "nextRefresh", 0f); Invoke(panel, "LateUpdate");
            Check("hover HP still refreshes without target change", hp.text.Contains("<b>73</b>"));
            panel.Show(actor); panel.Preview(null);
            Check("selection stays pinned and commands return after preview", panel.HasSelection && button.gameObject.activeSelf);

            var move = new PlayerMove(turn) { SelectedUnit = actor, MenuSwitch = true };
            turn.Context.SelectUnit = actor;
            click.UC(move, turn, systems.AttackGenerator);
            var other = new GameObject("Stunned selection"); other.transform.SetParent(root.transform);
            var stunned = other.AddComponent<Status>();
            stunned.kind = Kind.Knight; stunned.team = Team.Player; stunned.HP = stunned.MaxHP = 100;
            stunned.ActiveEffects.Add(new ActiveEffect(StatusEffectType.Stun, 1));
            move.ClickedUnit = stunned;
            Invoke(click, "HandlePlayerUnitReselect");
            Check("stunned reselect preserves the previous actor", move.SelectedUnit == actor && turn.Context.SelectUnit == actor);
            stunned.ActiveEffects.Clear(); stunned.HP = 0;
            Invoke(click, "HandlePlayerUnitReselect");
            Check("dead reselect preserves the previous actor", move.SelectedUnit == actor && turn.Context.SelectUnit == actor);

            Physics.SyncTransforms();
            Check("offset model collider fixture is hit", Physics.Raycast(new Ray(colliderObject.transform.position + Vector3.up * 5, Vector3.down), out var hit, 10));
            var attack = new PlayerAttack(turn, move, PlayerMove.AttackMode.Normal);
            Set(click, "playerattack", attack);
            Invoke(click, "HandleNormalAttackClick", hit);
            Check("attack resolves child collider to actor without attacking ally", click.AttackTarget == actor && !attack.AttackSuccess && actor.HP == 73 && systems.APSystem.GetAP(Team.Player) == originalAP);

            var skill = SkillData.Table.First(pair => pair.Value.Target == SkillTarget.AllySingle);
            actor.AssignedSkillId = skill.Key;
            systems.FactionState.SetAP(Team.Player, 100);
            int skillAPBefore = systems.APSystem.GetAP(Team.Player);
            systems.AttackGenerator.AttackP.Clear();
            systems.AttackGenerator.AttackP.Add(actor.transform.position);
            Invoke(click, "HandleSkillClick", hit);
            Check("skill uses actor cell rather than offset collider cell", attack.AttackSuccess && actor.HP > 73
                && systems.APSystem.GetAP(Team.Player) == skillAPBefore - skill.Value.APCost);
        }
        finally
        {
            panel.Hide();
            if (probe != null) UnityEngine.Object.DestroyImmediate(probe);
            systems.FactionState.SetAP(Team.Player, originalAP);
            systems.AttackGenerator.AttackP.Clear(); systems.AttackGenerator.AttackP.AddRange(originalRange);
            turn.Context.SelectUnit = originalSelection;
            if (turn.CurrentState is PlayerMove activeMove) click.UC(activeMove, turn, systems.AttackGenerator);
            click.AttackTarget = null;
            UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
#endif
