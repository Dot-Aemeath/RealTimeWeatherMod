using System;
using Bulbul;
using HarmonyLib;
using R3;
using UnityEngine;

namespace ChillWithYou.EnvSync.Core
{
    internal sealed class TimeTransitionController : IDisposable
    {
        private readonly GameEnvironmentRuntime _runtime;
        private TimeTransitionState _state = new TimeTransitionState();
        private DaylightPresentation _daylight = new DaylightPresentation();
        private int _alignmentRevision;
        private bool _continuous;
        private TimeTransitionRendering _rendering;
        private SyncPolicySnapshot _policy;
        private EnvironmentInputSnapshot _inputs;
        private ScheduledTimeBlend _schedule;
        private bool _inGameChange;
        private bool _unavailable;
        private bool _wasBlocked;
        private float _nextPrepare;
        private int _sceneHandle = -1;
        private EnvironmentApplyMode _applyMode = EnvironmentApplyMode.EarlyStartup;

        internal string UnavailableReason { get; private set; } = "";
        internal string Mode => _unavailable ? "unavailable" : _state.Mode;
        internal TimeBlend Current => _state.Current;
        internal DateTime? ManualUntil => _state.ManualUntil;
        internal bool InGameChange => _inGameChange;
        internal bool HasRendering => _rendering != null && _rendering.IsValid;
        internal bool OwnsPresentation => HasRendering && _state.OwnsVisual;
        internal bool NeedsTick => _state.OwnsVisual;
        internal ModEnvironment Logical => _state.Logical;
        internal bool UsesContinuousPresentation => _continuous && HasRendering && !_unavailable;
        internal DaylightVisual Daylight => _daylight.Current;
        internal DaylightVisual DaylightTarget => _daylight.Target;
        internal ModEnvironment CommittedLogical => ReadLogical();
        internal object WindowService => _rendering?.WindowService;
        internal object OriginalScenario => _rendering?.OriginalScenario;
        internal bool DrawFlares(UnityEngine.Rendering.CommandBuffer cmd,
            UnityEngine.Rendering.RenderTargetIdentifier source, UnityEngine.Rendering.RenderTargetIdentifier destination,
            Material material) => OwnsPresentation && _rendering.DrawFlares(cmd, source, destination, material);

        private bool Enabled => ChillEnvPlugin.Cfg_EnableSmoothTransitions != null && WeatherDebugControl.SmoothEnabled;
        private bool Blocked => ChillEnvPlugin.IsInCutscene() || SceneryAutomationSystem.IsWhaleSystemTriggered;

        internal TimeTransitionController(GameEnvironmentRuntime runtime) { _runtime = runtime; }

        internal void Tick(SyncPolicySnapshot policy, EnvironmentInputSnapshot inputs)
        {
            _policy = policy;
            _inputs = inputs;
            int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            if (_sceneHandle != scene)
            {
                Dispose();
                _sceneHandle = scene;
            }
            if (!Enabled && !_state.OwnsVisual && !_state.HasVisual) return;
            _continuous = Enabled && inputs != null && DaylightSchedule.IsValid(inputs.Sunrise, inputs.Sunset);
            _schedule = TimeTransitionSchedule.Resolve(policy, inputs, _continuous);
            if (Blocked)
            {
                if (!_wasBlocked) Release();
                _state.Suspend();
                _wasBlocked = true;
                return;
            }
            _wasBlocked = false;
            if (_unavailable || (!Enabled && !_state.OwnsVisual)) return;
            if (!policy.CanControlTime && !_state.OwnsVisual) return;
            if (!EnsureRendering()) return;
            if (!_state.HasVisual || !_state.OwnsVisual)
            {
                var native = ReadLogical();
                _state.Seed(native);
                _daylight.Seed(native);
            }
            bool immediate = WeatherDebugControl.ManualPreviewActive;
            _state.Tick(Enabled, policy.CanControlTime, false, _schedule, inputs, Time.realtimeSinceStartupAsDouble, immediate);
            _daylight.Tick(DaylightSchedule.Resolve(policy, inputs),
                _continuous && policy.CanControlTime && !_state.ManualUntil.HasValue &&
                _state.Mode != "manual" && _state.Mode != "manual-hold",
                _alignmentRevision != _state.AlignmentRevision, Time.realtimeSinceStartupAsDouble, immediate);
            _alignmentRevision = _state.AlignmentRevision;
            if (_state.OwnsVisual && (policy.CanControlTime || _state.ManualUntil.HasValue) && ReadLogical() != _state.Logical)
            {
                _runtime.TryApplyBaseImmediately(ModEnvironmentMap.ToGame(_state.Logical), _applyMode);
                _applyMode = EnvironmentApplyMode.Interactive;
            }
            Present();
            if (!Enabled && !_state.OwnsVisual && policy.CanControlTime)
                _runtime.TryApplyBaseImmediately(ModEnvironmentMap.ToGame(EnvironmentDecision.Resolve(policy, inputs).Base), _applyMode);
        }

