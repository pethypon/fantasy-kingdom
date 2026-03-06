using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 資源バーUI：木材・石材・鉄鉱石・鉄・魔石・石炭・小麦・パン・水・板材・切石・市民を表示
/// Canvas > SafeAreaRoot > ResourceBar に付ける
///
/// Awake() でフォントサイズ・配置を自動設定する
/// 各行の親に HorizontalLayoutGroup を自動追加して均等配置
/// </summary>
public class ResourceBarUI : MonoBehaviour
{
    [Header("資源テキスト")]
    [SerializeField] private TextMeshProUGUI woodText;
    [SerializeField] private TextMeshProUGUI stoneText;
    [SerializeField] private TextMeshProUGUI coalText;
    [SerializeField] private TextMeshProUGUI ironOreText;
    [SerializeField] private TextMeshProUGUI ironText;
    [SerializeField] private TextMeshProUGUI magicOreText;
    [SerializeField] private TextMeshProUGUI wheatText;
    [SerializeField] private TextMeshProUGUI breadText;
    [SerializeField] private TextMeshProUGUI waterText;
    [SerializeField] private TextMeshProUGUI plankText;
    [SerializeField] private TextMeshProUGUI cutStoneText;
    [SerializeField] private TextMeshProUGUI citizenText;

    [Header("参照")]
    [SerializeField] private FactionState factionState;

    [Header("設定")]
    [SerializeField] private Team displayTeam = Team.Player;
    [SerializeField] private float fontSize = 22f;

    private FactionState.ResourceData lastSnapshot;

    private void Awake()
    {
        TextMeshProUGUI[] allTexts = {
            woodText, stoneText, coalText, ironOreText, ironText, magicOreText,
            wheatText, breadText, waterText, plankText, cutStoneText, citizenText
        };

        foreach (var tmp in allTexts)
        {
            if (tmp == null) continue;
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableAutoSizing = false;

            // LayoutElement で最小幅を揃える
            var le = tmp.GetComponent<LayoutElement>();
            if (le == null) le = tmp.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 90f;
            le.preferredWidth = 110f;
            le.flexibleWidth = 1f;
        }

        // 各行の親に HorizontalLayoutGroup を自動設定
        ConfigureRowLayout(woodText);
        ConfigureRowLayout(wheatText);
    }

    private void ConfigureRowLayout(TextMeshProUGUI anyChildInRow)
    {
        if (anyChildInRow == null) return;
        Transform row = anyChildInRow.transform.parent;
        if (row == null) return;

        var hlg = row.GetComponent<HorizontalLayoutGroup>();
        if (hlg == null) hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();

        hlg.spacing = 16f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = false;
        hlg.padding = new RectOffset(8, 8, 2, 2);
    }

    private void Update()
    {
        if (factionState == null) return;

        FactionState.ResourceData res = displayTeam == Team.Player
            ? factionState.PlayerResources
            : factionState.EnemyResources;

        if (res == null) return;
        if (!HasChanged(res)) return;

        SaveSnapshot(res);
        Refresh(res);
    }

    private void Refresh(FactionState.ResourceData res)
    {
        SetText(woodText,     "木材",   res.Wood);
        SetText(stoneText,    "石材",   res.Stone);
        SetText(coalText,     "石炭",   res.Coal);
        SetText(ironOreText,  "鉄鉱",   res.IronOre);
        SetText(ironText,     "鉄",    res.Iron);
        SetText(magicOreText, "魔石",   res.MagicOre);
        SetText(wheatText,    "小麦",   res.Wheat);
        SetText(breadText,    "パン",   res.Bread);
        SetText(waterText,    "水",    res.Water);
        SetText(plankText,    "板材",   res.Plank);
        SetText(cutStoneText, "切石",   res.CutStone);
        SetText(citizenText,  "市民",   res.Citizen);
    }

    private static void SetText(TextMeshProUGUI tmp, string label, int value)
    {
        if (tmp != null) tmp.text = label + ": " + value;
    }

    private bool HasChanged(FactionState.ResourceData res)
    {
        if (lastSnapshot == null) return true;
        return res.Wood     != lastSnapshot.Wood
            || res.Stone    != lastSnapshot.Stone
            || res.Coal     != lastSnapshot.Coal
            || res.IronOre  != lastSnapshot.IronOre
            || res.Iron     != lastSnapshot.Iron
            || res.MagicOre != lastSnapshot.MagicOre
            || res.Wheat    != lastSnapshot.Wheat
            || res.Bread    != lastSnapshot.Bread
            || res.Water    != lastSnapshot.Water
            || res.Plank    != lastSnapshot.Plank
            || res.CutStone != lastSnapshot.CutStone
            || res.Citizen  != lastSnapshot.Citizen;
    }

    private void SaveSnapshot(FactionState.ResourceData res)
    {
        if (lastSnapshot == null)
            lastSnapshot = new FactionState.ResourceData();

        lastSnapshot.Wood     = res.Wood;
        lastSnapshot.Stone    = res.Stone;
        lastSnapshot.Coal     = res.Coal;
        lastSnapshot.IronOre  = res.IronOre;
        lastSnapshot.Iron     = res.Iron;
        lastSnapshot.MagicOre = res.MagicOre;
        lastSnapshot.Wheat    = res.Wheat;
        lastSnapshot.Bread    = res.Bread;
        lastSnapshot.Water    = res.Water;
        lastSnapshot.Plank    = res.Plank;
        lastSnapshot.CutStone = res.CutStone;
        lastSnapshot.Citizen  = res.Citizen;
    }
}
