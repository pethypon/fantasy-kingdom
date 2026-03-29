using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// =====================================================================
//  SaveSystem — セーブ/ロード管理
//
//  JSON形式でゲーム状態を永続化する。
//  セーブデータは Application.persistentDataPath/saves/ に保存。
//  脅威度とマッチ履歴はプロファイルデータとして常に永続化する。
// =====================================================================
public static class SaveSystem
{
    const string SaveDir = "saves";
    const string ProfileFile = "profile.json";
    const int MaxSlots = 3;

    // ================================================================
    //  プロファイルデータ（脅威度 + 通算データ）
    // ================================================================

    [Serializable]
    public class ProfileData
    {
        public int ThreatLevel = 1;
        public int TotalWins = 0;
        public int TotalLosses = 0;
        public List<MatchAnalysisData> MatchHistory = new List<MatchAnalysisData>();

        // 機械学習AI通算データ
        public int MLTotalMatchesTrained = 0;
        public int MLTotalTrainingSteps = 0;
        public bool MLWeightsExist = false;
    }

    [Serializable]
    public class MatchAnalysisData
    {
        public int TurnsPlayed;
        public string PrimaryFailure;
    }

    // ================================================================
    //  ゲームセーブデータ
    // ================================================================

    [Serializable]
    public class GameSaveData
    {
        public string SaveDate;
        public int Turn;
        public int ThreatLevel;

        // ユニット
        public List<UnitSaveData> Units = new List<UnitSaveData>();

        // 資源
        public ResourceSaveData PlayerResources = new ResourceSaveData();
        public ResourceSaveData EnemyResources = new ResourceSaveData();

        // AP
        public APSaveData PlayerAP = new APSaveData();
        public APSaveData EnemyAP = new APSaveData();

        // サブクリスタル
        public int PlayerSubCrystals;
        public int EnemySubCrystals;

        // マップシード（再生成用）
        public float MapSeedX;
        public float MapSeedZ;

        // クリスタル位置
        public float PCPx, PCPy, PCPz;
        public float ECPx, ECPy, ECPz;

        // タイマー
        public TimerSaveData Timer = new TimerSaveData();

        // 霧の戦争（探索済みタイル）
        public FogSaveData Fog = new FogSaveData();

        // AI状態
        public AISaveData AI = new AISaveData();

        // NationState追加データ
        public NationExtraSaveData PlayerNationExtra = new NationExtraSaveData();
        public NationExtraSaveData EnemyNationExtra = new NationExtraSaveData();
    }

    [Serializable]
    public class UnitSaveData
    {
        public string Kind;
        public string Team;
        public string Type;
        public int HP;
        public int MaxHP;
        public int ATK;
        public int DEF;
        public int Level;
        public int ShieldTurns;
        public bool ShieldActivated;
        public float PosX, PosY, PosZ;
        public string Direction;
        public string PassiveSkill;
        public int AssignedSkillId;
        public int SkillCooldown;
        public string FacilityKind;
        public bool IsActive;
        public int Fatigue;

        // 状態異常
        public List<EffectSaveData> ActiveEffects = new List<EffectSaveData>();
    }

    [Serializable]
    public class EffectSaveData
    {
        public string DebuffType;
        public string BuffType;
        public int RemainingTurns;
    }

    [Serializable]
    public class ResourceSaveData
    {
        public int Wood, Stone, Water, Coal, IronOre, MagicOre;
        public int Plank, CutStone, Iron, Wheat, Bread, Citizen;
    }

    [Serializable]
    public class APSaveData
    {
        public int Current, Reset, Plus, Minus;
    }

    // ================================================================
    //  タイマーセーブデータ
    // ================================================================

    [Serializable]
    public class TimerSaveData
    {
        public float TurnTimeRemaining;
        public float PlayerTotalTime;
        public float EnemyTotalTime;
        public float TurnTimeLimit;
    }

    // ================================================================
    //  霧の戦争セーブデータ（探索済みタイル座標リスト）
    // ================================================================

    [Serializable]
    public class FogSaveData
    {
        public List<Vec3IntData> PlayerExplored = new List<Vec3IntData>();
        public List<Vec3IntData> EnemyExplored = new List<Vec3IntData>();
    }

