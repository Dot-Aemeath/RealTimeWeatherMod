using ChillWithYou.EnvSync.Core;
using ChillWithYou.EnvSync.Models;
using Xunit;

namespace RealTimeWeatherMod.Tests.Core;

public class TimeTransitionTests
{
    private static readonly SyncPolicySnapshot Policy = SyncPolicy.Build(true, true, false);

    private static EnvironmentInputSnapshot Inputs(string time, int code = 0, string sunrise = "06:30", string sunset = "18:30")
    {
        var now = DateTime.Parse("2026-09-18 " + time);
        var rise = TimeSpan.Parse(sunrise);
        var set = TimeSpan.Parse(sunset);
        return new EnvironmentInputSnapshot
        {
            Now = now, Sunrise = rise, Sunset = set, ConfigurationValid = true,
            TimeSegment = DebugScenario.DetermineTimeSegment(now, rise, set),
            Weather = new WeatherInfo { Code = code }
        };
    }

    [Theory]
    [InlineData("05:50", "Night", "Day", 0f)]
    [InlineData("06:20", "Night", "Day", .5f)]
    [InlineData("06:30", "Night", "Day", .7407407f)]
    [InlineData("06:50", "Night", "Day", 1f)]
    [InlineData("17:55", "Day", "Sunset", 0f)]
    [InlineData("18:05", "Day", "Sunset", .5f)]
    [InlineData("18:15", "Day", "Sunset", 1f)]
    [InlineData("18:35", "Sunset", "Night", 0f)]
    [InlineData("18:50", "Sunset", "Night", .5f)]
    [InlineData("19:05", "Sunset", "Night", 1f)]
    public void ScheduledBoundaries(string time, string from, string to, float progress)
    {
        var result = TimeTransitionSchedule.Resolve(Policy, Inputs(time));
        Assert.Equal(from, result.Visual.From.ToString());
        Assert.Equal(to, result.Visual.To.ToString());
        Assert.Equal(progress, result.Visual.Amount, 5);
        Assert.NotNull(result.Boundary);
    }

    [Fact]
    public void CloudyOverrideUsesSameDecisionAndSkipsEqualEndpoints()
    {
        var dawn = TimeTransitionSchedule.Resolve(Policy, Inputs("06:30", 9));
        Assert.Equal(ModEnvironment.Night, dawn.Visual.From);
        Assert.Equal(ModEnvironment.Cloudy, dawn.Visual.To);
        var evening = TimeTransitionSchedule.Resolve(Policy, Inputs("18:00", 9));
        Assert.Null(evening.Boundary);
        Assert.Equal(ModEnvironment.Cloudy, evening.Logical);
        var night = TimeTransitionSchedule.Resolve(Policy, Inputs("19:00", 9));
        Assert.Equal(ModEnvironment.Cloudy, night.Visual.From);
        Assert.Equal(ModEnvironment.Night, night.Visual.To);
    }

    [Fact]
    public void MidnightWindowAndNextDayAreResolvedByAbsoluteDate()
    {
        var result = TimeTransitionSchedule.Resolve(Policy, Inputs("23:59", 0, "00:03", "18:30"));
        Assert.Equal(new DateTime(2026, 9, 19, 0, 3, 0), result.Boundary);
        Assert.Equal(.648f, result.Visual.Amount, 5);
        var afterMidnight = TimeTransitionSchedule.Resolve(Policy, Inputs("00:04", 0, "00:03", "18:30"));
        Assert.Equal(new DateTime(2026, 9, 18, 0, 3, 0), afterMidnight.Boundary);
        Assert.InRange(afterMidnight.Visual.Amount, .76f, .77f);
        var next = TimeTransitionSchedule.Resolve(Policy, Inputs("23:00"));
        Assert.Equal(new DateTime(2026, 9, 19, 5, 50, 0), next.NextWindowStart);
    }

    [Theory]
    [InlineData("18:29", "18:30")]
    [InlineData("06:30", "23:50")]
    [InlineData("06:30", "06:00")]
    [InlineData("06:30", "07:20")]
    [InlineData("00:30", "23:20")]
    public void InvalidOrOverlappingSchedulesKeepDiscreteDecision(string rise, string set)
    {
        var inputs = Inputs("18:00", 0, rise, set);
        var result = TimeTransitionSchedule.Resolve(Policy, inputs);
        Assert.Null(result.Boundary);
        Assert.True(result.Visual.IsSingle);
        Assert.Equal(EnvironmentDecision.Resolve(Policy, inputs).Base, result.Logical);
    }

