using ChillWithYou.EnvSync.Core;
using Xunit;

namespace RealTimeWeatherMod.Tests.Core
{
    public class SceneryAutomationOwnershipStateTests
    {
        [Fact]
        public void RecordUserInteraction_AtomicallyReleasesManagedOwnership()
        {
            var state = new SceneryAutomationOwnershipState<string>();
            Assert.True(state.TryMarkManaged("rain"));

            OwnershipTransition transition = state.RecordUserInteraction("rain");

            Assert.True(transition.UserOwnershipAdded);
            Assert.True(transition.ManagedOwnershipReleased);
            Assert.True(state.IsUserOwned("rain"));
            Assert.False(state.IsManaged("rain"));
        }

        [Fact]
        public void TryMarkManaged_RefusesUserOwnedItem()
        {
            var state = new SceneryAutomationOwnershipState<string>();
            state.RecordUserInteraction("rain");

            Assert.False(state.TryMarkManaged("rain"));
            Assert.False(state.IsManaged("rain"));
        }

        [Fact]
        public void RecordUserInteraction_RepairsConflictingPersistedState()
        {
            var state = new SceneryAutomationOwnershipState<string>();
            state.TryMarkManaged("rain");

            OwnershipTransition transition = state.RecordUserInteraction("rain");

            Assert.True(transition.UserOwnershipAdded);
            Assert.True(transition.ManagedOwnershipReleased);
            Assert.True(state.IsUserOwned("rain"));
            Assert.False(state.IsManaged("rain"));
        }

        /// <summary>
        /// Restoring managed ownership must report the refusal rather than swallow it:
        /// a user-owned item silently failing to restore is indistinguishable from a
        /// successful restore to the caller.
        /// </summary>
        [Fact]
        public void TryMarkManaged_ReportsRefusalForUserOwnedItems()
        {
            var state = new SceneryAutomationOwnershipState<string>();
            state.RecordUserInteraction("rain");

            Assert.False(state.TryMarkManaged("rain"));
            Assert.True(state.IsUserOwned("rain"));
            Assert.Equal(0, state.ManagedCount);
        }

        [Fact]
        public void Snapshots_AreDetachedFromInternalState()
        {
            var state = new SceneryAutomationOwnershipState<string>();
            state.TryMarkManaged("rain");
            var snapshot = state.ManagedSnapshot();

            snapshot.Clear();

            Assert.True(state.IsManaged("rain"));
            Assert.Equal(1, state.ManagedCount);
        }
    }
}
