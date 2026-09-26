using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Bulbul;
using DistantLands.Cozy;
using DistantLands.Cozy.Data;
using HarmonyLib;
using UnityEngine;

namespace ChillWithYou.EnvSync.Core
{
    /// <summary>Cloud FX are read from native assets; only the active sphere is overridden per draw.</summary>
    internal sealed class TimeTransitionClouds
    {
        private readonly CloudFX[] _clouds;
        private readonly FilterFX _filter;
        private readonly Dictionary<FieldInfo, object> _originals = new Dictionary<FieldInfo, object>();
        private readonly CozyWeather _sphere;

        internal TimeTransitionClouds(MyCozyWeatherService service, CozyWeather sphere)
        {
            _sphere = sphere;
            var pairs = (MyCozyWeatherService.WeatherPair[])AccessTools.Field(typeof(MyCozyWeatherService), "_weatherProfiles").GetValue(service);
            var profile = pairs.FirstOrDefault(p => p.WeatherType.ToString() == "Cloudy")?.WeatherProf;
            if (profile == null) throw new InvalidOperationException("缺少多云天气预设");
            _clouds = profile.FX.OfType<CloudFX>().ToArray();
            _filter = profile.FX.OfType<FilterFX>().FirstOrDefault();
            if (_clouds.Length == 0 || _filter == null) throw new InvalidOperationException("多云预设缺少云层或光照过滤参数");
        }

        internal void Apply(float weight, TimeBlend time, WeatherVisual weather)
        {
            if (weight <= 0) return;
            Blend("cumulus", Mathf.Clamp(_clouds.Sum(c => (float)c.cumulusCoverage * c.transitionTimeModifier.Evaluate(1)), 0, 2), weight);
            Blend("cirrus", Mathf.Clamp(_clouds.Sum(c => (float)c.cirrusCoverage * c.transitionTimeModifier.Evaluate(1)), 0, 2), weight);
            Blend("altocumulus", Mathf.Clamp(_clouds.Sum(c => (float)c.altocumulusCoverage * c.transitionTimeModifier.Evaluate(1)), 0, 2), weight);
            Blend("cirrostratus", Mathf.Clamp(_clouds.Sum(c => (float)c.cirrostratusCoverage * c.transitionTimeModifier.Evaluate(1)), 0, 2), weight);
            Blend("chemtrails", Mathf.Clamp(_clouds.Sum(c => (float)c.chemtrailCoverage * c.transitionTimeModifier.Evaluate(1)), 0, 2), weight);
            Blend("nimbus", Mathf.Clamp(_clouds.Sum(c => (float)c.nimbusCoverage * c.transitionTimeModifier.Evaluate(1)), 0, 2), weight);
            Blend("nimbusHeightEffect", Mathf.Clamp01(_clouds.Sum(c => (float)c.nimbusHeightEffect * c.transitionTimeModifier.Evaluate(1))), weight);
            Blend("nimbusVariation", Mathf.Clamp01(_clouds.Sum(c => (float)c.nimbusVariation * c.transitionTimeModifier.Evaluate(1))), weight);
            foreach (var cloud in _clouds)
            {
                float w = weight * cloud.transitionTimeModifier.Evaluate(1);
                if (cloud.borderHeight) Blend("borderHeight", cloud.borderHeight, w);
                if (cloud.borderEffect) Blend("borderEffect", cloud.borderEffect, w);
                if (cloud.borderVariation) Blend("borderVariation", cloud.borderVariation, w);
            }
            // Preserve the time palette. Native Cloudy's full filter is a daytime
            // treatment; reduced evening/night strength retains warm dusk and dark skies.
            float overlay = 1 - time.Weight(ModEnvironment.Cloudy);
            float tint = (time.Weight(ModEnvironment.Cloudy) + overlay * (.6f * weather.Dimming + .4f * weather.Fog)) *
                (time.Weight(ModEnvironment.Day) + time.Weight(ModEnvironment.Cloudy) + .6f * time.Weight(ModEnvironment.Sunset) + .35f * time.Weight(ModEnvironment.Night));
            Blend("filterSaturation", _filter.filterSaturation, tint);
            Blend("filterValue", _filter.filterValue, tint);
            BlendColor("filterColor", _filter.filterColor, tint);
            BlendColor("sunFilter", _filter.sunFilter, tint);
            BlendColor("cloudFilter", _filter.cloudFilter, tint);
            float dimming = weather.Dimming * overlay;
            BlendColor("sunColor", _sphere.sunColor * (1 - .85f * dimming), 1);
            BlendColor("sunlightColor", _sphere.sunlightColor * (1 - .75f * dimming), 1);
            BlendColor("sunFlareColor", _sphere.sunFlareColor * (1 - .7f * dimming), 1);
            BlendColor("fogFlareColor", _sphere.fogFlareColor * (1 - .6f * dimming), 1);
            // Ordinary overcast changes illumination without adding low visibility.
            // Fog/haze grades affect distant scenery through COZY's existing fog.
            Blend("fogDensity", _sphere.fogDensity * (1 + .75f * weather.Fog * overlay), 1);
        }

        private void Blend(string name, float target, float weight)
        {
            var field = typeof(CozyWeather).GetField(name);
            object current = field.GetValue(_sphere);
            if (!_originals.ContainsKey(field)) _originals.Add(field, current);
            field.SetValue(_sphere, Mathf.Lerp((float)current, target, weight));
        }

        private void BlendColor(string name, Color target, float weight)
        {
            var field = typeof(CozyWeather).GetField(name);
            object current = field.GetValue(_sphere);
            if (!_originals.ContainsKey(field)) _originals.Add(field, current);
            field.SetValue(_sphere, Color.Lerp((Color)current, target, weight));
        }

        internal void Restore()
        {
            if (_sphere != null) foreach (var pair in _originals) pair.Key.SetValue(_sphere, pair.Value);
            _originals.Clear();
        }
    }
}
