using ChillWithYou.EnvSync.Core;
using ChillWithYou.EnvSync.Models;
using Xunit;

namespace RealTimeWeatherMod.Tests.Core;

public class DebugPlaybackTests
{
    private static readonly DateTime Start = new(2026, 9, 20, 17, 54, 45);

    private static DebugPlayback Create(double speed = 20) => new(
        new EnvironmentInputSnapshot { IsDebug = true, ConfigurationValid = true,
            Now = Start, Sunrise = TimeSpan.FromHours(6.5), Sunset = TimeSpan.FromHours(18.5),
            Weather = new WeatherInfo { Code = 1 } }, Start.AddSeconds(1230), speed, true, 3);

    [Fact]
    public void PrerollAndEndKeepClockBounded()
    {
        var playback = Create();
        Assert.Equal(Start, playback.Now(1));
        Assert.Equal(Start.AddSeconds(100), playback.Now(8));
        Assert.Equal(playback.End, playback.Now(1000));
    }

    [Fact]
    public void PauseResumeAndSpeedPreserveCurrentTime()
    {
        var playback = Create();
        playback.SetSpeed(0, 8);
        Assert.Equal(Start.AddSeconds(100), playback.Now(100));
        playback.Resume(100);
        Assert.Equal(Start.AddSeconds(120), playback.Now(101));
        playback.SetSpeed(1, 101);
        Assert.Equal(Start.AddSeconds(121), playback.Now(102));
    }

    [Fact]
    public void AcceleratedWholeDaysKeepRevisionAndCrossMidnight()
    {
        var start = Start.Date;
        var playback = new DebugPlayback(new EnvironmentInputSnapshot
        {
            IsDebug = true, ConfigurationValid = true, Now = start,
            Sunrise = TimeSpan.FromHours(6.5), Sunset = TimeSpan.FromHours(18.5),
            Weather = new WeatherInfo { Code = 1 }
        }, start.AddDays(2), 600, true, 3);
        int revision = playback.Revision;
        for (int hours = 0; hours <= 49; hours++)
        {
            var sample = playback.Capture(3 + hours * 6);
            Assert.Equal(start.AddHours(Math.Min(hours, 48)), sample.Now);
            Assert.Equal(revision, sample.PlaybackRevision);
        }
    }

    [Fact]
    public void SeekChangesRevisionAndSnapshotSegmentWithoutMutatingSource()
    {
        var playback = Create();
        int revision = playback.Revision;
        playback.Seek(Start.AddSeconds(400), 10);
        var sample = playback.Capture(10);
        Assert.NotEqual(revision, sample.PlaybackRevision);
        Assert.Equal(DebugTimeSegment.Sunset, sample.TimeSegment);
        Assert.Equal(Start, playback.Inputs.Now);
        Assert.Equal(sample.PlaybackRevision, sample.WithWeather(null).PlaybackRevision);
        Assert.NotEqual(sample.PlaybackRevision, Create().Revision);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(601)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidSpeedDoesNotChangeSession(double speed)
    {
        var playback = Create();
        Assert.Throws<ArgumentOutOfRangeException>(() => playback.SetSpeed(speed, 10));
        Assert.Equal(20, playback.Speed);
    }

    [Fact]
    public void AcceleratedPlaybackDoesNotRestartAlignmentOnEachFrame()
    {
        var playback = Create();
        var state = new TimeTransitionState();
        state.Seed(ModEnvironment.Night);
        var policy = SyncPolicy.Build(true, true, false);
        for (int i = 0; i < 330; i++)
        {
            double realtime = i * .2;
            var input = playback.Capture(realtime);
            var schedule = TimeTransitionSchedule.Resolve(policy, input);
            state.Tick(true, true, false, schedule, input, realtime);
            if (realtime > 4 && input.Now < playback.End.AddSeconds(-15))
            {
                Assert.Equal("scheduled", state.Mode);
                Assert.Equal(schedule.Visual.Amount, state.Current.Amount);
            }
        }
        playback.Seek(Start.AddSeconds(615), 66);
        var seek = playback.Capture(66);
        state.Tick(true, true, false, TimeTransitionSchedule.Resolve(policy, seek), seek, 66);
        Assert.Equal("aligning", state.Mode);
    }
}