    [Serializable]
    public class Vec3IntData
    {
        public int X, Y, Z;

        public Vec3IntData() { }
        public Vec3IntData(Vector3Int v) { X = v.x; Y = v.y; Z = v.z; }
        public Vector3Int ToVector3Int() => new Vector3Int(X, Y, Z);
    }

    // ================================================================
    //  AI状態セーブデータ
    // ================================================================

    [Serializable]
    public class AISaveData
    {
        // AIPersonality
        public string MajorPersonality;
        public int TraitCaution, TraitCommand, TraitObsession;
        public int TraitDefense, TraitTactics, TraitDevelopment;

        // AICommander 統計
        public string CurrentStrategy;
        public int TotalMoves, TotalAttacks, TotalSkills;
        public int TotalRetreats, TotalBuilds, TotalSummons;
        public int TotalKills;
        public int AITurnCount;
        public int RngSeed;

        // AILearning
        public bool LearningActive;
        public List<CellCountData> FailedFrontalAttacks = new List<CellCountData>();
        public List<CellCountData> SuccessFlanks = new List<CellCountData>();
        public List<CellCountData> IsolatedDeaths = new List<CellCountData>();
        public List<CellCountData> PlayerDefensePositions = new List<CellCountData>();
        public List<CellCountData> RouteSuccess = new List<CellCountData>();
        public List<CellCountData> RouteFailure = new List<CellCountData>();
        public float LearningCaution, LearningCommand, LearningTactics;
        public float LearningDefense, LearningDevelop;

        // 機械学習AI状態
        public bool MLActive;
        public int MLTotalMatchesTrained;
        public float MLAverageLoss;
    }

    [Serializable]
    public class CellCountData
    {
        public int X, Y, Z;
        public int Count;

        public CellCountData() { }
        public CellCountData(Vector3Int cell, int count)
        {
            X = cell.x; Y = cell.y; Z = cell.z;
            Count = count;
        }
    }

    // ================================================================
    //  NationState追加データ
    // ================================================================

    [Serializable]
    public class NationExtraSaveData
    {
        public List<int> PendingReturns = new List<int>();
        public int StarvationCounter;
        public int CitizenCapacity;
        public int ResourceCapacity;
        public int BarracksXP;
    }

    // ================================================================
    //  パス管理
    // ================================================================

    static string GetSaveDir()
    {
        string dir = Path.Combine(Application.persistentDataPath, SaveDir);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        return dir;
    }

    static string GetSlotPath(int slot) =>
        Path.Combine(GetSaveDir(), $"save_{slot}.json");

    static string GetProfilePath() =>
        Path.Combine(GetSaveDir(), ProfileFile);

    // ================================================================
    //  プロファイル操作
    // ================================================================

