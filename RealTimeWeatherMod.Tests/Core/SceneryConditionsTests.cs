using System;
using ChillWithYou.EnvSync.Core;
using ChillWithYou.EnvSync.Models;
using Xunit;

namespace RealTimeWeatherMod.Tests.Core
{
    public class SceneryConditionsTests
    {
        [Theory]
        [InlineData(6, true)]
        [InlineData(17, true)]
        [InlineData(18, false)]
        [InlineData(5, false)]
        public void IsDay_CoversDaylightHours(int hour, bool expected)
        {
            Assert.Equal(expected, SceneryConditions.IsDay(At(hour: hour)));
        }

        [Theory]
        [InlineData(19, true)]
        [InlineData(23, true)]
        [InlineData(4, true)]
        [InlineData(5, false)]
        [InlineData(18, false)]
        public void IsNight_CoversNightHoursAcrossMidnight(int hour, bool expected)
        {
            Assert.Equal(expected, SceneryConditions.IsNight(At(hour: hour)));
        }

        /// <summary>
        /// 18:00-19:00 is neither day nor night: the sunset window belongs to neither
        /// rule set, and that gap is deliberate.
        /// </summary>
        [Fact]
        public void DuskHour_IsNeitherDayNorNight()
        {
            EnvironmentInputSnapshot dusk = At(hour: 18);
            Assert.False(SceneryConditions.IsDay(dusk));
            Assert.False(SceneryConditions.IsNight(dusk));
        }

        [Fact]
        public void IsGoodWeather_MissingWeatherCountsAsGood()
        {
            Assert.True(SceneryConditions.IsGoodWeather(At(weather: null)));
        }

        [Theory]
        [InlineData(0, true)]
        [InlineData(9, true)]
        [InlineData(10, false)]
        [InlineData(22, false)]
        public void IsGoodWeather_UsesClearCodeRange(int code, bool expected)
        {
            Assert.Equal(expected, SceneryConditions.IsGoodWeather(At(weather: Weather(code))));
        }

        [Theory]
        [InlineData(20, true)]
        [InlineData(25, true)]
        [InlineData(19, false)]
        [InlineData(26, false)]
        public void IsSnowyWeather_UsesSnowCodeRange(int code, bool expected)
        {
            Assert.Equal(expected, SceneryConditions.IsSnowyWeather(At(weather: Weather(code))));
        }

        [Fact]
        public void GetSeason_MapsMonths()
        {
            Assert.Equal(Season.Spring, SceneryConditions.GetSeason(At(month: 3)));
            Assert.Equal(Season.Spring, SceneryConditions.GetSeason(At(month: 5)));
            Assert.Equal(Season.Summer, SceneryConditions.GetSeason(At(month: 7)));
            Assert.Equal(Season.Autumn, SceneryConditions.GetSeason(At(month: 10)));
            Assert.Equal(Season.Winter, SceneryConditions.GetSeason(At(month: 12)));
            Assert.Equal(Season.Winter, SceneryConditions.GetSeason(At(month: 1)));
        }

        [Theory]
        [InlineData(11, 30, true)]
        [InlineData(12, 0, true)]
        [InlineData(12, 31, false)]
        [InlineData(17, 30, true)]
        [InlineData(18, 30, true)]
        [InlineData(19, 0, false)]
        [InlineData(9, 0, false)]
        public void IsCookingWindow_CoversMealTimes(int hour, int minute, bool expected)
        {
            Assert.Equal(
                expected,
                SceneryConditions.IsCookingWindow(At(hour: hour, minute: minute)));
        }

        [Theory]
        [InlineData(31, true)]
        [InlineData(4, true)]
        [InlineData(20, false)]
        public void IsAirConditionerWindow_TriggersOnTemperatureExtremes(
            int temperature,
            bool expected)
        {
            var snapshot = At(weather: new WeatherInfo
            {
                Code = 1,
                Text = "test",
                Temperature = temperature
            });

            Assert.Equal(expected, SceneryConditions.IsAirConditionerWindow(snapshot));
        }

        [Fact]
        public void IsAirConditionerWindow_WithoutWeather_StaysOff()
        {
            Assert.False(SceneryConditions.IsAirConditionerWindow(At(weather: null)));
        }

