#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Controlled runtime checks, separate from ordinary match results.</summary>
public static class QualityGameplayChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static void Check(string name, bool ok)
    { if (!ok) throw new InvalidOperationException("[QualityGameplay] FAIL " + name); checks++; Debug.Log("[QualityGameplay] PASS " + name); }
    public static void Run(GameSystems live)
    {
        checks=0;
        var records=new List<RewardRecord>(UniqueRewardSystem.Records);
        using var effects=BoardActorAnimation.SuppressEffects();
        try
        {
            Check("archer active skill name describes its actual active effect",live.UnitSetting.UnitDataMap.TryGetValue(Kind.Archer,out var archer)
                && archer.specialSkill != null && SpecialSkillPresentation.Name(archer.specialSkill)=="毒霧の退避");
            var pool=(DungeonSystem.Artifact[])typeof(DungeonSystem).GetMethod("BuildArtifactPool",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
            Check("no removed DEF or shield mechanic is rolled",pool.Length==3 && Array.TrueForAll(pool,ArtifactPresentation.Available));
            foreach(var team in new[]{Team.Player,Team.Enemy})
            {
                using var f=new R2ConstructionEconomyTests.Fixture(live,team);
                var go=new GameObject("quality reward fixture");
                try
                {
                    var dungeon=go.AddComponent<DungeonSystem>();
                    void Set(string name,object value)=>typeof(DungeonSystem).GetField(name,Hidden).SetValue(dungeon,value);
                    Set("factionState",f.State);Set("unitSetting",f.Units);Set("crystalsystem",f.Crystals);
                    var crystal=new GameObject("observed crystal");crystal.transform.SetParent(team==Team.Player?f.Crystals.Playercrystal:f.Crystals.Enemycrystal);
                    var status=crystal.AddComponent<Status>(); status.team=team;status.kind=Kind.Crystal;status.MaxHP=3000;status.HP=2500;
                    var method=typeof(DungeonSystem).GetMethod("GrantArtifact",Hidden);
                    void Grant(DungeonSystem.Artifact art)=>method.Invoke(dungeon,new object[]{team,art});
                    int mana=f.Resources.MagicOre,atk=f.Engineer.ATK;
                    Grant(DungeonSystem.Artifact.ManaFocus);
                    Check(team+" mana reward is 30 once and has an owned save record",f.Resources.MagicOre==mana+30
                        && UniqueRewardSystem.Records.Exists(r=>r.Owner==team&&r.Id=="dungeon:ManaFocus"));
                    Grant(DungeonSystem.Artifact.WarBanner);
                    Check(team+" banner applies the documented current-unit bonus",f.Engineer.ATK==atk+2);
                    Grant(DungeonSystem.Artifact.CrystalShard);
                    Check(team+" shard applies maximum and current HP bonus",status.MaxHP==4000&&status.HP==3500);
                    int count=UniqueRewardSystem.Records.Count;
                    Grant(DungeonSystem.Artifact.IronAegis);Grant(DungeonSystem.Artifact.PhoenixFeather);
                    Check(team+" removed rewards cannot masquerade as acquired items",UniqueRewardSystem.Records.Count==count);
                }
                finally{UnityEngine.Object.DestroyImmediate(go);}
            }
            Check("optional missing icon is safe",GameIconCatalog.Get("unregistered.key")==null);
            Check("generated bread is a runtime sprite",GameIconCatalog.Get("resource.Bread")!=null);
            foreach(string key in new[]{"resource.Wood","resource.Stone","resource.Iron","resource.MagicOre","resource.Wheat","resource.Water","resource.Citizen","resource.SubCrystal","debuff.Poison","artifact.WarBanner","item.Relic"})
                Check("installed artwork connected: "+key,GameIconCatalog.Get(key)!=null);
        }
        finally{UniqueRewardSystem.Records.Clear();UniqueRewardSystem.Records.AddRange(records);}
        Debug.Log("[QualityGameplay] RESULT checks="+checks);
    }
}
#endif
