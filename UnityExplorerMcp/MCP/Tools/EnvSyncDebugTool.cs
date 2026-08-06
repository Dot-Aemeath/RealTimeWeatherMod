using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UnityExplorerMcp.MCP.Tools
{
    /// <summary>
    /// Thin MCP adapter over RealTimeWeatherMod's developer-facing debug seam.
    /// All scenario validation and environment decisions remain in the mod.
    /// </summary>
    internal sealed class EnvSyncDebugTool : MainThreadTool
    {
        private const string DebugControlTypeName =
            "ChillWithYou.EnvSync.Core.WeatherDebugControl";

        public override string Name => "envsync_debug";

        public override string Description =>
            "Apply a deterministic RealTimeWeatherMod weather/time scenario, " +
            "capture expected versus actual runtime state, or disable the scenario. " +
            "Actions: apply, snapshot, disable.";

        public override Dictionary<string, object> InputSchema =>
            new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["action"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["enum"] = new List<string> { "apply", "snapshot", "disable" },
                        ["description"] = "Operation to perform"
                    },
                    ["weatherCode"] = new Dictionary<string, object>
                    {
                        ["type"] = "number",
                        ["description"] = "Simulated Seniverse-normalized weather code; required for apply"
                    },
                    ["temperature"] = new Dictionary<string, object>
                    {
                        ["type"] = "number",
                        ["description"] = "Simulated Celsius temperature; required for apply"
                    },
                    ["weatherText"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["description"] = "Simulated weather description; required for apply"
                    },
                    ["currentDateTime"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["description"] = "HH:mm[:ss] or yyyy-MM-dd HH:mm[:ss]; required for apply"
                    },
                    ["sunrise"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["description"] = "Simulated sunrise as HH:mm[:ss]; required for apply"
                    },
                    ["sunset"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["description"] = "Simulated sunset as HH:mm[:ss]; required for apply"
                    }
                },
                ["required"] = new List<string> { "action" }
            };

        public override object Execute(Dictionary<string, object> args) => null;

        public override McpContentItem ExecuteOnMain(Dictionary<string, object> args)
        {
            try
            {
                Type debugControl = FindDebugControl();
                if (debugControl == null)
                {
                    return TextError("RealTimeWeatherMod 或 WeatherDebugControl 尚未加载");
                }

                string action = GetString(args, "action");
                if (string.Equals(action, "snapshot", StringComparison.OrdinalIgnoreCase))
                {
                    return Invoke(debugControl, "CaptureSnapshot", Type.EmptyTypes, null);
                }

                if (string.Equals(action, "disable", StringComparison.OrdinalIgnoreCase))
                {
                    return Invoke(debugControl, "DisableScenario", Type.EmptyTypes, null);
                }

                if (!string.Equals(action, "apply", StringComparison.OrdinalIgnoreCase))
                {
                    return TextError("action 必须是 apply、snapshot 或 disable");
                }

                if (!TryGetInt(args, "weatherCode", out var weatherCode) ||
                    !TryGetInt(args, "temperature", out var temperature) ||
                    !TryGetRequiredString(args, "weatherText", out var weatherText) ||
                    !TryGetRequiredString(args, "currentDateTime", out var currentDateTime) ||
                    !TryGetRequiredString(args, "sunrise", out var sunrise) ||
                    !TryGetRequiredString(args, "sunset", out var sunset))
                {
                    return TextError(
                        "apply 需要 weatherCode、temperature、weatherText、" +
                        "currentDateTime、sunrise 和 sunset");
                }

                return Invoke(
                    debugControl,
                    "ApplyScenario",
                    new[]
                    {
                        typeof(int),
                        typeof(int),
                        typeof(string),
                        typeof(string),
                        typeof(string),
                        typeof(string)
                    },
                    new object[]
                    {
                        weatherCode,
                        temperature,
                        weatherText,
                        currentDateTime,
                        sunrise,
                        sunset
                    });
            }
            catch (Exception ex)
            {
                return TextError(ex.Message);
            }
        }

        private static Type FindDebugControl()
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.GetName().Name,
                        "RealTimeWeatherMod",
                        StringComparison.Ordinal));
            return assembly?.GetType(DebugControlTypeName, false);
        }

        private static McpContentItem Invoke(
            Type debugControl,
            string methodName,
            Type[] parameterTypes,
            object[] arguments)
        {
            MethodInfo method = debugControl.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static,
                null,
                parameterTypes,
                null);
            if (method == null)
            {
                return TextError($"WeatherDebugControl.{methodName} 不可用");
            }

            try
            {
                string result = method.Invoke(null, arguments)?.ToString();
                return new McpContentItem
                {
                    type = "text",
                    text = result ?? TextErrorJson("调试接口未返回结果")
                };
            }
            catch (TargetInvocationException ex)
            {
                return TextError(ex.InnerException?.Message ?? ex.Message);
            }
        }

        private static string GetString(Dictionary<string, object> args, string key)
        {
            if (args != null && args.TryGetValue(key, out var value))
            {
                return value?.ToString();
            }

            return null;
        }

        private static bool TryGetRequiredString(
            Dictionary<string, object> args,
            string key,
            out string value)
        {
            value = GetString(args, key);
            return value != null;
        }

        private static bool TryGetInt(
            Dictionary<string, object> args,
            string key,
            out int value)
        {
            value = 0;
            if (args == null || !args.TryGetValue(key, out var raw))
            {
                return false;
            }

            if (raw is int integer)
            {
                value = integer;
                return true;
            }

            if (raw is long longValue &&
                longValue >= int.MinValue &&
                longValue <= int.MaxValue)
            {
                value = (int)longValue;
                return true;
            }

            if (raw is double doubleValue &&
                doubleValue >= int.MinValue &&
                doubleValue <= int.MaxValue &&
                Math.Abs(doubleValue % 1d) < double.Epsilon)
            {
                value = (int)doubleValue;
                return true;
            }

            return int.TryParse(raw?.ToString(), out value);
        }

        private static McpContentItem TextError(string error)
        {
            return new McpContentItem
            {
                type = "text",
                text = TextErrorJson(error)
            };
        }

        private static string TextErrorJson(string error)
        {
            return JsonHelper.Serialize(new
            {
                success = false,
                error = error ?? "未知调试错误"
            });
        }
    }
}
