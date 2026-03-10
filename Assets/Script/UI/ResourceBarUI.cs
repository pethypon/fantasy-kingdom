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
    [SerializeField] private TextMeshProUGUI subCrystalText;

    [Header("参照")]
    [SerializeField] private FactionState factionState;

    public void Init(FactionState fs) => factionState = fs;

    [Header("設定")]
    [SerializeField] private Team displayTeam = Team.Player;

    private FactionState.ResourceData lastSnapshot;
    private int lastCitizenCap = -1;
    private int lastSubCrystals = -1;
    private int lastPendingCount = -1;
    private int lastMinPending = -1;

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
                case "Coal":     coalText = tmp; break;
                case "IronOre":  ironOreText = tmp; break;
                case "Iron":     ironText = tmp; break;
                case "MagicOre": magicOreText = tmp; break;
                case "Wheat":    wheatText = tmp; break;
                case "Bread":    breadText = tmp; break;
                case "Water":    waterText = tmp; break;
                case "Plank":    plankText = tmp; break;
                case "CutStone": cutStoneText = tmp; break;
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
        int pendingCount = factionState.GetPendingReturnCount(displayTeam);
        int minPending = factionState.GetMinPendingReturn(displayTeam);
        if (!HasChanged(res) && citizenCap == lastCitizenCap
            && subCrystals == lastSubCrystals
            && pendingCount == lastPendingCount && minPending == lastMinPending) return;

        lastCitizenCap = citizenCap;
        lastSubCrystals = subCrystals;
        lastPendingCount = pendingCount;
        lastMinPending = minPending;
        SaveSnapshot(res);
        Refresh(res, citizenCap, subCrystals, pendingCount, minPending);
    }

    private void Refresh(FactionState.ResourceData res, int citizenCap,
                         int subCrystals = 0, int pendingCount = 0, int minPending = 0)
    {
        // 原材料 (茶系ラベル)
        SetText(woodText,     "木材",  res.Wood,  "#D4A574");
        SetText(stoneText,    "石材",  res.Stone, "#B0A898");
        // 鉱物 (青灰系ラベル)
        SetText(coalText,     "石炭",  res.Coal,     "#8899AA");
        SetText(ironOreText,  "鉄鉱",  res.IronOre,  "#9AACBE");
        // 加工品 (紫系ラベル)
        SetText(ironText,     "鉄",   res.Iron,     "#A89CC8");
        SetText(magicOreText, "魔石",  res.MagicOre, "#C088D0");
        // 食料 (暖色系ラベル)
        SetText(wheatText,    "小麦",  res.Wheat,    "#D4B85C");
        SetText(breadText,    "パン",  res.Bread,    "#D4A04C");
        // 水 (原材料)
        SetText(waterText,    "水",   res.Water,    "#6CB8D4");
        // 加工品
        SetText(plankText,    "板材",  res.Plank,    "#C8A87C");
        SetText(cutStoneText, "切石",  res.CutStone, "#A8B0A8");
        // 人口 (緑系ラベル) — 上限付き表示
        SetTextWithCap(citizenText, "市民", res.Citizen, citizenCap, "#78C888");
        // サブクリスタル (シアン系ラベル) — 返却待ち表示付き
        if (subCrystalText != null)
        {
            string pendingText = pendingCount > 0
                ? $" <size=70%>返却:{pendingCount}個({minPending}T)</size>"
                : "";
            subCrystalText.text = $"<color=#58C8E8><size=80%>副晶</size></color> {subCrystals}{pendingText}";
        }
    }

    private static void SetText(TextMeshProUGUI tmp, string label, int value, string colorHex)
    {
        if (tmp != null)
            tmp.text = $"<color={colorHex}><size=80%>{label}</size></color> {value}";
    }

    private static void SetTextWithCap(TextMeshProUGUI tmp, string label, int value, int cap, string colorHex)
    {
        if (tmp != null)
            tmp.text = $"<color={colorHex}><size=80%>{label}</size></color> {value}/{cap}";
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
