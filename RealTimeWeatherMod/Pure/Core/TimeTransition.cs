using System;
using System.Collections.Generic;

namespace ChillWithYou.EnvSync.Core
{
    internal struct TimeBlend
    {
        internal ModEnvironment From;
        internal ModEnvironment To;
        internal float Amount;

        internal static TimeBlend Single(ModEnvironment value) =>
            new TimeBlend { From = value, To = value };

        internal float Weight(ModEnvironment value) =>
            (From == value ? 1f - Amount : 0f) + (To == value ? Amount : 0f);

        internal ModEnvironment Dominant => Amount < 0.5f ? From : To;
        internal bool IsSingle => From == To || Amount <= 0f || Amount >= 1f;
    }

    internal struct ScheduledTimeBlend
    {
        internal ModEnvironment Logical;
        internal TimeBlend Visual;
        internal DateTime? Boundary;
        internal DateTime? NextWindowStart;
    }

    internal struct TimeTransitionWindow
    {
        internal DateTime Boundary;
        internal DateTime Start;
        internal DateTime End;
        internal double DurationSeconds => (End - Start).TotalSeconds;
        internal double Progress(DateTime now) => Math.Max(0, Math.Min(1, (now - Start).TotalSeconds / DurationSeconds));
        internal DateTime AtProgress(double progress) => Start.AddSeconds(DurationSeconds * progress);
    }

    internal static class TimeTransitionSchedule
    {
        // Boundary remains the existing discrete decision point. The visual windows
        // are asymmetric and independent of the game's button/save-state timing.
        internal static TimeTransitionWindow[] Windows(DateTime date, TimeSpan sunrise, TimeSpan sunset)
        {
            DateTime rise = date.Date + sunrise;
            DateTime set = date.Date + sunset;
            return new[]
            {
                new TimeTransitionWindow { Boundary = rise, Start = rise.AddMinutes(-75), End = rise.AddMinutes(45) },
                new TimeTransitionWindow { Boundary = set.AddMinutes(-30), Start = set.AddMinutes(-75), End = set.AddMinutes(-15) },
                new TimeTransitionWindow { Boundary = set.AddMinutes(30), Start = set.AddMinutes(25), End = set.AddMinutes(100) }
            };
        }

        internal static ScheduledTimeBlend Resolve(SyncPolicySnapshot policy, EnvironmentInputSnapshot inputs, bool independentClouds = false)
        {
            var logical = EnvironmentDecision.Resolve(policy, inputs, independentClouds).Base;
            var result = new ScheduledTimeBlend { Logical = logical, Visual = TimeBlend.Single(logical) };
            if (inputs == null || !IsValid(inputs.Sunrise, inputs.Sunset)) return result;

            // Include adjacent dates so a window straddling midnight is still one window.
            for (int day = -1; day <= 2; day++)
            {
                DateTime date = inputs.Now.Date.AddDays(day);
                foreach (var window in Windows(date, inputs.Sunrise, inputs.Sunset))
                {
                    DateTime boundary = window.Boundary;
                    var from = ResolveAt(policy, inputs, boundary.AddTicks(-1), independentClouds);
                    var to = ResolveAt(policy, inputs, boundary, independentClouds);
                    if (from == to) continue;
                    DateTime start = window.Start;
                    if (start > inputs.Now && (!result.NextWindowStart.HasValue || start < result.NextWindowStart))
                        result.NextWindowStart = start;
                    if (inputs.Now < start || inputs.Now > window.End) continue;
                    result.Boundary = boundary;
                    result.Visual = new TimeBlend { From = from, To = to, Amount = Smooth((float)window.Progress(inputs.Now)) };
                }
            }
            return result;
        }

        internal static bool IsValid(TimeSpan sunrise, TimeSpan sunset)
        {
            // The existing discrete resolver does not wrap a sunset ending after midnight.
            if (sunrise < TimeSpan.Zero || sunrise >= TimeSpan.FromDays(1) ||
                sunset < TimeSpan.Zero || sunset + TimeSpan.FromMinutes(30) >= TimeSpan.FromDays(1)) return false;
            var windows = Windows(new DateTime(2000, 1, 2), sunrise, sunset);
            return windows[0].End <= windows[1].Start && windows[2].End <= windows[0].Start.AddDays(1);
        }

