using System.Text;
using UnityExplorerMcp.MCP;
using UnityExplorerMcp.MCP.Tools;
using Xunit;

namespace UnityExplorerMcp.Tests
{
    public sealed class StreamableHttpProtocolTests
    {
        private const string CurrentMetadata =
            "\"_meta\":{" +
            "\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\"," +
            "\"io.modelcontextprotocol/clientInfo\":{" +
            "\"name\":\"tests\",\"version\":\"1\"}," +
            "\"io.modelcontextprotocol/clientCapabilities\":{}}";

        [Fact]
        public void Discover_AdvertisesSupportedVersionsAndServerMetadata()
        {
            StreamableHttpProtocol protocol = CreateProtocol();

            McpHttpResponse response = protocol.CreateResponse(
                CurrentPost("discover-current", "server/discover"));

            Assert.Equal(200, response.StatusCode);
            Assert.Equal("application/json; charset=utf-8", response.ContentType);
            Dictionary<string, object> envelope = Parse(response.Body);
            Assert.Equal("discover-current", envelope["id"]);
            var result = Assert.IsType<Dictionary<string, object>>(envelope["result"]);
            Assert.Equal("complete", result["resultType"]);
            var versions = Assert.IsType<List<object>>(result["supportedVersions"]);
            Assert.Contains("2026-07-28", versions);
            Assert.Contains("2025-11-25", versions);
            Assert.Equal(0L, result["ttlMs"]);
            Assert.Equal("private", result["cacheScope"]);
            var metadata = Assert.IsType<Dictionary<string, object>>(result["_meta"]);
            var serverInfo = Assert.IsType<Dictionary<string, object>>(
                metadata["io.modelcontextprotocol/serverInfo"]);
            Assert.Equal("unity-explorer-mcp", serverInfo["name"]);
        }

        [Fact]
        public void ToolsList_PreservesStringIdAndUsesDeterministicCurrentShape()
        {
            StreamableHttpProtocol protocol = CreateProtocol(
                new FakeTool("zeta"),
                new FakeTool("alpha"));

            McpHttpResponse response = protocol.CreateResponse(
                CurrentPost("调用-1", "tools/list"));

            Assert.Equal(200, response.StatusCode);
            Dictionary<string, object> envelope = Parse(response.Body);
            Assert.Equal("调用-1", envelope["id"]);
            var result = Assert.IsType<Dictionary<string, object>>(envelope["result"]);
            Assert.Equal("complete", result["resultType"]);
            Assert.Equal(0L, result["ttlMs"]);
            Assert.Equal("private", result["cacheScope"]);
            var tools = Assert.IsType<List<object>>(result["tools"]);
            Assert.Equal("alpha", Assert.IsType<Dictionary<string, object>>(tools[0])["name"]);
            Assert.Equal("zeta", Assert.IsType<Dictionary<string, object>>(tools[1])["name"]);
        }

        [Fact]
        public void ToolCall_ValidatesNameHeaderAndUsesCurrentResultShape()
        {
            StreamableHttpProtocol protocol = CreateProtocol(new FakeTool("alpha"));

            McpHttpResponse response = protocol.CreateResponse(
                CurrentPost(
                    "call-current",
                    "tools/call",
                    "\"name\":\"alpha\",\"arguments\":{}",
                    "alpha"));

            Assert.Equal(200, response.StatusCode);
            var result = Assert.IsType<Dictionary<string, object>>(Parse(response.Body)["result"]);
            Assert.Equal("complete", result["resultType"]);
            Assert.False(Assert.IsType<bool>(result["isError"]));
            Assert.True(result.ContainsKey("_meta"));
        }

        [Fact]
        public void ToolCall_AcceptsBase64EncodedNonAsciiNameHeader()
        {
            StreamableHttpProtocol protocol = CreateProtocol(new FakeTool("天气"));
            McpHttpRequest request = CurrentPost(
                "call-unicode",
                "tools/call",
                "\"name\":\"天气\",\"arguments\":{}",
                "天气");
            request.Headers["Mcp-Name"] = "=?base64?5aSp5rCU?=";

            Assert.Equal(200, protocol.CreateResponse(request).StatusCode);
        }

