using System.Collections.Generic;

/// <summary>
/// Kind列挙型の日本語表示名マッピング。
/// </summary>
public static class KindNameJP
{
    private static readonly Dictionary<Kind, string> Names = new Dictionary<Kind, string>
    {
        { Kind.Crystal,      "クリスタル" },
        { Kind.King,         "キング" },
        { Kind.Knight,       "ナイト" },
        { Kind.Archer,       "アーチャー" },
        { Kind.Magic,        "メイジ" },
        { Kind.Assassin,     "アサシン" },
        { Kind.Scout,        "スカウト" },
        { Kind.Priest,       "プリースト" },
        { Kind.Guardian,     "ガーディアン" },
        { Kind.Crossbow,     "クロスボウ" },
        { Kind.Magicsniper,  "魔法狙撃手" },
        { Kind.Bomber,       "ボマー" },
        { Kind.Boss,         "ボス" },
        { Kind.SubCrystal,   "サブクリスタル" },
        { Kind.WoodWall,     "木壁" },
        { Kind.StoneWall,    "石壁" },
        { Kind.None,         "---" },
    };

    public static string Get(Kind kind)
    {
        return Names.TryGetValue(kind, out string name) ? name : kind.ToString();
    }
}
