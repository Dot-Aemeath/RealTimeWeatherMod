using System;
using ChillWithYou.EnvSync.Core;
using ChillWithYou.EnvSync.Models;
using Xunit;

namespace RealTimeWeatherMod.Tests.Core
{
    public class EnvironmentDecisionTests
    {
        [Fact]
        public void Resolve_MapsEachTimeSegment()
        {
            SyncPolicySnapshot policy = Policy(time: true, weather: true);

            Assert.Equal(
                ModEnvironment.Day,
                EnvironmentDecision.Resolve(
                    policy,
                    Snapshot(DebugTimeSegment.Day, null)).Base);
            Assert.Equal(
                ModEnvironment.Sunset,
                EnvironmentDecision.Resolve(
                    policy,
                    Snapshot(DebugTimeSegment.Sunset, null)).Base);
            Assert.Equal(
                ModEnvironment.Night,
                EnvironmentDecision.Resolve(
                    policy,
                    Snapshot(DebugTimeSegment.Night, null)).Base);
        }

        [Fact]
        public void Resolve_BadWeatherDuringDay_OverridesBaseToCloudy()
        {
            var result = EnvironmentDecision.Resolve(
                Policy(time: true, weather: true),
                Snapshot(DebugTimeSegment.Day, Weather(11)));

            Assert.Equal(ModEnvironment.Cloudy, result.Base);
        }

        [Fact]
        public void Resolve_BadWeatherAtNight_KeepsNightBase()
        {
            var result = EnvironmentDecision.Resolve(
                Policy(time: true, weather: true),
                Snapshot(DebugTimeSegment.Night, Weather(11)));

            Assert.Equal(ModEnvironment.Night, result.Base);
        }

        [Fact]
        public void Resolve_WithoutCloudyOverridePermission_KeepsTimeBase()
        {
            // Weather sync off means the cloudy override is not ours to apply.
            var result = EnvironmentDecision.Resolve(
                Policy(time: true, weather: false),
                Snapshot(DebugTimeSegment.Day, Weather(11)));

            Assert.Equal(ModEnvironment.Day, result.Base);
        }

        [Fact]
        public void Resolve_NoWeatherData_KeepsTimeBase()
        {
            var result = EnvironmentDecision.Resolve(
                Policy(time: true, weather: true),
                Snapshot(DebugTimeSegment.Sunset, null));

            Assert.Equal(ModEnvironment.Sunset, result.Base);
        }

        [Fact]
        public void Resolve_MapsPrecipitationCodesToScenery()
        {
            AssertScenery(ModEnvironment.Snow, 20, 25);
            AssertScenery(ModEnvironment.ThunderRain, 11, 12, 16, 17, 18);
            AssertScenery(ModEnvironment.HeavyRain, 10, 14, 15);
            AssertScenery(ModEnvironment.LightRain, 13, 19);
        }

        private static void AssertScenery(ModEnvironment expected, params int[] codes)
        {
            foreach (int code in codes)
            {
                var decision = EnvironmentDecision.Resolve(
                    Policy(time: true, weather: true),
                    Snapshot(DebugTimeSegment.Day, Weather(code)));
                Assert.Equal(expected, decision.Scenery);
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(9)]
        [InlineData(38)]
        public void Resolve_ClearCodesHaveNoScenery(int code)
        {
            var decision = EnvironmentDecision.Resolve(
                Policy(time: true, weather: true),
                Snapshot(DebugTimeSegment.Day, Weather(code)));

            Assert.Null(decision.Scenery);
        }

        [Theory]
        [InlineData(10, false)]
        [InlineData(13, false)]
        [InlineData(21, false)]
        [InlineData(22, false)]
        [InlineData(4, true)]
        [InlineData(7, true)]
        [InlineData(31, true)]
        [InlineData(34, true)]
        [InlineData(36, true)]
        [InlineData(0, false)]
        [InlineData(32, false)]
        [InlineData(37, false)]
        public void Resolve_CloudyOverrideMatchesCodeTable(int code, bool expected)
        {
            var decision = EnvironmentDecision.Resolve(
                Policy(time: true, weather: true),
                Snapshot(DebugTimeSegment.Day, Weather(code)));

            Assert.Equal(
                expected ? ModEnvironment.Cloudy : ModEnvironment.Day,
                decision.Base);
        }

        [Fact]
        public void Resolve_WeatherSyncDisabled_ReportsNoScenery()
        {
            var decision = EnvironmentDecision.Resolve(
                Policy(time: true, weather: false),
                Snapshot(DebugTimeSegment.Day, Weather(13)));

            Assert.Null(decision.Scenery);
            Assert.Equal(ModEnvironment.Day, decision.Base);
        }

        [Fact]
        public void Resolve_ProducesBaseAndSceneryTogether()
        {
            var decision = EnvironmentDecision.Resolve(
                Policy(time: true, weather: true),
                Snapshot(DebugTimeSegment.Day, Weather(15)));

            Assert.Equal(ModEnvironment.Cloudy, decision.Base);
            Assert.Equal(ModEnvironment.HeavyRain, decision.Scenery);
        }

        /// <summary>
        /// The regression this module exists to prevent: the runner's applied state
        /// and the debug tool's expected state must come from the same call.
        /// </summary>
        [Fact]
        public void Resolve_UsesOneWeatherReadingForBaseAndScenery()
        {
            SyncPolicySnapshot policy = Policy(time: true, weather: true);
            EnvironmentInputSnapshot inputs = Snapshot(DebugTimeSegment.Sunset, Weather(11));

            var decision = EnvironmentDecision.Resolve(policy, inputs);

            Assert.Equal(ModEnvironment.Cloudy, decision.Base);
            Assert.Equal(ModEnvironment.ThunderRain, decision.Scenery);
        }

        private static SyncPolicySnapshot Policy(bool time, bool weather)
        {
            return SyncPolicy.Build(time, weather, false);
        }

        private static WeatherInfo Weather(int code)
        {
            return new WeatherInfo { Code = code, Text = "test", Temperature = 20 };
        }

        private static EnvironmentInputSnapshot Snapshot(
            DebugTimeSegment segment,
            WeatherInfo weather)
        {
            return new EnvironmentInputSnapshot
            {
                Now = new DateTime(2026, 6, 1, 12, 0, 0),
                Sunrise = new TimeSpan(6, 30, 0),
                Sunset = new TimeSpan(18, 30, 0),
                TimeSegment = segment,
                Weather = weather,
                ConfigurationValid = true
            };
        }
    }
}
