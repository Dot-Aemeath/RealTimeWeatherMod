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
    /// tool can execute. Standard protocol requests (2025-03-26 through
    /// 2025-11-25) go through the initialize handshake instead: the version lives
    /// in the initialize body, is mirrored on later requests, and mirrored
    /// Mcp-Method/Mcp-Name headers are optional for them.
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
            string method = GetMethod(message);
            bool isInitialize = string.Equals(
                method,
                "initialize",
                StringComparison.Ordinal);
            bool hasVersionHeader = headers.TryGetValue(
                "MCP-Protocol-Version",
                out string headerVersion);
            bool isCurrentProtocol = hasVersionHeader &&
                McpJsonRpcHandler.IsCurrentProtocolVersion(headerVersion);

            if (isInitialize)
            {
                // Standard handshake: the requested version travels in the body and
                // no mirrored headers or per-request metadata are required yet.
                string requestedVersion = GetInitializeProtocolVersion(message);
                if (!McpJsonRpcHandler.IsSupportedProtocolVersion(requestedVersion))
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
                                requested = requestedVersion
                            }));
                }
            }
            else
            {
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

                string bodyVersion = GetBodyProtocolVersion(message);
                if (bodyVersion != null &&
                    !string.Equals(bodyVersion, headerVersion, StringComparison.Ordinal))
                {
                    return HeaderError(
                        id,
                        "MCP-Protocol-Version header does not match request metadata");
                }

                if (isCurrentProtocol &&
                    !TryGetCurrentRequestMetadata(message, out string metadataError))
                {
                    return Error(id, -32602, metadataError);
                }
            }

            if (string.IsNullOrEmpty(method))
            {
                return Error(id, -32600, "Invalid Request");
            }

            // Mirrored Mcp-Method header: mandatory for the current protocol,
            // optional for standard protocol requests (checked when present).
            if (headers.TryGetValue("Mcp-Method", out string methodHeader))
            {
                if (!string.Equals(methodHeader, method, StringComparison.Ordinal))
                {
                    return HeaderError(
                        id,
                        "Mcp-Method header does not match the request method");
                }
            }
            else if (isCurrentProtocol)
            {
                return HeaderError(
                    id,
                    "Mcp-Method header is missing or does not match the request method");
            }

            if (isCurrentProtocol && RequiresNameHeader(method))
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

        private static string GetMethod(Dictionary<string, object> message)
        {
            if (!message.TryGetValue("method", out object methodValue))
            {
                return null;
            }

            return methodValue as string;
        }

        private static string GetInitializeProtocolVersion(
            Dictionary<string, object> message)
        {
            if (!message.TryGetValue("params", out object paramsValue) ||
                !(paramsValue is Dictionary<string, object> parameters) ||
                !parameters.TryGetValue("protocolVersion", out object versionValue))
            {
                return null;
            }

            return versionValue as string;
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
