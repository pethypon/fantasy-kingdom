using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  SaveSystem.Collect — 現在のゲーム状態からセーブデータを構築
// =====================================================================
public static partial class SaveSystem
{
    /// <summary>現在のゲーム状態をセーブデータに変換</summary>
    public static GameSaveData CollectGameState(
        TurnGenerator turnGen,
        FactionState factionState,
        TimerSystem timerSystem = null,
        VisionGenerator visionGen = null,
        AICommander aiCommander = null)
    {
        var data = new GameSaveData
        {
            Turn = turnGen.Context.Turn,
            MatchId = turnGen.MatchId,
            ThreatLevel = aiCommander?.ThreatLevel.Level ?? LoadProfile().ThreatLevel,
            Checkpoint = "player_turn",
            HasExplicitMapSnapshot = true,
            LandOverrides = new List<Vector3Int>(turnGen.Systems.MapCreate.SavedLandOverrides),
            R1Terrain = turnGen.Systems.MapCreate.UseR1Terrain,
            MapWidth = turnGen.Systems.MapCreate.maxX,
            MapDepth = turnGen.Systems.MapCreate.maxZ,
            MapMinY = turnGen.Systems.MapCreate.minY,
            MapMaxY = turnGen.Systems.MapCreate.maxY,
            MapAverageFoundation = turnGen.Systems.MapCreate.AverageFoundation,
            MapAmplitude = turnGen.Systems.MapCreate.Amplitude,
            HasMapGenerationSettings = true,
            MapNoiseScale = turnGen.Systems.MapCreate.noiseScale,
            MapRiverHalfWidth = turnGen.Systems.MapCreate.riverHalfWidth,
            MapSeedX = turnGen.Systems.MapCreate.SeedX,
            MapSeedZ = turnGen.Systems.MapCreate.SeedZ,
            Environment = turnGen.Systems.MapCreate.Environment.Capture(),
            PCPx = turnGen.Systems.CrystalSystem.PCP.x,
            PCPy = turnGen.Systems.CrystalSystem.PCP.y,
            PCPz = turnGen.Systems.CrystalSystem.PCP.z,
            ECPx = turnGen.Systems.CrystalSystem.ECP.x,
            ECPy = turnGen.Systems.CrystalSystem.ECP.y,
            ECPz = turnGen.Systems.CrystalSystem.ECP.z
        };

        if (turnGen.Systems.DungeonSystem != null)
        {
            data.Dungeons = new List<DungeonSystem.DungeonInfo>(turnGen.Systems.DungeonSystem.Dungeons);
            data.DungeonLastRound = turnGen.Systems.DungeonSystem.LastProcessedRound;
        }

        var neutral = turnGen.Systems.NeutralFactionSystem;
        if (neutral != null)
        {
            data.IndependentUnits = neutral.Capture();
            data.PreviousMonsterCount = neutral.PreviousMonsterCount;
            data.IndependentLastRound = neutral.LastRound;
            data.SpawnedIntruders = new List<string>(neutral.SpawnedIntruders);
        }
        data.Rewards = new List<RewardRecord>(UniqueRewardSystem.Records);
        data.ThirdFaction = turnGen.Systems.ThirdFactionSystem?.Capture();
        data.DeveloperAutoplayUsed = turnGen.DeveloperPlayerAIWasUsed;
        data.PlayerTerritoryIntel = turnGen.CapturePlayerTerritoryIntel();
        data.HasPlayerTerritoryIntelSnapshot = data.PlayerTerritoryIntel != null;
        data.PlayerObservedMemory = turnGen.CapturePlayerObservedMemory();
        data.HasPlayerObservedMemorySnapshot = data.PlayerObservedMemory != null;
        data.PlayerPreservation = turnGen.CapturePlayerPreservation();
        data.HasPlayerPreservationSnapshot = data.PlayerPreservation != null;
        data.WildBoss = turnGen.Systems.WildBossSystem?.Capture();
        // ユニット収集
        CollectUnits(data, turnGen.Systems.UnitSetting.PlayerUnit, turnGen.Systems.UnitSetting.EnemyUnit);

        // 建築物収集
        if (turnGen.Systems.BuildSystem != null)
        {
            CollectBuildings(data, turnGen.Systems.BuildSystem.PlayerBuildingParent);
            CollectBuildings(data, turnGen.Systems.BuildSystem.EnemyBuildingParent);
        }

        // クリスタル収集
        CollectCrystals(data, turnGen.Systems.CrystalSystem);

        // 資源
        CopyResources(factionState.PlayerResources, data.PlayerResources);
        CopyResources(factionState.EnemyResources, data.EnemyResources);

        // AP
        CopyAP(factionState.PlayerAP, data.PlayerAP);
        CopyAP(factionState.EnemyAP, data.EnemyAP);

        // サブクリスタル
        data.PlayerSubCrystals = factionState.PlayerSubCrystals;
        data.EnemySubCrystals = factionState.EnemySubCrystals;

        // タイマー
        if (timerSystem != null)
            CollectTimer(data.Timer, timerSystem);

        // 霧の戦争（探索済み）
        if (visionGen != null)
            CollectFog(data.Fog, visionGen);

        // AI状態
        if (aiCommander != null)
            CollectAI(data.AI, aiCommander);

        // NationState追加データ
        CollectNationExtra(data.PlayerNationExtra, factionState.PlayerNation);
        CollectNationExtra(data.EnemyNationExtra, factionState.EnemyNation);

        return data;
    }

