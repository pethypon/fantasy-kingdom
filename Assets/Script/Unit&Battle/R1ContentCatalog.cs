using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>R1の未確定コンテンツを、ルール実装から分離して設定する。</summary>
[CreateAssetMenu(menuName = "Fantasy Kingdom/R1 Content Catalog")]
public class R1ContentCatalog : ScriptableObject
{
    [Serializable] public class FixedSkill { public Kind Kind; public int SkillId = -1; }
    [Serializable] public class UniqueReward
    {
        public string Id;
        public string DisplayName;
        [TextArea] public string Description;
        public WildBossArchetype Boss;
        public int AttackBonus, DefenseBonus, CrystalHPBonus;
    }
    [Serializable] public class Encounter
    {
        public string Id;
        public GameObject Prefab;
        public UnitData Stats;
        [Min(1)] public int Count = 1;
        [Min(1)] public int FirstRound = 1;
        [Min(1)] public int VisionRange = 3;
        public UniqueReward Relic;
        public GameObject[] Retinue = new GameObject[0];
    }

    public List<FixedSkill> FixedSkills = new List<FixedSkill>();
    public List<UniqueReward> BossArtifacts = new List<UniqueReward>();
    public List<Encounter> Monsters = new List<Encounter>();
    public List<Encounter> Intruders = new List<Encounter>();

    public static R1ContentCatalog Load() => Resources.Load<R1ContentCatalog>("R1ContentCatalog");

    public void ApplyFixedSkills()
    {
        SkillData.FixedSkillIds.Clear();
        foreach (var item in FixedSkills)
            if (item != null && SkillData.Table.ContainsKey(item.SkillId)) SkillData.FixedSkillIds[item.Kind] = item.SkillId;
    }
}

public enum RewardCategory { DungeonRandomArtifact, StrongEnemyArtifact, IntruderRelic }

[Serializable]
public class RewardRecord
{
    public RewardCategory Category;
    public Team Owner;
    public string Id;
    public string DisplayName;
}

/// <summary>固有報酬の中身はカタログで設定。未設定の報酬を勝手に生成しない。</summary>
public static class UniqueRewardSystem
{
    public static readonly List<RewardRecord> Records = new List<RewardRecord>();

    public static void Grant(Team owner, R1ContentCatalog.UniqueReward reward, RewardCategory category, GameSystems systems)
    {
        if (reward == null || string.IsNullOrWhiteSpace(reward.Id) || systems == null
            || (owner != Team.Player && owner != Team.Enemy)) return;
        Records.Add(new RewardRecord { Category = category, Owner = owner, Id = reward.Id, DisplayName = reward.DisplayName });
        var units = owner == Team.Player ? systems.UnitSetting.PlayerUnit : systems.UnitSetting.EnemyUnit;
        foreach (var unit in units.GetComponentsInChildren<Status>())
        {
            if (!unit.IsAlive) continue;
            unit.ATK += reward.AttackBonus;
            unit.DEF += reward.DefenseBonus;
        }
        var crystals = owner == Team.Player ? systems.CrystalSystem.Playercrystal : systems.CrystalSystem.Enemycrystal;
        foreach (var crystal in crystals.GetComponentsInChildren<Status>())
        {
            crystal.MaxHP += reward.CrystalHPBonus;
            crystal.ApplyHeal(reward.CrystalHPBonus);
        }
        if (owner == Team.Player)
            ToastMessageUI.Show($"{(category == RewardCategory.IntruderRelic ? "固有レリック" : "固有アーティファクト")}獲得: {reward.DisplayName}", ToastMessageUI.MessageType.Info);
    }
}
