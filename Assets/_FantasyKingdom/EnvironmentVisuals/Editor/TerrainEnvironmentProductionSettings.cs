#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Explicit, inspectable first-play values. The installer never changes recorded definitions again.</summary>
public sealed class TerrainEnvironmentProductionSettings : ScriptableObject
{
    [TextArea(2, 5)] public string balanceStatus = "暫定バランス v1。ゲーム開始時の環境導入用。正式なゲームバランス調整値ではありません。既存の設定値は保持し、未設定値だけ補います。";
    [Header("暫定の未設定値補完")]
    [Min(0)] public int concealRevealDistance = 1;
    [Min(1)] public int forestAttackRevealRounds = 2;
    [Min(1)] public int caveCooldownTurns = 2;
    [Range(0, 1)] public float poisonMaximumHpDamageFraction = .05f;
    [Min(1)] public int mudMoveAP = 1;
    [Min(1)] public int roadMoveAPDiscount = 1;
    [Range(0, 1)] public float springMaximumHpHealFraction = .05f;
    [Min(1)] public int smokeRounds = 30;
    [Range(0, 1)] public float smokeMaximumHpDamageFraction = .10f;
    [Min(0)] public int smokeRevealDistance = 1;
    [Tooltip("未導入の標準岩場だけを通行・射撃可能にし、R1の被遠距離ダメージ×0.75を利用可能にします。奥の視界遮断は保持します。")]
    public bool makeUninstalledStandardRockPassable = true;
    [Header("導入履歴（再導入時に手動の無効化・調整を保護）")]
    public bool generationInstalled;
    public List<TerrainEnvironmentDefinition> installed = new List<TerrainEnvironmentDefinition>();

    public void Validate()
    {
        if (concealRevealDistance < 0 || forestAttackRevealRounds < 1 || caveCooldownTurns < 1
            || mudMoveAP < 1 || mudMoveAP > GameConstants.MaxAP || roadMoveAPDiscount < 1 || roadMoveAPDiscount > GameConstants.MaxAP
            || smokeRounds < 1 || smokeRevealDistance < 0 || Invalid(poisonMaximumHpDamageFraction)
            || Invalid(springMaximumHpHealFraction) || Invalid(smokeMaximumHpDamageFraction))
            throw new InvalidOperationException("環境の暫定導入値が不正です。EnvironmentProductionSettingsを確認してください。");
    }
    static bool Invalid(float value) => float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > 1;
}
#endif
