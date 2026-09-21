using System;
using System.Collections;
using System.Collections.Generic;
using ChillWithYou.EnvSync.Core;
using ChillWithYou.EnvSync.Models;
using Xunit;

namespace RealTimeWeatherMod.Tests.Core
{
    public class WeatherSyncModuleTests
    {
        [Fact]
        public void Synchronize_TimeOnly_DoesNotAcquireWeather()
        {
            var acquisition = new FakeAcquisition();
            var module = new WeatherSyncModule(acquisition, new FakeEffects());
            WeatherSyncResult result = null;

            Run(module.Synchronize(
                Request(Policy(time: true, weather: false, ui: false)),
                value => result = value));

            Assert.Equal(0, acquisition.FetchCount);
            Assert.True(result.ApplyEnvironment);
            Assert.False(result.UpdateWeatherUi);
            Assert.Null(result.Weather);
            Assert.True(result.RefreshSunSchedule);
        }

        [Fact]
        public void Synchronize_FreshWeather_ReturnsCompleteEffectsAndRefreshDelay()
        {
            var acquisition = new FakeAcquisition
            {
                NextResult = new WeatherFetchResult(
                    Weather(11),
                    WeatherFetchOrigin.Fresh,
                    3600f,
                    null)
            };
            var effects = new FakeEffects();
            var module = new WeatherSyncModule(acquisition, effects);
            WeatherSyncResult result = null;

            Run(module.Synchronize(
                Request(
                    Policy(time: true, weather: true, ui: true),
                    forceNetwork: true,
                    reschedule: true,
                    refreshSeconds: 1800f,
                    inputWeather: Weather(1)),
                value => result = value));

            Assert.Equal(1, acquisition.FetchCount);
            Assert.True(acquisition.Forces[0]);
            Assert.True(result.ApplyEnvironment);
            Assert.True(result.UpdateWeatherUi);
            Assert.True(result.RefreshSunSchedule);
            Assert.Equal(11, result.Weather.Code);
            Assert.Equal(11, result.Inputs.Weather.Code);
            Assert.Equal(1800f, result.NextWeatherCheckSeconds);
            Assert.Equal(1, effects.UiUpdates);
            Assert.Equal(1, effects.EnvironmentApplies);
            Assert.Equal(11, effects.AppliedInputs.Weather.Code);
            Assert.Equal(1, effects.SunRefreshes);
            Assert.Equal(1800f, effects.ScheduledDelay);
        }

        [Fact]
        public void Synchronize_CacheHit_DoesNotSchedulePastCacheExpiry()
        {
            var acquisition = new FakeAcquisition
            {
                NextResult = new WeatherFetchResult(
                    Weather(1),
                    WeatherFetchOrigin.Cache,
                    100f,
                    null)
            };
            var module = new WeatherSyncModule(acquisition, new FakeEffects());
            WeatherSyncResult result = null;

            Run(module.Synchronize(
                Request(
                    Policy(time: false, weather: true, ui: false),
                    reschedule: true,
                    refreshSeconds: 1800f),
                value => result = value));

            Assert.Equal(101f, result.NextWeatherCheckSeconds);
            Assert.True(result.RefreshSunSchedule);
        }

        [Fact]
        public void Synchronize_DebugScenario_BypassesProviderAndCarriesValidationWarning()
        {
            var acquisition = new FakeAcquisition();
            var module = new WeatherSyncModule(acquisition, new FakeEffects());
            WeatherSyncResult result = null;

            Run(module.Synchronize(
                Request(
                    Policy(time: true, weather: true, ui: true),
                    isDebug: true,
                    debugValid: false,
                    debugError: "invalid",
                    inputWeather: Weather(22)),
                value => result = value));

            Assert.Equal(0, acquisition.FetchCount);
            Assert.Equal(22, result.Weather.Code);
            Assert.Equal("invalid", result.Warning);
            Assert.False(result.RefreshSunSchedule);
        }

        [Fact]
        public void Synchronize_StartupMode_AppliesSharedEffectsButLeavesEnvironmentForSettling()
        {
            var acquisition = new FakeAcquisition
            {
                NextResult = new WeatherFetchResult(
                    Weather(11),
                    WeatherFetchOrigin.Fresh,
                    3600f,
                    null)
            };
            var effects = new FakeEffects();
            var module = new WeatherSyncModule(acquisition, effects);

            Run(module.Synchronize(
                Request(
                    Policy(time: true, weather: true, ui: true),
                    reschedule: true,
                    mode: WeatherSyncMode.Startup),
                _ => { }));

            Assert.Equal(1, effects.UiUpdates);
            Assert.Equal(0, effects.EnvironmentApplies);
            Assert.Equal(1, effects.SunRefreshes);
            Assert.NotNull(effects.ScheduledDelay);
        }

        [Fact]
        public void Synchronize_AcquisitionFailure_DoesNotLeakUnscopedWeather()
        {
            var acquisition = new FakeAcquisition
            {
                NextResult = new WeatherFetchResult(
                    null,
                    WeatherFetchOrigin.None,
                    0f,
                    "当前天气源未配置")
            };
            var module = new WeatherSyncModule(acquisition, new FakeEffects());
            WeatherSyncResult result = null;

            Run(module.Synchronize(
                Request(Policy(time: true, weather: true, ui: true)),
                value => result = value));

            Assert.Equal(1, acquisition.FetchCount);
            Assert.Null(result.Weather);
            Assert.Null(result.Inputs.Weather);
            Assert.False(result.RefreshSunSchedule);
            Assert.Contains("未配置", result.Warning);
        }

