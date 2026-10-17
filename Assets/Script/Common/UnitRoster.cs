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
}
