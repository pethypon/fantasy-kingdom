using System;

/// <summary>Evidence retained only when an old unit cannot be converted without guessing.</summary>
[Serializable]
public sealed class LegacyUnitStatSnapshot
{
    public int Level, Experience, HP, MaximumHP, ATK, DEF;
    public bool HasDefinitionBaseline;
    public int DefinitionMaxHP, DefinitionATK, DefinitionDEF;
    public LegacyUnitStatSnapshot Copy() => (LegacyUnitStatSnapshot)MemberwiseClone();
}

/// <summary>R2 production-unit rules. Authored bases stay unscaled on disk.</summary>
public static class FixedUnitRules
{
    public const int UpkeepFactor = 5;
    public static int ScaleBaseStat(int value)
        => value <= 0 ? 0 : (int)Math.Min(int.MaxValue, ((long)value * 7 + 2) / 4);
    public static bool UsesFixedStats(Kind kind) => kind != Kind.King && kind != Kind.Boss
        && kind != Kind.Crystal && kind != Kind.SubCrystal && kind != Kind.WoodWall && kind != Kind.StoneWall;
    public static int BaseStat(Kind kind, int value) => UsesFixedStats(kind) ? ScaleBaseStat(value) : Math.Max(0, value);
    public static int MultiplyUpkeep(int value, int extra = 0)
        => (int)Math.Min(int.MaxValue, ((long)Math.Max(0, value) + Math.Max(0, extra)) * UpkeepFactor);

    // Read-only old-save interpretation; never used to generate a new unit.
    public static int LegacyGrowth(int value, float growth)
    {
        if (float.IsNaN(growth) || float.IsInfinity(growth) || growth < 0) growth = 0;
        double gain = (float)(value * growth);
        return gain >= int.MaxValue ? int.MaxValue : Math.Max(2, (int)Math.Floor(gain + .5));
    }
    public static int LegacyStat(int value, float growth, int level)
        => (int)Math.Max(0, Math.Min(int.MaxValue, (long)value
            + (long)LegacyGrowth(value, growth) * (Math.Max(1, Math.Min(100, level)) - 1)));
}
