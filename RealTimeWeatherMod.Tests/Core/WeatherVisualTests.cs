using System;
using ChillWithYou.EnvSync.Core;
using ChillWithYou.EnvSync.Models;
using Xunit;

namespace RealTimeWeatherMod.Tests.Core;

public class WeatherVisualTests
{
    private static EnvironmentInputSnapshot Input(int code) => new EnvironmentInputSnapshot
    {
        Now = new DateTime(2026, 9, 23, 12, 30, 0), Sunrise = TimeSpan.FromHours(6.5), Sunset = TimeSpan.FromHours(18.5),
        TimeSegment = DebugTimeSegment.Day, Weather = new WeatherInfo { Code = code }
    };
    private static readonly SyncPolicySnapshot Policy = SyncPolicy.Build(true, true, false);
    private static WeatherVisual Weather(int code) => EnvironmentDecision.Resolve(Policy, Input(code), true).Weather;

    [Fact]
    public void CloudGradesIncreaseDimmingWithoutAddingFog()
    {
        var clear = Weather(1); var partly = Weather(4); var mostly = Weather(7); var overcast = Weather(9);
        Assert.True(clear.Dimming < partly.Dimming && partly.Dimming < mostly.Dimming && mostly.Dimming < overcast.Dimming);
        Assert.True(partly.Clouds < mostly.Clouds && mostly.Clouds < overcast.Clouds);
        Assert.Equal(0, partly.Fog); Assert.Equal(0, mostly.Fog); Assert.Equal(0, overcast.Fog);
        Assert.True(Weather(16).Dimming > overcast.Dimming);
    }

    [Fact]
    public void FogHazeAndDustHaveSeparateVisibilityAndIlluminationGrades()
    {
        Assert.InRange(Weather(30).Fog, .01f, .99f);
        Assert.True(Weather(31).Fog > Weather(30).Fog);
        Assert.True(Weather(28).Fog > Weather(26).Fog);
        Assert.True(Weather(30).Dimming > Weather(4).Dimming);
        Assert.NotEqual(Weather(30).Clouds, Weather(30).Dimming);
    }

    [Theory]
    [InlineData(0)] [InlineData(5)] [InlineData(6)] [InlineData(10)] [InlineData(13)]
    [InlineData(21)] [InlineData(22)] [InlineData(32)] [InlineData(33)] [InlineData(99)]
    public void ExistingCloudyExceptionsRemainUnchanged(int code)
        => Assert.True(Weather(code).Equals(default));

    [Theory]
    [InlineData(false, true)] [InlineData(true, false)] [InlineData(false, false)]
    public void PermissionDisablesAllWeatherChannels(bool time, bool weather)
        => Assert.True(EnvironmentDecision.Resolve(SyncPolicy.Build(time, weather, false), Input(30), true).Weather.Equals(default));

    [Fact]
    public void LegacyFallbackStillChoosesNativeCloudyWhileContinuousModeKeepsTime()
    {
        foreach (int code in new[] {4, 7, 9, 30, 31, 16, 28})
        {
            Assert.Equal(ModEnvironment.Cloudy, EnvironmentDecision.Resolve(Policy, Input(code)).Base);
            Assert.Equal(ModEnvironment.Day, EnvironmentDecision.Resolve(Policy, Input(code), true).Base);
        }
    }

    [Fact]
    public void GradeChangeWithEqualCloudCoverageFadesDimmingAndFogFromCurrentValues()
    {
        var state = new DaylightPresentation();
        var a = DaylightSchedule.Resolve(Policy, Input(7));
        var b = DaylightSchedule.Resolve(Policy, Input(31));
        Assert.Equal(a.Clouds, b.Clouds);
        state.Tick(a, true, true, 0, true); state.Tick(a, true, false, 2, true);
        state.Tick(b, true, false, 3, true);
        Assert.True(state.Current.Weather.Equals(a.Weather));
        state.Tick(b, true, false, 4, true);
        var middle = state.Current.Weather;
        Assert.InRange(middle.Fog, .01f, b.Weather.Fog - .01f);
        Assert.Equal(a.SolarPhase, state.Current.SolarPhase);
        state.Tick(a, true, false, 4, true);
        Assert.True(state.Current.Weather.Equals(middle));
        state.Tick(a, true, false, 6, true);
        Assert.True(state.Current.Weather.Equals(a.Weather));
        state.Tick(b, false, true, 7); state.Tick(b, false, false, 9);
        Assert.True(state.Current.Weather.Equals(default));
    }
}
