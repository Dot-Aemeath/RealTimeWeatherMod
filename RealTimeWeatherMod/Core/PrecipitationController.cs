using System;
using Bulbul;
using HarmonyLib;
using UnityEngine;

namespace ChillWithYou.EnvSync.Core
{
    /// <summary>
    /// Drives the three native rain windows from one faded intensity. Window selection,
    /// icons, ambient sound and persistence stay with the game; this only scales the
    /// runtime particle modules of the window instances and restores their authored values.
    /// </summary>
    internal sealed class PrecipitationController
    {
        // Rain particles live for one second; wait for them before hiding the window.
        private const float DrainSeconds = 1.25f;

        private sealed class RainWindow
        {
            internal ModEnvironment Environment;
            internal GameObject Root;
            internal ParticleSystem Rain;
            internal ParticleSystem Thunder;
            internal float Rate;
            internal float Size;
            internal int MaxParticles;
            internal ParticleSystem.Burst[] Bursts;
            internal ParticleSystem.Burst[] WorkingBursts;
        }

        private readonly GameEnvironmentRuntime _runtime;
        private readonly PrecipitationPresentation _presentation = new PrecipitationPresentation();
        private WindowViewService _service;
        private WindowViewService _failedService;
        private RainWindow[] _windows;
        private RainWindow _light, _heavy, _thunder;
        private bool _seeded;
        private bool _applied;
        private ModEnvironment? _fadingOut;
        private double _drainUntil;

        internal PrecipitationController(GameEnvironmentRuntime runtime) { _runtime = runtime; }

        internal PrecipitationVisual Current => _presentation.Current;
        internal PrecipitationVisual Target => _presentation.Target;
        internal ModEnvironment? FadingOut => _fadingOut;
        internal bool NeedsTick => _applied || _fadingOut.HasValue;

        private static bool Enabled => ChillEnvPlugin.Cfg_EnableSmoothTransitions != null && WeatherDebugControl.SmoothEnabled;
        private static bool Blocked => ChillEnvPlugin.IsInCutscene() || SceneryAutomationSystem.IsWhaleSystemTriggered;

        internal void Tick(SyncPolicySnapshot policy, EnvironmentInputSnapshot inputs)
        {
            try { TickCore(policy, inputs); }
            catch (Exception ex)
            {
                _failedService = _service;
                ChillEnvPlugin.Log?.LogWarning("[连续降雨] 当前场景使用原雨景: " + ex.Message);
                Release();
            }
        }

        /// <summary>
        /// Rain particles prewarm when their window activates. Apply the automatic
        /// intensity first, so a fade-in never starts with one frame of native rain.
        /// </summary>
        internal void PrepareActivation(WindowViewService service, WindowViewType viewType)
        {
            if (!Enabled || (viewType != WindowViewType.LightRain && viewType != WindowViewType.HeavyRain &&
                viewType != WindowViewType.ThunderRain)) return;
            if (_service != null && !ReferenceEquals(service, _service)) return;
            Tick(WeatherDebugControl.BuildPolicy(), ModEnvironmentContext.Capture());
        }

        /// <summary>
        /// Called for an automatic rain→dry change. The window, icon and ambient sound
        /// stay until the particles have faded; the controller then deactivates it.
        /// </summary>
        internal bool TryDeferDeactivation(ModEnvironment window)
        {
            if (!_applied || Current.Rain <= 0 || Blocked || !Enabled) return false;
            RainWindow state = Find(window);
            if (state == null || !state.Root.activeSelf) return false;
            if (_fadingOut != window) BeginFadeOut(window);
            return true;
        }

        private void BeginFadeOut(ModEnvironment window)
        {
            _fadingOut = window;
            _drainUntil = double.PositiveInfinity;
            ChillEnvPlugin.Log?.LogInfo($"[连续降雨] 雨势渐弱后关闭: {window}");
        }

        private void TickCore(SyncPolicySnapshot policy, EnvironmentInputSnapshot inputs)
        {
            if (!Enabled || Blocked || policy == null || !policy.CanControlWeather || !EnsureWindows())
            {
                Release();
                return;
            }

            if (_fadingOut.HasValue && !Find(_fadingOut.Value).Root.activeSelf) _fadingOut = null;
            PrecipitationVisual target = EnvironmentDecision.Resolve(policy, inputs).Precipitation;
            // Rain resumed: the runner selects the window again.
            if (target.Rain > 0) _fadingOut = null;

            if (target.Rain <= 0 && !_fadingOut.HasValue)
            {
                ModEnvironment? active = ActiveWindow();
                // The dry reading can arrive before the runner's scenery request.
                if (_applied && Current.Rain > 0 && active.HasValue) BeginFadeOut(active.Value);
                else
                {
                    // Only automatic rain is scaled; a window chosen by the player keeps
                    // its authored look, and later automatic rain continues from it.
                    RestoreParticles();
                    _presentation.Seed(PrecipitationVisual.Native(active));
                    _seeded = true;
                    return;
                }
            }

            if (!_seeded)
            {
                _presentation.Seed(PrecipitationVisual.Native(ActiveWindow()));
                _seeded = true;
            }
            double now = Time.realtimeSinceStartupAsDouble;
            _presentation.Tick(target, now);
            Apply(Current);

            if (!_fadingOut.HasValue || Current.Rain > 0) return;
            if (double.IsPositiveInfinity(_drainUntil)) _drainUntil = now + DrainSeconds;
            if (now < _drainUntil) return;
            ModEnvironment window = _fadingOut.Value;
            _fadingOut = null;
            if (!_runtime.TryApplySceneryState(ModEnvironmentMap.ToGame(window), false, EnvironmentApplyMode.Interactive))
                ChillEnvPlugin.Log?.LogWarning($"[连续降雨] 渐弱结束但无法关闭雨景: {window}");
        }

