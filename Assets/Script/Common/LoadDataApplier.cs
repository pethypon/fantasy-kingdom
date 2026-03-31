using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// セーブデータをゲーム状態に適用する責務を持つ静的クラス。
/// GameGerater の ApplyLoadData を抽出して単一責任を実現。
/// </summary>
public static class LoadDataApplier
{
    public static void Apply(
        SaveSystem.GameSaveData data,
        FactionState factionState,
        TurnGenerater turnGen,
        UnitSetting unitSetting,
        CrystalSystem crystalSystem,
        BuildSystem buildSystem,
        MoveGererater moveGen,
        VisionGenerater visionGen,
        MapCreate mapCreate)
    {
        if (data == null) return;

        Debug.Log($"[LoadDataApplier] ロード適用: Turn{data.Turn} 脅威度{data.ThreatLevel}");

        // ターン数復元（PlayerStart.Entry()でTurn++されるので1引く）
        turnGen.Turn = data.Turn - 1;

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

        // ユニットのステータス・位置を復元
        ApplyUnitLoadData(data, unitSetting.PlayerUnit);
        ApplyUnitLoadData(data, unitSetting.EnemyUnit);

        // クリスタルのステータス復元
        ApplyUnitLoadData(data, crystalSystem.Playercrystal);
        ApplyUnitLoadData(data, crystalSystem.Enemycrystal);

        // 建築物のステータス復元
        if (buildSystem != null)
        {
            ApplyUnitLoadData(data, buildSystem.PlayerBuildingParent);
            ApplyUnitLoadData(data, buildSystem.EnemyBuildingParent);
        }

        // タイマー復元
        if (turnGen.timerSystem != null)
            SaveSystem.RestoreTimer(data.Timer, turnGen.timerSystem);

        // 霧の戦争（探索済み）復元
        if (turnGen.visiongenerater != null)
            SaveSystem.RestoreFog(data.Fog, turnGen.visiongenerater);

        // AI状態復元
        if (turnGen.aiCommander != null)
            SaveSystem.RestoreAICommander(data.AI, turnGen.aiCommander);

        // 占有セル・視界を再計算
        moveGen.UnitPointCore();
        visionGen.VisionPoint(mapCreate, moveGen, crystalSystem);

        ToastMessageUI.Show("セーブデータをロードしました", ToastMessageUI.MessageType.Info, 3f);
    }

    private static void ApplyUnitLoadData(SaveSystem.GameSaveData data, Transform unitParent)
    {
        if (unitParent == null) return;

        var usedIndices = new HashSet<int>();

        foreach (Status s in unitParent.GetComponentsInChildren<Status>(true))
        {
            for (int i = 0; i < data.Units.Count; i++)
            {
                if (usedIndices.Contains(i)) continue;
                var ud = data.Units[i];

                if (ud.Kind == s.kind.ToString() && ud.Team == s.team.ToString())
                {
                    s.transform.position = new Vector3(ud.PosX, ud.PosY, ud.PosZ);
                    s.HP = ud.HP;
                    s.MaxHP = ud.MaxHP;
                    s.ATK = ud.ATK;
                    s.DEF = ud.DEF;
                    s.Level = ud.Level;
                    s.ShieldTurns = ud.ShieldTurns;
                    s.ShieldActivated = ud.ShieldActivated;
                    s.SkillCooldown = ud.SkillCooldown;
                    s.AssignedSkillId = ud.AssignedSkillId;
                    s.Fatigue = ud.Fatigue;

                    // 状態異常・バフ復元
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
                    usedIndices.Add(i);
                    break;
                }
            }
        }
    }
}
