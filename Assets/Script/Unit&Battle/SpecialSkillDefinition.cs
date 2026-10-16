using UnityEngine;

// Independent of the serialized Kind/Skill enums. Never reorder existing values.
public enum UnitSpecialSkill
{
    None, KnightBreakthrough, ArcherStationary, MagicRitual, AssassinVisionSeal,
    ScoutVisionGuard, PriestPurification, GuardianLifeLink, CrossbowStakes,
    MagicSniperLock, LifeBomb, WhiteMageHeal, HolyKnightSanctuary,
    SamuraiCounter, NinjaSwap, BerserkerRage, NavigatorPull
}
public enum BombDeathPolicy { Unconfigured, Cancel, Explode }
[System.Flags] public enum SpecialFactionMask { Unconfigured = 0, Hostile = 1, Allies = 2, Holder = 4 }

/// <summary>One definition shared by gameplay, UI and AI. -1/empty means not decided, never free.</summary>
[CreateAssetMenu(menuName = "Fantasy Kingdom/駒と建物/駒固有能力")]
public sealed class SpecialSkillDefinition : ScriptableObject
{
    public UnitSpecialSkill ability;
    public string displayName;
    public bool enabledForPlay = true;
    [Min(-1)] public int apCost = -1;
    [Min(-1)] public int cooldown = -1;
    public int[] stageAPCosts = { 5, 8, 11, 15, 20 };
    [Tooltip("未確定。0以上の値を設定するまで弱体は発生しません。")]
    [Range(-1, 1)] public float knightVisibleEnemyAtkPenalty = -1;
    public AnimationCurve stationaryAttackCurve = new AnimationCurve();
    [Min(-1)] public int sacrificeLimit = -1;
    public AnimationCurve sniperDamageCurve = AnimationCurve.Linear(0, 1, 25, 2.5f);
    public AnimationCurve sniperRecoilCurve = new AnimationCurve();
    [Min(-1)] public int bombExplosionRadius = -1;
    public SpecialFactionMask bombFactionMask;
    public BombDeathPolicy bombOnHolderDeathPolicy;
    public SpecialFactionMask lightningFactionMask;
    public Vector2Int[] holyKnightArea = new Vector2Int[0];
    public bool holyKnightInterruptionConfigured;
    public bool holyKnightInterruptOnDamage;
    [Min(-1)] public int samuraiGaugeGain = -1;
    [Min(-1)] public int samuraiGaugeMax = -1;
    // Serialized for existing assets only. Ninja swaps now cover the entire map.
    [HideInInspector] public int ninjaSwapRange = -1;
    public AnimationCurve ninjaChargeCurve = new AnimationCurve();
    public AnimationCurve berserkerAttackCurve = new AnimationCurve();
    public AnimationCurve berserkerDefenceCurve = new AnimationCurve();
    public AnimationCurve berserkerAttackAPCurve = new AnimationCurve();
    public AnimationCurve navigatorAPDamageCurve = new AnimationCurve();

    public const float ArcherMaximum = 1.75f, SniperMaximum = 2.5f;
    public const float KnightBlindSpotMultiplier = 2;
    public const float SniperMaximumRecoil = .15f, RitualSacrifice = .15f, BombFraction = .8f;
    public const int SniperMaximumDistance = 25, BombDelay = 5, StakeLifetime = 3;
    public const float WhiteMageHealFraction = .3f, HolyKnightHealFraction = .1f;
    public const float SamuraiCounterFraction = .5f, BerserkerMaximum = 2.5f, NavigatorMaximum = 1.5f;