    private static void Tick(TimeTransitionState state, string time, double realtime, bool enabled = true, bool automatic = true, bool blocked = false)
    {
        var inputs = Inputs(time);
        state.Tick(enabled, automatic, blocked, TimeTransitionSchedule.Resolve(Policy, inputs), inputs, realtime);
    }

    [Fact]
    public void StartupAlignsInTwoSecondsInsteadOfScheduledWindow()
    {
        var state = new TimeTransitionState();
        state.Seed(ModEnvironment.Day);
        Tick(state, "22:00:00", 0);
        Tick(state, "22:00:01", 1);
        Assert.Equal(.5f, state.Current.Weight(ModEnvironment.Night), 4);
        Tick(state, "22:00:02", 2);
        Assert.Equal(1f, state.Current.Weight(ModEnvironment.Night));
    }

    [Fact]
    public void ManualHoldReacquiresPresentationAfterSuspension()
    {
        var state = new TimeTransitionState();
        Tick(state, "12:00:00", 0);
        state.Manual(ModEnvironment.Night, TimeTransitionSchedule.Resolve(Policy, Inputs("12:00")), 0);
        Tick(state, "12:00:02", 2);
        var until = state.ManualUntil;
        state.Suspend();
        state.Seed(ModEnvironment.Day);
        Tick(state, "12:00:10", 10);
        Assert.True(state.OwnsVisual);
        Assert.Equal(until, state.ManualUntil);
        Tick(state, "12:00:12", 12);
        Tick(state, "12:00:14", 14);
        Assert.True(state.OwnsVisual);
        Assert.Equal("manual-hold", state.Mode);
        Assert.Equal(1, state.Current.Weight(ModEnvironment.Night));
    }

    [Fact]
    public void InvalidScheduleManualFinishesThenReturnsToAutomatic()
    {
        var inputs = Inputs("12:00", 0, "20:00", "06:00");
        var schedule = TimeTransitionSchedule.Resolve(Policy, inputs);
        var state = new TimeTransitionState();
        state.Seed(ModEnvironment.Day);
        state.Tick(true, true, false, schedule, inputs, 0);
        state.Manual(ModEnvironment.Sunset, schedule, 0);
        Assert.Null(state.ManualUntil);
        state.Tick(true, true, false, schedule, inputs, 2);
        Assert.Equal(1, state.Current.Weight(ModEnvironment.Sunset));
        state.Tick(true, true, false, schedule, inputs, 3);
        Assert.Equal(1, state.Current.Weight(ModEnvironment.Sunset));
        state.Tick(true, true, false, schedule, inputs, 5);
        Assert.Equal(1, state.Current.Weight(schedule.Logical));
        Assert.False(state.SuppressAutomatic);
    }

    [Fact]
    public void InvalidScheduleEditClearsExistingHoldAndValidScheduleRecovers()
    {
        var state = new TimeTransitionState();
        Tick(state, "12:00", 0);
        state.Manual(ModEnvironment.Night, TimeTransitionSchedule.Resolve(Policy, Inputs("12:00")), 0);
        Tick(state, "12:00:02", 2);
        var invalid = Inputs("12:00:03", 0, "20:00", "06:00");
        state.Tick(true, true, false, TimeTransitionSchedule.Resolve(Policy, invalid), invalid, 3);
        Assert.Null(state.ManualUntil);
        Tick(state, "12:00:04", 4);
        Tick(state, "12:00:06", 6);
        Assert.Equal(1, state.Current.Weight(ModEnvironment.Day));
    }

    [Fact]
    public void StartupInsideWindowTargetsCurrentMixture()
    {
        var state = new TimeTransitionState();
        state.Seed(ModEnvironment.Day);
        Tick(state, "18:00:00", 0);
        Tick(state, "18:00:02", 2);
        var expected = TimeTransitionSchedule.Resolve(Policy, Inputs("18:00:02"));
        Assert.Equal(expected.Visual.Amount, state.Current.Weight(ModEnvironment.Sunset), 5);
    }

