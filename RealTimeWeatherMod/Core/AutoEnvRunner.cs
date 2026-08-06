using System;
using Bulbul;
using HarmonyLib;
using UnityEngine;
using ChillWithYou.EnvSync.Models;
using ChillWithYou.EnvSync.Services;

namespace ChillWithYou.EnvSync.Core
{
    public partial class AutoEnvRunner : MonoBehaviour
    {
        private float _nextWeatherCheckTime;
        private float _nextTimeCheckTime;
        private EnvironmentType? _lastAppliedEnv;
        private WeatherSyncModule _weatherSync;
        private float _nextSunSyncAttemptTime;
        private bool _sunSyncInProgress;
        private bool _pendingSunScheduleRefresh;
        private int _sunScheduleGeneration;

        private static AutoEnvRunner _instance;

        internal static EnvironmentType? LastAppliedEnvironment => _instance?._lastAppliedEnv;

        private static readonly EnvironmentType[] BaseEnvironments = ModEnvironmentMap.BaseEnvironments;
        private static readonly EnvironmentType[] SceneryWeathers = ModEnvironmentMap.SceneryEnvironments;
        private static readonly EnvironmentType[] MainEnvironments = ModEnvironmentMap.MainEnvironments;

        private void Start()
        {
            _instance = this;
            _weatherSync = new WeatherSyncModule(
                WeatherAcquisitionRuntime.Acquisition,
                new RuntimeWeatherSyncEffects(this));
            _nextWeatherCheckTime = Time.time + 10f;
            _nextTimeCheckTime = Time.time + 10f;
            ChillEnvPlugin.Log?.LogInfo("Runner 启动...");

            StartCoroutine(EarlyStartupSync());
        }

        private SyncPolicySnapshot BuildPolicySnapshot()
        {
            return SyncPolicy.Build(
                ChillEnvPlugin.Cfg_EnableTimeSync.Value,
                ChillEnvPlugin.Cfg_EnableWeatherSync.Value,
                ChillEnvPlugin.Cfg_ShowWeatherOnUI.Value);
        }

        private bool HasConfiguredWeatherSource()
        {
            return WeatherAcquisitionRuntime.CanAcquire();
        }

        private void UpdateUiWeatherString(WeatherInfo weather)
        {
            ChillEnvPlugin.UIWeatherString = WeatherUiState.NextWeatherText(
                ChillEnvPlugin.Cfg_ShowWeatherOnUI.Value,
                ChillEnvPlugin.UIWeatherString,
                weather);
        }

        private float GetConfiguredWeatherRefreshSeconds()
        {
            return Mathf.Max(1, ChillEnvPlugin.Cfg_WeatherRefreshMinutes.Value) * 60f;
        }

        private void CheckAndSyncSunSchedule(bool force = false)
        {
            // A debug scenario owns its simulated sun schedule. Never let a
            // background provider request overwrite or race those inputs.
            if (ChillEnvPlugin.Cfg_DebugMode.Value)
            {
                return;
            }

            if (!ChillEnvPlugin.Cfg_EnableWeatherSync.Value && !ChillEnvPlugin.Cfg_EnableTimeSync.Value)
            {
                return;
            }

            if (!HasConfiguredWeatherSource())
            {
                return;
            }

            string lastSync = ChillEnvPlugin.Cfg_LastSunSyncDate.Value;
            string today = DateTime.Now.ToString("yyyy-MM-dd");

            if (_sunSyncInProgress)
            {
                if (force)
                {
                    _pendingSunScheduleRefresh = true;
                }

                return;
            }

            if (!force && (lastSync == today || Time.time < _nextSunSyncAttemptTime))
            {
                return;
            }

            StartCoroutine(SyncSunScheduleRoutine(today, _sunScheduleGeneration));
        }

