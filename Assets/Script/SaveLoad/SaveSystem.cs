using System;
using System.IO;
using UnityEngine;

// =====================================================================
//  SaveSystem — セーブ/ロード管理のファサード
//
//  JSON形式でゲーム状態を永続化する。
//  セーブデータは Application.persistentDataPath/saves/ に保存。
//  脅威度とマッチ履歴はプロファイルデータとして常に永続化する。
//
//  実装は以下の partial ファイルに分離されている:
//    - SaveSystem.DataTypes.cs  全 [Serializable] データクラス
//    - SaveSystem.Collect.cs    ゲーム状態 → セーブデータ
//    - SaveSystem.Restore.cs    セーブデータ → ゲーム状態
//    - SaveSystem.Migration.cs  バージョン間マイグレーション
// =====================================================================
public static partial class SaveSystem
{
    const string SaveDir = "saves";
    const string ProfileFile = "profile.json";
    const int MaxSlots = 3;

    /// <summary>
    /// 現在のセーブデータバージョン。
    /// 構造変更時はインクリメントし、SaveSystem.Migration.cs の
    /// RunMigrations() にマイグレータを追加すること。
    /// </summary>
    public const int CurrentSaveVersion = 1;

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

        data.Version = CurrentSaveVersion;
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
            var data = JsonUtility.FromJson<GameSaveData>(json);
            if (data == null) return null;

            // バージョンマイグレーション (SaveSystem.Migration.cs)
            return RunMigrations(data);
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
}