    [Fact]
    public void ManualSelectionSurvivesPollingAndResumesNextWindow()
    {
        var state = new TimeTransitionState();
        state.Seed(ModEnvironment.Day);
        Tick(state, "17:00:00", 0);
        Tick(state, "17:00:02", 2);
        state.Manual(ModEnvironment.Night, TimeTransitionSchedule.Resolve(Policy, Inputs("17:00:02")), 2);
        Tick(state, "17:00:04", 4);
        Tick(state, "17:00:34", 34);
        Assert.True(state.SuppressAutomatic);
        Assert.Equal(1, state.Current.Weight(ModEnvironment.Night));
        Tick(state, "17:55:00", 3300);
        Assert.False(state.SuppressAutomatic);
        Assert.Equal(1, state.Current.Weight(ModEnvironment.Night));
        Tick(state, "17:55:02", 3302);
        Assert.True(state.Current.Weight(ModEnvironment.Day) > .99f);
    }

    [Fact]
    public void ClockJumpAndResumeUseShortAlignment()
    {
        var state = new TimeTransitionState();
        state.Seed(ModEnvironment.Day);
        Tick(state, "12:00:00", 0);
        Tick(state, "12:00:02", 2);
        Tick(state, "22:00:00", 3);
        Assert.Equal(1, state.Current.Weight(ModEnvironment.Day));
        Tick(state, "22:00:02", 5);
        Assert.Equal(1, state.Current.Weight(ModEnvironment.Night));
        Tick(state, "22:00:03", 6, blocked: true);
        Assert.False(state.OwnsVisual);
        state.Seed(ModEnvironment.Day);
        Tick(state, "22:00:04", 7);
        Tick(state, "22:00:06", 9);
        Assert.Equal(1, state.Current.Weight(ModEnvironment.Night));
    }

    [Fact]
    public void DisablingSmoothSettlesLogicalStateAndReleases()
    {
        var state = new TimeTransitionState();
        state.Seed(ModEnvironment.Day);
        Tick(state, "18:00:00", 0);
        Tick(state, "18:00:02", 2);
        Tick(state, "18:00:03", 3, enabled: false);
        Tick(state, "18:00:05", 5, enabled: false);
        Assert.False(state.OwnsVisual);
        Assert.Equal(1, state.Current.Weight(ModEnvironment.Sunset));
    }

    [Fact]
    public void DisablingTimeSyncStopsAutomaticProgress()
    {
        var state = new TimeTransitionState();
        state.Seed(ModEnvironment.Day);
        Tick(state, "18:00:00", 0);
        Tick(state, "18:00:02", 2);
        Tick(state, "18:00:03", 3, automatic: false);
        Tick(state, "18:00:05", 5, automatic: false);
        Tick(state, "23:00:00", 6, automatic: false);
        Assert.False(state.OwnsVisual);
        Assert.Equal(1, state.Current.Weight(ModEnvironment.Sunset));
    }

    [Fact]
    public void RepeatedManualRequestsStartAtCurrentVisual()
    {
        var state = new TimeTransitionState();
        state.Seed(ModEnvironment.Day);
        Tick(state, "12:00:00", 0);
        var schedule = TimeTransitionSchedule.Resolve(Policy, Inputs("12:00"));
        state.Manual(ModEnvironment.Night, schedule, 0);
        Tick(state, "12:00:01", 1);
        var prior = state.Current;
        state.Manual(ModEnvironment.Cloudy, schedule, 1);
        Tick(state, "12:00:01", 1);
        Assert.Equal(prior.Weight(ModEnvironment.Night), state.Current.Weight(ModEnvironment.Night));
        Tick(state, "12:00:03", 3);
        Assert.Equal(1, state.Current.Weight(ModEnvironment.Cloudy));
    }

