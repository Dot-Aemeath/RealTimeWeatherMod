using System;
using System.Collections;
using System.Globalization;
using ChillWithYou.EnvSync.Core;
using ChillWithYou.EnvSync.Models;

namespace ChillWithYou.EnvSync.Services
{
    internal sealed class OpenMeteoWeatherProviderAdapter : IWeatherProviderAdapter
    {
        internal const string ProviderName = "OpenMeteo";
        private const string LogTag = "[OpenMeteo]";

        public string Name => ProviderName;

        public bool IsConfigured(WeatherSourceSettings source)
        {
            return true;
        }

        public string GetCacheKey(WeatherSourceSettings source)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}:{1:0.####},{2:0.####}",
                ProviderName,
                source.Latitude,
                source.Longitude);
        }

        public string DescribeTarget(WeatherSourceSettings source)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} 坐标 {1:0.####}, {2:0.####}",
                ProviderName,
                source.Latitude,
                source.Longitude);
        }

        public IEnumerator FetchWeather(
            WeatherSourceSettings source,
            Action<ProviderFetchResult<WeatherInfo>> onComplete)
        {
            string url = string.Format(
                CultureInfo.InvariantCulture,
                "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&current=temperature_2m,relative_humidity_2m,precipitation,rain,showers,snowfall,weather_code,is_day,cloud_cover&timezone=auto&forecast_days=1",
                source.Latitude,
                source.Longitude);
            ChillEnvPlugin.Log?.LogInfo("[OpenMeteo] 发起天气请求: " + DescribeTarget(source));

            return ProviderJsonRequest.Send(
                LogTag,
                "天气",
                url,
                10,
                payload => WeatherPayloadParser.ParseOpenMeteoWeather(payload, DateTime.Now),
                onComplete);
        }

        public IEnumerator FetchSunSchedule(
            WeatherSourceSettings source,
            Action<ProviderFetchResult<SunData>> onComplete)
        {
            string url = string.Format(
                CultureInfo.InvariantCulture,
                "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&daily=sunrise,sunset&timezone=auto&forecast_days=1",
                source.Latitude,
                source.Longitude);
            ChillEnvPlugin.Log?.LogInfo("[OpenMeteo] 发起日出日落请求: " + DescribeTarget(source));

            return ProviderJsonRequest.Send(
                LogTag,
                "日出日落",
                url,
                15,
                WeatherPayloadParser.ParseOpenMeteoSunSchedule,
                onComplete);
        }
    }
}
