using ChillWithYou.EnvSync.Core;
using ChillWithYou.EnvSync.Models;
using Xunit;

namespace RealTimeWeatherMod.Tests.Core;

public class LongRunningTimeTransitionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void ThreeContinuousDaysCrossYearWithoutResettingController(int weatherCode)
    {
        var start = new DateTime(2026, 12, 30);
        var policy = SyncPolicy.Build(true, true, false);
        var state = new TimeTransitionState();
        state.Seed(ModEnvironment.Night);
        var boundaries = new HashSet<DateTime>();
        var weather = new WeatherInfo { Code = weatherCode };
        // Start with a large process uptime as well as crossing calendar boundaries.
        const double uptime = 30 * 86400;
        var windows = new[]
        {
            (Start: 5 * 3600 + 15 * 60, End: 7 * 3600 + 15 * 60, Boundary: 6.5 * 3600, From: ModEnvironment.Night, To: ModEnvironment.Day),
            (Start: 17 * 3600 + 15 * 60, End: 18 * 3600 + 15 * 60, Boundary: 18d * 3600, From: ModEnvironment.Day, To: ModEnvironment.Sunset),
            (Start: 18 * 3600 + 55 * 60, End: 20 * 3600 + 10 * 60, Boundary: 19d * 3600, From: ModEnvironment.Sunset, To: ModEnvironment.Night)
        };
        for (int seconds = 0; seconds <= 3 * 86400; seconds += 10)
        {
            var now = start.AddSeconds(seconds);
            var input = new EnvironmentInputSnapshot
            {
                ConfigurationValid = true, Now = now, Weather = weather,
                Sunrise = TimeSpan.FromHours(6.5), Sunset = TimeSpan.FromHours(18.5),
                TimeSegment = DebugScenario.DetermineTimeSegment(now, TimeSpan.FromHours(6.5), TimeSpan.FromHours(18.5))
            };
            var schedule = TimeTransitionSchedule.Resolve(policy, input);
            state.Tick(true, true, false, schedule, input, uptime + seconds);
            // The second sampled tick completes startup alignment; mode is refreshed
            // on the following tick. Startup timing has its own frame-granular tests.
            if (seconds <= 10) continue;
            Assert.True(state.OwnsVisual);
            Assert.False(state.SuppressAutomatic);
            double t = now.TimeOfDay.TotalSeconds;
            var window = windows.FirstOrDefault(w => t >= w.Start && t <= w.End);
            if (window.End != 0)
            {
                var from = window.From;
                var to = window.To;
                double p = (t - window.Start) / (window.End - window.Start);
                double weight = p * p * (3 - 2 * p);
                Assert.Equal("scheduled", state.Mode);
                Assert.InRange(Math.Abs(weight - state.Current.Weight(to)), 0, 1e-6);
                Assert.InRange(Math.Abs(1 - weight - state.Current.Weight(from)), 0, 1e-6);
                Assert.Equal(t < window.Boundary ? from : to, state.Logical);
                boundaries.Add(now.Date.AddSeconds(window.Boundary));
            }
            else
            {
                var expected = t < 6.5 * 3600 || t >= 19 * 3600 ? ModEnvironment.Night : t < 18 * 3600 ? ModEnvironment.Day : ModEnvironment.Sunset;
                Assert.Equal("settled", state.Mode);
                Assert.Equal(1, state.Current.Weight(expected));
                Assert.Equal(expected, state.Logical);
            }
        }
        Assert.Equal(9, boundaries.Count);
    }
}
