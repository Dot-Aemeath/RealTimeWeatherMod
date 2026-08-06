using System;
using ChillWithYou.EnvSync.Models;

namespace ChillWithYou.EnvSync.Core
{
    /// <summary>
    /// One coherent reading of time, sun schedule and weather. Every decision made
    /// during a single pass must share the same snapshot: capture once at the entry
    /// point and pass it down. Re-capturing mid-pass lets the real clock advance and
    /// makes rules within one pass disagree about "now".
    /// </summary>
    internal sealed class EnvironmentInputSnapshot
    {
        internal bool IsDebug;
        internal bool ConfigurationValid;
        internal string ConfigurationError;
        internal DateTime Now;
        internal TimeSpan Sunrise;
        internal TimeSpan Sunset;
        internal WeatherInfo Weather;
        internal DebugTimeSegment TimeSegment;

        internal EnvironmentInputSnapshot WithWeather(WeatherInfo weather)
        {
            return new EnvironmentInputSnapshot
            {
                IsDebug = IsDebug,
                ConfigurationValid = ConfigurationValid,
                ConfigurationError = ConfigurationError,
                Now = Now,
                Sunrise = Sunrise,
                Sunset = Sunset,
                Weather = weather,
                TimeSegment = TimeSegment
            };
        }
    }
}
