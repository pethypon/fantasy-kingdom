using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// セーブデータの適用（ユニット復元・資源復元・タイマー復元等）を担当する。
/// GameGenerator の ApplyLoadData() から分離された単一責任クラス。
/// </summary>
public static class SaveGameApplier
{
    // Old saves allowed units on rivers. Relocate only blocked units, preserving occupied cells.
    public static void RelocateBlockedUnits(MapCreate map)
    {
        if(map == null) return;
        var actors = new List<Status>(); CombatRegistry.Collect(actors);
        var occupied = new HashSet<Vector3Int>();
        foreach(var actor in actors) occupied.Add(GridHelper.ToGridXZ(actor.transform.position));
        foreach(var actor in actors) {
            if(actor.type != Type.Unit) continue;
            var cell=GridHelper.ToGridXZ(actor.transform.position);
            if(!map.IsRiver(cell.x,cell.z) && !map.IsHighMountain(cell.x,cell.z)) continue;
            float distance=float.MaxValue; Vector3 best=actor.transform.position; bool found=false;
            foreach(var candidate in map.SetPos) {
                if(occupied.Contains(GridHelper.ToGridXZ(candidate))) continue;
                float d=((Vector3)candidate-actor.transform.position).sqrMagnitude;
                if(d<distance) {distance=d;best=candidate;found=true;}
            }
            if(found) {actor.transform.position=best;occupied.Add(GridHelper.ToGridXZ(best));}
            else Debug.LogWarning("[SaveGameApplier] No free land for saved unit: " + actor.kind);
        }
    }

