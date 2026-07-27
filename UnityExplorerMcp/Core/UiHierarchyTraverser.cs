using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityExplorerMcp.Models;

namespace UnityExplorerMcp.Core
{
    internal static class UiHierarchyTraverser
    {
        public static List<CanvasInfo> ListCanvases()
        {
            var canvases = Resources.FindObjectsOfTypeAll<Canvas>();
            var result = new List<CanvasInfo>();
            foreach (var c in canvases)
            {
                if (c.gameObject == null || c.gameObject.scene.name == null) continue;
                result.Add(new CanvasInfo
                {
                    name = c.gameObject.name,
                    path = GetPath(c.gameObject.transform),
                    sortingOrder = c.sortingOrder,
                    renderMode = c.renderMode.ToString(),
                    elementCount = CountChildren(c.transform)
                });
            }
            return result;
        }

        public static UiElementInfo GetHierarchy(
            string canvasName = null,
            int maxDepth = 5,
            bool activeOnly = true)
        {
            var root = new UiElementInfo
            {
                name = "Scene",
                path = "",
                children = new List<UiElementInfo>()
            };

            Canvas[] canvases;
            if (!string.IsNullOrEmpty(canvasName))
            {
                canvases = Resources.FindObjectsOfTypeAll<Canvas>();
            }
            else
            {
                canvases = Resources.FindObjectsOfTypeAll<Canvas>();
            }

            foreach (var canvas in canvases)
            {
                if (canvas.gameObject == null || canvas.gameObject.scene.name == null) continue;
                if (activeOnly && !canvas.gameObject.activeInHierarchy) continue;
                if (!string.IsNullOrEmpty(canvasName) &&
                    !ContainsIgnoreCase(canvas.gameObject.name, canvasName))
                    continue;
                if (string.IsNullOrEmpty(canvasName) && canvas.transform.parent != null &&
                    canvas.transform.parent.GetComponentInParent<Canvas>() != null)
                    continue;

                root.children.Add(TraverseTransform(
                    canvas.transform,
                    GetParentPath(canvas.transform),
                    0,
                    maxDepth,
                    activeOnly));
            }

            return root;
        }

        public static UiElementInfo TraverseTransform(Transform t, string parentPath)
        {
            return TraverseTransform(t, parentPath, 0, int.MaxValue, false);
        }

        private static UiElementInfo TraverseTransform(
            Transform t,
            string parentPath,
            int depth,
            int maxDepth,
            bool activeOnly)
        {
            var path = string.IsNullOrEmpty(parentPath)
                ? t.name
                : parentPath + "/" + t.name;

            var info = new UiElementInfo
            {
                instanceId = t.gameObject.GetInstanceID(),
                name = t.name,
                path = path,
                active = t.gameObject.activeInHierarchy,
                tag = t.tag,
                layer = t.gameObject.layer,
                childCount = t.childCount,
                componentCount = t.gameObject.GetComponents<Component>().Length,
                rectTransform = GetRectTransformInfo(t),
                children = new List<UiElementInfo>()
            };

            if (depth >= maxDepth) return info;

            for (int i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i);
                if (activeOnly && !child.gameObject.activeInHierarchy) continue;
                info.children.Add(TraverseTransform(child, path, depth + 1, maxDepth, activeOnly));
            }

            return info;
        }

        public static UiElementInfo GetElementByPath(string path, int instanceId = 0)
        {
            var go = FindGameObject(path, instanceId);
            if (go == null) return null;
            return CreateElementSummary(go);
        }

        public static string GetPath(Transform t)
        {
            if (t == null) return "";
            var sb = new StringBuilder(t.name);
            while (t.parent != null)
            {
                t = t.parent;
                sb.Insert(0, "/");
                sb.Insert(0, t.name);
            }
            return sb.ToString();
        }

        private static string GetParentPath(Transform transform)
        {
            return transform != null && transform.parent != null
                ? GetPath(transform.parent)
                : "";
        }

