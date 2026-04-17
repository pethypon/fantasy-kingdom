using System;
using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  SaveSystem.DataTypes — 全 [Serializable] データクラス定義
//  JsonUtility で扱えるよう、plain-field クラス/struct を定義する。
// =====================================================================
public static partial class SaveSystem
{
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
        /// <summary>セーブデータバージョン（互換性管理用）</summary>
        public int Version = CurrentSaveVersion;

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
        public bool ShieldEverActivated;
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
}
