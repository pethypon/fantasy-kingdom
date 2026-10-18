using System;
using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  SaveSystem.Migration — セーブデータのバージョン間マイグレーション
//
//  新しいセーブ構造に変更するたびに以下の手順を踏む:
//    1. SaveSystem.CurrentSaveVersion をインクリメント
//    2. 新しいフィールドのデフォルト値を GameSaveData に設定
//    3. MigrateVN_to_VN1() を追加（旧データに対する埋め合わせ処理）
//    4. MigrationSteps の末尾に次の段階を追加（既存段階は削除しない）
//
//  これにより旧セーブデータをロードした瞬間に最新フォーマットへ変換され、
//  呼び出し側は常に最新バージョンのデータだけを扱えばよい。
// =====================================================================
public static partial class SaveSystem
{
    /// <summary>
    /// ロード時のマイグレーションエントリポイント。
    /// バージョン0 (未設定) から順に現行バージョンまで migrate する。
    /// 未知の未来バージョンは拒否し、ファイルの保護は保存境界で行う。
    /// </summary>
    static readonly Action<GameSaveData>[] MigrationSteps =
    {
        Migrate_V0_to_V1, Migrate_V1_to_V2, Migrate_V2_to_V3, Migrate_V3_to_V4,
        Migrate_V4_to_V5, Migrate_V5_to_V6, Migrate_V6_to_V7, Migrate_V7_to_V8, Migrate_V8_to_V9
    };
    public static bool ValidateMigrationChain(out string reason)
    {
        if (MigrationSteps.Length != CurrentSaveVersion)
        { reason = "セーブ移行処理のバージョン数が一致しません。"; return false; }
        foreach (var step in MigrationSteps) if (step == null)
        { reason = "セーブ移行処理に欠番があります。"; return false; }
        reason = ""; return true;
    }
    static GameSaveData RunMigrations(GameSaveData data)
    {
        if (data == null || data.Version < 0 || data.Version > CurrentSaveVersion
            || data.SchemaVersion > CurrentSaveVersion || !ValidateMigrationChain(out _)) return null;
        while (data.Version < CurrentSaveVersion)
        {
            MigrationSteps[data.Version](data);
            data.Version++;
        }
        data.SchemaVersion = CurrentSaveVersion;
        return data;
    }

    static void Migrate_V2_to_V3(GameSaveData data)
    {
        if (data.Units == null) return;
        foreach (var unit in data.Units)
            if (unit != null && unit.Kind == "Crystal")
            {
                // Preserve the shipped migration, including the dead-crystal guard and a single application.
                unit.MaxHP = (int)Math.Min(int.MaxValue, (long)unit.MaxHP + 5000);
                if (unit.HP > 0) unit.HP = (int)Math.Min(unit.MaxHP, (long)unit.HP + 5000);
            }
    }
    static void Migrate_V7_to_V8(GameSaveData data)
    {
        // An old save never recorded the pre-reward definition. Do not invent that history.
        if (data.Units == null) return;
        foreach (var unit in data.Units)
            if (unit != null) unit.HasDefinitionStatSnapshot = false;
    }
    static void Migrate_V8_to_V9(GameSaveData data)
    {
        // Old formats did not identify a match. Derive a stable identity from its immutable
        // generation/AI identity, rather than awarding a fresh identity on every load.
        data.MatchId = LegacyMatchId(data);
    }
    static void Migrate_V6_to_V7(GameSaveData data)
    {
        Migrate_V0_to_V1(data);
        data.Checkpoint = PlayerTurnCheckpoint;
        data.HasExplicitMapSnapshot = false;
        data.AI.HasDeterministicSeed = false; // v0-v6 stored threat level in RngSeed, not the real seed.
        data.GameVersion = data.LastSavedVersion = "legacy";
        if (string.IsNullOrEmpty(data.CreatedVersion)) data.CreatedVersion = "legacy";
        if (data.Units == null) data.Units = new List<UnitSaveData>();
        foreach (var unit in data.Units)
            if (unit != null)
            {
                unit.HasMovedThisTurn = unit.FirstSkillUsedThisTurn = unit.DebuffNullifiedThisTurn = unit.InterceptUsedThisTurn = false;
                unit.UpkeepUnpaidTurns = 0;
                unit.BuildingOperationAvailable = true;
                if (unit.MaxHP == 0 && unit.HP >= 0) unit.MaxHP = unit.HP;
                if (unit.AssignedSkillId == 0) unit.AssignedSkillId = -1; // No shipped skill uses zero; old missing field meant none.
                if (unit.ActiveEffects == null) unit.ActiveEffects = new List<EffectSaveData>();
                if (string.IsNullOrEmpty(unit.Direction)) unit.Direction = "N";
                if (string.IsNullOrEmpty(unit.PassiveSkill)) unit.PassiveSkill = "None";
                if (string.IsNullOrEmpty(unit.SpecialAbility)) unit.SpecialAbility = "None";
                if (string.IsNullOrEmpty(unit.FacilityKind)) unit.FacilityKind = "Field";
                foreach (var effect in unit.ActiveEffects)
                    if (effect != null)
                    {
                        if (string.IsNullOrEmpty(effect.DebuffType)) effect.DebuffType = "None";
                        if (string.IsNullOrEmpty(effect.BuffType)) effect.BuffType = "None";
                    }
            }
        if (data.Fog.PlayerExplored == null) data.Fog.PlayerExplored = new List<Vec3IntData>();
        if (data.Fog.EnemyExplored == null) data.Fog.EnemyExplored = new List<Vec3IntData>();
        if (data.PlayerNationExtra.PendingReturns == null) data.PlayerNationExtra.PendingReturns = new List<int>();
        if (data.EnemyNationExtra.PendingReturns == null) data.EnemyNationExtra.PendingReturns = new List<int>();
        // Missing legacy generation settings explicitly keep the historical scene defaults.
        if (data.MapWidth == 0 && data.MapDepth == 0) data.MapWidth = data.MapDepth = 35;
        // Old demolition retained hidden building objects at (-1000,-1000,-1000).
        // These are discarded runtime shells, never placed actors. Keep dead units/crystals and all rule state.
        data.Units.RemoveAll(unit => unit != null && !unit.IsActive && unit.Kind != "Crystal"
            && (unit.Type == "Building" || unit.Type == "Wall")
            && (unit.PosX < 0 || unit.PosZ < 0 || unit.PosX >= data.MapWidth || unit.PosZ >= data.MapDepth));
        if (data.ThreatLevel == 0) data.ThreatLevel = 1;
        if (data.Timer.TurnTimeLimit == 0) data.Timer.TurnTimeLimit = 180;
        if (data.PlayerAP.Reset == 0) data.PlayerAP.Reset = 30;
        if (data.EnemyAP.Reset == 0) data.EnemyAP.Reset = 30;
    }