    public bool IsPassive => ability == UnitSpecialSkill.ArcherStationary || ability == UnitSpecialSkill.SamuraiCounter;
    public void ApplySpecifiedAPCost()
    {
        switch (ability)
        {
            case UnitSpecialSkill.KnightBreakthrough: apCost = 14; break;
            case UnitSpecialSkill.ArcherStationary: case UnitSpecialSkill.SamuraiCounter: apCost = 0; break;
            case UnitSpecialSkill.MagicRitual: apCost = 22; break;
            case UnitSpecialSkill.AssassinVisionSeal: apCost = 14; break;
            case UnitSpecialSkill.ScoutVisionGuard: apCost = 20; break;
            case UnitSpecialSkill.PriestPurification: apCost = 8; break;
            case UnitSpecialSkill.GuardianLifeLink: apCost = 18; break;
            case UnitSpecialSkill.CrossbowStakes: apCost = 10; break;
            case UnitSpecialSkill.MagicSniperLock: apCost = 18; break;
            case UnitSpecialSkill.LifeBomb: apCost = 20; break;
            case UnitSpecialSkill.WhiteMageHeal: apCost = 12; break;
            case UnitSpecialSkill.HolyKnightSanctuary: apCost = 15; break;
            case UnitSpecialSkill.NinjaSwap: apCost = 13; break;
            case UnitSpecialSkill.BerserkerRage: apCost = 5; stageAPCosts = new[] { 5, 8, 11, 15, 20 }; break;
            case UnitSpecialSkill.NavigatorPull: apCost = 12; break;
            default: apCost = -1; break;
        }
    }
    public bool HasCurve(AnimationCurve curve) => curve != null && curve.length > 0;
    public string ConfigurationError
    {
        get
        {
            if (!enabledForPlay || ability == UnitSpecialSkill.None) return "駒固有能力が無効です";
            if (!IsPassive && (apCost < 0 || cooldown < 0)) return "AP・クールダウンが未設定です";
            if (!IsPassive && apCost > APSystem.MaximumSkillAP) return $"スキルAPは{APSystem.MaximumSkillAP}以下に設定してください";
            if (ability == UnitSpecialSkill.ArcherStationary && !HasCurve(stationaryAttackCurve)) return "未移動強化の中間倍率が未設定です";
            if (ability == UnitSpecialSkill.MagicSniperLock && !HasCurve(sniperRecoilCurve)) return "距離ごとの反動が未設定です";
            if (ability == UnitSpecialSkill.MagicRitual && sacrificeLimit < 1) return "供儀人数上限が未設定です";
            if (ability == UnitSpecialSkill.CrossbowStakes && lightningFactionMask == 0) return "雷線の陣営判定が未設定です";
            if (ability == UnitSpecialSkill.LifeBomb && (bombExplosionRadius < 0 || bombFactionMask == 0 || bombOnHolderDeathPolicy == BombDeathPolicy.Unconfigured)) return "爆発範囲・陣営・死亡時処理が未設定です";
            if (ability == UnitSpecialSkill.HolyKnightSanctuary && (holyKnightArea == null || holyKnightArea.Length == 0 || !holyKnightInterruptionConfigured)) return "回復範囲・中断条件が未設定です";
            if (ability == UnitSpecialSkill.SamuraiCounter && (samuraiGaugeGain < 0 || samuraiGaugeMax < 1)) return "刀ゲージが未設定です";
            if (ability == UnitSpecialSkill.NinjaSwap && !HasCurve(ninjaChargeCurve)) return "クナイの強化倍率が未設定です";
            if (ability == UnitSpecialSkill.NavigatorPull && !HasCurve(navigatorAPDamageCurve)) return "残りAPと火力の関係が未設定です";
            if (ability == UnitSpecialSkill.BerserkerRage && (!HasCurve(berserkerAttackCurve) || !HasCurve(berserkerDefenceCurve) || !HasCurve(berserkerAttackAPCurve))) return "狂戦士の段階・APが未設定です";
            if (ability == UnitSpecialSkill.BerserkerRage && (stageAPCosts == null || stageAPCosts.Length != 5 || System.Array.Exists(stageAPCosts, cost => cost < 0))) return "狂戦士の次段階スキルAPが未設定です";
            if (ability == UnitSpecialSkill.BerserkerRage && System.Array.Exists(stageAPCosts, cost => cost > APSystem.MaximumSkillAP)) return $"各段階のスキルAPは{APSystem.MaximumSkillAP}以下に設定してください";
            return null;
        }
    }
    public float ArcherMultiplier(int turns) => HasCurve(stationaryAttackCurve) ? Mathf.Clamp(stationaryAttackCurve.Evaluate(Mathf.Max(0, turns)), 1, ArcherMaximum) : 1;
    public static float StakeMultiplier(int age) => age >= 3 ? 2 : age >= 2 ? 1.5f : 1.25f;
    public float SniperMultiplier(float distance) => Mathf.Clamp(sniperDamageCurve != null && sniperDamageCurve.length > 0 ? sniperDamageCurve.Evaluate(Mathf.Clamp(distance, 0, 25)) : 1, 1, SniperMaximum);
    public float SniperRecoil(float distance) => distance >= SniperMaximumDistance ? SniperMaximumRecoil : HasCurve(sniperRecoilCurve) ? Mathf.Clamp(sniperRecoilCurve.Evaluate(Mathf.Max(0, distance)), 0, SniperMaximumRecoil) : 0;
    void OnValidate() => GameContentRevision.NotifyChanged();
}
