using System;
using System.Collections.Generic;
using UnityEngine;
using UnityExplorerMcp.Models;

namespace UnityExplorerMcp.Core
{
    internal static class UiLayoutPreviewService
    {
        private sealed class RectTransformState
        {
            public RectTransform target;
            public Vector2 anchorMin;
            public Vector2 anchorMax;
            public Vector2 pivot;
            public Vector2 anchoredPosition;
            public Vector2 sizeDelta;
            public Vector3 localScale;
            public Vector3 localEulerAngles;

            public static RectTransformState Capture(RectTransform target)
            {
                return new RectTransformState
                {
                    target = target,
                    anchorMin = target.anchorMin,
                    anchorMax = target.anchorMax,
                    pivot = target.pivot,
                    anchoredPosition = target.anchoredPosition,
                    sizeDelta = target.sizeDelta,
                    localScale = target.localScale,
                    localEulerAngles = target.localEulerAngles
                };
            }

            public void Restore()
            {
                if (target == null) return;
                target.anchorMin = anchorMin;
                target.anchorMax = anchorMax;
                target.pivot = pivot;
                target.anchoredPosition = anchoredPosition;
                target.sizeDelta = sizeDelta;
                target.localScale = localScale;
                target.localEulerAngles = localEulerAngles;
            }
        }

        private static readonly Dictionary<string, Dictionary<int, RectTransformState>> Sessions =
            new Dictionary<string, Dictionary<int, RectTransformState>>();
        private const int MaxSessions = 64;
        private const int MaxElementsPerSession = 256;

        public static void PruneDestroyedTargets()
        {
            foreach (var id in new List<string>(Sessions.Keys))
            {
                var states = Sessions[id];
                foreach (var key in new List<int>(states.Keys))
                    if (states[key].target == null) states.Remove(key);
                if (states.Count == 0) Sessions.Remove(id);
            }
        }

        public static object Apply(
            string previewId,
            RectTransform target,
            RectTransform reference,
            string placement,
            float gap,
            float? anchoredX,
            float? anchoredY,
            float? width,
            float? height,
            bool matchWidth,
            bool matchHeight)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            PruneDestroyedTargets();
            if (string.IsNullOrEmpty(previewId)) previewId = Guid.NewGuid().ToString("N");

            if (!Sessions.TryGetValue(previewId, out var states))
            {
                if (Sessions.Count >= MaxSessions)
                    throw new InvalidOperationException("Preview session limit reached; roll back an existing preview first");
                states = new Dictionary<int, RectTransformState>();
                Sessions[previewId] = states;
            }

            int instanceId = target.gameObject.GetInstanceID();
            if (!states.ContainsKey(instanceId))
            {
                if (states.Count >= MaxElementsPerSession)
                    throw new InvalidOperationException("Preview element limit reached; roll back this preview first");
                states[instanceId] = RectTransformState.Capture(target);
            }

            if (reference != null)
            {
                if (matchWidth) width = reference.rect.width;
                if (matchHeight) height = reference.rect.height;
            }

            if (width.HasValue)
                target.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width.Value);
            if (height.HasValue)
                target.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height.Value);

            if (reference != null)
                PlaceRelative(target, reference, placement, gap);

            if (anchoredX.HasValue || anchoredY.HasValue)
            {
                target.anchoredPosition = new Vector2(
                    anchoredX ?? target.anchoredPosition.x,
                    anchoredY ?? target.anchoredPosition.y);
            }

            Canvas.ForceUpdateCanvases();
            return new
            {
                previewId,
                changedElementCount = states.Count,
                element = UiHierarchyTraverser.CreateElementSummary(target.gameObject)
            };
        }

        public static object Rollback(string previewId)
        {
            int restored = 0;
            if (string.IsNullOrEmpty(previewId))
            {
                foreach (var session in Sessions.Values)
                    restored += RestoreStates(session);
                Sessions.Clear();
                Canvas.ForceUpdateCanvases();
                return new { rolledBackAll = true, restoredElementCount = restored };
            }

            if (!Sessions.TryGetValue(previewId, out var states))
                return new { previewId, found = false, restoredElementCount = 0 };

            restored = RestoreStates(states);
            Sessions.Remove(previewId);
            Canvas.ForceUpdateCanvases();
            return new { previewId, found = true, restoredElementCount = restored };
        }

        private static int RestoreStates(Dictionary<int, RectTransformState> states)
        {
            int restored = 0;
            foreach (var state in states.Values)
            {
                if (state.target == null) continue;
                state.Restore();
                restored++;
            }
            return restored;
        }

        private static void PlaceRelative(
            RectTransform target,
            RectTransform reference,
            string placement,
            float gap)
        {
            var targetParent = target.parent as RectTransform;
            if (targetParent == null)
                throw new InvalidOperationException("Target RectTransform has no RectTransform parent");

            string normalized = string.IsNullOrEmpty(placement)
                ? "same"
                : placement.ToLowerInvariant();
            var referencePoint = reference.rect.center;
            switch (normalized)
            {
                case "same":
                case "center":
                    break;
                case "below":
                    referencePoint.y -= (reference.rect.height + target.rect.height) * 0.5f + gap;
                    break;
                case "above":
                    referencePoint.y += (reference.rect.height + target.rect.height) * 0.5f + gap;
                    break;
                case "left":
                    referencePoint.x -= (reference.rect.width + target.rect.width) * 0.5f + gap;
                    break;
                case "right":
                    referencePoint.x += (reference.rect.width + target.rect.width) * 0.5f + gap;
                    break;
                default:
                    throw new ArgumentException("placement must be same, center, below, above, left, or right");
            }

            var desiredWorldCenter = reference.TransformPoint(referencePoint);
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            var currentWorldCenter = (corners[0] + corners[2]) * 0.5f;
            target.position += desiredWorldCenter - currentWorldCenter;
        }
    }
}
