using TMPro;
using UnityEngine;
/// <summary>
/// 資源バーUI：資源値を表示する
/// UIBuilder が生成した TMP 要素を名前で自動検出する
/// </summary>
public class ResourceBarUI : MonoBehaviour
{
    [Header("資源テキスト")]
    [SerializeField] private TextMeshProUGUI woodText;
    [SerializeField] private TextMeshProUGUI stoneText;
    [SerializeField] private TextMeshProUGUI ironText;
    [SerializeField] private TextMeshProUGUI magicOreText;
    [SerializeField] private TextMeshProUGUI wheatText;
    [SerializeField] private TextMeshProUGUI breadText;
    [SerializeField] private TextMeshProUGUI waterText;
    [SerializeField] private TextMeshProUGUI citizenText;
    [SerializeField] private TextMeshProUGUI subCrystalText;

    [Header("参照")]
    [SerializeField] private FactionState factionState;

    public void Init(FactionState fs) => factionState = fs;

    [Header("設定")]
    [SerializeField] private Team displayTeam = Team.Player;

    private FactionState.ResourceData lastSnapshot;
    private int lastCitizenCap = -1;
    private int lastSubCrystals = -1;

    private void Awake()
    {
        AutoDiscoverTexts();
    }

    private void AutoDiscoverTexts()
    {
        var allTMP = GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (var tmp in allTMP)
        {
            switch (tmp.gameObject.name)
            {
                case "Wood":     woodText = tmp; break;
                case "Stone":    stoneText = tmp; break;
                case "Iron":     ironText = tmp; break;
                case "MagicOre": magicOreText = tmp; break;
                case "Wheat":    wheatText = tmp; break;
                case "Bread":    breadText = tmp; break;
                case "Water":    waterText = tmp; break;
                case "Citizen":  citizenText = tmp; break;
                case "SubCrystal": subCrystalText = tmp; break;
            }
        }
    }

    private void Update()
    {
        if (factionState == null) return;

        FactionState.ResourceData res = displayTeam == Team.Player
            ? factionState.PlayerResources
            : factionState.EnemyResources;

        if (res == null) return;

        int citizenCap = factionState.GetCitizenCap(displayTeam);
        int subCrystals = factionState.GetSubCrystals(displayTeam);
        if (!HasChanged(res) && citizenCap == lastCitizenCap
            && subCrystals == lastSubCrystals) return;

        lastCitizenCap = citizenCap;
        lastSubCrystals = subCrystals;
        SaveSnapshot(res);
        Refresh(res, citizenCap, subCrystals);
    }

    private void Refresh(FactionState.ResourceData res, int citizenCap,
                         int subCrystals = 0)
    {
        SetText(woodText,     "木材",  res.Wood,  "#D4A574");
        SetText(stoneText,    "石材",  res.Stone, "#B0A898");
        SetText(ironText,     "鉄",   res.Iron,     "#A89CC8");
        SetText(magicOreText, "魔石",  res.MagicOre, "#C088D0");
        SetText(wheatText,    "小麦",  res.Wheat,    "#D4B85C");
        SetText(breadText,    "パン",  res.Bread,    "#D4A04C");
        SetText(waterText,    "水",   res.Water,    "#6CB8D4");
        SetTextWithCap(citizenText, "市民", res.Citizen, citizenCap, "#78C888");
        SetText(subCrystalText, "副晶", subCrystals, "#58C8E8");
    }

    private static void SetText(TextMeshProUGUI tmp, string label, int value, string colorHex)
    {
        if (tmp != null)
            tmp.text = $"<color={colorHex}><b>{label}</b></color> <color=#EEEEEE>{value}</color>";
    }

    private static void SetTextWithCap(TextMeshProUGUI tmp, string label, int value, int cap, string colorHex)
    {
        if (tmp != null)
            tmp.text = $"<color={colorHex}><b>{label}</b></color> <color=#EEEEEE>{value}/{cap}</color>";
    }

    private bool HasChanged(FactionState.ResourceData res)
    {
        if (lastSnapshot == null) return true;
        return res.Wood     != lastSnapshot.Wood
            || res.Stone    != lastSnapshot.Stone
            || res.Iron     != lastSnapshot.Iron
            || res.MagicOre != lastSnapshot.MagicOre
            || res.Wheat    != lastSnapshot.Wheat
            || res.Bread    != lastSnapshot.Bread
            || res.Water    != lastSnapshot.Water
            || res.Citizen  != lastSnapshot.Citizen;
    }

    private void SaveSnapshot(FactionState.ResourceData res)
    {
        if (lastSnapshot == null)
            lastSnapshot = new FactionState.ResourceData();

        lastSnapshot.Wood     = res.Wood;
        lastSnapshot.Stone    = res.Stone;
        lastSnapshot.Iron     = res.Iron;
        lastSnapshot.MagicOre = res.MagicOre;
        lastSnapshot.Wheat    = res.Wheat;
        lastSnapshot.Bread    = res.Bread;
        lastSnapshot.Water    = res.Water;
        lastSnapshot.Citizen  = res.Citizen;
    }
}
