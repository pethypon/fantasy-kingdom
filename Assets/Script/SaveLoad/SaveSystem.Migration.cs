using UnityEngine;

// =====================================================================
//  SaveSystem.Migration — セーブデータのバージョン間マイグレーション
//
//  新しいセーブ構造に変更するたびに以下の手順を踏む:
//    1. SaveSystem.CurrentSaveVersion をインクリメント
//    2. 新しいフィールドのデフォルト値を GameSaveData に設定
//    3. MigrateVN_to_VN1() を追加（旧データに対する埋め合わせ処理）
//    4. RunMigrations() の switch ラダーに追加
//
//  これにより旧セーブデータをロードした瞬間に最新フォーマットへ変換され、
//  呼び出し側は常に最新バージョンのデータだけを扱えばよい。
// =====================================================================
public static partial class SaveSystem
{
    /// <summary>
    /// ロード時のマイグレーションエントリポイント。
    /// バージョン0 (未設定) から順に現行バージョンまで migrate する。
    /// 未知の未来バージョンが来た場合は警告のみ出し、そのまま返す。
    /// </summary>
    static GameSaveData RunMigrations(GameSaveData data)
    {
        if (data == null) return null;

        // 未来バージョン: 非破壊的に読み込みを試みる
        if (data.Version > CurrentSaveVersion)
        {
            Debug.LogWarning($"[SaveSystem] セーブデータのバージョン({data.Version})が現在のバージョン({CurrentSaveVersion})より新しいです。互換性の問題が発生する可能性があります。");
            return data;
        }

        // 旧バージョンをレガシー(0) として扱う
        int from = data.Version;
        int to = CurrentSaveVersion;

        if (from == to) return data;

        Debug.Log($"[SaveSystem] マイグレーション開始 v{from} → v{to}");

        // バージョンごとの累積マイグレーション
        // 将来的に v2, v3 が増えたらここに追加する
        // 例:
        //   if (from <= 1 && to >= 2) Migrate_V1_to_V2(data);
        //   if (from <= 2 && to >= 3) Migrate_V2_to_V3(data);
        if (from <= 0 && to >= 1) Migrate_V0_to_V1(data);

        data.Version = to;
        Debug.Log($"[SaveSystem] マイグレーション完了 → v{to}");
        return data;
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
    static void Migrate_V0_to_V1(GameSaveData data)
    {
        if (data.Timer == null) data.Timer = new TimerSaveData();
        if (data.Fog == null) data.Fog = new FogSaveData();
        if (data.AI == null) data.AI = new AISaveData();
        if (data.PlayerNationExtra == null) data.PlayerNationExtra = new NationExtraSaveData();
        if (data.EnemyNationExtra == null) data.EnemyNationExtra = new NationExtraSaveData();
        if (data.PlayerResources == null) data.PlayerResources = new ResourceSaveData();
        if (data.EnemyResources == null) data.EnemyResources = new ResourceSaveData();
        if (data.PlayerAP == null) data.PlayerAP = new APSaveData();
        if (data.EnemyAP == null) data.EnemyAP = new APSaveData();
    }
}