    // ================================================================
    //  v0 (レガシー: Version フィールド未設定) → v1
    //
    //  v0 は最初期の「Version フィールド自体が存在しない」セーブデータ。
    //  JsonUtility はフィールド欠損を 0 としてパースするため、この分岐で
    //  処理される。
    //
    //  v0 → v1 では以下を保証する:
    //    - Fog / AI / NationExtra など後から追加された子オブジェクトが
    //      null にならないようデフォルト値を注入
    //    - 敵パンデータ（v0 時代は無かった）を 0 として復元
    // ================================================================
    static void Migrate_V3_to_V4(GameSaveData data)
    {
        // Old saves have no AI action history; do not invent experience or rewards.
        if (data.AI == null) data.AI = new AISaveData();
        if (data.PlayerNationExtra == null) data.PlayerNationExtra = new NationExtraSaveData();
        if (data.EnemyNationExtra == null) data.EnemyNationExtra = new NationExtraSaveData();
        int completedRounds = Mathf.Max(0, data.Turn - 1);
        data.PlayerNationExtra.TurnsAlive = completedRounds;
        data.EnemyNationExtra.TurnsAlive = completedRounds;
    }

    static void Migrate_V4_to_V5(GameSaveData data)
    {
        // Old saves start with no exploration history. Preserve explicit nation phases and reflection.
        if (data.AI == null) data.AI = new AISaveData();
    }
    static void Migrate_V5_to_V6(GameSaveData data)
    {
        // Missing operation history starts empty. Existing exploration, reflection and rule phases are preserved.
        if (data.AI == null) data.AI = new AISaveData();
    }

    static void Migrate_V0_to_V1(GameSaveData data)
    {
        if (data.Timer == null) data.Timer = new TimerSaveData
        { TurnTimeLimit = 180, TurnTimeRemaining = 180, PlayerTotalTime = 36000, EnemyTotalTime = 36000 };
        if (data.Fog == null) data.Fog = new FogSaveData();
        if (data.AI == null) data.AI = new AISaveData();
        if (data.PlayerNationExtra == null) data.PlayerNationExtra = new NationExtraSaveData();
        if (data.EnemyNationExtra == null) data.EnemyNationExtra = new NationExtraSaveData();
        if (data.PlayerResources == null) data.PlayerResources = new ResourceSaveData();
        if (data.EnemyResources == null) data.EnemyResources = new ResourceSaveData();
        if (data.PlayerAP == null) data.PlayerAP = new APSaveData();
        if (data.EnemyAP == null) data.EnemyAP = new APSaveData();
    }

    // ================================================================
    //  v1 → v2
    //
    //  UnitSaveData に Experience / SpecialAbility / SurvivalInstinctUsed を
    //  追加し、建築物の MaxHP を保存するようになった。
    //  JsonUtility は欠損フィールドを 0 / "" / false でパースするため
    //  ロード側のガード（空 SpecialAbility はスキップ、MaxHP=0 は HP で補完）
    //  で安全に読める。ここでの変換処理は不要。
    // ================================================================
    static void Migrate_V1_to_V2(GameSaveData data)
    {
        // フィールド追加のみのため変換不要（ドキュメント目的のマイグレータ）
    }
}
