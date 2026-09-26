#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

public static class TacticalUIRegressionTests
{
    static void Check(string name, bool ok)
    {
        if (!ok) throw new Exception("[TacticalUI] FAIL " + name);
        Debug.Log("[TacticalUI] PASS " + name);
    }
    static Status Next(List<Status> source, Status current, bool reverse = false) =>
        (Status)typeof(PlayerMove).GetMethod("FindCycleTarget", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { source, current, reverse });

    public static void Run(GameSystems systems, TurnGenerator turn)
    {
        var root = new GameObject("Tactical UI fixture");
        var units = new List<Status>();
        var range = systems.AttackGenerator.AttackP.ToArray();
        var vision = (HashSet<Vector3Int>)typeof(VisionGenerator).GetField("_playerVisionBox", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(systems.VisionGenerator);
        var cell = new Vector3Int(10000, 0, 10000);
        bool added = vision.Add(cell);
        try
        {
            bool shiftEndsTurn = false;
            bool enterEndsTurn = false;
            foreach (var binding in turn.GameAction.GamePlay.TurnEnd.bindings)
            {
                shiftEndsTurn |= binding.path == "<Keyboard>/leftShift" || binding.path == "<Keyboard>/rightShift";
                enterEndsTurn |= binding.path == "<Keyboard>/enter";
            }
            Check("Shift cycling cannot end turn and Enter remains bound", !shiftEndsTurn && enterEndsTurn);
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("Cycle " + i); go.transform.SetParent(root.transform);
                var s = go.AddComponent<Status>(); s.kind = Kind.Knight; s.team = Team.Player;
                s.HP = s.MaxHP = 100; units.Add(s);
            }
            Check("forward follows actual mouse selection", Next(units, units[1]) == units[2]);
            Check("forward wraps at end", Next(units, units[2]) == units[0]);
            Check("reverse wraps at beginning", Next(units, units[0], true) == units[2]);
            Check("unselected starts at correct end", Next(units, null) == units[0] && Next(units, null, true) == units[2]);
            units[1].ActiveEffects.Add(new ActiveEffect(StatusEffectType.Stun, 1));
            Check("cycle skips stunned units", Next(units, units[0]) == units[2]);
            units[2].HP = 0;
            Check("only eligible unit remains selected", Next(units, units[0]) == units[0]);
            root.SetActive(false);
            Check("inactive hierarchy cannot be cycled", Next(units, null) == null);
            root.SetActive(true);
            units[1].ActiveEffects.Clear(); units[2].HP = 100;

            var target = units[0]; target.transform.position = cell;
            systems.AttackGenerator.AttackP.Clear(); systems.AttackGenerator.AttackP.Add(cell);
            foreach (var team in new[] { Team.Enemy, Team.Monster, Team.Intruder, Team.Obstacle })
            {
                target.team = team;
                Check("normal preview supports hostile " + team, systems.UnitClick.CanTargetNormalAttack(target));
            }
            target.team = Team.Player;
            Check("normal preview rejects ally", !systems.UnitClick.CanTargetNormalAttack(target));
            target.team = Team.Enemy; systems.AttackGenerator.AttackP.Clear();
            Check("normal preview rejects out-of-range enemy", !systems.UnitClick.CanTargetNormalAttack(target));
            systems.AttackGenerator.AttackP.Add(cell); vision.Remove(cell);
            Check("normal preview rejects unseen enemy", !systems.UnitClick.CanTargetNormalAttack(target));
            var panel = systems.UnitPanelUI;
            foreach (var b in panel.GetComponentsInChildren<Button>(true))
            {
                var accent = b.transform.Find("CommandAccent");
                if (accent != null) Check("command decoration does not intercept " + b.name, !accent.GetComponent<Image>().raycastTarget);
            }
        }
        finally
        {
            if (added) vision.Remove(cell); else vision.Add(cell);
            systems.AttackGenerator.AttackP.Clear(); systems.AttackGenerator.AttackP.AddRange(range);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
#endif
