public partial class AIBoardState
{
    EnemyTerritoryIntel standaloneTerritoryIntel;
    /// <summary>Commander-owned through its governor; isolated boards keep their own memory.</summary>
    public EnemyTerritoryIntel TerritoryIntel => Governor != null ? Governor.TerritoryIntel
        : standaloneTerritoryIntel ?? (standaloneTerritoryIntel = new EnemyTerritoryIntel());
}