        [Fact]
        public void ToolExecutionFailure_ReturnsCurrentMcpToolErrorResult()
        {
            StreamableHttpProtocol protocol = CreateProtocol(new FakeTool("fails", true));

            McpHttpResponse response = protocol.CreateResponse(
                CurrentPost(
                    "failure-current",
                    "tools/call",
                    "\"name\":\"fails\",\"arguments\":{}",
                    "fails"));

            Assert.Equal(200, response.StatusCode);
            Dictionary<string, object> envelope = Parse(response.Body);
            var result = Assert.IsType<Dictionary<string, object>>(envelope["result"]);
            Assert.Equal("complete", result["resultType"]);
            Assert.True(Assert.IsType<bool>(result["isError"]));
            Assert.False(envelope.ContainsKey("error"));
        }

        [Fact]
        public void CurrentNotification_Returns202WithoutBody()
        {
            StreamableHttpProtocol protocol = CreateProtocol();
            McpHttpRequest request = Post(
                "{\"jsonrpc\":\"2.0\",\"method\":\"example/notification\"," +
                "\"params\":{" + CurrentMetadata + "}}");
            AddCurrentHeaders(request, "example/notification");

            McpHttpResponse response = protocol.CreateResponse(request);

            Assert.Equal(202, response.StatusCode);
            Assert.Equal(string.Empty, response.Body);
            Assert.Null(response.ContentType);
        }

        [Fact]
        public void ClientJsonRpcResponse_IsRejected()
        {
            StreamableHttpProtocol protocol = CreateProtocol();
            McpHttpRequest request = Post(
                "{\"jsonrpc\":\"2.0\",\"id\":7,\"result\":{}," +
                "\"params\":{" + CurrentMetadata + "}}");
            AddCurrentHeaders(request, "client/response");

            McpHttpResponse response = protocol.CreateResponse(request);

            Assert.Equal(400, response.StatusCode);
            Assert.Equal(-32600L, ErrorCode(response));
        }

        [Fact]
        public void InitializeWithoutProtocolVersion_IsRejected()
        {
            StreamableHttpProtocol protocol = CreateProtocol();

            McpHttpResponse response = protocol.CreateResponse(
                CurrentPost("no-initialize", "initialize"));

            Assert.Equal(400, response.StatusCode);
            Assert.Equal(-32022L, ErrorCode(response));
        }

        [Fact]
        public void StandardRequest_MissingVersionHeader_IsRejected()
        {
            StreamableHttpProtocol protocol = CreateProtocol();
            McpHttpRequest request = Post(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"," +
                "\"params\":{}}");

            McpHttpResponse response = protocol.CreateResponse(request);

            Assert.Equal(400, response.StatusCode);
            Assert.Equal(-32020L, ErrorCode(response));
        }

        [Fact]
        public void StandardInitialize_HandshakeSucceedsAndEchoesVersion()
        {
            StreamableHttpProtocol protocol = CreateProtocol(new FakeTool("alpha"));

            McpHttpResponse response = protocol.CreateResponse(
                StandardPost(
                    "init-sdk",
                    "initialize",
                    "\"protocolVersion\":\"2025-11-25\"," +
                    "\"capabilities\":{}," +
                    "\"clientInfo\":{\"name\":\"sdk\",\"version\":\"1.0\"}"));

            Assert.Equal(200, response.StatusCode);
            Dictionary<string, object> envelope = Parse(response.Body);
            Assert.Equal("init-sdk", envelope["id"]);
            var result = Assert.IsType<Dictionary<string, object>>(envelope["result"]);
            Assert.Equal("2025-11-25", result["protocolVersion"]);
            var capabilities = Assert.IsType<Dictionary<string, object>>(result["capabilities"]);
            var tools = Assert.IsType<Dictionary<string, object>>(capabilities["tools"]);
            Assert.False(Assert.IsType<bool>(tools["listChanged"]));
            var serverInfo = Assert.IsType<Dictionary<string, object>>(result["serverInfo"]);
            Assert.Equal("unity-explorer-mcp", serverInfo["name"]);
            Assert.True(result.ContainsKey("instructions"));
        }

