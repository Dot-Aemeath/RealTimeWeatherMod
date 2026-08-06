using System;
using System.Collections.Generic;
using System.Text;

namespace UnityExplorerMcp.MCP
{
    internal sealed class McpHttpValidationResult
    {
        private McpHttpValidationResult(
            Dictionary<string, object> message,
            int errorStatusCode,
            string errorBody)
        {
            Message = message;
            ErrorStatusCode = errorStatusCode;
            ErrorBody = errorBody;
        }

        internal bool IsValid => Message != null;
        internal Dictionary<string, object> Message { get; }
        internal int ErrorStatusCode { get; }
        internal string ErrorBody { get; }

        internal static McpHttpValidationResult Success(
            Dictionary<string, object> message)
        {
            return new McpHttpValidationResult(message, 0, null);
        }

        internal static McpHttpValidationResult Error(
            int statusCode,
            string errorBody)
        {
            return new McpHttpValidationResult(
                null,
                statusCode,
                errorBody);
        }
    }

    /// <summary>
    /// Validates the headers mirrored by the current Streamable HTTP binding. The
    /// JSON-RPC body remains the source of truth; mismatches are rejected before a
    /// tool can execute.
    /// </summary>
    internal static class McpStreamableHttpRequestValidator
    {
        private const int HeaderMismatch = -32020;
        private const int UnsupportedProtocolVersion = -32022;
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static McpHttpValidationResult Validate(
            string body,
            Dictionary<string, string> headers)
        {
            Dictionary<string, object> message = JsonHelper.DeserializeSimple(body);
            if (message == null)
            {
                return Error(null, -32700, "Parse error");
            }

            object id = GetResponseId(message);
            string bodyVersion = GetBodyProtocolVersion(message);
            bool hasVersionHeader = headers.TryGetValue(
                "MCP-Protocol-Version",
                out string headerVersion);

            if (!hasVersionHeader)
            {
                return HeaderError(
                    id,
                    "MCP-Protocol-Version header is missing");
            }

            if (!McpJsonRpcHandler.IsSupportedProtocolVersion(headerVersion))
            {
                return McpHttpValidationResult.Error(
                    400,
                    JsonHelper.SerializeJsonRpcError(
                        id,
                        UnsupportedProtocolVersion,
                        "Unsupported protocol version",
                        new
                        {
                            supported = McpJsonRpcHandler.GetSupportedProtocolVersions(),
                            requested = headerVersion
                        }));
            }

            if (!string.Equals(bodyVersion, headerVersion, StringComparison.Ordinal))
            {
                return HeaderError(
                    id,
                    "MCP-Protocol-Version header does not match request metadata");
            }

            if (!TryGetCurrentRequestMetadata(message, out string metadataError))
            {
                return Error(id, -32602, metadataError);
            }

            if (!message.TryGetValue("method", out object methodValue) ||
                !(methodValue is string method) ||
                string.IsNullOrEmpty(method))
            {
                return Error(id, -32600, "Invalid Request");
            }

            if (!headers.TryGetValue("Mcp-Method", out string methodHeader) ||
                !string.Equals(methodHeader, method, StringComparison.Ordinal))
            {
                return HeaderError(
                    id,
                    "Mcp-Method header is missing or does not match the request method");
            }

            if (RequiresNameHeader(method))
            {
                string bodyName = GetRequestName(message, method);
                if (bodyName == null)
                {
                    return Error(id, -32602, "Request is missing its name or uri");
                }

                if (!headers.TryGetValue("Mcp-Name", out string encodedName) ||
                    !TryDecodeMirroredHeader(encodedName, out string headerName) ||
                    !string.Equals(headerName, bodyName, StringComparison.Ordinal))
                {
                    return HeaderError(
                        id,
                        "Mcp-Name header is missing, malformed or does not match the request body");
                }
            }

            return McpHttpValidationResult.Success(message);
        }

