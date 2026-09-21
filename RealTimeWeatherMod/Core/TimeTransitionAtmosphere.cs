using System;
using System.Collections.Generic;
using System.Reflection;
using Bulbul;
using DistantLands.Cozy;
using DistantLands.Cozy.Data;
using HarmonyLib;
using UnityEngine;

namespace ChillWithYou.EnvSync.Core
{
    internal sealed class TimeTransitionAtmosphere
    {
        private sealed class Parameter
        {
            internal FieldInfo Field;
            internal readonly object[] Values = new object[4];
            internal AtmosphereProfile.SRPFlare WorkingFlare;
            internal object OriginalFlare;
            internal bool FlareOverridden;
        }

        private readonly CozyWeather _sphere;
        private readonly List<Parameter> _parameters = new List<Parameter>();
        private readonly float[] _times = new float[4];
        private TimeBlend _blend;
        private CozyTimeModule _overriddenTime;
        private float _originalTime;

        internal CozyWeather Sphere => _sphere;

        internal TimeTransitionAtmosphere(MyCozyWeatherService service, CozyWeather[] presets)
        {
            _sphere = (CozyWeather)AccessTools.Field(typeof(MyCozyWeatherService), "_cozyWeather").GetValue(service);
            var pairs = (MyCozyWeatherService.AtmospherePair[])AccessTools.Field(typeof(MyCozyWeatherService), "_atmosphereProfiles").GetValue(service);
            var profiles = new AtmosphereProfile[4];
            for (int i = 0; i < 4; i++)
            {
                if (presets[i] == null || presets[i].timeModule == null)
                    throw new InvalidOperationException("COZY 时段模板缺少时间模块");
                string name = ((ModEnvironment)i).ToString();
                foreach (var pair in pairs)
                    if (pair.WeatherType.ToString() == name) profiles[i] = pair.AtomosphereProf;
                if (profiles[i] == null) throw new InvalidOperationException("缺少大气预设: " + name);
                float rawTime = presets[i].timeModule.currentTime;
                _times[i] = presets[i].timeModule.transit != null
                    ? presets[i].timeModule.transit.ModifyDayPercentage(rawTime) / 360f : rawTime;
            }

            foreach (var field in typeof(AtmosphereProfile).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var destination = typeof(CozyWeather).GetField(field.Name, BindingFlags.Public | BindingFlags.Instance);
                if (destination == null) continue;
                var parameter = new Parameter { Field = destination };
                for (int i = 0; i < 4; i++)
                {
                    object value = field.GetValue(profiles[i]);
                    if (value is VariableProperty variable)
                    {
                        float t = _sphere.usePhysicalSunHeight ? _times[i] : (float)presets[i].timeModule.currentTime;
                        value = destination.FieldType == typeof(Color)
                            ? (object)variable.GetColorValue(t) : variable.GetFloatValue(t);
                    }
                    else if (value is Overridable<float> overridable)
                    {
                        // COZY's fogStart fields use its implicit conversion, including
                        // the default when overrideValue is false, rather than a boxed float.
                        value = (float)overridable;
                    }
                    parameter.Values[i] = value;
                }
                Type type = destination.FieldType;
                if (type == typeof(float) || type == typeof(Color) || type == typeof(Vector2) || type == typeof(Vector3))
                    _parameters.Add(parameter);
                else if (type == typeof(AtmosphereProfile.SRPFlare))
                {
                    AtmosphereProfile.SRPFlare active = null;
                    foreach (var value in parameter.Values)
                    {
                        var flare = value as AtmosphereProfile.SRPFlare;
                        if (flare == null || flare.flare == null) continue;
                        if (active != null && (active.flare != flare.flare ||
                            active.allowOffscreen != flare.allowOffscreen || active.useOcclusion != flare.useOcclusion ||
                            !SameDiscreteValue(active.screenAttenuation, flare.screenAttenuation)))
                            throw new InvalidOperationException("大气光晕资源或遮挡设置不同: " + field.Name);
                        active = flare;
                    }
                    parameter.WorkingFlare = new AtmosphereProfile.SRPFlare();
                    _parameters.Add(parameter);
                }
                else
                {
                    // Discrete textures and shader modes cannot be made smooth by changing a float.
                    for (int i = 1; i < 4; i++)
                        if (!SameDiscreteValue(parameter.Values[0], parameter.Values[i]))
                            throw new InvalidOperationException("大气预设的离散资源不同: " + field.Name);
                }
            }
        }