        [Fact]
        public void StandardInitialize_UnsupportedVersion_Returns32022()
        {
            StreamableHttpProtocol protocol = CreateProtocol();

            McpHttpResponse response = protocol.CreateResponse(
                StandardPost(
                    "init-old",
                    "initialize",
                    "\"protocolVersion\":\"2024-01-01\"," +
                    "\"capabilities\":{}," +
                    "\"clientInfo\":{\"name\":\"sdk\",\"version\":\"1.0\"}"));

            Assert.Equal(400, response.StatusCode);
            Assert.Equal(-32022L, ErrorCode(response));
            var error = Assert.IsType<Dictionary<string, object>>(Parse(response.Body)["error"]);
            var data = Assert.IsType<Dictionary<string, object>>(error["data"]);
            var supported = Assert.IsType<List<object>>(data["supported"]);
            Assert.Contains("2025-11-25", supported);
        }

        [Fact]
        public void StandardToolsList_WorksWithoutMirroredHeaders()
        {
            StreamableHttpProtocol protocol = CreateProtocol(
                new FakeTool("zeta"),
                new FakeTool("alpha"));
            McpHttpRequest request = Post(
                "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"," +
                "\"params\":{}}");
            AddStandardVersionHeader(request);

            McpHttpResponse response = protocol.CreateResponse(request);

            Assert.Equal(200, response.StatusCode);
            Dictionary<string, object> envelope = Parse(response.Body);
            Assert.Equal(2L, envelope["id"]);
            var result = Assert.IsType<Dictionary<string, object>>(envelope["result"]);
            var tools = Assert.IsType<List<object>>(result["tools"]);
            Assert.Equal("alpha", Assert.IsType<Dictionary<string, object>>(tools[0])["name"]);
            Assert.Equal("zeta", Assert.IsType<Dictionary<string, object>>(tools[1])["name"]);
        }

        [Fact]
        public void StandardToolCall_WorksWithoutNameHeader()
        {
            StreamableHttpProtocol protocol = CreateProtocol(new FakeTool("alpha"));
            McpHttpRequest request = Post(
                "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\"," +
                "\"params\":{\"name\":\"alpha\",\"arguments\":{}}}");
            AddStandardVersionHeader(request);

            McpHttpResponse response = protocol.CreateResponse(request);

            Assert.Equal(200, response.StatusCode);
            var result = Assert.IsType<Dictionary<string, object>>(Parse(response.Body)["result"]);
            Assert.False(Assert.IsType<bool>(result["isError"]));
            var content = Assert.IsType<List<object>>(result["content"]);
            Assert.Equal(
                "alpha",
                Assert.IsType<Dictionary<string, object>>(content[0])["text"]);
        }