    // ================================================================
    //  ユニット・建築物収集
    // ================================================================

    static void CollectUnits(GameSaveData data, Transform playerParent, Transform enemyParent)
    {
        if (playerParent != null)
            foreach (Status s in playerParent.GetComponentsInChildren<Status>(true))
                if (s.type == Type.Unit && (!s.IsRuntimeSpawned || s.gameObject.activeSelf)) data.Units.Add(StatusToSaveData(s));

        if (enemyParent != null)
            foreach (Status s in enemyParent.GetComponentsInChildren<Status>(true))
                if (s.type == Type.Unit && (!s.IsRuntimeSpawned || s.gameObject.activeSelf)) data.Units.Add(StatusToSaveData(s));
    }

    static void CollectBuildings(GameSaveData data, Transform parent)
    {
        if (parent == null) return;
        foreach (Status s in parent.GetComponentsInChildren<Status>(true))
            if (s.gameObject.activeSelf) data.Units.Add(StatusToSaveData(s));
    }

    static void CollectCrystals(GameSaveData data, CrystalSystem cs)
    {
        if (cs == null) return;

        // クリスタル親からStatusを収集
        var playerCrystalParent = cs.Playercrystal;
        var enemyCrystalParent = cs.Enemycrystal;

        if (playerCrystalParent != null)
            foreach (Status s in playerCrystalParent.GetComponentsInChildren<Status>(true))
                if (s.kind == Kind.Crystal || s.kind == Kind.SubCrystal && s.gameObject.activeSelf)
                    data.Units.Add(StatusToSaveData(s));

        if (enemyCrystalParent != null)
            foreach (Status s in enemyCrystalParent.GetComponentsInChildren<Status>(true))
                if (s.kind == Kind.Crystal || s.kind == Kind.SubCrystal && s.gameObject.activeSelf)
                    data.Units.Add(StatusToSaveData(s));
    }

    public static UnitSaveData CaptureUnit(Status s) => StatusToSaveData(s);

