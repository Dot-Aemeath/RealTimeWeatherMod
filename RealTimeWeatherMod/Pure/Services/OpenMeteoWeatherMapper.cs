using System;
using ChillWithYou.EnvSync.Models;

namespace ChillWithYou.EnvSync.Services
{
    internal static class OpenMeteoWeatherMapper
    {
        internal static WeatherInfo Map(
            int weatherCode,
            double temperature,
            double precipitation,
            double rain,
            double showers,
            double snowfall,
            double cloudCover,
            DateTime updateTime)
        {
            int normalizedCode = ToSeniverseCode(weatherCode, precipitation, rain, showers, snowfall, cloudCover);
            return new WeatherInfo
            {
                Code = normalizedCode,
                Text = ToWeatherText(weatherCode, normalizedCode),
                Temperature = (int)Math.Round(temperature),
                Condition = ToCondition(normalizedCode),
                UpdateTime = updateTime
            };
        }

        internal static int ToSeniverseCode(
            int weatherCode,
            double precipitation,
            double rain,
            double showers,
            double snowfall,
            double cloudCover)
        {
            if (snowfall > 0d || IsSnow(weatherCode))
            {
                return 21;
            }

            if (IsThunder(weatherCode))
            {
                return 11;
            }

            if (IsHeavyRain(weatherCode) || precipitation >= 2.5d || rain + showers >= 2.5d)
            {
                return 14;
            }

            if (IsLightRain(weatherCode) || precipitation > 0d || rain > 0d || showers > 0d)
            {
                return 13;
            }

            if (weatherCode == 45 || weatherCode == 48)
            {
                return 30;
            }

            // Preserve the WMO sky categories in the Seniverse code space so
            // continuous presentation can distinguish overcast from partly cloudy.
            if (weatherCode == 3) return 9;
            if (weatherCode == 2) return 4;
            if (weatherCode == 1) return 5;
            if (cloudCover >= 90d) return 9;
            if (cloudCover >= 65d) return 4;

            return 1;
        }

        private static bool IsLightRain(int weatherCode)
        {
            return weatherCode == 51 ||
                weatherCode == 53 ||
                weatherCode == 55 ||
                weatherCode == 56 ||
                weatherCode == 57 ||
                weatherCode == 61 ||
                weatherCode == 63 ||
                weatherCode == 80;
        }

        private static bool IsHeavyRain(int weatherCode)
        {
            return weatherCode == 65 ||
                weatherCode == 66 ||
                weatherCode == 67 ||
                weatherCode == 81 ||
                weatherCode == 82;
        }

        private static bool IsSnow(int weatherCode)
        {
            return weatherCode == 71 ||
                weatherCode == 73 ||
                weatherCode == 75 ||
                weatherCode == 77 ||
                weatherCode == 85 ||
                weatherCode == 86;
        }

        private static bool IsThunder(int weatherCode)
        {
            return weatherCode == 95 ||
                weatherCode == 96 ||
                weatherCode == 99;
        }

        private static WeatherCondition ToCondition(int normalizedCode)
        {
            return WeatherConditionMapper.FromSeniverseCode(normalizedCode);
        }

        private static string ToWeatherText(int weatherCode, int normalizedCode)
        {
            if (normalizedCode == 21) return "Snow";
            if (normalizedCode == 11) return "ThunderRain";
            if (normalizedCode == 14) return "HeavyRain";
            if (normalizedCode == 13) return "LightRain";
            if (normalizedCode == 30) return "Fog";
            if (normalizedCode == 9) return "Overcast";
            if (normalizedCode == 5) return "Partly Cloudy";
            if (normalizedCode == 4) return "Cloudy";
            return weatherCode == 0 ? "Clear" : "Unknown";
        }
    }
}
