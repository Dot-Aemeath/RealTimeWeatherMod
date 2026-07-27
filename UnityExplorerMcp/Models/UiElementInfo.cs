using System.Collections.Generic;

namespace UnityExplorerMcp.Models
{
    internal class UiElementInfo
    {
        public int instanceId { get; set; }
        public string name { get; set; }
        public string path { get; set; }
        public bool active { get; set; }
        public string tag { get; set; }
        public int layer { get; set; }
        public int childCount { get; set; }
        public int componentCount { get; set; }
        public string text { get; set; }
        public List<string> componentTypes { get; set; }
        public string interactiveAncestorPath { get; set; }
        public RectTransformInfo rectTransform { get; set; }
        public ScreenRectInfo screenRect { get; set; }
        public List<UiElementInfo> children { get; set; }
    }

    internal class RectTransformInfo
    {
        public float posX { get; set; }
        public float posY { get; set; }
        public float width { get; set; }
        public float height { get; set; }
        public float anchorMinX { get; set; }
        public float anchorMinY { get; set; }
        public float anchorMaxX { get; set; }
        public float anchorMaxY { get; set; }
        public float pivotX { get; set; }
        public float pivotY { get; set; }
        public float localPosX { get; set; }
        public float localPosY { get; set; }
        public float scaleX { get; set; }
        public float scaleY { get; set; }
        public float rotation { get; set; }
    }

    internal class ScreenRectInfo
    {
        public float minX { get; set; }
        public float minY { get; set; }
        public float maxX { get; set; }
        public float maxY { get; set; }
        public float centerX { get; set; }
        public float centerY { get; set; }
        public bool onScreen { get; set; }
    }

    internal class UiSearchResult
    {
        public int totalMatches { get; set; }
        public int returned { get; set; }
        public bool truncated { get; set; }
        public List<UiElementInfo> elements { get; set; }
    }

    internal class ComponentInfo
    {
        public string type { get; set; }
        public bool enabled { get; set; }
        public Dictionary<string, object> properties { get; set; }
    }

    internal class CanvasInfo
    {
        public string name { get; set; }
        public string path { get; set; }
        public int sortingOrder { get; set; }
        public string renderMode { get; set; }
        public int elementCount { get; set; }
    }
}
