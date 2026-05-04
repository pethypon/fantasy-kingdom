using UnityEngine;

/// <summary>
/// ゲームシステム（Build, Summon, Economy, Attack, SubCrystal, Skill, Timer）の
/// 検出・生成・初期化を担当する。GameGenerator から分離された単一責任クラス。
/// </summary>
public static class SystemInitializer
{
    /// <summary>
    /// BuildSystem, SummonSystem, EconomySystem, BuildingAttackSystem, SubCrystalSystem を
    /// 初期化し、TurnGenerator に登録する。
    /// </summary>
    public static void InitGameSystems(
        GameGenerator owner,
        TurnGenerator turnGen,
        FactionState factionState,
        MapCreate mapCreate,
        TerritorySystem territorySystem,
        APSystem apSystem,
        MoveGenerator moveGenerator,
        VisionGenerator visionGenerator,
        CrystalSystem crystalSystem,
        UnitSetting unitSetting,
        ref BuildSystem buildSystem,
        ref SummonSystem summonSystem,
        ref EconomySystem economySystem,
        ref BuildingAttackSystem buildingAttackSystem,
        ref SubCrystalSystem subCrystalSystem,
        ref DungeonSystem dungeonSystem,
        ref WildBossSystem wildBossSystem,
        UIBuilder uiBuilder)
    {
        // ---- BuildSystem ----
        buildSystem = EnsureComponent(owner, buildSystem);
        if (buildSystem != null)
        {
            buildSystem.Init(turnGen, territorySystem, apSystem,
                             factionState, moveGenerator, mapCreate);
            turnGen.Systems.BuildSystem = buildSystem;

            if (uiBuilder != null)
                uiBuilder.InitBuildButtons(buildSystem, apSystem, factionState);
        }

        // ---- SummonSystem ----
        summonSystem = FindOrWarn<SummonSystem>(summonSystem, "SummonSystem");
        if (summonSystem != null)
        {
            summonSystem.Init(turnGen, territorySystem, apSystem,
                              factionState, moveGenerator, mapCreate,
                              unitSetting, visionGenerator);
            turnGen.Systems.SummonSystem = summonSystem;

            if (uiBuilder != null)
                uiBuilder.InitSummonButtons(summonSystem, apSystem, factionState, unitSetting);
        }

        // ---- EconomySystem ----
        economySystem = EnsureComponent(owner, economySystem);
        economySystem.Init(buildSystem, factionState, unitSetting, crystalSystem);
        turnGen.Systems.EconomySystem = economySystem;

        // ---- BuildingAttackSystem ----
        buildingAttackSystem = EnsureComponent(owner, buildingAttackSystem);
        buildingAttackSystem.Init(buildSystem, moveGenerator, unitSetting,
                                  visionGenerator, mapCreate, crystalSystem);
        turnGen.Systems.BuildingAttackSystem = buildingAttackSystem;

        // ---- SubCrystalSystem ----
        subCrystalSystem = EnsureComponent(owner, subCrystalSystem);
        subCrystalSystem.Init(turnGen, territorySystem, factionState,
                               mapCreate, buildSystem, visionGenerator,
                               moveGenerator, crystalSystem);
        turnGen.Systems.SubCrystalSystem = subCrystalSystem;

        // ---- DungeonSystem ----
        dungeonSystem = EnsureComponent(owner, dungeonSystem);
        dungeonSystem.Init(mapCreate, crystalSystem, territorySystem,
                           factionState, unitSetting, buildSystem);
        dungeonSystem.GenerateDungeons();
        turnGen.Systems.DungeonSystem = dungeonSystem;

        // ---- WildBossSystem ----（ダンジョンと領土が確定した後に配置）
        wildBossSystem = EnsureComponent(owner, wildBossSystem);
        wildBossSystem.Init(mapCreate, crystalSystem, territorySystem,
                            dungeonSystem, unitSetting);
        wildBossSystem.GenerateWildBoss();
        turnGen.Systems.WildBossSystem = wildBossSystem;

        // ---- クロスリファレンス ----
        if (buildSystem != null)
            buildSystem.subCrystalSystem = subCrystalSystem;
        if (buildingAttackSystem != null)
            buildingAttackSystem.SetSubCrystalSystem(subCrystalSystem);
        if (buildSystem != null)
            visionGenerator.SetBuildingParents(buildSystem.PlayerBuildingParent, buildSystem.EnemyBuildingParent);
        // 強敵(WildBoss)本体と縄張りも視界外で非表示にする
        visionGenerator.SetWildBossParents(wildBossSystem.BossParent, wildBossSystem.TerritoryParent);
        // ダンジョンマーカーも視界外で非表示にする
        visionGenerator.SetDungeonMarkerParent(dungeonSystem.MarkerParent);
    }

