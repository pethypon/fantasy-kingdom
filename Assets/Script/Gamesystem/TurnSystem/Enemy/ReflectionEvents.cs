using UnityEngine;

/// <summary>Authoritative game events, never inferred from hidden enemy counts or predicted combat.</summary>
public static class AIReflectionEvents
{
    static AICommander Commander(TurnGenerator turn, Team team)
    {
        if (turn == null) return null;
        if (team == Team.Enemy) return turn.Systems?.AICommander;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (team == Team.Player) return turn.PlayerAI?.Commander;
#endif
        return null;
    }
    public static void RecordDamage(TurnGenerator turn, Status attacker, Status target, int damage,
        bool formationKill = false)
    {
        if (damage <= 0 || attacker == null || target == null || attacker.team == target.team) return;
        string killed = target.HP <= 0 && target.type == Type.Unit ? target.ReflectionLifeId : null;
        Commander(turn, attacker.team)?.NotifyActualDamage(damage, true, killed, formationKill);
        Commander(turn, target.team)?.NotifyActualDamage(damage, false);
    }
    public static void RecordArtifact(TurnGenerator turn, Team owner, string eventId)
        => Commander(turn, owner)?.NotifyArtifactAcquired(eventId);
}
