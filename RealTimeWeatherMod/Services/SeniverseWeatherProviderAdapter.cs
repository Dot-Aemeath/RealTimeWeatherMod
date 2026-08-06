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

        public string Name => ProviderName;

        public bool IsConfigured(WeatherSourceSettings source)
        {
            return !string.IsNullOrEmpty(source.ApiKey) || SeniverseKeySource.HasDefaultKey;
        }

        public string GetCacheKey(WeatherSourceSettings source)
        {
            return ProviderName + ":" + source.Location.ToLowerInvariant();
        }

        public string DescribeTarget(WeatherSourceSettings source)
        {
            return "心知天气 城市 " + source.Location;
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

            string url = "https://api.seniverse.com/v3/weather/now.json?key=" + key +
                "&location=" + UnityWebRequest.EscapeURL(source.Location) +
                "&language=zh-Hans&unit=c";
            ChillEnvPlugin.Log?.LogInfo("[心知天气] 发起天气请求: " + source.Location);

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

            string url = "https://api.seniverse.com/v3/geo/sun.json?key=" + key +
                "&location=" + UnityWebRequest.EscapeURL(source.Location) +
                "&language=zh-Hans&start=0&days=1";
            ChillEnvPlugin.Log?.LogInfo("[心知天气] 发起日出日落请求: " + source.Location);

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
