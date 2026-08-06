using System;
using ChillWithYou.EnvSync.Core;
using Xunit;

namespace RealTimeWeatherMod.Tests.Core
{
    public class SceneryDailyTriggerStateTests
    {
        [Fact]
        public void TryRoll_RecordsFailureOnlyOncePerDay()
        {
            var state = new SceneryDailyTriggerState();
            state.EnsureDate(new DateTime(2026, 8, 4));

            Assert.False(state.TryRoll(
                SceneryDailyTrigger.WindBell,
                0.05,
                0.9,
                out var changed,
                out var triggeredNow));
            Assert.True(changed);
            Assert.False(triggeredNow);

            Assert.False(state.TryRoll(
                SceneryDailyTrigger.WindBell,
                1.0,
                0.0,
                out changed,
                out triggeredNow));
            Assert.False(changed);
        }

        [Fact]
        public void EnsureDate_ResetsDailyEntriesAndWhaleOverride()
        {
            var state = new SceneryDailyTriggerState();
            state.EnsureDate(new DateTime(2026, 8, 4));
            state.TryRoll(
                SceneryDailyTrigger.Whale,
                1.0,
                0.0,
                out _,
                out _);

            Assert.True(state.EnsureDate(new DateTime(2026, 8, 5)));

            Assert.False(state.IsTriggered(SceneryDailyTrigger.Whale));
            Assert.False(state.WhaleOverrideActive);
        }

        [Fact]
        public void WhaleRoll_ActivatesAndCanClearOverrideWithoutChangingDailyRoll()
        {
            var state = new SceneryDailyTriggerState();

            Assert.True(state.TryRoll(
                SceneryDailyTrigger.Whale,
                0.5,
                0.1,
                out _,
                out var triggeredNow));
            Assert.True(triggeredNow);
            Assert.True(state.WhaleOverrideActive);

            Assert.True(state.ClearWhaleOverride());
            Assert.False(state.WhaleOverrideActive);
            Assert.True(state.IsTriggered(SceneryDailyTrigger.Whale));
        }

        [Fact]
        public void TryRollRecurring_StampsStartTimeAndExpiresAfterDuration()
        {
            var state = new SceneryDailyTriggerState();
            DateTime start = new DateTime(2026, 6, 1, 21, 0, 0);
            TimeSpan duration = SceneryDailyTriggerState.BlueButterflyDuration;

            Assert.True(state.TryRollRecurring(
                SceneryDailyTrigger.BlueButterfly,
                start,
                0.15,
                0.1,
                out var changed));
            Assert.True(changed);
            Assert.True(state.IsActiveWithin(
                SceneryDailyTrigger.BlueButterfly, start.AddMinutes(19), duration));
            Assert.False(state.IsActiveWithin(
                SceneryDailyTrigger.BlueButterfly, start.AddMinutes(20), duration));
        }

        /// <summary>
        /// A recurring miss must not consume the day: unlike TryRoll it leaves RollDone
        /// false so the next segment can roll again.
        /// </summary>
        [Fact]
        public void TryRollRecurring_MissLeavesTheTriggerRollable()
        {
            var state = new SceneryDailyTriggerState();
            DateTime now = new DateTime(2026, 6, 1, 21, 0, 0);

            Assert.False(state.TryRollRecurring(
                SceneryDailyTrigger.BlueButterfly, now, 0.15, 0.9, out var changed));
            Assert.False(changed);
            Assert.False(state.Snapshot(SceneryDailyTrigger.BlueButterfly).RollDone);

            Assert.True(state.TryRollRecurring(
                SceneryDailyTrigger.BlueButterfly, now.AddMinutes(20), 0.15, 0.1, out _));
        }

        [Fact]
        public void TryRollRecurring_DoesNotRetriggerOnceActive()
        {
            var state = new SceneryDailyTriggerState();
            DateTime start = new DateTime(2026, 6, 1, 21, 0, 0);

            Assert.True(state.TryRollRecurring(
                SceneryDailyTrigger.BlueButterfly, start, 1d, 0d, out _));
            Assert.False(state.TryRollRecurring(
                SceneryDailyTrigger.BlueButterfly, start.AddMinutes(20), 1d, 0d, out var changed));
            Assert.False(changed);
            Assert.Equal(start, state.Snapshot(SceneryDailyTrigger.BlueButterfly).StartTime);
        }

        [Fact]
        public void Restore_NormalizesTriggeredEntryAsRolled()
        {
            var state = new SceneryDailyTriggerState();
            state.Restore(SceneryDailyTrigger.Space, true, false, DateTime.MinValue);

            SceneryTriggerSnapshot snapshot = state.Snapshot(SceneryDailyTrigger.Space);

            Assert.True(snapshot.Triggered);
            Assert.True(snapshot.RollDone);
        }

