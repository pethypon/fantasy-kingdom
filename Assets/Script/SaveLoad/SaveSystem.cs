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
        public float MapSeed;
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
    public static GameSaveData CollectGameState(TurnGenerater turnGen, FactionState factionState)
    {
        var data = new GameSaveData
        {
            Turn = turnGen.Turn,
            ThreatLevel = LoadProfile().ThreatLevel,
            MapSeed = 0f // マップはシーン再読み込みで再生成
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
            IsActive = s.gameObject.activeSelf
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

    // ================================================================
    //  資源データの復元ヘルパー
    // ================================================================

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
}
