using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// セーブデータの適用（ユニット復元・資源復元・タイマー復元等）を担当する。
/// GameGenerator の ApplyLoadData() から分離された単一責任クラス。
/// </summary>
public static class SaveGameApplier
{
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

        Debug.Log($"[SaveGameApplier] ロード適用: Turn{data.Turn} 脅威度{data.ThreatLevel}");

        // ターン数復元（PlayerStart.Entry()でTurn++されるので1引く）
        turnGen.Context.Turn = data.Turn - 1;

        // 資源復元
        SaveSystem.RestoreResources(data.PlayerResources, factionState.PlayerResources);
        SaveSystem.RestoreResources(data.EnemyResources, factionState.EnemyResources);

        // AP復元
        SaveSystem.RestoreAP(data.PlayerAP, factionState.PlayerAP);
        SaveSystem.RestoreAP(data.EnemyAP, factionState.EnemyAP);

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

        foreach (Status s in unitParent.GetComponentsInChildren<Status>(true))
        {
            for (int i = 0; i < data.Units.Count; i++)
            {
                if (usedIndices.Contains(i)) continue;
                var ud = data.Units[i];

                if (ud.Kind == s.kind.ToString() && ud.Team == s.team.ToString())
                {
                    ApplyStatusFields(s, ud);
                    usedIndices.Add(i);
                    break;
                }
            }
        }
    }

    /// <summary>セーブエントリの全フィールドをStatusに書き戻す</summary>
    static void ApplyStatusFields(Status s, SaveSystem.UnitSaveData ud)
    {
        s.transform.position = new Vector3(ud.PosX, ud.PosY, ud.PosZ);

        s.HP = ud.HP;
        // 旧セーブデータでは建築物の MaxHP が 0 のことがあるため HP で補完する
        s.MaxHP = ud.MaxHP > 0 ? ud.MaxHP : ud.HP;
        s.ATK = ud.ATK;
        s.DEF = ud.DEF;
        s.Level = ud.Level;
        s.Experience = ud.Experience;
        s.ShieldTurns = ud.ShieldTurns;
        s.ShieldActivated = ud.ShieldActivated;
        s.ShieldEverActivated = ud.ShieldEverActivated;
        s.SkillCooldown = ud.SkillCooldown;
        s.AssignedSkillId = ud.AssignedSkillId;
        s.Fatigue = ud.Fatigue;
        s.SurvivalInstinctUsed = ud.SurvivalInstinctUsed;

        if (System.Enum.TryParse<Direction>(ud.Direction, out var dir))
            s.direction = dir;
        if (System.Enum.TryParse<PassiveSkill>(ud.PassiveSkill, out var passive))
            s.passiveskill = passive;
        if (!string.IsNullOrEmpty(ud.SpecialAbility)
            && System.Enum.TryParse<SpecialAbility>(ud.SpecialAbility, out var ability))
            s.specialAbility = ability;

        s.ActiveEffects.Clear();
        foreach (var ef in ud.ActiveEffects)
        {
            if (System.Enum.TryParse<StatusEffectType>(ef.DebuffType, out var debuff)
                && debuff != StatusEffectType.None)
                s.ActiveEffects.Add(new ActiveEffect(debuff, ef.RemainingTurns));
            else if (System.Enum.TryParse<BuffType>(ef.BuffType, out var buff)
                     && buff != BuffType.None)
                s.ActiveEffects.Add(new ActiveEffect(buff, ef.RemainingTurns));
        }

        s.gameObject.SetActive(ud.IsActive);
    }

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

            if (!System.Enum.TryParse<Team>(ud.Team, out var team)) continue;
            if (team != Team.Player && team != Team.Enemy) continue;
            if (!System.Enum.TryParse<Kind>(ud.Kind, out var kind)) continue;

            // クリスタルは CrystalSystem.CrystalCore で復元済みのはず
            if (kind == Kind.Crystal)
            {
                Debug.LogWarning($"[SaveGameApplier] クリスタルのセーブエントリが未マッチです ({ud.Team})");
                continue;
            }

            var worldPos = new Vector3(ud.PosX, ud.PosY, ud.PosZ);
            Status spawned = null;

            if (System.Enum.TryParse<Type>(ud.Type, out var type) && type == Type.Unit)
            {
                if (summonSystem != null)
                    spawned = summonSystem.SpawnUnitForLoad(kind, team, worldPos);
            }
            else if (buildSystem != null
                     && System.Enum.TryParse<FacilityKind>(ud.FacilityKind, out var facility))
            {
                spawned = buildSystem.PlaceBuildingForLoad(GridHelper.ToGrid(worldPos), facility, team);
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