    static UnitSaveData StatusToSaveData(Status s)
    {
        var d = new UnitSaveData
        {
            ReflectionLifeId = s.ReflectionLifeId,
            Special = SpecialSkillRuntime.CopyState(s.Special),
            NaturalRecovery = NaturalRecoverySystem.CopyState(s.NaturalRecovery),
            HasBarrierSnapshot = true, Barrier = s.Barrier, MaxBarrier = s.MaxBarrier, BarrierRecovery = BarrierSystem.CopyState(s.BarrierRecovery),
            Kind = StableSaveKeys.Key(s.kind),
            DefinitionId = s.type == Type.Unit ? s.unitDefinitionId : null,
            HasPrimaryCommanderRole = true, IsPrimaryCommander = s.IsPrimaryCommander,
            Team = StableSaveKeys.Key(s.team),
            Type = StableSaveKeys.Key(s.type),
            HP = s.HP,
            MaxHP = s.MaxHP,
            ATK = s.ATK,
            DEF = s.DEF,
            Level = s.type == Type.Unit ? 1 : s.Level,
            FixedStatsRevision = 2, FixedStatsMigrationDeferred = s.FixedStatsMigrationDeferred,
            LegacyUnitStats = s.LegacyUnitStats?.Copy(),
            ShieldTurns = s.ShieldTurns,
            ShieldActivated = s.ShieldActivated,
            ShieldEverActivated = s.ShieldEverActivated,
            MainCrystalRuleVersion = s.kind == Kind.Crystal ? MainCrystalRules.Version : 0,
            MainCrystalCounter = s.kind == Kind.Crystal ? s.CrystalCounter?.Copy() : null,
            PosX = s.transform.position.x,
            PosY = s.transform.position.y,
            PosZ = s.transform.position.z,
            Direction = StableSaveKeys.Key(s.direction),
            PassiveSkill = StableSaveKeys.Key(s.passiveskill),
            AssignedSkillId = s.AssignedSkillId,
            SkillCooldown = s.SkillCooldown,
            FacilityKind = StableSaveKeys.Key(s.facilityKind),
            AuthoredFacilityId = s.AuthoredFacility != null ? s.AuthoredFacility.definitionId : s.authoredFacilityId,
            IsActive = s.gameObject.activeSelf,
            HasMovedThisTurn = s.HasMovedThisTurn,
            FirstSkillUsedThisTurn = s.FirstSkillUsedThisTurn,
            DebuffNullifiedThisTurn = s.DebuffNullifiedThisTurn,
            InterceptUsedThisTurn = s.InterceptUsedThisTurn,
            Fatigue = s.Fatigue,
            UpkeepUnpaidTurns = s.UpkeepUnpaidTurns,
            LastMaintenanceRound = s.LastMaintenanceRound,
            BuiltByEngineer = s.BuiltByEngineer,
            BuildingOperationAvailable = s.BuildingOperationAvailable,
            Experience = s.type == Type.Unit ? 0 : s.Experience,
            SpecialAbility = StableSaveKeys.Key(s.specialAbility),
            SurvivalInstinctUsed = s.SurvivalInstinctUsed
        };

        if (!s.FixedStatsMigrationDeferred && StatusBalanceRefresh.TryGetDefinitionStats(s, out int definitionHP, out int definitionATK, out int definitionDEF))
        {
            d.HasDefinitionStatSnapshot = true;
            d.DefinitionMaxHP = definitionHP;
            d.DefinitionATK = definitionATK;
            d.DefinitionDEF = definitionDEF;
        }

        if (s.ActiveEffects != null)
        {
            foreach (var e in s.ActiveEffects)
            {
                d.ActiveEffects.Add(new EffectSaveData
                {
                    DebuffType = StableSaveKeys.Key(e.debuffType),
                    BuffType = StableSaveKeys.Key(e.buffType),
                    RemainingTurns = e.remainingTurns
                });
            }
        }

        return d;
    }

    // ================================================================
    //  タイマー収集
    // ================================================================

    static void CollectTimer(TimerSaveData dst, TimerSystem timer)
    {
        dst.TurnTimeRemaining = timer.TurnTimeRemaining;
        dst.PlayerTotalTime = timer.PlayerTimeRemaining;
        dst.EnemyTotalTime = timer.EnemyTimeRemaining;
        dst.TurnTimeLimit = timer.TurnTimeLimit;
    }

    // ================================================================
    //  霧の戦争収集
    // ================================================================

    static void CollectFog(FogSaveData dst, VisionGenerator visionGen)
    {
        if (visionGen.PlayerExplored != null)
            foreach (var cell in visionGen.PlayerExplored)
                dst.PlayerExplored.Add(new Vec3IntData(cell));

        if (visionGen.EnemyExplored != null)
            foreach (var cell in visionGen.EnemyExplored)
                dst.EnemyExplored.Add(new Vec3IntData(cell));
    }

    // ================================================================
    //  AI状態収集
    // ================================================================

