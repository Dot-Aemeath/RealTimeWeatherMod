using ChillWithYou.EnvSync.Core;
using ChillWithYou.EnvSync.Models;
using Xunit;

namespace RealTimeWeatherMod.Tests.Core;

public class DaylightPresentationTests
{
    private static readonly SyncPolicySnapshot Policy = SyncPolicy.Build(true, true, false);

    [Fact]
    public void SunsetAndNightHaveContinuousInteriorVariation()
    {
        Assert.Equal(0, DaylightSchedule.Resolve(Policy, Inputs("18:15")).SunsetProgress);
        Assert.Equal(.5f, DaylightSchedule.Resolve(Policy, Inputs("18:35")).SunsetProgress);
        Assert.Equal(1, DaylightSchedule.Resolve(Policy, Inputs("18:55")).SunsetProgress);
        Assert.Equal(0, DaylightSchedule.Resolve(Policy, Inputs("20:10")).NightDepth);
        Assert.Equal(0, DaylightSchedule.Resolve(Policy, Inputs("05:15")).NightDepth);
        Assert.Equal(1, DaylightSchedule.Resolve(Policy, Inputs("00:42:30")).NightDepth, 6);
        var before = DaylightSchedule.Resolve(Policy, Inputs("23:59:59"));
        var after = DaylightSchedule.Resolve(Policy, Inputs("00:00:00"));
        Assert.InRange(Math.Abs(before.NightDepth - after.NightDepth), 0, .0001f);
    }

    [Fact]
    public void LateSunsetKeepsDuskEndpointAcrossMidnight()
    {
        foreach (var clock in new[] { "23:59:59", "00:00:00", "00:20:00" })
        {
            var input = Inputs(clock);
            input.Sunset = TimeSpan.FromHours(23);
            Assert.True(DaylightSchedule.IsValid(input.Sunrise, input.Sunset));
            Assert.Equal(1, DaylightSchedule.Resolve(Policy, input).SunsetProgress);
        }
    }

    [Fact]
    public void PreviewSeeksTimeImmediatelyWhileCloudsStillFade()
    {
        var state = new DaylightPresentation();
        state.Tick(DaylightSchedule.Resolve(Policy, Inputs("08:00")), true, true, 0, true);
        var target = DaylightSchedule.Resolve(Policy, Inputs("18:35", 9));
        state.Tick(target, true, true, .1, true);
        Assert.Equal(target.Progress, state.Current.Progress);
        Assert.Equal(target.SunsetProgress, state.Current.SunsetProgress);
        Assert.Equal(0, state.Current.Clouds);
        state.Tick(target, true, false, 2.1, true);
        Assert.Equal(target.Clouds, state.Current.Clouds);
    }

    [Theory]
    [InlineData("06:30", .25f)]
    [InlineData("12:30", .5f)]
    [InlineData("18:30", .75f)]
    [InlineData("00:30", 0f)]
    public void SunFollowsRiseNoonSetAndSolarMidnight(string clock, float expected)
        => Assert.Equal(expected, DaylightSchedule.Resolve(Policy, Inputs(clock)).SolarPhase, 6);

    [Theory]
    [InlineData("06:30", "18:30")]
    [InlineData("04:00", "21:00")]
    [InlineData("08:00", "16:00")]
    public void SunMovesForwardAllDayAndIsBelowHorizonOutsideDaylight(string rise, string set)
    {
        float previous = 0;
        for (int minute = 0; minute < 1440; minute++)
        {
            var clock = TimeSpan.FromMinutes(minute);
            var phase = DaylightSchedule.ResolveSolarPhase(clock, TimeSpan.Parse(rise), TimeSpan.Parse(set));
            if (minute > 0)
            {
                float delta = phase - previous;
                if (delta < 0) delta += 1;
                Assert.InRange(delta, .0001f, .002f);
            }
            double height = -Math.Cos(phase * 2 * Math.PI);
            bool day = clock > TimeSpan.Parse(rise) && clock < TimeSpan.Parse(set);
            if (clock != TimeSpan.Parse(rise) && clock != TimeSpan.Parse(set))
                Assert.Equal(day, height > 0);
            previous = phase;
        }
    }

