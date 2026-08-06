using System;
using System.Collections;
using System.Collections.Generic;
using ChillWithYou.EnvSync.Models;

namespace ChillWithYou.EnvSync.Core
{
    internal sealed class WeatherSourceSettings
    {
        internal WeatherSourceSettings(
            string provider,
            string apiKey,
            string location,
            double latitude,
            double longitude,
            int cacheExpiryMinutes)
        {
            Provider = provider?.Trim() ?? string.Empty;
            ApiKey = apiKey ?? string.Empty;
            Location = location?.Trim() ?? string.Empty;
            Latitude = latitude;
            Longitude = longitude;
            CacheExpiryMinutes = Math.Max(1, cacheExpiryMinutes);
        }

        internal string Provider { get; }
        internal string ApiKey { get; }
        internal string Location { get; }
        internal double Latitude { get; }
        internal double Longitude { get; }
        internal int CacheExpiryMinutes { get; }
    }

    internal sealed class ProviderFetchResult<T> where T : class
    {
        private ProviderFetchResult(T value, string error)
        {
            Value = value;
            Error = error;
        }

        internal T Value { get; }
        internal string Error { get; }
        internal bool Succeeded => Value != null && string.IsNullOrEmpty(Error);

        internal static ProviderFetchResult<T> Success(T value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return new ProviderFetchResult<T>(value, null);
        }

        internal static ProviderFetchResult<T> Failure(string error)
        {
            return new ProviderFetchResult<T>(null, error ?? "天气源未返回结果");
        }
    }

    internal interface IWeatherProviderAdapter
    {
        /// <summary>
        /// Provider identifier, compared case-insensitively against
        /// <see cref="WeatherSourceSettings.Provider"/>. Selection is by name only —
        /// no adapter may claim "everything that isn't someone else", which would make
        /// registration order load-bearing. An unrecognised name falls back to the
        /// module's designated default adapter instead.
        /// </summary>
        string Name { get; }
        bool IsConfigured(WeatherSourceSettings source);
        string GetCacheKey(WeatherSourceSettings source);
        string DescribeTarget(WeatherSourceSettings source);
        IEnumerator FetchWeather(
            WeatherSourceSettings source,
            Action<ProviderFetchResult<WeatherInfo>> onComplete);
        IEnumerator FetchSunSchedule(
            WeatherSourceSettings source,
            Action<ProviderFetchResult<SunData>> onComplete);
    }

    internal interface IWeatherAcquisition
    {
        IEnumerator AcquireWeather(
            WeatherSourceSettings source,
            bool force,
            Action<WeatherFetchResult> onComplete);
        IEnumerator AcquireSunSchedule(
            WeatherSourceSettings source,
            Action<SunScheduleFetchResult> onComplete);
        void Invalidate();
    }

    internal enum WeatherFetchOrigin
    {
        None,
        Fresh,
        Cache,
        StaleFallback
    }

    internal sealed class WeatherFetchResult
    {
        internal WeatherFetchResult(
            WeatherInfo weather,
            WeatherFetchOrigin origin,
            float cacheRemainingSeconds,
            string error)
        {
            Weather = weather;
            Origin = origin;
            CacheRemainingSeconds = Math.Max(0f, cacheRemainingSeconds);
            Error = error;
        }

        internal WeatherInfo Weather { get; }
        internal WeatherFetchOrigin Origin { get; }
        internal float CacheRemainingSeconds { get; }
        internal string Error { get; }
        internal bool IsUsable => Weather != null;
        internal bool Succeeded => Origin == WeatherFetchOrigin.Fresh || Origin == WeatherFetchOrigin.Cache;
        internal bool Refreshed => Origin == WeatherFetchOrigin.Fresh;
    }

    internal sealed class SunScheduleFetchResult
    {
        internal SunScheduleFetchResult(SunData schedule, string error)
        {
            Schedule = schedule;
            Error = error;
        }

        internal SunData Schedule { get; }
        internal string Error { get; }
        internal bool Succeeded => Schedule != null && string.IsNullOrEmpty(Error);
    }

    /// <summary>
    /// Owns provider selection, cache semantics and fallback behaviour. Callers
    /// request weather once and receive a complete result instead of coordinating
    /// cache fields and success side channels themselves.
    /// </summary>
    internal sealed class WeatherAcquisitionModule : IWeatherAcquisition
    {
        private readonly List<IWeatherProviderAdapter> _adapters;
        private readonly IWeatherProviderAdapter _defaultAdapter;
        private readonly Func<DateTime> _now;
        private WeatherInfo _cachedWeather;
        private DateTime _lastFetchTime;
        private string _lastCacheKey;

        /// <param name="defaultProviderName">
        /// Adapter used when the configured provider name matches none of the
        /// registered adapters — an empty setting, or a value left behind by an older
        /// config. Must name one of <paramref name="adapters"/>.
        /// </param>
        internal WeatherAcquisitionModule(
            IEnumerable<IWeatherProviderAdapter> adapters,
            string defaultProviderName,
            Func<DateTime> now)
        {
            if (adapters == null) throw new ArgumentNullException(nameof(adapters));
            _adapters = new List<IWeatherProviderAdapter>(adapters);
            if (_adapters.Count == 0)
            {
                throw new ArgumentException("至少需要一个天气源 Adapter。", nameof(adapters));
            }

            _defaultAdapter = FindByName(defaultProviderName);
            if (_defaultAdapter == null)
            {
                throw new ArgumentException(
                    $"默认天气源 \"{defaultProviderName}\" 不在已注册的 Adapter 中。",
                    nameof(defaultProviderName));
            }

            _now = now ?? throw new ArgumentNullException(nameof(now));
        }

