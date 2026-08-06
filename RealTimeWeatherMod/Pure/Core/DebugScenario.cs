using System;
using System.Globalization;
using ChillWithYou.EnvSync.Models;

namespace ChillWithYou.EnvSync.Core
{
    internal enum DebugTimeSegment
    {
        Day,
        Sunset,
        Night
    }

    internal sealed class DebugScenarioInput
    {
        internal int WeatherCode;
        internal int Temperature;
        internal string WeatherText;
        internal string CurrentDateTime;
        internal string Sunrise;
        internal string Sunset;
    }

    internal sealed class DebugScenarioState
    {
        internal DebugScenarioState(
            DateTime now,
            TimeSpan sunrise,
            TimeSpan sunset,
            WeatherInfo weather)
        {
            Now = now;
            Sunrise = sunrise;
            Sunset = sunset;
            Weather = weather;
            TimeSegment = DebugScenario.DetermineTimeSegment(now, sunrise, sunset);
        }

        internal DateTime Now { get; }
        internal TimeSpan Sunrise { get; }
        internal TimeSpan Sunset { get; }
        internal WeatherInfo Weather { get; }
        internal DebugTimeSegment TimeSegment { get; }
    }

    /// <summary>
    /// Resolves every simulated input as one atomic scenario. Callers never need
    /// to know the accepted clock formats or reproduce the sunrise/sunset rules.
    /// </summary>
    internal static class DebugScenario
    {
        private static readonly string[] FullDateTimeFormats =
        {
            "yyyy-MM-dd HH:mm",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-ddTHH:mm",
            "yyyy-MM-ddTHH:mm:ss"
        };

        private static readonly string[] TimeOnlyFormats =
        {
            "H:mm",
            "HH:mm",
            "H:mm:ss",
            "HH:mm:ss"
        };

        internal static bool TryCreate(
            DebugScenarioInput input,
            DateTime systemNow,
            out DebugScenarioState state,
            out string error)
        {
            state = null;
            error = null;
            if (input == null)
            {
                error = "调试场景不能为空";
                return false;
            }

            if (!TryParseCurrentDateTime(input.CurrentDateTime, systemNow, out var now))
            {
                error = "SimulatedDateTime 必须是 HH:mm[:ss] 或 yyyy-MM-dd HH:mm[:ss]";
                return false;
            }

            if (!TryParseTimeOfDay(input.Sunrise, out var sunrise))
            {
                error = "SimulatedSunrise 必须是 HH:mm[:ss]";
                return false;
            }

            if (!TryParseTimeOfDay(input.Sunset, out var sunset))
            {
                error = "SimulatedSunset 必须是 HH:mm[:ss]";
                return false;
            }

            if (sunset <= sunrise)
            {
                error = "SimulatedSunset 必须晚于 SimulatedSunrise";
                return false;
            }

            if (sunset < TimeSpan.FromMinutes(30))
            {
                error = "SimulatedSunset 必须不早于 00:30";
                return false;
            }

            string weatherText = string.IsNullOrWhiteSpace(input.WeatherText)
                ? "DebugWeather"
                : input.WeatherText.Trim();
            var weather = new WeatherInfo
            {
                Code = input.WeatherCode,
                Temperature = input.Temperature,
                Text = weatherText,
                Condition = WeatherConditionMapper.FromSeniverseCode(input.WeatherCode),
                UpdateTime = now
            };

            state = new DebugScenarioState(now, sunrise, sunset, weather);
            return true;
        }

        internal static DebugTimeSegment DetermineTimeSegment(
            DateTime now,
            TimeSpan sunrise,
            TimeSpan sunset)
        {
            TimeSpan currentTime = now.TimeOfDay;
            TimeSpan sunsetStart = sunset.Subtract(TimeSpan.FromMinutes(30));
            TimeSpan sunsetEnd = sunset.Add(TimeSpan.FromMinutes(30));

            if (currentTime >= sunrise && currentTime < sunsetStart)
            {
                return DebugTimeSegment.Day;
            }

            if (currentTime >= sunsetStart && currentTime < sunsetEnd)
            {
                return DebugTimeSegment.Sunset;
            }

            return DebugTimeSegment.Night;
        }

        private static bool TryParseCurrentDateTime(
            string value,
            DateTime systemNow,
            out DateTime result)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result = systemNow;
                return true;
            }

            string normalized = value.Trim();
            if (DateTime.TryParseExact(
                normalized,
                FullDateTimeFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var fullDateTime))
            {
                result = DateTime.SpecifyKind(fullDateTime, systemNow.Kind);
                return true;
            }

            if (DateTime.TryParseExact(
                normalized,
                TimeOnlyFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var timeOnly))
            {
                result = systemNow.Date.Add(timeOnly.TimeOfDay);
                return true;
            }

            result = default(DateTime);
            return false;
        }

        private static bool TryParseTimeOfDay(string value, out TimeSpan result)
        {
            if (DateTime.TryParseExact(
                value?.Trim(),
                TimeOnlyFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var parsed))
            {
                result = parsed.TimeOfDay;
                return true;
            }

            result = default(TimeSpan);
            return false;
        }
    }
}