        private static bool TryGetCurrentRequestMetadata(
            Dictionary<string, object> message,
            out string error)
        {
            error = null;
            if (!message.TryGetValue("params", out object paramsValue) ||
                !(paramsValue is Dictionary<string, object> parameters) ||
                !parameters.TryGetValue("_meta", out object metaValue) ||
                !(metaValue is Dictionary<string, object> metadata))
            {
                error = "Request is missing required per-request metadata";
                return false;
            }

            if (!metadata.TryGetValue(
                    "io.modelcontextprotocol/protocolVersion",
                    out object versionValue) ||
                !(versionValue is string version) ||
                !McpJsonRpcHandler.IsSupportedProtocolVersion(version))
            {
                error = "Request metadata has an invalid protocol version";
                return false;
            }

            if (!metadata.TryGetValue(
                    "io.modelcontextprotocol/clientCapabilities",
                    out object capabilities) ||
                !(capabilities is Dictionary<string, object>))
            {
                error = "Request metadata is missing client capabilities";
                return false;
            }

            if (metadata.TryGetValue(
                    "io.modelcontextprotocol/clientInfo",
                    out object clientInfoValue))
            {
                if (!(clientInfoValue is Dictionary<string, object> clientInfo) ||
                    !clientInfo.TryGetValue("name", out object name) ||
                    !(name is string) ||
                    !clientInfo.TryGetValue("version", out object versionName) ||
                    !(versionName is string))
                {
                    error = "Request metadata has invalid client information";
                    return false;
                }
            }

            return true;
        }

        private static string GetBodyProtocolVersion(
            Dictionary<string, object> message)
        {
            if (!message.TryGetValue("params", out object paramsValue) ||
                !(paramsValue is Dictionary<string, object> parameters) ||
                !parameters.TryGetValue("_meta", out object metaValue) ||
                !(metaValue is Dictionary<string, object> metadata) ||
                !metadata.TryGetValue(
                    "io.modelcontextprotocol/protocolVersion",
                    out object versionValue))
            {
                return null;
            }

            return versionValue as string;
        }

        private static bool RequiresNameHeader(string method)
        {
            return method == "tools/call" ||
                method == "resources/read" ||
                method == "prompts/get";
        }

        private static string GetRequestName(
            Dictionary<string, object> message,
            string method)
        {
            if (!message.TryGetValue("params", out object paramsValue) ||
                !(paramsValue is Dictionary<string, object> parameters))
            {
                return null;
            }

            string key = method == "resources/read" ? "uri" : "name";
            return parameters.TryGetValue(key, out object nameValue)
                ? nameValue as string
                : null;
        }

        private static bool TryDecodeMirroredHeader(
            string encoded,
            out string decoded)
        {
            decoded = null;
            if (encoded == null) return false;

            const string prefix = "=?base64?";
            const string suffix = "?=";
            if (!encoded.StartsWith(prefix, StringComparison.Ordinal) ||
                !encoded.EndsWith(suffix, StringComparison.Ordinal))
            {
                decoded = encoded;
                return IsSafePlainHeaderValue(encoded);
            }

            string payload = encoded.Substring(
                prefix.Length,
                encoded.Length - prefix.Length - suffix.Length);
            try
            {
                byte[] bytes = Convert.FromBase64String(payload);
                decoded = StrictUtf8.GetString(bytes);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }

        private static bool IsSafePlainHeaderValue(string value)
        {
            if (value.Length == 0 ||
                !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                return false;
            }

            foreach (char character in value)
            {
                if (character < 0x20 || character > 0x7e) return false;
            }

            return true;
        }

        private static object GetResponseId(Dictionary<string, object> message)
        {
            if (!message.TryGetValue("id", out object id) ||
                !McpJsonRpcHandler.IsValidRequestId(id))
            {
                return null;
            }

            return id;
        }

        private static McpHttpValidationResult HeaderError(
            object id,
            string message)
        {
            return Error(id, HeaderMismatch, message);
        }

        private static McpHttpValidationResult Error(
            object id,
            int code,
            string message)
        {
            return McpHttpValidationResult.Error(
                400,
                JsonHelper.SerializeJsonRpcError(id, code, message));
        }
    }
}
