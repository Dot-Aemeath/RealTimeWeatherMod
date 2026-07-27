using System.Collections.Generic;
using UnityExplorerMcp.Core;

namespace UnityExplorerMcp.MCP.Tools
{
    internal class SearchElementsTool : MainThreadTool
    {
        public override string Name => "search_elements";
        public override string Description => "Find UI by GameObject name, hierarchy path, or visible Text/TMP text. Returns token-bounded matches with instance IDs, component types, interactive ancestor, and screen pixel bounds.";
        public override Dictionary<string, object> InputSchema => new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["query"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Search term (case-insensitive name match)"
                },
                ["componentType"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Optional component type filter (e.g., 'Button', 'Image', 'Text')"
                },
                ["activeOnly"] = new Dictionary<string, object>
                {
                    ["type"] = "boolean",
                    ["description"] = "Only return active UI. Default: true. Set false to inspect closed/inactive panels."
                },
                ["maxResults"] = new Dictionary<string, object>
                {
                    ["type"] = "number",
                    ["description"] = "Maximum results to return (default: 25, max: 100)"
                }
            },
            ["required"] = new List<string> { "query" }
        };

        public override object Execute(Dictionary<string, object> args) => null;

        public override McpContentItem ExecuteOnMain(Dictionary<string, object> args)
        {
            if (args == null || !args.TryGetValue("query", out var queryVal))
                return new McpContentItem { type = "text", text = "{\"error\":\"Missing 'query' argument\"}" };

            string query = queryVal?.ToString();
            string componentType = args.TryGetValue("componentType", out var ct) ? ct?.ToString() : null;
            bool activeOnly = !args.TryGetValue("activeOnly", out var av) || !(av is bool active) || active;
            int maxResults = GetInt(args, "maxResults", 25, 1, 100);

            var results = UiHierarchyTraverser.SearchElements(query, componentType, activeOnly, maxResults);
            var json = MCP.JsonHelper.Serialize(results);
            return new McpContentItem { type = "text", text = json };
        }

        private static int GetInt(Dictionary<string, object> args, string name, int defaultValue, int min, int max)
        {
            if (!args.TryGetValue(name, out var value)) return defaultValue;
            int result = defaultValue;
            if (value is long l) result = (int)l;
            else if (value is double d) result = (int)d;
            else if (value is int i) result = i;
            return result < min ? min : result > max ? max : result;
        }
    }
}
