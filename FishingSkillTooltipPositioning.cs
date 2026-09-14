using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TrollingFishing;

internal static class FishingSkillTooltipPositioning
{
    private static readonly ConditionalWeakTable<UITooltip, TooltipBinding> TooltipBindings = new();
    private static readonly ConditionalWeakTable<SkillsDialog, List<UITooltip>> DialogTooltips = new();
    private static readonly Vector3[] Corners = new Vector3[4];
    private static bool _layoutFailureLogged;

    internal static void Bind(SkillsDialog dialog, UITooltip tooltip)
    {
        Canvas? canvas = tooltip.GetComponentInParent<Canvas>();
        RectTransform? panel = FindSkillPanel(dialog);
        if (canvas == null || canvas.transform is not RectTransform canvasRect || panel == null)
        {
            return;
        }

        RectTransform? row = tooltip.transform as RectTransform;
        for (Transform current = tooltip.transform;
             current != null && current != dialog.m_listRoot;
             current = current.parent)
        {
            if (current.parent == dialog.m_listRoot)
            {
                row = current as RectTransform;
                break;
            }
        }

        if (row == null)
        {
            return;
        }

        TooltipBindings.Remove(tooltip);
        TooltipBindings.Add(tooltip, new TooltipBinding(dialog, panel, row, canvas, canvasRect, tooltip));
        DialogTooltips.GetValue(dialog, _ => new List<UITooltip>(1)).Add(tooltip);

        // Keep gamepad tooltips on the canvas and outside the skill list's scroll mask.
        FishingGameAccess.TooltipAnchor(tooltip) = canvasRect;
        FishingGameAccess.TooltipPosition(tooltip) = Vector2.zero;
    }

    internal static void ClearBindings(SkillsDialog dialog)
    {
        if (!DialogTooltips.TryGetValue(dialog, out List<UITooltip>? tooltips))
        {
            return;
        }

        // SkillsDialog reuses rows, so restore their vanilla positioning before Setup resets them.
        foreach (UITooltip tooltip in tooltips)
        {
            if (tooltip != null && TooltipBindings.TryGetValue(tooltip, out TooltipBinding? binding))
            {
                FishingGameAccess.TooltipAnchor(tooltip) = binding.OriginalAnchor;
                FishingGameAccess.TooltipPosition(tooltip) = binding.OriginalFixedPosition;
                if (FishingGameAccess.CurrentTooltip() == tooltip)
                {
                    UITooltip.HideTooltip();
                }
            }

            TooltipBindings.Remove(tooltip!);
        }

        tooltips.Clear();
    }