        private static bool SameDiscreteValue(object a, object b)
        {
            if (Equals(a, b)) return true;
            if (a == null || b == null || a.GetType() != b.GetType() || a is UnityEngine.Object) return false;
            return a.GetType().IsClass && JsonUtility.ToJson(a) == JsonUtility.ToJson(b);
        }

        internal void SetBlend(TimeBlend blend) { _blend = blend; }

        internal void RefreshNativeParameters()
        {
            // COZY propagates the profile in Update, but uploads shader values in
            // LateUpdate. A profile switched between them needs propagation now.
            _sphere.atmosphereModule.PropogateVariables();
        }

        internal void Apply(object sphere)
        {
            if (!ReferenceEquals(sphere, _sphere)) return;
            int from = (int)_blend.From;
            int to = (int)_blend.To;
            float p = _blend.Amount;
            foreach (var parameter in _parameters)
            {
                object a = parameter.Values[from];
                object b = parameter.Values[to];
                object value;
                if (parameter.WorkingFlare != null)
                {
                    var af = a as AtmosphereProfile.SRPFlare;
                    var bf = b as AtmosphereProfile.SRPFlare;
                    var source = af?.flare != null ? af : bf?.flare != null ? bf : af ?? bf;
                    var working = parameter.WorkingFlare;
                    working.flare = source?.flare;
                    working.allowOffscreen = source?.allowOffscreen ?? false;
                    working.useOcclusion = source?.useOcclusion ?? false;
                    working.screenAttenuation = source?.screenAttenuation;
                    working.intensity = Mathf.Lerp(af?.flare != null ? af.intensity : 0,
                        bf?.flare != null ? bf.intensity : 0, p);
                    working.scale = Mathf.Lerp(af?.flare != null ? af.scale : source?.scale ?? 1,
                        bf?.flare != null ? bf.scale : source?.scale ?? 1, p);
                    working.occlusionRadius = Mathf.Lerp(af?.flare != null ? af.occlusionRadius : source?.occlusionRadius ?? 0,
                        bf?.flare != null ? bf.occlusionRadius : source?.occlusionRadius ?? 0, p);
                    if (!parameter.FlareOverridden)
                    {
                        parameter.OriginalFlare = parameter.Field.GetValue(_sphere);
                        parameter.FlareOverridden = true;
                    }
                    value = working;
                }
                else if (a is float af) value = Mathf.Lerp(af, (float)b, p);
                else if (a is Color ac) value = Color.Lerp(ac, (Color)b, p);
                else if (a is Vector2 a2) value = Vector2.Lerp(a2, (Vector2)b, p);
                else value = Vector3.Lerp((Vector3)a, (Vector3)b, p);
                parameter.Field.SetValue(_sphere, value);
            }
            if (_overriddenTime == null && _sphere.timeModule != null)
            {
                _overriddenTime = _sphere.timeModule;
                _originalTime = _overriddenTime.modifiedDayPercentage;
            }
            if (_overriddenTime != null)
                _overriddenTime.modifiedDayPercentage = Mathf.Repeat(
                    _times[from] + Mathf.DeltaAngle(_times[from] * 360, _times[to] * 360) / 360 * p, 1);
        }

        internal void RestoreTime(object sphere)
        {
            if (!ReferenceEquals(sphere, _sphere)) return;
            foreach (var parameter in _parameters)
                if (parameter.FlareOverridden)
                {
                    parameter.Field.SetValue(_sphere, parameter.OriginalFlare);
                    parameter.OriginalFlare = null;
                    parameter.FlareOverridden = false;
                }
            if (_overriddenTime == null) return;
            _overriddenTime.modifiedDayPercentage = _originalTime;
            _overriddenTime = null;
        }
    }
}
