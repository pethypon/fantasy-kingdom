using UnityEngine;

public partial class MapCreate
{
    [Header("環境オブジェクト（森・毒沼・洞窟など）")]
    [SerializeField] TerrainEnvironmentCatalog environmentCatalog;
    TerrainEnvironmentRuntime environment;
    public TerrainEnvironmentRuntime Environment => environment ?? (environment = new TerrainEnvironmentRuntime(this));
    public void InitializeEnvironment(TerrainEnvironmentCatalog catalog = null)
    {
        if (catalog != null) environmentCatalog = catalog;
        if (environmentCatalog == null) environmentCatalog = Resources.Load<TerrainEnvironmentCatalog>("Terrain/EnvironmentCatalog");
        Environment.Initialize(environmentCatalog);
    }
    public bool IsLegalMove(Status actor, Vector3 from, Vector3 to)
    {
        if (actor == null || !TryGetHeight(Mathf.RoundToInt(to.x), Mathf.RoundToInt(to.z), out float height)
            || Mathf.Abs(height - to.y) > .01f || Environment.BlocksMovement(Mathf.RoundToInt(to.x), Mathf.RoundToInt(to.z))) return false;
        if (Environment.IsCaveConnection(from, to)) return true;
        return MovePatterns.CanMove(actor, actor.direction, to.x - from.x, to.z - from.z) && CanTraverse(from, to);
    }
}
