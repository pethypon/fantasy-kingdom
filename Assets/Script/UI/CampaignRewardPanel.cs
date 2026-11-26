using System;
using UnityEngine;

public static class CampaignRewardPanel
{
    public static GameObject Show(CampaignRewards rewards, int round, Action<int> choose)
    {
        var center = new Vector2(.5f, .5f);
        var root = UIFactory.CreatePanel("CampaignRewards", UIBuilder.ScreenCanvas.transform,
            Vector2.zero, Vector2.one, center, Vector2.zero);
        UIFactory.AddImage(root.gameObject, new Color(.025f,.035f,.055f,.69f));
        var card = UIFactory.CreatePanel("選択", root, center, center, center, new Vector2(760, 510));
        var backdrop = UIFactory.AddImage(card.gameObject, new Color(.11f,.13f,.18f));
        GameUITheme.Current?.StylePanel(backdrop);
        var font = UIFactory.LoadDefaultFont();
        var heading = UIFactory.CreateTMP("見出し", card, $"第{round}ターンの報酬", 43, font);
        heading.color = new Color(1f, .9f, .69f);
        heading.fontStyle = TMPro.FontStyles.Bold;
        UIFactory.SetAnchors(heading, .05f, .79f, .95f, .94f);
        var hint = UIFactory.CreateTMP("所持数", card,  $"1つ選択　・　所持魂 {rewards.player.souls}", 28, font);
        UIFactory.SetAnchors(hint, .05f, .68f, .95f, .79f);
        var options = rewards.player.pending;
        for (int i = 0; i < options.Length; i++)
        {
            int slot = i;
            var button = UIFactory.CreateButton("Reward" + i, card, rewards.Label(options[i]), 30,
                BrandGuide.BtnSummonEnabled, font);
            float top = .65f - i * .19f;
            UIFactory.SetAnchors((RectTransform)button.transform, .09f, top - .15f, .91f, top);
            button.onClick.AddListener(() => choose(slot));
        }
        return root.gameObject;
    }
}
