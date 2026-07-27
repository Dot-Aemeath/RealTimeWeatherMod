using System.Collections.Generic;
using UnityExplorerMcp.Core;

namespace UnityExplorerMcp.MCP.Tools
{
    internal sealed class RollbackUiPreviewTool : MainThreadTool
    {
        public override string Name => "rollback_ui_preview";
        public override string Description =>
            "Rollback one safe UI layout preview by previewId, or all previews when previewId is omitted.";

        public override Dictionary<string, object> InputSchema => new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["previewId"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Preview session ID; omit to rollback every active preview"
                }
            }
        };

        public override object Execute(Dictionary<string, object> args) => null;

        public override McpContentItem ExecuteOnMain(Dictionary<string, object> args)
        {
            var result = UiLayoutPreviewService.Rollback(
                ToolArgumentReader.GetString(args, "previewId"));
            return new McpContentItem
            {
                type = "text",
                text = MCP.JsonHelper.Serialize(result)
            };
        }
    }
}
