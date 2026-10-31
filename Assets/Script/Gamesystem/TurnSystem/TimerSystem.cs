using UnityEngine;

/// <summary>
/// Legacy clock compatibility for saved games. R2 has no turn or match time limit.
/// AI search retains its separate technical budgets; these values never end a turn or decide a game.
/// </summary>
public class TimerSystem : MonoBehaviour
{
    // Preserve serialized names so old scenes and saves remain readable. They are inert historical data.
    [HideInInspector] public float TurnTimeLimit = 180f;
    [HideInInspector] public float PlayerTotalTime = 36000f;
    [HideInInspector] public float EnemyTotalTime = 36000f;
    [HideInInspector] public float MaxTotalTime = 216000f;
    [HideInInspector] public float TurnTimeBonus = 60f;

    float turnTimeRemaining;
    bool isRunning;
    Team currentTeam;

    // Kept for call-site / save compatibility. No R2 path invokes either callback.
    public System.Action OnTurnTimeExpired;
    public System.Action<GameResult> OnTotalTimeExpired;
    public bool TimeLimitsEnabled => false;
    public float TurnTimeRemaining => turnTimeRemaining;
    public float PlayerTimeRemaining => PlayerTotalTime;
    public float EnemyTimeRemaining => EnemyTotalTime;
    public Team CurrentTeam => currentTeam;
    public bool IsRunning => isRunning;
    public GameResult? LastTimeUpResult => null;
    public string TimeUpReasonText => string.Empty;

    /// <summary>No gameplay deadline; callers still apply their own search/turn safety budgets.</summary>
    public double AvailableAIThinkingMilliseconds(Team team) => double.PositiveInfinity;

    public void RestoreTurnTimeRemaining(float value) => turnTimeRemaining = SafeSeconds(value);
    public void Init(TurnGenerator turnGenerator, CrystalSystem crystalsystem) { }
    public void ResumeTurn(Team team) { currentTeam = team; isRunning = true; }
    public void StartTurn(Team team) { currentTeam = team; isRunning = true; }
    public void StopTurn() => isRunning = false;

    /// <summary>Legacy test/caller seam. Advancing elapsed time has no gameplay effect in R2.</summary>
    internal void Advance(float dt) { }

    static float SafeSeconds(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Max(0, value);

    /// <summary>Utility retained for historical records, never displayed as an active gameplay clock.</summary>
    public static string FormatTime(float seconds)
    {
        seconds = SafeSeconds(seconds);
        int min = (int)(seconds / 60f);
        int sec = (int)(seconds % 60f);
        return $"{min:D2}:{sec:D2}";
    }
}
