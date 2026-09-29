using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Exact resource counts remain available when HUD numbers use 万/億.</summary>
public sealed class ResourceValueTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    string value;
    public string Value { get => value; set { this.value=value; if(label!=null) label.text=value; } }
    RectTransform popup;
    TextMeshProUGUI label;
    public void OnPointerEnter(PointerEventData data)
    {
        var canvas=GetComponentInParent<Canvas>();
        if (canvas==null) return;
        if (popup==null)
        {
            var go=new GameObject("Resource exact value",typeof(RectTransform),typeof(Image));
            popup=go.GetComponent<RectTransform>(); popup.SetParent(canvas.transform,false);
            popup.sizeDelta=new Vector2(580,58);popup.pivot=new Vector2(0,1);
            var image=go.GetComponent<Image>();image.color=new Color(.04f,.06f,.09f,.98f);image.raycastTarget=false;
            GameUITheme.Current?.StylePlate(image,new Color(.75f,.83f,.92f));
            label=UIFactory.CreateTMP("Value",popup,"",24,UIFactory.LoadDefaultFont());
            UIFactory.StretchFill(label.rectTransform);label.rectTransform.offsetMin=new Vector2(16,4);label.rectTransform.offsetMax=new Vector2(-16,-4);
            label.raycastTarget=false;label.enableAutoSizing=true;label.fontSizeMin=18;label.fontSizeMax=24;
        }
        label.text=Value;
        var root=(RectTransform)canvas.transform;
        var corners=new Vector3[4];((RectTransform)transform).GetWorldCorners(corners);
        var point=(Vector2)root.InverseTransformPoint(corners[0]);
        popup.anchoredPosition=new Vector2(Mathf.Clamp(point.x,root.rect.xMin,root.rect.xMax-popup.rect.width),point.y-4);
        popup.SetAsLastSibling();popup.gameObject.SetActive(true);
    }
    public void OnPointerExit(PointerEventData data) { if(popup!=null) popup.gameObject.SetActive(false); }
    void OnDisable() { if(popup!=null) popup.gameObject.SetActive(false); }
    void OnDestroy() { if(popup!=null) Destroy(popup.gameObject); }
}
