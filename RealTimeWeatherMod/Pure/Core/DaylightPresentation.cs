using System;

namespace ChillWithYou.EnvSync.Core
{
    internal struct DaylightVisual
    {
        internal float Progress;
        internal float Strength;
        internal WeatherVisual Weather;
        internal float Clouds { get => Weather.Clouds; set => Weather.Clouds = value; }
        internal float SunsetProgress;
        internal float NightDepth;
        internal float SolarPhase;
        internal float CombinedClouds(TimeBlend time) => 1 - (1 - Clouds) * (1 - time.Weight(ModEnvironment.Cloudy));

        internal static DaylightVisual Lerp(DaylightVisual a, DaylightVisual b, float p)
        {
            if (p <= 0) return a;
            if (p >= 1) return b;
            return new DaylightVisual
            {
                Progress = a.Progress + (b.Progress - a.Progress) * p,
                Strength = a.Strength + (b.Strength - a.Strength) * p,
                Weather = WeatherVisual.Lerp(a.Weather, b.Weather, p),
                SunsetProgress = a.SunsetProgress + (b.SunsetProgress - a.SunsetProgress) * p,
                NightDepth = a.NightDepth + (b.NightDepth - a.NightDepth) * p,
                SolarPhase = BlendPhase(a.SolarPhase, b.SolarPhase, p)
            };
        }

        internal static float BlendPhase(float from, float to, float p)
        {
            float delta = to - from;
            delta -= (float)Math.Floor(delta + .5f);
            float value = from + delta * p;
            return value - (float)Math.Floor(value);
        }
    }

    internal static class DaylightSchedule
    {
        internal static bool IsValid(TimeSpan sunrise, TimeSpan sunset) =>
            TimeTransitionSchedule.IsValid(sunrise, sunset) && sunset - sunrise > TimeSpan.FromMinutes(150);

        internal static DaylightVisual Resolve(SyncPolicySnapshot policy, EnvironmentInputSnapshot inputs)
        {
            var result = new DaylightVisual { Progress = .5f };
            if (inputs == null || !IsValid(inputs.Sunrise, inputs.Sunset)) return result;
            result.SolarPhase = ResolveSolarPhase(inputs.Now.TimeOfDay, inputs.Sunrise, inputs.Sunset);
            // Match the existing dawn/day and day/sunset endpoints. Noon remains the
            // midpoint of sunrise/sunset, rather than the midpoint of these asymmetric windows.
            var windows = TimeTransitionSchedule.Windows(inputs.Now.Date, inputs.Sunrise, inputs.Sunset);
            double start = (windows[0].End - inputs.Now.Date).TotalSeconds;
            double end = (windows[1].Start - inputs.Now.Date).TotalSeconds;
            double noon = (inputs.Sunrise.TotalSeconds + inputs.Sunset.TotalSeconds) / 2;
            double now = inputs.Now.TimeOfDay.TotalSeconds;
            result.Progress = now <= noon
                ? .5f * TimeTransitionSchedule.Smooth((float)((now - start) / (noon - start)))
                : .5f + .5f * TimeTransitionSchedule.Smooth((float)((now - noon) / (end - noon)));
            // A dawn window may begin the previous evening.
            var dawn = TimeTransitionSchedule.Windows(inputs.Now.Date.AddDays(1), inputs.Sunrise, inputs.Sunset)[0];
            if (inputs.Now >= dawn.Start) result.Progress = 0;
            // A late sunset's fade can continue past midnight; retain its dusk endpoint.
            var sunsetSample = inputs.Now < windows[0].Start ? inputs.Now.AddDays(1) : inputs.Now;
            result.SunsetProgress = TimeTransitionSchedule.Smooth((float)((sunsetSample - windows[1].End).TotalSeconds /
                (windows[2].Start - windows[1].End).TotalSeconds));
            var nightStart = windows[2].End;
            var nightEnd = dawn.Start;
            if (inputs.Now < windows[0].Start)
            {
                nightStart = nightStart.AddDays(-1);
                nightEnd = windows[0].Start;
            }
            if (inputs.Now > nightStart && inputs.Now < nightEnd)
            {
                double phase = (inputs.Now - nightStart).TotalSeconds / (nightEnd - nightStart).TotalSeconds;
                result.NightDepth = (float)Math.Pow(Math.Sin(Math.PI * phase), 2);
            }
            result.Strength = policy != null && policy.CanControlTime ? 1 : 0;
            result.Weather = EnvironmentDecision.Resolve(policy, inputs, true).Weather;
            return result;
        }