        [Fact]
        public void StandardInitializedNotification_Returns202()
        {
            StreamableHttpProtocol protocol = CreateProtocol();
            McpHttpRequest request = Post(
                "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
            AddStandardVersionHeader(request);

            McpHttpResponse response = protocol.CreateResponse(request);

            Assert.Equal(202, response.StatusCode);
            Assert.Equal(string.Empty, response.Body);
        }

        [Fact]
        public void StandardRequest_WrongMethodHeader_IsStillRejected()
        {
            StreamableHttpProtocol protocol = CreateProtocol();
            McpHttpRequest request = Post(
                "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/list\"," +
                "\"params\":{}}");
            AddStandardVersionHeader(request);
            request.Headers["Mcp-Method"] = "tools/call";

            McpHttpResponse response = protocol.CreateResponse(request);

            Assert.Equal(400, response.StatusCode);
            Assert.Equal(-32020L, ErrorCode(response));
        }

        [Fact]
        public void MalformedJson_ReturnsHttp400AndJsonRpcNullId()
        {
            StreamableHttpProtocol protocol = CreateProtocol();

            McpHttpResponse response = protocol.CreateResponse(Post("{not-json"));

            Assert.Equal(400, response.StatusCode);
            Dictionary<string, object> envelope = Parse(response.Body);
            Assert.True(envelope.ContainsKey("id"));
            Assert.Null(envelope["id"]);
            Assert.Equal(-32700L, ErrorCode(response));
        }

        [Fact]
        public void Post_RequiresBothStreamableHttpAcceptTypes()
        {
            StreamableHttpProtocol protocol = CreateProtocol();
            McpHttpRequest request = CurrentPost("accept-current", "server/discover");
            request.Headers["Accept"] = "application/json";

            Assert.Equal(406, protocol.CreateResponse(request).StatusCode);
        }

        [Fact]
        public void Post_RejectsUnsupportedProtocolVersion()
        {
            StreamableHttpProtocol protocol = CreateProtocol();
            McpHttpRequest request = CurrentPost("old-version", "server/discover");
            request.Headers["MCP-Protocol-Version"] = "2024-01-01";

            McpHttpResponse response = protocol.CreateResponse(request);

            Assert.Equal(400, response.StatusCode);
            Assert.Equal(-32022L, ErrorCode(response));
            var error = Assert.IsType<Dictionary<string, object>>(Parse(response.Body)["error"]);
            var data = Assert.IsType<Dictionary<string, object>>(error["data"]);
            Assert.Equal("2024-01-01", data["requested"]);
            var supported = Assert.IsType<List<object>>(data["supported"]);
            Assert.Contains("2026-07-28", supported);
            Assert.Contains("2025-11-25", supported);
        }

        [Fact]
        public void Post_RejectsMissingProtocolVersionHeader()
        {
            StreamableHttpProtocol protocol = CreateProtocol();
            McpHttpRequest request = CurrentPost("missing-version", "server/discover");
            request.Headers.Remove("MCP-Protocol-Version");

            McpHttpResponse response = protocol.CreateResponse(request);

            Assert.Equal(400, response.StatusCode);
            Assert.Equal(-32020L, ErrorCode(response));
        }

        [Fact]
        public void Post_RejectsMissingOrMismatchedMirroredHeaders()
        {
            StreamableHttpProtocol protocol = CreateProtocol(new FakeTool("alpha"));
            McpHttpRequest missingMethod = CurrentPost("missing-method", "tools/list");
            missingMethod.Headers.Remove("Mcp-Method");
            McpHttpRequest mismatchedName = CurrentPost(
                "wrong-name",
                "tools/call",
                "\"name\":\"alpha\",\"arguments\":{}",
                "other");

            McpHttpResponse methodResponse = protocol.CreateResponse(missingMethod);
            McpHttpResponse nameResponse = protocol.CreateResponse(mismatchedName);

            Assert.Equal(400, methodResponse.StatusCode);
            Assert.Equal(-32020L, ErrorCode(methodResponse));
            Assert.Equal(400, nameResponse.StatusCode);
            Assert.Equal(-32020L, ErrorCode(nameResponse));
        }

        [Fact]
        public void Post_RejectsProtocolHeaderAndBodyMismatch()
        {
            StreamableHttpProtocol protocol = CreateProtocol();
            McpHttpRequest request = CurrentPost("version-mismatch", "tools/list");
            request.Body = request.Body.Replace("2026-07-28", "2099-01-01");

            McpHttpResponse response = protocol.CreateResponse(request);

            Assert.Equal(400, response.StatusCode);
            Assert.Equal(-32020L, ErrorCode(response));
        }

        [Fact]
        public void Post_RejectsMissingPerRequestCapabilities()
        {
            StreamableHttpProtocol protocol = CreateProtocol();
            McpHttpRequest request = CurrentPost("missing-capabilities", "tools/list");
            request.Body = request.Body.Replace(
                ",\"io.modelcontextprotocol/clientCapabilities\":{}",
                string.Empty);

            McpHttpResponse response = protocol.CreateResponse(request);

            Assert.Equal(400, response.StatusCode);
            Assert.Equal(-32602L, ErrorCode(response));
        }

        [Fact]
        public void UnknownMethod_ReturnsHttp404WithJsonRpcMethodError()
        {
            StreamableHttpProtocol protocol = CreateProtocol();

            McpHttpResponse response = protocol.CreateResponse(
                CurrentPost("unknown-current", "example/unknown"));

            Assert.Equal(404, response.StatusCode);
            Assert.Equal(-32601L, ErrorCode(response));
        }

        [Fact]
        public void BrowserOrigin_IsRejected()
        {
            StreamableHttpProtocol protocol = CreateProtocol();
            McpHttpRequest request = CurrentPost("origin-current", "server/discover");
            request.Headers["Origin"] = "https://example.invalid";

            Assert.Equal(403, protocol.CreateResponse(request).StatusCode);
        }

        [Fact]
        public void Get_Returns405BecauseCurrentProtocolHasNoGetStream()
        {
            StreamableHttpProtocol protocol = CreateProtocol();
            McpHttpRequest request = BaseRequest("GET", "/mcp", string.Empty);
            request.Headers["Accept"] = "text/event-stream";

            McpHttpResponse response = protocol.CreateResponse(request);

            Assert.Equal(405, response.StatusCode);
            Assert.Equal("POST", response.Headers["Allow"]);
        }

        [Fact]
        public void RootEndpoint_IsNotExposed()
        {
            StreamableHttpProtocol protocol = CreateProtocol();
            McpHttpRequest request = CurrentPost("wrong-path", "server/discover");
            request.Target = "/";

            Assert.Equal(404, protocol.CreateResponse(request).StatusCode);
        }

        [Fact]
        public void WireReader_CombinesRepeatedAcceptHeadersAndReadsUtf8Body()
        {
            string body = CurrentRequest("天气", "server/discover");
            int bodyLength = Encoding.UTF8.GetByteCount(body);
            string raw =
                "POST /mcp HTTP/1.1\r\n" +
                "Host: localhost:8972\r\n" +
                "Content-Type: application/json\r\n" +
                "Accept: application/json\r\n" +
                "Accept: text/event-stream\r\n" +
                "MCP-Protocol-Version: 2026-07-28\r\n" +
                "Mcp-Method: server/discover\r\n" +
                "Content-Length: " + bodyLength + "\r\n\r\n" +
                body;
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(raw));

            McpHttpRequest request = McpHttpWire.ReadRequest(stream, 1024 * 1024);

            Assert.Equal(body, request.Body);
            Assert.Equal("application/json,text/event-stream", request.Headers["Accept"]);
            Assert.Equal(200, CreateProtocol().CreateResponse(request).StatusCode);
        }