    [Fact]
    public void ClockAlignmentCrossesSolarMidnightWithoutOrbitReversal()
    {
        Assert.Equal(0, DaylightVisual.BlendPhase(.99f, .01f, .5f), 6);
        Assert.Equal(.75f, DaylightVisual.BlendPhase(.25f, .75f, 1), 6);
    }

    [Fact]
    public void FirstAcquisitionDoesNotAnimateFromAnInventedMidnightPhase()
    {
        var state = new DaylightPresentation();
        var target = DaylightSchedule.Resolve(Policy, Inputs("12:30"));
        state.Tick(target, true, true, 0);
        state.Tick(target, true, false, 1);
        Assert.Equal(.5f, state.Current.SolarPhase);
        Assert.Equal(.5f, state.Current.Strength);
    }

    [Theory]
    [InlineData(0f)] [InlineData(.3f)] [InlineData(1f)]
    public void HorizonRetainsSceneryIlluminationWithoutSolarShadows(float nightWeight)
    {
        var horizon = SolarLighting.Resolve(0, nightWeight, 0);
        Assert.Equal(0, horizon.Direct);
        Assert.InRange(horizon.Illumination, .4f, 1);
        foreach (float height in new[] { -.0001f, .0001f })
        {
            var adjacent = SolarLighting.Resolve(height, nightWeight, 0);
            Assert.InRange(Math.Abs(adjacent.Illumination - horizon.Illumination), 0, .00001f);
            Assert.InRange(adjacent.NightDirection, 0, .00001f);
        }
    }

    [Fact]
    public void DayAndNightKeepTheirAuthoredLightLevels()
    {
        var day = SolarLighting.Resolve(.8f, 0, 0);
        Assert.Equal(1, day.Illumination);
        Assert.Equal(1, day.Direct);
        var night = SolarLighting.Resolve(-.8f, 1, 0);
        Assert.Equal(1, night.Illumination);
        Assert.Equal(0, night.Direct);
        Assert.Equal(1, night.NightDirection);
    }

    [Theory]
    [InlineData("18:15")] [InlineData("18:30")] [InlineData("18:55")]
    public void FullSunsetReachesAuthoredBrightnessEvenAcrossTheHorizon(string clock)
    {
        var input = Inputs(clock);
        var blend = TimeTransitionSchedule.Resolve(Policy, input, true).Visual;
        Assert.Equal(1, blend.Weight(ModEnvironment.Sunset));
        float phase = DaylightSchedule.Resolve(Policy, input).SolarPhase;
        float height = (float)-Math.Cos(2 * Math.PI * phase);
        var light = SolarLighting.Resolve(height, blend.Weight(ModEnvironment.Night), blend.Weight(ModEnvironment.Sunset));
        Assert.Equal(1, light.Illumination);
        if (clock != "18:15") Assert.InRange(light.Direct, 0, .00001f);
    }

    [Fact]
    public void SunsetLightLevelJoinsBothAdjacentBlendsContinuously()
    {
        foreach (string clock in new[] { "17:15", "18:15", "18:55", "20:10" })
        {
            var input = Inputs(clock);
            Func<float> illumination = () =>
            {
                var blend = TimeTransitionSchedule.Resolve(Policy, input, true).Visual;
                float phase = DaylightSchedule.Resolve(Policy, input).SolarPhase;
                return SolarLighting.Resolve((float)-Math.Cos(phase * 2 * Math.PI),
                    blend.Weight(ModEnvironment.Night), blend.Weight(ModEnvironment.Sunset)).Illumination;
            };
            float endpoint = illumination();
            foreach (double offset in new[] { -.1, .1 })
            {
                input = Inputs(clock); input.Now = input.Now.AddSeconds(offset);
                Assert.InRange(Math.Abs(illumination() - endpoint), 0, .00001f);
            }
        }
    }
    private static EnvironmentInputSnapshot Inputs(string time, int code = 1)
    {
        var now = DateTime.Parse("2026-09-22 " + time);
        var rise = TimeSpan.FromHours(6.5);
        var set = TimeSpan.FromHours(18.5);
        return new EnvironmentInputSnapshot { Now = now, Sunrise = rise, Sunset = set,
            TimeSegment = DebugScenario.DetermineTimeSegment(now, rise, set), Weather = new WeatherInfo { Code = code } };
    }

