using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace UnityExplorerMcp.MCP
{
    internal sealed class McpHttpRequest
    {
        internal string Method { get; set; }
        internal string Target { get; set; }
        internal Dictionary<string, string> Headers { get; set; }
        internal string Body { get; set; }
        internal bool HasBodyFraming { get; set; }
    }

    internal sealed class McpHttpResponse
    {
        private McpHttpResponse(
            int statusCode,
            string reasonPhrase,
            string contentType,
            string body)
        {
            StatusCode = statusCode;
            ReasonPhrase = reasonPhrase;
            ContentType = contentType;
            Body = body ?? string.Empty;
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        internal int StatusCode { get; }
        internal string ReasonPhrase { get; }
        internal string ContentType { get; }
        internal string Body { get; }
        internal Dictionary<string, string> Headers { get; }

        internal static McpHttpResponse Json(int statusCode, string reasonPhrase, string body)
        {
            return new McpHttpResponse(
                statusCode,
                reasonPhrase,
                "application/json; charset=utf-8",
                body);
        }

        internal static McpHttpResponse Text(int statusCode, string reasonPhrase)
        {
            return new McpHttpResponse(
                statusCode,
                reasonPhrase,
                "text/plain; charset=utf-8",
                statusCode + " " + reasonPhrase);
        }

        internal static McpHttpResponse Empty(int statusCode, string reasonPhrase)
        {
            return new McpHttpResponse(statusCode, reasonPhrase, null, string.Empty);
        }
    }

    /// <summary>
    /// Implements the current MCP Streamable HTTP transport. Requests carry all
    /// protocol metadata independently; responses are direct JSON because this server
    /// emits no request-scoped notifications. Multiple clients can use the endpoint.
    /// </summary>
    internal sealed class StreamableHttpProtocol
    {
        internal const string EndpointPath = "/mcp";

        private readonly int _port;
        private readonly McpJsonRpcHandler _handler;

        internal StreamableHttpProtocol(int port, McpJsonRpcHandler handler)
        {
            _port = port;
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        internal McpHttpResponse CreateResponse(McpHttpRequest request)
        {
            if (request == null)
            {
                return McpHttpResponse.Text(400, "Bad Request");
            }

            if (request.Headers == null || !request.Headers.ContainsKey("Host"))
            {
                // HTTP/1.1 requires a Host header; a missing one is a malformed
                // request, not a policy rejection.
                return McpHttpResponse.Text(400, "Bad Request");
            }

            if (!IsAllowedHost(request.Headers) || request.Headers.ContainsKey("Origin"))
            {
                return McpHttpResponse.Text(403, "Forbidden");
            }

            if (!string.Equals(request.Target, EndpointPath, StringComparison.Ordinal))
            {
                return McpHttpResponse.Text(404, "Not Found");
            }

            if (string.Equals(request.Method, "GET", StringComparison.Ordinal))
            {
                McpHttpResponse response = McpHttpResponse.Text(405, "Method Not Allowed");
                response.Headers["Allow"] = "POST";
                return response;
            }

            if (!string.Equals(request.Method, "POST", StringComparison.Ordinal))
            {
                McpHttpResponse response = McpHttpResponse.Text(405, "Method Not Allowed");
                response.Headers["Allow"] = "POST";
                return response;
            }

            if (!request.HasBodyFraming)
            {
                return McpHttpResponse.Text(411, "Length Required");
            }

            if (!HasMediaType(request.Headers, "Content-Type", "application/json"))
            {
                return McpHttpResponse.Text(415, "Unsupported Media Type");
            }

            if (!HasMediaType(request.Headers, "Accept", "application/json") ||
                !HasMediaType(request.Headers, "Accept", "text/event-stream"))
            {
                return McpHttpResponse.Text(406, "Not Acceptable");
            }

            McpHttpValidationResult validation =
                McpStreamableHttpRequestValidator.Validate(
                    request.Body,
                    request.Headers);
            if (!validation.IsValid)
            {
                return McpHttpResponse.Json(
                    validation.ErrorStatusCode,
                    ReasonPhrase(validation.ErrorStatusCode),
                    validation.ErrorBody);
            }

            McpProtocolResult result = _handler.Process(validation.Message);

            if (result.Kind == McpMessageKind.Notification)
            {
                return McpHttpResponse.Empty(202, "Accepted");
            }

            return McpHttpResponse.Json(
                result.HttpStatusCode,
                ReasonPhrase(result.HttpStatusCode),
                result.ResponseBody);
        }

        private static string ReasonPhrase(int statusCode)
        {
            switch (statusCode)
            {
                case 200: return "OK";
                case 202: return "Accepted";
                case 400: return "Bad Request";
                case 403: return "Forbidden";
                case 404: return "Not Found";
                case 405: return "Method Not Allowed";
                case 406: return "Not Acceptable";
                case 411: return "Length Required";
                case 415: return "Unsupported Media Type";
                default: return "Error";
            }
        }

        private bool IsAllowedHost(Dictionary<string, string> headers)
        {
            if (headers == null || !headers.TryGetValue("Host", out string host))
            {
                return false;
            }

            return string.Equals(
                       host,
                       "localhost:" + _port,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       host,
                       "127.0.0.1:" + _port,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasMediaType(
            Dictionary<string, string> headers,
            string headerName,
            string expectedMediaType)
        {
            if (headers == null || !headers.TryGetValue(headerName, out string value))
            {
                return false;
            }

            string[] entries = value.Split(',');
            foreach (string entry in entries)
            {
                int parameterStart = entry.IndexOf(';');
                string mediaType = parameterStart >= 0
                    ? entry.Substring(0, parameterStart).Trim()
                    : entry.Trim();
                if (string.Equals(
                        mediaType,
                        expectedMediaType,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal static class McpHttpWire
    {
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static McpHttpRequest ReadRequest(Stream stream, int maxRequestBytes)
        {
            var buffer = new byte[Math.Min(8192, maxRequestBytes)];
            int totalRead = 0;
            int bodyStart = -1;
            int contentLength = 0;
            bool hasContentLength = false;
            bool isChunked = false;
            int chunkPosition = -1;
            bool awaitingTrailerEnd = false;
            bool chunkedComplete = false;
            byte[] decodedChunkedBody = null;
            string method = null;
            string target = null;
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            using (var chunkOutput = new MemoryStream())
            {
                while (true)
                {
                    if (totalRead >= buffer.Length)
                    {
                        if (totalRead >= maxRequestBytes)
                        {
                            throw new InvalidDataException("HTTP request exceeds size limit");
                        }

                        Array.Resize(
                            ref buffer,
                            Math.Min(buffer.Length * 2, maxRequestBytes));
                    }

                    int read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
                    if (read <= 0)
                    {
                        if (totalRead == 0) return null;
                        break;
                    }

                    totalRead += read;
                    if (bodyStart < 0)
                    {
                        string headerText = Encoding.ASCII.GetString(buffer, 0, totalRead);
                        int headerEnd = headerText.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                        if (headerEnd >= 0)
                        {
                            bodyStart = headerEnd + 4;
                            ParseHeaders(
                                headerText.Substring(0, headerEnd),
                                out method,
                                out target,
                                headers);

                            if (headers.TryGetValue("Content-Length", out string lengthText))
                            {
                                hasContentLength = true;
                                if (!int.TryParse(
                                        lengthText,
                                        NumberStyles.None,
                                        CultureInfo.InvariantCulture,
                                        out contentLength))
                                {
                                    throw new InvalidDataException("Invalid Content-Length");
                                }
                            }

                            if (headers.TryGetValue(
                                    "Transfer-Encoding",
                                    out string transferEncoding))
                            {
                                if (hasContentLength)
                                {
                                    throw new InvalidDataException(
                                        "Content-Length and Transfer-Encoding cannot be combined");
                                }

                                if (!string.Equals(
                                        transferEncoding.Trim(),
                                        "chunked",
                                        StringComparison.OrdinalIgnoreCase))
                                {
                                    throw new InvalidDataException(
                                        "Unsupported Transfer-Encoding");
                                }

                                isChunked = true;
                                chunkPosition = bodyStart;
                            }

                            if (contentLength < 0 ||
                                (long)bodyStart + contentLength > maxRequestBytes)
                            {
                                throw new InvalidDataException("Invalid or oversized Content-Length");
                            }
                        }
                    }

                    if (bodyStart >= 0 && isChunked)
                    {
                        TryDecodeChunkedBody(
                            buffer,
                            ref chunkPosition,
                            ref awaitingTrailerEnd,
                            totalRead,
                            maxRequestBytes - bodyStart,
                            chunkOutput,
                            out chunkedComplete);
                        if (chunkedComplete)
                        {
                            decodedChunkedBody = chunkOutput.ToArray();
                            break;
                        }
                    }
                    else if (bodyStart >= 0 && totalRead >= (long)bodyStart + contentLength)
                    {
                        break;
                    }
                }

                if (bodyStart < 0 || method == null)
                {
                    throw new InvalidDataException("Incomplete HTTP headers");
                }

                if (!isChunked && totalRead < (long)bodyStart + contentLength)
                {
                    throw new InvalidDataException("Incomplete HTTP body");
                }

                string body;
                if (isChunked)
                {
                    if (!chunkedComplete)
                    {
                        throw new InvalidDataException("Incomplete chunked HTTP body");
                    }

                    body = DecodeBody(
                        decodedChunkedBody,
                        0,
                        decodedChunkedBody.Length);
                }
                else
                {
                    body = contentLength == 0
                        ? string.Empty
                        : DecodeBody(buffer, bodyStart, contentLength);
                }

                return new McpHttpRequest
                {
                    Method = method,
                    Target = target,
                    Headers = headers,
                    Body = body,
                    HasBodyFraming = hasContentLength || isChunked
                };
            }
        }

        /// <summary>
        /// Undecodable bytes are a malformed request like any other, so they surface
        /// as InvalidDataException — the one exception type ReadRequest promises its
        /// caller for bad input. A DecoderFallbackException escaping here would miss
        /// the caller's 400 path and drop the connection instead.
        /// </summary>
        private static string DecodeBody(byte[] buffer, int offset, int count)
        {
            try
            {
                return StrictUtf8.GetString(buffer, offset, count);
            }
            catch (DecoderFallbackException ex)
            {
                throw new InvalidDataException("HTTP body is not valid UTF-8", ex);
            }
        }

        /// <summary>
        /// Incrementally decodes a chunked body. <paramref name="position"/> and
        /// <paramref name="output"/> are caller-held across reads, so previously
        /// decoded chunks are not re-parsed after every read. When the current
        /// chunk's bytes are incomplete, position stays on the incomplete size
        /// line and the next call re-parses only that line. Once the zero-size
        /// chunk is consumed, <paramref name="awaitingTrailerEnd"/> switches the
        /// parser into trailer mode, where the position must not be re-parsed as
        /// a size line on the next call.
        /// </summary>
        private static void TryDecodeChunkedBody(
            byte[] buffer,
            ref int position,
            ref bool awaitingTrailerEnd,
            int totalRead,
            int maxBodyBytes,
            MemoryStream output,
            out bool complete)
        {
            complete = false;
            while (true)
            {
                if (awaitingTrailerEnd)
                {
                    // The zero-size chunk line is consumed; only an empty line
                    // ends the message. Incomplete trailer lines keep position.
                    int trailerEnd = FindCrlf(buffer, position, totalRead);
                    if (trailerEnd < 0) return;
                    if (trailerEnd == position)
                    {
                        complete = true;
                        return;
                    }

                    if (Array.IndexOf(
                            buffer,
                            (byte)':',
                            position,
                            trailerEnd - position) < 0)
                    {
                        throw new InvalidDataException("Invalid chunk trailer");
                    }

                    position = trailerEnd + 2;
                    continue;
                }

                int lineEnd = FindCrlf(buffer, position, totalRead);
                if (lineEnd < 0) return;

                string sizeLine = Encoding.ASCII.GetString(
                    buffer,
                    position,
                    lineEnd - position);
                int extensionStart = sizeLine.IndexOf(';');
                string sizeText = extensionStart >= 0
                    ? sizeLine.Substring(0, extensionStart).Trim()
                    : sizeLine.Trim();
                if (sizeText.Length == 0 ||
                    !int.TryParse(
                        sizeText,
                        NumberStyles.AllowHexSpecifier,
                        CultureInfo.InvariantCulture,
                        out int chunkSize) ||
                    chunkSize < 0)
                {
                    throw new InvalidDataException("Invalid chunk size");
                }

                if (chunkSize == 0)
                {
                    position = lineEnd + 2;
                    awaitingTrailerEnd = true;
                    continue;
                }

                if (output.Length + chunkSize > maxBodyBytes)
                {
                    throw new InvalidDataException("Chunked HTTP body exceeds size limit");
                }

                // Keep position on the size line until the chunk data and its
                // CRLF terminator are fully buffered; chunk data may itself
                // contain CRLF and must never be mistaken for a size line.
                int chunkDataStart = lineEnd + 2;
                if (totalRead - chunkDataStart < chunkSize + 2)
                {
                    return;
                }

                position = chunkDataStart;
                output.Write(buffer, position, chunkSize);
                position += chunkSize;
                if (buffer[position] != '\r' || buffer[position + 1] != '\n')
                {
                    throw new InvalidDataException("Invalid chunk terminator");
                }

                position += 2;
            }
        }

        private static int FindCrlf(byte[] buffer, int start, int totalRead)
        {
            for (int i = start; i + 1 < totalRead; i++)
            {
                if (buffer[i] == '\r' && buffer[i + 1] == '\n') return i;
            }

            return -1;
        }

        internal static void WriteResponse(Stream stream, McpHttpResponse response)
        {
            byte[] body = Encoding.UTF8.GetBytes(response.Body);
            var header = new StringBuilder();
            header.Append("HTTP/1.1 ");
            header.Append(response.StatusCode);
            header.Append(' ');
            header.Append(response.ReasonPhrase);
            header.Append("\r\n");
            if (!string.IsNullOrEmpty(response.ContentType))
            {
                header.Append("Content-Type: ");
                header.Append(response.ContentType);
                header.Append("\r\n");
            }

            foreach (KeyValuePair<string, string> pair in response.Headers)
            {
                header.Append(pair.Key);
                header.Append(": ");
                header.Append(pair.Value);
                header.Append("\r\n");
            }

            header.Append("Cache-Control: no-store\r\n");
            header.Append("X-Content-Type-Options: nosniff\r\n");
            header.Append("Connection: close\r\n");
            header.Append("Content-Length: ");
            header.Append(body.Length);
            header.Append("\r\n\r\n");

            byte[] headerBytes = Encoding.ASCII.GetBytes(header.ToString());
            stream.Write(headerBytes, 0, headerBytes.Length);
            if (body.Length > 0)
            {
                stream.Write(body, 0, body.Length);
            }
        }

        private static void ParseHeaders(
            string headerBlock,
            out string method,
            out string target,
            Dictionary<string, string> headers)
        {
            string[] lines = headerBlock.Split(
                new[] { "\r\n" },
                StringSplitOptions.None);
            string[] requestParts = lines[0].Split(' ');
            if (requestParts.Length != 3 ||
                !requestParts[2].StartsWith("HTTP/", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Invalid HTTP request line");
            }

            method = requestParts[0];
            target = requestParts[1];
            for (int i = 1; i < lines.Length; i++)
            {
                int separator = lines[i].IndexOf(':');
                if (separator <= 0)
                {
                    throw new InvalidDataException("Invalid HTTP header");
                }

                string name = lines[i].Substring(0, separator).Trim();
                string value = lines[i].Substring(separator + 1).Trim();
                if (headers.TryGetValue(name, out string existing))
                {
                    headers[name] = existing + "," + value;
                }
                else
                {
                    headers[name] = value;
                }
            }
        }
    }
}
