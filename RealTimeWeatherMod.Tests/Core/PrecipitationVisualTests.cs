using System;
using ChillWithYou.EnvSync.Core;
using ChillWithYou.EnvSync.Models;
using Xunit;

namespace RealTimeWeatherMod.Tests.Core;

public class PrecipitationVisualTests
{
    private static EnvironmentInputSnapshot Input(int code) => new EnvironmentInputSnapshot
    {
        Now = new DateTime(2026, 9, 23, 12, 30, 0), Sunrise = TimeSpan.FromHours(6.5), Sunset = TimeSpan.FromHours(18.5),
        TimeSegment = DebugTimeSegment.Day, Weather = new WeatherInfo { Code = code }
    };
    private static PrecipitationVisual Rain(int code, bool time = true, bool weather = true) =>
        EnvironmentDecision.Resolve(SyncPolicy.Build(time, weather, false), Input(code)).Precipitation;

    [Fact]
    public void RainGradesIncreaseFromNativeLightToNativeHeavy()
    {
        Assert.Equal(PrecipitationVisual.LightAnchor, Rain(13).Rain);
        int[] ordered = { 13, 19, 10, 14, 15, 16, 17 };
        for (int i = 1; i < ordered.Length; i++)
            Assert.True(Rain(ordered[i - 1]).Rain < Rain(ordered[i]).Rain, $"{ordered[i - 1]} < {ordered[i]}");
        Assert.Equal(1, Rain(18).Rain);
    }

    [Fact]
    public void ThunderOnlyAccompaniesThunderstormsAndStorms()
    {
        foreach (int code in new[] { 10, 13, 14, 15, 19 }) Assert.Equal(0, Rain(code).Thunder);
        Assert.Equal(1, Rain(11).Thunder);
        Assert.Equal(1, Rain(12).Thunder);
        Assert.True(Rain(16).Thunder > 0 && Rain(16).Thunder < Rain(17).Thunder && Rain(17).Thunder < Rain(18).Thunder);
    }

    [Theory]
    [InlineData(1)] [InlineData(9)] [InlineData(20)] [InlineData(25)] [InlineData(30)] [InlineData(99)]
    public void DryAndSnowCodesHaveNoRain(int code) => Assert.True(Rain(code).Equals(default));

    [Fact]
    public void RainFollowsWeatherPermissionIndependentOfTimeSync()
    {
        Assert.True(Rain(15, time: false).Equals(Rain(15)));
        Assert.True(Rain(15, weather: false).Equals(default));
    }

    [Fact]
    public void EveryRainGradeKeepsItsNativeWindowSelection()
    {
        // The UI window stays the legacy category; intensity only scales inside it.
        foreach (int code in new[] { 10, 11, 12, 13, 14, 15, 16, 17, 18, 19 })
        {
            var decision = EnvironmentDecision.Resolve(SyncPolicy.Build(true, true, false), Input(code));
            Assert.NotNull(decision.Scenery);
            Assert.True(decision.Precipitation.Rain > 0);
            Assert.Equal(decision.Precipitation.Thunder > 0, decision.Scenery == ModEnvironment.ThunderRain);
        }
    }

    [Fact]
    public void EmissionReproducesNativeEndpointsAndReachesZero()
    {
        Assert.Equal(0, new PrecipitationVisual(0, 0).EmissionRate(100, 1000));
        Assert.Equal(50, new PrecipitationVisual(PrecipitationVisual.LightAnchor / 2, 0).EmissionRate(100, 1000), 3);
        Assert.Equal(100, PrecipitationVisual.Native(ModEnvironment.LightRain).EmissionRate(100, 1000), 3);
        Assert.Equal(1000, PrecipitationVisual.Native(ModEnvironment.HeavyRain).EmissionRate(100, 1000), 3);
        float last = -1;
        for (float r = 0; r <= 1; r += .05f)
        {
            float rate = new PrecipitationVisual(r, 0).EmissionRate(100, 1000);
            Assert.True(rate >= last);
            last = rate;
        }
    }

    [Fact]
    public void NativeSeedsMatchTheAuthoredWindows()
    {
        Assert.Equal(0, PrecipitationVisual.Native(ModEnvironment.LightRain).HeavyWeight);
        Assert.Equal(1, PrecipitationVisual.Native(ModEnvironment.HeavyRain).HeavyWeight);
        Assert.Equal(1, PrecipitationVisual.Native(ModEnvironment.ThunderRain).Thunder);
        Assert.True(PrecipitationVisual.Native(null).Equals(default));
        Assert.True(PrecipitationVisual.Native(ModEnvironment.Snow).Equals(default));
    }

    [Fact]
    public void FadeStartsFromCurrentValueAndReversesWithoutJumping()
    {
        var state = new PrecipitationPresentation();
        state.Seed(default);
        var heavy = new PrecipitationVisual(1, 1);
        state.Tick(heavy, 0);
        Assert.Equal(0, state.Current.Rain);
        state.Tick(heavy, PrecipitationPresentation.FadeSeconds / 2);
        var middle = state.Current;
        Assert.InRange(middle.Rain, .4f, .6f);
        Assert.Equal(middle.Rain, middle.Thunder, 5);

        state.Tick(default, PrecipitationPresentation.FadeSeconds / 2);
        Assert.True(state.Current.Equals(middle));
        state.Tick(default, PrecipitationPresentation.FadeSeconds);
        Assert.InRange(state.Current.Rain, .01f, middle.Rain - .01f);
        state.Tick(default, PrecipitationPresentation.FadeSeconds * 1.5);
        Assert.True(state.Current.Equals(default));
    }

    [Fact]
    public void GradeChangeFadesFromNativeSeed()
    {
        var state = new PrecipitationPresentation();
        state.Seed(PrecipitationVisual.Native(ModEnvironment.HeavyRain));
        var moderate = new PrecipitationVisual(.45f, 0);
        state.Tick(moderate, 10);
        Assert.Equal(1, state.Current.Rain);
        state.Tick(moderate, 10 + PrecipitationPresentation.FadeSeconds);
        Assert.True(state.Current.Equals(moderate));
    }
}
