using System;
using System.Collections.Generic;
using UnityEngine;
using UnityExplorerMcp.Core;
using UnityExplorerMcp.Models;

namespace UnityExplorerMcp.MCP.Tools
{
    internal sealed class CompareUiElementsTool : MainThreadTool
    {
        public override string Name => "compare_ui_elements";
        public override string Description =>
            "Compare two UI elements: screen centers, sizes, alignment deltas, edge gaps, and overlap. Useful for matching and spacing controls.";

        public override Dictionary<string, object> InputSchema => new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["pathA"] = new Dictionary<string, object> { ["type"] = "string" },
                ["instanceIdA"] = new Dictionary<string, object> { ["type"] = "number" },
                ["pathB"] = new Dictionary<string, object> { ["type"] = "string" },
                ["instanceIdB"] = new Dictionary<string, object> { ["type"] = "number" }
            }
        };

        public override object Execute(Dictionary<string, object> args) => null;

        public override McpContentItem ExecuteOnMain(Dictionary<string, object> args)
        {
            var objectA = UiHierarchyTraverser.FindGameObject(
                ToolArgumentReader.GetString(args, "pathA"),
                ToolArgumentReader.GetInt(args, "instanceIdA"));
            var objectB = UiHierarchyTraverser.FindGameObject(
                ToolArgumentReader.GetString(args, "pathB"),
                ToolArgumentReader.GetInt(args, "instanceIdB"));
            if (objectA == null || objectB == null)
                return Text(new { error = "Both UI elements must be found" });

            var a = UiHierarchyTraverser.CreateElementSummary(objectA);
            var b = UiHierarchyTraverser.CreateElementSummary(objectB);
            if (a.screenRect == null || b.screenRect == null)
                return Text(new { error = "Both elements must have RectTransforms" });

            float overlapWidth = Math.Max(
                0f,
                Math.Min(a.screenRect.maxX, b.screenRect.maxX) -
                Math.Max(a.screenRect.minX, b.screenRect.minX));
            float overlapHeight = Math.Max(
                0f,
                Math.Min(a.screenRect.maxY, b.screenRect.maxY) -
                Math.Max(a.screenRect.minY, b.screenRect.minY));

            var result = new
            {
                elementA = Compact(a),
                elementB = Compact(b),
                delta = new
                {
                    centerX = a.screenRect.centerX - b.screenRect.centerX,
                    centerY = a.screenRect.centerY - b.screenRect.centerY,
                    width = Width(a.screenRect) - Width(b.screenRect),
                    height = Height(a.screenRect) - Height(b.screenRect),
                    moveAToMatchBCenterX = b.screenRect.centerX - a.screenRect.centerX,
                    moveAToMatchBCenterY = b.screenRect.centerY - a.screenRect.centerY
                },
                edges = new
                {
                    aLeftMinusBLeft = a.screenRect.minX - b.screenRect.minX,
                    aRightMinusBRight = a.screenRect.maxX - b.screenRect.maxX,
                    aBottomMinusBBottom = a.screenRect.minY - b.screenRect.minY,
                    aTopMinusBTop = a.screenRect.maxY - b.screenRect.maxY
                },
                overlap = new
                {
                    intersects = overlapWidth > 0f && overlapHeight > 0f,
                    width = overlapWidth,
                    height = overlapHeight
                }
            };
            return Text(result);
        }

        private static object Compact(UiElementInfo element)
        {
            return new
            {
                element.instanceId,
                element.name,
                element.path,
                element.rectTransform,
                element.screenRect
            };
        }

        private static float Width(ScreenRectInfo rect) => rect.maxX - rect.minX;
        private static float Height(ScreenRectInfo rect) => rect.maxY - rect.minY;

        private static McpContentItem Text(object value)
        {
            return new McpContentItem { type = "text", text = MCP.JsonHelper.Serialize(value) };
        }
    }
}