    /// <summary>ロードデータを全システムに適用する。</summary>
    public static void Apply(
        SaveSystem.GameSaveData data,
        FactionState factionState,
        TurnGenerator turnGen,
        UnitSetting unitSetting,
        CrystalSystem crystalSystem,
        BuildSystem buildSystem,
        MoveGenerator moveGenerator,
        VisionGenerator visionGenerator,
        MapCreate mapCreate,
        SummonSystem summonSystem = null)
    {
        if (data == null)
        {
            Debug.LogError("[SaveGameApplier] ロードデータが null です");
            return;
        }
        if (factionState == null)
        {
            Debug.LogError("[SaveGameApplier] FactionState が null です");
            return;
        }
        if (!SaveSystem.ValidateGameData(data, out string validationFailure))
            throw new System.ArgumentException("セーブを適用できません: " + validationFailure, nameof(data));
        turnGen.RestoreMatchId(data.MatchId);
        using var presentationRestore = BoardActorAnimation.SuppressEffects();

        Debug.Log($"[SaveGameApplier] ロード適用: Turn{data.Turn} 脅威度{data.ThreatLevel}");
        mapCreate?.Environment.Restore(data.Environment);

        // ターン数復元（PlayerStart.Entry()でTurn++されるので1引く）
        turnGen.Context.Turn = data.Turn - 1;
        turnGen.Context.ResumeSavedPlayerTurn = true;

        // 資源復元
        SaveSystem.RestoreResources(data.PlayerResources, factionState.PlayerResources);
        SaveSystem.RestoreResources(data.EnemyResources, factionState.EnemyResources);

        // AP復元
        SaveSystem.RestoreAP(data.PlayerAP, factionState.PlayerAP);
        SaveSystem.RestoreAP(data.EnemyAP, factionState.EnemyAP);
        factionState.UpdateCitizenAPBonus(Team.Player);
        factionState.UpdateCitizenAPBonus(Team.Enemy);

        // サブクリスタル復元
        factionState.PlayerSubCrystals = data.PlayerSubCrystals;
        factionState.EnemySubCrystals = data.EnemySubCrystals;

        // NationState追加データ復元
        SaveSystem.RestoreNationExtra(data.PlayerNationExtra, factionState.PlayerNation);
        SaveSystem.RestoreNationExtra(data.EnemyNationExtra, factionState.EnemyNation);

        // ユニット・クリスタル・建築物復元
        // usedIndices を全親で共有し、同一セーブエントリの二重適用を防ぐ
        var usedIndices = new HashSet<int>();
        ApplyUnitLoadData(data, unitSetting.PlayerUnit, usedIndices);
        ApplyUnitLoadData(data, unitSetting.EnemyUnit, usedIndices);

        if (crystalSystem != null)
        {
            ApplyUnitLoadData(data, crystalSystem.Playercrystal, usedIndices);
            ApplyUnitLoadData(data, crystalSystem.Enemycrystal, usedIndices);
        }

        if (buildSystem != null)
        {
            ApplyUnitLoadData(data, buildSystem.PlayerBuildingParent, usedIndices);
            ApplyUnitLoadData(data, buildSystem.EnemyBuildingParent, usedIndices);
        }

        // シーンに存在しない駒（召喚ユニット・建築物）をセーブデータから再生成
        SpawnUnmatchedEntries(data, usedIndices, summonSystem, buildSystem);

        turnGen.Systems.DungeonSystem?.RestoreDungeons(data.Dungeons, data.DungeonLastRound);
        turnGen.Systems.NeutralFactionSystem?.Restore(data.IndependentUnits, data.PreviousMonsterCount,
            data.IndependentLastRound, data.SpawnedIntruders);
        turnGen.Systems.WildBossSystem?.Restore(data.WildBoss);
        turnGen.Systems.ThirdFactionSystem?.Restore(data.ThirdFaction);
        turnGen.MarkDeveloperMatch(data.DeveloperAutoplayUsed);
        BoardActorPresentation.ApplyAll();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        turnGen.PlayerAI?.RestoreUsedFlag(data.DeveloperAutoplayUsed);
#endif
        RelocateBlockedUnits(mapCreate);
        SpecialSkillRuntime.RestoreLinks();
        UniqueRewardSystem.Records.Clear();
        if (data.Rewards != null) UniqueRewardSystem.Records.AddRange(data.Rewards);

        // 再生成された駒を UnitRegistry に反映
        if (UnitRegistry.Instance != null && unitSetting != null)
        {
            UnitRegistry.Instance.ScanAndRegister(
                unitSetting.PlayerUnit, unitSetting.EnemyUnit,
                buildSystem != null ? buildSystem.PlayerBuildingParent : null,
                buildSystem != null ? buildSystem.EnemyBuildingParent : null);
        }

        // タイマー復元
        if (turnGen.Systems.TimerSystem != null)
            SaveSystem.RestoreTimer(data.Timer, turnGen.Systems.TimerSystem);

        // 霧の戦争（探索済み）復元
        if (visionGenerator != null)
            SaveSystem.RestoreFog(data.Fog, visionGenerator);

        // AI状態復元
        if (turnGen.Systems.AICommander != null)
            SaveSystem.RestoreAICommander(data.AI, turnGen.Systems.AICommander);

        // 占有セル・視界を再計算（ユニット位置を復元したため dirty を立てて確実に再計算）
        if (moveGenerator != null) moveGenerator.UnitPointCore();
        if (visionGenerator != null && mapCreate != null && moveGenerator != null)
        {
            visionGenerator.MarkVisionDirty();
            visionGenerator.VisionPoint(mapCreate, moveGenerator, crystalSystem);
        }

        ToastMessageUI.Show("セーブデータをロードしました", ToastMessageUI.MessageType.Info, 3f);
    }