        internal static float ResolveSolarPhase(TimeSpan clock, TimeSpan sunrise, TimeSpan sunset)
        {
            double now = clock.TotalSeconds;
            double rise = sunrise.TotalSeconds;
            double set = sunset.TotalSeconds;
            double phase;
            if (now >= rise && now <= set)
                phase = .25 + .5 * (now - rise) / (set - rise);
            else
            {
                if (now < rise) now += 86400;
                phase = .75 + .5 * (now - set) / (86400 - (set - rise));
            }
            return (float)(phase - Math.Floor(phase));
        }
    }

    internal struct SolarLighting
    {
        internal float Direct;
        internal float Illumination;
        internal float NightDirection;

        internal static SolarLighting Resolve(float height, float nightWeight, float sunsetWeight)
        {
            float direct = TimeTransitionSchedule.Smooth(height / .12f);
            // Window scenery shaders use the main light color instead of ambient
            // probes. Keep twilight illumination when direct sunlight disappears,
            // reaching the authored sunset and night light levels at their endpoints.
            float diffuse = .4f + .6f * Math.Max(0, Math.Min(1, nightWeight + sunsetWeight));
            return new SolarLighting
            {
                Direct = direct,
                Illumination = direct + (1 - direct) * diffuse,
                NightDirection = TimeTransitionSchedule.Smooth(-height / .2f)
            };
        }
    }

    /// <summary>Independent visual channels; weather changes never restart the sun's clock.</summary>
    internal sealed class DaylightPresentation
    {
        internal DaylightVisual Current { get; private set; } = new DaylightVisual { Progress = .5f };
        internal DaylightVisual Target { get; private set; }
        private DaylightVisual _from;
        private double _alignmentStart = double.NegativeInfinity;
        private double _cloudStart = double.NegativeInfinity;
        private WeatherVisual _weatherFrom;
        private WeatherVisual _weatherTarget;
        private bool _automatic;

        internal void Seed(ModEnvironment native)
        {
            float clouds = native == ModEnvironment.Cloudy ? 1 : 0;
            Current = new DaylightVisual { Progress = .5f, Weather = new WeatherVisual(clouds, 0, 0) };
            // Native Cloudy already carries its own lighting and fog treatment.
            _weatherFrom = _weatherTarget = Current.Weather;
        }

        internal void Tick(DaylightVisual target, bool automatic, bool realign, double realtime, bool immediate = false)
        {
            if (!automatic) { target.Strength = 0; target.Weather = default; }
            if (realign || automatic != _automatic)
            {
                _from = Current;
                // Native presets have no orbital phase. Acquire their direction
                // without first travelling from the default midnight phase.
                if (automatic && !_automatic && Current.Strength <= 0)
                    _from.SolarPhase = target.SolarPhase;
                _alignmentStart = realtime;
            }
            if (!target.Weather.Equals(_weatherTarget))
            {
                _weatherFrom = Current.Weather;
                _weatherTarget = target.Weather;
                _cloudStart = realtime;
            }
            Target = target;
            float p = immediate ? 1 : TimeTransitionSchedule.Smooth((float)((realtime - _alignmentStart) / TimeTransitionState.ShortSeconds));
            var value = DaylightVisual.Lerp(_from, target, p);
            float cloudP = TimeTransitionSchedule.Smooth((float)((realtime - _cloudStart) / TimeTransitionState.ShortSeconds));
            value.Weather = cloudP >= 1 ? _weatherTarget : WeatherVisual.Lerp(_weatherFrom, _weatherTarget, cloudP);
            Current = value;
            _automatic = automatic;
        }
    }
}
