/// <summary>Actual outcome of one committed HP event; requested damage is not HP loss.</summary>
public enum HpChangeCause { Unknown, NormalAttack, Skill, Poison, Smoke, Geyser, CrossbowLightning, Sacrifice, SelfRecoil, Bomb, Explosion, Redirected, Counter }

public readonly struct HpChangeResult
{
    public readonly int RequestedAmount, ActualHpLoss, ActualHeal;
    public readonly Status Source, Target;
    public readonly HpChangeKind Kind;
    public readonly HpChangeCause Cause;
    public readonly bool WasCompletelyNegated;
    public HpChangeResult(Status source, Status target, int requested, int loss, int heal, HpChangeKind kind, HpChangeCause cause = HpChangeCause.Unknown)
    {
        Source = source; Target = target; RequestedAmount = requested; ActualHpLoss = loss;
        ActualHeal = heal; Kind = kind; Cause = cause; WasCompletelyNegated = requested > 0 && loss == 0 && heal == 0;
    }
}