    /// <summary>ユニット親以下のStatusにセーブデータを適用する（マッチ済みインデックスは全親で共有）</summary>
    public static void ApplyUnitLoadData(SaveSystem.GameSaveData data, Transform unitParent, HashSet<int> usedIndices)
    {
        if (unitParent == null || data == null || usedIndices == null) return;

        var actors = unitParent.GetComponentsInChildren<Status>(true);
        // Reserve starting slots before ordinary actors can consume their records.
        // The role survives a replacement/retirement of its old authored definition.
        foreach (Status actor in actors)
        {
            if (!actor.IsPrimaryCommander || actor.type != Type.Unit) continue;
            int index = FindCommanderSave(data, actor, usedIndices);
            if (index < 0) continue;
            ApplyStatusFields(actor, data.Units[index]);
            usedIndices.Add(index);
        }

        foreach (Status s in actors)
        {
            if (s.IsPrimaryCommander && s.type == Type.Unit) continue;
            for (int i = 0; i < data.Units.Count; i++)
            {
                if (usedIndices.Contains(i)) continue;
                var ud = data.Units[i];
                if (ud.HasPrimaryCommanderRole && ud.IsPrimaryCommander) continue;

                bool sameDefinition = string.IsNullOrEmpty(ud.DefinitionId)
                    ? true
                    : string.Equals(ud.DefinitionId, s.unitDefinitionId, System.StringComparison.Ordinal);
                if (!string.IsNullOrEmpty(ud.AuthoredFacilityId))
                    sameDefinition &= string.Equals(ud.AuthoredFacilityId,
                        s.AuthoredFacility != null ? s.AuthoredFacility.definitionId : s.authoredFacilityId,
                        System.StringComparison.Ordinal);
                if (sameDefinition && (!ud.HasPrimaryCommanderRole || ud.IsPrimaryCommander == s.IsPrimaryCommander)
                    && ud.Kind == StableSaveKeys.Key(s.kind) && ud.Team == StableSaveKeys.Key(s.team))
                {
                    ApplyStatusFields(s, ud);
                    usedIndices.Add(i);
                    break;
                }
            }
        }
    }

    static int FindCommanderSave(SaveSystem.GameSaveData data, Status actor, HashSet<int> usedIndices)
    {
        string team = StableSaveKeys.Key(actor.team);
        if (actor.team != Team.Player && actor.team != Team.Enemy) return -1;
        for (int i = 0; i < data.Units.Count; i++)
        {
            var saved = data.Units[i];
            if (!usedIndices.Contains(i) && saved.HasPrimaryCommanderRole && saved.IsPrimaryCommander
                && saved.Type == StableSaveKeys.Key(Type.Unit) && saved.Team == team) return i;
        }

        // Historical CollectUnits always wrote the initial commander first for each
        // side, including inactive initial slots. Never infer a role from a later Boss.
        for (int i = 0; i < data.Units.Count; i++)
        {
            var saved = data.Units[i];
            if (saved.Team != team || saved.Type != StableSaveKeys.Key(Type.Unit)) continue;
            if (usedIndices.Contains(i) || saved.HasPrimaryCommanderRole
                || saved.Kind != StableSaveKeys.Key(actor.kind)) return -1;
            if (string.IsNullOrEmpty(saved.DefinitionId)) return i;
            var catalog = UnitAuthoringCatalog.Load();
            return IsLegacyCommanderDefinition(catalog?.units, saved.DefinitionId)
                || IsLegacyCommanderDefinition(catalog?.retiredUnits, saved.DefinitionId) ? i : -1;
        }
        return -1;
    }