        [Fact]
        public void WireReader_DecodesChunkedPostBody()
        {
            string body = CurrentRequest("chunk", "server/discover");
            string firstChunk = body.Substring(0, body.Length / 2);
            string secondChunk = body.Substring(body.Length / 2);
            string raw =
                "POST /mcp HTTP/1.1\r\n" +
                "Host: localhost:8972\r\n" +
                "Content-Type: application/json\r\n" +
                "Accept: application/json, text/event-stream\r\n" +
                "MCP-Protocol-Version: 2026-07-28\r\n" +
                "Mcp-Method: server/discover\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                firstChunk.Length.ToString("X") + "\r\n" + firstChunk + "\r\n" +
                secondChunk.Length.ToString("X") + "\r\n" + secondChunk + "\r\n" +
                "0\r\n\r\n";
            using var stream = new MemoryStream(Encoding.ASCII.GetBytes(raw));

            McpHttpRequest request = McpHttpWire.ReadRequest(stream, 1024 * 1024);

            Assert.True(request.HasBodyFraming);
            Assert.Equal(body, request.Body);
            Assert.Equal(200, CreateProtocol().CreateResponse(request).StatusCode);
        }

        [Fact]
        public void WireReader_RejectsAmbiguousContentLengthAndTransferEncoding()
        {
            string raw =
                "POST /mcp HTTP/1.1\r\n" +
                "Host: localhost:8972\r\n" +
                "Content-Length: 0\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n";
            using var stream = new MemoryStream(Encoding.ASCII.GetBytes(raw));

            Assert.Throws<InvalidDataException>(
                () => McpHttpWire.ReadRequest(stream, 1024 * 1024));
        }

