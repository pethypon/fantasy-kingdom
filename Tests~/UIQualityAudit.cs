#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Native UI observations in a real initialized match. Does not change recipes or actors.</summary>
public static class UIQualityAudit
{
    [Serializable] public sealed class Report
    {
        public string Method = "Native camera render, production UI callbacks, initialized ordinary match";
        public string Limitations = "No human first-time comprehension test, mouse motor assessment, or pixel contrast measurement. Overflow candidates require screenshot review. Scroll-hidden content is not a clipping defect.";
        public List<ScreenObservation> Screens = new List<ScreenObservation>();
        public List<TooltipCheck> TooltipChecks = new List<TooltipCheck>();
        public List<string> Unverified = new List<string>();
    }
    [Serializable] public sealed class ScreenObservation
    {
        public string State, File;
        public int Width, Height, VisibleLabels, VisibleGlyphs, VisibleButtons, ScrollHiddenLabels;
        public float TextAreaFraction;
        public List<LabelObservation> Labels = new List<LabelObservation>();
        public List<string> OverflowCandidates = new List<string>();
        public List<string> SmallButtonCandidates = new List<string>();
        public List<ScrollObservation> Scrolls = new List<ScrollObservation>();
    }
    [Serializable] public sealed class ScrollObservation
    {
        public string Path;
        public float ViewportHeight, ContentHeight, VerticalPosition;
    }
    [Serializable] public sealed class LabelObservation
    {
        public string Path, Text;
        public int Characters, Lines;
        public float FontPixels, WidthPixels, HeightPixels, PreferredHeight;
        public bool Overflow, InScroll;
    }
    [Serializable] public sealed class TooltipCheck
    {
        public int Width, Height;
        public float FirstPopupHeight, ReopenPopupHeight, FirstTextHeight, PreferredTextHeight;
        public bool ColdTextFits, ReopenMatchesCold;
    }

