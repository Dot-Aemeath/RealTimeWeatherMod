using System.Collections.Generic;

namespace UnityExplorerMcp.MCP.Tools
{
    internal class GetMouseElementTool : MainThreadTool
    {
        public override string Name => "get_mouse_element";
        public override string Description => "Get UI elements under the mouse across every active Canvas, ordered topmost first. Returns paths, instance IDs, text, components, and screen bounds.";
        public override Dictionary<string, object> InputSchema => new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["maxResults"] = new Dictionary<string, object>
                {
                    ["type"] = "number",
                    ["description"] = "Maximum hit stack depth (default: 5, max: 20)"
                }
            }
        };

        public override object Execute(Dictionary<string, object> args) => null;

        public override McpContentItem ExecuteOnMain(Dictionary<string, object> args)
        {
            int maxResults = 5;
            if (args != null && args.TryGetValue("maxResults", out var value))
            {
                if (value is long l) maxResults = (int)l;
                else if (value is double d) maxResults = (int)d;
                if (maxResults < 1) maxResults = 1;
                if (maxResults > 20) maxResults = 20;
            }

            var results = Core.UiHierarchyTraverser.GetMouseElements(maxResults);
            if (results.Count == 0)
                return new McpContentItem { type = "text", text = "{\"found\":false}" };

            var json = MCP.JsonHelper.Serialize(new
            {
                found = true,
                mousePosition = UnityEngine.Input.mousePosition.ToString(),
                elements = results
            });
            return new McpContentItem { type = "text", text = json };
        }
    }
}
