using System;
using System.Collections.Generic;

namespace ChillWithYou.EnvSync.Core
{
    internal enum SceneryDailyTrigger
    {
        WindBell,
        HotSpring,
        Space,
        Whale,
        BlueButterfly
    }

    internal struct SceneryTriggerSnapshot
    {
        internal SceneryTriggerSnapshot(bool triggered, bool rollDone)
            : this(triggered, rollDone, DateTime.MinValue)
        {
        }

        internal SceneryTriggerSnapshot(bool triggered, bool rollDone, DateTime startTime)
        {
            Triggered = triggered;
            RollDone = rollDone;
            StartTime = startTime;
        }

        internal bool Triggered { get; }
        internal bool RollDone { get; }

        /// <summary>
        /// When the trigger fired. Only meaningful for triggers that have a duration;
        /// <see cref="DateTime.MinValue"/> for the all-day ones.
        /// </summary>
        internal DateTime StartTime { get; }
    }

    /// <summary>
    /// Owns daily probability state, roll-once semantics and the whale override.
    /// All five eggs live in one table — including the blue butterfly, whose only real
    /// difference is that it has a duration and re-rolls per segment instead of once
    /// per day. Persistence adapters restore and snapshot through explicit methods.
    ///
    /// Threading: main thread only, like the rest of SceneryAutomationSystem.
    /// </summary>
    internal sealed class SceneryDailyTriggerState
    {
        /// <summary>Every trigger, in persistence order.</summary>
        internal static readonly SceneryDailyTrigger[] All =
        {
            SceneryDailyTrigger.BlueButterfly,
            SceneryDailyTrigger.WindBell,
            SceneryDailyTrigger.HotSpring,
            SceneryDailyTrigger.Space,
            SceneryDailyTrigger.Whale
        };

        internal static readonly TimeSpan BlueButterflyDuration = TimeSpan.FromMinutes(20);

        /// <summary>
        /// Whether a trigger's <see cref="SceneryTriggerSnapshot.StartTime"/> carries
        /// meaning. Restore paths use this to decide whether a missing timestamp needs
        /// a fallback; all-day triggers never have one.
        /// </summary>
        internal static bool HasDuration(SceneryDailyTrigger trigger)
        {
            return trigger == SceneryDailyTrigger.BlueButterfly;
        }

        private readonly Dictionary<SceneryDailyTrigger, SceneryTriggerSnapshot> _entries =
            new Dictionary<SceneryDailyTrigger, SceneryTriggerSnapshot>();

        internal DateTime Date { get; private set; } = DateTime.MinValue;
        internal bool WhaleOverrideActive { get; private set; }

        internal bool EnsureDate(DateTime date)
        {
            if (Date.Date == date.Date)
            {
                return false;
            }

            Reset(date.Date);
            return true;
        }

        internal void Reset(DateTime date)
        {
            Date = date == DateTime.MinValue ? DateTime.MinValue : date.Date;
            _entries.Clear();
            WhaleOverrideActive = false;
        }

        internal void RestoreDate(DateTime date)
        {
            Date = date.Date;
        }

        internal void Restore(
            SceneryDailyTrigger trigger,
            bool triggered,
            bool rollDone,
            DateTime startTime)
        {
            // A triggered all-day egg has necessarily rolled, so normalize the flag —
            // older state files predate RollDone. Duration triggers re-roll per segment
            // and never set it, so leave theirs alone.
            bool normalizedRollDone = HasDuration(trigger)
                ? rollDone
                : rollDone || triggered;

            _entries[trigger] = new SceneryTriggerSnapshot(
                triggered,
                normalizedRollDone,
                triggered ? startTime : DateTime.MinValue);
        }

        internal SceneryTriggerSnapshot Snapshot(SceneryDailyTrigger trigger)
        {
            SceneryTriggerSnapshot snapshot;
            return _entries.TryGetValue(trigger, out snapshot)
                ? snapshot
                : new SceneryTriggerSnapshot(false, false);
        }

        internal bool IsTriggered(SceneryDailyTrigger trigger)
        {
            return Snapshot(trigger).Triggered;
        }

        /// <summary>
        /// Roll once per day: after the first roll the outcome sticks until the date
        /// changes, whether it hit or missed.
        /// </summary>
        internal bool TryRoll(
            SceneryDailyTrigger trigger,
            double probability,
            double sample,
            out bool stateChanged,
            out bool triggeredNow)
        {
            SceneryTriggerSnapshot current = Snapshot(trigger);
            stateChanged = false;
            triggeredNow = false;
            if (current.Triggered) return true;
            if (current.RollDone) return false;

            bool triggered = sample < Clamp(probability);
            _entries[trigger] = new SceneryTriggerSnapshot(triggered, true);
            stateChanged = true;
            triggeredNow = triggered;
            if (trigger == SceneryDailyTrigger.Whale && triggered)
            {
                WhaleOverrideActive = true;
            }

            return triggered;
        }

        /// <summary>
        /// Roll until it hits: a miss leaves RollDone false so a later segment may roll
        /// again, and a hit stamps StartTime so <see cref="IsActiveWithin"/> can expire it.
        /// </summary>
        internal bool TryRollRecurring(
            SceneryDailyTrigger trigger,
            DateTime now,
            double probability,
            double sample,
            out bool stateChanged)
        {
            stateChanged = false;
            if (Snapshot(trigger).Triggered) return false;
            if (sample >= Clamp(probability)) return false;

            _entries[trigger] = new SceneryTriggerSnapshot(true, false, now);
            stateChanged = true;
            return true;
        }

        internal bool IsActiveWithin(
            SceneryDailyTrigger trigger,
            DateTime now,
            TimeSpan duration)
        {
            SceneryTriggerSnapshot snapshot = Snapshot(trigger);
            return snapshot.Triggered &&
                snapshot.StartTime != DateTime.MinValue &&
                now - snapshot.StartTime < duration;
        }

        internal void RestoreWhaleOverride(bool active)
        {
            WhaleOverrideActive = active;
        }

        internal bool ClearWhaleOverride()
        {
            if (!WhaleOverrideActive) return false;
            WhaleOverrideActive = false;
            return true;
        }

        private static double Clamp(double probability)
        {
            return Math.Max(0d, Math.Min(1d, probability));
        }
    }
}
