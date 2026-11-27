using System;
using UnityEngine;

public enum CampaignRewardKind { Soul, Attack, Healing, Wood, Stone, Water, Bread, Iron, MagicOre, Wheat }

[Serializable]
public sealed class CampaignRewardSettings
{
    [InspectorName("初期の魂"), Range(0, 99)] public int initialSouls = 3;
    [InspectorName("標準召喚費（魂）"), Range(1, 20)] public int summonCost = 3;
    [InspectorName("敵の駒を撃破した時の魂"), Range(0, 20)] public int defeatSouls = 1;
    [InspectorName("魂の獲得数"), Range(1, 99)] public int soulReward = 2;
    [InspectorName("魂の保有上限"), Range(1, 999)] public int soulCap = 12;
    [InspectorName("資源の獲得量"), Range(1, 999)] public int resourceReward = 30;
    [InspectorName("攻撃強化（％）"), Range(1, 15)] public int attackPercent = 3;
    [InspectorName("治癒強化（％）"), Range(1, 15)] public int healingPercent = 5;
    [InspectorName("強化の持続（自軍ターン）"), Range(1, 100)] public int duration = 10;
    [InspectorName("同じ強化の重複上限"), Range(1, 5)] public int stacks = 3;
    [InspectorName("魂・強化・資源から各1候補")] public bool oneOfEach = true;

    public bool Valid => initialSouls >= 0 && initialSouls <= 99 && summonCost >= 1 && summonCost <= 20
        && defeatSouls >= 0 && defeatSouls <= 20 && soulReward >= 1 && soulReward <= 99 && soulCap >= 1 && soulCap <= 999
        && resourceReward >= 1 && resourceReward <= 999 && attackPercent >= 1 && attackPercent <= 15
        && healingPercent >= 1 && healingPercent <= 15 && duration >= 1 && duration <= 100 && stacks >= 1 && stacks <= 5;
}

[Serializable]
public sealed class CampaignRewardAccount
{
    public int souls, lastOfferRound, attackStacks, healStacks, attackUntil, healUntil;
    public CampaignRewardKind[] pending = Array.Empty<CampaignRewardKind>();

    internal bool Valid(CampaignRewardSettings settings, int round)
    {
        if (souls < 0 || souls > settings.soulCap || lastOfferRound < 0 || lastOfferRound > round ||
            lastOfferRound % 5 != 0 || attackStacks < 0 || attackStacks > settings.stacks || healStacks < 0 ||
            healStacks > settings.stacks || attackUntil < 0 || healUntil < 0 || pending == null) return false;
        if (pending.Length == 0) return true;
        if (pending.Length != 3 || lastOfferRound == 0 || lastOfferRound != round) return false;
        for (int i = 0; i < pending.Length; i++)
        {
            if ((uint)pending[i] > (uint)CampaignRewardKind.Wheat) return false;
            for (int j = 0; j < i; j++) if (pending[j] == pending[i]) return false;
        }
        return true;
    }
}

// Entirely match-owned. The offer, choices and tuning travel with the save.
[Serializable]
public sealed class CampaignRewards
{
    public CampaignRewardSettings settings;
    public CampaignRewardAccount player, enemy;

    public static CampaignRewards Start()
    {
        var source = GameAuthoringRules.Active?.campaignRewards;
        var rules = source != null && source.Valid
            ? JsonUtility.FromJson<CampaignRewardSettings>(JsonUtility.ToJson(source)) : new CampaignRewardSettings();
        int starting = Math.Min(rules.initialSouls, rules.soulCap);
        return new CampaignRewards { settings = rules, player = new CampaignRewardAccount { souls = starting },
            enemy = new CampaignRewardAccount { souls = starting } };
    }

    public bool Valid(int round) => settings != null && settings.Valid && player != null && enemy != null
        && player.Valid(settings, round) && enemy.Valid(settings, round);

    public CampaignRewardAccount Account(Team team) => team == Team.Player ? player : team == Team.Enemy ? enemy : null;
    public int Cost(UnitData data) => RecruitmentCost(data == null ? Kind.None : data.kind, data?.soulCostOverride ?? 0, settings.summonCost);

    public static int RecruitmentCost(Kind kind, int authoredCost, int standardCost)
    {
        int cost = authoredCost > 0 ? authoredCost : standardCost;
        bool established = kind >= Kind.Knight && kind <= Kind.Bomber || kind >= Kind.WhiteMage && kind <= Kind.DragonKnight;
        return established ? Math.Max(3, cost) : cost;
    }

    public void RewardDefeat(Status source, Status victim)
    {
        if (source == null || victim == null || source.team == victim.team || victim.IsPrimaryCommander
            || victim.type != Type.Unit || victim.kind == Kind.Crystal || victim.kind == Kind.SubCrystal
            || Account(victim.team) == null) return;
        var side = Account(source.team);
        if (side != null) side.souls = Math.Min(settings.soulCap, side.souls + settings.defeatSouls);
    }

    public bool Prepare(Team team, int round, int seed)
    {
        var side = Account(team);
        if (side == null || round < 1) return false;
        if (round >= side.attackUntil) side.attackStacks = 0;
        if (round >= side.healUntil) side.healStacks = 0;
        if (round % 5 != 0 || round < side.lastOfferRound) return false;
        if (side.lastOfferRound == round) return side.pending.Length == 3;
        var random = new System.Random(unchecked(seed ^ (round * 7331) ^ ((int)team * 17891)));
        side.lastOfferRound = round;
        if (settings.oneOfEach)
            side.pending = new[] { CampaignRewardKind.Soul, (CampaignRewardKind)random.Next(1,3), (CampaignRewardKind)random.Next(3,10) };
        else
        {
            var pool = (CampaignRewardKind[])Enum.GetValues(typeof(CampaignRewardKind));
            side.pending = new CampaignRewardKind[3];
            for (int i = 0; i < 3; i++)
            {
                int j = random.Next(i, pool.Length);
                side.pending[i] = pool[j];
                pool[j] = pool[i];
            }
        }
        return true;
    }