    [Theory]
    [InlineData("05:50", 0)]
    [InlineData("07:15", 0)]
    [InlineData("12:30", .5)]
    [InlineData("17:15", 1)]
    [InlineData("18:15", 1)]
    public void DaylightAnchorsMatchTimeWindowsAndSolarNoon(string time, float expected)
        => Assert.Equal(expected, DaylightSchedule.Resolve(Policy, Inputs(time)).Progress, 6);

    [Fact]
    public void DaylightIsContinuousAndMonotonicAcrossBothDaytimeSegments()
    {
        float previous = 0;
        for (var t = DateTime.Parse("2026-09-22 06:49"); t <= DateTime.Parse("2026-09-22 17:56"); t = t.AddSeconds(1))
        {
            float current = DaylightSchedule.Resolve(Policy, Inputs(t.ToString("HH:mm:ss"))).Progress;
            Assert.InRange(current - previous, 0, .0001f);
            previous = current;
        }
        Assert.Equal(1, previous);
    }

    [Theory]
    [InlineData(4, true)] [InlineData(7, true)] [InlineData(9, true)]
    [InlineData(11, true)] [InlineData(20, true)] [InlineData(30, true)] [InlineData(35, true)]
    [InlineData(0, false)] [InlineData(5, false)] [InlineData(10, false)]
    [InlineData(13, false)] [InlineData(21, false)] [InlineData(22, false)] [InlineData(32, false)]
    public void IndependentCloudsKeepBadWeatherExceptionsAndPreserveNight(int code, bool clouds)
    {
        var input = Inputs("22:00", code);
        var next = EnvironmentDecision.Resolve(Policy, input, true);
        Assert.Equal(ModEnvironment.Night, next.Base);
        Assert.Equal(clouds, next.Clouds);
        Assert.Equal(EnvironmentDecision.Resolve(Policy, input).Scenery, next.Scenery);
    }

    [Fact]
    public void CloudyEveningRetainsDuskWhileLegacyModeStillOverridesIt()
    {
        var input = Inputs("17:45", 9);
        var split = TimeTransitionSchedule.Resolve(Policy, input, true);
        Assert.Equal(ModEnvironment.Day, split.Visual.From);
        Assert.Equal(ModEnvironment.Sunset, split.Visual.To);
        Assert.Equal(.5f, split.Visual.Amount, 5);
        Assert.Equal(ModEnvironment.Cloudy, TimeTransitionSchedule.Resolve(Policy, input).Logical);
        Assert.Equal(ModEnvironment.Cloudy, EnvironmentDecision.Resolve(Policy, input).Base);
    }

    [Theory]
    [InlineData(false, true)] [InlineData(true, false)] [InlineData(false, false)]
    public void CloudsRespectExistingCombinedPermission(bool time, bool weather)
        => Assert.False(EnvironmentDecision.Resolve(SyncPolicy.Build(time, weather, false), Inputs("12:00", 9), true).Clouds);

