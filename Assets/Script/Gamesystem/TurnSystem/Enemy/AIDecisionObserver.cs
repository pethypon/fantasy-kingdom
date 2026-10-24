using System;
using System.Collections.Generic;

/// <summary>Optional synchronous diagnostics. No subscribers, buffers or file I/O in ordinary play.
/// Observers must never mutate candidates or feed authoritative reporting data back into the AI.</summary>
public static class AIDecisionObserver
{
    public static event Action<AIBoardState, List<AIAction>, AIAction, string> Observed;
    public static void Emit(AIBoardState board, List<AIAction> actions, AIAction selected, string stage)
        => Observed?.Invoke(board, actions, selected, stage);
}