    static void CollectAI(AISaveData dst, AICommander commander)
    {
        // Personality
        var p = commander.Personality;
        dst.MajorPersonality = StableSaveKeys.Key(p.Major);
        dst.TraitCaution = p.Traits.Caution;
        dst.TraitCommand = p.Traits.Command;
        dst.TraitObsession = p.Traits.Obsession;
        dst.TraitDefense = p.Traits.Defense;
        dst.TraitTactics = p.Traits.Tactics;
        dst.TraitDevelopment = p.Traits.Development;

        // Strategy
        dst.CurrentStrategy = StableSaveKeys.Key(commander.CurrentStrategy);

        // Stats（公開プロパティ経由）
        dst.TotalMoves = commander.SaveTotalMoves;
        dst.TotalAttacks = commander.SaveTotalAttacks;
        dst.TotalSkills = commander.SaveTotalSkills;
        dst.TotalRetreats = commander.SaveTotalRetreats;
        dst.TotalBuilds = commander.SaveTotalBuilds;
        dst.TotalSummons = commander.SaveTotalSummons;
        dst.TotalKills = commander.SaveTotalKills;
        dst.AITurnCount = commander.SaveTurnCount;
        dst.ObservedMemory = commander.CaptureObservedMemory();
        dst.HasObservedMemorySnapshot = dst.ObservedMemory != null;
        dst.Operation = commander.SavePlan;
        dst.Reflection = commander.CaptureReflectionState();
        dst.Operations = commander.CaptureOperationState();
        dst.Exploration = commander.CaptureExplorationState();
        dst.StrategicObjective=commander.SaveStrategicObjective;
        dst.TerritoryIntel=commander.CaptureTerritoryState();
        dst.HasTerritoryIntelSnapshot = dst.TerritoryIntel != null;
        dst.Preservation = commander.CapturePreservationState();
        dst.HasPreservationSnapshot = dst.Preservation != null;
        dst.RngSeed = commander.RandomSeed;
        dst.HasDeterministicSeed = true;

        // Learning
        var learning = commander.Learning;
        dst.LearningActive = learning.IsActive;
        dst.LearningCaution = learning.SaveCautionModifier;
        dst.LearningCommand = learning.SaveCommandModifier;
        dst.LearningTactics = learning.SaveTacticsModifier;
        dst.LearningDefense = learning.SaveDefenseModifier;
        dst.LearningDevelop = learning.SaveDevelopModifier;

        CollectCellCountDict(learning.SaveFailedFrontalAttacks, dst.FailedFrontalAttacks);
        CollectCellCountDict(learning.SaveSuccessFlanks, dst.SuccessFlanks);
        CollectCellCountDict(learning.SaveIsolatedDeaths, dst.IsolatedDeaths);
        CollectCellCountDict(learning.SavePlayerDefensePositions, dst.PlayerDefensePositions);
        CollectCellCountDict(learning.SaveRouteSuccess, dst.RouteSuccess);
        CollectCellCountDict(learning.SaveRouteFailure, dst.RouteFailure);

        // 機械学習AI状態
        var ml = commander.MLIntegration;
        dst.MLActive = ml?.IsActive ?? false;
        dst.MLTotalMatchesTrained = ml?.TotalMatchesTrained ?? 0;
        dst.MLAverageLoss = ml?.AverageLoss ?? 0;
    }

    static void CollectCellCountDict(Dictionary<Vector3Int, int> src, List<CellCountData> dst)
    {
        if (src == null) return;
        foreach (var kv in src)
            dst.Add(new CellCountData(kv.Key, kv.Value));
    }

    // ================================================================
    //  NationState追加データ収集
    // ================================================================

    static void CollectNationExtra(NationExtraSaveData dst, NationState nation)
    {
        if (nation == null) return;
        dst.PendingReturns = new List<int>(nation.PendingReturns);
        dst.StarvationCounter = nation.StarvationCounter;
        dst.TurnsAlive = nation.TurnsAlive;
        dst.CitizenCapacity = nation.CitizenCapacity;
        dst.ResourceCapacity = nation.ResourceCapacity;
        dst.BarracksXP = nation.BarracksXP;
    }

    // ================================================================
    //  資源・AP コピーヘルパー
    // ================================================================

    static void CopyResources(FactionState.ResourceData src, ResourceSaveData dst)
    {
        dst.Wood = src.Wood; dst.Stone = src.Stone; dst.Water = src.Water;
        dst.MagicOre = src.MagicOre; dst.Iron = src.Iron;
        dst.Wheat = src.Wheat; dst.Bread = src.Bread; dst.Citizen = src.Citizen;
    }

    static void CopyAP(FactionState.APData src, APSaveData dst)
    {
        dst.Current = src.Current; dst.Reset = src.Reset;
        dst.Plus = src.Plus; dst.Minus = src.Minus;
    }
}
