using System;
using System.Collections.Generic;
using UnityEngine;
using UnityExplorerMcp.Core;

namespace UnityExplorerMcp.MCP.Tools
{
    internal sealed class InspectScreenPointTool : MainThreadTool
    {
        public override string Name => "inspect_screen_point";
        public override string Description =>
            "Inspect the UI hit stack at explicit screenshot coordinates. Defaults to top-left origin to match PNG screenshots.";

        public override Dictionary<string, object> InputSchema => new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["x"] = new Dictionary<string, object> { ["type"] = "number" },
                ["y"] = new Dictionary<string, object> { ["type"] = "number" },
                ["origin"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "top-left (default) or bottom-left"
                },
                ["maxResults"] = new Dictionary<string, object>
                {
                    ["type"] = "number",
                    ["description"] = "Maximum hit stack depth (default: 5, max: 20)"
                }
            },
            ["required"] = new List<string> { "x", "y" }
        };

        public override object Execute(Dictionary<string, object> args) => null;

        public override McpContentItem ExecuteOnMain(Dictionary<string, object> args)
        {
            float? x = ToolArgumentReader.GetOptionalFloat(args, "x");
            float? y = ToolArgumentReader.GetOptionalFloat(args, "y");
            if (!x.HasValue || !y.HasValue)
                return Text(new { error = "x and y are required" });

            string origin = ToolArgumentReader.GetString(args, "origin") ?? "top-left";
            float unityY;
            if (string.Equals(origin, "top-left", StringComparison.OrdinalIgnoreCase))
                unityY = Screen.height - y.Value;
            else if (string.Equals(origin, "bottom-left", StringComparison.OrdinalIgnoreCase))
                unityY = y.Value;
            else
                return Text(new { error = "origin must be top-left or bottom-left" });

            int maxResults = Math.Max(1, Math.Min(20, ToolArgumentReader.GetInt(args, "maxResults", 5)));
            var unityPoint = new Vector2(x.Value, unityY);
            var elements = UiHierarchyTraverser.GetElementsAtScreenPoint(unityPoint, maxResults);
            return Text(new
            {
                found = elements.Count > 0,
                requestedPoint = new { x = x.Value, y = y.Value, origin },
                unityScreenPoint = new { x = unityPoint.x, y = unityPoint.y, origin = "bottom-left" },
                screen = new { width = Screen.width, height = Screen.height },
                elements
            });
        }

        private static McpContentItem Text(object value)
        {
            return new McpContentItem { type = "text", text = MCP.JsonHelper.Serialize(value) };
        }
    }
}
