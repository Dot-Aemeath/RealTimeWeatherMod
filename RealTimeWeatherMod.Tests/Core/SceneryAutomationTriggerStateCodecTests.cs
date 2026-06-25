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
    }
}