        internal bool TryAutomaticRequest(EnvironmentApplyMode mode)
        {
            if ((!Enabled && !_state.OwnsVisual) || Blocked || _unavailable) return false;
            // Startup can reach this entry before the first Update.
            if (_inputs == null)
            {
                var policy = WeatherDebugControl.BuildPolicy();
                Tick(policy, ModEnvironmentContext.Capture());
            }
            if (!EnsureRendering()) return false;
            if (_state.SuppressAutomatic || _state.OwnsVisual) return true;
            return false;
        }

        internal bool BeginGameChange(EnvironmentUI ui, EnvironmentType target)
        {
            try { return BeginGameChangeCore(ui, target); }
            catch (Exception ex)
            {
                Fail("无法接管游戏时段切换: " + ex.Message);
                return false;
            }
        }

        private bool BeginGameChangeCore(EnvironmentUI ui, EnvironmentType target)
        {
            if (_inGameChange || Blocked || (!Enabled && !_state.OwnsVisual) || _unavailable) return false;
            if (!Enum.TryParse(target.ToString(), out ModEnvironment env) || (int)env > 3) return false;
            var unlocks = AccessTools.Field(typeof(EnvironmentUI), "_unlockItemService").GetValue(ui) as UnlockItemService;
            if (unlocks == null || unlocks.Environment.GetLockState(target).IsLocked.CurrentValue) return false;
            if (!EnsureRendering() || !ReferenceEquals(ui, _rendering.EnvironmentUi)) return false;
            if (!_state.HasVisual)
            {
                var native = ReadLogical();
                _state.Seed(native);
                _daylight.Seed(native);
            }
            if (_runtime.AutomaticApplyDepth == 0)
            {
                _inputs = ModEnvironmentContext.Capture();
                _policy = WeatherDebugControl.BuildPolicy();
                _schedule = TimeTransitionSchedule.Resolve(_policy, _inputs, _continuous);
                _state.Manual(env, _schedule, Time.realtimeSinceStartupAsDouble);
            }
            _inGameChange = _state.OwnsVisual;
            return _inGameChange;
        }

        internal void EndGameChange(Exception exception)
        {
            _inGameChange = false;
            if (exception != null) Fail("游戏时段切换失败: " + exception.Message);
            else Present();
        }

        internal bool HandleWindowChange(WindowViewService service, WindowViewType target)
        {
            try { return HandleWindowChangeCore(service, target); }
            catch (Exception ex)
            {
                Fail("无法接管窗景切换: " + ex.Message);
                return false;
            }
        }

