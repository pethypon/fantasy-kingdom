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
            ThreatLevel = LoadProfile().ThreatLevel,
            MapSeedX = turnGen.Systems.MapCreate.SeedX,
            MapSeedZ = turnGen.Systems.MapCreate.SeedZ,
            PCPx = turnGen.Systems.CrystalSystem.PCP.x,
            PCPy = turnGen.Systems.CrystalSystem.PCP.y,
            PCPz = turnGen.Systems.CrystalSystem.PCP.z,
            ECPx = turnGen.Systems.CrystalSystem.ECP.x,
            ECPy = turnGen.Systems.CrystalSystem.ECP.y,
            ECPz = turnGen.Systems.CrystalSystem.ECP.z
        };

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
                if (s.type == Type.Unit) data.Units.Add(StatusToSaveData(s));

        if (enemyParent != null)
            foreach (Status s in enemyParent.GetComponentsInChildren<Status>(true))
                if (s.type == Type.Unit) data.Units.Add(StatusToSaveData(s));
    }

    static void CollectBuildings(GameSaveData data, Transform parent)
    {
        if (parent == null) return;
        foreach (Status s in parent.GetComponentsInChildren<Status>(true))
            data.Units.Add(StatusToSaveData(s));
    }

    static void CollectCrystals(GameSaveData data, CrystalSystem cs)
    {
        if (cs == null) return;

        // クリスタル親からStatusを収集
        var playerCrystalParent = cs.transform.parent?.Find("PlayerCrystal");
        var enemyCrystalParent = cs.transform.parent?.Find("EnemyCrystal");

        if (playerCrystalParent != null)
            foreach (Status s in playerCrystalParent.GetComponentsInChildren<Status>(true))
                if (s.kind == Kind.Crystal || s.kind == Kind.SubCrystal)
                    data.Units.Add(StatusToSaveData(s));

        if (enemyCrystalParent != null)
            foreach (Status s in enemyCrystalParent.GetComponentsInChildren<Status>(true))
                if (s.kind == Kind.Crystal || s.kind == Kind.SubCrystal)
                    data.Units.Add(StatusToSaveData(s));
    }

    static UnitSaveData StatusToSaveData(Status s)
    {
        var d = new UnitSaveData
        {
            Kind = s.kind.ToString(),
            Team = s.team.ToString(),
            Type = s.type.ToString(),
            HP = s.HP,
            MaxHP = s.MaxHP,
            ATK = s.ATK,
            DEF = s.DEF,
            Level = s.Level,
            ShieldTurns = s.ShieldTurns,
            ShieldActivated = s.ShieldActivated,
            ShieldEverActivated = s.ShieldEverActivated,
            PosX = s.transform.position.x,
            PosY = s.transform.position.y,
            PosZ = s.transform.position.z,
            Direction = s.direction.ToString(),
            PassiveSkill = s.passiveskill.ToString(),
            AssignedSkillId = s.AssignedSkillId,
            SkillCooldown = s.SkillCooldown,
            FacilityKind = s.facilityKind.ToString(),
            IsActive = s.gameObject.activeSelf,
            Fatigue = s.Fatigue
        };

        if (s.ActiveEffects != null)
        {
            foreach (var e in s.ActiveEffects)
            {
                d.ActiveEffects.Add(new EffectSaveData
                {
                    DebuffType = e.debuffType.ToString(),
                    BuffType = e.buffType.ToString(),
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
        dst.MajorPersonality = p.Major.ToString();
        dst.TraitCaution = p.Traits.Caution;
        dst.TraitCommand = p.Traits.Command;
        dst.TraitObsession = p.Traits.Obsession;
        dst.TraitDefense = p.Traits.Defense;
        dst.TraitTactics = p.Traits.Tactics;
        dst.TraitDevelopment = p.Traits.Development;

        // Strategy
        dst.CurrentStrategy = commander.CurrentStrategy.ToString();

        // Stats（公開プロパティ経由）
        dst.TotalMoves = commander.SaveTotalMoves;
        dst.TotalAttacks = commander.SaveTotalAttacks;
        dst.TotalSkills = commander.SaveTotalSkills;
        dst.TotalRetreats = commander.SaveTotalRetreats;
        dst.TotalBuilds = commander.SaveTotalBuilds;
        dst.TotalSummons = commander.SaveTotalSummons;
        dst.TotalKills = commander.SaveTotalKills;
        dst.AITurnCount = commander.SaveTurnCount;
        dst.RngSeed = commander.ThreatLevel.Level; // シードは脅威度から復元可

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
        dst.MLActive = ml.IsActive;
        dst.MLTotalMatchesTrained = ml.TotalMatchesTrained;
        dst.MLAverageLoss = ml.AverageLoss;
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
        dst.Coal = src.Coal; dst.IronOre = src.IronOre; dst.MagicOre = src.MagicOre;
        dst.Plank = src.Plank; dst.CutStone = src.CutStone; dst.Iron = src.Iron;
        dst.Wheat = src.Wheat; dst.Bread = src.Bread; dst.Citizen = src.Citizen;
    }

    static void CopyAP(FactionState.APData src, APSaveData dst)
    {
        dst.Current = src.Current; dst.Reset = src.Reset;
        dst.Plus = src.Plus; dst.Minus = src.Minus;
    }
}
