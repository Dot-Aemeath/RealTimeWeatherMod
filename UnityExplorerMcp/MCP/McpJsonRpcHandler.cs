using System;
using System.Collections.Generic;
using UnityExplorerMcp.MCP.Tools;

namespace UnityExplorerMcp.MCP
{
    internal enum McpMessageKind
    {
        Request,
        Notification,
        Invalid
    }

    internal sealed class McpProtocolResult
    {
        private McpProtocolResult(
            McpMessageKind kind,
            string responseBody,
            int httpStatusCode)
        {
            Kind = kind;
            ResponseBody = responseBody;
            HttpStatusCode = httpStatusCode;
        }

        internal McpMessageKind Kind { get; }
        internal string ResponseBody { get; }
        internal int HttpStatusCode { get; }

        internal static McpProtocolResult Request(
            string responseBody,
            int httpStatusCode = 200)
        {
            return new McpProtocolResult(
                McpMessageKind.Request,
                responseBody,
                httpStatusCode);
        }

        internal static McpProtocolResult AcceptedNotification()
        {
            return new McpProtocolResult(
                McpMessageKind.Notification,
                null,
                202);
        }

        internal static McpProtocolResult Invalid(string responseBody)
        {
            return new McpProtocolResult(
                McpMessageKind.Invalid,
                responseBody,
                400);
        }
    }

    /// <summary>
    /// Dispatches the current, stateless MCP protocol. Version, client identity and
    /// capabilities are carried on every request; no initialization-era state exists.
    /// HTTP framing and mirrored-header validation stay outside this module.
    /// </summary>
    internal sealed class McpJsonRpcHandler
    {
        internal const string CurrentProtocolVersion = "2026-07-28";

        private readonly ToolRegistry _toolRegistry;
        private readonly Func<IMcpTool, Dictionary<string, object>, McpContentItem> _executeTool;

        internal McpJsonRpcHandler(
            ToolRegistry toolRegistry,
            Func<IMcpTool, Dictionary<string, object>, McpContentItem> executeTool)
        {
            _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
            _executeTool = executeTool ?? throw new ArgumentNullException(nameof(executeTool));
        }

        internal static bool IsSupportedProtocolVersion(string version)
        {
            return string.Equals(
                version,
                CurrentProtocolVersion,
                StringComparison.Ordinal);
        }

        internal static string[] GetSupportedProtocolVersions()
        {
            return new[] { CurrentProtocolVersion };
        }

        internal McpProtocolResult Process(Dictionary<string, object> request)
        {
            if (request == null)
            {
                return Invalid(-32700, "Parse error");
            }

            if (!request.TryGetValue("jsonrpc", out object jsonRpc) ||
                !string.Equals(jsonRpc as string, "2.0", StringComparison.Ordinal))
            {
                return Invalid(-32600, "Invalid Request");
            }

            if (!request.TryGetValue("method", out object methodValue))
            {
                return Invalid(-32600, "Clients must not send JSON-RPC responses");
            }

            string method = methodValue as string;
            if (string.IsNullOrEmpty(method))
            {
                return Invalid(-32600, "Invalid Request");
            }

            Dictionary<string, object> parameters = null;
            if (request.TryGetValue("params", out object paramsValue))
            {
                parameters = paramsValue as Dictionary<string, object>;
                if (parameters == null)
                {
                    if (!request.ContainsKey("id"))
                    {
                        return McpProtocolResult.AcceptedNotification();
                    }

                    return McpProtocolResult.Request(
                        JsonHelper.SerializeJsonRpcError(
                            request["id"],
                            -32602,
                            "Invalid params"));
                }
            }

            if (!request.TryGetValue("id", out object id))
            {
                return McpProtocolResult.AcceptedNotification();
            }

            if (!IsValidRequestId(id))
            {
                return Invalid(-32600, "Invalid Request");
            }

            if (method == "server/discover")
            {
                return ProcessDiscover(id);
            }

            if (method == "tools/list")
            {
                return Respond(
                    id,
                    new
                    {
                        resultType = "complete",
                        tools = _toolRegistry.GetToolDescriptions(),
                        ttlMs = 0,
                        cacheScope = "private",
                        _meta = CreateServerMeta()
                    });
            }

            if (method == "tools/call")
            {
                return ProcessToolCall(id, parameters);
            }

            return McpProtocolResult.Request(
                JsonHelper.SerializeJsonRpcError(
                    id,
                    -32601,
                    "Method not found: " + method),
                404);
        }

