using System.Collections.Generic;
using UnityEngine;

public partial class BattleSystem
{
    // Reused only for the synchronous end-turn transaction; cleared even when damage ends the game.
    static readonly List<Status> counterCrystals = new List<Status>(4);
    static readonly List<Status> counterTargets = new List<Status>(32);
    static bool processingCrystalCounters;

    /// <summary>Call once from the attacking faction's formal turn end, before recovery. Repeated calls are harmless.</summary>
    public static void ProcessCrystalCounterAtTurnEnd(GameSystems systems, Team attackingTeam, int round)
    {
        if (systems?.CrystalSystem == null || systems.TurnGenerator?.IsGameOver == true
            || !MainCrystalCounterState.SupportedTeam(attackingTeam) || processingCrystalCounters) return;
        processingCrystalCounters = true;
        counterCrystals.Clear(); counterTargets.Clear();
        try
        {
            var holder = systems.CrystalSystem.Playercrystal;
            if (holder != null) holder.GetComponentsInChildren(false, counterCrystals);
            holder = systems.CrystalSystem.Enemycrystal;
            if (holder != null)
                foreach (var crystal in holder.GetComponentsInChildren<Status>())
                    if (!counterCrystals.Contains(crystal)) counterCrystals.Add(crystal);
            foreach (var crystal in counterCrystals)
            {
                if (systems.TurnGenerator?.IsGameOver == true) break;
                if (crystal == null || crystal.kind != Kind.Crystal || crystal.team == attackingTeam) continue;
                // Consume BEFORE damage/death callbacks, including a dead source: no late replay after load.
                bool pending = crystal.CrystalCounter?.TryConsume(attackingTeam, round) == true;
                if (!pending || !crystal.IsAlive || crystal.MaxHP <= 0) continue;
                MainCrystalRules.ClearLegacyShield(crystal);
                var units = attackingTeam == Team.Player ? systems.UnitSetting?.PlayerUnit
                    : attackingTeam == Team.Enemy ? systems.UnitSetting?.EnemyUnit : systems.NeutralFactionSystem?.UnitParent;
                if (units == null || systems.TerritorySystem == null) continue;
                counterTargets.Clear(); units.GetComponentsInChildren(false, counterTargets);
                for (int i = counterTargets.Count - 1; i >= 0; i--)
                {
                    var target = counterTargets[i];
                    if (target == null || !target.IsAlive || target.team != attackingTeam || target.type != Type.Unit
                        || target.kind == Kind.Crystal || !MainCrystalRules.InCounterArea(systems.TerritorySystem, target.transform.position, crystal.team))
                        counterTargets.RemoveAt(i);
                }
                counterTargets.Sort((a, b) => MainCrystalRules.CompareTargets(crystal.transform.position,
                    a.transform.position, a.ReflectionLifeId, b.transform.position, b.ReflectionLifeId));
                int count = Mathf.Min(MainCrystalRules.MaximumCounterTargets, counterTargets.Count);
                for (int i = 0; i < count; i++)
                {
                    if (systems.TurnGenerator?.IsGameOver == true || !crystal.IsAlive) break;
                    var target = counterTargets[i];
                    if (target == null || !target.IsAlive) continue;
                    int damage = MainCrystalRules.CounterDamage(target.MaxHP);
                    // Fixed counter damage still uses the shared barrier/nullification/survival rules.
                    SpecialSkillRuntime.ApplyHpChange(null, target, damage, HpChangeKind.NormalDamage, cause: HpChangeCause.Counter);
                    target.HandleDeathIfDead();
                }
            }
        }
        finally { counterTargets.Clear(); counterCrystals.Clear(); processingCrystalCounters = false; }
    }
}
