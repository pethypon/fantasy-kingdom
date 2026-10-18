using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class TerrainEnvironmentPlacement
{
    public TerrainEnvironmentDefinition definition;
    public Vector2Int cell;
    public bool hasCaveExit;
    public Vector2Int caveExit;
}

[Serializable]
public sealed class TerrainEnvironmentGenerationRule
{
    public TerrainEnvironmentKind kind;
    public bool enabled = true;
    [Min(1)] public int weight = 1;
    [Min(1)] public int maximumCells = 12;
}

/// <summary>新規ゲーム専用の生成設定。手動配置と保存済みセルは生成で上書きしない。</summary>
[Serializable]
public sealed class TerrainEnvironmentGenerationSettings
{
    public bool enabled;
    [Tooltip("地形シードに加える環境専用シード。同じ地形と値なら同じ配置です。")]
    public int seedOffset = 19827;
    [Range(0, .4f), Tooltip("初期安全地帯を除いた配置可能マスに対する環境の目標割合。")]
    public float coverage = .16f;
    [Range(0, 4096)] public int maximumCells = 160;
    [Range(1, 8192)] public int maximumAttempts = 1600;
    [Range(5, 12), Tooltip("両クリスタル周辺の環境を置かない距離。初期部隊の配置範囲5以上。")]
    public int initialSafeRadius = 5;
    public List<TerrainEnvironmentGenerationRule> rules = new List<TerrainEnvironmentGenerationRule>();

    public static TerrainEnvironmentGenerationSettings CreatePlayableDefaults()
    {
        var result = new TerrainEnvironmentGenerationSettings { enabled = true };
        Add(result, TerrainEnvironmentKind.Forest, 4, 64);
        Add(result, TerrainEnvironmentKind.Bush, 4, 64);
        Add(result, TerrainEnvironmentKind.Rock, 1, 8);
        Add(result, TerrainEnvironmentKind.PoisonSwamp, 1, 8);
        Add(result, TerrainEnvironmentKind.Mud, 1, 12);
        Add(result, TerrainEnvironmentKind.Road, 1, 12);
        Add(result, TerrainEnvironmentKind.HealingSpring, 1, 3);
        Add(result, TerrainEnvironmentKind.Geyser, 1, 4);
        Add(result, TerrainEnvironmentKind.Cave, 1, 4);
        return result;
    }
    static void Add(TerrainEnvironmentGenerationSettings settings, TerrainEnvironmentKind kind, int weight, int maximum)
        => settings.rules.Add(new TerrainEnvironmentGenerationRule { kind = kind, weight = weight, maximumCells = maximum });
}

[CreateAssetMenu(menuName = "Fantasy Kingdom/環境/環境配置カタログ", fileName = "EnvironmentCatalog")]
public sealed class TerrainEnvironmentCatalog : ScriptableObject
{
    [Tooltip("無効なら今までの地形だけで生成します。")]
    public bool enabledForPlay;
    public List<TerrainEnvironmentDefinition> definitions = new List<TerrainEnvironmentDefinition>();
    public List<TerrainEnvironmentPlacement> placements = new List<TerrainEnvironmentPlacement>();
    public TerrainEnvironmentGenerationSettings generation = new TerrainEnvironmentGenerationSettings();
}
