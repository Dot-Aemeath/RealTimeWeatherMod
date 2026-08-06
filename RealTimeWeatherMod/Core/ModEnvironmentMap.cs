using System;
using Bulbul;

namespace ChillWithYou.EnvSync.Core
{
    /// <summary>
    /// The only place the mod's own ModEnvironment meets the game's EnvironmentType.
    /// Keeping the conversion here is what allows EnvironmentDecision to stay free of
    /// the Bulbul assembly and therefore testable.
    /// </summary>
    internal static class ModEnvironmentMap
    {
        internal static readonly EnvironmentType[] BaseEnvironments =
        {
            EnvironmentType.Day,
            EnvironmentType.Sunset,
            EnvironmentType.Night,
            EnvironmentType.Cloudy
        };

        internal static readonly EnvironmentType[] SceneryEnvironments =
        {
            EnvironmentType.ThunderRain,
            EnvironmentType.HeavyRain,
            EnvironmentType.LightRain,
            EnvironmentType.Snow
        };

        /// <summary>
        /// Base plus scenery, in the order status reporting expects to probe them.
        /// </summary>
        internal static readonly EnvironmentType[] MainEnvironments =
        {
            EnvironmentType.Day,
            EnvironmentType.Sunset,
            EnvironmentType.Night,
            EnvironmentType.Cloudy,
            EnvironmentType.LightRain,
            EnvironmentType.HeavyRain,
            EnvironmentType.ThunderRain,
            EnvironmentType.Snow
        };

        internal static EnvironmentType ToGame(ModEnvironment environment)
        {
            switch (environment)
            {
                case ModEnvironment.Day: return EnvironmentType.Day;
                case ModEnvironment.Sunset: return EnvironmentType.Sunset;
                case ModEnvironment.Night: return EnvironmentType.Night;
                case ModEnvironment.Cloudy: return EnvironmentType.Cloudy;
                case ModEnvironment.LightRain: return EnvironmentType.LightRain;
                case ModEnvironment.HeavyRain: return EnvironmentType.HeavyRain;
                case ModEnvironment.ThunderRain: return EnvironmentType.ThunderRain;
                case ModEnvironment.Snow: return EnvironmentType.Snow;
                default:
                    throw new ArgumentOutOfRangeException(nameof(environment));
            }
        }

        internal static EnvironmentType? ToGame(ModEnvironment? environment)
        {
            return environment.HasValue ? ToGame(environment.Value) : (EnvironmentType?)null;
        }
    }
}
