using System.Collections.Generic;
using UnityExplorerMcp.Core;

namespace UnityExplorerMcp.MCP.Tools
{
    internal class InspectElementTool : MainThreadTool
    {
        public override string Name => "inspect_element";
        public override string Description => "Inspect one exact UI element by path or instanceId. Prefer instanceId from search_elements when names are duplicated. Returns a compact element summary and component properties.";
        public override Dictionary<string, object> InputSchema => new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["path"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Full hierarchy path or object name. Optional when instanceId is supplied."
                },
                ["instanceId"] = new Dictionary<string, object>
                {
                    ["type"] = "number",
                    ["description"] = "Exact runtime instance ID returned by search_elements"
                }
            }
        };

        public override object Execute(Dictionary<string, object> args) => null;

        public override McpContentItem ExecuteOnMain(Dictionary<string, object> args)
        {
            if (args == null)
                return new McpContentItem { type = "text", text = "{\"error\":\"Supply 'path' or 'instanceId'\"}" };

            string path = args.TryGetValue("path", out var pathVal) ? pathVal?.ToString() : null;
            int instanceId = 0;
            if (args.TryGetValue("instanceId", out var idVal))
            {
                if (idVal is long l) instanceId = (int)l;
                else if (idVal is double d) instanceId = (int)d;
                else if (idVal is int i) instanceId = i;
            }
            if (string.IsNullOrEmpty(path) && instanceId == 0)
                return new McpContentItem { type = "text", text = "{\"error\":\"Supply 'path' or 'instanceId'\"}" };

            var components = UiHierarchyTraverser.GetComponentDetails(path, instanceId);
            if (components == null)
                return new McpContentItem { type = "text", text = "{\"error\":\"GameObject not found\"}" };

            var element = UiHierarchyTraverser.GetElementByPath(path, instanceId);
            var json = MCP.JsonHelper.Serialize(new { element, components });
            return new McpContentItem { type = "text", text = json };
        }
    }
}
