using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityExplorerMcp.Core;
using UnityExplorerMcp.MCP;
using UnityExplorerMcp.MCP.Tools;

namespace UnityExplorerMcp
{
    [BepInPlugin("chillwithyou.unityexplorermcp", "UnityExplorer MCP Bridge", "1.0.0")]
    [BepInDependency("com.sinai.unityexplorer", BepInDependency.DependencyFlags.HardDependency)]
    public class UnityExplorerMcpPlugin : BaseUnityPlugin
    {
        private static McpServer _mcpServer;
        private static ConsoleCaptureService _consoleService;
        private static bool _shutdownRequested = false;

        private void Awake()
        {
            Logger.LogInfo("[UnityExplorerMcp] Initializing...");

            var port = Config.Bind("MCP", "Port", 8972,
                "HTTP server port for MCP connections").Value;
            var enabled = Config.Bind("MCP", "Enabled", false,
                "Enable the local debugging server. Restart required; keep disabled during normal play.").Value;
            var allowExecuteCode = Config.Bind("MCP", "AllowExecuteCode", false,
                "Expose arbitrary C# execution to local clients. Trusted development only; restart required.").Value;
            if (!enabled)
            {
                Logger.LogInfo("[UnityExplorerMcp] Local debugging server disabled.");
                return;
            }

            MainThreadDispatcher.InitializeFromMainThread();
            SceneManager.sceneUnloaded += OnSceneUnloaded;

            _consoleService = new ConsoleCaptureService(Logger);
            _consoleService.Start();

            _mcpServer = new McpServer(port, Logger);
            _mcpServer.RegisterTool(new GetUiHierarchyTool());
            _mcpServer.RegisterTool(new InspectElementTool());
            _mcpServer.RegisterTool(new SearchElementsTool());
            if (allowExecuteCode) _mcpServer.RegisterTool(new ExecuteCodeTool());
            _mcpServer.RegisterTool(new EnvSyncDebugTool());
            _mcpServer.RegisterTool(new GetConsoleLogsTool(_consoleService));
            _mcpServer.RegisterTool(new TakeScreenshotTool());
            _mcpServer.RegisterTool(new GetMouseElementTool());
            _mcpServer.RegisterTool(new InspectScreenPointTool());
            _mcpServer.RegisterTool(new CompareUiElementsTool());
            _mcpServer.RegisterTool(new PreviewUiLayoutTool());
            _mcpServer.RegisterTool(new RollbackUiPreviewTool());
            _mcpServer.RegisterTool(new SearchUiSpritesTool());
            _mcpServer.RegisterTool(new ListScenesTool());
            _mcpServer.Start();

            Logger.LogInfo("[UnityExplorerMcp] Plugin initialized. MCP server on port " + port);
        }

        private void OnDestroy()
        {
            if (_shutdownRequested) return;
            _shutdownRequested = true;
            Logger.LogInfo("[UnityExplorerMcp] OnDestroy called - server keeps running.");
        }

        private void OnApplicationQuit()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            _shutdownRequested = true;
            _mcpServer?.Stop();
            _consoleService?.Stop();
            Logger.LogInfo("[UnityExplorerMcp] Clean shutdown.");
        }

        private static void OnSceneUnloaded(Scene scene)
        {
            UiLayoutPreviewService.PruneDestroyedTargets();
        }
    }
}