        [Fact]
        public void WireReader_InvalidUtf8SurfacesAsInvalidDataSoServerReturns400()
        {
            // McpServer.HandleClient turns InvalidDataException into 400. Any other
            // exception type escapes to the outer handler and drops the connection
            // with no response, so the exact type is the contract being asserted.
            byte[] invalidBody = { 0xC3, 0x28 };
            var header = Encoding.ASCII.GetBytes(
                "POST /mcp HTTP/1.1\r\n" +
                "Host: localhost:8972\r\n" +
                "Content-Type: application/json\r\n" +
                "Accept: application/json, text/event-stream\r\n" +
                "MCP-Protocol-Version: 2026-07-28\r\n" +
                "Mcp-Method: server/discover\r\n" +
                "Content-Length: " + invalidBody.Length + "\r\n\r\n");
            var raw = new byte[header.Length + invalidBody.Length];
            Buffer.BlockCopy(header, 0, raw, 0, header.Length);
            Buffer.BlockCopy(invalidBody, 0, raw, header.Length, invalidBody.Length);
            using var stream = new MemoryStream(raw);

            var error = Assert.Throws<InvalidDataException>(
                () => McpHttpWire.ReadRequest(stream, 1024 * 1024));
            Assert.IsType<DecoderFallbackException>(error.InnerException);
        }

        [Fact]
        public void Post_RequiresExplicitBodyFraming()
        {
            McpHttpRequest request = CurrentPost("no-framing", "server/discover");
            request.HasBodyFraming = false;

            Assert.Equal(411, CreateProtocol().CreateResponse(request).StatusCode);
        }

        [Fact]
        public void DeeplyNestedJson_Returns400InsteadOfCrashing()
        {
            StreamableHttpProtocol protocol = CreateProtocol();
            var body = new StringBuilder();
            body.Append("{\"a\":");
            for (int i = 0; i < 500; i++) body.Append('[');
            body.Append('1');
            for (int i = 0; i < 500; i++) body.Append(']');
            body.Append('}');

            McpHttpResponse response = protocol.CreateResponse(Post(body.ToString()));

            Assert.Equal(400, response.StatusCode);
            Assert.Equal(-32700L, ErrorCode(response));
        }

        [Fact]
        public void HugeExponentNumber_IsRejectedAsParseError()
        {
            StreamableHttpProtocol protocol = CreateProtocol();
            McpHttpRequest request = Post(
                "{\"jsonrpc\":\"2.0\",\"id\":\"huge\",\"method\":\"server/discover\"," +
                "\"params\":{\"a\":1e999," + CurrentMetadata + "}}");
            AddCurrentHeaders(request, "server/discover");

            McpHttpResponse response = protocol.CreateResponse(request);

            Assert.Equal(400, response.StatusCode);
            Assert.Equal(-32700L, ErrorCode(response));
        }