        private System.Collections.IEnumerator SyncSunScheduleRoutine(string targetDate, int requestGeneration)
        {
            _sunSyncInProgress = true;
            try
            {
                bool success = false;
                const int MaxShortRetries = 5;
                float delay = 5f;

                for (int attempt = 0; attempt < MaxShortRetries; attempt++)
                {
                    yield return WeatherAcquisitionRuntime.AcquireSunSchedule((result) =>
                    {
                        SunData data = result?.Schedule;
                        if (data != null && requestGeneration == _sunScheduleGeneration)
                        {
                            ChillEnvPlugin.Log?.LogInfo($"[SunSync] 同步成功: 日出{data.sunrise} 日落{data.sunset}");

                            ChillEnvPlugin.Cfg_SunriseTime.Value = data.sunrise;
                            ChillEnvPlugin.Cfg_SunsetTime.Value = data.sunset;
                            ChillEnvPlugin.Cfg_LastSunSyncDate.Value = targetDate;

                            ChillEnvPlugin.Instance.Config.Save();
                            success = true;
                        }
                    });

                    if (requestGeneration != _sunScheduleGeneration)
                    {
                        yield break;
                    }

                    if (success)
                    {
                        _nextSunSyncAttemptTime = Time.time + GetConfiguredWeatherRefreshSeconds();
                        yield break;
                    }

                    if (attempt < MaxShortRetries - 1)
                    {
                        ChillEnvPlugin.Log?.LogWarning($"[SunSync] 同步失败，{delay}秒后重试 ({attempt + 1}/{MaxShortRetries})");
                        yield return new WaitForSeconds(delay);
                        delay *= 2f;
                    }
                }

                ChillEnvPlugin.Log?.LogError("[SunSync] 短重试耗尽，30分钟后再次尝试");
                _nextSunSyncAttemptTime = Time.time + 30f * 60f;
            }
            finally
            {
                _sunSyncInProgress = false;
                if (_pendingSunScheduleRefresh)
                {
                    _pendingSunScheduleRefresh = false;
                    CheckAndSyncSunSchedule(true);
                }
            }
        }

        private void Update()
        {
            if (!ChillEnvPlugin.Initialized || GameEnvironmentRuntime.Shared.RegisteredControllerCount == 0)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.F9))
            {
                ChillEnvPlugin.Log?.LogInfo("F9: 强制同步");
                TriggerSync(false, true, false);
            }

            if (Input.GetKeyDown(KeyCode.F8))
            {
                ShowStatus();
            }

            if (Input.GetKeyDown(KeyCode.F7))
            {
                ChillEnvPlugin.Log?.LogInfo("F7: 强制刷新");
                ChillEnvPlugin.Instance.Config.Reload();
                ForceRefreshWeather();
            }

            if (Time.time >= _nextTimeCheckTime)
            {
                _nextTimeCheckTime = Time.time + 30f;
                TriggerSync(false, false, false);
            }