        public static UiSearchResult SearchElements(
            string query,
            string componentType = null,
            bool activeOnly = true,
            int maxResults = 25)
        {
            var elements = new List<UiElementInfo>();
            var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            int totalMatches = 0;
            string normalizedQuery = query ?? "";

            foreach (var go in allObjects)
            {
                if (go.scene.name == null) continue;
                if (go.hideFlags.HasFlag(HideFlags.HideInHierarchy)) continue;
                if (!(go.transform is RectTransform)) continue;
                if (activeOnly && !go.activeInHierarchy) continue;

                if (!string.IsNullOrEmpty(componentType))
                {
                    if (!HasComponent(go, componentType)) continue;
                }

                string text = GetUiText(go);
                string path = null;
                bool matches = string.IsNullOrEmpty(normalizedQuery) ||
                    ContainsIgnoreCase(go.name, normalizedQuery) ||
                    ContainsIgnoreCase(text, normalizedQuery);
                if (!matches)
                {
                    path = GetPath(go.transform);
                    matches = ContainsIgnoreCase(path, normalizedQuery);
                }
                if (!matches) continue;

                totalMatches++;
                if (elements.Count >= maxResults) continue;
                elements.Add(CreateElementSummary(go, path, text));
            }

            return new UiSearchResult
            {
                totalMatches = totalMatches,
                returned = elements.Count,
                truncated = totalMatches > elements.Count,
                elements = elements
            };
        }

        internal static UiElementInfo CreateElementSummary(GameObject go, string path = null, string text = null)
        {
            var components = go.GetComponents<Component>();
            var componentTypes = new List<string>();
            foreach (var component in components)
            {
                if (component != null) componentTypes.Add(component.GetType().Name);
            }

            return new UiElementInfo
            {
                instanceId = go.GetInstanceID(),
                name = go.name,
                path = path ?? GetPath(go.transform),
                active = go.activeInHierarchy,
                tag = go.tag,
                layer = go.layer,
                childCount = go.transform.childCount,
                componentCount = components.Length,
                text = text ?? GetUiText(go),
                componentTypes = componentTypes,
                interactiveAncestorPath = GetInteractiveAncestorPath(go.transform),
                rectTransform = GetRectTransformInfo(go.transform),
                screenRect = GetScreenRect(go.transform as RectTransform)
            };
        }

