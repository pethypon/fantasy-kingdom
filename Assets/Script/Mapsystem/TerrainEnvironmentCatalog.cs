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

[CreateAssetMenu(menuName = "Fantasy Kingdom/環境/環境配置カタログ", fileName = "EnvironmentCatalog")]
public sealed class TerrainEnvironmentCatalog : ScriptableObject
{
    [Tooltip("無効なら今までの地形だけで生成します。")]
    public bool enabledForPlay;
    public List<TerrainEnvironmentDefinition> definitions = new List<TerrainEnvironmentDefinition>();
    public List<TerrainEnvironmentPlacement> placements = new List<TerrainEnvironmentPlacement>();
}
