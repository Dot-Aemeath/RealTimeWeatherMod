using System;
using System.Globalization;
using ChillWithYou.EnvSync.Core;
using Xunit;

namespace RealTimeWeatherMod.Tests.Core
{
    public class SceneryAutomationTriggerStateCodecTests
    {
        [Fact]
        public void FormatEntry_PreservesRollDone_WhenProbabilityEventDidNotTrigger()
        {
            string value = SceneryAutomationTriggerStateCodec.FormatEntry(
                triggered: false,
                rollDone: true);

            SceneryAutomationTriggerStateCodec.Entry entry;
            bool parsed = SceneryAutomationTriggerStateCodec.TryParseEntry(value, out entry);

            Assert.True(parsed);
            Assert.Equal("R", value);
            Assert.False(entry.Triggered);
            Assert.True(entry.RollDone);
        }

        [Fact]
        public void FormatEntry_ReturnsNull_WhenNothingToPersist()
        {
            Assert.Null(SceneryAutomationTriggerStateCodec.FormatEntry(
                triggered: false,
                rollDone: false));
        }

        [Fact]
        public void FormatEntry_RoundTripsStartTime()
        {
            var startTime = new DateTime(2026, 6, 25, 21, 30, 0, DateTimeKind.Local);
            string value = SceneryAutomationTriggerStateCodec.FormatEntry(
                triggered: true,
                rollDone: false,
                startTime: startTime);

            SceneryAutomationTriggerStateCodec.Entry entry;
            bool parsed = SceneryAutomationTriggerStateCodec.TryParseEntry(value, out entry);

            Assert.True(parsed);
            Assert.True(entry.Triggered);
            Assert.Equal(startTime, entry.StartTime);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("X")]
        [InlineData(";")]
        [InlineData("X;not-a-date")]
        public void TryParseEntry_RejectsGarbage(string value)
        {
            SceneryAutomationTriggerStateCodec.Entry entry;
            Assert.False(SceneryAutomationTriggerStateCodec.TryParseEntry(value, out entry));
        }

        [Fact]
        public void TryParseEntry_AcceptsUnknownFlags_WhenStartTimeIsValid()
        {
            string isoTime = new DateTime(2026, 6, 25, 21, 30, 0, DateTimeKind.Local)
                .ToString("o", CultureInfo.InvariantCulture);

            SceneryAutomationTriggerStateCodec.Entry entry;
            bool parsed = SceneryAutomationTriggerStateCodec.TryParseEntry("X;" + isoTime, out entry);

            Assert.True(parsed);
            Assert.False(entry.Triggered);
            Assert.False(entry.RollDone);
            Assert.NotEqual(DateTime.MinValue, entry.StartTime);
        }

        [Theory]
        [InlineData("2026-06-25", true)]
        [InlineData("2026-6-25", false)]
        [InlineData("not-a-date", false)]
        [InlineData("", false)]
        public void TryParseDate_OnlyAcceptsIsoDate(string value, bool expected)
        {
            DateTime date;
            Assert.Equal(expected, SceneryAutomationTriggerStateCodec.TryParseDate(value, out date));
        }
    }
}
