using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using BepInEx.Logging;
using UnityExplorerMcp.Core;
using UnityExplorerMcp.MCP.Tools;

namespace UnityExplorerMcp.MCP
{
    /// <summary>
    /// Local TCP host for the Streamable HTTP module. Registration and lifecycle
    /// are the complete external interface; HTTP and JSON-RPC details stay behind it.
    /// </summary>
    internal sealed class McpServer
    {
        private const int MaxRequestBytes = 1024 * 1024;

        private readonly int _port;
        private readonly ManualLogSource _logger;
        private readonly ToolRegistry _toolRegistry;
        private readonly StreamableHttpProtocol _protocol;
        private TcpListener _listener;
        private CancellationTokenSource _cts;

        internal McpServer(int port, ManualLogSource logger)
        {
            _port = port;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _toolRegistry = new ToolRegistry();
            var jsonRpc = new McpJsonRpcHandler(_toolRegistry, ExecuteTool);
            _protocol = new StreamableHttpProtocol(port, jsonRpc);
        }

        internal void RegisterTool(IMcpTool tool)
        {
            _toolRegistry.Register(tool);
        }

        internal void Start()
        {
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();

            var thread = new Thread(AcceptClients)
            {
                IsBackground = true,
                Name = "MCP-Streamable-HTTP"
            };
            thread.Start();

            _logger.LogInfo(
                "[UnityExplorerMcp] Streamable HTTP listening on http://localhost:" +
                _port + StreamableHttpProtocol.EndpointPath);
            _logger.LogInfo(
                "[UnityExplorerMcp] Protocol versions: " +
                string.Join(", ", McpJsonRpcHandler.GetSupportedProtocolVersions()));
            _logger.LogInfo(
                "[UnityExplorerMcp] Tools: " +
                _toolRegistry.GetToolDescriptions().Count + " registered");
        }

        internal void Stop()
        {
            _cts?.Cancel();
            _listener?.Stop();
        }

        private void AcceptClients()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = _listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(HandleClient, client);
                }
                catch when (_cts.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError("[UnityExplorerMcp] Accept error: " + ex.Message);
                }
            }
        }

        private void HandleClient(object state)
        {
            var client = (TcpClient)state;
            try
            {
                client.ReceiveTimeout = 15000;
                client.SendTimeout = 15000;
                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    McpHttpRequest request;
                    try
                    {
                        request = McpHttpWire.ReadRequest(stream, MaxRequestBytes);
                    }
                    catch (InvalidDataException)
                    {
                        McpHttpWire.WriteResponse(
                            stream,
                            McpHttpResponse.Text(400, "Bad Request"));
                        return;
                    }

                    if (request == null)
                    {
                        // Port readiness probes may connect and close without an
                        // HTTP request. Nothing remains to receive a response.
                        return;
                    }

                    McpHttpWire.WriteResponse(
                        stream,
                        _protocol.CreateResponse(request));
                }
            }
            catch (IOException)
            {
                // Client disconnects and request timeouts are normal transport events.
            }
            catch (Exception ex)
            {
                _logger.LogError("[UnityExplorerMcp] Handle error: " + ex.Message);
            }
        }

        private static McpContentItem ExecuteTool(
            IMcpTool tool,
            Dictionary<string, object> arguments)
        {
            if (tool is MainThreadTool mainThreadTool)
            {
                object result = MainThreadDispatcher.Execute(
                    () => (object)mainThreadTool.ExecuteOnMain(arguments));
                return (McpContentItem)result;
            }

            object toolResult = tool.Execute(arguments);
            string text = toolResult is string value
                ? value
                : JsonHelper.Serialize(toolResult);
            return new McpContentItem
            {
                type = "text",
                text = text
            };
        }
    }

    internal abstract class MainThreadTool : IMcpTool
    {
        public abstract string Name { get; }
        public abstract string Description { get; }
        public abstract Dictionary<string, object> InputSchema { get; }
        object IMcpTool.Execute(Dictionary<string, object> args) => Execute(args);
        public abstract object Execute(Dictionary<string, object> args);
        public abstract McpContentItem ExecuteOnMain(Dictionary<string, object> args);
    }
}
