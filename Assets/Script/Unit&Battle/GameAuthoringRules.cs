using System;
using UnityEngine;

[Serializable]
public sealed class FactionAuthoringSetting
{
    public Team team;
    public string displayName;
    public Color color = Color.white;
    [Range(3, 50)] public int initialAP = 30;
    public FactionState.ResourceData initialResources = new FactionState.ResourceData
    {
        Wood = 200, Stone = 200, Iron = 30, MagicOre = 15, Water = 50, Bread = 100, Citizen = 5
    };
    [TextArea] public string controllerDescription;
}

/// <summary>Opt-in rules. Missing or disabled assets preserve the shipped rules and legacy saves.</summary>
[CreateAssetMenu(menuName = "Fantasy Kingdom/ゲーム制作/ゲームルール")]
public sealed class GameAuthoringRules : ScriptableObject
{
    public const string ResourcesPath = "GameContent/Rules";
    public bool applyRules;
    [Min(1)] public float turnSeconds = 180;
    [Min(1)] public float totalSeconds = 36000;
    [Min(0)] public float timeBonusSeconds = 60;
    [Range(1, 50)] public int moveAP = 3, attackAP = 2;
    [Range(0, 50)] public int heightAP = 2;
    [Range(0, 10)] public int citizenAP = 1, breadPerCitizen = 1;
    [Range(1, 100)] public int starvationGraceTurns = 10;
    [Range(16, 80)] public int mapWidth = 35, mapDepth = 35;
    [Range(.01f, 1)] public float noiseScale = .1f;
    [Range(0, 3)] public float riverHalfWidth = .7f;
    public FactionAuthoringSetting[] factions =
    {
        new FactionAuthoringSetting { team = Team.Player, displayName = "プレイヤー", color = new Color(.18f,.72f,1f), controllerDescription = "手動操作（開発用 F8 AI 切替）" },
        new FactionAuthoringSetting { team = Team.Enemy, displayName = "敵軍", color = new Color(1f,.25f,.23f), controllerDescription = "敵 AI" },
        new FactionAuthoringSetting { team = Team.Monster, displayName = "魔物", color = new Color(.7f,.42f,.9f), controllerDescription = "第三陣営 AI（敵の行動後）" },
        new FactionAuthoringSetting { team = Team.Intruder, displayName = "乱入者", color = new Color(1f,.64f,.22f), controllerDescription = "第三陣営 AI（敵の行動後）" },
        new FactionAuthoringSetting { team = Team.Obstacle, displayName = "強敵", color = new Color(.9f,.75f,.35f), controllerDescription = "強敵 AI（第三陣営の行動後）" }
    };

    static GameAuthoringRules loaded;
    static bool didLoad;
    public static GameAuthoringRules Active
    {
        get
        {
            if (!didLoad) { loaded = Resources.Load<GameAuthoringRules>(ResourcesPath); didLoad = true; }
            return loaded != null && loaded.applyRules ? loaded : null;
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetCache() { loaded = null; didLoad = false; }
    void OnValidate() => ResetCache();

    public FactionAuthoringSetting FindFaction(Team team)
    {
        if (factions != null) foreach (var faction in factions)
            if (faction != null && faction.team == team) return faction;
        return null;
    }
    public static string FactionName(Team team, string fallback)
    {
        var faction = Active?.FindFaction(team);
        return faction != null && !string.IsNullOrWhiteSpace(faction.displayName) ? faction.displayName : fallback;
    }
    public static Color FactionColor(Team team, Color fallback) => Active?.FindFaction(team)?.color ?? fallback;

    public void ApplyNewGame(FactionState state)
    {
        if (!applyRules || state == null) return;
        foreach (var team in new[] { Team.Player, Team.Enemy, Team.Monster, Team.Intruder })
        {
            var faction = FindFaction(team);
            if (faction == null) continue;
            var ap = state.GetAPData(team);
            ap.Reset = Mathf.Clamp(faction.initialAP, 3, GameConstants.MaxAP);
            ap.Current = ap.Reset;
            if (team != Team.Player && team != Team.Enemy) continue;
            CopyResources(faction.initialResources, state.GetResources(team));
        }
    }
    public void ApplyTimer(TimerSystem timer)
    {
        if (!applyRules || timer == null) return;
        timer.TurnTimeLimit = SafePositive(turnSeconds, 180);
        timer.PlayerTotalTime = timer.EnemyTotalTime = Mathf.Clamp(SafePositive(totalSeconds, 36000), 1, timer.MaxTotalTime);
        timer.TurnTimeBonus = Mathf.Clamp(SafePositive(timeBonusSeconds, 0), 0, timer.MaxTotalTime);
    }
    public void ApplyNewMap(MapCreate map)
    {
        if (!applyRules || map == null) return;
        map.preset = MapPresetSize.Custom;
        map.maxX = Mathf.Clamp(mapWidth, 16, 80); map.maxZ = Mathf.Clamp(mapDepth, 16, 80);
        map.noiseScale = Mathf.Clamp(SafePositive(noiseScale, .1f), .01f, 1);
        map.riverHalfWidth = Mathf.Clamp(SafePositive(riverHalfWidth, .7f), 0, 3);
    }
    static float SafePositive(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Max(0, value);
    static void CopyResources(FactionState.ResourceData from, FactionState.ResourceData to)
    {
        if (from == null || to == null) return;
        to.Wood = Mathf.Max(0, from.Wood); to.Stone = Mathf.Max(0, from.Stone);
        to.Iron = Mathf.Max(0, from.Iron); to.MagicOre = Mathf.Max(0, from.MagicOre);
        to.Wheat = Mathf.Max(0, from.Wheat); to.Bread = Mathf.Max(0, from.Bread);
        to.Water = Mathf.Max(0, from.Water); to.Citizen = Mathf.Max(0, from.Citizen);
    }
}
