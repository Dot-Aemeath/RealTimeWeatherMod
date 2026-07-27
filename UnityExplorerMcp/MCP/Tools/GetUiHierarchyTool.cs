using System.Collections.Generic;

namespace UnityExplorerMcp.MCP.Tools
{
    internal class GetUiHierarchyTool : MainThreadTool
    {
        public override string Name => "get_ui_hierarchy";
        public override string Description => "Get a depth-limited UI hierarchy. Defaults to active root Canvases and 5 levels to control token use; use search_elements first for text/name lookup.";
        public override Dictionary<string, object> InputSchema => new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["canvasName"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Optional canvas name filter (case-insensitive)"
                },
                ["maxDepth"] = new Dictionary<string, object>
                {
                    ["type"] = "number",
                    ["description"] = "Maximum child depth (default: 5, max: 10)"
                },
                ["activeOnly"] = new Dictionary<string, object>
                {
                    ["type"] = "boolean",
                    ["description"] = "Only include active objects (default: true)"
                }
            }
        };

        public override object Execute(Dictionary<string, object> args) => null;

        public override McpContentItem ExecuteOnMain(Dictionary<string, object> args)
        {
            string canvasFilter = args != null && args.TryGetValue("canvasName", out var v) ? v?.ToString() : null;
            int maxDepth = 5;
            bool activeOnly = true;
            if (args != null)
            {
                if (args.TryGetValue("maxDepth", out var depth))
                {
                    if (depth is long l) maxDepth = (int)l;
                    else if (depth is double d) maxDepth = (int)d;
                    if (maxDepth < 1) maxDepth = 1;
                    if (maxDepth > 10) maxDepth = 10;
                }
                if (args.TryGetValue("activeOnly", out var active) && active is bool b)
                    activeOnly = b;
            }
            var hierarchy = Core.UiHierarchyTraverser.GetHierarchy(canvasFilter, maxDepth, activeOnly);
            var json = MCP.JsonHelper.Serialize(hierarchy);
            return new McpContentItem { type = "text", text = json };
        }
    }
}
