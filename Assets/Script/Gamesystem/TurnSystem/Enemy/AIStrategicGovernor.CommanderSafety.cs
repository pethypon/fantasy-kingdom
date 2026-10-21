using UnityEngine;

public sealed partial class AIStrategicGovernor
{
    const float CommanderHomeRadius = 3;
    const float CommanderEscortRadius = 2;
    const float CommanderAdvanceHealth = .75f;

    // A commander is the loss condition, not an expendable explorer. This gate is
    // shared by scoring/filtering and the final execution recheck, including F8 AI.
    string CommanderMovementRejectReason(AIAction action, AIBoardState board, float risk)
    {
        if (!IsMovement(action)) return null;
        var king = action.Unit;
        Vector3 from = king.transform.position, destination = action.TargetPos;
        if (GridHelper.ToGridXZ(from) == GridHelper.ToGridXZ(destination)) return null;
        float currentRisk = IncomingDamage(board, king, from, null);
        // Never block an escape merely because it lacks an escort or leads through
        // unseen terrain. The existing least-lethal alternative check runs first.
        if (risk < currentRisk - .01f) return null;
        if (risk > currentRisk + .01f || risk > 0 && risk >= king.HP * .2f)
            return "king_frontline_exposure";
        if (!board.IsVisibleToEnemy(destination)) return "king_unobserved_advance";
        int steps = Mathf.Clamp(Mathf.CeilToInt(Distance(from, destination)), 1, 8);
        for (int i = 1; i < steps; i++)
            if (!board.IsVisibleToEnemy(Vector3.Lerp(from, destination, (float)i / steps))) return "king_unobserved_advance";
        float homeBefore = Distance(from, board.EnemyCrystalPos), homeAfter = Distance(destination, board.EnemyCrystalPos);
        if (homeAfter < homeBefore && risk <= currentRisk + .01f) return null;
        // Do not spend leftover AP shuffling a safe king around its home. The
        // selector deliberately skips Wait, so quiet holds must be enforced here.
        if (homeBefore <= CommanderHomeRadius && currentRisk <= 0 && risk <= 0) return "king_hold_safe_home";
        if (king.MaxHP > 0 && king.HP < king.MaxHP * CommanderAdvanceHealth) return "king_wounded_advance";
        if (board.MapCreate != null && !board.MapCreate.CanTraverse(destination, from)) return "king_no_retreat";

        Status nearestThreat = null;
        float nearestDistance = float.PositiveInfinity;
        foreach (var enemy in board.AlivePlayerUnits)
        {
            if (enemy == null || !enemy.IsAlive || enemy.type != Type.Unit || !board.IsSpecialActorObserved(enemy)
                || !NeutralFactionSystem.AreHostile(king, enemy)) continue;
            float distance = Distance(destination, enemy.transform.position);
            if (distance < nearestDistance) { nearestDistance = distance; nearestThreat = enemy; }
        }
        bool escorted = false, behindEscort = false;
        foreach (var ally in board.AliveEnemyUnits)
        {
            if (ally == null || !ally.IsAlive || ally.team != board.ActorTeam || ally.type != Type.Unit
                || MatchObjectiveRules.IsCommander(ally) || ally.kind == Kind.Scout || UnitRoster.IsDedicatedHealer(ally.kind)
                || ally.HP < ally.MaxHP * .5f || StatusEffectSystem.IsStunned(ally)
                || BoardActionProfile.For(ally)?.CanAttack == false || SpecialSkillRuntime.AttackBlocked(ally)
                || Distance(destination, ally.transform.position) > CommanderEscortRadius) continue;
            escorted = true;
            if (nearestThreat != null && Distance(ally.transform.position, nearestThreat.transform.position) < nearestDistance)
                behindEscort = true;
        }
        if (!escorted) return "king_unescorted_advance";
        if (nearestDistance <= 4 && !behindEscort) return "king_ahead_of_escort";
        return null;
    }
}
