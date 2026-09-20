using System.Collections.Generic;
using UnityEngine;
/// <summary>All active combat actors, including neutral factions and crystals. No scene searches.</summary>
public static class CombatRegistry
{
    static readonly HashSet<Status> actors = new HashSet<Status>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => actors.Clear();
    public static void Register(Status actor) { if (actor != null && actor.type <= Type.Wall) actors.Add(actor); }
    public static void Unregister(Status actor) => actors.Remove(actor);
    public static Status[] Snapshot() { var list = new List<Status>(); Collect(list); return list.ToArray(); }
    public static void Collect(List<Status> result)
    {
        result.Clear();
        foreach (var actor in actors)
            if (actor != null && actor.IsAlive && actor.gameObject.activeInHierarchy && actor.type <= Type.Wall) result.Add(actor);
    }
}
