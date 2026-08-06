using System;
using System.Collections;
using System.Collections.Generic;
using ChillWithYou.EnvSync.Core;
using ChillWithYou.EnvSync.Models;
using Xunit;

namespace RealTimeWeatherMod.Tests.Core
{
    public class WeatherAcquisitionModuleTests
    {
        [Fact]
        public void AcquireWeather_FreshThenCache_HidesCacheProtocolFromCaller()
        {
            DateTime now = new DateTime(2026, 8, 4, 10, 0, 0);
            var adapter = new FakeAdapter("A")
            {
                NextWeather = ProviderFetchResult<WeatherInfo>.Success(Weather(1))
            };
            var module = Module(adapter, () => now);
            WeatherFetchResult first = null;
            WeatherFetchResult second = null;

            Run(module.AcquireWeather(Source("A"), false, result => first = result));
            now = now.AddMinutes(5);
            Run(module.AcquireWeather(Source("A"), false, result => second = result));

            Assert.Equal(WeatherFetchOrigin.Fresh, first.Origin);
            Assert.True(first.Succeeded);
            Assert.Equal(WeatherFetchOrigin.Cache, second.Origin);
            Assert.True(second.Succeeded);
            Assert.InRange(second.CacheRemainingSeconds, 299f, 301f);
            Assert.Equal(1, adapter.WeatherFetchCount);
        }

        [Fact]
        public void AcquireWeather_Force_BypassesFreshCache()
        {
            var adapter = new FakeAdapter("A")
            {
                NextWeather = ProviderFetchResult<WeatherInfo>.Success(Weather(1))
            };
            var module = Module(adapter);

            Run(module.AcquireWeather(Source("A"), false, _ => { }));
            adapter.NextWeather = ProviderFetchResult<WeatherInfo>.Success(Weather(2));
            WeatherFetchResult result = null;
            Run(module.AcquireWeather(Source("A"), true, value => result = value));

            Assert.Equal(2, adapter.WeatherFetchCount);
            Assert.Equal(2, result.Weather.Code);
            Assert.True(result.Refreshed);
        }

        [Fact]
        public void AcquireWeather_FailedRefresh_ReturnsStaleFallbackWithoutClaimingSuccess()
        {
            DateTime now = new DateTime(2026, 8, 4, 10, 0, 0);
            var adapter = new FakeAdapter("A")
            {
                NextWeather = ProviderFetchResult<WeatherInfo>.Success(Weather(1))
            };
            var module = Module(adapter, () => now);
            Run(module.AcquireWeather(Source("A"), false, _ => { }));
            now = now.AddMinutes(11);
            adapter.NextWeather = ProviderFetchResult<WeatherInfo>.Failure("network");
            WeatherFetchResult result = null;

            Run(module.AcquireWeather(Source("A"), false, value => result = value));

            Assert.Equal(WeatherFetchOrigin.StaleFallback, result.Origin);
            Assert.False(result.Succeeded);
            Assert.True(result.IsUsable);
            Assert.Equal(1, result.Weather.Code);
            Assert.Equal("network", result.Error);
        }

        [Fact]
        public void AcquireWeather_DifferentTarget_DoesNotLeakOldFallback()
        {
            var adapter = new FakeAdapter("A")
            {
                NextWeather = ProviderFetchResult<WeatherInfo>.Success(Weather(1))
            };
            var module = Module(adapter);
            Run(module.AcquireWeather(Source("A", "first"), false, _ => { }));
            adapter.NextWeather = ProviderFetchResult<WeatherInfo>.Failure("network");
            WeatherFetchResult result = null;

            Run(module.AcquireWeather(Source("A", "second"), false, value => result = value));

            Assert.Equal(WeatherFetchOrigin.None, result.Origin);
            Assert.False(result.IsUsable);
        }