    public bool Claim(FactionState factions, Team team, int round, int index)
    {
        var side = Account(team);
        if (side == null || factions == null || side.lastOfferRound != round || (uint)index >= side.pending.Length) return false;
        var kind = side.pending[index];
        int expiry = (int)Math.Min(int.MaxValue, (long)round + settings.duration);
        switch (kind)
        {
            case CampaignRewardKind.Soul: side.souls = Math.Min(settings.soulCap, side.souls + settings.soulReward); break;
            case CampaignRewardKind.Attack: side.attackStacks = Math.Min(settings.stacks, side.attackStacks + 1); side.attackUntil = expiry; break;
            case CampaignRewardKind.Healing: side.healStacks = Math.Min(settings.stacks, side.healStacks + 1); side.healUntil = expiry; break;
            default:
                var nation = factions.GetNation(team);
                ref int stock = ref Stock(nation.Resources, kind);
                int cap = EconomySystem.IsResourceCapacityEnforced(nation.ResourceCapacity) ? factions.GetResourceCap(team) : int.MaxValue;
                stock = Math.Max(stock, (int)Math.Min(cap, (long)stock + settings.resourceReward));
                break;
        }
        side.pending = Array.Empty<CampaignRewardKind>();
        return true;
    }

    static ref int Stock(FactionState.ResourceData resources, CampaignRewardKind kind)
    {
        switch (kind)
        {
            case CampaignRewardKind.Wood: return ref resources.Wood;
            case CampaignRewardKind.Stone: return ref resources.Stone;
            case CampaignRewardKind.Water: return ref resources.Water;
            case CampaignRewardKind.Bread: return ref resources.Bread;
            case CampaignRewardKind.Iron: return ref resources.Iron;
            case CampaignRewardKind.MagicOre: return ref resources.MagicOre;
            case CampaignRewardKind.Wheat: return ref resources.Wheat;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    public int Choose(GameSystems systems, Team team)
    {
        var side = Account(team);
        var parent = team == Team.Player ? systems.UnitSetting.PlayerUnit : systems.UnitSetting.EnemyUnit;
        int troops = 0, wounded = 0;
        foreach (var unit in parent.GetComponentsInChildren<Status>())
        {
            if (!unit.IsAlive || unit.type != Type.Unit) continue;
            troops++;
            if (unit.HPRatio < .75f) wounded++;
        }
        var resources = systems.FactionState.GetResources(team);
        int best = 0;
        float bestValue = float.MinValue;
        for (int i = 0; i < side.pending.Length; i++)
        {
            var kind = side.pending[i];
            float value;
            if (kind == CampaignRewardKind.Soul)
                value = side.souls >= settings.soulCap ? -1 : (troops < 5 ? 95 : 64) - 12 * side.souls;
            else if (kind == CampaignRewardKind.Attack)
                value = 30 + Mathf.Min(35, 5 * troops) - 6 * side.attackStacks;
            else if (kind == CampaignRewardKind.Healing)
                value = 25 + Mathf.Min(55, 14 * wounded) - 6 * side.healStacks;
            else
                value = 78 - Mathf.Min(65, Stock(resources, kind));
            if (value > bestValue) { bestValue = value; best = i; }
        }
        return best;
    }

    public string Label(CampaignRewardKind kind)
    {
        switch (kind)
        {
            case CampaignRewardKind.Soul: return "召喚の魂　+" + settings.soulReward;
            case CampaignRewardKind.Attack: return $"攻撃力　+{settings.attackPercent}%　（{settings.duration}ターン）";
            case CampaignRewardKind.Healing: return $"受ける回復量　+{settings.healingPercent}%　（{settings.duration}ターン）";
            default: return ResourceNames[(int)kind - 3] + "　+" + settings.resourceReward;
        }
    }
    static readonly string[] ResourceNames = { "木材", "石材", "水", "パン", "鉄", "魔石", "小麦" };

    // A forecast can copy its own faction effects and tick them without accessing live state.
    public struct ArmyBonus
    {
        public int attackPercent, healPercent, attackTurns, healTurns;
        public void NextOwnerTurn()
        {
            if (attackTurns > 0 && --attackTurns == 0) attackPercent = 0;
            if (healTurns > 0 && --healTurns == 0) healPercent = 0;
        }
    }
    public ArmyBonus Forecast(Team team, int round)
    {
        var side = Account(team);
        if (side == null) return default;
        return new ArmyBonus
        {
            attackPercent = side.attackStacks * settings.attackPercent, healPercent = side.healStacks * settings.healingPercent,
            attackTurns = Math.Max(0, side.attackUntil - round), healTurns = Math.Max(0, side.healUntil - round)
        };
    }

    public static int ApplyHealBonus(int amount, float multiplier)
        => (int)Math.Min(int.MaxValue, Math.Round(Math.Max(0, amount) * (double)multiplier, MidpointRounding.AwayFromZero));

    public static float UnitBonus(Status actor, bool healing)
    {
        if (actor == null || actor.type != Type.Unit || actor.kind == Kind.Crystal) return 1f;
        var rewards = actor.RuntimeSystems?.FactionState?.Rewards;
        var side = rewards?.Account(actor.team);
        if (side == null) return 1f;
        return 1f + .01f * (healing ? side.healStacks * rewards.settings.healingPercent : side.attackStacks * rewards.settings.attackPercent);
    }
}
