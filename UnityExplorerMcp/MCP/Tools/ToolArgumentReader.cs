using System.Collections.Generic;

namespace UnityExplorerMcp.MCP.Tools
{
    internal static class ToolArgumentReader
    {
        public static string GetString(Dictionary<string, object> args, string name)
        {
            return args != null && args.TryGetValue(name, out var value)
                ? value?.ToString()
                : null;
        }

        public static int GetInt(Dictionary<string, object> args, string name, int defaultValue = 0)
        {
            if (args == null || !args.TryGetValue(name, out var value)) return defaultValue;
            if (value is int i) return i;
            if (value is long l) return (int)l;
            if (value is double d) return (int)d;
            return defaultValue;
        }

        public static float? GetOptionalFloat(Dictionary<string, object> args, string name)
        {
            if (args == null || !args.TryGetValue(name, out var value)) return null;
            if (value is float f) return f;
            if (value is double d) return (float)d;
            if (value is long l) return l;
            if (value is int i) return i;
            return null;
        }

        public static float GetFloat(Dictionary<string, object> args, string name, float defaultValue = 0f)
        {
            return GetOptionalFloat(args, name) ?? defaultValue;
        }

        public static bool GetBool(Dictionary<string, object> args, string name, bool defaultValue = false)
        {
            return args != null && args.TryGetValue(name, out var value) && value is bool result
                ? result
                : defaultValue;
        }
    }
}