        private static ModEnvironment ResolveAt(SyncPolicySnapshot policy, EnvironmentInputSnapshot inputs, DateTime now, bool independentClouds)
        {
            var sample = inputs.WithWeather(inputs.Weather);
            sample.Now = now;
            sample.TimeSegment = DebugScenario.DetermineTimeSegment(now, inputs.Sunrise, inputs.Sunset);
            return EnvironmentDecision.Resolve(policy, sample, independentClouds).Base;
        }

        internal static float Smooth(float p)
        {
            p = Math.Max(0f, Math.Min(1f, p));
            return p * p * (3f - 2f * p);
        }
    }

    /// <summary>
    /// Seekable presentation state. No accumulated game time, Unity state or weather acquisition.
    /// A short interruption stays on two-preset edges, which the native lightmap blender supports.
    /// </summary>
    internal sealed class TimeTransitionState
    {
        internal const double ShortSeconds = 2;
        internal TimeBlend Current { get; private set; }
        internal ModEnvironment Logical { get; private set; }
        internal string Mode { get; private set; } = "idle";
        internal DateTime? ManualUntil { get; private set; }
        internal bool HasVisual { get; private set; }
        internal bool OwnsVisual { get; private set; }
        internal bool SuppressAutomatic => ManualUntil.HasValue;
        internal int AlignmentRevision { get; private set; }

        private bool _wasEnabled;
        private bool _wasAutomatic;
        private bool _resume;
        private bool _short;
        private bool _manualTransition;
        private bool _releasing;
        private TimeBlend _shortFrom;
        private TimeBlend _shortTarget;
        private ModEnvironment _shortWaypoint;
        private double _shortStart;
        private DateTime? _lastNow;
        private double _lastRealtime;
        private int? _lastPlaybackRevision;
        private TimeSpan _sunrise;
        private TimeSpan _sunset;
        private ModEnvironment _manual;
        private ModEnvironment _lastLogical;
        private DateTime? _lastBoundary;

        internal void Seed(ModEnvironment current)
        {
            Current = TimeBlend.Single(current);
            Logical = current;
            HasVisual = true;
        }

        internal void Manual(ModEnvironment target, ScheduledTimeBlend schedule, double realtime)
        {
            _manual = target;
            Logical = target;
            // Without a valid window, finish the manual animation but do not create
            // an indefinite hold that can outlive a repaired sun schedule.
            ManualUntil = schedule.NextWindowStart;
            BeginShort(TimeBlend.Single(target), realtime);
            _manualTransition = true;
            Mode = "manual";
        }

        internal void Suspend()
        {
            OwnsVisual = false;
            _short = false;
            _manualTransition = false;
            _releasing = false;
            HasVisual = false;
            _resume = true;
            Mode = "suspended";
        }