    /// <summary>
    /// SkillSystem と TimerSystem を初期化する。
    /// </summary>
    public static void InitSkillsAndTimer(
        GameGenerator owner,
        TurnGenerator turnGen,
        FactionState factionState,
        MoveGenerator moveGenerator,
        UnitSetting unitSetting,
        ref SkillSystem skillSystem)
    {
        skillSystem = EnsureComponent(owner, skillSystem);
        skillSystem.turnGenerator = turnGen;
        skillSystem.battlesystem = turnGen.Systems.BattleSystem;
        skillSystem.moveGenerator = moveGenerator;
        skillSystem.Init(factionState);
        turnGen.Systems.SkillSystem = skillSystem;

        SkillData.AssignSkillsToAll(unitSetting.PlayerUnit);
        SkillData.AssignSkillsToAll(unitSetting.EnemyUnit);

        var timerSystem = EnsureComponent<TimerSystem>(owner, null);
        timerSystem.Init(turnGen, turnGen.Systems.CrystalSystem);
        turnGen.Systems.TimerSystem = timerSystem;
    }

    /// <summary>AI指揮官を初期化する。</summary>
    public static void InitAI(
        TurnGenerator turnGen,
        MoveGenerator moveGenerator,
        VisionGenerator visionGenerator,
        APSystem apSystem,
        UnitSetting unitSetting,
        CrystalSystem crystalSystem,
        MapCreate mapCreate,
        BuildSystem buildSystem,
        SummonSystem summonSystem,
        FactionState factionState,
        SkillSystem skillSystem,
        SubCrystalSystem subCrystalSystem)
    {
        var aiMajor = AIPersonality.RandomMajor();
        int threatLevel = SaveSystem.LoadProfile().ThreatLevel;
        turnGen.Systems.AICommander = new AICommander(
            turnGen, moveGenerator, turnGen.Systems.AttackGenerator,
            turnGen.Systems.BattleSystem, visionGenerator,
            apSystem, unitSetting, crystalSystem, mapCreate, aiMajor,
            buildSystem, summonSystem, factionState, skillSystem,
            subCrystalSystem, threatLevel);
    }

    // ================================================================
    //  ヘルパー
    // ================================================================

    /// <summary>コンポーネントが null なら owner から取得 or 追加する</summary>
    private static T EnsureComponent<T>(GameGenerator owner, T existing) where T : Component
    {
        if (existing != null) return existing;
        var found = owner.gameObject.GetComponent<T>();
        if (found != null)
        {
            Debug.LogWarning($"[SystemInitializer] {typeof(T).Name} 未設定 → 自動検出");
            return found;
        }
        Debug.LogWarning($"[SystemInitializer] {typeof(T).Name} 未設定 → 自動生成");
        return owner.gameObject.AddComponent<T>();
    }

    /// <summary>既存インスタンスがなければ FindFirstObjectByType で検索する</summary>
    private static T FindOrWarn<T>(T existing, string name) where T : Object
    {
        if (existing != null) return existing;
        var found = Object.FindFirstObjectByType<T>();
        if (found != null)
            Debug.LogWarning($"[SystemInitializer] {name} 未設定 → 自動検出");
        return found;
    }
}