        [Fact]
        public void Synchronize_ConcurrentForceRequest_IsCoalescedAfterCurrentRun()
        {
            var acquisition = new FakeAcquisition
            {
                NextResult = new WeatherFetchResult(
                    Weather(1),
                    WeatherFetchOrigin.Fresh,
                    60f,
                    null)
            };
            var module = new WeatherSyncModule(acquisition, new FakeEffects());
            int firstCallbacks = 0;
            int secondCallbacks = 0;
            IEnumerator first = module.Synchronize(
                Request(Policy(time: true, weather: true, ui: false)),
                _ => firstCallbacks++);

            Assert.True(first.MoveNext());
            var firstRun = Assert.IsAssignableFrom<IEnumerator>(first.Current);

            Run(module.Synchronize(
                Request(
                    Policy(time: true, weather: true, ui: false),
                    forceNetwork: true),
                _ => secondCallbacks++));
            Run(firstRun);
            Run(first);

            Assert.Equal(2, acquisition.FetchCount);
            Assert.Equal(new[] { false, true }, acquisition.Forces);
            Assert.Equal(1, firstCallbacks);
            Assert.Equal(1, secondCallbacks);
        }

        [Fact]
        public void Synchronize_TimeTickDuringAcquisition_DoesNotOverwriteFreshWeather()
        {
            var acquisition = new FakeAcquisition
            {
                NextResult = new WeatherFetchResult(Weather(11), WeatherFetchOrigin.Fresh, 60f, null)
            };
            var effects = new FakeEffects();
            var module = new WeatherSyncModule(acquisition, effects);
            IEnumerator first = module.Synchronize(Request(Policy(true, true, true)), null);
            Assert.True(first.MoveNext());
            var firstRun = Assert.IsAssignableFrom<IEnumerator>(first.Current);

            Run(module.Synchronize(
                Request(Policy(true, true, true), inputWeather: Weather(1), acquireWeather: false), null));
            Run(firstRun);
            Run(first);

            Assert.Equal(1, acquisition.FetchCount);
            Assert.Equal(1, effects.EnvironmentApplies);
            Assert.Equal(1, effects.UiUpdates);
            Assert.Equal(11, effects.AppliedInputs.Weather.Code);
        }

        [Fact]
        public void Synchronize_TimeTick_UsesSnapshotWithoutAcquisition()
        {
            var acquisition = new FakeAcquisition();
            var module = new WeatherSyncModule(acquisition, new FakeEffects());
            WeatherSyncResult result = null;
            Run(module.Synchronize(Request(Policy(true, true, true), inputWeather: Weather(11), acquireWeather: false), value => result = value));
            Assert.Equal(0, acquisition.FetchCount);
            Assert.Equal(11, result.Weather.Code);
            Assert.True(result.ApplyEnvironment);
            Assert.True(result.RefreshSunSchedule);
        }

        private static WeatherSyncRequest Request(
            SyncPolicySnapshot policy,
            bool forceNetwork = false,
            bool reschedule = false,
            float refreshSeconds = 1800f,
            bool isDebug = false,
            bool debugValid = true,
            string debugError = null,
            WeatherInfo inputWeather = null,
            WeatherSyncMode mode = WeatherSyncMode.Regular,
            bool acquireWeather = true)
        {
            return new WeatherSyncRequest(
                policy,
                new WeatherSourceSettings("test", "key", "city", 1d, 2d, 60),
                new EnvironmentInputSnapshot
                {
                    IsDebug = isDebug,
                    ConfigurationValid = debugValid,
                    ConfigurationError = debugError,
                    Now = new DateTime(2026, 6, 1, 12, 0, 0),
                    Sunrise = new TimeSpan(6, 30, 0),
                    Sunset = new TimeSpan(18, 30, 0),
                    Weather = inputWeather,
                    TimeSegment = DebugTimeSegment.Day
                },
                forceNetwork,
                false,
                reschedule,
                refreshSeconds,
                mode, acquireWeather);
        }

        private static SyncPolicySnapshot Policy(bool time, bool weather, bool ui)
        {
            return SyncPolicy.Build(time, weather, ui);
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
                if (nested != null) stack.Push(nested);
            }
        }

        private sealed class FakeEffects : IWeatherSyncEffects
        {
            internal int UiUpdates { get; private set; }
            internal int EnvironmentApplies { get; private set; }
            internal int SunRefreshes { get; private set; }
            internal float? ScheduledDelay { get; private set; }
            internal EnvironmentInputSnapshot AppliedInputs { get; private set; }

            public void UpdateWeatherUi(WeatherInfo weather)
            {
                UiUpdates++;
            }

            public void ApplyEnvironment(
                SyncPolicySnapshot policy,
                EnvironmentInputSnapshot inputs,
                bool force)
            {
                EnvironmentApplies++;
                AppliedInputs = inputs;
            }

            public void RefreshSunSchedule()
            {
                SunRefreshes++;
            }

            public void ScheduleWeatherCheck(float delaySeconds)
            {
                ScheduledDelay = delaySeconds;
            }

            public void ReportWarning(string warning, bool isDebug)
            {
            }
        }

        private sealed class FakeAcquisition : IWeatherAcquisition
        {
            internal int FetchCount { get; private set; }
            internal List<bool> Forces { get; } = new List<bool>();
            internal WeatherFetchResult NextResult { get; set; }

            public IEnumerator AcquireWeather(
                WeatherSourceSettings source,
                bool force,
                Action<WeatherFetchResult> onComplete)
            {
                FetchCount++;
                Forces.Add(force);
                yield return null;
                onComplete(NextResult);
            }

            public IEnumerator AcquireSunSchedule(
                WeatherSourceSettings source,
                Action<SunScheduleFetchResult> onComplete)
            {
                yield break;
            }

            public void Invalidate()
            {
            }
        }
    }
}
