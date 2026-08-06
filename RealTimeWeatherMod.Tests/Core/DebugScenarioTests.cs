using ChillWithYou.EnvSync.Core;
using ChillWithYou.EnvSync.Models;
using Xunit;

namespace RealTimeWeatherMod.Tests.Core
{
    public class DebugScenarioTests
    {
        [Fact]
        public void TryCreate_TimeOnly_PreservesSystemDateAndResolvesWeather()
        {
            var systemNow = new DateTime(2026, 8, 3, 22, 15, 0, DateTimeKind.Local);
            var input = CreateInput(currentDateTime: "07:12:34", weatherCode: 11);

            bool success = DebugScenario.TryCreate(input, systemNow, out var state, out var error);

            Assert.True(success, error);
            Assert.Equal(new DateTime(2026, 8, 3, 7, 12, 34, DateTimeKind.Local), state.Now);
            Assert.Equal(11, state.Weather.Code);
            Assert.Equal(WeatherCondition.Rainy, state.Weather.Condition);
            Assert.Equal(DebugTimeSegment.Day, state.TimeSegment);
        }

        [Fact]
        public void TryCreate_FullDateTime_OverridesDateForSeasonalTesting()
        {
            var systemNow = new DateTime(2026, 8, 3, 22, 15, 0, DateTimeKind.Local);
            var input = CreateInput(currentDateTime: "2026-12-24 23:45");

            bool success = DebugScenario.TryCreate(input, systemNow, out var state, out var error);

            Assert.True(success, error);
            Assert.Equal(2026, state.Now.Year);
            Assert.Equal(12, state.Now.Month);
            Assert.Equal(24, state.Now.Day);
            Assert.Equal(23, state.Now.Hour);
            Assert.Equal(DebugTimeSegment.Night, state.TimeSegment);
        }

        [Theory]
        [InlineData("05:59", 2)]
        [InlineData("06:00", 0)]
        [InlineData("17:29", 0)]
        [InlineData("17:30", 1)]
        [InlineData("18:29", 1)]
        [InlineData("18:30", 2)]
        public void TryCreate_UsesSameThirtyMinuteSunsetWindowAsRuntime(
            string currentDateTime,
            int expected)
        {
            var input = CreateInput(
                currentDateTime: currentDateTime,
                sunrise: "06:00",
                sunset: "18:00");

            bool success = DebugScenario.TryCreate(
                input,
                new DateTime(2026, 8, 3, 12, 0, 0),
                out var state,
                out var error);

            Assert.True(success, error);
            Assert.Equal((DebugTimeSegment)expected, state.TimeSegment);
        }

        [Fact]
        public void TryCreate_InvalidDateTime_RejectsWholeScenario()
        {
            var input = CreateInput(currentDateTime: "not-a-clock");

            bool success = DebugScenario.TryCreate(
                input,
                DateTime.Now,
                out var state,
                out var error);

            Assert.False(success);
            Assert.Null(state);
            Assert.Contains("SimulatedDateTime", error);
        }

        [Fact]
        public void TryCreate_SunsetBeforeSunrise_RejectsWholeScenario()
        {
            var input = CreateInput(sunrise: "18:00", sunset: "06:00");

            bool success = DebugScenario.TryCreate(
                input,
                DateTime.Now,
                out var state,
                out var error);

            Assert.False(success);
            Assert.Null(state);
            Assert.Contains("晚于", error);
        }

        [Fact]
        public void TryCreate_BlankWeatherText_UsesStableFallback()
        {
            var input = CreateInput(weatherText: "   ");

            bool success = DebugScenario.TryCreate(
                input,
                DateTime.Now,
                out var state,
                out var error);

            Assert.True(success, error);
            Assert.Equal("DebugWeather", state.Weather.Text);
        }

        private static DebugScenarioInput CreateInput(
            string currentDateTime = "12:00",
            string sunrise = "06:30",
            string sunset = "18:30",
            int weatherCode = 1,
            string weatherText = "DebugWeather")
        {
            return new DebugScenarioInput
            {
                WeatherCode = weatherCode,
                Temperature = 25,
                WeatherText = weatherText,
                CurrentDateTime = currentDateTime,
                Sunrise = sunrise,
                Sunset = sunset
            };
        }
    }
}
