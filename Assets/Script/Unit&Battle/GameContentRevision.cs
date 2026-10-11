using System.Threading;
using UnityEngine;

/// <summary>One revision for authored balance caches. Asset callbacks only signal; runtime owners refresh on the main thread.</summary>
public static class GameContentRevision
{
    static int revision;
    public static int Revision => Volatile.Read(ref revision);
    public static void NotifyChanged() => Interlocked.Increment(ref revision);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession() => revision = 0;
}
