using TMPro;
using UnityEngine;

/// <summary>
/// 資源バーUI：木材・石材・鉄鉱石・鉄・魔石・石炭・小麦・パン・水・板材・切石・市民を表示
/// Canvas > SafeAreaRoot > ResourceBar に付ける
///
/// 構成:
///   ResourceBar (HorizontalLayoutGroup)
///     ├─ WoodText
///     ├─ StoneText
///     ├─ CoalText
///     ├─ IronOreText
///     ├─ IronText
///     ├─ MagicOreText
///     ├─ WheatText
///     ├─ BreadText
///     ├─ WaterText
///     ├─ PlankText
///     ├─ CutStoneText
///     └─ CitizenText
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

    private FactionState.ResourceData lastSnapshot;

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
        SetText(woodText,     "Wood",     res.Wood);
        SetText(stoneText,    "Stone",    res.Stone);
        SetText(coalText,     "Coal",     res.Coal);
        SetText(ironOreText,  "IronOre",  res.IronOre);
        SetText(ironText,     "Iron",     res.Iron);
        SetText(magicOreText, "MagicOre", res.MagicOre);
        SetText(wheatText,    "Wheat",    res.Wheat);
        SetText(breadText,    "Bread",    res.Bread);
        SetText(waterText,    "Water",    res.Water);
        SetText(plankText,    "Plank",    res.Plank);
        SetText(cutStoneText, "CutStone", res.CutStone);
        SetText(citizenText,  "Citizen",  res.Citizen);
    }

    private static void SetText(TextMeshProUGUI tmp, string label, int value)
    {
        if (tmp != null) tmp.text = label + " " + value;
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