        [Fact]
        public void AcquireWeather_SelectsMatchingAdapter()
        {
            var first = new FakeAdapter("A")
            {
                NextWeather = ProviderFetchResult<WeatherInfo>.Success(Weather(1))
            };
            var second = new FakeAdapter("B")
            {
                NextWeather = ProviderFetchResult<WeatherInfo>.Success(Weather(2))
            };
            var module = Module(first, second);
            WeatherFetchResult result = null;

            Run(module.AcquireWeather(Source("B"), false, value => result = value));

            Assert.Equal(0, first.WeatherFetchCount);
            Assert.Equal(1, second.WeatherFetchCount);
            Assert.Equal(2, result.Weather.Code);
        }

        [Fact]
        public void Invalidate_RemovesCurrentWeatherAndCache()
        {
            var adapter = new FakeAdapter("A")
            {
                NextWeather = ProviderFetchResult<WeatherInfo>.Success(Weather(1))
            };
            var module = Module(adapter);
            Run(module.AcquireWeather(Source("A"), false, _ => { }));

            module.Invalidate();
            adapter.NextWeather = ProviderFetchResult<WeatherInfo>.Failure("network");
            WeatherFetchResult result = null;
            Run(module.AcquireWeather(Source("A"), false, value => result = value));

            Assert.Null(module.CurrentWeather);
            Assert.False(result.IsUsable);
        }

        [Fact]
        public void AcquireSunSchedule_RoutesThroughSelectedAdapter()
        {
            var adapter = new FakeAdapter("A")
            {
                NextSunSchedule = ProviderFetchResult<SunData>.Success(
                    new SunData { sunrise = "06:00", sunset = "18:00" })
            };
            var module = Module(adapter);
            SunScheduleFetchResult result = null;

            Run(module.AcquireSunSchedule(Source("A"), value => result = value));

            Assert.True(result.Succeeded);
            Assert.Equal("06:00", result.Schedule.sunrise);
            Assert.Equal(1, adapter.SunFetchCount);
        }

        [Fact]
        public void UnconfiguredAdapter_FailsWithoutStartingTransport()
        {
            var adapter = new FakeAdapter("A") { Configured = false };
            var module = Module(adapter);
            WeatherFetchResult result = null;

            Run(module.AcquireWeather(Source("A"), false, value => result = value));

            Assert.False(result.Succeeded);
            Assert.Contains("未配置", result.Error);
            Assert.Equal(0, adapter.WeatherFetchCount);
        }

        [Fact]
        public void UnconfiguredDifferentTarget_DoesNotLeakOldCache()
        {
            var adapter = new FakeAdapter("A")
            {
                NextWeather = ProviderFetchResult<WeatherInfo>.Success(Weather(1))
            };
            var module = Module(adapter);
            Run(module.AcquireWeather(Source("A", "first"), false, _ => { }));
            adapter.Configured = false;
            WeatherFetchResult result = null;

            Run(module.AcquireWeather(
                Source("A", "second"),
                false,
                value => result = value));

            Assert.Equal(WeatherFetchOrigin.None, result.Origin);
            Assert.Null(result.Weather);
            Assert.Contains("未配置", result.Error);
            Assert.Equal(1, adapter.WeatherFetchCount);
        }

        [Fact]
        public void AcquireWeather_UnknownProviderName_FallsBackToTheDesignatedDefault()
        {
            var openMeteo = new FakeAdapter("OpenMeteo");
            var seniverse = new FakeAdapter("Seniverse")
            {
                NextWeather = ProviderFetchResult<WeatherInfo>.Success(Weather(7))
            };
            var module = new WeatherAcquisitionModule(
                new IWeatherProviderAdapter[] { openMeteo, seniverse },
                "Seniverse",
                () => new DateTime(2026, 8, 4, 10, 0, 0));
            WeatherFetchResult result = null;

            Run(module.AcquireWeather(Source("在旧配置里留下的值"), false, value => result = value));

            Assert.Equal(0, openMeteo.WeatherFetchCount);
            Assert.Equal(1, seniverse.WeatherFetchCount);
            Assert.Equal(7, result.Weather.Code);
        }

