using System;
using System.Collections.Generic;
using System.Linq;
using Bulbul;
using DistantLands.Cozy;
using HarmonyLib;
using MagicLightmapSwitcher;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace ChillWithYou.EnvSync.Core
{
    /// <summary>
    /// Owns only the four time roots and their presentation resources. Capability checks
    /// finish before activating roots or replacing a material. Source assets stay immutable.
    /// </summary>
    internal sealed class TimeTransitionRendering : IDisposable
    {
        internal sealed class NotReadyException : Exception
        {
            internal NotReadyException(string message) : base(message) { }
        }

        private sealed class LightState
        {
            internal Light Light;
            internal int Preset;
            internal float Intensity;
            internal bool Enabled;
            internal Color Color;
            internal Quaternion Rotation;
            internal LightShadows Shadows;
        }
        private sealed class VolumeState
        {
            internal Volume Volume;
            internal int Preset;
            internal float Weight;
            internal VolumeProfile Profile;
            internal bool HadInstance;
            internal VolumeProfile Effective;
        }
        private sealed class Surface
        {
            internal readonly Renderer[] Renderers = new Renderer[4];
            internal readonly Material[][] Materials = new Material[4][];
            internal Material[] Working;
            internal int Canonical;
            internal bool FadeOnly;
            internal readonly Material[][] FadeWorking = new Material[4][];
        }

        private readonly GameObject[] _roots = new GameObject[4];
        private readonly List<LightState> _lights = new List<LightState>();
        private readonly List<VolumeState> _volumes = new List<VolumeState>();
        private VolumeProfile _workingVolume;
        private readonly TimeTransitionFlares _flares = new TimeTransitionFlares();
        private readonly List<Surface> _surfaces = new List<Surface>();
        private readonly Dictionary<Renderer, bool> _rendererEnabled = new Dictionary<Renderer, bool>();
        private readonly List<Tuple<Material, bool, Color>> _emission = new List<Tuple<Material, bool, Color>>();
        private readonly MyCozyWeatherService _cozy;
        private readonly TimeTransitionAtmosphere _atmosphere;
        private readonly RuntimeAPI _lightmaps = new RuntimeAPI();
        private MagicLightmapSwitcher.MagicLightmapSwitcher _switcher;
        private StoredLightingScenario _pair;
        private ModEnvironment _pairFrom;
        private ModEnvironment _pairTo;
        private bool _disposed;
        private ModEnvironment _logical;
        private ModEnvironment? _settled;

        internal EnvironmentUI EnvironmentUi { get; }
        internal WindowViewService WindowService { get; }
        internal StoredLightingScenario OriginalScenario { get; }
        internal bool IsValid => !_disposed && EnvironmentUi != null && WindowService != null;
        internal bool IsBlending { get; private set; }

        internal static TimeTransitionRendering Create(EnvironmentUI ui, WindowViewService windows)
        {
            var cozy = (MyCozyWeatherService)AccessTools.Field(typeof(WindowViewService), "_cozyWeatherService").GetValue(windows);
            if (cozy == null || !cozy.IsReady) return null;
            return new TimeTransitionRendering(ui, windows, cozy);
        }

        private TimeTransitionRendering(EnvironmentUI ui, WindowViewService windows, MyCozyWeatherService cozy)
        {
            EnvironmentUi = ui;
            WindowService = windows;
            _cozy = cozy;
            OriginalScenario = (StoredLightingScenario)AccessTools.Field(typeof(EnvironmentUI), "lightingScenario").GetValue(ui);
            string[] names = { "day", "sunset", "night", "cloudy" };
            var presets = new CozyWeather[4];
            for (int i = 0; i < 4; i++)
            {
                _roots[i] = (GameObject)AccessTools.Field(typeof(WindowViewService), "_" + names[i]).GetValue(windows);
                presets[i] = (CozyWeather)AccessTools.Field(typeof(MyCozyWeatherService), "_" + names[i] + "CozyWeather").GetValue(cozy);
                if (_roots[i] == null) throw new InvalidOperationException("缺少时段对象: " + names[i]);
            }
            _atmosphere = new TimeTransitionAtmosphere(cozy, presets);
            if (presets.Any(p => p == _atmosphere.Sphere))
                throw new InvalidOperationException("活动 COZY 实例与时段模板重叠");
            InspectLightmaps();
            InspectRoots(presets);
            foreach (var field in new[] { "_jetAdjuster", "_ferrisWheelAdjuster" })
            {
                object adjuster = AccessTools.Field(typeof(WindowViewService), field).GetValue(windows);
                if (adjuster == null) continue;
                var material = AccessTools.Field(adjuster.GetType(), "_material").GetValue(adjuster) as Material;
                if (material != null && material.HasProperty("_EmissionColor"))
                    _emission.Add(Tuple.Create(material, field == "_jetAdjuster", material.GetColor("_EmissionColor")));
            }
            InspectVolumes();
        }

        private void InspectLightmaps()
        {
            if (OriginalScenario == null) return;
            _switcher = _lightmaps.GetSwitcherSource(OriginalScenario.targetScene);
            if (_switcher == null || !_switcher.storedDataUpdated)
                throw new NotReadyException("光照贴图切换器尚未就绪");
            if (_switcher.useTextureArrays)
                throw new InvalidOperationException("MLS 纹理数组索引不支持临时两端场景");
            var properties = _switcher.systemProperties;
            if (properties == null || properties.useSwitchingOnly ||
                (properties.universalRPActive && !properties.universalRPPatched) ||
                (properties.highDefinitionRPActive && !properties.highDefinitionRPPatched))
                throw new InvalidOperationException("当前渲染管线未启用 MLS 光照混合");
            if (OriginalScenario.blendableLightmaps.Count < 4)
                throw new InvalidOperationException("光照场景不足四个时段");
            int? renderers = null;
            foreach (var item in OriginalScenario.blendableLightmaps.Take(4))
            {
                var data = item?.lightingData;
                if (data?.sceneLightingData?.lightmapsLight == null || data.sceneLightingData.lightmapsLight.Any(t => t == null))
                    throw new InvalidOperationException("光照贴图资源不完整");
                if (renderers.HasValue && renderers.Value != data.rendererDataDeserialized.Count)
                    throw new InvalidOperationException("时段烘焙对象数量不一致");
                renderers = data.rendererDataDeserialized.Count;
            }
        }

        private static bool ActiveWithin(Transform transform, Transform root)
        {
            for (var t = transform; t != root && t != null; t = t.parent)
                if (!t.gameObject.activeSelf) return false;
            return true;
        }

        private void InspectRoots(CozyWeather[] templates)
        {
            var groups = new Dictionary<string, Surface>();
            for (int i = 0; i < 4; i++)
            {
                Transform root = _roots[i].transform;
                if (templates[i].gameObject.activeSelf && ActiveWithin(templates[i].transform, root))
                    throw new InvalidOperationException("时段根对象含活动 COZY 模板，无法安全同时启用");
                foreach (var light in _roots[i].GetComponentsInChildren<Light>(true))
                    if (ActiveWithin(light.transform, root) && !light.transform.IsChildOf(templates[i].transform))
                        _lights.Add(new LightState { Light = light, Preset = i, Intensity = light.intensity, Enabled = light.enabled, Color = light.color, Rotation = light.transform.rotation, Shadows = light.shadows });
                foreach (var volume in _roots[i].GetComponentsInChildren<Volume>(true))
                    if (volume.enabled && ActiveWithin(volume.transform, root) && !volume.transform.IsChildOf(templates[i].transform))
                        _volumes.Add(new VolumeState { Volume = volume, Preset = i, Weight = volume.weight,
                            HadInstance = volume.HasInstantiatedProfile(), Profile = volume.HasInstantiatedProfile() ? volume.profile : volume.sharedProfile });
                if (_roots[i].GetComponentsInChildren<ReflectionProbe>(true).Any(p => p.enabled && ActiveWithin(p.transform, root)))
                    throw new InvalidOperationException("时段根对象含独立活动反射探针");
                if (_roots[i].GetComponentsInChildren<Renderer>(true).Any(r => !(r is MeshRenderer) && r.enabled &&
                    ActiveWithin(r.transform, root) && !r.transform.IsChildOf(templates[i].transform)))
                    throw new InvalidOperationException("时段根对象含额外粒子或动态网格");
                foreach (var renderer in _roots[i].GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!renderer.enabled || !ActiveWithin(renderer.transform, root) || renderer.transform.IsChildOf(templates[i].transform)) continue;
                    string key = Path(renderer.transform, root);
                    if (!groups.TryGetValue(key, out var group)) groups.Add(key, group = new Surface());
                    if (group.Renderers[i] != null) throw new InvalidOperationException("时段网格路径重复: " + key);
                    group.Renderers[i] = renderer;
                    group.Materials[i] = renderer.sharedMaterials;
                    _rendererEnabled.Add(renderer, renderer.enabled);
                }
            }
            foreach (var group in groups.Values)
            {
                int first = Array.FindIndex(group.Renderers, r => r != null);
                group.Canonical = first;
                if (group.Renderers.Any(r => r == null))
                {
                    foreach (var materials in group.Materials.Where(m => m != null))
                        foreach (var material in materials)
                            if (material == null || material.renderQueue < 3000 || AlphaProperty(material) == null)
                                throw new InvalidOperationException("时段独有网格不支持透明度混合: " + group.Renderers[first].name);
                    group.FadeOnly = true;
                    _surfaces.Add(group);
                    continue;
                }
                for (int i = 0; i < 4; i++)
                {
                    if (group.Renderers[i] == null)
                        throw new InvalidOperationException("时段包含独有网格: " + group.Renderers[first].name);
                    var a = group.Renderers[first];
                    var b = group.Renderers[i];
                    if (a.GetComponent<MeshFilter>()?.sharedMesh != b.GetComponent<MeshFilter>()?.sharedMesh ||
                        Vector3.Distance(a.transform.position, b.transform.position) > 0.001f ||
                        Quaternion.Angle(a.transform.rotation, b.transform.rotation) > 0.01f ||
                        Vector3.Distance(a.transform.lossyScale, b.transform.lossyScale) > 0.001f ||
                        group.Materials[first].Length != group.Materials[i].Length)
                        throw new InvalidOperationException("时段网格无法按同一表面混合: " + a.name);
                    if (a.lightmapIndex != b.lightmapIndex || a.lightmapScaleOffset != b.lightmapScaleOffset)
                        throw new InvalidOperationException("时段表面的烘焙索引或 UV 不同: " + a.name);
                    for (int m = 0; m < group.Materials[first].Length; m++)
                        CheckMaterial(group.Materials[first][m], group.Materials[i][m]);
                }
                _surfaces.Add(group);
            }
            var directional = _lights.Where(l => l.Light.type == LightType.Directional && l.Enabled).ToArray();
            if (directional.GroupBy(l => l.Preset).Any(g => g.Count() > 1))
                throw new InvalidOperationException("时段含多盏方向光，无法保证主阴影连续");
            if (directional.Length > 0 && directional.Any(l => l.Light.cullingMask != directional[0].Light.cullingMask || l.Light.cookie != directional[0].Light.cookie))
                throw new InvalidOperationException("时段主光的遮罩或 Cookie 不一致");
        }

        private void InspectVolumes()
        {
            if (_volumes.Count == 0) return;
            if (_volumes.Count != 4 || _volumes.Select(v => v.Preset).Distinct().Count() != 4)
                throw new InvalidOperationException("时段需要一一对应的后处理 Volume");
            _volumes.Sort((a, b) => a.Preset.CompareTo(b.Preset));
            var first = _volumes[0];
            foreach (var volume in _volumes)
            {
                if (!volume.Volume.isGlobal || volume.Profile == null || first.Profile == null ||
                    volume.Volume.priority != first.Volume.priority || volume.Profile.components.Count != first.Profile.components.Count)
                    throw new InvalidOperationException("时段后处理结构、范围或优先级不同");
                foreach (var source in first.Profile.components)
                {
                    var target = volume.Profile.components.FirstOrDefault(c => c.GetType() == source.GetType());
                    if (target == null || source.active != target.active || source.parameters.Count != target.parameters.Count)
                        throw new InvalidOperationException("时段后处理组件不同: " + source.name);
                }
            }
            // Resolve unset parameters through the native defaults and lower volume stack.
            // Serialized values of unchecked parameters are not their rendered values.
            var defaults = VolumeManager.instance.CreateStack();
            try
            {
                var camera = Camera.main;
                var cameraData = camera != null ? camera.GetComponent<UniversalAdditionalCameraData>() : null;
                int mask = cameraData != null ? (int)cameraData.volumeLayerMask : 1;
                var lower = VolumeManager.instance.GetVolumes(mask).Where(v => v.isActiveAndEnabled &&
                    v.weight > 0 && v.priority <= first.Volume.priority && !_volumes.Any(t => t.Volume == v)).ToArray();
                foreach (var volume in _volumes)
                {
                    volume.Effective = ScriptableObject.CreateInstance<VolumeProfile>();
                    volume.Effective.hideFlags = HideFlags.HideAndDontSave;
                    foreach (var source in volume.Profile.components)
                    {
                        var resolved = Object.Instantiate(defaults.GetComponent(source.GetType()));
                        resolved.hideFlags = HideFlags.HideAndDontSave;
                        volume.Effective.components.Add(resolved);
                        foreach (var background in lower)
                        {
                            var profile = background.HasInstantiatedProfile() ? background.profile : background.sharedProfile;
                            var component = profile?.components.FirstOrDefault(c => c.GetType() == source.GetType());
                            if (component != null && component.active)
                                component.Override(resolved, VolumeInfluence(background, camera));
                        }
                        if (source.active) source.Override(resolved, Mathf.Clamp01(volume.Weight));
                        resolved.active = source.active;
                        resolved.SetAllOverridesTo(true);
                    }
                }
                foreach (var volume in _volumes)
                foreach (var source in first.Effective.components)
                {
                    var target = volume.Effective.components.First(c => c.GetType() == source.GetType());
                    for (int p = 0; p < source.parameters.Count; p++)
                    {
                        var a = source.parameters[p];
                        var b = target.parameters[p];
                        if (a.GetType() != b.GetType())
                            throw new InvalidOperationException("时段后处理覆盖项不同: " + source.name);
                        // Beautify bypasses these switches while automatic optimization is on.
                        // Only normalize these proven inactive branches, not arbitrary booleans.
                        if (IsInactiveBeautifySwitch(source, target, a))
                        {
                            b.SetValue(a);
                            continue;
                        }
                        var property = a.GetType().GetProperty("value");
                        object av = property?.GetValue(a, null);
                        object bv = property?.GetValue(b, null);
                        bool numeric = av is float || av is Color || av is Vector2 || av is Vector3 || av is Vector4;
                        if ((!numeric || a.GetType().Name.StartsWith("NoInterp", StringComparison.Ordinal)) && !Equals(av, bv))
                            throw new InvalidOperationException("时段后处理包含不同的离散参数: " + source.name + "/" + p + "/" + (ModEnvironment)volume.Preset);
                    }
                }
            }
            catch
            {
                foreach (var volume in _volumes) { DestroyProfile(volume.Effective); volume.Effective = null; }
                throw;
            }
            finally { VolumeManager.instance.DestroyStack(defaults); }
        }

        private static bool IsInactiveBeautifySwitch(VolumeComponent a, VolumeComponent b, VolumeParameter parameter)
        {
            if (a.GetType().FullName != "Beautify.Universal.Beautify") return false;
            var auto = a.GetType().GetField("optimizeBuildBeautifyAuto");
            if (!((BoolParameter)auto.GetValue(a)).value || !((BoolParameter)auto.GetValue(b)).value) return false;
            return new[] { "stripBeautifyEdgeAA", "stripBeautifyChromaticAberration" }
                .Any(name => ReferenceEquals(a.GetType().GetField(name).GetValue(a), parameter));
        }

        private static float VolumeInfluence(Volume volume, Camera camera)
        {
            float weight = Mathf.Clamp01(volume.weight);
            if (volume.isGlobal) return weight;
            if (camera == null) return 0;
            float distance = float.PositiveInfinity;
            foreach (var collider in volume.GetComponents<Collider>())
                if (collider.enabled) distance = Mathf.Min(distance,
                    (collider.ClosestPoint(camera.transform.position) - camera.transform.position).sqrMagnitude);
            float range = volume.blendDistance * volume.blendDistance;
            if (distance > range) return 0;
            return weight * (range > 0 ? 1 - distance / range : 1);
        }

        private static void DestroyProfile(VolumeProfile profile)
        {
            if (profile == null) return;
            foreach (var component in profile.components) Object.Destroy(component);
            profile.components.Clear();
            Object.Destroy(profile);
        }

        private void ApplyVolumes(TimeBlend blend)
        {
            if (_volumes.Count == 0) return;
            var from = _volumes[(int)blend.From];
            var to = _volumes[(int)blend.To];
            if (_workingVolume == null)
            {
                _workingVolume = ScriptableObject.CreateInstance<VolumeProfile>();
                _workingVolume.hideFlags = HideFlags.HideAndDontSave;
                foreach (var component in _volumes[0].Effective.components)
                {
                    var copy = Object.Instantiate(component);
                    copy.hideFlags = HideFlags.HideAndDontSave;
                    _workingVolume.components.Add(copy);
                }
                _volumes[0].Volume.profile = _workingVolume;
            }
            foreach (var working in _workingVolume.components)
            {
                var a = from.Effective.components.First(c => c.GetType() == working.GetType());
                var b = to.Effective.components.First(c => c.GetType() == working.GetType());
                for (int p = 0; p < working.parameters.Count; p++)
                {
                    working.parameters[p].SetValue(a.parameters[p]);
                    working.parameters[p].overrideState = a.parameters[p].overrideState;
                }
                b.Override(working, blend.Amount);
                if (a is Beautify.Universal.Beautify flareFrom && b is Beautify.Universal.Beautify flareTo)
                    _flares.DetailWeight = Mathf.Lerp(flareFrom.anamorphicFlaresIntensity.value > 0 ? 1 : 0,
                        flareTo.anamorphicFlaresIntensity.value > 0 ? 1 : 0, blend.Amount);
            }
            foreach (var volume in _volumes) volume.Volume.weight = 0;
            _volumes[0].Volume.weight = 1;
        }

        private static string AlphaProperty(Material material) =>
            material.HasProperty("_BaseColor") ? "_BaseColor" : material.HasProperty("_Color") ? "_Color" : null;

        private static string Path(Transform transform, Transform root)
        {
            string value = transform.name;
            while (transform.parent != null && transform.parent != root)
            {
                transform = transform.parent;
                value = transform.name + "/" + value;
            }
            return value;
        }

        private static void CheckMaterial(Material a, Material b)
        {
            if (a == null || b == null || a.shader != b.shader || !a.shaderKeywords.OrderBy(k => k).SequenceEqual(b.shaderKeywords.OrderBy(k => k)))
                throw new InvalidOperationException("时段表面 Shader 或关键字不同");
            foreach (string property in a.GetTexturePropertyNames())
                if (!CanFadeEmissionMap(a, b, property) &&
                    (a.GetTexture(property) != b.GetTexture(property) || a.GetTextureScale(property) != b.GetTextureScale(property) || a.GetTextureOffset(property) != b.GetTextureOffset(property)))
                    throw new InvalidOperationException("时段表面贴图不同: " + a.name + "/" + property);
            foreach (string property in new[] { "_SrcBlend", "_DstBlend", "_ZWrite", "_ZTest", "_Cull" })
                if (a.HasProperty(property) && a.GetFloat(property) != b.GetFloat(property))
                    throw new InvalidOperationException("时段表面渲染状态不同: " + a.name + "/" + property);
        }

        private static bool CanFadeEmissionMap(Material a, Material b, string property) =>
            property == "_EmissionMap" && a.HasProperty("_UseEmission") && b.HasProperty("_UseEmission") &&
            a.HasProperty("_EmissionColor") && b.HasProperty("_EmissionColor") &&
            (a.GetFloat("_UseEmission") == 0 || b.GetFloat("_UseEmission") == 0);

        private static void BlendMaterial(Material working, Material from, Material to, float amount)
        {
            working.Lerp(from, to, amount);
            // The game's lilToon desk lamp uses an unlit preset with emission disabled
            // and a lit preset with an emission texture. Keep that texture and fade its
            // contribution; interpolating the enable flag would turn it on at once.
            if (!CanFadeEmissionMap(from, to, "_EmissionMap")) return;
            bool fromLit = from.GetFloat("_UseEmission") != 0;
            bool toLit = to.GetFloat("_UseEmission") != 0;
            var lit = fromLit ? from : to;
            working.SetTexture("_EmissionMap", lit.GetTexture("_EmissionMap"));
            working.SetTextureScale("_EmissionMap", lit.GetTextureScale("_EmissionMap"));
            working.SetTextureOffset("_EmissionMap", lit.GetTextureOffset("_EmissionMap"));
            working.SetFloat("_UseEmission", fromLit || toLit ? 1 : 0);
            working.SetColor("_EmissionColor", Color.Lerp(fromLit ? from.GetColor("_EmissionColor") : Color.clear,
                toLit ? to.GetColor("_EmissionColor") : Color.clear, amount));
        }

        internal void Apply(TimeBlend blend, ModEnvironment logical)
        {
            if (!IsValid) return;
            _logical = logical;
            if (blend.IsSingle)
            {
                Settle(blend.Dominant);
                return;
            }
            _settled = null;
            IsBlending = true;
            ApplySmoothShadows();
            _atmosphere.SetBlend(blend);
            // Only the canonical opaque surface renders; original materials are never edited.
            foreach (var surface in _surfaces)
            {
                if (surface.FadeOnly)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        if (surface.Renderers[i] == null) continue;
                        if (surface.FadeWorking[i] == null)
                            surface.FadeWorking[i] = surface.Materials[i].Select(m => new Material(m) { hideFlags = HideFlags.HideAndDontSave }).ToArray();
                        float weight = blend.Weight((ModEnvironment)i);
                        for (int m = 0; m < surface.FadeWorking[i].Length; m++)
                        {
                            string property = AlphaProperty(surface.FadeWorking[i][m]);
                            Color color = surface.Materials[i][m].GetColor(property);
                            color.a *= weight;
                            surface.FadeWorking[i][m].SetColor(property, color);
                        }
                        surface.Renderers[i].sharedMaterials = surface.FadeWorking[i];
                        surface.Renderers[i].enabled = weight > 0;
                    }
                    continue;
                }
                if (surface.Working == null)
                    surface.Working = surface.Materials[surface.Canonical].Select(m => new Material(m) { hideFlags = HideFlags.HideAndDontSave }).ToArray();
                for (int i = 0; i < 4; i++) surface.Renderers[i].enabled = i == surface.Canonical;
                for (int m = 0; m < surface.Working.Length; m++)
                    BlendMaterial(surface.Working[m], surface.Materials[(int)blend.From][m], surface.Materials[(int)blend.To][m], blend.Amount);
                surface.Renderers[surface.Canonical].sharedMaterials = surface.Working;
            }
            foreach (var light in _lights)
            {
                light.Light.intensity = light.Intensity * blend.Weight((ModEnvironment)light.Preset);
                light.Light.enabled = light.Enabled && light.Light.intensity > 0;
            }
            var sun = _lights.FirstOrDefault(l => l.Enabled && l.Light.type == LightType.Directional);
            if (sun != null)
            {
                var from = _lights.FirstOrDefault(l => l.Preset == (int)blend.From && l.Enabled && l.Light.type == LightType.Directional);
                var to = _lights.FirstOrDefault(l => l.Preset == (int)blend.To && l.Enabled && l.Light.type == LightType.Directional);
                foreach (var light in _lights.Where(l => l.Light.type == LightType.Directional)) light.Light.enabled = false;
                sun.Light.intensity = Mathf.Lerp(from?.Intensity ?? 0, to?.Intensity ?? 0, blend.Amount);
                sun.Light.color = Color.Lerp(from?.Color ?? to?.Color ?? sun.Color, to?.Color ?? from?.Color ?? sun.Color, blend.Amount);
                sun.Light.transform.rotation = Quaternion.Slerp(from?.Rotation ?? to?.Rotation ?? sun.Rotation, to?.Rotation ?? from?.Rotation ?? sun.Rotation, blend.Amount);
                sun.Light.enabled = sun.Light.intensity > 0;
            }
            ApplyVolumes(blend);
            // Roots keep component ownership and transforms. Dormant descendants stay dormant.
            foreach (var root in _roots) root.SetActive(true);
            ApplyLightmaps(blend);
            ApplyEmission(blend);
        }

        private void ApplyLightmaps(TimeBlend blend)
        {
            if (OriginalScenario == null) return;
            if (_pair == null || _pairFrom != blend.From || _pairTo != blend.To)
            {
                ReleasePair();
                _pair = Object.Instantiate(OriginalScenario);
                _pair.hideFlags = HideFlags.HideAndDontSave;
                _pair.name = "RealTimeWeather time transition";
                _pairFrom = blend.From;
                _pairTo = blend.To;
                _pair.blendableLightmaps = new List<StoredLightingScenario.LightmapData>
                {
                    new StoredLightingScenario.LightmapData(0, 0, 0, Vector2.up, Vector2.up, OriginalScenario.blendableLightmaps[(int)blend.From].lightingData),
                    new StoredLightingScenario.LightmapData(1, 0, 1, Vector2.up, Vector2.up, OriginalScenario.blendableLightmaps[(int)blend.To].lightingData)
                };
                _pair.selfTestCompleted = false;
                string error = _pair.SelfTest();
                if (error.Length != 0) throw new InvalidOperationException(error);
            }
            _lightmaps.BlendLightmapsCustom(blend.Amount, _pair);
        }

        private void ApplyEmission(TimeBlend blend)
        {
            foreach (var item in _emission)
            {
                if (item.Item1 == null) continue;
                Color color = item.Item3;
                color.a = Mathf.Lerp(EmissionWeight(blend.From, item.Item2), EmissionWeight(blend.To, item.Item2), blend.Amount);
                item.Item1.SetColor("_EmissionColor", color);
            }
        }

        private static float EmissionWeight(ModEnvironment value, bool jet) =>
            value == ModEnvironment.Day ? 0 : value == ModEnvironment.Sunset ? (jet ? 0.6f : 0) : 1;

        private void Settle(ModEnvironment target)
        {
            if (_settled == target && !IsBlending) return;
            IsBlending = false;
            RestoreComponents();
            ApplySmoothShadows();
            for (int i = 0; i < 4; i++) if (_roots[i] != null) _roots[i].SetActive(i == (int)target);
            if (OriginalScenario != null) _lightmaps.SwitchLightmap((int)target, OriginalScenario);
            ReleasePair();
            if (_cozy != null)
            {
                _cozy.ChangeCozyWeather((WindowViewType)Enum.Parse(typeof(WindowViewType), target.ToString()));
                _atmosphere.RefreshNativeParameters();
            }
            ApplyEmission(TimeBlend.Single(target));
            _settled = target;
        }

        private void ApplySmoothShadows()
        {
            // Keep the same filter in stable night and while blending: switching back to
            // Hard at the endpoint would reintroduce a visible discontinuity.
            foreach (var light in _lights)
                if (light.Light != null && light.Light.type == LightType.Directional && light.Shadows != LightShadows.None)
                    light.Light.shadows = LightShadows.Soft;
        }

        private void RestoreComponents()
        {
            _atmosphere.RestoreTime(_atmosphere.Sphere);
            foreach (var light in _lights)
                if (light.Light != null) { light.Light.intensity = light.Intensity; light.Light.enabled = light.Enabled; light.Light.color = light.Color; light.Light.transform.rotation = light.Rotation; }
            foreach (var volume in _volumes)
                if (volume.Volume != null)
                {
                    volume.Volume.weight = volume.Weight;
                    if (_workingVolume != null) volume.Volume.profile = volume.HadInstance ? volume.Profile : null;
                }
            if (_workingVolume != null)
            {
                foreach (var component in _workingVolume.components) Object.Destroy(component);
                _workingVolume.components.Clear();
                Object.Destroy(_workingVolume);
                _workingVolume = null;
            }
            foreach (var surface in _surfaces)
            {
                for (int i = 0; i < 4; i++)
                    if (surface.Renderers[i] != null)
                    {
                        surface.Renderers[i].sharedMaterials = surface.Materials[i];
                        surface.Renderers[i].enabled = _rendererEnabled[surface.Renderers[i]];
                    }
                if (surface.Working != null)
                    foreach (var material in surface.Working) Object.Destroy(material);
                surface.Working = null;
                for (int i = 0; i < 4; i++)
                {
                    if (surface.FadeWorking[i] != null) foreach (var material in surface.FadeWorking[i]) Object.Destroy(material);
                    surface.FadeWorking[i] = null;
                }
            }
        }

        private void ReleasePair()
        {
            if (_pair == null) return;
            if (_switcher != null)
            {
                if (_switcher.currentLightmapScenario == _pair) _switcher.currentLightmapScenario = OriginalScenario;
                if (_switcher.lastLightmapScenario == _pair) _switcher.lastLightmapScenario = OriginalScenario;
                _switcher.blendingProcess = false;
            }
            Object.Destroy(_pair);
            _pair = null;
        }

        internal void ApplyAtmosphere(object sphere) { if (IsBlending) _atmosphere.Apply(sphere); }
        internal bool DrawFlares(CommandBuffer cmd, RenderTargetIdentifier source,
            RenderTargetIdentifier destination, Material material) =>
            IsBlending && _flares.Draw(cmd, source, destination, material);
        internal void RestoreAtmosphereTime(object sphere) => _atmosphere.RestoreTime(sphere);

        public void Dispose()
        {
            if (_disposed) return;
            try { if (IsBlending) Settle(_logical); else RestoreComponents(); }
            finally
            {
                foreach (var light in _lights) if (light.Light != null) light.Light.shadows = light.Shadows;
                foreach (var volume in _volumes) DestroyProfile(volume.Effective);
                ReleasePair();
                _disposed = true;
            }
        }
    }
}
