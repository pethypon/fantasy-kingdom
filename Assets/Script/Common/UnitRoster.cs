/// <summary>Stable production recruitment order shared by UI, live AI and search fallbacks.</summary>
public static class UnitRoster
{
    public static readonly Kind[] SummonableKinds =
    {
        Kind.Knight, Kind.Archer, Kind.Magic, Kind.Assassin,
        Kind.Scout, Kind.Priest, Kind.Guardian, Kind.Crossbow,
        Kind.Magicsniper, Kind.Bomber, Kind.WhiteMage, Kind.HolyKnight,
        Kind.Samurai, Kind.Ninja, Kind.Berserker, Kind.Navigator,
    };

    /// <summary>Reuse the inherited role's existing valuation until separate R1 tuning is authored.
    /// Recruitment, abilities and action masks must still resolve the actual Kind's UnitData.</summary>
    public static Kind EvaluationBaseKind(Kind kind)
    {
        switch (kind)
        {
            case Kind.WhiteMage: return Kind.Priest;
            case Kind.HolyKnight:
            case Kind.Samurai:
            case Kind.Berserker: return Kind.Knight;
            case Kind.Ninja: return Kind.Assassin;
            case Kind.Navigator: return Kind.Guardian;
            default: return kind;
        }
    }

    public static bool IsDedicatedHealer(Kind kind) => kind == Kind.Priest || kind == Kind.WhiteMage;
}
