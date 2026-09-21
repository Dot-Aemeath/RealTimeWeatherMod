using System;

namespace ChillWithYou.EnvSync.Core
{
    // Unity's monotonic time is supplied by the caller; no system-clock mutation or config IO.
    internal sealed class DebugPlayback
    {
        private static int _revisionCounter;
        private DateTime _anchor;
        private double _realtime;
        private double _resumeSpeed;
        internal double Speed { get; private set; }
        internal int Revision { get; private set; }
        internal DateTime End { get; }
        internal EnvironmentInputSnapshot Inputs { get; }
        internal bool Smooth { get; }

        internal DebugPlayback(EnvironmentInputSnapshot inputs, DateTime end, double speed, bool smooth, double realtime)
        {
            ValidateSpeed(speed);
            Inputs = inputs;
            End = end;
            Smooth = smooth;
            _anchor = inputs.Now;
            _realtime = realtime;
            Speed = _resumeSpeed = speed;
            Revision = ++_revisionCounter;
        }

        internal DateTime Now(double realtime)
        {
            double seconds = Math.Min(Math.Max(0, realtime - _realtime) * Speed, (End - _anchor).TotalSeconds);
            return _anchor.AddSeconds(seconds);
        }

        internal EnvironmentInputSnapshot Capture(double realtime)
        {
            var result = Inputs.WithWeather(Inputs.Weather);
            result.Now = Now(realtime);
            result.TimeSegment = DebugScenario.DetermineTimeSegment(result.Now, result.Sunrise, result.Sunset);
            result.PlaybackRevision = Revision;
            return result;
        }

        internal void SetSpeed(double speed, double realtime)
        {
            ValidateSpeed(speed);
            _anchor = Now(realtime);
            _realtime = realtime;
            Speed = speed;
            if (speed > 0) _resumeSpeed = speed;
        }

        internal void Resume(double realtime) => SetSpeed(_resumeSpeed, realtime);

        internal void Seek(DateTime time, double realtime)
        {
            if (time > End) throw new ArgumentOutOfRangeException(nameof(time), "Cannot seek beyond the test window.");
            _anchor = time;
            _realtime = realtime;
            Revision = ++_revisionCounter;
        }

        private static void ValidateSpeed(double speed)
        {
            if (double.IsNaN(speed) || double.IsInfinity(speed) || speed < 0 || speed > 600)
                throw new ArgumentOutOfRangeException(nameof(speed), "Speed must be between 0 and 600.");
        }
    }
}
