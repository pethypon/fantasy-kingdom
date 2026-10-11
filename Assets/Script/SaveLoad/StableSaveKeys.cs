using System;
using System.Collections.Generic;

/// <summary>Frozen save keys: display/enum names may change, these literals may not. See Docs/Stable_ID_Policy.md.</summary>
public static class StableSaveKeys
{
    static class Table<T> where T : struct, Enum
    {
        internal static readonly Dictionary<T, string> ByValue = new Dictionary<T, string>();
        internal static readonly Dictionary<string, T> ByKey = new Dictionary<string, T>(StringComparer.Ordinal);
    }
    static StableSaveKeys()
    {
        Add(Kind.Crystal, "Crystal");
        Add(Kind.King, "King");
        Add(Kind.Knight, "Knight");
        Add(Kind.Archer, "Archer");
        Add(Kind.Magic, "Magic");
        Add(Kind.Assassin, "Assassin");
        Add(Kind.Scout, "Scout");
        Add(Kind.Priest, "Priest");
        Add(Kind.Guardian, "Guardian");
        Add(Kind.Crossbow, "Crossbow");
        Add(Kind.Magicsniper, "Magicsniper");
        Add(Kind.Bomber, "Bomber");
        Add(Kind.Boss, "Boss");
        Add(Kind.SubCrystal, "SubCrystal");
        Add(Kind.WoodWall, "WoodWall");
        Add(Kind.StoneWall, "StoneWall");
        Add(Kind.None, "None");
        Add(Team.Player, "Player");
        Add(Team.Enemy, "Enemy");
        Add(Team.Obstacle, "Obstacle");
        Add(Team.None, "None");
        Add(Team.Monster, "Monster");
        Add(Team.Intruder, "Intruder");
        Add(Type.Unit, "Unit");
        Add(Type.Building, "Building");
        Add(Type.Wall, "Wall");
        Add(Type.MovePoint, "MovePoint");
        Add(Type.AttackPoint, "AttackPoint");
        Add(Direction.N, "N");
        Add(Direction.S, "S");
        Add(PassiveSkill.None, "None");
        Add(PassiveSkill.Impregnable, "Impregnable");
        Add(PassiveSkill.HunterEyes, "HunterEyes");
        Add(PassiveSkill.Destroyer, "Destroyer");
        Add(PassiveSkill.Assassination, "Assassination");
        Add(PassiveSkill.Sniper, "Sniper");
        Add(PassiveSkill.StrangeKingAura, "StrangeKingAura");
        Add(FacilityKind.Field, "Field");
        Add(FacilityKind.Bakery, "Bakery");
        Add(FacilityKind.LoggingCamp, "LoggingCamp");
        Add(FacilityKind.Quarry, "Quarry");
        Add(FacilityKind.Mine, "Mine");
        Add(FacilityKind.Barracks, "Barracks");
        Add(FacilityKind.House, "House");
        Add(FacilityKind.LuxuryHouse, "LuxuryHouse");
        Add(FacilityKind.Well, "Well");
        Add(FacilityKind.Warehouse, "Warehouse");
        Add(FacilityKind.WoodWall, "WoodWall");
        Add(FacilityKind.StoneWall, "StoneWall");
        Add(FacilityKind.Mortar, "Mortar");
        Add(FacilityKind.Cannon, "Cannon");
        Add(FacilityKind.RestraintTrap, "RestraintTrap");
        Add(FacilityKind.SpikeTrap, "SpikeTrap");
        Add(FacilityKind.HeroSword, "HeroSword");
        Add(FacilityKind.SubCrystal, "SubCrystal");
        Add(SpecialAbility.None, "None");
        Add(SpecialAbility.SwiftPosture, "SwiftPosture");
        Add(SpecialAbility.InterceptPosture, "InterceptPosture");
        Add(SpecialAbility.PierceEnhance, "PierceEnhance");
        Add(SpecialAbility.SiegeAdapt, "SiegeAdapt");
        Add(SpecialAbility.WatchEye, "WatchEye");
        Add(SpecialAbility.Tenacity, "Tenacity");
        Add(SpecialAbility.PursuitInstinct, "PursuitInstinct");
        Add(SpecialAbility.WallFamiliar, "WallFamiliar");
        Add(SpecialAbility.FocusMaintain, "FocusMaintain");
        Add(SpecialAbility.FirstAid, "FirstAid");
        Add(SpecialAbility.PoisonCoat, "PoisonCoat");
        Add(SpecialAbility.FrostBlade, "FrostBlade");
        Add(SpecialAbility.Foresight, "Foresight");
        Add(SpecialAbility.PressureShot, "PressureShot");
        Add(SpecialAbility.Efficiency, "Efficiency");
        Add(SpecialAbility.Desperation, "Desperation");
        Add(SpecialAbility.IronWalkDefense, "IronWalkDefense");
        Add(SpecialAbility.HeightAdapt, "HeightAdapt");
        Add(SpecialAbility.LowHunt, "LowHunt");
        Add(SpecialAbility.MagicResist, "MagicResist");
        Add(SpecialAbility.Breach, "Breach");
        Add(SpecialAbility.VenomBlade, "VenomBlade");
        Add(SpecialAbility.FrostPressure, "FrostPressure");
        Add(SpecialAbility.SupportSpread, "SupportSpread");
        Add(SpecialAbility.SurvivalInstinct, "SurvivalInstinct");
        Add(SpecialAbility.ShadowCross, "ShadowCross");
        Add(SpecialAbility.ArtilleryControl, "ArtilleryControl");
        Add(SpecialAbility.GuardianZone, "GuardianZone");
        Add(SpecialAbility.SniperCorrection, "SniperCorrection");
        Add(SpecialAbility.HolyReaction, "HolyReaction");
        Add(SpecialAbility.BattlefieldDomination, "BattlefieldDomination");
        Add(SpecialAbility.IndomitableWill, "IndomitableWill");
        Add(SpecialAbility.ThunderChain, "ThunderChain");
        Add(SpecialAbility.IcePrison, "IcePrison");
        Add(SpecialAbility.PurifyHalo, "PurifyHalo");
        Add(StatusEffectType.None, "None");
        Add(StatusEffectType.Stun, "Stun");
        Add(StatusEffectType.Mark, "Mark");
        Add(StatusEffectType.ArmorBreak, "ArmorBreak");
        Add(StatusEffectType.Weaken, "Weaken");
        Add(StatusEffectType.Poison, "Poison");
        Add(StatusEffectType.Chill, "Chill");
        Add(StatusEffectType.Freeze, "Freeze");
        Add(StatusEffectType.Seal, "Seal");
        Add(StatusEffectType.Blind, "Blind");
        Add(StatusEffectType.NarrowVision, "NarrowVision");
        Add(BuffType.None, "None");
        Add(BuffType.Offensive, "Offensive");
        Add(BuffType.Defensive, "Defensive");
        Add(BuffType.Haste, "Haste");
        Add(BuffType.Insight, "Insight");
        Add(BuffType.Barrier, "Barrier");
        Add(BuffType.Reflect, "Reflect");
        Add(MajorPersonality.Combat, "Combat");
        Add(MajorPersonality.Intellect, "Intellect");
        Add(MajorPersonality.Adaptive, "Adaptive");
        Add(MajorPersonality.Growth, "Growth");
        Add(TurnStrategy.Assault, "Assault");
        Add(TurnStrategy.CrystalDefense, "CrystalDefense");
        Add(TurnStrategy.RetreatRegroup, "RetreatRegroup");
        Add(TurnStrategy.EconomyBuild, "EconomyBuild");
        Add(TurnStrategy.Balanced, "Balanced");
        Add(TurnStrategy.ScoutSearch, "ScoutSearch");
        Add(TurnStrategy.ContactEngage, "ContactEngage");
    }
    static void Add<T>(T value, string key) where T : struct, Enum
    {
        Table<T>.ByValue.Add(value, key);
        Table<T>.ByKey.Add(key, value);
    }
    public static string Key<T>(T value) where T : struct, Enum
        => Table<T>.ByValue.TryGetValue(value, out string key) ? key : throw new ArgumentOutOfRangeException(nameof(value), "Missing stable save key");
    public static bool TryParse<T>(string key, out T value) where T : struct, Enum
    {
        value = default;
        return key != null && Table<T>.ByKey.TryGetValue(key, out value);
    }
    public static bool IsKnown<T>(string key) where T : struct, Enum => TryParse<T>(key, out _);
}