        [Fact]
        public void NullId_IsAcceptedAsRequestId()
        {
            StreamableHttpProtocol protocol = CreateProtocol();
            McpHttpRequest request = Post(
                "{\"jsonrpc\":\"2.0\",\"id\":null,\"method\":\"server/discover\"," +
                "\"params\":{" + CurrentMetadata + "}}");
            AddCurrentHeaders(request, "server/discover");

            McpHttpResponse response = protocol.CreateResponse(request);

            Assert.Equal(200, response.StatusCode);
            Dictionary<string, object> envelope = Parse(response.Body);
            Assert.True(envelope.ContainsKey("id"));
            Assert.Null(envelope["id"]);
        }

        [Fact]
        public void ToolErrorContentItem_IsMarkedAsErrorResult()
        {
            var registry = new ToolRegistry();
            registry.Register(new FakeTool("alpha"));
            var handler = new McpJsonRpcHandler(
                registry,
                (tool, arguments) => new McpContentItem
                {
                    type = "text",
                    text = "boom",
                    isError = true
                });
            var protocol = new StreamableHttpProtocol(8972, handler);

            McpHttpResponse response = protocol.CreateResponse(
                CurrentPost(
                    "err-current",
                    "tools/call",
                    "\"name\":\"alpha\",\"arguments\":{}",
                    "alpha"));

            Assert.Equal(200, response.StatusCode);
            var result = Assert.IsType<Dictionary<string, object>>(Parse(response.Body)["result"]);
            Assert.True(Assert.IsType<bool>(result["isError"]));
        }

        [Fact]
        public void MissingHostHeader_Returns400()
        {
            StreamableHttpProtocol protocol = CreateProtocol();
            McpHttpRequest request = CurrentPost("no-host", "server/discover");
            request.Headers.Remove("Host");

            Assert.Equal(400, protocol.CreateResponse(request).StatusCode);
        }

        [Fact]
        public void WireReader_RejectsHugeContentLengthBeforeIntegerOverflow()
        {
            string raw =
                "POST /mcp HTTP/1.1\r\n" +
                "Host: localhost:8972\r\n" +
                "Content-Type: application/json\r\n" +
                "Accept: application/json, text/event-stream\r\n" +
                "MCP-Protocol-Version: 2026-07-28\r\n" +
                "Mcp-Method: server/discover\r\n" +
                "Content-Length: 2147483647\r\n\r\n";
            using var stream = new MemoryStream(Encoding.ASCII.GetBytes(raw));

            var error = Assert.Throws<InvalidDataException>(
                () => McpHttpWire.ReadRequest(stream, 1024 * 1024));
            Assert.Contains("Content-Length", error.Message);
        }

        [Fact]
        public void WireReader_DecodesChunkedBodyAcrossManySmallReads()
        {
            string body = CurrentRequest("chunk-small", "server/discover");
            string raw =
                "POST /mcp HTTP/1.1\r\n" +
                "Host: localhost:8972\r\n" +
                "Content-Type: application/json\r\n" +
                "Accept: application/json, text/event-stream\r\n" +
                "MCP-Protocol-Version: 2026-07-28\r\n" +
                "Mcp-Method: server/discover\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                body.Length.ToString("X") + "\r\n" + body + "\r\n" +
                "0\r\n\r\n";
            using var stream = new SlowStream(Encoding.ASCII.GetBytes(raw), 3);

            McpHttpRequest request = McpHttpWire.ReadRequest(stream, 1024 * 1024);

            Assert.True(request.HasBodyFraming);
            Assert.Equal(body, request.Body);
            Assert.Equal(200, CreateProtocol().CreateResponse(request).StatusCode);
        }

        [Fact]
        public void WireWriter_EmitsEmpty202WithDefensiveHeaders()
        {
            using var stream = new MemoryStream();

            McpHttpWire.WriteResponse(stream, McpHttpResponse.Empty(202, "Accepted"));

            string raw = Encoding.ASCII.GetString(stream.ToArray());
            Assert.StartsWith("HTTP/1.1 202 Accepted\r\n", raw);
            Assert.Contains("Content-Length: 0\r\n", raw);
            Assert.Contains("Cache-Control: no-store\r\n", raw);
            Assert.Contains("X-Content-Type-Options: nosniff\r\n", raw);
        }