        private static bool ContainsIgnoreCase(string value, string query)
        {
            return !string.IsNullOrEmpty(value) &&
                value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetUiText(GameObject go)
        {
            var text = go.GetComponent<Text>();
            if (text != null) return text.text;
            var tmp = go.GetComponent<TMP_Text>();
            return tmp != null ? tmp.text : null;
        }

        private static string GetInteractiveAncestorPath(Transform transform)
        {
            var current = transform;
            while (current != null)
            {
                if (current.GetComponent<Selectable>() != null)
                    return GetPath(current);
                current = current.parent;
            }
            return null;
        }

        private static ScreenRectInfo GetScreenRect(RectTransform rectTransform)
        {
            if (rectTransform == null) return null;

            var corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            var canvas = rectTransform.GetComponentInParent<Canvas>();
            Camera camera = null;
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                camera = canvas.worldCamera;

            var first = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            float minX = first.x;
            float minY = first.y;
            float maxX = first.x;
            float maxY = first.y;
            for (int i = 1; i < corners.Length; i++)
            {
                var point = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
                minX = Math.Min(minX, point.x);
                minY = Math.Min(minY, point.y);
                maxX = Math.Max(maxX, point.x);
                maxY = Math.Max(maxY, point.y);
            }

            return new ScreenRectInfo
            {
                minX = minX,
                minY = minY,
                maxX = maxX,
                maxY = maxY,
                centerX = (minX + maxX) / 2f,
                centerY = (minY + maxY) / 2f,
                onScreen = maxX >= 0 && maxY >= 0 && minX <= Screen.width && minY <= Screen.height
            };
        }

        public static List<ComponentInfo> GetComponentDetails(string path, int instanceId = 0)
        {
            var go = FindGameObject(path, instanceId);
            if (go == null) return null;

            var components = go.GetComponents<Component>();
            var result = new List<ComponentInfo>();

            foreach (var comp in components)
            {
                if (comp == null) continue;
                var info = new ComponentInfo
                {
                    type = comp.GetType().Name,
                    enabled = true,
                    properties = new Dictionary<string, object>()
                };

                // Check if it's a Behaviour with enabled
                if (comp is Behaviour b) info.enabled = b.enabled;
                else if (comp is Renderer r) info.enabled = r.enabled;
                else if (comp is Collider col) info.enabled = col.enabled;

                // Get key properties based on type
                var compType = comp.GetType();

                if (comp is RectTransform rt)
                {
                    info.properties["anchoredPosition"] = rt.anchoredPosition.ToString();
                    info.properties["sizeDelta"] = rt.sizeDelta.ToString();
                    info.properties["anchorMin"] = rt.anchorMin.ToString();
                    info.properties["anchorMax"] = rt.anchorMax.ToString();
                    info.properties["pivot"] = rt.pivot.ToString();
                    info.properties["localPosition"] = rt.localPosition.ToString();
                    info.properties["position"] = rt.position.ToString();
                }
                else if (comp is Text txt)
                {
                    info.properties["text"] = txt.text;
                    info.properties["fontSize"] = txt.fontSize;
                    info.properties["color"] = txt.color.ToString();
                    info.properties["alignment"] = txt.alignment.ToString();
                    info.properties["font"] = txt.font?.name;
                }
                else if (comp is TextMeshProUGUI tmp)
                {
                    info.properties["text"] = tmp.text;
                    info.properties["fontSize"] = tmp.fontSize;
                    info.properties["color"] = tmp.color.ToString();
                    info.properties["alignment"] = tmp.alignment.ToString();
                    info.properties["font"] = tmp.font?.name;
                }
                else if (comp is Image img)
                {
                    info.properties["sprite"] = img.sprite?.name;
                    info.properties["color"] = img.color.ToString();
                    info.properties["raycastTarget"] = img.raycastTarget;
                }
                else if (comp is Button btn)
                {
                    info.properties["interactable"] = btn.interactable;
                    info.properties["transition"] = btn.transition.ToString();
                    info.properties["targetGraphic"] = btn.targetGraphic?.name;
                }
                else if (comp is Canvas canvas)
                {
                    info.properties["sortingOrder"] = canvas.sortingOrder;
                    info.properties["renderMode"] = canvas.renderMode.ToString();
                }
                else if (comp is CanvasGroup cg)
                {
                    info.properties["alpha"] = cg.alpha;
                    info.properties["interactable"] = cg.interactable;
                    info.properties["blocksRaycasts"] = cg.blocksRaycasts;
                }
                else if (comp is ScrollRect sr)
                {
                    info.properties["horizontal"] = sr.horizontal;
                    info.properties["vertical"] = sr.vertical;
                    info.properties["content"] = sr.content?.name;
                    info.properties["viewport"] = sr.viewport?.name;
                }
                else if (comp is InputField inf)
                {
                    info.properties["text"] = inf.text;
                    info.properties["interactable"] = inf.interactable;
                    info.properties["contentType"] = inf.contentType.ToString();
                }
                else if (comp is Toggle toggle)
                {
                    info.properties["isOn"] = toggle.isOn;
                    info.properties["interactable"] = toggle.interactable;
                }
                else if (comp is Slider slider)
                {
                    info.properties["value"] = slider.value;
                    info.properties["minValue"] = slider.minValue;
                    info.properties["maxValue"] = slider.maxValue;
                    info.properties["interactable"] = slider.interactable;
                }
                else if (comp is Dropdown dropdown)
                {
                    info.properties["value"] = dropdown.value;
                    info.properties["interactable"] = dropdown.interactable;
                    info.properties["options"] = dropdown.options.Count;
                }
                else if (comp is GridLayoutGroup glg)
                {
                    info.properties["cellSize"] = glg.cellSize.ToString();
                    info.properties["spacing"] = glg.spacing.ToString();
                    info.properties["constraint"] = glg.constraint.ToString();
                }
                else if (comp is VerticalLayoutGroup vlg)
                {
                    info.properties["spacing"] = vlg.spacing;
                    info.properties["padding"] = vlg.padding?.ToString();
                }
                else if (comp is HorizontalLayoutGroup hlg)
                {
                    info.properties["spacing"] = hlg.spacing;
                    info.properties["padding"] = hlg.padding?.ToString();
                }
                else if (comp is ContentSizeFitter csf)
                {
                    info.properties["horizontalFit"] = csf.horizontalFit.ToString();
                    info.properties["verticalFit"] = csf.verticalFit.ToString();
                }
                else
                {
                    // Generic: get limited public properties
                    int propCount = 0;
                    foreach (var prop in compType.GetProperties(
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Instance))
                    {
                        if (prop.GetIndexParameters().Length > 0) continue;
                        if (!prop.CanRead) continue;
                        if (prop.Name == "name" || prop.Name == "hideFlags" ||
                            prop.Name == "gameObject" || prop.Name == "transform" ||
                            prop.Name == "tag")
                            continue;
                        if (propCount >= 20) { info.properties["..."] = "more properties omitted"; break; }
                        try
                        {
                            var val = prop.GetValue(comp);
                            if (val != null && (val is string || val is int ||
                                val is float || val is double || val is bool ||
                                val is long || val is Vector2 || val is Vector3 ||
                                val is Vector4 || val is Color || val is Enum))
                            {
                                info.properties[prop.Name] = val.ToString();
                            }
                        }
                        catch { }
                    }
                }

                result.Add(info);
            }

            return result;
        }

        internal static GameObject FindGameObject(string path, int instanceId)
        {
            if (instanceId == 0 && !string.IsNullOrEmpty(path))
            {
                var direct = GameObject.Find(path);
                if (direct != null) return direct;
            }

            var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (var obj in allObjects)
            {
                if (obj.scene.name == null) continue;
                if (instanceId != 0 && obj.GetInstanceID() == instanceId)
                    return obj;
                if (instanceId == 0 && !string.IsNullOrEmpty(path) &&
                    (GetPath(obj.transform) == path || obj.name == path))
                    return obj;
            }
            return null;
        }

        private static bool HasComponent(GameObject go, string typeName)
        {
            var comps = go.GetComponents<Component>();
            foreach (var c in comps)
            {
                if (c == null) continue;
                var name = c.GetType().Name;
                if (string.Equals(name, typeName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.GetType().FullName, typeName, StringComparison.OrdinalIgnoreCase))
                    return true;
                // "Text" should also match TextMeshProUGUI / TMP_Text
                if (string.Equals(typeName, "Text", StringComparison.OrdinalIgnoreCase) &&
                    c is TMP_Text)
                    return true;
                // "Image" should also match RawImage
                if (string.Equals(typeName, "Image", StringComparison.OrdinalIgnoreCase) &&
                    name == "RawImage")
                    return true;
                // "Button" should also match Toggle-like behaviors if relevant
            }
            return false;
        }

        private static RectTransformInfo GetRectTransformInfo(Transform t)
        {
            var info = new RectTransformInfo
            {
                scaleX = 1f, scaleY = 1f,
                anchorMinX = 0f, anchorMinY = 0f,
                anchorMaxX = 1f, anchorMaxY = 1f,
                pivotX = 0.5f, pivotY = 0.5f
            };

            if (t is RectTransform rt)
            {
                info.posX = rt.position.x;
                info.posY = rt.position.y;
                info.width = rt.rect.width;
                info.height = rt.rect.height;
                info.anchorMinX = rt.anchorMin.x;
                info.anchorMinY = rt.anchorMin.y;
                info.anchorMaxX = rt.anchorMax.x;
                info.anchorMaxY = rt.anchorMax.y;
                info.pivotX = rt.pivot.x;
                info.pivotY = rt.pivot.y;
                info.localPosX = rt.localPosition.x;
                info.localPosY = rt.localPosition.y;
                info.scaleX = rt.localScale.x;
                info.scaleY = rt.localScale.y;
                info.rotation = rt.localEulerAngles.z;
            }
            else
            {
                info.posX = t.position.x;
                info.posY = t.position.y;
                info.width = 0;
                info.height = 0;
                info.localPosX = t.localPosition.x;
                info.localPosY = t.localPosition.y;
                info.scaleX = t.localScale.x;
                info.scaleY = t.localScale.y;
            }

            return info;
        }

        private static int CountChildren(Transform t)
        {
            int count = 1;
            for (int i = 0; i < t.childCount; i++)
                count += CountChildren(t.GetChild(i));
            return count;
        }

        public static List<UiElementInfo> GetMouseElements(int maxResults = 5)
        {
            return GetElementsAtScreenPoint(Input.mousePosition, maxResults);
        }

        public static List<UiElementInfo> GetElementsAtScreenPoint(Vector2 screenPoint, int maxResults = 5)
        {
            var matches = new List<UiElementInfo>();
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            var seen = new HashSet<int>();

            if (eventSystem != null)
            {
                var data = new UnityEngine.EventSystems.PointerEventData(eventSystem)
                {
                    position = screenPoint
                };

                var results = new List<UnityEngine.EventSystems.RaycastResult>();
                eventSystem.RaycastAll(data, results);
                foreach (var hit in results)
                {
                    if (hit.gameObject == null) continue;
                    AddScreenPointMatch(matches, seen, hit.gameObject, maxResults);
                    if (matches.Count >= maxResults) return matches;
                }
            }

            // Some games use temporary/non-standard canvases for which EventSystem.RaycastAll
            // produces no result. Fall back to visible raycast-target Graphics whose actual
            // screen rectangle contains the point.
            var graphicHits = new List<Graphic>();
            foreach (var graphic in Resources.FindObjectsOfTypeAll<Graphic>())
            {
                if (graphic == null || !graphic.enabled || !graphic.raycastTarget) continue;
                if (graphic.gameObject == null || graphic.gameObject.scene.name == null) continue;
                if (!graphic.gameObject.activeInHierarchy || graphic.rectTransform == null) continue;

                var canvas = graphic.canvas;
                Camera camera = null;
                if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    camera = canvas.worldCamera;

                if (RectTransformUtility.RectangleContainsScreenPoint(
                    graphic.rectTransform,
                    screenPoint,
                    camera))
                {
                    graphicHits.Add(graphic);
                }
            }

            graphicHits.Sort((left, right) =>
            {
                int leftOrder = left.canvas != null ? left.canvas.sortingOrder : 0;
                int rightOrder = right.canvas != null ? right.canvas.sortingOrder : 0;
                int orderComparison = rightOrder.CompareTo(leftOrder);
                if (orderComparison != 0) return orderComparison;
                return right.depth.CompareTo(left.depth);
            });

            foreach (var graphic in graphicHits)
            {
                AddScreenPointMatch(matches, seen, graphic.gameObject, maxResults);
                if (matches.Count >= maxResults) break;
            }

            return matches;
        }

        private static void AddScreenPointMatch(
            List<UiElementInfo> matches,
            HashSet<int> seen,
            GameObject gameObject,
            int maxResults)
        {
            if (gameObject == null || matches.Count >= maxResults) return;
            int id = gameObject.GetInstanceID();
            if (seen.Add(id))
                matches.Add(CreateElementSummary(gameObject));

            if (matches.Count >= maxResults) return;
            var selectable = gameObject.GetComponentInParent<Selectable>();
            if (selectable == null || selectable.gameObject == gameObject) return;

            int selectableId = selectable.gameObject.GetInstanceID();
            if (seen.Add(selectableId))
                matches.Add(CreateElementSummary(selectable.gameObject));
        }
    }
}
