using System;
using System.Collections;
using ChillWithYou.EnvSync.Core;
using ChillWithYou.EnvSync.Models;

namespace ChillWithYou.EnvSync.Services
{
    /// <summary>
    /// Production composition adapter: captures current config once per request
    /// and delegates the complete protocol to WeatherAcquisitionModule.
    /// </summary>
    internal static class WeatherAcquisitionRuntime
    {
        private static readonly WeatherAcquisitionModule Module =
            new WeatherAcquisitionModule(
                new IWeatherProviderAdapter[]
                {
                    new OpenMeteoWeatherProviderAdapter(),
                    new SeniverseWeatherProviderAdapter()
                },
                SeniverseWeatherProviderAdapter.ProviderName,
                () => DateTime.Now);

        internal static WeatherInfo CurrentWeather => Module.CurrentWeather;
        internal static IWeatherAcquisition Acquisition => Module;

        internal static bool CanAcquire()
        {
            return Module.CanAcquire(CaptureSettings());
        }

        internal static string DescribeTarget()
        {
            return Module.DescribeTarget(CaptureSettings());
        }

        internal static IEnumerator AcquireSunSchedule(
            Action<SunScheduleFetchResult> onComplete)
        {
            return Module.AcquireSunSchedule(CaptureSettings(), onComplete);
        }

        internal static void Invalidate()
        {
            Module.Invalidate();
        }

        internal static WeatherSourceSettings CaptureSettings()
        {
            return new WeatherSourceSettings(
                ChillEnvPlugin.Cfg_WeatherProvider?.Value ?? SeniverseWeatherProviderAdapter.ProviderName,
                ChillEnvPlugin.Cfg_SeniverseKey?.Value ?? string.Empty,
                ChillEnvPlugin.Cfg_Location?.Value ?? string.Empty,
                ChillEnvPlugin.Cfg_OpenMeteoLatitude?.Value ?? 39.9075d,
                ChillEnvPlugin.Cfg_OpenMeteoLongitude?.Value ?? 116.39723d,
                ChillEnvPlugin.Cfg_CacheExpiryMinutes?.Value ?? 60);
        }
    }
}