        internal WeatherInfo CurrentWeather => _cachedWeather;

        internal bool CanAcquire(WeatherSourceSettings source)
        {
            IWeatherProviderAdapter adapter = FindAdapter(source);
            return adapter != null && adapter.IsConfigured(source);
        }

        internal string DescribeTarget(WeatherSourceSettings source)
        {
            IWeatherProviderAdapter adapter = FindAdapter(source);
            return adapter == null ? "未知天气源" : adapter.DescribeTarget(source);
        }

        public IEnumerator AcquireWeather(
            WeatherSourceSettings source,
            bool force,
            Action<WeatherFetchResult> onComplete)
        {
            IWeatherProviderAdapter adapter = FindAdapter(source);
            if (adapter == null)
            {
                onComplete?.Invoke(new WeatherFetchResult(
                    null,
                    WeatherFetchOrigin.None,
                    0f,
                    "没有匹配的天气源 Adapter"));
                yield break;
            }

            string cacheKey = adapter.GetCacheKey(source) ?? string.Empty;
            if (!force && TryGetFreshCache(cacheKey, source.CacheExpiryMinutes, out var remainingSeconds))
            {
                onComplete?.Invoke(new WeatherFetchResult(
                    _cachedWeather,
                    WeatherFetchOrigin.Cache,
                    remainingSeconds,
                    null));
                yield break;
            }

            WeatherInfo fallback = HasCache(cacheKey) ? _cachedWeather : null;
            if (!adapter.IsConfigured(source))
            {
                onComplete?.Invoke(new WeatherFetchResult(
                    fallback,
                    fallback == null ? WeatherFetchOrigin.None : WeatherFetchOrigin.StaleFallback,
                    0f,
                    "当前天气源未配置"));
                yield break;
            }

            ProviderFetchResult<WeatherInfo> providerResult = null;
            yield return adapter.FetchWeather(source, result => providerResult = result);

            if (providerResult != null && providerResult.Succeeded)
            {
                _cachedWeather = providerResult.Value;
                _lastCacheKey = cacheKey;
                _lastFetchTime = _now();
                onComplete?.Invoke(new WeatherFetchResult(
                    _cachedWeather,
                    WeatherFetchOrigin.Fresh,
                    source.CacheExpiryMinutes * 60f,
                    null));
                yield break;
            }

            string error = providerResult?.Error ?? "天气源未完成回调";
            onComplete?.Invoke(new WeatherFetchResult(
                fallback,
                fallback == null ? WeatherFetchOrigin.None : WeatherFetchOrigin.StaleFallback,
                0f,
                error));
        }

        public IEnumerator AcquireSunSchedule(
            WeatherSourceSettings source,
            Action<SunScheduleFetchResult> onComplete)
        {
            IWeatherProviderAdapter adapter = FindAdapter(source);
            if (adapter == null)
            {
                onComplete?.Invoke(new SunScheduleFetchResult(null, "没有匹配的天气源 Adapter"));
                yield break;
            }

            if (!adapter.IsConfigured(source))
            {
                onComplete?.Invoke(new SunScheduleFetchResult(null, "当前天气源未配置"));
                yield break;
            }

            ProviderFetchResult<SunData> providerResult = null;
            yield return adapter.FetchSunSchedule(source, result => providerResult = result);
            if (providerResult != null && providerResult.Succeeded)
            {
                onComplete?.Invoke(new SunScheduleFetchResult(providerResult.Value, null));
                yield break;
            }

            onComplete?.Invoke(new SunScheduleFetchResult(
                null,
                providerResult?.Error ?? "天气源未完成日出日落回调"));
        }

        public void Invalidate()
        {
            _cachedWeather = null;
            _lastCacheKey = null;
            _lastFetchTime = DateTime.MinValue;
        }

        private IWeatherProviderAdapter FindAdapter(WeatherSourceSettings source)
        {
            if (source == null) return null;
            return FindByName(source.Provider) ?? _defaultAdapter;
        }

        private IWeatherProviderAdapter FindByName(string providerName)
        {
            if (string.IsNullOrEmpty(providerName)) return null;
            foreach (IWeatherProviderAdapter adapter in _adapters)
            {
                if (adapter != null &&
                    string.Equals(adapter.Name, providerName, StringComparison.OrdinalIgnoreCase))
                {
                    return adapter;
                }
            }

            return null;
        }

        private bool HasCache(string cacheKey)
        {
            return _cachedWeather != null &&
                string.Equals(_lastCacheKey, cacheKey, StringComparison.OrdinalIgnoreCase);
        }

        private bool TryGetFreshCache(
            string cacheKey,
            int expiryMinutes,
            out float remainingSeconds)
        {
            remainingSeconds = 0f;
            if (!HasCache(cacheKey))
            {
                return false;
            }

            TimeSpan remaining = TimeSpan.FromMinutes(Math.Max(1, expiryMinutes)) -
                (_now() - _lastFetchTime);
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            remainingSeconds = (float)remaining.TotalSeconds;
            return true;
        }
    }
}