        [Fact]
        public void Restore_DropsStartTimeWhenNotTriggered()
        {
            var state = new SceneryDailyTriggerState();
            state.Restore(
                SceneryDailyTrigger.BlueButterfly,
                false,
                false,
                new DateTime(2026, 6, 1, 21, 0, 0));

            Assert.Equal(
                DateTime.MinValue,
                state.Snapshot(SceneryDailyTrigger.BlueButterfly).StartTime);
        }

        [Fact]
        public void Reset_ClearsEveryTriggerIncludingTheTimedOne()
        {
            var state = new SceneryDailyTriggerState();
            DateTime day = new DateTime(2026, 6, 1, 21, 0, 0);
            state.TryRollRecurring(SceneryDailyTrigger.BlueButterfly, day, 1d, 0d, out _);
            state.TryRoll(SceneryDailyTrigger.Whale, 1d, 0d, out _, out _);

            Assert.True(state.EnsureDate(day.AddDays(1)));

            foreach (SceneryDailyTrigger trigger in SceneryDailyTriggerState.All)
            {
                Assert.False(state.IsTriggered(trigger));
            }
            Assert.False(state.WhaleOverrideActive);
        }

        [Fact]
        public void All_CoversEveryDeclaredTrigger()
        {
            Assert.Equal(
                Enum.GetValues(typeof(SceneryDailyTrigger)).Length,
                SceneryDailyTriggerState.All.Length);
        }

        /// <summary>
        /// The trigger-state file lives in the user's config directory and survives
        /// upgrades. Folding the blue butterfly into the shared table must not change
        /// what gets written, or an existing egg would be lost on the next launch.
        /// </summary>
        [Fact]
        public void PersistedForm_SurvivesARestoreRoundTripUnchanged()
        {
            DateTime start = new DateTime(2026, 6, 1, 21, 0, 0, DateTimeKind.Local);
            var original = new SceneryDailyTriggerState();
            original.TryRollRecurring(SceneryDailyTrigger.BlueButterfly, start, 1d, 0d, out _);
            original.TryRoll(SceneryDailyTrigger.WindBell, 1d, 0d, out _, out _);
            original.TryRoll(SceneryDailyTrigger.Space, 0d, 0.5d, out _, out _);

            var restored = new SceneryDailyTriggerState();
            foreach (SceneryDailyTrigger trigger in SceneryDailyTriggerState.All)
            {
                string line = Format(original.Snapshot(trigger));
                if (line == null) continue;

                SceneryAutomationTriggerStateCodec.Entry entry;
                Assert.True(SceneryAutomationTriggerStateCodec.TryParseEntry(line, out entry));
                restored.Restore(trigger, entry.Triggered, entry.RollDone, entry.StartTime);
            }

            foreach (SceneryDailyTrigger trigger in SceneryDailyTriggerState.All)
            {
                Assert.Equal(Format(original.Snapshot(trigger)), Format(restored.Snapshot(trigger)));
            }
        }

        /// <summary>
        /// Pre-existing files wrote the butterfly as "T;&lt;timestamp&gt;" with no R flag.
        /// </summary>
        [Fact]
        public void BlueButterfly_KeepsTheLegacyFlagShape()
        {
            DateTime start = new DateTime(2026, 6, 1, 21, 0, 0);
            var state = new SceneryDailyTriggerState();
            state.TryRollRecurring(SceneryDailyTrigger.BlueButterfly, start, 1d, 0d, out _);

            string line = Format(state.Snapshot(SceneryDailyTrigger.BlueButterfly));

            Assert.StartsWith("T;", line);
            Assert.DoesNotContain("R", line.Split(';')[0]);
        }

        [Fact]
        public void LegacyEntryWithoutRollFlag_StillCountsAsRolledForAllDayTriggers()
        {
            var state = new SceneryDailyTriggerState();
            SceneryAutomationTriggerStateCodec.Entry entry;
            Assert.True(SceneryAutomationTriggerStateCodec.TryParseEntry("T", out entry));

            state.Restore(SceneryDailyTrigger.WindBell, entry.Triggered, entry.RollDone, entry.StartTime);

            Assert.True(state.Snapshot(SceneryDailyTrigger.WindBell).RollDone);
        }

        private static string Format(SceneryTriggerSnapshot snapshot)
        {
            return SceneryAutomationTriggerStateCodec.FormatEntry(
                snapshot.Triggered,
                snapshot.RollDone,
                snapshot.StartTime);
        }

        [Fact]
        public void HasDuration_OnlyTheBlueButterflyCarriesAStartTime()
        {
            foreach (SceneryDailyTrigger trigger in SceneryDailyTriggerState.All)
            {
                Assert.Equal(
                    trigger == SceneryDailyTrigger.BlueButterfly,
                    SceneryDailyTriggerState.HasDuration(trigger));
            }
        }
    }
}