        private bool HandleWindowChangeCore(WindowViewService service, WindowViewType target)
        {
            if (!ReferenceEquals(service, WindowService)) return false;
            if (!_inGameChange)
            {
                // Story/built-in callers own their change. Restore our resources before
                // the original method applies its new state, never after that write.
                Release();
                _state.Suspend();
                return false;
            }
            if (Enum.TryParse(target.ToString(), out WindowTimeOfDayType time))
            {
                var reactive = AccessTools.Field(typeof(WindowViewService), "_currentTimeOfDayType")
                    .GetValue(service) as ReactiveProperty<WindowTimeOfDayType>;
                reactive.Value = time;
            }
            return true;
        }

        internal void ApplyAtmosphere(object sphere)
        {
            if (_rendering == null || !_rendering.IsBlending || Blocked) return;
            try { _rendering.ApplyAtmosphere(sphere); }
            catch (Exception ex) { Fail(ex.ToString()); }
        }

        internal void RestoreAtmosphereTime(object sphere) => _rendering?.RestoreAtmosphereTime(sphere);
        internal void CompleteAtmosphere(object sphere)
        {
            if (_rendering == null || Blocked) return;
            try { _rendering.CompleteAtmosphere(sphere); }
            catch (Exception ex)
            {
                _rendering.RestoreAtmosphereTime(sphere);
                Fail(ex.ToString());
            }
        }

        private bool EnsureRendering()
        {
            if (HasRendering) return true;
            if (_unavailable || Time.unscaledTime < _nextPrepare) return false;
            _nextPrepare = Time.unscaledTime + 1;
            if (!_runtime.TryGetRuntimeServices(out var ui, out var windows, out _) || !(ui is EnvironmentUI environmentUi))
                return false;
            try
            {
                _rendering = TimeTransitionRendering.Create(environmentUi, (WindowViewService)windows);
                if (_rendering == null) return false; // COZY is still initializing.
                UnavailableReason = "";
                return true;
            }
            catch (TimeTransitionRendering.NotReadyException) { return false; }
            catch (Exception ex) { Fail(ex.Message); return false; }
        }

        private ModEnvironment ReadLogical()
        {
            foreach (var value in ModEnvironmentMap.BaseEnvironments)
                if (_runtime.TryIsWindowActive(value, out bool active) && active && Enum.TryParse(value.ToString(), out ModEnvironment result))
                    return result;
            return _state.HasVisual ? _state.Logical : _schedule.Logical;
        }

        private void Present()
        {
            if (_rendering == null) return;
            try
            {
                // Apply the final short-transition frame before releasing, but never
                // initialize presentation solely to discard an unowned snapshot.
                if (_state.OwnsVisual || _rendering.IsBlending)
                    _rendering.Apply(_state.Current, _state.Logical, _daylight.Current);
                if (!_state.OwnsVisual) Release();
            }
            catch (Exception ex) { Fail(ex.ToString()); }
        }

        private void Fail(string reason)
        {
            Release();
            _unavailable = true;
            _inGameChange = false;
            UnavailableReason = reason;
            _state.Suspend();
            ChillEnvPlugin.Log?.LogWarning("[平滑时段] 当前场景使用原切换方式: " + reason);
            if (_inputs != null && _policy != null && _policy.CanControlTime && !Blocked)
                _runtime.TryApplyBaseImmediately(ModEnvironmentMap.ToGame(EnvironmentDecision.Resolve(_policy, _inputs).Base), _applyMode);
        }

        private void Release()
        {
            var rendering = _rendering;
            _rendering = null;
            _daylight = new DaylightPresentation();
            _alignmentRevision = 0;
            try { rendering?.Dispose(); }
            catch (Exception ex) { ChillEnvPlugin.Log?.LogDebug("[平滑时段] 清理已失效的场景资源: " + ex.Message); }
        }

        public void Dispose()
        {
            Release();
            _state = new TimeTransitionState();
            _daylight = new DaylightPresentation();
            _alignmentRevision = 0;
            _continuous = false;
            _unavailable = false;
            UnavailableReason = "";
            _inGameChange = false;
            _wasBlocked = false;
            _nextPrepare = 0;
            _applyMode = EnvironmentApplyMode.EarlyStartup;
        }
    }
}