    static bool IsLegacyCommanderDefinition(List<UnitAuthoringCatalog.Entry> entries, string id)
    {
        if (entries == null) return false;
        foreach (var entry in entries)
            if (entry != null && !entry.standaloneDefinition && entry.data != null
                && string.Equals(entry.data.definitionId, id, System.StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>セーブエントリの全フィールドをStatusに書き戻す</summary>
    public static void ApplyStatusFields(Status s, SaveSystem.UnitSaveData ud)
    {
        s.RestoreReflectionLifeId(ud.ReflectionLifeId);
        s.Special = SpecialSkillRuntime.CopyState(ud.Special);
        // Missing role fields in legacy saves retain only the matched initial-slot role.
        if (ud.HasPrimaryCommanderRole) s.IsPrimaryCommander = ud.IsPrimaryCommander;
        if (s.type == Type.Building || s.type == Type.Wall)
        {
            s.authoredFacilityId = ud.AuthoredFacilityId;
            s.AuthoredFacility = FacilityAuthoringCatalog.Loaded?.Find(ud.AuthoredFacilityId);
            if (s.AuthoredFacility != null) s.facilityKind = s.AuthoredFacility.behaviourKind;
        }
        // A matched initial slot follows today's commander settings. Ordinary units
        // retain their specific definition, including retired standalone definitions.
        if (s.type == Type.Unit && !string.IsNullOrEmpty(ud.DefinitionId)
            && !(s.IsPrimaryCommander && s.GrowthData != null && s.GrowthData.IsValidForAuthoring))
        {
            var definition = UnitAuthoringCatalog.Load()?.GetById(ud.DefinitionId);
            if (definition != null) s.GrowthData = definition;
            s.unitDefinitionId = ud.DefinitionId;
        }
        s.transform.position = new Vector3(ud.PosX, ud.PosY, ud.PosZ);

        s.HP = ud.HP;
        // 旧セーブデータでは建築物の MaxHP が 0 のことがあるため HP で補完する
        s.MaxHP = ud.MaxHP > 0 ? ud.MaxHP : ud.HP;
        s.ATK = ud.ATK;
        s.DEF = ud.DEF;
        s.Level = s.type == Type.Unit ? Mathf.Clamp(ud.Level, 1, GameConstants.MaxUnitLevel) : ud.Level;
        s.Experience = ud.Experience;
        ApplyLatestDefinitionStats(s, ud);
        StatusBalanceRefresh.RecordDefinitionBaseline(s);
        s.ShieldTurns = ud.ShieldTurns;
        s.ShieldActivated = ud.ShieldActivated;
        s.ShieldEverActivated = ud.ShieldEverActivated;
        s.SkillCooldown = ud.SkillCooldown;
        s.HasMovedThisTurn = ud.HasMovedThisTurn;
        s.FirstSkillUsedThisTurn = ud.FirstSkillUsedThisTurn;
        s.DebuffNullifiedThisTurn = ud.DebuffNullifiedThisTurn;
        s.InterceptUsedThisTurn = ud.InterceptUsedThisTurn;
        if (s.GrowthData != null && s.GrowthData.useAuthoredAbilities)
            s.AssignedSkillId = SkillData.Table.ContainsKey(ud.AssignedSkillId) ? ud.AssignedSkillId : -1;
        else SkillData.AssignFixedSkill(s);
        s.Fatigue = ud.Fatigue;
        s.UpkeepUnpaidTurns = ud.UpkeepUnpaidTurns;
        s.BuildingOperationAvailable = ud.BuildingOperationAvailable;
        s.SurvivalInstinctUsed = ud.SurvivalInstinctUsed;

        if (StableSaveKeys.TryParse<Direction>(ud.Direction, out var dir))
        {
            s.direction = dir;
            BoardActorAnimation.NotifyFacing(s);
        }
        if (StableSaveKeys.TryParse<PassiveSkill>(ud.PassiveSkill, out var passive))
            s.passiveskill = passive;
        if (!string.IsNullOrEmpty(ud.SpecialAbility)
            && StableSaveKeys.TryParse<SpecialAbility>(ud.SpecialAbility, out var ability))
            s.specialAbility = ability;

        s.ActiveEffects.Clear();
        foreach (var ef in ud.ActiveEffects)
        {
            if (StableSaveKeys.TryParse<StatusEffectType>(ef.DebuffType, out var debuff)
                && debuff != StatusEffectType.None)
                s.ActiveEffects.Add(new ActiveEffect(debuff, ef.RemainingTurns));
            else if (StableSaveKeys.TryParse<BuffType>(ef.BuffType, out var buff)
                     && buff != BuffType.None)
                s.ActiveEffects.Add(new ActiveEffect(buff, ef.RemainingTurns));
        }

        s.gameObject.SetActive(ud.IsActive);
        if (s.type == Type.Unit && s.GrowthData != null) s.GrowthData.ApplyAbilityIdentity(s);
        // HP/balance restoration is not a new hit. Restore the saved clock only after all HP writes.
        s.NaturalRecovery = NaturalRecoverySystem.CopyState(ud.NaturalRecovery);
    }

    // Use current formal settings, retaining runtime rewards as a delta from the saved baseline.
    // v0-v7 lack that baseline: preserve their exact stats rather than guessing away earned bonuses.
    static void ApplyLatestDefinitionStats(Status actor, SaveSystem.UnitSaveData saved)
    {
        if (!saved.HasDefinitionStatSnapshot || !StatusBalanceRefresh.TryGetDefinitionStats(actor,
            out int definitionHP, out int definitionATK, out int definitionDEF)) return;
        int oldMaxHP = saved.MaxHP > 0 ? saved.MaxHP : saved.HP;
        actor.MaxHP = AddDefinitionDelta(definitionHP, oldMaxHP, saved.DefinitionMaxHP, 1);
        actor.ATK = AddDefinitionDelta(definitionATK, saved.ATK, saved.DefinitionATK, 0);
        actor.DEF = AddDefinitionDelta(definitionDEF, saved.DEF, saved.DefinitionDEF, 0);
        actor.HP = saved.HP <= 0 ? 0 : Mathf.Clamp((int)System.Math.Min(int.MaxValue,
            System.Math.Round((double)saved.HP / System.Math.Max(1, oldMaxHP) * actor.MaxHP)), 1, actor.MaxHP);
    }
    static int AddDefinitionDelta(int current, int saved, int baseline, int minimum)
        => (int)System.Math.Max(minimum, System.Math.Min(int.MaxValue, (long)current + saved - baseline));

    /// <summary>
    /// シーン上の既存駒にマッチしなかったセーブエントリ（召喚ユニット・建築物）を再生成する。
    /// 初期配置は King / StrangeKing / クリスタルのみのため、召喚・建築で増えた駒はここで復元する。
    /// </summary>
    static void SpawnUnmatchedEntries(
        SaveSystem.GameSaveData data, HashSet<int> usedIndices,
        SummonSystem summonSystem, BuildSystem buildSystem)
    {
        for (int i = 0; i < data.Units.Count; i++)
        {
            if (usedIndices.Contains(i)) continue;
            var ud = data.Units[i];

            // 撃破済み（非アクティブ）の駒は再生成不要
            if (!ud.IsActive) continue;

            if (!StableSaveKeys.TryParse<Team>(ud.Team, out var team)) continue;
            if (team != Team.Player && team != Team.Enemy) continue;
            if (!StableSaveKeys.TryParse<Kind>(ud.Kind, out var kind)) continue;

            // クリスタルは CrystalSystem.CrystalCore で復元済みのはず
            if (kind == Kind.Crystal)
            {
                Debug.LogWarning($"[SaveGameApplier] クリスタルのセーブエントリが未マッチです ({ud.Team})");
                continue;
            }

            var worldPos = new Vector3(ud.PosX, ud.PosY, ud.PosZ);
            Status spawned = null;

            if (StableSaveKeys.TryParse<Type>(ud.Type, out var type) && type == Type.Unit)
            {
                if (summonSystem != null)
                    spawned = summonSystem.SpawnUnitForLoad(kind, team, worldPos, ud.DefinitionId);
            }
            else if (buildSystem != null
                     && StableSaveKeys.TryParse<FacilityKind>(ud.FacilityKind, out var facility))
            {
                spawned = buildSystem.PlaceBuildingForLoad(GridHelper.ToGrid(worldPos), facility, team, ud.AuthoredFacilityId);
            }

            if (spawned != null)
            {
                ApplyStatusFields(spawned, ud);
                usedIndices.Add(i);
                Debug.Log($"[SaveGameApplier] 再生成: {ud.Kind} ({ud.Team}) at ({ud.PosX}, {ud.PosY}, {ud.PosZ})");
            }
            else
            {
                Debug.LogWarning($"[SaveGameApplier] 再生成失敗: {ud.Kind} ({ud.Team}) Type={ud.Type} Facility={ud.FacilityKind}");
            }
        }
    }
}
