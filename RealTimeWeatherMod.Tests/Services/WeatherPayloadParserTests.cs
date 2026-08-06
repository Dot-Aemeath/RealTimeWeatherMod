using ChillWithYou.EnvSync.Models;
using ChillWithYou.EnvSync.Services;
using Xunit;

namespace RealTimeWeatherMod.Tests.Services
{
    public class WeatherPayloadParserTests
    {
        [Fact]
        public void ParseSeniverseWeather_ReadsCurrentObservation()
        {
            var now = new DateTime(2026, 8, 4, 12, 0, 0);
            const string json =
                "{\"results\":[{\"now\":{\"text\":\"雷阵雨\",\"code\":\"11\",\"temperature\":\"19\"}}]}";

            WeatherInfo result = WeatherPayloadParser.ParseSeniverseWeather(json, now);

            Assert.NotNull(result);
            Assert.Equal(11, result.Code);
            Assert.Equal(19, result.Temperature);
            Assert.Equal("雷阵雨", result.Text);
            Assert.Equal(now, result.UpdateTime);
        }

        [Fact]
        public void ParseSeniverseWeather_ErrorPayload_ReturnsNull()
        {
            WeatherInfo result = WeatherPayloadParser.ParseSeniverseWeather(
                "{\"status\":\"The API key is invalid.\"}",
                DateTime.UtcNow);

            Assert.Null(result);
        }

        [Fact]
        public void ParseOpenMeteoWeather_MapsCurrentObject()
        {
            const string json =
                "{\"current\":{\"temperature_2m\":8.7,\"precipitation\":1.2," +
                "\"rain\":1.2,\"showers\":0,\"snowfall\":0," +
                "\"weather_code\":95,\"cloud_cover\":90}}";

            WeatherInfo result = WeatherPayloadParser.ParseOpenMeteoWeather(
                json,
                DateTime.UtcNow);

            Assert.NotNull(result);
            Assert.Equal(11, result.Code);
            Assert.Equal(9, result.Temperature);
            Assert.Equal("ThunderRain", result.Text);
        }

        [Fact]
        public void ParseSunSchedules_NormalizesBothProviderShapes()
        {
            SunData seniverse = WeatherPayloadParser.ParseSeniverseSunSchedule(
                "{\"results\":[{\"sun\":[{\"sunrise\":\"06:12\",\"sunset\":\"18:45\"}]}]}");
            SunData openMeteo = WeatherPayloadParser.ParseOpenMeteoSunSchedule(
                "{\"daily\":{\"sunrise\":[\"2026-08-04T06:12\"]," +
                "\"sunset\":[\"2026-08-04T18:45\"]}}" );

            Assert.Equal("06:12", seniverse.sunrise);
            Assert.Equal("18:45", seniverse.sunset);
            Assert.Equal("06:12", openMeteo.sunrise);
            Assert.Equal("18:45", openMeteo.sunset);
        }
    }
}
