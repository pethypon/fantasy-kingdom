using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Snapshot invariants. Does not mutate runtime state or sanitize invalid current rules.</summary>
public static partial class SaveSystem
{
    public const int MaximumMapDimension = 256, MaximumUnitEntries = 65536;
    public static bool ValidateGameData(GameSaveData data, out string reason)
    {
        reason = "";
        if (data == null || data.Version != CurrentSaveVersion || data.SchemaVersion != CurrentSaveVersion)
            return Invalid("セーブの形式が現在の版と一致しません。", out reason);
        if (data.Checkpoint != PlayerTurnCheckpoint) return Invalid("保存したターンの再開位置に対応していません。", out reason);
        if (!IsValidMatchId(data.MatchId)) return Invalid("試合の識別子が不正です。", out reason);
        if (data.Turn < 1 || data.Turn == int.MaxValue || data.ThreatLevel < 1 || data.ThreatLevel > 100)
            return Invalid("ターン数または脅威度が不正です。", out reason);
        if (data.MapWidth < 1 || data.MapDepth < 1 || data.MapWidth > MaximumMapDimension || data.MapDepth > MaximumMapDimension)
            return Invalid("マップの大きさが不正です。", out reason);
        if (!Finite(data.MapSeedX) || !Finite(data.MapSeedZ)
            || data.HasMapGenerationSettings && (!Finite(data.MapNoiseScale) || data.MapNoiseScale < 0
                || !Finite(data.MapRiverHalfWidth) || data.MapRiverHalfWidth < 0 || data.MapRiverHalfWidth > 3))
            return Invalid("地形の生成設定が不正です。", out reason);
        if (data.HasExplicitMapSnapshot && (data.MapMaxY < 1 || data.MapMaxY > 1024 || data.MapMinY < -1024
            || data.MapMinY >= data.MapMaxY || data.MapAmplitude < 0 || data.MapAmplitude > 1000000
            || data.MapAverageFoundation < -1000000 || data.MapAverageFoundation > 1000000))
            return Invalid("地形の高さ設定が対応範囲外です。", out reason);
        if (!Position(data.PCPx, data.PCPy, data.PCPz, data) || !Position(data.ECPx, data.ECPy, data.ECPz, data))
            return Invalid("クリスタルの座標がマップの範囲外です。", out reason);
        if (!ResourcesValid(data.PlayerResources) || !ResourcesValid(data.EnemyResources)) return Invalid("資源量に不正な値があります。", out reason);
        if (!APValid(data.PlayerAP) || !APValid(data.EnemyAP)) return Invalid("行動ポイントに不正な値があります。", out reason);
        if (data.Timer == null || !Seconds(data.Timer.TurnTimeRemaining) || !Seconds(data.Timer.PlayerTotalTime)
            || !Seconds(data.Timer.EnemyTotalTime) || !Seconds(data.Timer.TurnTimeLimit))
            return Invalid("タイマーの保存値が不正です。", out reason);
        if (!NationValid(data.PlayerNationExtra) || !NationValid(data.EnemyNationExtra)) return Invalid("国の保存値が不正です。", out reason);
        if (data.PlayerSubCrystals < 0 || data.EnemySubCrystals < 0 || data.PlayerSubCrystals > GameConstants.InitialSubCrystals
            || data.EnemySubCrystals > GameConstants.InitialSubCrystals) return Invalid("サブクリスタル数が不正です。", out reason);
        if (data.Units == null || data.Units.Count > MaximumUnitEntries) return Invalid("駒の保存件数が不正です。", out reason);
        var lifeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var unit in data.Units)
            if (!UnitValid(unit, data, lifeIds, out reason)) return false;
        if (data.Fog == null || !CellsValid(data.Fog.PlayerExplored, data) || !CellsValid(data.Fog.EnemyExplored, data))
            return Invalid("探索済みのマスが不正です。", out reason);
        if (data.LandOverrides != null)
        {
            if (data.LandOverrides.Count > data.MapWidth * data.MapDepth) return Invalid("地形変更の保存件数が不正です。", out reason);
            foreach (var cell in data.LandOverrides) if (!Cell(cell.x, cell.z, data)) return Invalid("地形変更の座標が不正です。", out reason);
        }
        if (data.AI == null || data.AI.AITurnCount < 0 || data.AI.AITurnCount == int.MaxValue
            || data.AI.HasDeterministicSeed && (data.AI.RngSeed < 0 || data.AI.RandomAlgorithm != "system-random-v1"))
            return Invalid("AI の乱数またはターン情報が不正です。", out reason);
        var enemyTerritory = data.AI.HasTerritoryIntelSnapshot ? data.AI.TerritoryIntel : null;
        var playerTerritory = data.HasPlayerTerritoryIntelSnapshot ? data.PlayerTerritoryIntel : null;
        if (data.AI.HasTerritoryIntelSnapshot && enemyTerritory == null || data.HasPlayerTerritoryIntelSnapshot && playerTerritory == null
            || !EnemyTerritoryIntel.IsValidState(enemyTerritory, data.MapWidth, data.MapDepth, Team.Enemy)
            || !EnemyTerritoryIntel.IsValidState(playerTerritory, data.MapWidth, data.MapDepth, Team.Player)
            || !TerritoryClockValid(enemyTerritory, data.AI.AITurnCount)
            || !TerritoryClockValid(playerTerritory, data.Turn))
            return Invalid("AI の敵領土観測記憶が不正です。", out reason);
        if (!string.IsNullOrEmpty(data.AI.MajorPersonality) && !EnumName<MajorPersonality>(data.AI.MajorPersonality)
            || !string.IsNullOrEmpty(data.AI.CurrentStrategy) && !EnumName<TurnStrategy>(data.AI.CurrentStrategy))
            return Invalid("AI の性格または戦略が不明です。", out reason);
        long traitTotal = (long)data.AI.TraitCaution + data.AI.TraitCommand + data.AI.TraitObsession
            + data.AI.TraitDefense + data.AI.TraitTactics + data.AI.TraitDevelopment;
        if (data.AI.TraitCaution < 0 || data.AI.TraitCommand < 0 || data.AI.TraitObsession < 0 || data.AI.TraitDefense < 0
            || data.AI.TraitTactics < 0 || data.AI.TraitDevelopment < 0 || traitTotal != 0 && traitTotal != 300)
            return Invalid("AI の性格配分が不正です。", out reason);
        if (!Finite(data.AI.LearningCaution) || !Finite(data.AI.LearningCommand) || !Finite(data.AI.LearningTactics)
            || !Finite(data.AI.LearningDefense) || !Finite(data.AI.LearningDevelop) || !Finite(data.AI.MLAverageLoss))
            return Invalid("AI の学習値が不正です。", out reason);
        if (data.Dungeons != null)
        {
            if (data.Dungeons.Count > data.MapWidth * data.MapDepth) return Invalid("ダンジョン件数が不正です。", out reason);
            foreach (var dungeon in data.Dungeons)
                if (dungeon == null || !Cell(dungeon.Position.x, dungeon.Position.z, data)
                    || !Enum.IsDefined(typeof(Team), dungeon.ClaimingTeam) || !Enum.IsDefined(typeof(DungeonSystem.Artifact), dungeon.Reward)
                    || dungeon.SharedRemainingTurns < 0 || dungeon.SharedRemainingTurns > DungeonSystem.ClaimTurns)
                    return Invalid("ダンジョンの保存値が不正です。", out reason);
        }
        if (data.IndependentUnits != null)
        {
            if (data.IndependentUnits.Count > MaximumUnitEntries) return Invalid("第三陣営の保存件数が不正です。", out reason);
            foreach (var actor in data.IndependentUnits)
                if (actor == null || !Enum.IsDefined(typeof(Team), actor.Team) || actor.Unit == null
                    || !UnitValid(actor.Unit, data, lifeIds, out reason)) return Invalid("第三陣営の駒が不正です。 " + reason, out reason);
        }
        return true;
    }
    static bool TerritoryClockValid(EnemyTerritoryIntelState state, int turn)
        => state == null || state.FirstObservedTurn <= turn && state.LastConfirmedTurn <= turn
            && state.LastProgressTurn <= turn && state.PausedTurn <= turn && state.LastObservationTurn <= turn;

    static bool UnitValid(UnitSaveData unit, GameSaveData data, HashSet<string> lifeIds, out string reason)
    {
        reason = "";
        if (unit == null || !EnumName<Kind>(unit.Kind) || !EnumName<Team>(unit.Team) || !EnumName<Type>(unit.Type)
            || !EnumName<Direction>(unit.Direction) || !EnumName<PassiveSkill>(unit.PassiveSkill)
            || !string.IsNullOrEmpty(unit.SpecialAbility) && !EnumName<SpecialAbility>(unit.SpecialAbility)
            || !EnumName<FacilityKind>(unit.FacilityKind)) return Invalid("駒の種類・陣営・向き・能力が不明です。", out reason);
        if (!StableSaveKeys.TryParse<Type>(unit.Type, out var type) || type == Type.MovePoint || type == Type.AttackPoint)
            return Invalid("表示専用のマスは駒として保存できません。", out reason);
        if (!Position(unit.PosX, unit.PosY, unit.PosZ, data) || unit.HP < 0 || unit.MaxHP < 0 || unit.HP > unit.MaxHP
            || unit.ATK < 0 || unit.DEF < 0 || unit.Level < 0 || unit.ShieldTurns < 0 || unit.SkillCooldown < 0
            || unit.Fatigue < 0 || unit.Experience < 0 || unit.UpkeepUnpaidTurns < 0) return Invalid("駒の座標または能力値が不正です: " + unit.Kind
                + " (位置 " + unit.PosX + "," + unit.PosY + "," + unit.PosZ + "; HP " + unit.HP + "/" + unit.MaxHP + ")", out reason);
        if (unit.FixedStatsRevision < 0 || unit.FixedStatsRevision > 2 || unit.LastMaintenanceRound < -1
            || type == Type.Unit && (unit.Level != 1 || unit.Experience != 0))
            return Invalid("固定能力値または維持費処理の保存形式が不正です。", out reason);
        var legacyStats = unit.LegacyUnitStats;
        if (legacyStats != null && (legacyStats.Level < 0 || legacyStats.Experience < 0 || legacyStats.HP < 0
            || legacyStats.MaximumHP < legacyStats.HP || legacyStats.ATK < 0 || legacyStats.DEF < 0
            || legacyStats.HasDefinitionBaseline && (legacyStats.DefinitionMaxHP < 1 || legacyStats.DefinitionATK < 0 || legacyStats.DefinitionDEF < 0)))
            return Invalid("旧ユニット能力値の退避データが不正です。", out reason);
        if (!string.IsNullOrEmpty(unit.ReflectionLifeId) && (unit.ReflectionLifeId.Length > 64 || !lifeIds.Add(unit.ReflectionLifeId)))
            return Invalid("駒の永続識別子が重複、または不正です。", out reason);
        if (unit.HasBarrierSnapshot && (unit.Barrier < 0 || unit.MaxBarrier < 0 || unit.Barrier > unit.MaxBarrier)) return Invalid("バリアが不正です", out reason);
        if (unit.Kind == StableSaveKeys.Key(Kind.Crystal) && (unit.MainCrystalRuleVersion < 0
            || unit.MainCrystalRuleVersion > MainCrystalRules.Version
            || unit.MainCrystalCounter != null && !unit.MainCrystalCounter.IsValidForRound(data.Turn)))
            return Invalid("メインクリスタルの反撃予約またはルール形式が不正です。", out reason);
        if (unit.HasDefinitionStatSnapshot && (unit.DefinitionMaxHP < 1 || unit.DefinitionATK < 0 || unit.DefinitionDEF < 0))
            return Invalid("駒の設定値スナップショットが不正です。", out reason);
        if (!string.IsNullOrEmpty(unit.DefinitionId) && UnitAuthoringCatalog.Load()?.GetById(unit.DefinitionId) == null)
            return Invalid("駒の定義が見つかりません: " + unit.DefinitionId, out reason);
        if (!string.IsNullOrEmpty(unit.AuthoredFacilityId) && FacilityAuthoringCatalog.Loaded?.Find(unit.AuthoredFacilityId) == null)
            return Invalid("建物の定義が見つかりません: " + unit.AuthoredFacilityId, out reason);
        if (unit.AssignedSkillId >= 0 && !SkillData.Table.ContainsKey(unit.AssignedSkillId))
            return Invalid("駒のスキルが見つかりません: " + unit.AssignedSkillId, out reason);
        if (unit.ActiveEffects == null || unit.ActiveEffects.Count > 256) return Invalid("状態効果の保存値が不正です。", out reason);
        foreach (var effect in unit.ActiveEffects)
            if (effect == null || effect.RemainingTurns < 0 || !EnumName<StatusEffectType>(effect.DebuffType) || !EnumName<BuffType>(effect.BuffType))
                return Invalid("状態効果の種類が不明です。", out reason);
        return true;
    }
    static bool ValidateProfileData(ProfileData data, out string reason)
    {
        reason = "";
        if (data == null || data.SchemaVersion < 0 || data.SchemaVersion > CurrentProfileVersion || data.ThreatLevel < 1 || data.ThreatLevel > 100
            || data.TotalWins < 0 || data.TotalLosses < 0 || data.MLTotalMatchesTrained < 0 || data.MLTotalTrainingSteps < 0
            || data.MatchHistory == null || data.MatchHistory.Count > 100000)
            return Invalid("プロファイルの保存値が不正です。", out reason);
        foreach (var match in data.MatchHistory)
            if (match == null || match.TurnsPlayed < 0 || match.PrimaryFailure != null && match.PrimaryFailure.Length > 4096)
                return Invalid("試合履歴が不正です。", out reason);
        if (data.CompletedMatchIds == null || data.CompletedMatchIds.Count > MaximumCompletedMatches)
            return Invalid("試合の完了記録が不正です。", out reason);
        var completed = new HashSet<string>(StringComparer.Ordinal);
        foreach (string matchId in data.CompletedMatchIds)
            if (!IsValidMatchId(matchId) || !completed.Add(matchId)) return Invalid("試合の完了記録が重複、または不正です。", out reason);
        return true;
    }
    static bool ResourcesValid(ResourceSaveData r) => r != null && r.Wood >= 0 && r.Stone >= 0 && r.Water >= 0
        && r.MagicOre >= 0 && r.Iron >= 0 && r.Wheat >= 0 && r.Bread >= 0 && r.Citizen >= 0;
    static bool APValid(APSaveData a) => a != null && a.Current >= 0 && a.Current <= GameConstants.MaxAP
        && a.Reset >= 3 && a.Reset <= GameConstants.MaxAP && a.Plus >= 0 && a.Plus <= GameConstants.MaxAP
        && a.Minus >= 0 && a.Minus <= GameConstants.MaxAP;
    static bool NationValid(NationExtraSaveData n) => n != null && n.TurnsAlive >= 0 && n.StarvationCounter >= 0
        && n.CitizenCapacity >= 0 && n.ResourceCapacity >= 0 && n.BarracksXP >= 0 && n.PendingReturns != null && n.PendingReturns.Count <= 4096;
    static bool CellsValid(List<Vec3IntData> cells, GameSaveData data)
    {
        if (cells == null || cells.Count > data.MapWidth * data.MapDepth) return false;
        foreach (var cell in cells) if (cell == null || !Cell(cell.X, cell.Z, data)) return false;
        return true;
    }
    static bool Cell(int x, int z, GameSaveData data) => x >= 0 && z >= 0 && x < data.MapWidth && z < data.MapDepth;
    static bool Position(float x, float y, float z, GameSaveData data) => Finite(x) && Finite(y) && Finite(z)
        && x >= 0 && z >= 0 && x < data.MapWidth && z < data.MapDepth && y >= -1024 && y <= 1024;
    static bool EnumName<T>(string value) where T : struct, Enum => StableSaveKeys.TryParse<T>(value, out _);
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool Seconds(float value) => Finite(value) && value >= 0;
    static bool Invalid(string message, out string reason) { reason = message; return false; }
}
