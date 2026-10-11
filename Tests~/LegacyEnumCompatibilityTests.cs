#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

/// <summary>Old numeric AI/asset serialization must not change when new enums are appended.</summary>
public static class LegacyEnumCompatibilityTests
{
    [Serializable] sealed class Fixture { public string BaselineCommit; public EnumRow[] Enums; }
    [Serializable] sealed class EnumRow { public string TypeName; public Member[] Members; }
    [Serializable] sealed class Member { public string Name; public int Value; }
    public static void RunAll()
    {
        string file = Path.Combine(Application.dataPath, "../Tests~/Data/Compatibility/LegacyEnumValues.json");
        var fixture = JsonUtility.FromJson<Fixture>(File.ReadAllText(file));
        int checks = 0;
        foreach (var row in fixture.Enums)
        {
            var type = typeof(Status).Assembly.GetType(row.TypeName);
            if (type == null || !type.IsEnum) throw new InvalidOperationException("Missing legacy enum: " + row.TypeName);
            foreach (var member in row.Members)
            {
                if (!Enum.IsDefined(type, member.Name) || Convert.ToInt32(Enum.Parse(type, member.Name)) != member.Value)
                    throw new InvalidOperationException("Legacy numeric value changed: " + row.TypeName + "." + member.Name);
                checks++;
            }
        }
        RoundTrip<Kind>(fixture); RoundTrip<Team>(fixture); RoundTrip<global::Type>(fixture); RoundTrip<Direction>(fixture);
        RoundTrip<FacilityKind>(fixture); RoundTrip<PassiveSkill>(fixture); RoundTrip<SpecialAbility>(fixture);
        RoundTrip<StatusEffectType>(fixture); RoundTrip<BuffType>(fixture); RoundTrip<MajorPersonality>(fixture); RoundTrip<TurnStrategy>(fixture);
        if (StableSaveKeys.TryParse<Kind>("2", out _) || StableSaveKeys.TryParse<Kind>("新しい表示名", out _))
            throw new InvalidOperationException("Save keys must not depend on UI labels or numeric indices");
        Debug.Log("[LegacyEnumCompatibility] " + checks + " frozen numeric values and 11 stable key tables passed");
    }
    static void RoundTrip<T>(Fixture fixture) where T : struct, Enum
    {
        foreach (var row in fixture.Enums)
            if (row.TypeName == typeof(T).Name)
                foreach (var legacy in row.Members)
                    if (StableSaveKeys.Key((T)Enum.ToObject(typeof(T), legacy.Value)) != legacy.Name)
                        throw new InvalidOperationException("Published save key changed: " + row.TypeName + " / " + legacy.Name);
        foreach (T value in Enum.GetValues(typeof(T)))
        {
            string key = StableSaveKeys.Key(value);
            // These literals are the published legacy save keys, deliberately retained.
            if (!StableSaveKeys.TryParse<T>(key, out var restored) || !value.Equals(restored))
                throw new InvalidOperationException("Stable key migration missing: " + typeof(T).Name + " / " + value);
        }
    }
}
#endif