    internal static void UpdateVisibleTooltip(UITooltip tooltip)
    {
        if (FishingGameAccess.CurrentTooltip() != tooltip || FishingGameAccess.TooltipRoot() == null ||
            !TooltipBindings.TryGetValue(tooltip, out TooltipBinding? binding))
        {
            return;
        }

        try
        {
            if (binding.Dialog == null || !binding.Dialog.isActiveAndEnabled ||
                binding.Row == null || !binding.Row.gameObject.activeInHierarchy)
            {
                UITooltip.HideTooltip();
                return;
            }

            GameObject root = FishingGameAccess.TooltipRoot();
            if (!root.activeSelf)
            {
                return;
            }

            if (binding.View == null || binding.View.Root != root)
            {
                binding.View = TooltipView.Create(root, binding.CanvasRect);
                if (binding.View == null)
                {
                    RestoreVanillaPositioning(tooltip, binding);
                    TooltipBindings.Remove(tooltip);
                    return;
                }
            }

            Camera? camera = binding.Canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : binding.Canvas.worldCamera;
            Rect safeArea = Screen.safeArea;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    binding.CanvasRect,
                    safeArea.min,
                    camera,
                    out Vector2 screenMin) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    binding.CanvasRect,
                    safeArea.max,
                    camera,
                    out Vector2 screenMax))
            {
                return;
            }

            Rect viewport = Rect.MinMaxRect(screenMin.x, screenMin.y, screenMax.x, screenMax.y);
            Rect panelBounds = GetCanvasBounds(binding.Panel, binding.CanvasRect);
            Rect rowBounds = GetCanvasBounds(binding.Row, binding.CanvasRect);
            binding.View.UpdateLayout();
            float scale = FishingSkillTooltipLayout.GetScale(viewport, binding.View.Size);
            Vector2 topLeft = FishingSkillTooltipLayout.GetTopLeft(
                viewport,
                panelBounds,
                rowBounds.yMax,
                binding.View.Size * scale);
            binding.View.Panel.localScale = new Vector3(scale, scale, 1f);
            binding.View.Panel.position = binding.CanvasRect.TransformPoint(
                new Vector3(topLeft.x, topLeft.y, 0f));
        }
        catch (Exception exception)
        {
            RestoreVanillaPositioning(tooltip, binding);
            TooltipBindings.Remove(tooltip);
            UITooltip.HideTooltip();
            if (!_layoutFailureLogged)
            {
                _layoutFailureLogged = true;
                TrollingFishingPlugin.ModLogger.LogWarning(
                    "Could not position the Fishing skill tooltip: " +
                    exception.GetBaseException().Message);
            }
        }
    }

    private static void RestoreVanillaPositioning(UITooltip tooltip, TooltipBinding binding)
    {
        FishingGameAccess.TooltipAnchor(tooltip) = binding.OriginalAnchor;
        FishingGameAccess.TooltipPosition(tooltip) = binding.OriginalFixedPosition;
    }

    private static RectTransform? FindSkillPanel(SkillsDialog dialog)
    {
        if (dialog.m_listRoot == null)
        {
            return null;
        }

        for (Transform current = dialog.m_listRoot.parent;
             current != null && current != dialog.transform;
             current = current.parent)
        {
            if (current.parent == dialog.transform && current is RectTransform frame)
            {
                return frame.Find("bkg") as RectTransform ?? frame;
            }
        }

        return null;
    }

    private static Rect GetCanvasBounds(RectTransform rect, RectTransform canvas)
    {
        rect.GetWorldCorners(Corners);
        Vector2 min = canvas.InverseTransformPoint(Corners[0]);
        Vector2 max = min;
        for (int index = 1; index < Corners.Length; index++)
        {
            Vector2 point = canvas.InverseTransformPoint(Corners[index]);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private sealed class TooltipBinding
    {
        internal readonly SkillsDialog Dialog;
        internal readonly RectTransform Panel;
        internal readonly RectTransform Row;
        internal readonly Canvas Canvas;
        internal readonly RectTransform CanvasRect;
        internal readonly RectTransform? OriginalAnchor;
        internal readonly Vector2 OriginalFixedPosition;
        internal TooltipView? View;

        internal TooltipBinding(
            SkillsDialog dialog,
            RectTransform panel,
            RectTransform row,
            Canvas canvas,
            RectTransform canvasRect,
            UITooltip tooltip)
        {
            Dialog = dialog;
            Panel = panel;
            Row = row;
            Canvas = canvas;
            CanvasRect = canvasRect;
            OriginalAnchor = FishingGameAccess.TooltipAnchor(tooltip);
            OriginalFixedPosition = FishingGameAccess.TooltipPosition(tooltip);
        }
    }

    private sealed class TooltipView
    {
        private const float Padding = FishingSkillTooltipLayout.Padding;
        private const float TopicGap = 8f;

        internal readonly GameObject Root;
        internal readonly RectTransform Panel;
        private readonly TMP_Text _body;
        private readonly TMP_Text? _topic;
        private string? _bodyText;
        private string? _topicText;

        internal Vector2 Size => Panel.sizeDelta;

        private TooltipView(GameObject root, RectTransform panel, TMP_Text body, TMP_Text? topic)
        {
            Root = root;
            Panel = panel;
            _body = body;
            _topic = topic;
        }

        internal static TooltipView? Create(GameObject root, RectTransform canvas)
        {
            TMP_Text? body = Utils.FindChild(root.transform, "Text")?.GetComponent<TMP_Text>();
            TMP_Text? topic = Utils.FindChild(root.transform, "Topic")?.GetComponent<TMP_Text>();
            if (body == null || body.font == null || body.transform == root.transform)
            {
                return null;
            }

            root.transform.SetParent(canvas, false);
            root.transform.localScale = Vector3.one;
            root.transform.localRotation = Quaternion.identity;
            DisableAutomaticLayout(root);
            CanvasGroup group = root.GetComponent<CanvasGroup>() ?? root.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            GameObject panelObject = new(
                "TrollingFishing_SkillTooltip",
                typeof(RectTransform),
                typeof(Image));
            RectTransform panel = (RectTransform)panelObject.transform;
            panel.SetParent(root.transform, false);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0f, 1f);
            Image background = panelObject.GetComponent<Image>();
            background.color = new Color(0.055f, 0.045f, 0.04f, 0.94f);
            background.raycastTarget = false;

            PrepareText(body, panel, TextAlignmentOptions.TopLeft);
            if (topic != null && topic != body)
            {
                PrepareText(topic, panel, TextAlignmentOptions.Top);
            }

            for (int index = 0; index < root.transform.childCount; index++)
            {
                Transform child = root.transform.GetChild(index);
                if (child != panel)
                {
                    child.gameObject.SetActive(false);
                }
            }

            panel.SetAsFirstSibling();
            return new TooltipView(root, panel, body, topic == body ? null : topic);
        }

        internal void UpdateLayout()
        {
            if (_bodyText == _body.text && _topicText == _topic?.text)
            {
                return;
            }

            _bodyText = _body.text;
            _topicText = _topic?.text;
            const float innerWidth = FishingSkillTooltipLayout.BodyWidth;
            float y = Padding;
            if (_topic != null)
            {
                bool hasTopic = !string.IsNullOrWhiteSpace(_topicText);
                _topic.gameObject.SetActive(hasTopic);
                if (hasTopic)
                {
                    y += SetTextRect(_topic, innerWidth, y) + TopicGap;
                }
            }

            y += SetTextRect(_body, innerWidth, y);
            Panel.sizeDelta = new Vector2(FishingSkillTooltipLayout.Width, y + Padding);
        }

        private static void PrepareText(TMP_Text text, RectTransform panel, TextAlignmentOptions alignment)
        {
            DisableAutomaticLayout(text.gameObject);
            RectTransform rect = text.rectTransform;
            rect.SetParent(panel, false);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.enableAutoSizing = false;
            text.margin = Vector4.zero;
            text.raycastTarget = false;
            text.gameObject.SetActive(true);
        }

        private static void DisableAutomaticLayout(GameObject target)
        {
            LayoutGroup? layout = target.GetComponent<LayoutGroup>();
            ContentSizeFitter? fitter = target.GetComponent<ContentSizeFitter>();
            AspectRatioFitter? aspect = target.GetComponent<AspectRatioFitter>();
            if (layout != null)
            {
                layout.enabled = false;
            }

            if (fitter != null)
            {
                fitter.enabled = false;
            }

            if (aspect != null)
            {
                aspect.enabled = false;
            }
        }

        private static float SetTextRect(TMP_Text text, float width, float y)
        {
            float height = Mathf.Max(
                1f,
                Mathf.Ceil(text.GetPreferredValues(text.text, width, float.PositiveInfinity).y));
            text.rectTransform.anchoredPosition = new Vector2(Padding, -y);
            text.rectTransform.sizeDelta = new Vector2(width, height);
            return height;
        }
    }
}

