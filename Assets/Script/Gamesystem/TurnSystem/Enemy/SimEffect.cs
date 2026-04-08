// =====================================================================
//  SimEffect — シミュレーション上のステータス効果
// =====================================================================
public struct SimEffect
{
    public StatusEffectType Debuff;
    public BuffType Buff;
    public int RemainingTurns;

    public bool IsDebuff => Debuff != StatusEffectType.None;
    public bool IsBuff => Buff != BuffType.None;

    public SimEffect(StatusEffectType debuff, int turns)
    {
        Debuff = debuff;
        Buff = BuffType.None;
        RemainingTurns = turns;
    }

    public SimEffect(BuffType buff, int turns)
    {
        Debuff = StatusEffectType.None;
        Buff = buff;
        RemainingTurns = turns;
    }
}