        internal static bool IsValidRequestId(object id)
        {
            if (id is string) return true;
            if (id is int || id is long) return true;
            if (id is double number)
            {
                return !double.IsNaN(number) &&
                    !double.IsInfinity(number) &&
                    Math.Abs(number % 1d) < double.Epsilon;
            }

            return false;
        }

        private static McpProtocolResult ProcessDiscover(object id)
        {
            return Respond(
                id,
                new
                {
                    resultType = "complete",
                    supportedVersions = GetSupportedProtocolVersions(),
                    capabilities = new
                    {
                        tools = new { listChanged = false }
                    },
                    instructions = CreateInstructions(),
                    ttlMs = 0,
                    cacheScope = "private",
                    _meta = CreateServerMeta()
                });
        }

        private McpProtocolResult ProcessToolCall(
            object id,
            Dictionary<string, object> parameters)
        {
            if (parameters == null ||
                !parameters.TryGetValue("name", out object nameValue) ||
                !(nameValue is string toolName) ||
                string.IsNullOrEmpty(toolName))
            {
                return McpProtocolResult.Request(
                    JsonHelper.SerializeJsonRpcError(id, -32602, "Missing tool name"));
            }

            IMcpTool tool = _toolRegistry.GetTool(toolName);
            if (tool == null)
            {
                return McpProtocolResult.Request(
                    JsonHelper.SerializeJsonRpcError(id, -32602, "Unknown tool: " + toolName));
            }

            var arguments = new Dictionary<string, object>();
            if (parameters.TryGetValue("arguments", out object argumentsValue))
            {
                arguments = argumentsValue as Dictionary<string, object>;
                if (arguments == null)
                {
                    return McpProtocolResult.Request(
                        JsonHelper.SerializeJsonRpcError(
                            id,
                            -32602,
                            "Tool arguments must be an object"));
                }
            }

            try
            {
                McpContentItem contentItem = _executeTool(tool, arguments);
                if (contentItem == null)
                {
                    throw new InvalidOperationException("Tool returned no content");
                }

                return CreateToolResult(id, contentItem, false);
            }
            catch (Exception ex)
            {
                return CreateToolResult(
                    id,
                    new McpContentItem
                    {
                        type = "text",
                        text = ex.Message
                    },
                    true);
            }
        }

        private static McpProtocolResult CreateToolResult(
            object id,
            McpContentItem contentItem,
            bool isError)
        {
            try
            {
                return Respond(
                    id,
                    new
                    {
                        resultType = "complete",
                        content = new List<McpContentItem> { contentItem },
                        isError,
                        _meta = CreateServerMeta()
                    });
            }
            catch (Exception ex)
            {
                return McpProtocolResult.Request(
                    JsonHelper.SerializeJsonRpcError(id, -32603, ex.Message));
            }
        }

        private static object CreateServerInfo()
        {
            return new
            {
                name = "unity-explorer-mcp",
                title = "UnityExplorer MCP Bridge",
                version = "1.0.0",
                description = "Runtime inspection and deterministic debugging for Unity mods"
            };
        }

        private static Dictionary<string, object> CreateServerMeta()
        {
            return new Dictionary<string, object>
            {
                ["io.modelcontextprotocol/serverInfo"] = CreateServerInfo()
            };
        }

        private static string CreateInstructions()
        {
            return
                "Use bounded inspection tools before execute_code. " +
                "This development server is available only on the local machine.";
        }

        private static McpProtocolResult Respond(object id, object result)
        {
            return McpProtocolResult.Request(
                JsonHelper.SerializeJsonRpcResponseBody(id, result));
        }

        private static McpProtocolResult Invalid(int code, string message)
        {
            return McpProtocolResult.Invalid(
                JsonHelper.SerializeJsonRpcError(null, code, message));
        }
    }
}