        private static StreamableHttpProtocol CreateProtocol(params IMcpTool[] tools)
        {
            var registry = new ToolRegistry();
            foreach (IMcpTool tool in tools)
            {
                registry.Register(tool);
            }

            var handler = new McpJsonRpcHandler(
                registry,
                (tool, arguments) =>
                {
                    object value = tool.Execute(arguments);
                    return new McpContentItem
                    {
                        type = "text",
                        text = value?.ToString() ?? string.Empty
                    };
                });
            return new StreamableHttpProtocol(8972, handler);
        }

        private static McpHttpRequest CurrentPost(
            string id,
            string method,
            string additionalParams = null,
            string nameHeader = null)
        {
            McpHttpRequest request = Post(CurrentRequest(id, method, additionalParams));
            AddCurrentHeaders(request, method);
            if (nameHeader != null) request.Headers["Mcp-Name"] = nameHeader;
            return request;
        }

        private static string CurrentRequest(
            string id,
            string method,
            string additionalParams = null)
        {
            return
                "{\"jsonrpc\":\"2.0\",\"id\":\"" + id +
                "\",\"method\":\"" + method + "\",\"params\":{" +
                CurrentMetadata +
                (string.IsNullOrEmpty(additionalParams)
                    ? string.Empty
                    : "," + additionalParams) +
                "}}";
        }

        private static void AddCurrentHeaders(McpHttpRequest request, string method)
        {
            request.Headers["MCP-Protocol-Version"] = "2026-07-28";
            request.Headers["Mcp-Method"] = method;
        }

        private static McpHttpRequest StandardPost(
            string id,
            string method,
            string additionalParams = null)
        {
            return Post(
                "{\"jsonrpc\":\"2.0\",\"id\":\"" + id +
                "\",\"method\":\"" + method + "\",\"params\":{" +
                additionalParams +
                "}}");
        }

        private static void AddStandardVersionHeader(McpHttpRequest request)
        {
            request.Headers["MCP-Protocol-Version"] = "2025-11-25";
        }

        private static McpHttpRequest Post(string body)
        {
            return BaseRequest("POST", "/mcp", body);
        }

        private static McpHttpRequest BaseRequest(string method, string target, string body)
        {
            return new McpHttpRequest
            {
                Method = method,
                Target = target,
                Body = body,
                HasBodyFraming = true,
                Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Host"] = "localhost:8972",
                    ["Content-Type"] = "application/json; charset=utf-8",
                    ["Accept"] = "application/json, text/event-stream"
                }
            };
        }

        private static Dictionary<string, object> Parse(string json)
        {
            return Assert.IsType<Dictionary<string, object>>(
                JsonHelper.DeserializeSimple(json));
        }

        private static long ErrorCode(McpHttpResponse response)
        {
            var error = Assert.IsType<Dictionary<string, object>>(Parse(response.Body)["error"]);
            return Assert.IsType<long>(error["code"]);
        }

        private sealed class SlowStream : MemoryStream
        {
            private readonly int _maxChunk;

            internal SlowStream(byte[] buffer, int maxChunk) : base(buffer)
            {
                _maxChunk = maxChunk;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return base.Read(buffer, offset, Math.Min(count, _maxChunk));
            }
        }

        private sealed class FakeTool : IMcpTool
        {
            private readonly bool _throws;

            internal FakeTool(string name, bool throws = false)
            {
                Name = name;
                _throws = throws;
            }

            public string Name { get; }
            public string Description => "fake";
            public Dictionary<string, object> InputSchema =>
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>()
                };

            public object Execute(Dictionary<string, object> args)
            {
                if (_throws) throw new InvalidOperationException("expected failure");
                return Name;
            }
        }
    }
}
