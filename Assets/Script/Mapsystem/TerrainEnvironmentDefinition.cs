using UnityEngine;

public enum TerrainEnvironmentKind { Forest, Bush, Rock, PoisonSwamp, Mud, Road, HealingSpring, Geyser, Cave, Smoke }

/// <summary>Environment rules are authored separately from terrain height/material variants.</summary>
[CreateAssetMenu(menuName = "Fantasy Kingdom/環境/環境の種類", fileName = "Environment")]
public sealed class TerrainEnvironmentDefinition : ScriptableObject
{
    [Header("識別・表示")]
    public string definitionId;
    public string displayName;
    public TerrainEnvironmentKind kind;
    [Tooltip("必要な値を設定してから有効にします。未確定の調整値は自動で補いません。")]
    public bool enabledForPlay;
    public GameObject visualPrefab;
    [Header("移動・射線・視界")]
    public bool blocksMovement;
    public bool blocksShots;
    public bool blocksVision;
    public bool concealsOccupants;
    [Tooltip("隠密マスを確認できる距離。-1は未設定。斜めを含むマス距離です。")]
    public int revealDistance = -1;
    [Tooltip("移動先のAP補正。泥は正、道は負。最終移動APは1以上です。")]
    public int moveAPAdjustment;
    [Header("侵入・滞在効果（未設定は-1）")]
    public bool damageOnEntry;
    public bool damageOnOwnerTurn;
    public int flatDamage = -1;
    [Tooltip("最大HPに対する割合。間欠泉25%案も確定後にここで設定します。")]
    public float maximumHpDamageFraction = -1;
    public float maximumHpHealFraction = -1;
    [Header("燃焼・煙")]
    public bool burnable;
    [Tooltip("焼却後の煙の持続ラウンド。-1は未設定。")]
    public int burnSmokeRounds = -1;
    [Tooltip("煙の確認距離。-1は未設定。")]
    public int burnSmokeRevealDistance = -1;

    public string ConfigurationError
    {
        get
        {
            if (string.IsNullOrWhiteSpace(definitionId)) return "識別IDが未設定です";
            if (InvalidFraction(maximumHpDamageFraction) || InvalidFraction(maximumHpHealFraction)) return "HP割合には有限の0～1（未設定は-1）を指定してください";
            if (flatDamage < -1) return "固定ダメージは0以上（未設定は-1）を指定してください";
            if (System.Math.Abs((long)moveAPAdjustment) > GameConstants.MaxAP) return "移動AP補正は最大APの範囲内で設定してください";
            if (concealsOccupants && revealDistance < 0) return "隠密の確認距離が未設定です";
            if (kind == TerrainEnvironmentKind.Mud && moveAPAdjustment <= 0) return "泥の追加移動APが未設定です";
            if (kind == TerrainEnvironmentKind.Road && moveAPAdjustment >= 0) return "道の移動AP軽減が未設定です";
            if ((damageOnEntry || damageOnOwnerTurn) && flatDamage < 0 && maximumHpDamageFraction < 0)
                return "ダメージ量が未設定です";
            if (maximumHpDamageFraction > 1 || maximumHpHealFraction > 1) return "HP割合は0～1で設定してください";
            if (burnable && (burnSmokeRounds < 0 || burnSmokeRevealDistance < 0)) return "燃焼後の煙が未設定です";
            if (kind == TerrainEnvironmentKind.HealingSpring && maximumHpHealFraction < 0) return "回復量が未設定です";
            return null;
        }
    }
    static bool InvalidFraction(float value) => float.IsNaN(value) || float.IsInfinity(value) || value != -1 && (value < 0 || value > 1);
    public bool IsActive => enabledForPlay && ConfigurationError == null;

    public static string NameJapanese(TerrainEnvironmentKind value)
    {
        switch (value)
        {
            case TerrainEnvironmentKind.Forest: return "森";
            case TerrainEnvironmentKind.Bush: return "草むら";
            case TerrainEnvironmentKind.Rock: return "岩場";
            case TerrainEnvironmentKind.PoisonSwamp: return "毒沼";
            case TerrainEnvironmentKind.Mud: return "泥";
            case TerrainEnvironmentKind.Road: return "道";
            case TerrainEnvironmentKind.HealingSpring: return "回復泉";
            case TerrainEnvironmentKind.Geyser: return "間欠泉";
            case TerrainEnvironmentKind.Cave: return "洞窟";
            default: return "煙";
        }
    }
}