    public static ProfileData LoadProfile()
    {
        string path = GetProfilePath();
        if (!File.Exists(path)) return new ProfileData();

        try
        {
            string json = File.ReadAllText(path);
            return JsonUtility.FromJson<ProfileData>(json) ?? new ProfileData();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveSystem] プロファイル読み込み失敗: {e.Message}");
            return new ProfileData();
        }
    }

    public static void SaveProfile(ProfileData data)
    {
        try
        {
            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(GetProfilePath(), json);
            Debug.Log($"[SaveSystem] プロファイル保存: 脅威度={data.ThreatLevel}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveSystem] プロファイル保存失敗: {e.Message}");
        }
    }

    /// <summary>脅威度を1上げてプロファイルに保存</summary>
    public static int IncrementThreatLevel()
    {
        var profile = LoadProfile();
        profile.ThreatLevel = Mathf.Clamp(profile.ThreatLevel + 1, 1, 100);
        profile.TotalWins++;
        SaveProfile(profile);
        return profile.ThreatLevel;
    }

    /// <summary>敗北を記録（脅威度は変動しない）</summary>
    public static void RecordLoss()
    {
        var profile = LoadProfile();
        profile.TotalLosses++;
        SaveProfile(profile);
    }

    // ================================================================
    //  ゲームセーブ操作
    // ================================================================

    public static void SaveGame(int slot, GameSaveData data)
    {
        if (slot < 0 || slot >= MaxSlots)
        {
            Debug.LogError($"[SaveSystem] 無効なスロット: {slot}");
            return;
        }

        data.SaveDate = DateTime.Now.ToString("yyyy/MM/dd HH:mm");

        try
        {
            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(GetSlotPath(slot), json);
            Debug.Log($"[SaveSystem] ゲーム保存: スロット{slot + 1} Turn{data.Turn}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveSystem] セーブ失敗: {e.Message}");
        }
    }

    public static GameSaveData LoadGame(int slot)
    {
        string path = GetSlotPath(slot);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[SaveSystem] スロット{slot + 1}にセーブデータなし");
            return null;
        }

        try
        {
            string json = File.ReadAllText(path);
            return JsonUtility.FromJson<GameSaveData>(json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveSystem] ロード失敗: {e.Message}");
            return null;
        }
    }

    public static bool HasSaveData(int slot)
    {
        return File.Exists(GetSlotPath(slot));
    }

    /// <summary>セーブスロットの概要情報を取得</summary>
    public static string GetSlotSummary(int slot)
    {
        if (!HasSaveData(slot)) return "--- 空き ---";

        var data = LoadGame(slot);
        if (data == null) return "--- 破損 ---";

        return $"Turn {data.Turn}  脅威度{data.ThreatLevel}  {data.SaveDate}";
    }

    // ================================================================
    //  ゲーム状態の収集
    // ================================================================

    /// <summary>現在のゲーム状態をセーブデータに変換</summary>
    public static GameSaveData CollectGameState(
        TurnGenerater turnGen,
        FactionState factionState,
        TimerSystem timerSystem = null,
        VisionGenerater visionGen = null,
        AICommander aiCommander = null)
    {
        var data = new GameSaveData
        {
            Turn = turnGen.Turn,
            ThreatLevel = LoadProfile().ThreatLevel,
            MapSeedX = turnGen.mapcreate.SeedX,
            MapSeedZ = turnGen.mapcreate.SeedZ,
            PCPx = turnGen.crystalsystem.PCP.x,
            PCPy = turnGen.crystalsystem.PCP.y,
            PCPz = turnGen.crystalsystem.PCP.z,
            ECPx = turnGen.crystalsystem.ECP.x,
            ECPy = turnGen.crystalsystem.ECP.y,
            ECPz = turnGen.crystalsystem.ECP.z
        };

        // ユニット収集
        CollectUnits(data, turnGen.unitset.PlayerUnit, turnGen.unitset.EnemyUnit);

        // 建築物収集
        if (turnGen.buildsystem != null)
        {
            CollectBuildings(data, turnGen.buildsystem.PlayerBuildingParent);
            CollectBuildings(data, turnGen.buildsystem.EnemyBuildingParent);
        }

        // クリスタル収集
        CollectCrystals(data, turnGen.crystalsystem);

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

    static void CollectFog(FogSaveData dst, VisionGenerater visionGen)
    {
        if (visionGen.PlayerExploard != null)
            foreach (var cell in visionGen.PlayerExploard)
                dst.PlayerExplored.Add(new Vec3IntData(cell));

        if (visionGen.EnemyExploard != null)
            foreach (var cell in visionGen.EnemyExploard)
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
    //  資源・AP 復元ヘルパー
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

    public static void RestoreResources(ResourceSaveData src, FactionState.ResourceData dst)
    {
        dst.Wood = src.Wood; dst.Stone = src.Stone; dst.Water = src.Water;
        dst.Coal = src.Coal; dst.IronOre = src.IronOre; dst.MagicOre = src.MagicOre;
        dst.Plank = src.Plank; dst.CutStone = src.CutStone; dst.Iron = src.Iron;
        dst.Wheat = src.Wheat; dst.Bread = src.Bread; dst.Citizen = src.Citizen;
    }

    public static void RestoreAP(APSaveData src, FactionState.APData dst)
    {
        dst.Current = src.Current; dst.Reset = src.Reset;
        dst.Plus = src.Plus; dst.Minus = src.Minus;
    }

    // ================================================================
    //  タイマー復元
    // ================================================================

    public static void RestoreTimer(TimerSaveData src, TimerSystem timer)
    {
        if (src == null || timer == null) return;
        timer.PlayerTotalTime = src.PlayerTotalTime;
        timer.EnemyTotalTime = src.EnemyTotalTime;
        timer.TurnTimeLimit = src.TurnTimeLimit;
        timer.RestoreTurnTimeRemaining(src.TurnTimeRemaining);
    }

    // ================================================================
    //  霧の戦争復元
    // ================================================================

    public static void RestoreFog(FogSaveData src, VisionGenerater visionGen)
    {
        if (src == null || visionGen == null) return;

        if (visionGen.PlayerExploard == null)
            visionGen.PlayerExploard = new HashSet<Vector3Int>();
        else
            visionGen.PlayerExploard.Clear();

        if (visionGen.EnemyExploard == null)
            visionGen.EnemyExploard = new HashSet<Vector3Int>();
        else
            visionGen.EnemyExploard.Clear();

        foreach (var v in src.PlayerExplored)
            visionGen.PlayerExploard.Add(v.ToVector3Int());
        foreach (var v in src.EnemyExplored)
            visionGen.EnemyExploard.Add(v.ToVector3Int());
    }

    // ================================================================
    //  NationState追加データ復元
    // ================================================================

    public static void RestoreNationExtra(NationExtraSaveData src, NationState nation)
    {
        if (src == null || nation == null) return;
        nation.PendingReturns = new List<int>(src.PendingReturns);
        nation.StarvationCounter = src.StarvationCounter;
        nation.CitizenCapacity = src.CitizenCapacity;
        nation.ResourceCapacity = src.ResourceCapacity;
        nation.BarracksXP = src.BarracksXP;
    }

    // ================================================================
    //  AI状態復元
    // ================================================================

    public static void RestoreAILearning(AISaveData src, AILearning learning)
    {
        if (src == null || learning == null) return;

        learning.SaveCautionModifier = src.LearningCaution;
        learning.SaveCommandModifier = src.LearningCommand;
        learning.SaveTacticsModifier = src.LearningTactics;
        learning.SaveDefenseModifier = src.LearningDefense;
        learning.SaveDevelopModifier = src.LearningDevelop;

        RestoreCellCountDict(src.FailedFrontalAttacks, learning.SaveFailedFrontalAttacks);
        RestoreCellCountDict(src.SuccessFlanks, learning.SaveSuccessFlanks);
        RestoreCellCountDict(src.IsolatedDeaths, learning.SaveIsolatedDeaths);
        RestoreCellCountDict(src.PlayerDefensePositions, learning.SavePlayerDefensePositions);
        RestoreCellCountDict(src.RouteSuccess, learning.SaveRouteSuccess);
        RestoreCellCountDict(src.RouteFailure, learning.SaveRouteFailure);
    }

    public static void RestoreAICommander(AISaveData src, AICommander commander)
    {
        if (src == null || commander == null) return;

        commander.SaveTotalMoves = src.TotalMoves;
        commander.SaveTotalAttacks = src.TotalAttacks;
        commander.SaveTotalSkills = src.TotalSkills;
        commander.SaveTotalRetreats = src.TotalRetreats;
        commander.SaveTotalBuilds = src.TotalBuilds;
        commander.SaveTotalSummons = src.TotalSummons;
        commander.SaveTotalKills = src.TotalKills;
        commander.SaveTurnCount = src.AITurnCount;

        if (Enum.TryParse<TurnStrategy>(src.CurrentStrategy, out var strategy))
            commander.RestoreStrategy(strategy);

        RestoreAILearning(src, commander.Learning);
    }

    static void RestoreCellCountDict(List<CellCountData> src, Dictionary<Vector3Int, int> dst)
    {
        if (src == null || dst == null) return;
        dst.Clear();
        foreach (var entry in src)
            dst[new Vector3Int(entry.X, entry.Y, entry.Z)] = entry.Count;
    }
}
