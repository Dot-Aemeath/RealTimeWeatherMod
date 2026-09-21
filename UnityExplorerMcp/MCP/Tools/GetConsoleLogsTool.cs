using System.Collections.Generic;

namespace UnityExplorerMcp.MCP.Tools
{
    internal class GetConsoleLogsTool : IMcpTool
    {
        private readonly Core.ConsoleCaptureService _consoleService;

        public GetConsoleLogsTool(Core.ConsoleCaptureService consoleService)
        {
            _consoleService = consoleService;
        }

        public string Name => "get_console_logs";
        public string Description => "Get recent Unity console logs. Returns the last N log entries with timestamps, log levels, and messages.";
        public Dictionary<string, object> InputSchema => new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["count"] = new Dictionary<string, object>
                {
                    ["type"] = "number",
                    ["description"] = "Number of recent logs to return (default: 50, max: 500)"
                }
            }
        };

        public object Execute(Dictionary<string, object> args)
        {
            int count = 50;
            if (args != null && args.TryGetValue("count", out var cv))
            {
                if (int.TryParse(cv?.ToString(), out int parsed) && parsed > 0)
                    count = System.Math.Min(parsed, Core.ConsoleCaptureService.MaxLogs);
            }

            var logs = _consoleService.GetRecent(count);
            var json = MCP.JsonHelper.Serialize(logs);
            return json;
        }
    }
}
