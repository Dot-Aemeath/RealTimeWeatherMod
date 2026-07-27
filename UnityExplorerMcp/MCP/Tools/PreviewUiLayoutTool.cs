using System.Collections.Generic;
using UnityEngine;
using UnityExplorerMcp.Core;

namespace UnityExplorerMcp.MCP.Tools
{
    internal sealed class PreviewUiLayoutTool : MainThreadTool
    {
        public override string Name => "preview_ui_layout";
        public override string Description =>
            "Safely preview RectTransform position/size changes without executing code. Can align relative to a reference element. Returns previewId for rollback.";

        public override Dictionary<string, object> InputSchema => new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["previewId"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Optional existing preview session ID; omit to create one"
                },
                ["path"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Target hierarchy path; optional when instanceId is supplied"
                },
                ["instanceId"] = new Dictionary<string, object>
                {
                    ["type"] = "number",
                    ["description"] = "Exact target runtime instance ID"
                },
                ["referencePath"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Optional reference element path for relative placement"
                },
                ["referenceInstanceId"] = new Dictionary<string, object>
                {
                    ["type"] = "number",
                    ["description"] = "Optional exact reference runtime instance ID"
                },
                ["placement"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Relative placement: same/center, below, above, left, or right"
                },
                ["gap"] = new Dictionary<string, object>
                {
                    ["type"] = "number",
                    ["description"] = "Gap in reference RectTransform units (default: 0)"
                },
                ["matchWidth"] = new Dictionary<string, object>
                {
                    ["type"] = "boolean",
                    ["description"] = "Match the reference element width"
                },
                ["matchHeight"] = new Dictionary<string, object>
                {
                    ["type"] = "boolean",
                    ["description"] = "Match the reference element height"
                },
                ["anchoredPositionX"] = new Dictionary<string, object> { ["type"] = "number" },
                ["anchoredPositionY"] = new Dictionary<string, object> { ["type"] = "number" },
                ["width"] = new Dictionary<string, object> { ["type"] = "number" },
                ["height"] = new Dictionary<string, object> { ["type"] = "number" }
            }
        };

        public override object Execute(Dictionary<string, object> args) => null;

        public override McpContentItem ExecuteOnMain(Dictionary<string, object> args)
        {
            int instanceId = ToolArgumentReader.GetInt(args, "instanceId");
            string path = ToolArgumentReader.GetString(args, "path");
            var targetObject = UiHierarchyTraverser.FindGameObject(path, instanceId);
            var target = targetObject != null ? targetObject.transform as RectTransform : null;
            if (target == null)
                return Error("Target RectTransform not found");

            int referenceId = ToolArgumentReader.GetInt(args, "referenceInstanceId");
            string referencePath = ToolArgumentReader.GetString(args, "referencePath");
            RectTransform reference = null;
            if (referenceId != 0 || !string.IsNullOrEmpty(referencePath))
            {
                var referenceObject = UiHierarchyTraverser.FindGameObject(referencePath, referenceId);
                reference = referenceObject != null
                    ? referenceObject.transform as RectTransform
                    : null;
                if (reference == null)
                    return Error("Reference RectTransform not found");
            }

            try
            {
                var result = UiLayoutPreviewService.Apply(
                    ToolArgumentReader.GetString(args, "previewId"),
                    target,
                    reference,
                    ToolArgumentReader.GetString(args, "placement"),
                    ToolArgumentReader.GetFloat(args, "gap"),
                    ToolArgumentReader.GetOptionalFloat(args, "anchoredPositionX"),
                    ToolArgumentReader.GetOptionalFloat(args, "anchoredPositionY"),
                    ToolArgumentReader.GetOptionalFloat(args, "width"),
                    ToolArgumentReader.GetOptionalFloat(args, "height"),
                    ToolArgumentReader.GetBool(args, "matchWidth"),
                    ToolArgumentReader.GetBool(args, "matchHeight"));
                return Text(result);
            }
            catch (System.Exception ex)
            {
                return Error(ex.Message);
            }
        }

        private static McpContentItem Text(object value)
        {
            return new McpContentItem { type = "text", text = MCP.JsonHelper.Serialize(value) };
        }

        private static McpContentItem Error(string message)
        {
            return Text(new { error = message });
        }
    }
}
