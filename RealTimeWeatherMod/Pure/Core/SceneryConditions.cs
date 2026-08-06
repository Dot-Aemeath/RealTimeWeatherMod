using System;
using System.Globalization;
using ChillWithYou.EnvSync.Models;

namespace ChillWithYou.EnvSync.Core
{
    internal enum Season
    {
        Spring,
        Summer,
        Autumn,
        Winter
    }

    /// <summary>
    /// The calendar and weather half of every scenery rule, as pure functions over a
    /// single EnvironmentInputSnapshot. Roll-once state, ownership and Unity calls stay
    /// in SceneryAutomationSystem; what remains here is testable without the game.
    /// Every predicate reads its "now" from the snapshot it is handed, so an entire
    /// rule pass sees one consistent moment.
    /// </summary>
    internal static class SceneryConditions
    {
        internal static Season GetSeason(EnvironmentInputSnapshot inputs)
        {
            int month = inputs.Now.Month;
            if (month >= 3 && month <= 5) return Season.Spring;
            if (month >= 6 && month <= 8) return Season.Summer;
            if (month >= 9 && month <= 11) return Season.Autumn;
            return Season.Winter;
        }

        internal static bool IsDay(EnvironmentInputSnapshot inputs)
        {
            int hour = inputs.Now.Hour;
            return hour >= 6 && hour < 18;
        }

        internal static bool IsNight(EnvironmentInputSnapshot inputs)
        {
            int hour = inputs.Now.Hour;
            return hour >= 19 || hour < 5;
        }

        /// <summary>
        /// No weather data counts as good weather: scenery eggs should not be
        /// suppressed just because the provider is unreachable.
        /// </summary>
        internal static bool IsGoodWeather(EnvironmentInputSnapshot inputs)
        {
            WeatherInfo weather = inputs.Weather;
            if (weather == null) return true;
            return weather.Code >= 0 && weather.Code <= 9;
        }

        internal static bool IsSnowyWeather(EnvironmentInputSnapshot inputs)
        {
            // 20-25 为雨夹雪~暴雪（心知码空间，OpenMeteo 映射后同样落在此区间）
            WeatherInfo weather = inputs.Weather;
            return weather != null && weather.Code >= 20 && weather.Code <= 25;
        }

        internal static bool IsFireworksWindow(EnvironmentInputSnapshot inputs)
        {
            DateTime now = inputs.Now;
            bool isGregorianNewYear = now.Month == 1 && now.Day == 1;
            return IsNight(inputs) && (isGregorianNewYear || IsLunarNewYearPeriod(now));
        }

        internal static bool IsCookingWindow(EnvironmentInputSnapshot inputs)
        {
            DateTime now = inputs.Now;
            double time = now.Hour + now.Minute / 60.0;
            return (time >= 11.5 && time <= 12.5) || (time >= 17.5 && time <= 18.5);
        }

        internal static bool IsAirConditionerWindow(EnvironmentInputSnapshot inputs)
        {
            WeatherInfo weather = inputs.Weather;
            if (weather == null) return false;
            return weather.Temperature > 30 || weather.Temperature < 5;
        }

        internal static bool IsSakuraWindow(EnvironmentInputSnapshot inputs)
        {
            int month = inputs.Now.Month;
            return month >= 3 && month <= 4 && IsDay(inputs) && IsGoodWeather(inputs);
        }

        internal static bool IsCicadaWindow(EnvironmentInputSnapshot inputs)
        {
            return GetSeason(inputs) == Season.Summer &&
                IsDay(inputs) &&
                IsGoodWeather(inputs);
        }

        internal static bool IsSpaceWindow(EnvironmentInputSnapshot inputs)
        {
            return IsNight(inputs) && IsGoodWeather(inputs);
        }

        internal static bool IsLocomotiveWindow(EnvironmentInputSnapshot inputs)
        {
            DateTime now = inputs.Now;
            bool isChristmas = now.Month == 12 && (now.Day == 24 || now.Day == 25);
            return isChristmas && IsNight(inputs) && IsGoodWeather(inputs);
        }

        internal static bool IsBalloonWindow(EnvironmentInputSnapshot inputs)
        {
            DateTime now = inputs.Now;
            bool isChildrensDay = now.Month == 6 && now.Day == 1;
            return isChildrensDay && IsDay(inputs) && IsGoodWeather(inputs);
        }

        internal static bool IsBooksWindow(EnvironmentInputSnapshot inputs)
        {
            DateTime now = inputs.Now;
            bool isBookDay = now.Month == 4 && now.Day == 23;
            bool isSchoolDay = now.Month == 9 && now.Day == 1;
            return isBookDay || isSchoolDay;
        }

        internal static bool IsBlueButterflyWindow(EnvironmentInputSnapshot inputs)
        {
            int month = inputs.Now.Month;
            return month >= 5 && month <= 6 && IsNight(inputs) && IsGoodWeather(inputs);
        }

        /// <summary>
        /// Deterministic per-20-minute-segment seed: the same segment always rolls the
        /// same result, so a restart cannot re-roll the butterfly into existence.
        /// </summary>
        internal static int BlueButterflySegmentSeed(DateTime now)
        {
            int currentSegment = (now.Hour * 60 + now.Minute) / 20;
            return now.Year * 10000 + now.DayOfYear * 100 + currentSegment;
        }

        internal static bool IsWindBellWindow(EnvironmentInputSnapshot inputs)
        {
            int month = inputs.Now.Month;
            return month >= 7 && month <= 8 && IsGoodWeather(inputs);
        }

        internal static bool IsHotSpringWindow(EnvironmentInputSnapshot inputs)
        {
            int month = inputs.Now.Month;
            bool inSeason = (month >= 11 && month <= 12) || (month >= 1 && month <= 2);
            return inSeason && (IsGoodWeather(inputs) || IsSnowyWeather(inputs));
        }

        internal static double HotSpringProbability(EnvironmentInputSnapshot inputs)
        {
            return IsSnowyWeather(inputs) ? 0.30 : 0.05;
        }

        /// <summary>
        /// 除夕到正月初五。农历计算失败时返回 false，宁可漏放烟花也不误放。
        /// </summary>
        internal static bool IsLunarNewYearPeriod(DateTime gregorianDate)
        {
            try
            {
                var chineseCalendar = new ChineseLunisolarCalendar();

                int lunarMonth = chineseCalendar.GetMonth(gregorianDate);
                int lunarDay = chineseCalendar.GetDayOfMonth(gregorianDate);

                // 正月初一到初五
                if (lunarMonth == 1 && lunarDay >= 1 && lunarDay <= 5)
                {
                    return true;
                }

                // 除夕（农历年最后一月的最后一天）。闰年有 13 个月，腊月被
                // GetMonth 报为 13，所以必须按「本年最后一个月」判断而不是硬编码 12。
                int lunarYear = chineseCalendar.GetYear(gregorianDate);
                int lastMonth = chineseCalendar.GetMonthsInYear(lunarYear);
                if (lunarMonth == lastMonth &&
                    lunarDay == chineseCalendar.GetDaysInMonth(lunarYear, lastMonth))
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