        [Fact]
        public void IsSakuraWindow_RequiresSpringDaylightAndClearSky()
        {
            Assert.True(SceneryConditions.IsSakuraWindow(At(month: 4, hour: 12)));
            Assert.False(SceneryConditions.IsSakuraWindow(At(month: 5, hour: 12)));
            Assert.False(SceneryConditions.IsSakuraWindow(At(month: 4, hour: 22)));
            Assert.False(
                SceneryConditions.IsSakuraWindow(At(month: 4, hour: 12, weather: Weather(13))));
        }

        [Fact]
        public void IsLocomotiveWindow_OnlyOnChristmasNights()
        {
            Assert.True(SceneryConditions.IsLocomotiveWindow(At(month: 12, day: 24, hour: 22)));
            Assert.True(SceneryConditions.IsLocomotiveWindow(At(month: 12, day: 25, hour: 22)));
            Assert.False(SceneryConditions.IsLocomotiveWindow(At(month: 12, day: 26, hour: 22)));
            Assert.False(SceneryConditions.IsLocomotiveWindow(At(month: 12, day: 24, hour: 12)));
        }

        [Fact]
        public void IsBalloonWindow_OnlyOnChildrensDayDaylight()
        {
            Assert.True(SceneryConditions.IsBalloonWindow(At(month: 6, day: 1, hour: 12)));
            Assert.False(SceneryConditions.IsBalloonWindow(At(month: 6, day: 2, hour: 12)));
            Assert.False(SceneryConditions.IsBalloonWindow(At(month: 6, day: 1, hour: 22)));
        }

        [Fact]
        public void IsBooksWindow_IgnoresTimeAndWeather()
        {
            Assert.True(
                SceneryConditions.IsBooksWindow(At(month: 4, day: 23, hour: 3, weather: Weather(22))));
            Assert.True(SceneryConditions.IsBooksWindow(At(month: 9, day: 1, hour: 12)));
            Assert.False(SceneryConditions.IsBooksWindow(At(month: 9, day: 2, hour: 12)));
        }

        [Fact]
        public void IsWindBellWindow_RequiresMidsummerAndClearSky()
        {
            Assert.True(SceneryConditions.IsWindBellWindow(At(month: 7, hour: 3)));
            Assert.True(SceneryConditions.IsWindBellWindow(At(month: 8, hour: 15)));
            Assert.False(SceneryConditions.IsWindBellWindow(At(month: 9, hour: 15)));
            Assert.False(
                SceneryConditions.IsWindBellWindow(At(month: 7, hour: 15, weather: Weather(13))));
        }

        [Theory]
        [InlineData(11, true)]
        [InlineData(12, true)]
        [InlineData(1, true)]
        [InlineData(2, true)]
        [InlineData(3, false)]
        [InlineData(10, false)]
        public void IsHotSpringWindow_CoversWinterMonths(int month, bool expected)
        {
            Assert.Equal(expected, SceneryConditions.IsHotSpringWindow(At(month: month)));
        }

        /// <summary>
        /// Snow keeps the hot spring eligible even though snow codes are not
        /// "good weather" — that exception is the whole point of the rule.
        /// </summary>
        [Fact]
        public void IsHotSpringWindow_SnowIsEligibleAndRaisesProbability()
        {
            EnvironmentInputSnapshot snowy = At(month: 12, weather: Weather(22));
            EnvironmentInputSnapshot clear = At(month: 12, weather: Weather(1));
            EnvironmentInputSnapshot rainy = At(month: 12, weather: Weather(13));

            Assert.True(SceneryConditions.IsHotSpringWindow(snowy));
            Assert.False(SceneryConditions.IsHotSpringWindow(rainy));
            Assert.Equal(0.30, SceneryConditions.HotSpringProbability(snowy));
            Assert.Equal(0.05, SceneryConditions.HotSpringProbability(clear));
        }

        [Fact]
        public void IsBlueButterflyWindow_RequiresLateSpringNights()
        {
            Assert.True(SceneryConditions.IsBlueButterflyWindow(At(month: 5, hour: 22)));
            Assert.True(SceneryConditions.IsBlueButterflyWindow(At(month: 6, hour: 2)));
            Assert.False(SceneryConditions.IsBlueButterflyWindow(At(month: 7, hour: 22)));
            Assert.False(SceneryConditions.IsBlueButterflyWindow(At(month: 5, hour: 12)));
        }

