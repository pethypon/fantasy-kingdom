/// <summary>One description shared by the reward log and manual. Enum values remain save-compatible.</summary>
public static class ArtifactPresentation
{
    public static bool Available(DungeonSystem.Artifact artifact) => artifact == DungeonSystem.Artifact.CrystalShard
        || artifact == DungeonSystem.Artifact.WarBanner || artifact == DungeonSystem.Artifact.ManaFocus;
    public static string Name(DungeonSystem.Artifact artifact) => artifact switch
    {
        DungeonSystem.Artifact.CrystalShard => "クリスタルシャード",
        DungeonSystem.Artifact.WarBanner => "ウォーバナー",
        DungeonSystem.Artifact.IronAegis => "アイアンイージス",
        DungeonSystem.Artifact.ManaFocus => "マナフォーカス",
        DungeonSystem.Artifact.PhoenixFeather => "フェニックスフェザー", _ => "なし"
    };
    public static string Description(DungeonSystem.Artifact artifact) => artifact switch
    {
        DungeonSystem.Artifact.CrystalShard => "メインクリスタルの最大HP＋1000、HPを1000回復",
        DungeonSystem.Artifact.WarBanner => "獲得時に生存している味方駒の攻撃力＋2",
        DungeonSystem.Artifact.ManaFocus => "魔石を30獲得（1回）",
        _ => "効果調整中・新規の抽選対象外"
    };
}