    [Fact]
    public void WeatherReversalStartsAtCurrentWeightAndDoesNotRestartDaylight()
    {
        var state = new DaylightPresentation();
        var target = new DaylightVisual { Progress = .6f, Strength = 1 };
        state.Tick(target, true, true, 0);
        state.Tick(target, true, false, 2);
        target.Clouds = 1;
        state.Tick(target, true, false, 3);
        state.Tick(target, true, false, 4);
        Assert.Equal(.5f, state.Current.Clouds);
        Assert.Equal(.6f, state.Current.Progress);
        target.Clouds = 0;
        state.Tick(target, true, false, 4);
        Assert.Equal(.5f, state.Current.Clouds);
        state.Tick(target, true, false, 5);
        Assert.Equal(.25f, state.Current.Clouds);
        state.Tick(target, true, false, 6);
        Assert.Equal(0, state.Current.Clouds);
    }

    [Fact]
    public void ManualAndDisabledModesReleaseBothChannelsAndResumeGradually()
    {
        var state = new DaylightPresentation();
        var target = DaylightSchedule.Resolve(Policy, Inputs("09:00", 9));
        state.Tick(target, true, true, 0);
        state.Tick(target, true, false, 2);
        state.Tick(target, false, true, 3);
        Assert.Equal(1, state.Current.Strength);
        state.Tick(target, false, false, 5);
        Assert.Equal(0, state.Current.Strength);
        Assert.Equal(0, state.Current.Clouds);
        state.Tick(target, true, true, 6);
        Assert.Equal(0, state.Current.Strength);
        state.Tick(target, true, false, 8);
        Assert.Equal(1, state.Current.Strength);
        Assert.Equal(target.Clouds, state.Current.Clouds);
    }

    [Fact]
    public void ClockOrScheduleChangeRealignsWithoutFirstFrameJump()
    {
        var state = new DaylightPresentation();
        var morning = DaylightSchedule.Resolve(Policy, Inputs("09:00"));
        state.Tick(morning, true, true, 0);
        state.Tick(morning, true, false, 2);
        var evening = DaylightSchedule.Resolve(Policy, Inputs("17:00"));
        state.Tick(evening, true, true, 3);
        Assert.Equal(morning.Progress, state.Current.Progress);
        state.Tick(evening, true, false, 5);
        Assert.Equal(evening.Progress, state.Current.Progress);
    }

    [Fact]
    public void ChangedSunTimesRecomputeNoonAndShareTheControllersAlignmentRevision()
    {
        var inputs = Inputs("12:30");
        var time = new TimeTransitionState();
        var daylight = new DaylightPresentation();
        time.Seed(ModEnvironment.Day);
        time.Tick(true, true, false, TimeTransitionSchedule.Resolve(Policy, inputs, true), inputs, 0);
        daylight.Tick(DaylightSchedule.Resolve(Policy, inputs), true, true, 0);
        daylight.Tick(DaylightSchedule.Resolve(Policy, inputs), true, false, 2);
        Assert.Equal(.5f, daylight.Current.Progress);
        int previousRevision = time.AlignmentRevision;
        inputs.Sunrise = TimeSpan.FromHours(8);
        inputs.Sunset = TimeSpan.FromHours(21);
        var target = DaylightSchedule.Resolve(Policy, inputs);
        Assert.InRange(target.Progress, 0, .499f);
        time.Tick(true, true, false, TimeTransitionSchedule.Resolve(Policy, inputs, true), inputs, 3);
        Assert.NotEqual(previousRevision, time.AlignmentRevision);
        daylight.Tick(target, true, previousRevision != time.AlignmentRevision, 3);
        Assert.Equal(.5f, daylight.Current.Progress);
        daylight.Tick(target, true, false, 5);
        Assert.Equal(target.Progress, daylight.Current.Progress);
    }

    [Fact]
    public void InvalidSunScheduleDoesNotDrivePresentation()
    {
        var input = Inputs("12:00", 9);
        input.Sunset = input.Sunrise;
        var result = DaylightSchedule.Resolve(Policy, input);
        Assert.Equal(0, result.Strength);
        Assert.Equal(0, result.Clouds);
    }

