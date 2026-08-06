using System;
using System.Globalization;
using ChillWithYou.EnvSync.Models;
using ChillWithYou.EnvSync.Services;

namespace ChillWithYou.EnvSync.Core
{
    /// <summary>
    /// Single source of truth for the time, sun schedule and weather observed by
    /// environment logic. Debug mode replaces inputs here instead of branching in
    /// every consumer.
    ///
    /// This is an entry-point read, not an accessor: capture once per pass and pass
    /// the resulting <see cref="EnvironmentInputSnapshot"/> down. Calling it per
    /// decision re-reads the clock and re-parses config on every call.
    /// </summary>
    internal static class ModEnvironmentContext
    {
        private static readonly TimeSpan DefaultSunrise = new TimeSpan(6, 30, 0);
        private static readonly TimeSpan DefaultSunset = new TimeSpan(18, 30, 0);

        internal static EnvironmentInputSnapshot Capture()
        {
            DateTime systemNow = DateTime.Now;
            TimeSpan configuredSunrise = ParseConfiguredTime(
                ChillEnvPlugin.Cfg_SunriseTime?.Value,
                DefaultSunrise);
            TimeSpan configuredSunset = ParseConfiguredTime(
                ChillEnvPlugin.Cfg_SunsetTime?.Value,
                DefaultSunset);

            if (ChillEnvPlugin.Cfg_DebugMode?.Value != true)
            {
                return CreateSnapshot(
                    false,
                    true,
                    null,
                    systemNow,
                    configuredSunrise,
                    configuredSunset,
                    WeatherAcquisitionRuntime.CurrentWeather);
            }

            var input = ReadDebugInput();
            if (DebugScenario.TryCreate(input, systemNow, out var scenario, out var error))
            {
                return CreateSnapshot(
                    true,
                    true,
                    null,
                    scenario.Now,
                    scenario.Sunrise,
                    scenario.Sunset,
                    scenario.Weather);
            }

            // Manual config edits can still be malformed. Keep debug weather
            // useful while falling back to the real clock and valid production
            // sun schedule; the snapshot exposes the validation error to MCP.
            var fallbackInput = new DebugScenarioInput
            {
                WeatherCode = input.WeatherCode,
                Temperature = input.Temperature,
                WeatherText = input.WeatherText,
                CurrentDateTime = string.Empty,
                Sunrise = FormatTime(configuredSunrise),
                Sunset = FormatTime(configuredSunset)
            };
            DebugScenario.TryCreate(
                fallbackInput,
                systemNow,
                out var fallback,
                out _);

            return CreateSnapshot(
                true,
                false,
                error,
                fallback.Now,
                fallback.Sunrise,
                fallback.Sunset,
                fallback.Weather);
        }

        internal static DebugScenarioInput ReadDebugInput()
        {
            return new DebugScenarioInput
            {
                WeatherCode = ChillEnvPlugin.Cfg_DebugCode?.Value ?? 1,
                Temperature = ChillEnvPlugin.Cfg_DebugTemp?.Value ?? 25,
                WeatherText = ChillEnvPlugin.Cfg_DebugText?.Value ?? "DebugWeather",
                CurrentDateTime = ChillEnvPlugin.Cfg_DebugCurrentDateTime?.Value ?? "12:00",
                Sunrise = ChillEnvPlugin.Cfg_DebugSunriseTime?.Value ?? "06:30",
                Sunset = ChillEnvPlugin.Cfg_DebugSunsetTime?.Value ?? "18:30"
            };
        }

        private static EnvironmentInputSnapshot CreateSnapshot(
            bool isDebug,
            bool configurationValid,
            string configurationError,
            DateTime now,
            TimeSpan sunrise,
            TimeSpan sunset,
            WeatherInfo weather)
        {
            return new EnvironmentInputSnapshot
            {
                IsDebug = isDebug,
                ConfigurationValid = configurationValid,
                ConfigurationError = configurationError,
                Now = now,
                Sunrise = sunrise,
                Sunset = sunset,
                Weather = weather,
                TimeSegment = DebugScenario.DetermineTimeSegment(now, sunrise, sunset)
            };
        }

        private static TimeSpan ParseConfiguredTime(string value, TimeSpan fallback)
        {
            if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            return fallback;
        }

        private static string FormatTime(TimeSpan value)
        {
            return value.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        }
    }
}