        private void Apply(PrecipitationVisual visual)
        {
            _applied = true;
            float rate = visual.EmissionRate(_light.Rate, _heavy.Rate);
            float size = _light.Size + (_heavy.Size - _light.Size) * visual.HeavyWeight;
            foreach (var window in _windows)
            {
                var main = window.Rain.main;
                main.maxParticles = Math.Max(window.MaxParticles, _heavy.MaxParticles);
                main.startSizeMultiplier = size;
                var emission = window.Rain.emission;
                emission.rateOverTimeMultiplier = rate;
                if (window.Thunder == null) continue;
                // Flash timing stays authored; weaker storms skip some of its bursts.
                for (int i = 0; i < window.Bursts.Length; i++)
                {
                    window.WorkingBursts[i] = window.Bursts[i];
                    window.WorkingBursts[i].probability = window.Bursts[i].probability * Mathf.Clamp01(visual.Thunder);
                }
                window.Thunder.emission.SetBursts(window.WorkingBursts);
            }
        }

        private void RestoreParticles()
        {
            if (!_applied || _windows == null) return;
            _applied = false;
            foreach (var window in _windows)
            {
                if (window.Rain == null) continue;
                var main = window.Rain.main;
                main.maxParticles = window.MaxParticles;
                main.startSizeMultiplier = window.Size;
                var emission = window.Rain.emission;
                emission.rateOverTimeMultiplier = window.Rate;
                if (window.Thunder != null) window.Thunder.emission.SetBursts(window.Bursts);
            }
        }

        internal void Release()
        {
            ModEnvironment? fading = _fadingOut;
            _fadingOut = null;
            try { RestoreParticles(); }
            catch (Exception ex) { ChillEnvPlugin.Log?.LogDebug("[连续降雨] 清理已失效的雨景: " + ex.Message); }
            _applied = false;
            _seeded = false;
            // A deferred automatic change must still complete when control ends.
            if (fading.HasValue && !Blocked)
                _runtime.TryApplySceneryState(ModEnvironmentMap.ToGame(fading.Value), false, EnvironmentApplyMode.Interactive);
        }

        private ModEnvironment? ActiveWindow()
        {
            if (_thunder.Root.activeSelf) return ModEnvironment.ThunderRain;
            if (_heavy.Root.activeSelf) return ModEnvironment.HeavyRain;
            if (_light.Root.activeSelf) return ModEnvironment.LightRain;
            return null;
        }

        private RainWindow Find(ModEnvironment environment)
        {
            if (_windows == null) return null;
            foreach (var window in _windows)
                if (window.Environment == environment) return window;
            return null;
        }

        private bool EnsureWindows()
        {
            if (!_runtime.TryGetRuntimeServices(out _, out object service, out _) || !(service is WindowViewService windows))
                return false;
            if (ReferenceEquals(windows, _service) && _windows != null && _windows[0].Root != null) return true;
            if (ReferenceEquals(windows, _failedService)) return false;
            // A new scene owns new window instances; the old ones were destroyed with it.
            _windows = null;
            _applied = false;
            _seeded = false;
            _fadingOut = null;
            _service = windows;
            _light = Inspect(windows, ModEnvironment.LightRain, "_lightRain", false);
            _heavy = Inspect(windows, ModEnvironment.HeavyRain, "_heavyRain", false);
            _thunder = Inspect(windows, ModEnvironment.ThunderRain, "_thunderRain", true);
            _windows = new[] { _light, _heavy, _thunder };
            if (_light.Rate <= 0 || _heavy.Rate < _light.Rate)
                throw new InvalidOperationException("雨景粒子的原生强度顺序不符");
            return true;
        }

        private static RainWindow Inspect(WindowViewService service, ModEnvironment environment, string field, bool thunder)
        {
            var root = AccessTools.Field(typeof(WindowViewService), field)?.GetValue(service) as GameObject;
            if (root == null) throw new InvalidOperationException("缺少雨景对象: " + field);
            var rain = root.transform.Find("Rain")?.GetComponent<ParticleSystem>();
            if (rain == null) throw new InvalidOperationException("雨景缺少雨粒子: " + field);
            var main = rain.main;
            var emission = rain.emission;
            if (main.startSize.mode != ParticleSystemCurveMode.Constant || emission.rateOverTime.mode != ParticleSystemCurveMode.Constant)
                throw new InvalidOperationException("雨粒子不是常量发射: " + field);
            var state = new RainWindow
            {
                Environment = environment,
                Root = root,
                Rain = rain,
                Rate = emission.rateOverTimeMultiplier,
                Size = main.startSizeMultiplier,
                MaxParticles = main.maxParticles
            };
            if (thunder)
            {
                state.Thunder = root.transform.Find("Thunder")?.GetComponent<ParticleSystem>();
                if (state.Thunder == null) throw new InvalidOperationException("雷雨缺少闪电粒子");
                state.Bursts = new ParticleSystem.Burst[state.Thunder.emission.burstCount];
                state.Thunder.emission.GetBursts(state.Bursts);
                state.WorkingBursts = new ParticleSystem.Burst[state.Bursts.Length];
            }
            return state;
        }
    }
}