    [Fact]
    public void ShortDayWithoutRoomForBothNoonSegmentsUsesLegacyCapability()
    {
        var input = Inputs("07:00", 9);
        input.Sunset = input.Sunrise.Add(TimeSpan.FromMinutes(130));
        Assert.True(TimeTransitionSchedule.IsValid(input.Sunrise, input.Sunset));
        Assert.False(DaylightSchedule.IsValid(input.Sunrise, input.Sunset));
        Assert.Equal(0, DaylightSchedule.Resolve(Policy, input).Strength);
        Assert.Equal(ModEnvironment.Cloudy, EnvironmentDecision.Resolve(Policy, input,
            DaylightSchedule.IsValid(input.Sunrise, input.Sunset)).Base);
    }

    [Fact]
    public void DawnCrossingMidnightKeepsTheSameDaylightEndpoint()
    {
        var a = Inputs("23:50"); a.Sunrise = TimeSpan.FromMinutes(3);
        var b = Inputs("00:04"); b.Now = b.Now.AddDays(1); b.Sunrise = a.Sunrise;
        Assert.Equal(0, DaylightSchedule.Resolve(Policy, a).Progress);
        Assert.Equal(0, DaylightSchedule.Resolve(Policy, b).Progress);
    }

    [Fact]
    public void EnablingOnNativeCloudyDoesNotClearItsCloudsOnTheFirstFrame()
    {
        var state = new DaylightPresentation();
        state.Seed(ModEnvironment.Cloudy);
        state.Tick(DaylightSchedule.Resolve(Policy, Inputs("12:00", 9)), true, true, 0);
        Assert.Equal(1, state.Current.Clouds);
        Assert.Equal(1, state.Current.CombinedClouds(TimeBlend.Single(ModEnvironment.Cloudy)));
    }

    [Theory]
    [InlineData(0, 0, 0)] [InlineData(0, .5, .5)] [InlineData(0, 1, 1)]
    [InlineData(1, 0, 1)] [InlineData(1, .5, 1)] [InlineData(1, 1, 1)]
    public void ManualCloudyBlendsWithOverlayWithoutDoublingCoverage(float overlay, float cloudy, float expected)
    {
        var visual = new DaylightVisual { Clouds = overlay };
        var time = new TimeBlend { From = ModEnvironment.Day, To = ModEnvironment.Cloudy, Amount = cloudy };
        Assert.Equal(expected, visual.CombinedClouds(time));
    }

    [Theory]
    [InlineData(1)] [InlineData(9)]
    public void ContinuousCalendarCrossesTwoMidnightsWithoutReplacingItsControllers(int weather)
    {
        var time = new TimeTransitionState();
        var daylight = new DaylightPresentation();
        time.Seed(ModEnvironment.Night);
        int revision = 0;
        for (int minute = 0; minute <= 48 * 60; minute++)
        {
            var input = Inputs("00:00", weather);
            input.Now = new DateTime(2026, 12, 31).AddMinutes(minute);
            input.TimeSegment = DebugScenario.DetermineTimeSegment(input.Now, input.Sunrise, input.Sunset);
            input.PlaybackRevision = 1;
            var schedule = TimeTransitionSchedule.Resolve(Policy, input, true);
            time.Tick(true, true, false, schedule, input, minute * 60);
            daylight.Tick(DaylightSchedule.Resolve(Policy, input), true, revision != time.AlignmentRevision, minute * 60);
            revision = time.AlignmentRevision;
            Assert.NotEqual(ModEnvironment.Cloudy, time.Logical);
            Assert.InRange(daylight.Current.Progress, 0, 1);
            if (minute > 0) Assert.Equal(EnvironmentDecision.Resolve(Policy, input, true).Weather.Clouds, daylight.Current.Clouds);
            if (input.Now.TimeOfDay == TimeSpan.FromHours(12.5)) Assert.Equal(.5f, daylight.Current.Progress);
        }
        Assert.Equal(ModEnvironment.Night, time.Logical);
        Assert.True(time.OwnsVisual);
    }
}
