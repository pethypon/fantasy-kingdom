using UnityEngine;

/// <summary>
/// PlayerStart / EnemyStart で共通するターン開始処理を集約するヘルパー。
/// </summary>
public static class TurnStartHelper
{
    /// <summary>
    /// 指定チームのターン開始処理を実行する。
    /// クリスタルシールドのティック、状態異常ティック、特殊能力開始処理、
    /// APリセット、疲労リセット、サブクリスタル返却、タイマー開始を行う。
    /// </summary>
    public static void ProcessTurnStart(GameSystems systems, Team team)
    {
        // チームに対応するクリスタル・ユニット親を取得
        Transform crystal = (team == Team.Player)
            ? systems.CrystalSystem?.Playercrystal
            : systems.CrystalSystem?.Enemycrystal;

        Transform unitParent = (team == Team.Player)
            ? systems.UnitSetting?.PlayerUnit
            : systems.UnitSetting?.EnemyUnit;

        // クリスタルシールドのターン経過
        if (crystal != null)
            BattleSystem.TickCrystalShields(crystal);

        // 国の生存ターン数を加算（クリスタル序盤収入の逓減に使用）
        if (systems.FactionState != null)
            systems.FactionState.GetNation(team).TurnsAlive++;

        // 状態異常ティック（DoTダメージ + ターン経過）
        if (unitParent != null)
            StatusEffectSystem.TickAllUnits(unitParent);

        // Special Ability: ターン開始時処理（フラグリセット + 浄化光輪）
        if (unitParent != null)
            SpecialAbilitySystem.OnTurnStart(unitParent);

        // AP リセット
        systems.APSystem?.ResetAP(team);

        // 疲労リセット
        if (systems.APSystem != null && unitParent != null)
            systems.APSystem.ResetFatigue(unitParent);

        // サブクリスタル返却待ちタイマー処理
        if (systems.SubCrystalSystem != null)
            systems.SubCrystalSystem.TickPendingReturns(team);
        else
            Debug.LogWarning($"[TurnStartHelper] subCrystalSystem が null のため TickPendingReturns をスキップ ({team})");

        // タイマー開始
        if (systems.TimerSystem != null)
            systems.TimerSystem.StartTurn(team);

        // ダンジョン占有判定・タイマー進行
        if (systems.DungeonSystem != null)
            systems.DungeonSystem.ProcessTurn(team);

        // 強敵ターン処理は WildBossState（専用ターン）で行うためここでは呼ばない
    }
}
