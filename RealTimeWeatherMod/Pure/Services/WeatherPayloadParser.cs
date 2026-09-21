using System;
using System.Globalization;
using ChillWithYou.EnvSync.Models;

namespace ChillWithYou.EnvSync.Services
{
    internal static class WeatherPayloadParser
    {
        internal static WeatherInfo ParseSeniverseWeather(string json, DateTime now)
        {
            try
            {
                if (string.IsNullOrEmpty(json) ||
                    (json.Contains("\"status\"") && !json.Contains("\"results\"")))
                {
                    return null;
                }

                int nowIndex = json.IndexOf("\"now\"", StringComparison.Ordinal);
                if (nowIndex < 0) return null;

                if (!int.TryParse(ExtractStringValue(json, "\"code\":\"", "\""), out int code) ||
                    !int.TryParse(ExtractStringValue(json, "\"temperature\":\"", "\""), out int temperature))
                    return null;
                string text = ExtractStringValue(json, "\"text\":\"", "\"");
                if (string.IsNullOrEmpty(text)) return null;

                return new WeatherInfo
                {
                    Code = code,
                    Text = text,
                    Temperature = temperature,
                    Condition = WeatherConditionMapper.FromSeniverseCode(code),
                    UpdateTime = now
                };
            }
            catch
            {
                return null;
            }
        }

        internal static WeatherInfo ParseOpenMeteoWeather(string json, DateTime now)
        {
            string current = ExtractObjectProperty(json, "\"current\"");
            if (string.IsNullOrEmpty(current)) return null;

            if (!TryExtractDoubleValue(current, "\"weather_code\"", out double code) ||
                code != Math.Truncate(code) || code < int.MinValue || code > int.MaxValue ||
                !TryExtractDoubleValue(current, "\"temperature_2m\"", out double temperature) ||
                Math.Round(temperature) < int.MinValue || Math.Round(temperature) > int.MaxValue)
            {
                return null;
            }
            int weatherCode = (int)code;
            double precipitation = ExtractDoubleValue(current, "\"precipitation\"");
            double rain = ExtractDoubleValue(current, "\"rain\"");
            double showers = ExtractDoubleValue(current, "\"showers\"");
            double snowfall = ExtractDoubleValue(current, "\"snowfall\"");
            double cloudCover = ExtractDoubleValue(current, "\"cloud_cover\"");

            return OpenMeteoWeatherMapper.Map(
                weatherCode,
                temperature,
                precipitation,
                rain,
                showers,
                snowfall,
                cloudCover,
                now);
        }

        internal static SunData ParseSeniverseSunSchedule(string json)
        {
            int sunIndex = string.IsNullOrEmpty(json)
                ? -1
                : json.IndexOf("\"sun\"", StringComparison.Ordinal);
            if (sunIndex < 0) return null;

            string sunrise = ExtractStringValue(json, "\"sunrise\":\"", "\"");
            string sunset = ExtractStringValue(json, "\"sunset\":\"", "\"");
            return string.IsNullOrEmpty(sunrise) || string.IsNullOrEmpty(sunset)
                ? null
                : new SunData { sunrise = sunrise, sunset = sunset };
        }

        internal static SunData ParseOpenMeteoSunSchedule(string json)
        {
            string sunrise = NormalizeOpenMeteoTime(
                ExtractFirstArrayString(json, "\"sunrise\""));
            string sunset = NormalizeOpenMeteoTime(
                ExtractFirstArrayString(json, "\"sunset\""));
            return string.IsNullOrEmpty(sunrise) || string.IsNullOrEmpty(sunset)
                ? null
                : new SunData { sunrise = sunrise, sunset = sunset };
        }

        private static string NormalizeOpenMeteoTime(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            int timeStart = value.IndexOf('T');
            string time = timeStart >= 0 ? value.Substring(timeStart + 1) : value;
            if (time.Length < 5) return null;
            string clock = time.Substring(0, 5);
            return TimeSpan.TryParseExact(clock, @"hh\:mm", CultureInfo.InvariantCulture, out _)
                ? clock
                : null;
        }

        private static double ExtractDoubleValue(string json, string propertyName)
        {
            return TryExtractDoubleValue(json, propertyName, out double value) ? value : 0d;
        }

        private static bool TryExtractDoubleValue(string json, string propertyName, out double result)
        {
            result = 0d;
            string quotedKey = propertyName.StartsWith("\"") && propertyName.EndsWith("\"")
                ? propertyName
                : "\"" + propertyName + "\"";

            int searchStart = 0;
            int start;
            while (true)
            {
                start = json.IndexOf(quotedKey, searchStart, StringComparison.Ordinal);
                if (start < 0) return false;

                int colon = start + quotedKey.Length;
                while (colon < json.Length && char.IsWhiteSpace(json[colon])) colon++;
                if (colon < json.Length && json[colon] == ':')
                {
                    start = colon + 1;
                    break;
                }

                searchStart = start + 1;
            }

            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
            int end = start;
            while (end < json.Length && "-+0123456789.eE".IndexOf(json[end]) >= 0) end++;
            if (end <= start) return false;
            if (end < json.Length && !char.IsWhiteSpace(json[end]) &&
                json[end] != ',' && json[end] != '}') return false;

            return double.TryParse(
                json.Substring(start, end - start),
                NumberStyles.Float | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture,
                out result) && !double.IsNaN(result) && !double.IsInfinity(result);
        }

        private static string ExtractStringValue(string json, string prefix, string suffix)
        {
            int start = json.IndexOf(prefix, StringComparison.Ordinal);
            if (start < 0) return null;
            start += prefix.Length;
            int end = json.IndexOf(suffix, start, StringComparison.Ordinal);
            return end < 0 ? null : json.Substring(start, end - start);
        }

        private static string ExtractFirstArrayString(string json, string propertyName)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int start = json.IndexOf(propertyName, StringComparison.Ordinal);
            if (start < 0) return null;
            start += propertyName.Length;
            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
            if (start >= json.Length || json[start++] != ':') return null;
            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
            if (start >= json.Length || json[start++] != '[') return null;
            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
            if (start >= json.Length || json[start] != '"') return null;

            int quoteStart = start;
            int quoteEnd = json.IndexOf('"', quoteStart + 1);
            return quoteEnd < 0
                ? null
                : json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
        }

        private static string ExtractObjectProperty(string json, string propertyName)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int nameStart = json.IndexOf(propertyName, StringComparison.Ordinal);
            if (nameStart < 0) return null;
            int colon = json.IndexOf(':', nameStart + propertyName.Length);
            if (colon < 0) return null;
            int start = json.IndexOf('{', colon + 1);
            if (start < 0) return null;

            int depth = 0;
            bool inString = false;
            bool escaped = false;
            for (int i = start; i < json.Length; i++)
            {
                char ch = json[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\' && inString)
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString) continue;
                if (ch == '{') depth++;
                else if (ch == '}' && --depth == 0)
                {
                    return json.Substring(start, i - start + 1);
                }
            }

            return null;
        }
    }
}
