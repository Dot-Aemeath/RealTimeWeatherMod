namespace ChillWithYou.EnvSync.Core
{
    /// <summary>Independent artistic weights, not measured cloud cover or visibility.</summary>
    internal struct WeatherVisual
    {
        internal float Clouds;
        internal float Dimming;
        internal float Fog;

        internal WeatherVisual(float clouds, float dimming, float fog)
        {
            Clouds = clouds;
            Dimming = dimming;
            Fog = fog;
        }

        internal bool Equals(WeatherVisual other) =>
            Clouds == other.Clouds && Dimming == other.Dimming && Fog == other.Fog;

        internal static WeatherVisual Lerp(WeatherVisual from, WeatherVisual to, float p) =>
            new WeatherVisual(from.Clouds + (to.Clouds - from.Clouds) * p,
                from.Dimming + (to.Dimming - from.Dimming) * p,
                from.Fog + (to.Fog - from.Fog) * p);
    }
}
