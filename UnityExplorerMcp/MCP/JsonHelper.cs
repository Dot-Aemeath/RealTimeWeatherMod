using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace UnityExplorerMcp.MCP
{
    internal static class JsonHelper
    {
        public static string Serialize(object obj, int maxDepth = 10)
        {
            return SerializeValue(obj, 0, maxDepth);
        }

        private static string SerializeValue(object obj, int depth, int maxDepth)
        {
            if (depth > maxDepth) return "\"<max depth>\"";
            if (obj == null) return "null";
            if (obj is string s) return EscapeString(s);
            if (obj is bool b) return b ? "true" : "false";
            if (obj is int i) return i.ToString();
            if (obj is long l) return l.ToString();
            if (obj is float f) return f.ToString("R", CultureInfo.InvariantCulture);
            if (obj is double d) return d.ToString("R", CultureInfo.InvariantCulture);
            if (obj is decimal m) return m.ToString(CultureInfo.InvariantCulture);
            if (obj is Enum e) return EscapeString(e.ToString());
            if (obj is IDictionary dict) return SerializeDictionary(dict, depth, maxDepth);
            if (obj is IList list) return SerializeArray(list, depth, maxDepth);
            if (obj is IEnumerable enumerable && !(obj is string))
                return SerializeArray(enumerable, depth, maxDepth);
            return SerializeObject(obj, depth, maxDepth);
        }

        private static string EscapeString(string s)
        {
            var sb = new StringBuilder();
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u" + ((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static string SerializeObject(object obj, int depth, int maxDepth)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            bool first = true;
            var type = obj.GetType();
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead) continue;
                if (prop.GetIndexParameters().Length > 0) continue;
                try
                {
                    var val = prop.GetValue(obj);
                    if (val == null) continue;
                    if (!first) sb.Append(',');
                    sb.Append(EscapeString(prop.Name));
                    sb.Append(':');
                    sb.Append(SerializeValue(val, depth + 1, maxDepth));
                    first = false;
                }
                catch { }
            }
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                try
                {
                    var val = field.GetValue(obj);
                    if (val == null) continue;
                    if (!first) sb.Append(',');
                    sb.Append(EscapeString(field.Name));
                    sb.Append(':');
                    sb.Append(SerializeValue(val, depth + 1, maxDepth));
                    first = false;
                }
                catch { }
            }
            sb.Append('}');
            return sb.ToString();
        }

        private static string SerializeArray(IEnumerable enumerable, int depth, int maxDepth)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            bool first = true;
            foreach (var item in enumerable)
            {
                if (!first) sb.Append(',');
                sb.Append(SerializeValue(item, depth + 1, maxDepth));
                first = false;
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string SerializeDictionary(IDictionary dict, int depth, int maxDepth)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            bool first = true;
            foreach (DictionaryEntry entry in dict)
            {
                if (!first) sb.Append(',');
                sb.Append(SerializeValue(entry.Key, depth + 1, maxDepth));
                sb.Append(':');
                sb.Append(SerializeValue(entry.Value, depth + 1, maxDepth));
                first = false;
            }
            sb.Append('}');
            return sb.ToString();
        }

        public static Dictionary<string, object> DeserializeSimple(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var parser = new JsonParser(json);
            return parser.ParseDocument();
        }

        private class JsonParser
        {
            private readonly string _json;
            private int _pos;
            private bool _failed;

            public JsonParser(string json)
            {
                _json = json;
                _pos = 0;
            }

            public Dictionary<string, object> ParseDocument()
            {
                Dictionary<string, object> result = ParseObject();
                SkipWhitespace();
                return !_failed && _pos == _json.Length ? result : null;
            }

            private void SkipWhitespace()
            {
                while (_pos < _json.Length && char.IsWhiteSpace(_json[_pos])) _pos++;
            }

            private char Peek()
            {
                SkipWhitespace();
                return _pos < _json.Length ? _json[_pos] : '\0';
            }

            private char Consume()
            {
                SkipWhitespace();
                return _pos < _json.Length ? _json[_pos++] : '\0';
            }

            public Dictionary<string, object> ParseObject()
            {
                if (Consume() != '{') return Fail<Dictionary<string, object>>();
                var dict = new Dictionary<string, object>();
                if (Peek() == '}') { _pos++; return dict; }
                while (true)
                {
                    var key = ParseString();
                    if (_failed || key == null) return null;
                    if (Consume() != ':') return Fail<Dictionary<string, object>>();
                    dict[key] = ParseValue();
                    if (_failed) return null;
                    var c = Consume();
                    if (c == '}') break;
                    if (c != ',') return Fail<Dictionary<string, object>>();
                }
                return dict;
            }

            private string ParseString()
            {
                if (Consume() != '"') return Fail<string>();
                var sb = new StringBuilder();
                while (_pos < _json.Length)
                {
                    var c = _json[_pos++];
                    if (c == '"') return sb.ToString();
                    if (c == '\\')
                    {
                        if (_pos >= _json.Length) return Fail<string>();
                        var esc = _json[_pos++];
                        switch (esc)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'u':
                                if (_pos + 4 > _json.Length)
                                    return Fail<string>();
                                string hex = _json.Substring(_pos, 4);
                                if (!int.TryParse(
                                        hex,
                                        NumberStyles.AllowHexSpecifier,
                                        CultureInfo.InvariantCulture,
                                        out int codePoint))
                                {
                                    return Fail<string>();
                                }
                                sb.Append((char)codePoint);
                                _pos += 4;
                                break;
                            default:
                                return Fail<string>();
                        }
                    }
                    else
                    {
                        if (c < 0x20) return Fail<string>();
                        sb.Append(c);
                    }
                }
                return Fail<string>();
            }

            private object ParseValue()
            {
                var c = Peek();
                if (c == '"') return ParseString();
                if (c == '{') return ParseObject();
                if (c == '[') return ParseArray();
                if (TryConsumeLiteral("true")) return true;
                if (TryConsumeLiteral("false")) return false;
                if (TryConsumeLiteral("null")) return null;
                return ParseNumber();
            }

            private List<object> ParseArray()
            {
                if (Consume() != '[') return Fail<List<object>>();
                var list = new List<object>();
                if (Peek() == ']') { _pos++; return list; }
                while (true)
                {
                    list.Add(ParseValue());
                    if (_failed) return null;
                    var c = Consume();
                    if (c == ']') break;
                    if (c != ',') return Fail<List<object>>();
                }
                return list;
            }

            private object ParseNumber()
            {
                int start = _pos;
                while (_pos < _json.Length && "-0123456789.eE+".IndexOf(_json[_pos]) >= 0) _pos++;
                var numStr = _json.Substring(start, _pos - start);
                if (numStr.Length == 0) return Fail<object>();
                if (numStr.Contains(".") || numStr.Contains("e") || numStr.Contains("E"))
                {
                    if (double.TryParse(
                        numStr,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double d))
                        return d;
                    return Fail<object>();
                }
                if (long.TryParse(
                    numStr,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long l))
                    return l;
                return Fail<object>();
            }

            private bool TryConsumeLiteral(string literal)
            {
                SkipWhitespace();
                if (_pos + literal.Length > _json.Length ||
                    !string.Equals(
                        _json.Substring(_pos, literal.Length),
                        literal,
                        StringComparison.Ordinal))
                {
                    return false;
                }

                _pos += literal.Length;
                return true;
            }

            private T Fail<T>()
            {
                _failed = true;
                return default(T);
            }
        }

        public static string SerializeJsonRpcRequest(string method, object args)
        {
            var sb = new StringBuilder();
            sb.Append("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":");
            sb.Append(EscapeString(method));
            sb.Append(",\"params\":");
            sb.Append(Serialize(args));
            sb.Append("}");
            return sb.ToString();
        }

        public static string SerializeJsonRpcResponseBody(object id, object result)
        {
            var sb = new StringBuilder();
            sb.Append("{\"jsonrpc\":\"2.0\",\"id\":");
            sb.Append(Serialize(id));
            sb.Append(",\"result\":");
            sb.Append(Serialize(result));
            sb.Append("}");
            return sb.ToString();
        }

        public static string SerializeJsonRpcResponse(object id, object resultContent)
        {
            var sb = new StringBuilder();
            sb.Append("{\"jsonrpc\":\"2.0\",\"id\":");
            sb.Append(Serialize(id));
            sb.Append(",\"result\":{\"content\":[");
            bool first = true;
            if (resultContent is IEnumerable<McpContentItem> items)
            {
                foreach (var item in items)
                {
                    if (!first) sb.Append(',');
                    sb.Append(Serialize(item));
                    first = false;
                }
            }
            else
            {
                sb.Append("{\"type\":\"text\",\"text\":");
                sb.Append(EscapeString(resultContent?.ToString() ?? "null"));
                sb.Append("}");
            }
            sb.Append("],\"isError\":false}}");
            return sb.ToString();
        }

        public static string SerializeJsonRpcError(
            object id,
            int code,
            string message,
            object data = null)
        {
            var sb = new StringBuilder();
            sb.Append("{\"jsonrpc\":\"2.0\",\"id\":");
            sb.Append(Serialize(id));
            sb.Append(",\"error\":{\"code\":");
            sb.Append(code);
            sb.Append(",\"message\":");
            sb.Append(EscapeString(message));
            if (data != null)
            {
                sb.Append(",\"data\":");
                sb.Append(Serialize(data));
            }
            sb.Append("}}");
            return sb.ToString();
        }
    }

    internal class McpContentItem
    {
        public string type { get; set; }
        public string text { get; set; }
        public string data { get; set; }
        public string mimeType { get; set; }
    }
}