    [Fact]
    public void ThirdPresetInterruptionRemainsContinuousAndNormalized()
    {
        var a = new TimeBlend { From = ModEnvironment.Day, To = ModEnvironment.Sunset, Amount = .4f };
        var b = TimeBlend.Single(ModEnvironment.Night);
        var previous = a;
        for (int step = 1; step <= 200; step++)
        {
            var current = TimeTransitionState.ShortPath(a, b, step / 200f);
            float sum = 0;
            for (int e = 0; e < 4; e++)
            {
                float weight = current.Weight((ModEnvironment)e);
                sum += weight;
                Assert.InRange(Math.Abs(weight - previous.Weight((ModEnvironment)e)), 0, .04f);
            }
            Assert.Equal(1, sum, 5);
            previous = current;
        }
        Assert.Equal(1, previous.Weight(ModEnvironment.Night));
    }

    [Fact]
    public void ScheduleEditRealignsWithoutJumpingTheFirstFrame()
    {
        var state = new TimeTransitionState();
        state.Seed(ModEnvironment.Day);
        Tick(state, "18:00:00", 0);
        Tick(state, "18:00:02", 2);
        float previous = state.Current.Weight(ModEnvironment.Sunset);
        var inputs = Inputs("18:00:03", 0, "06:30", "19:30");
        state.Tick(true, true, false, TimeTransitionSchedule.Resolve(Policy, inputs), inputs, 3);
        Assert.Equal(previous, state.Current.Weight(ModEnvironment.Sunset));
        inputs.Now = inputs.Now.AddSeconds(2);
        state.Tick(true, true, false, TimeTransitionSchedule.Resolve(Policy, inputs), inputs, 5);
        Assert.Equal(1, state.Current.Weight(ModEnvironment.Day));
    }

    [Fact]
    public void EnablingSmoothInsideWindowUsesShortAlignment()
    {
        var state = new TimeTransitionState();
        state.Seed(ModEnvironment.Day);
        Tick(state, "18:05:00", 0, enabled: false);
        Tick(state, "18:05:01", 1);
        Assert.Equal(1, state.Current.Weight(ModEnvironment.Day));
        Tick(state, "18:05:03", 3);
        Assert.InRange(state.Current.Weight(ModEnvironment.Sunset), .5f, .51f);
    }

    [Fact]
    public void ManualShortTransitionWorksWithoutAutomaticTimeSync()
    {
        var state = new TimeTransitionState();
        state.Seed(ModEnvironment.Day);
        Tick(state, "12:00:00", 0, automatic: false);
        state.Manual(ModEnvironment.Night, TimeTransitionSchedule.Resolve(Policy, Inputs("12:00")), 0);
        Tick(state, "12:00:02", 2, automatic: false);
        Assert.Equal(1, state.Current.Weight(ModEnvironment.Night));
        Assert.True(state.SuppressAutomatic);
    }

    [Fact]
    public void FullSunsetHoldsBetweenTheTwoEveningWindows()
    {
        foreach (var time in new[] { "18:15:01", "18:25:00", "18:34:59" })
        {
            var result = TimeTransitionSchedule.Resolve(Policy, Inputs(time));
            Assert.Null(result.Boundary);
            Assert.Equal(1, result.Visual.Weight(ModEnvironment.Sunset));
            Assert.Equal(DateTime.Parse("2026-09-18 18:35"), result.NextWindowStart);
        }
    }

    [Fact]
    public void WindowSeekUsesActualDurationAndKeepsDiscreteAnchors()
    {
        var windows = TimeTransitionSchedule.Windows(new DateTime(2026, 9, 18), TimeSpan.FromHours(6.5), TimeSpan.FromHours(18.5));
        Assert.Equal(new[] { 3600d, 1200d, 1800d }, windows.Select(w => w.DurationSeconds));
        Assert.Equal(new[] { "06:20", "18:05", "18:50" }, windows.Select(w => w.AtProgress(.5).ToString("HH:mm")));
        Assert.Equal(new[] { "06:30", "18:00", "19:00" }, windows.Select(w => w.Boundary.ToString("HH:mm")));
        foreach (var window in windows)
        {
            Assert.Equal(0, window.Progress(window.Start.AddSeconds(-15)));
            Assert.Equal(1, window.Progress(window.End.AddSeconds(15)));
            foreach (double p in new[] { 0d, .25, .5, .75, 1 })
                Assert.Equal(p, window.Progress(window.AtProgress(p)), 5);
        }
    }
}
