using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 画面上部に一時的に表示するトースト通知。
/// 実績解除などから ToastUI.Show(string, seconds) で呼び出す。
/// </summary>
public class ToastUI : MonoBehaviour
{
    public static void Show(string message, float seconds = 3f)
    {
        var canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        var go = new GameObject("Toast", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);
        go.transform.SetAsLastSibling();

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.3f, 0.82f);
        rt.anchorMax = new Vector2(0.7f, 0.92f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var bg = go.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.12f, 0.92f);
        bg.raycastTarget = false;

        var textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var trt = textGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(8, 8); trt.offsetMax = new Vector2(-8, -8);
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = message;
        tmp.fontSize = 18;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(0.95f, 0.88f, 0.55f);
        tmp.raycastTarget = false;

        var behaviour = go.AddComponent<ToastUI>();
        behaviour.StartCoroutine(behaviour.DestroyAfter(seconds));
    }

    private IEnumerator DestroyAfter(float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        if (this != null && gameObject != null) Destroy(gameObject);
    }
}