        [Fact]
        public void BlueButterflySegmentSeed_IsStableWithinASegmentAndDiffersAcross()
        {
            var baseTime = new DateTime(2026, 5, 20, 21, 5, 0);

            Assert.Equal(
                SceneryConditions.BlueButterflySegmentSeed(baseTime),
                SceneryConditions.BlueButterflySegmentSeed(baseTime.AddMinutes(10)));
            Assert.NotEqual(
                SceneryConditions.BlueButterflySegmentSeed(baseTime),
                SceneryConditions.BlueButterflySegmentSeed(baseTime.AddMinutes(20)));
            Assert.NotEqual(
                SceneryConditions.BlueButterflySegmentSeed(baseTime),
                SceneryConditions.BlueButterflySegmentSeed(baseTime.AddDays(1)));
        }

        [Fact]
        public void IsFireworksWindow_CoversGregorianNewYearNight()
        {
            Assert.True(SceneryConditions.IsFireworksWindow(At(month: 1, day: 1, hour: 22)));
            Assert.False(SceneryConditions.IsFireworksWindow(At(month: 1, day: 1, hour: 12)));
            Assert.False(SceneryConditions.IsFireworksWindow(At(month: 1, day: 15, hour: 22)));
        }

        [Fact]
        public void IsLunarNewYearPeriod_CoversTheFirstFiveDaysOfTheFirstLunarMonth()
        {
            // 2026 春节 2/17；2025 春节 1/29。
            Assert.True(SceneryConditions.IsLunarNewYearPeriod(new DateTime(2026, 2, 17)));
            Assert.True(SceneryConditions.IsLunarNewYearPeriod(new DateTime(2026, 2, 21)));
            Assert.False(SceneryConditions.IsLunarNewYearPeriod(new DateTime(2026, 2, 22)));
            Assert.False(SceneryConditions.IsLunarNewYearPeriod(new DateTime(2026, 2, 25)));
        }

        [Fact]
        public void IsLunarNewYearPeriod_DetectsEveInNonLeapLunarYears()
        {
            // 农历 2024/2022/2026 年无闰月，腊月索引为 12。
            Assert.True(SceneryConditions.IsLunarNewYearPeriod(new DateTime(2025, 1, 28)));
            Assert.True(SceneryConditions.IsLunarNewYearPeriod(new DateTime(2023, 1, 21)));
            Assert.True(SceneryConditions.IsLunarNewYearPeriod(new DateTime(2027, 2, 5)));
        }

        /// <summary>
        /// 回归：闰年农历有 13 个月，腊月被 GetMonth 报为 13。旧实现硬编码判断
        /// == 12，导致每个闰年的除夕都漏检（农历 2025 闰七月 → 2026-02-16，
        /// 农历 2023 闰二月 → 2024-02-09）。
        /// </summary>
        [Fact]
        public void IsLunarNewYearPeriod_DetectsEveInLeapLunarYears()
        {
            Assert.True(SceneryConditions.IsLunarNewYearPeriod(new DateTime(2026, 2, 16)));
            Assert.True(SceneryConditions.IsLunarNewYearPeriod(new DateTime(2024, 2, 9)));
        }

        [Fact]
        public void IsLunarNewYearPeriod_ExcludesTheDayBeforeEve()
        {
            Assert.False(SceneryConditions.IsLunarNewYearPeriod(new DateTime(2026, 2, 15)));
            Assert.False(SceneryConditions.IsLunarNewYearPeriod(new DateTime(2025, 1, 27)));
        }

        [Fact]
        public void IsSpaceWindow_RequiresClearNight()
        {
            Assert.True(SceneryConditions.IsSpaceWindow(At(hour: 22)));
            Assert.False(SceneryConditions.IsSpaceWindow(At(hour: 12)));
            Assert.False(SceneryConditions.IsSpaceWindow(At(hour: 22, weather: Weather(13))));
        }

        private static EnvironmentInputSnapshot At(
            int month = 6,
            int day = 15,
            int hour = 12,
            int minute = 0,
            WeatherInfo weather = null)
        {
            return new EnvironmentInputSnapshot
            {
                Now = new DateTime(2026, month, day, hour, minute, 0),
                Sunrise = new TimeSpan(6, 30, 0),
                Sunset = new TimeSpan(18, 30, 0),
                TimeSegment = DebugTimeSegment.Day,
                Weather = weather,
                ConfigurationValid = true
            };
        }

        private static WeatherInfo Weather(int code)
        {
            return new WeatherInfo { Code = code, Text = "test", Temperature = 20 };
        }
    }
}