    public static IEnumerator Run(GameSystems systems, string output)
    {
        if (systems == null || UIBuilder.ScreenCanvas == null || Camera.main == null)
            throw new InvalidOperationException("UIQualityAudit requires an initialized native match and camera.");
        Directory.CreateDirectory(output);
        var report = new Report();
        var canvas = UIBuilder.ScreenCanvas;
        var previous = UnitPanelUI.SelectedStatus;
        var ally = systems.UnitSetting.PlayerUnit.GetComponentsInChildren<Status>()
            .FirstOrDefault(s => s.IsAlive && s.type == Type.Unit && s.kind != Kind.King);
        if (ally == null) ally = systems.UnitSetting.PlayerUnit.GetComponentsInChildren<Status>().FirstOrDefault(s => s.IsAlive);
        var enemy = systems.UnitSetting.EnemyUnit.GetComponentsInChildren<Status>(true)
            .FirstOrDefault(s => s.IsAlive && s.type == Type.Unit && systems.VisionGenerator.IsInVisionXZ(Team.Player, GridHelper.ToGridXZ(s.transform.position)));
        var building = systems.UnitSetting.PlayerUnit.GetComponentsInChildren<Status>()
            .FirstOrDefault(s => s.IsAlive && (s.type == Type.Building || s.type == Type.Wall));
        var slide = canvas.GetComponentsInChildren<SlidePanelUI>(true).FirstOrDefault();
        foreach (var size in new[] { new Vector2Int(1280, 720), new Vector2Int(960, 600) })
        {
            CloseLayers(systems, slide);
            yield return new WaitForSecondsRealtime(.7f);
            yield return Capture(canvas, "01-default", size, output, report);
            if (ally != null)
            {
                systems.UnitPanelUI.Show(ally);
                yield return Capture(canvas, "02-ally-selected", size, output, report);
                if (Click(canvas, "SpecialStateBtn"))
                {
                    yield return Capture(canvas, "03-ally-details", size, output, report);
                    SpecialSkillSelectionUI.CloseCurrent();
                    yield return null;
                }
            }
            else report.Unverified.Add(size + " friendly selection: no living unit available.");
            if (enemy != null)
            {
                systems.UnitPanelUI.Show(enemy);
                yield return Capture(canvas, "04-visible-enemy-selected", size, output, report);
            }
            else report.Unverified.Add(size + " enemy selection: no enemy in legitimate player vision at this audit moment.");
            if (building != null)
            {
                systems.UnitPanelUI.Show(building);
                yield return Capture(canvas, "05-friendly-building-selected", size, output, report);
            }
            else report.Unverified.Add(size + " building selection: no existing friendly building at this audit moment.");
            systems.UnitPanelUI.Hide();
            if (Click(canvas, "BuildOpenButton"))
            {
                yield return new WaitForSecondsRealtime(.7f);
                yield return Capture(canvas, "06-building-list", size, output, report);
            }
            if (Click(canvas, "UnitOpenButton"))
            {
                yield return new WaitForSecondsRealtime(.4f);
                yield return Capture(canvas, "07-summon-list", size, output, report);
                var tooltip = canvas.GetComponentsInChildren<SummonUnitTooltip>()
                    .FirstOrDefault(t => t.Definition != null && t.gameObject.activeInHierarchy);
                if (tooltip != null)
                {
                    var check = new TooltipCheck { Width = size.x, Height = size.y };
                    // Settle the render target and scroll layout before pointer entry. Converting the
                    // canvas after entry fires ScrollRect.onValueChanged, intentionally hiding popups.
                    yield return Capture(canvas, "08-summon-hover", size, output, report, () =>
                    {
                        // This isolated audit owns this scene popup. Verify cold creation at each size,
                        // rather than passing because the previous resolution initialized TMP already.
                        var prior = canvas.transform.Find("SummonUnitDetailsTooltip");
                        if (prior != null) UnityEngine.Object.DestroyImmediate(prior.gameObject);
                        var corners = new Vector3[4]; ((RectTransform)tooltip.transform).GetWorldCorners(corners);
                        var point = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, (corners[0] + corners[2]) * .5f);
                        tooltip.OnPointerEnter(new PointerEventData(EventSystem.current) { position = point });
                    }, () =>
                    {
                        var popup = (RectTransform)canvas.transform.Find("SummonUnitDetailsTooltip");
                        var label = popup != null ? popup.GetComponentInChildren<TMP_Text>() : null;
                        check.FirstPopupHeight = popup != null ? popup.rect.height : 0;
                        check.FirstTextHeight = label != null ? label.rectTransform.rect.height : 0;
                        check.PreferredTextHeight = label != null ? label.preferredHeight : 0;
                        check.ColdTextFits = label != null && !label.isTextOverflowing
                            && check.FirstTextHeight + 2 >= check.PreferredTextHeight;
                        if (!check.ColdTextFits) throw new InvalidOperationException("[UIQualityAudit] Cold summon hover clips text at " + size
                            + ": actual=" + check.FirstTextHeight + " preferred=" + check.PreferredTextHeight + " popup=" + check.FirstPopupHeight);
                    });
                    tooltip.OnPointerExit(null);
                    yield return Capture(canvas, "08b-summon-reopen", size, output, report, () =>
                    {
                        var corners = new Vector3[4]; ((RectTransform)tooltip.transform).GetWorldCorners(corners);
                        var point = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, (corners[0] + corners[2]) * .5f);
                        tooltip.OnPointerEnter(new PointerEventData(EventSystem.current) { position = point });
                    }, () =>
                    {
                        var popup = (RectTransform)canvas.transform.Find("SummonUnitDetailsTooltip");
                        check.ReopenPopupHeight = popup != null ? popup.rect.height : 0;
                        check.ReopenMatchesCold = popup != null && Mathf.Abs(check.FirstPopupHeight - check.ReopenPopupHeight) <= 2;
                        report.TooltipChecks.Add(check);
                        if (!check.ReopenMatchesCold) throw new InvalidOperationException("[UIQualityAudit] Cold/reopen summon hover size differs at " + size);
                        Debug.Log("[UIQualityAudit] PASS cold summon hover fits and matches reopen at " + size);
                    });
                    tooltip.OnPointerExit(null);
                }
                else report.Unverified.Add(size + " summon hover: no authored production row available.");
            }
            if (slide != null) slide.ClosePanel();
            yield return new WaitForSecondsRealtime(.7f);
            if (Click(canvas, "MenuButton"))
            {
                yield return Capture(canvas, "09-menu", size, output, report);
                var manualButton = canvas.GetComponentsInChildren<Button>()
                    .FirstOrDefault(b => b.GetComponentInChildren<TMP_Text>()?.text == "駒・ルール説明書");
                if (manualButton != null)
                {
                    manualButton.onClick.Invoke();
                    // The production manual intentionally remembers its selected tab.
                    // Select it explicitly so the second resolution is not mislabeled.
                    var piecesTab = canvas.GetComponentsInChildren<Button>()
                        .FirstOrDefault(b => b.GetComponentInChildren<TMP_Text>()?.text == "駒");
                    if (piecesTab != null) piecesTab.onClick.Invoke();
                    yield return Capture(canvas, "10-manual-pieces", size, output, report);
                    var rulesTab = canvas.GetComponentsInChildren<Button>()
                        .FirstOrDefault(b => b.GetComponentInChildren<TMP_Text>()?.text == "ルール");
                    if (rulesTab != null)
                    {
                        rulesTab.onClick.Invoke();
                        yield return Capture(canvas, "11-manual-rules", size, output, report);
                    }
                    var manualScroll = canvas.GetComponentsInChildren<ScrollRect>()
                        .FirstOrDefault(s => s.name == "ScrollView" && s.transform.parent.name == "ManualPanel");
                    if (manualScroll != null)
                    {
                        Canvas.ForceUpdateCanvases();
                        manualScroll.verticalNormalizedPosition = 0;
                        yield return Capture(canvas, "12-manual-rules-bottom", size, output, report,
                            () => manualScroll.verticalNormalizedPosition = 0);
                    }
                    GameManualUI.Instance?.Close();
                    yield return null;
                }
                GameMenuUI.Instance?.Close();
                yield return null;
            }
        }
        CloseLayers(systems, slide);
        if (previous != null && previous.IsAlive) systems.UnitPanelUI.Show(previous);
        File.WriteAllText(Path.Combine(output, "ui-observations.json"), JsonUtility.ToJson(report, true));
        File.WriteAllText(Path.Combine(output, "ui-method.txt"), report.Method + "\n" + report.Limitations
            + "\n1280x720 and 960x600 are native RenderTexture output dimensions; controls use production CanvasScaler."
            + "\nSelection uses existing UnitPanelUI.Show; opening menus invokes production Button.onClick; hover invokes the real pointer handler. This is automated UI execution, not a manual mouse playthrough."
            + "\nMissing enemy/building states are recorded without teleporting actors or exposing hidden enemies.\n" + string.Join("\n", report.Unverified));
        Debug.Log("[UIQualityAudit] Native screens=" + report.Screens.Count + " output=" + output);
    }

    static void CloseLayers(GameSystems systems, SlidePanelUI slide)
    {
        SpecialSkillSelectionUI.CloseCurrent(); SummonUnitTooltip.HideCurrent();
        GameManualUI.Instance?.Close(); GameMenuUI.Instance?.Close();
        if (slide != null) slide.ClosePanel();
        systems.UnitPanelUI.Hide();
    }
    static bool Click(Canvas canvas, string name)
    {
        var button = canvas.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == name);
        if (button == null || !button.gameObject.activeInHierarchy || !button.interactable) return false;
        button.onClick.Invoke(); return true;
    }

    static IEnumerator Capture(Canvas canvas, string state, Vector2Int size, string output, Report report,
        Action beforeRender = null, Action afterRender = null)
    {
        var camera = Camera.main;
        var oldMode = canvas.renderMode; var oldCamera = canvas.worldCamera; var oldPlane = canvas.planeDistance;
        var oldRT = camera.targetTexture; var active = RenderTexture.active;
        var target = new RenderTexture(size.x, size.y, 24);
        var headPositions = new List<KeyValuePair<RectTransform, Vector3>>();
        Texture2D pixels = null;
        try
        {
            camera.targetTexture = target;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            yield return null;
            Canvas.ForceUpdateCanvases();
            foreach (var text in canvas.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
            Canvas.ForceUpdateCanvases();
            beforeRender?.Invoke();
            Canvas.ForceUpdateCanvases();
            // Production head labels are positioned in Overlay screen pixels. Only this
            // capture uses a camera canvas, so map those same native positions onto its
            // plane rather than rendering the labels far outside the camera frustum.
            var heads = canvas.transform.Find("UnitHeads");
            if (heads != null)
                for (int i = 0; i < heads.childCount; i++)
                {
                    var head = heads.GetChild(i) as RectTransform;
                    if (head == null || head.name != "HeadUI") continue;
                    headPositions.Add(new KeyValuePair<RectTransform, Vector3>(head, head.position));
                    if (RectTransformUtility.ScreenPointToWorldPointInRectangle((RectTransform)canvas.transform,
                        head.position, camera, out var position)) head.position = position;
                }
            camera.Render(); RenderTexture.active = target;
            pixels = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); pixels.Apply();
            var filename = state + "-" + size.x + "x" + size.y + ".png";
            File.WriteAllBytes(Path.Combine(output, filename), pixels.EncodeToPNG());
            afterRender?.Invoke();
            report.Screens.Add(Observe(canvas, state, filename, size));
        }
        finally
        {
            foreach (var head in headPositions) if (head.Key != null) head.Key.position = head.Value;
            canvas.renderMode = oldMode; canvas.worldCamera = oldCamera; canvas.planeDistance = oldPlane;
            camera.targetTexture = oldRT; RenderTexture.active = active;
            if (pixels != null) UnityEngine.Object.Destroy(pixels);
            target.Release(); UnityEngine.Object.Destroy(target);
        }
        yield return null;
    }

    static ScreenObservation Observe(Canvas canvas, string state, string file, Vector2Int size)
    {
        var result = new ScreenObservation { State = state, File = file, Width = size.x, Height = size.y };
        var full = new Rect(0, 0, size.x, size.y);
        foreach (var text in canvas.GetComponentsInChildren<TMP_Text>())
        {
            if (!Visible(text.transform) || string.IsNullOrWhiteSpace(text.text)) continue;
            var bounds = PixelRect(text.rectTransform, canvas.worldCamera);
            bool inScroll = text.GetComponentInParent<ScrollRect>() != null;
            var visible = Clip(bounds, full);
            foreach (var mask in text.GetComponentsInParent<Mask>()) if (mask.enabled) visible = Clip(visible, PixelRect((RectTransform)mask.transform, canvas.worldCamera));
            foreach (var mask in text.GetComponentsInParent<RectMask2D>()) if (mask.enabled) visible = Clip(visible, PixelRect((RectTransform)mask.transform, canvas.worldCamera));
            if (visible.width <= 0 || visible.height <= 0) { if (inScroll) result.ScrollHiddenLabels++; continue; }
            var clean = text.GetParsedText();
            result.VisibleLabels++;
            result.TextAreaFraction += visible.width * visible.height / (size.x * (float)size.y);
            int glyphs = 0;
            for (int i = 0; i < text.textInfo.characterCount; i++)
            {
                var c = text.textInfo.characterInfo[i]; if (!c.isVisible) continue;
                var center = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera,
                    text.transform.TransformPoint((c.bottomLeft + c.topRight) * .5f));
                if (visible.Contains(center)) glyphs++;
            }
            result.VisibleGlyphs += glyphs;
            var observation = new LabelObservation
            {
                Path = PathOf(text.transform, canvas.transform), Text = clean, Characters = clean.Length,
                Lines = text.textInfo.lineCount, FontPixels = text.fontSize * canvas.scaleFactor,
                WidthPixels = bounds.width, HeightPixels = bounds.height, PreferredHeight = text.preferredHeight,
                Overflow = text.isTextOverflowing, InScroll = inScroll
            };
            result.Labels.Add(observation);
            if (observation.Overflow) result.OverflowCandidates.Add(observation.Path);
        }
        foreach (var button in canvas.GetComponentsInChildren<Button>())
        {
            if (!Visible(button.transform)) continue;
            var rect = Clip(PixelRect((RectTransform)button.transform, canvas.worldCamera), full);
            if (rect.width <= 0 || rect.height <= 0) continue;
            result.VisibleButtons++;
            if (rect.width < 24 || rect.height < 24) result.SmallButtonCandidates.Add(PathOf(button.transform, canvas.transform));
        }
        foreach (var scroll in canvas.GetComponentsInChildren<ScrollRect>())
            if (Visible(scroll.transform) && scroll.viewport != null && scroll.content != null)
                result.Scrolls.Add(new ScrollObservation
                {
                    Path = PathOf(scroll.transform, canvas.transform), ViewportHeight = scroll.viewport.rect.height,
                    ContentHeight = scroll.content.rect.height, VerticalPosition = scroll.verticalNormalizedPosition
                });
        return result;
    }
    static bool Visible(Transform transform)
    {
        if (!transform.gameObject.activeInHierarchy) return false;
        foreach (var group in transform.GetComponentsInParent<CanvasGroup>()) if (group.alpha <= .01f) return false;
        return true;
    }
    static Rect PixelRect(RectTransform transform, Camera camera)
    {
        var corners = new Vector3[4]; transform.GetWorldCorners(corners);
        var min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
        var max = min;
        for (int i = 1; i < 4; i++)
        {
            var point = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
            min = Vector2.Min(min, point); max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
    static Rect Clip(Rect a, Rect b)
    {
        float left = Mathf.Max(a.xMin, b.xMin), right = Mathf.Min(a.xMax, b.xMax);
        float bottom = Mathf.Max(a.yMin, b.yMin), top = Mathf.Min(a.yMax, b.yMax);
        return new Rect(left, bottom, Mathf.Max(0, right - left), Mathf.Max(0, top - bottom));
    }
    static string PathOf(Transform value, Transform root)
    {
        string path = value.name;
        while (value.parent != null && value.parent != root) { value = value.parent; path = value.name + "/" + path; }
        return path;
    }
}
#endif
