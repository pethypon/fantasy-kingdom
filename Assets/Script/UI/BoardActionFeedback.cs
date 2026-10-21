using UnityEngine;

/// <summary>Event-only feedback. All world effects and sounds share this observation/suppression gate.</summary>
public static class BoardActionFeedback
{
    struct AttackStamp { public int Actor, Frame; }
    static readonly AttackStamp[] recentAttacks = new AttackStamp[32];
    static int nextAttack;
    static VisionGenerator vision;
    public static long EmissionCount { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    { vision = null; EmissionCount = 0; nextAttack = 0; System.Array.Clear(recentAttacks, 0, recentAttacks.Length); }

    static bool CanPresent(Status actor, out BoardFeedbackSettings settings)
    {
        settings = null;
        if (!Application.isPlaying || BoardActorAnimation.EffectsSuppressed || actor == null || !actor.IsAlive) return false;
        if (vision == null) vision = actor.RuntimeSystems?.VisionGenerator;
        if (vision == null || !vision.IsPresentationCellVisible(actor.transform.position)) return false;
        if (actor.team != Team.Player && !vision.IsActorVisibleTo(Team.Player, actor)) return false;
        settings = BoardFeedbackSettings.Active;
        return settings != null && (settings.effectsEnabled || settings.soundsEnabled);
    }
    static Color ColorFor(Status actor)
        => actor.isWildBoss ? new Color(1, .34f, .38f, .85f)
            : actor.team == Team.Player ? new Color(.60f, .85f, 1, .85f) : new Color(1, .61f, .35f, .85f);
    static void Emit(BoardFeedbackKind kind, Status actor, Vector3 point, Vector3 direction, Color color, BoardFeedbackSettings settings)
    {
        EmissionCount++;
        if (settings.effectsEnabled) BoardCombatEffects.Emit(kind, point, direction, color, settings);
        if (settings.soundsEnabled) BoardSoundEffects.Play(kind, settings);
    }
    public static void Move(Status actor, Vector3 from)
    {
        if (CanPresent(actor, out var settings)) Emit(BoardFeedbackKind.Move, actor, actor.transform.position, Vector3.zero, ColorFor(actor), settings);
    }
    public static void Attack(Status actor, Vector3 target)
    {
        if (!CanPresent(actor, out var settings)) return;
        int id = actor.GetInstanceID(), frame = Time.frameCount;
        for (int i = 0; i < recentAttacks.Length; i++)
            if (recentAttacks[i].Actor == id && recentAttacks[i].Frame == frame) return;
        recentAttacks[nextAttack] = new AttackStamp { Actor = id, Frame = frame }; nextAttack = (nextAttack + 1) % recentAttacks.Length;
        var kind = actor.kind == Kind.Magic || actor.kind == Kind.Magicsniper ? BoardFeedbackKind.Magic
            : actor.kind == Kind.Archer || actor.kind == Kind.Crossbow || actor.kind == Kind.Ninja ? BoardFeedbackKind.Projectile : BoardFeedbackKind.Attack;
        Vector3 direction = target - actor.transform.position;
        bool observed = true;
        for (int i = 0; i <= 8; i++) if (!vision.IsPresentationCellVisible(Vector3.Lerp(actor.transform.position, target, i / 8f))) { observed = false; break; }
        if (!observed) { direction = actor.direction == Direction.N ? Vector3.forward : Vector3.back; kind = BoardFeedbackKind.Attack; }
        Emit(kind, actor, actor.transform.position, direction, ColorFor(actor), settings);
    }
    public static void Hit(Status actor)
    {
        if (CanPresent(actor, out var settings)) Emit(BoardFeedbackKind.Hit, actor, actor.transform.position, Vector3.zero, new Color(1, .80f, .54f, .85f), settings);
    }
    public static void Heal(Status actor)
    {
        if (CanPresent(actor, out var settings)) Emit(BoardFeedbackKind.Heal, actor, actor.transform.position, Vector3.zero, new Color(.42f, 1, .70f, .85f), settings);
    }
    public static void Shield(Status actor)
    {
        if (CanPresent(actor, out var settings)) Emit(BoardFeedbackKind.Shield, actor, actor.transform.position, Vector3.zero, new Color(.56f, .81f, 1, .85f), settings);
    }
    public static void Skill(Status actor)
    {
        if (!CanPresent(actor, out var settings)) return;
        int id = actor.GetInstanceID();
        for (int i = 0; i < recentAttacks.Length; i++) if (recentAttacks[i].Actor == id && recentAttacks[i].Frame == Time.frameCount) return;
        Emit(BoardFeedbackKind.Skill, actor, actor.transform.position, Vector3.zero, ColorFor(actor), settings);
    }
    public static void Summon(Status actor)
    {
        if (CanPresent(actor, out var settings)) Emit(BoardFeedbackKind.Summon, actor, actor.transform.position, Vector3.zero, ColorFor(actor), settings);
    }
    public static void Build(Status actor)
    {
        if (CanPresent(actor, out var settings)) Emit(BoardFeedbackKind.Build, actor, actor.transform.position, Vector3.zero, new Color(.95f, .77f, .43f, .85f), settings);
    }
    public static void UI()
    {
        if (!Application.isPlaying || BoardActorAnimation.EffectsSuppressed) return;
        var settings = BoardFeedbackSettings.Active;
        if (settings != null && settings.soundsEnabled) BoardSoundEffects.Play(BoardFeedbackKind.UiClick, settings);
    }
    public static void Result(GameResult result)
    {
        if (!Application.isPlaying || BoardActorAnimation.EffectsSuppressed) return;
        bool won = result == GameResult.Win || result == GameResult.TimeUpWin;
        if (!won && result != GameResult.Lose && result != GameResult.TimeUpLose) return;
        var settings = BoardFeedbackSettings.Active;
        if (settings != null && settings.soundsEnabled)
            BoardSoundEffects.Play(won ? BoardFeedbackKind.Victory : BoardFeedbackKind.Defeat, settings);
    }
}
