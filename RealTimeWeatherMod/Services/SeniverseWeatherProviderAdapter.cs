using System;
using System.Collections;
using UnityEngine.Networking;
using ChillWithYou.EnvSync.Core;
using ChillWithYou.EnvSync.Models;

namespace ChillWithYou.EnvSync.Services
{
    internal sealed class SeniverseWeatherProviderAdapter : IWeatherProviderAdapter
    {
        internal const string ProviderName = "Seniverse";
        private const string LogTag = "[心知天气]";

        /// <summary>心知天气自身支持的按 IP 定位取值。</summary>
        private const string AutoLocateLocation = "ip";

        public string Name => ProviderName;

        public bool IsConfigured(WeatherSourceSettings source)
        {
            return !string.IsNullOrEmpty(source.ApiKey) || SeniverseKeySource.HasDefaultKey;
        }

        public string GetCacheKey(WeatherSourceSettings source)
        {
            return ProviderName + ":" + ResolveLocation(source).ToLowerInvariant();
        }

        public string DescribeTarget(WeatherSourceSettings source)
        {
            string location = ResolveLocation(source);
            return string.Equals(location, AutoLocateLocation, StringComparison.OrdinalIgnoreCase)
                ? "心知天气 IP 自动定位"
                : "心知天气 城市 " + location;
        }

        /// <summary>
        /// 城市留空时落到心知自身的 "ip" 自动定位，而不是把空串当城市发出去 ——
        /// 后者只会让每个刷新周期都发一次注定失败的请求。清空设置面板里的城市
        /// 输入框因此降级为自动定位，而不是静默停止同步。
        /// </summary>
        private static string ResolveLocation(WeatherSourceSettings source)
        {
            return string.IsNullOrEmpty(source.Location)
                ? AutoLocateLocation
                : source.Location;
        }

        public IEnumerator FetchWeather(
            WeatherSourceSettings source,
            Action<ProviderFetchResult<WeatherInfo>> onComplete)
        {
            string key = SeniverseKeySource.Resolve(source.ApiKey);
            if (string.IsNullOrEmpty(key))
            {
                onComplete?.Invoke(ProviderFetchResult<WeatherInfo>.Failure(
                    "未配置心知天气 API Key"));
                yield break;
            }

            string location = ResolveLocation(source);
            string url = "https://api.seniverse.com/v3/weather/now.json?key=" + key +
                "&location=" + UnityWebRequest.EscapeURL(location) +
                "&language=zh-Hans&unit=c";
            ChillEnvPlugin.Log?.LogInfo("[心知天气] 发起天气请求: " + location);

            yield return ProviderJsonRequest.Send(
                LogTag,
                "天气",
                url,
                10,
                payload => WeatherPayloadParser.ParseSeniverseWeather(payload, DateTime.Now),
                onComplete);
        }

        public IEnumerator FetchSunSchedule(
            WeatherSourceSettings source,
            Action<ProviderFetchResult<SunData>> onComplete)
        {
            string key = SeniverseKeySource.Resolve(source.ApiKey);
            if (string.IsNullOrEmpty(key))
            {
                onComplete?.Invoke(ProviderFetchResult<SunData>.Failure(
                    "未配置心知天气 API Key"));
                yield break;
            }

            string location = ResolveLocation(source);
            string url = "https://api.seniverse.com/v3/geo/sun.json?key=" + key +
                "&location=" + UnityWebRequest.EscapeURL(location) +
                "&language=zh-Hans&start=0&days=1";
            ChillEnvPlugin.Log?.LogInfo("[心知天气] 发起日出日落请求: " + location);

            yield return ProviderJsonRequest.Send(
                LogTag,
                "日出日落",
                url,
                15,
                WeatherPayloadParser.ParseSeniverseSunSchedule,
                onComplete);
        }
    }
}