        /// <summary>
        /// Registration order must not decide which adapter serves a request — that was
        /// the implicit contract when one adapter matched "anything but the other".
        /// </summary>
        [Fact]
        public void AcquireWeather_SelectionIsByNameNotRegistrationOrder()
        {
            var first = new FakeAdapter("OpenMeteo")
            {
                NextWeather = ProviderFetchResult<WeatherInfo>.Success(Weather(1))
            };
            var second = new FakeAdapter("Seniverse")
            {
                NextWeather = ProviderFetchResult<WeatherInfo>.Success(Weather(2))
            };

            foreach (var adapters in new[]
            {
                new IWeatherProviderAdapter[] { first, second },
                new IWeatherProviderAdapter[] { second, first }
            })
            {
                var module = new WeatherAcquisitionModule(
                    adapters,
                    "Seniverse",
                    () => new DateTime(2026, 8, 4, 10, 0, 0));
                WeatherFetchResult result = null;
                Run(module.AcquireWeather(Source("openmeteo"), true, value => result = value));

                Assert.Equal(1, result.Weather.Code);
            }
        }

        [Fact]
        public void Constructor_RejectsADefaultThatIsNotRegistered()
        {
            Assert.Throws<ArgumentException>(() => new WeatherAcquisitionModule(
                new IWeatherProviderAdapter[] { new FakeAdapter("OpenMeteo") },
                "Seniverse",
                () => DateTime.Now));
        }

        private static WeatherAcquisitionModule Module(params IWeatherProviderAdapter[] adapters)
        {
            return Module(adapters, () => new DateTime(2026, 8, 4, 10, 0, 0));
        }

        private static WeatherAcquisitionModule Module(
            IWeatherProviderAdapter adapter,
            Func<DateTime> now)
        {
            return Module(new[] { adapter }, now);
        }

        private static WeatherAcquisitionModule Module(
            IEnumerable<IWeatherProviderAdapter> adapters,
            Func<DateTime> now)
        {
            var list = new List<IWeatherProviderAdapter>(adapters);
            return new WeatherAcquisitionModule(list, list[0].Name, now);
        }

        private static WeatherSourceSettings Source(string provider, string location = "test")
        {
            return new WeatherSourceSettings(provider, "key", location, 1d, 2d, 10);
        }

        private static WeatherInfo Weather(int code)
        {
            return new WeatherInfo { Code = code, Text = "test", Temperature = 20 };
        }

        private static void Run(IEnumerator routine)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            while (stack.Count > 0)
            {
                IEnumerator current = stack.Peek();
                if (!current.MoveNext())
                {
                    stack.Pop();
                    continue;
                }

                var nested = current.Current as IEnumerator;
                if (nested != null)
                {
                    stack.Push(nested);
                }
            }
        }

        private sealed class FakeAdapter : IWeatherProviderAdapter
        {
            private readonly string _provider;

            internal FakeAdapter(string provider)
            {
                _provider = provider;
            }

            internal bool Configured { get; set; } = true;
            internal int WeatherFetchCount { get; private set; }
            internal int SunFetchCount { get; private set; }
            internal ProviderFetchResult<WeatherInfo> NextWeather { get; set; }
            internal ProviderFetchResult<SunData> NextSunSchedule { get; set; }

            public string Name => _provider;

            public bool IsConfigured(WeatherSourceSettings source)
            {
                return Configured;
            }

            public string GetCacheKey(WeatherSourceSettings source)
            {
                return _provider + ":" + source.Location;
            }

            public string DescribeTarget(WeatherSourceSettings source)
            {
                return GetCacheKey(source);
            }

            public IEnumerator FetchWeather(
                WeatherSourceSettings source,
                Action<ProviderFetchResult<WeatherInfo>> onComplete)
            {
                WeatherFetchCount++;
                onComplete(NextWeather ?? ProviderFetchResult<WeatherInfo>.Failure("missing"));
                yield break;
            }

            public IEnumerator FetchSunSchedule(
                WeatherSourceSettings source,
                Action<ProviderFetchResult<SunData>> onComplete)
            {
                SunFetchCount++;
                onComplete(NextSunSchedule ?? ProviderFetchResult<SunData>.Failure("missing"));
                yield break;
            }
        }
    }
}
