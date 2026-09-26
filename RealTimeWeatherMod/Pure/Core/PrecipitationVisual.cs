using System;

namespace ChillWithYou.EnvSync.Core
{
    /// <summary>
    /// One rain scale shared by the three native rain windows. <see cref="LightAnchor"/>
    /// reproduces the native LightRain particles and 1 the native HeavyRain particles;
    /// the window chosen by <see cref="EnvironmentDecision"/> only selects the UI state.
    /// </summary>
    internal struct PrecipitationVisual
    {
        internal const float LightAnchor = .1f;

        internal float Rain;
        internal float Thunder;

        internal PrecipitationVisual(float rain, float thunder)
        {
            Rain = rain;
            Thunder = thunder;
        }

        /// <summary>0 at the native light window, 1 at the native heavy window.</summary>
        internal float HeavyWeight => Rain <= LightAnchor ? 0 : Math.Min(1, (Rain - LightAnchor) / (1 - LightAnchor));

        internal bool Equals(PrecipitationVisual other) => Rain == other.Rain && Thunder == other.Thunder;

        /// <summary>Emission reaches zero below the light anchor so rain can fade in and out.</summary>
        internal float EmissionRate(float lightRate, float heavyRate) => Rain <= LightAnchor
            ? lightRate * Math.Max(0, Rain) / LightAnchor
            : lightRate + (heavyRate - lightRate) * HeavyWeight;

        internal static PrecipitationVisual Lerp(PrecipitationVisual from, PrecipitationVisual to, float p) =>
            new PrecipitationVisual(from.Rain + (to.Rain - from.Rain) * p,
                from.Thunder + (to.Thunder - from.Thunder) * p);

        /// <summary>Seed from a natively active window, which carries its authored intensity.</summary>
        internal static PrecipitationVisual Native(ModEnvironment? window)
        {
            switch (window)
            {
                case ModEnvironment.LightRain: return new PrecipitationVisual(LightAnchor, 0);
                case ModEnvironment.HeavyRain: return new PrecipitationVisual(1, 0);
                case ModEnvironment.ThunderRain: return new PrecipitationVisual(1, 1);
                default: return default;
            }
        }

        /// <summary>
        /// Seniverse-normalized rain codes. Artistic grades, not measured rainfall;
        /// the native windows remain the endpoints of the scale.
        /// </summary>
        internal static PrecipitationVisual Resolve(int code)
        {
            switch (code)
            {
                case 13: return new PrecipitationVisual(LightAnchor, 0);
                case 19: return new PrecipitationVisual(.25f, 0);
                case 10: return new PrecipitationVisual(.3f, 0);
                case 14: return new PrecipitationVisual(.45f, 0);
                case 15: return new PrecipitationVisual(.75f, 0);
                case 11: return new PrecipitationVisual(.6f, 1);
                case 12: return new PrecipitationVisual(.7f, 1);
                case 16: return new PrecipitationVisual(.9f, .35f);
                case 17: return new PrecipitationVisual(1, .6f);
                case 18: return new PrecipitationVisual(1, .85f);
                default: return default;
            }
        }
    }

    /// <summary>Fades rain intensity independently of the time and cloud channels.</summary>
    internal sealed class PrecipitationPresentation
    {
        internal const double FadeSeconds = 6;

        internal PrecipitationVisual Current { get; private set; }
        internal PrecipitationVisual Target { get; private set; }
        private PrecipitationVisual _from;
        private double _start = double.NegativeInfinity;

        internal void Seed(PrecipitationVisual native)
        {
            Current = Target = _from = native;
            _start = double.NegativeInfinity;
        }

        internal void Tick(PrecipitationVisual target, double realtime)
        {
            if (!target.Equals(Target))
            {
                // Retarget from the displayed value, so reversals never jump.
                _from = Current;
                Target = target;
                _start = realtime;
            }
            float p = TimeTransitionSchedule.Smooth((float)((realtime - _start) / FadeSeconds));
            Current = p >= 1 ? Target : PrecipitationVisual.Lerp(_from, Target, p);
        }
    }
}