        internal void Tick(bool enabled, bool automatic, bool blocked, ScheduledTimeBlend schedule,
            EnvironmentInputSnapshot inputs, double realtime, bool immediate = false)
        {
            // Playback speed is intentional. Only an explicit seek restarts short alignment.
            bool jump = inputs.PlaybackRevision.HasValue
                ? inputs.PlaybackRevision != _lastPlaybackRevision
                : _lastNow.HasValue && Math.Abs((inputs.Now - _lastNow.Value).TotalSeconds -
                    (inputs.IsDebug ? 0 : realtime - _lastRealtime)) > 2;
            _lastPlaybackRevision = inputs.PlaybackRevision;
            bool changedSchedule = _lastNow.HasValue && (_sunrise != inputs.Sunrise || _sunset != inputs.Sunset);
            _lastNow = inputs.Now;
            _lastRealtime = realtime;
            _sunrise = inputs.Sunrise;
            _sunset = inputs.Sunset;
            if (blocked)
            {
                Suspend();
                return;
            }

            bool manualExpired = ManualUntil.HasValue && inputs.Now >= ManualUntil.Value;
            if (manualExpired) ManualUntil = null;
            else if (ManualUntil.HasValue && changedSchedule)
            {
                ManualUntil = schedule.NextWindowStart;
                manualExpired = !ManualUntil.HasValue;
            }
            if (!automatic && _wasAutomatic)
            {
                // Finish at the game's logical preset rather than leaving half-enabled objects.
                BeginShort(TimeBlend.Single(Logical), realtime);
                _releasing = true;
            }
            if (!enabled && _wasEnabled)
            {
                BeginShort(TimeBlend.Single(Logical), realtime);
                _releasing = true;
                ManualUntil = null;
                _manualTransition = false;
            }

            bool drive = enabled && automatic && !ManualUntil.HasValue && !_manualTransition;
            if (drive)
            {
                bool enter = !_wasEnabled || !_wasAutomatic || _resume || !OwnsVisual || manualExpired ||
                    Mode == "manual" || Mode == "manual-hold";
                Logical = schedule.Logical;
                bool weatherChanged = _lastLogical != schedule.Logical && _lastBoundary == schedule.Boundary && !jump && !changedSchedule;
                if (immediate)
                {
                    Current = schedule.Visual;
                    HasVisual = true;
                    _short = false;
                }
                else if (enter || jump || changedSchedule)
                {
                    BeginShort(schedule.Visual, realtime);
                }
                else if (weatherChanged && !schedule.Boundary.HasValue)
                {
                    // Weather-driven base changes retain their existing immediate behavior.
                    Current = schedule.Visual;
                    _short = false;
                }
                if (_short) _shortTarget = schedule.Visual;
                else Current = schedule.Visual;
                OwnsVisual = true;
                _releasing = false;
                Mode = _short ? "aligning" : schedule.Boundary.HasValue ? "scheduled" : "settled";
            }
            else if (enabled && (ManualUntil.HasValue || _manualTransition))
            {
                Logical = _manual;
                if (automatic && !OwnsVisual)
                    BeginShort(TimeBlend.Single(_manual), realtime);
                Mode = _short ? "manual" : "manual-hold";
            }

            if (_short)
            {
                float p = (float)((realtime - _shortStart) / ShortSeconds);
                Current = ShortPath(_shortFrom, _shortTarget, p, _shortWaypoint);
                if (p >= 1)
                {
                    _short = false;
                    _manualTransition = false;
                    if (_releasing)
                    {
                        OwnsVisual = false;
                        _releasing = false;
                        Mode = "idle";
                    }
                }
                else if (_releasing) Mode = "releasing";
            }
            _wasEnabled = enabled;
            _wasAutomatic = automatic;
            _resume = false;
            _lastLogical = schedule.Logical;
            _lastBoundary = schedule.Boundary;
        }

        private void BeginShort(TimeBlend target, double realtime)
        {
            AlignmentRevision++;
            if (!HasVisual) Seed(target.Dominant);
            _shortFrom = Current;
            _shortTarget = target;
            _shortWaypoint = target.Dominant;
            _shortStart = realtime;
            _short = true;
            OwnsVisual = true;
            _releasing = false;
        }

        internal static TimeBlend ShortPath(TimeBlend from, TimeBlend to, float progress, ModEnvironment? targetWaypoint = null)
        {
            if (progress >= 1) return to;
            if (progress <= 0) return from;
            var presets = new HashSet<ModEnvironment>();
            foreach (var e in new[] { from.From, from.To, to.From, to.To })
                if (from.Weight(e) > 0 || to.Weight(e) > 0) presets.Add(e);
            if (presets.Count <= 2)
            {
                var values = new List<ModEnvironment>(presets);
                if (values.Count == 1) return TimeBlend.Single(values[0]);
                float p = TimeTransitionSchedule.Smooth(progress);
                return new TimeBlend { From = values[0], To = values[1], Amount = from.Weight(values[1]) + (to.Weight(values[1]) - from.Weight(values[1])) * p };
            }
            // A new third preset cannot fit in MLS's two texture slots. Pass continuously
            // through endpoint presets within the same two seconds instead of snapping.
            if (progress < 1f / 3f) return ShortPath(from, TimeBlend.Single(from.Dominant), progress * 3);
            var waypoint = targetWaypoint ?? to.Dominant;
            if (progress < 2f / 3f) return ShortPath(TimeBlend.Single(from.Dominant), TimeBlend.Single(waypoint), progress * 3 - 1);
            return ShortPath(TimeBlend.Single(waypoint), to, progress * 3 - 2);
        }
    }
}
