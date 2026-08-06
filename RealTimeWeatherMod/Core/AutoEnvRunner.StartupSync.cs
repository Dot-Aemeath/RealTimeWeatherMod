using Bulbul;
using UnityEngine;
using ChillWithYou.EnvSync.Models;
using ChillWithYou.EnvSync.Services;

namespace ChillWithYou.EnvSync.Core
{
    public partial class AutoEnvRunner
    {
        private const float StartupSyncPollIntervalSeconds = 0.25f;

        private bool _firstSyncDone;
        private bool _startupSyncRunning;
        private bool _startupAppliedOnce;
        private bool _startupWeatherFetchFinished;
        private EnvironmentInputSnapshot _pendingStartupInputs;

        private void TryApplyStartupAmbientSoundSync()
        {
            SceneryAutomationSystem.TryApplyStartupAmbientSoundState(out _);
        }

        private bool TryApplyStartupBase(EnvironmentType target)
        {
            if (_startupAppliedOnce && IsEnvironmentActive(target))
            {
                return true;
            }

            return GameEnvironmentRuntime.Shared.TryApplyBase(
                target,
                EnvironmentApplyMode.EarlyStartup);
        }

        private bool TryApplyStartupScenery(EnvironmentType? target)
        {
            foreach (var environmentType in SceneryWeathers)
            {
                bool shouldBeActive = target.HasValue && target.Value == environmentType;
                if (!GameEnvironmentRuntime.Shared.TryApplySceneryState(
                    environmentType,
                    shouldBeActive,
                    EnvironmentApplyMode.EarlyStartup))
                {
                    return false;
                }
            }

            return true;
        }

        private bool AreSceneryControllersReady()
        {
            return GameEnvironmentRuntime.Shared.AreControllersReady(SceneryWeathers);
        }

        private bool IsStartupStateApplied(
            SyncPolicySnapshot policy,
            EnvironmentDecisionResult decision,
            bool hasWeather)
        {
            if (policy.CanControlTime &&
                !IsEnvironmentActive(ModEnvironmentMap.ToGame(decision.Base)))
            {
                return false;
            }

            if (policy.CanControlWeather && hasWeather)
            {
                EnvironmentType? target = ModEnvironmentMap.ToGame(decision.Scenery);
                foreach (var environmentType in SceneryWeathers)
                {
                    bool shouldBeActive = target.HasValue && target.Value == environmentType;
                    if (IsEnvironmentActive(environmentType) != shouldBeActive)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool TryApplyStartupEnvironment(
            SyncPolicySnapshot policy,
            EnvironmentDecisionResult decision,
            bool hasWeather)
        {
            if (policy.CanControlTime &&
                !TryApplyStartupBase(ModEnvironmentMap.ToGame(decision.Base)))
            {
                return false;
            }

            if (policy.CanControlWeather &&
                hasWeather &&
                !TryApplyStartupScenery(ModEnvironmentMap.ToGame(decision.Scenery)))
            {
                return false;
            }

            return IsStartupStateApplied(policy, decision, hasWeather);
        }

        private System.Collections.IEnumerator EarlyStartupSync()
        {
            if (_startupSyncRunning || _firstSyncDone)
            {
                yield break;
            }

            _startupSyncRunning = true;

            try
            {
                SyncPolicySnapshot policy = BuildPolicySnapshot();
                EnvironmentInputSnapshot environmentInputs = ModEnvironmentContext.Capture();
                EnvironmentInputSnapshot noWeatherInputs = environmentInputs.WithWeather(null);
                bool needsWeatherData =
                    policy.CanControlWeather &&
                    (environmentInputs.IsDebug || WeatherAcquisitionRuntime.CanAcquire());

                _startupWeatherFetchFinished = false;
                _pendingStartupInputs = null;
                EnsureWeatherSyncModule();
                WeatherSyncRequest request = CreateSyncRequest(
                    policy,
                    false,
                    false,
                    true,
                    environmentInputs,
                    WeatherSyncMode.Startup);
                StartCoroutine(_weatherSync.Synchronize(
                    request,
                    result =>
                    {
                        _startupWeatherFetchFinished = true;
                        _pendingStartupInputs = result?.Inputs;
                    }));

                float timeout = 30f;
                while (!_firstSyncDone && timeout > 0f)
                {
                    EnvironmentInputSnapshot decisionInputs =
                        _pendingStartupInputs ?? noWeatherInputs;
                    bool hasWeather = decisionInputs.Weather != null;
                    bool runtimeReady = GameEnvironmentRuntime.Shared.TryPrepare();
                    if (runtimeReady)
                    {
                        TryApplyStartupAmbientSoundSync();
                    }

                    bool weatherDecisionReady =
                        _pendingStartupInputs != null || _startupWeatherFetchFinished;
                    bool needsSceneryControllers = policy.CanControlWeather && hasWeather;
                    bool sceneryControllersReady =
                        !needsSceneryControllers || AreSceneryControllersReady();

                    var action = StartupWeatherSyncPolicy.Determine(
                        policy.CanMutateEnvironment,
                        runtimeReady,
                        needsWeatherData,
                        weatherDecisionReady,
                        needsSceneryControllers,
                        sceneryControllersReady);

                    if (action == StartupWeatherSyncAction.Skip)
                    {
                        _firstSyncDone = true;
                        break;
                    }

                    if (action == StartupWeatherSyncAction.ApplyAndKeepSettling ||
                        action == StartupWeatherSyncAction.ApplyAndFinish)
                    {
                        EnvironmentDecisionResult decision =
                            EnvironmentDecision.Resolve(policy, decisionInputs);
                        bool applied = TryApplyStartupEnvironment(
                            policy,
                            decision,
                            hasWeather);
                        if (applied && !_startupAppliedOnce)
                        {
                            _startupAppliedOnce = true;
                            string scenery = !hasWeather
                                ? "未接管"
                                : (ModEnvironmentMap.ToGame(decision.Scenery)?.ToString() ?? "无降水");
                            string baseEnvironment = policy.CanControlTime
                                ? ModEnvironmentMap.ToGame(decision.Base).ToString()
                                : "未接管";
                            ChillEnvPlugin.Log?.LogInfo(
                                $"[启动同步] 已在主角坐下前应用环境: base={baseEnvironment}, scenery={scenery}");
                        }

                        if (applied && action == StartupWeatherSyncAction.ApplyAndFinish)
                        {
                            _firstSyncDone = true;
                            ChillEnvPlugin.Log?.LogInfo("[启动同步] 环境状态验证完成");
                            break;
                        }
                    }

                    yield return new WaitForSeconds(StartupSyncPollIntervalSeconds);
                    timeout -= StartupSyncPollIntervalSeconds;
                }

                if (!_firstSyncDone)
                {
                    _firstSyncDone = _startupAppliedOnce;
                    if (_startupAppliedOnce)
                    {
                        ChillEnvPlugin.Log?.LogWarning(
                            "[启动同步] 控制器等待超时，但直连环境已应用并验证");
                    }
                    else
                    {
                        ChillEnvPlugin.Log?.LogWarning(
                            "[启动同步] 超时等待环境运行时，后续由常规定时器兜底");
                    }
                }
            }
            finally
            {
                _startupSyncRunning = false;
            }
        }

        public static void TriggerImmediateSync()
        {
            if (_instance != null && !_instance._firstSyncDone && !_instance._startupSyncRunning)
            {
                _instance.StartCoroutine(_instance.EarlyStartupSync());
            }
        }
    }
}
