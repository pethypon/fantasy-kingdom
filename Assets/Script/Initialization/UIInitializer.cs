using UnityEngine;

/// <summary>
/// UX系UIシステム（Toast, InputHint, DamagePreview, FloatingDamage 等）の
/// 生成と TurnGenerator への登録を担当する。
/// GameGenerator の InitUXSystems() から分離された単一責任クラス。
/// </summary>
public static class UIInitializer
{
    /// <summary>全UXシステムを生成して TurnGenerator に登録する。</summary>
    public static void Initialize(TurnGenerator turnGen, APSystem apSystem)
    {
        if (turnGen == null)
        {
            Debug.LogError("[UIInitializer] TurnGenerator が null です");
            return;
        }

        EnsureSingleton<ToastMessageUI>("ToastMessageUI");

        var inputHint = CreateComponent<InputHintUI>("InputHintUI");
        turnGen.Systems.InputHintUI = inputHint;

        var dmgPreview = CreateComponent<DamagePreviewUI>("DamagePreviewUI");
        dmgPreview.turnGenerator = turnGen;
        turnGen.Systems.DamagePreviewUI = dmgPreview;

        EnsureSingleton<FloatingDamageUI>("FloatingDamageUI");
        EnsureSingleton<EnemyTurnBannerUI>("EnemyTurnBannerUI");

        if (TurnAPIndicatorUI.Instance == null)
        {
            var indicator = CreateComponent<TurnAPIndicatorUI>("TurnAPIndicatorUI");
            indicator.Init(turnGen, apSystem);
        }

        EnsureSingleton<UnitHoverTooltipUI>("UnitHoverTooltipUI");

        var highlight = CreateComponent<UnitSelectionHighlight>("UnitSelectionHighlight");
        highlight.Init(turnGen);

        turnGen.Systems.MoveUndoSystem = new MoveUndoSystem(apSystem, turnGen.Systems.MoveGenerator);

        EnsureSingleton<ActionLogUI>("ActionLogUI");
    }

    // ================================================================
    //  ヘルパー
    // ================================================================

    private static T CreateComponent<T>(string name) where T : Component
    {
        var go = new GameObject(name);
        return go.AddComponent<T>();
    }

    private static void EnsureSingleton<T>(string name) where T : Component
    {
        // Instance プロパティの存在を前提としたシングルトンチェック
        // シングルトンが既に存在する場合はスキップ
        if (Object.FindFirstObjectByType<T>() != null) return;
        CreateComponent<T>(name);
    }
}
