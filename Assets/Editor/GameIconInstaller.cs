#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Connects installed artwork without copying or publishing the purchased pack.</summary>
public sealed class GameIconInstaller : IPreprocessBuildWithReport
{
    public const string CatalogPath = "Assets/Resources/GeneratedUI/GameIcons.asset";
    const string Pack = "Assets/Admurin's Pixel Items/PixelItems/";
    static readonly (string key, string path)[] Icons = {
        ("resource.Wood", "General/Singles/22_Craftable_Log.png"),
        ("resource.Stone", "General/Singles/16_Throwable_Rocks.png"),
        ("resource.Iron", "General/Singles/62_Ingot_Iron.png"),
        ("resource.MagicOre", "Gems/Singles/72_Gem_Amethyst.png"),
        ("resource.Wheat", "Emoticons/Singles/94_Wheat.png"),
        ("resource.Water", "Skills/Singles/50_Water_Element.png"),
        ("resource.Citizen", "Skills/Singles/175_Passive_Male_Blue.png"),
        ("resource.SubCrystal", "Gems/Singles/22_Gem_Sapphire.png"),
        ("debuff.Stun", "Emoticons/Singles/127_Dazed.png"),
        ("debuff.Mark", "Skills/Singles/169_Passive_Male_Accuracy.png"),
        ("debuff.Weaken", "Emoticons/Singles/71_Sick.png"),
        ("debuff.Poison", "Emoticons/Singles/124_Poison.png"),
        ("debuff.Chill", "Skills/Singles/58_Ice_Element.png"),
        ("debuff.Freeze", "Emoticons/Singles/123_Frozen.png"),
        ("debuff.Seal", "Emoticons/Singles/98_Locked.png"),
        ("debuff.Blind", "Emoticons/Singles/01_NormalEyes.png"),
        ("debuff.NarrowVision", "Emoticons/Singles/103_KeenEyes.png"),
        ("buff.Offensive", "Emoticons/Singles/89_Sword.png"),
        ("buff.Haste", "Emoticons/Singles/104_Speedster.png"),
        ("buff.Insight", "Emoticons/Singles/103_KeenEyes.png"),
        ("buff.Barrier", "Emoticons/Singles/90_Shield.png"),
        ("buff.Reflect", "Skills/Singles/163_Mastery_Shield.png"),
        ("state.Shield", "Emoticons/Singles/90_Shield.png"),
        ("artifact.CrystalShard", "Gems/Singles/22_Gem_Sapphire.png"),
        ("artifact.WarBanner", "Miscellaneous/Singles/119_Pirate_Loot_Flag.png"),
        ("artifact.IronAegis", "Armory/Singles/Weapon Singles/Steel/Steel_Shield1.png"),
        ("artifact.ManaFocus", "Potions/Singles/162_Essence_Mana.png"),
        ("artifact.PhoenixFeather", "General/Singles/301_Feather_Red_A.png"),
        ("item.Relic", "Jewelry/Rings/Singles/11_Small_Skull.png"),
        ("item.Scroll", "Librarium/Singles/43_Scroll_Arcane.png"),
        ("item.Healing", "Potions/Singles/103_Potion_Health_A.png"),
    };
    public int callbackOrder => -100;
    public void OnPreprocessBuild(BuildReport report) => Connect();
    [InitializeOnLoadMethod] static void Schedule() => EditorApplication.delayCall += Connect;
    [MenuItem("Fantasy Kingdom/表示/アイコン素材を接続")]
    public static void Connect()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || AssetDatabase.IsAssetImportWorkerProcess()) return;
        const string bread = "Assets/Resources/UI/Icons/Bread.png";
        if (AssetImporter.GetAtPath(bread) is TextureImporter importer && (importer.textureType != TextureImporterType.Sprite
            || importer.textureShape != TextureImporterShape.Texture2D || importer.spriteImportMode != SpriteImportMode.Single || importer.maxTextureSize != 64))
        {
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.maxTextureSize = 64; importer.mipmapEnabled = false; importer.filterMode = FilterMode.Point;
            importer.alphaIsTransparency = true; importer.isReadable = false; importer.SaveAndReimport();
            AssetDatabase.ImportAsset(bread, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }
        EnsureFolder("Assets/Resources/GeneratedUI");
        var catalog = AssetDatabase.LoadAssetAtPath<GameIconCatalog>(CatalogPath);
        bool changed = catalog == null;
        if (catalog == null) { catalog = ScriptableObject.CreateInstance<GameIconCatalog>(); AssetDatabase.CreateAsset(catalog, CatalogPath); }
        foreach (var item in Icons) changed |= Add(catalog, item.key, Pack + item.path);
        changed |= Add(catalog, "resource.Bread", bread);
        Debug.Log("[GameIcons] Connected " + catalog.entries.Count + " icons; bread sprite=" + (AssetDatabase.LoadAssetAtPath<Sprite>(bread) != null));
        if (changed) { EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog); }
    }
    static bool Add(GameIconCatalog catalog, string key, string path)
    {
        var existing = catalog.entries.Find(e => e != null && e.key == key);
        if (existing != null && existing.sprite != null) return false; // Preserve designer overrides.
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null) return false;
        if (existing != null) existing.sprite = sprite;
        else catalog.entries.Add(new GameIconCatalog.Entry { key = key, sprite = sprite });
        return true;
    }
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\','/'); EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
#endif