[HarmonyPatch(typeof(UITooltip), "LateUpdate")]
internal static class FishingSkillTooltipPositionPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(UITooltip __instance)
    {
        FishingSkillTooltipPositioning.UpdateVisibleTooltip(__instance);
    }
}

internal static class FishingSkillTooltipLayout
{
    internal const float BodyWidth = 250f;
    internal const float Padding = 16f;
    internal const float Width = BodyWidth + Padding * 2f;
    private const float Gap = 8f;
    private const float Margin = 12f;

    internal static float GetScale(Rect viewport, Vector2 size)
    {
        float widthScale = Mathf.Max(1f, viewport.width - Margin * 2f) / Mathf.Max(1f, size.x);
        float heightScale = Mathf.Max(1f, viewport.height - Margin * 2f) / Mathf.Max(1f, size.y);
        return Mathf.Min(1f, Mathf.Min(widthScale, heightScale));
    }

    internal static Vector2 GetTopLeft(
        Rect viewport,
        Rect skillPanel,
        float rowTop,
        Vector2 size)
    {
        float minX = viewport.xMin + Margin;
        float maxY = viewport.yMax - Margin;
        float x = Mathf.Clamp(
            skillPanel.xMin - Gap - size.x,
            minX,
            Mathf.Max(minX, viewport.xMax - Margin - size.x));
        float y = Mathf.Clamp(
            rowTop,
            Mathf.Min(maxY, viewport.yMin + Margin + size.y),
            maxY);
        return new Vector2(x, y);
    }
}