            if (Time.time >= _nextWeatherCheckTime)
            {
                TriggerSync(true, false, true);
            }
        }

        private void ShowStatus()
        {
            var now = ModEnvironmentContext.Capture().Now;
            ChillEnvPlugin.Log?.LogInfo($"--- 状态 [{now:HH:mm:ss}] ---");
            ChillEnvPlugin.Log?.LogInfo($"插件记录: {_lastAppliedEnv}");
            var currentActive = GetCurrentActiveEnvironment();
            ChillEnvPlugin.Log?.LogInfo($"游戏实际: {currentActive}");
            ChillEnvPlugin.Log?.LogInfo($"UI文本: {ChillEnvPlugin.UIWeatherString}");
            if (ChillEnvPlugin.Cfg_DebugMode.Value)
            {
                ChillEnvPlugin.Log?.LogWarning("【警告】调试模式已开启！");
                ChillEnvPlugin.Log?.LogInfo($"[调试快照] {WeatherDebugControl.CaptureSnapshot()}");
            }
        }

        private void ForceRefreshWeather()
        {
            TriggerSync(true, false, true);
        }

        /// <summary>
        /// 公开方法：立即强制刷新天气（供外部调用）
        /// </summary>
        public static void TriggerWeatherRefresh()
        {
            if (_instance != null)
            {
                string target = WeatherAcquisitionRuntime.DescribeTarget();
                ChillEnvPlugin.Log?.LogInfo($"[外部触发] 请求天气刷新 ({target})");
                _instance.ForceRefreshWeather();
            }
        }

        /// <summary>
        /// 公开方法：立即刷新 UI 天气文本（优先复用有效缓存）
        /// </summary>
        public static void TriggerUiWeatherRefresh()
        {
            if (_instance != null)
            {
                ChillEnvPlugin.Log?.LogInfo("[外部触发] 请求 UI 天气刷新");
                _instance.TriggerSync(false, false, false);
            }
        }

        /// <summary>
        /// 公开方法：立即刷新日出日落数据（供外部调用，如城市修改时）
        /// </summary>
        public static void TriggerSunScheduleRefresh()
        {
            if (_instance != null)
            {
                string target = WeatherAcquisitionRuntime.DescribeTarget();
                ChillEnvPlugin.Log?.LogInfo($"[外部触发] 请求日出日落刷新 ({target})");
                _instance.ForceSunScheduleRefresh();
            }
        }

        private void ForceSunScheduleRefresh()
        {
            _sunScheduleGeneration++;
            _nextSunSyncAttemptTime = 0f;
            _pendingSunScheduleRefresh = _sunSyncInProgress;
            ChillEnvPlugin.Cfg_LastSunSyncDate.Value = string.Empty;
            ChillEnvPlugin.Instance.Config.Save();
            CheckAndSyncSunSchedule(true);
        }

        private void TriggerSync(
            bool forceNetwork,
            bool forceApply,
            bool rescheduleWeatherCheck)
        {
            if (ChillEnvPlugin.Cfg_DebugMode.Value)
            {
                ChillEnvPlugin.Log?.LogInfo(
                    $"TriggerSync called (forceNetwork={forceNetwork}, " +
                    $"forceApply={forceApply}, reschedule={rescheduleWeatherCheck})");
            }

            if (ChillEnvPlugin.IsInCutscene())
            {
                _nextTimeCheckTime = Time.time + 30f;
                if (rescheduleWeatherCheck)
                {
                    _nextWeatherCheckTime = Time.time + 30f;
                }
                return;
            }

            if (!ChillEnvPlugin.Cfg_ShowWeatherOnUI.Value)
            {
                ChillEnvPlugin.UIWeatherString = string.Empty;
            }

            EnsureWeatherSyncModule();
            WeatherSyncRequest request = CreateSyncRequest(
                BuildPolicySnapshot(),
                forceNetwork,
                forceApply,
                rescheduleWeatherCheck);
            StartCoroutine(_weatherSync.Synchronize(request, null));
        }

        private WeatherSyncRequest CreateSyncRequest(
            SyncPolicySnapshot policy,
            bool forceNetwork,
            bool forceApply,
            bool rescheduleWeatherCheck,
            EnvironmentInputSnapshot inputs = null,
            WeatherSyncMode mode = WeatherSyncMode.Regular)
        {
            return new WeatherSyncRequest(
                policy,
                WeatherAcquisitionRuntime.CaptureSettings(),
                inputs ?? ModEnvironmentContext.Capture(),
                forceNetwork,
                forceApply,
                rescheduleWeatherCheck,
                GetConfiguredWeatherRefreshSeconds(),
                mode);
        }

        private void EnsureWeatherSyncModule()
        {
            if (_weatherSync == null)
            {
                _weatherSync = new WeatherSyncModule(
                    WeatherAcquisitionRuntime.Acquisition,
                    new RuntimeWeatherSyncEffects(this));
            }
        }

        private EnvironmentType? GetCurrentBaseEnvironment()
        {
            foreach (var env in BaseEnvironments)
            {
                if (IsEnvironmentActive(env))
                {
                    return env;
                }
            }

            return null;
        }

        private EnvironmentType? GetCurrentActiveEnvironment()
        {
            foreach (var env in MainEnvironments)
            {
                if (GameEnvironmentRuntime.Shared.TryIsWindowActive(env, out var isActive) && isActive)
                {
                    return env;
                }
            }

            return null;
        }

        private bool IsEnvironmentActive(EnvironmentType env)
        {
            return GameEnvironmentRuntime.Shared.TryIsWindowActive(env, out var isActive) && isActive;
        }

        internal static void TriggerDebugSync()
        {
            if (_instance == null)
            {
                return;
            }

            _instance._nextTimeCheckTime = Time.time + 30f;
            _instance.TriggerSync(false, true, true);
        }

        private void ApplyBaseEnvironment(EnvironmentType target, bool force)
        {
            if (force || !IsEnvironmentActive(target))
            {
                if (GameEnvironmentRuntime.Shared.TryApplyBase(
                    target,
                    EnvironmentApplyMode.Interactive))
                {
                    ChillEnvPlugin.Log?.LogInfo($"[环境] 切换至: {target}");
                }
                else
                {
                    ChillEnvPlugin.Log?.LogWarning($"[环境] 切换失败，运行时尚未就绪: {target}");
                }
            }
        }

        private void ApplyScenery(EnvironmentType? target, bool force)
        {
            foreach (var env in SceneryWeathers)
            {
                bool shouldBeActive = target.HasValue && target.Value == env;
                if (GameEnvironmentRuntime.Shared.TryApplySceneryState(
                    env,
                    shouldBeActive,
                    EnvironmentApplyMode.Interactive))
                {
                    continue;
                }

                ChillEnvPlugin.Log?.LogWarning(
                    $"[景色] 无法同步，环境运行时尚未就绪: {env}={shouldBeActive}");
            }
        }

        private void ApplyByPolicy(
            SyncPolicySnapshot policy,
            EnvironmentInputSnapshot inputs,
            bool force)
        {
            if (SceneryAutomationSystem.IsWhaleSystemTriggered)
            {
                ChillEnvPlugin.Log?.LogInfo("[鲸鱼彩蛋] 系统抽中的鲸鱼生效中，跳过天气切换");
                return;
            }

            EnvironmentDecisionResult decision = EnvironmentDecision.Resolve(policy, inputs);
            EnvironmentType finalEnv = ModEnvironmentMap.ToGame(decision.Base);

            if (policy.CanControlTime)
            {
                ApplyBaseEnvironment(finalEnv, force);
                _lastAppliedEnv = finalEnv;
            }

            if (!policy.CanControlWeather || inputs.Weather == null)
            {
                return;
            }

            ApplyScenery(ModEnvironmentMap.ToGame(decision.Scenery), force);
            if (!policy.CanControlTime)
            {
                _lastAppliedEnv = GetCurrentBaseEnvironment() ?? finalEnv;
            }
        }

        private sealed class RuntimeWeatherSyncEffects : IWeatherSyncEffects
        {
            private readonly AutoEnvRunner _runner;

            internal RuntimeWeatherSyncEffects(AutoEnvRunner runner)
            {
                _runner = runner;
            }

            public void UpdateWeatherUi(WeatherInfo weather)
            {
                _runner.UpdateUiWeatherString(weather);
            }

            public void ApplyEnvironment(
                SyncPolicySnapshot policy,
                EnvironmentInputSnapshot inputs,
                bool force)
            {
                _runner.ApplyByPolicy(policy, inputs, force);
            }

            public void RefreshSunSchedule()
            {
                _runner.CheckAndSyncSunSchedule();
            }

            public void ScheduleWeatherCheck(float delaySeconds)
            {
                _runner._nextWeatherCheckTime = Time.time + Mathf.Max(5f, delaySeconds);
            }

            public void ReportWarning(string warning, bool isDebug)
            {
                if (isDebug)
                {
                    ChillEnvPlugin.Log?.LogWarning("[同步] " + warning);
                }
                else
                {
                    ChillEnvPlugin.Log?.LogDebug("[同步] " + warning);
                }
            }
        }
    }
}
