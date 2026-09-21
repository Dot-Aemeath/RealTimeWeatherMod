using ChillWithYou.EnvSync.Models;

namespace ChillWithYou.EnvSync.Core
{
    /// <summary>
    /// The environments this mod drives, independent of the game assembly's
    /// EnvironmentType. Keeping the decision in mod-owned terms is what lets it be
    /// tested without loading Unity or Bulbul; ModEnvironmentMap converts at the edge.
    /// </summary>
    internal enum ModEnvironment
    {
        Day,
        Sunset,
        Night,
        Cloudy,
        LightRain,
        HeavyRain,
        ThunderRain,
        Snow
    }

    internal struct EnvironmentDecisionResult
    {
        internal EnvironmentDecisionResult(ModEnvironment baseEnvironment, ModEnvironment? scenery)
        {
            Base = baseEnvironment;
            Scenery = scenery;
        }

        internal ModEnvironment Base { get; }
        internal ModEnvironment? Scenery { get; }
    }

    /// <summary>
    /// Single source of truth for "what should the environment be right now".
    /// Regular sync, startup sync and the envsync_debug expectation all resolve
    /// through here, so the debug tool can never validate against a stale copy of
    /// the rule it is supposed to be checking.
    /// </summary>
    internal static class EnvironmentDecision
    {
        internal static EnvironmentDecisionResult Resolve(
            SyncPolicySnapshot policy,
            EnvironmentInputSnapshot inputs)
        {
            WeatherInfo weather = inputs == null ? null : inputs.Weather;
            DebugTimeSegment segment = inputs == null
                ? DebugTimeSegment.Night
                : inputs.TimeSegment;

            return new EnvironmentDecisionResult(
                ResolveBase(policy, segment, weather),
                ResolveScenery(policy, weather));
        }

        private static ModEnvironment ResolveBase(
            SyncPolicySnapshot policy,
            DebugTimeSegment segment,
            WeatherInfo weather)
        {
            ModEnvironment target = ResolveTimeBase(segment);
            if (policy != null &&
                policy.CanApplyCloudyOverride &&
                weather != null &&
                IsBadWeather(weather.Code) &&
                target != ModEnvironment.Night)
            {
                return ModEnvironment.Cloudy;
            }

            return target;
        }

        private static ModEnvironment? ResolveScenery(
            SyncPolicySnapshot policy,
            WeatherInfo weather)
        {
            if (policy == null || !policy.CanControlWeather || weather == null)
            {
                return null;
            }

            return ResolveSceneryCode(weather.Code);
        }

        private static ModEnvironment ResolveTimeBase(DebugTimeSegment segment)
        {
            switch (segment)
            {
                case DebugTimeSegment.Day:
                    return ModEnvironment.Day;
                case DebugTimeSegment.Sunset:
                    return ModEnvironment.Sunset;
                default:
                    return ModEnvironment.Night;
            }
        }

        /// <summary>
        /// Seniverse-normalized code space; OpenMeteo codes are mapped into it
        /// upstream by OpenMeteoWeatherMapper.
        /// </summary>
        internal static ModEnvironment? ResolveSceneryCode(int code)
        {
            if (code >= 20 && code <= 25) return ModEnvironment.Snow;
            if (code == 11 || code == 12 || (code >= 16 && code <= 18)) return ModEnvironment.ThunderRain;
            if (code == 10 || code == 14 || code == 15) return ModEnvironment.HeavyRain;
            if (code == 13 || code == 19) return ModEnvironment.LightRain;
            return null;
        }

        private static bool IsBadWeather(int code)
        {
            if (code == 10 || code == 13 || code == 21 || code == 22)
            {
                return false;
            }

            if (code == 4)
            {
                return true;
            }

            if (code >= 7 && code <= 31)
            {
                return true;
            }

            if (code >= 34 && code <= 36)
            {
                return true;
            }

            return false;
        }
    }
}
