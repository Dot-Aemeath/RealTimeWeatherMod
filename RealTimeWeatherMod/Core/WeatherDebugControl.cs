using System;
using System.Collections.Generic;
using Bulbul;
using UnityEngine;

namespace ChillWithYou.EnvSync.Core
{
    /// <summary>
    /// Developer-facing seam for deterministic environment scenarios. It owns
    /// validation, persistence, immediate application and runtime verification so
    /// MCP callers only need three operations: apply, snapshot and disable.
    /// </summary>
    public static class WeatherDebugControl
    {
        private static readonly EnvironmentType[] BaseEnvironments =
            ModEnvironmentMap.BaseEnvironments;

        private static readonly EnvironmentType[] SceneryWeathers =
            ModEnvironmentMap.SceneryEnvironments;

        public static string ApplyScenario(
            int weatherCode,
            int temperature,
            string weatherText,
            string currentDateTime,
            string sunrise,
            string sunset)
        {
            if (!IsConfigurationReady())
            {
                return SerializeError("Mod 配置尚未初始化");
            }

            var input = new DebugScenarioInput
            {
                WeatherCode = weatherCode,
                Temperature = temperature,
                WeatherText = weatherText,
                CurrentDateTime = currentDateTime,
                Sunrise = sunrise,
                Sunset = sunset
            };
            if (!DebugScenario.TryCreate(input, DateTime.Now, out _, out var error))
            {
                return SerializeError(error);
            }

            ChillEnvPlugin.Cfg_DebugCode.Value = weatherCode;
            ChillEnvPlugin.Cfg_DebugTemp.Value = temperature;
            ChillEnvPlugin.Cfg_DebugText.Value = string.IsNullOrWhiteSpace(weatherText)
                ? "DebugWeather"
                : weatherText.Trim();
            ChillEnvPlugin.Cfg_DebugCurrentDateTime.Value = currentDateTime?.Trim() ?? string.Empty;
            ChillEnvPlugin.Cfg_DebugSunriseTime.Value = sunrise.Trim();
            ChillEnvPlugin.Cfg_DebugSunsetTime.Value = sunset.Trim();
            ChillEnvPlugin.Cfg_DebugMode.Value = true;
            ChillEnvPlugin.Instance.Config.Save();

            ChillEnvPlugin.Log?.LogInfo(
                $"[调试场景] 已应用: code={weatherCode}, temp={temperature}, " +
                $"time={currentDateTime}, sunrise={sunrise}, sunset={sunset}");
            TriggerImmediateApplication();
            return CaptureSnapshot();
        }

        public static string DisableScenario()
        {
            if (!IsConfigurationReady())
            {
                return SerializeError("Mod 配置尚未初始化");
            }

            ChillEnvPlugin.Cfg_DebugMode.Value = false;
            ChillEnvPlugin.Instance.Config.Save();
            ChillEnvPlugin.Log?.LogInfo("[调试场景] 已关闭，恢复真实输入");
            TriggerImmediateApplication();
            return CaptureSnapshot();
        }

        public static string CaptureSnapshot()
        {
            if (!IsConfigurationReady())
            {
                return SerializeError("Mod 配置尚未初始化");
            }

            EnvironmentInputSnapshot inputs = ModEnvironmentContext.Capture();
            SyncPolicySnapshot policy = SyncPolicy.Build(
                ChillEnvPlugin.Cfg_EnableTimeSync.Value,
                ChillEnvPlugin.Cfg_EnableWeatherSync.Value,
                ChillEnvPlugin.Cfg_ShowWeatherOnUI.Value);
            // Expected state resolves through the same module the runner applies
            // from, so this check can never validate against a stale copy of the rule.
            EnvironmentDecisionResult decision = EnvironmentDecision.Resolve(policy, inputs);
            EnvironmentType? expectedBase = policy.CanControlTime
                ? ModEnvironmentMap.ToGame(decision.Base)
                : (EnvironmentType?)null;
            EnvironmentType? expectedScenery = ModEnvironmentMap.ToGame(decision.Scenery);

            bool runtimeReady = GameEnvironmentRuntime.Shared.TryPrepare();
            var actualStates = new Dictionary<EnvironmentType, bool>();
            var activeEnvironments = new List<string>();
            bool actualStateReadable = true;
            foreach (EnvironmentType environmentType in BaseEnvironments)
            {
                actualStateReadable &= ReadActualState(
                    environmentType,
                    actualStates,
                    activeEnvironments);
            }
            foreach (EnvironmentType environmentType in SceneryWeathers)
            {
                actualStateReadable &= ReadActualState(
                    environmentType,
                    actualStates,
                    activeEnvironments);
            }

            bool baseMatches = !policy.CanControlTime ||
                (expectedBase.HasValue &&
                 IsOnlyExpectedActive(expectedBase.Value, BaseEnvironments, actualStates));
            bool sceneryMatches = !policy.CanControlWeather || inputs.Weather == null ||
                IsOnlyExpectedSceneryActive(expectedScenery, actualStates);

            var snapshot = new DebugRuntimeSnapshot
            {
                success = true,
                debugEnabled = inputs.IsDebug,
                configurationValid = inputs.ConfigurationValid,
                configurationError = inputs.ConfigurationError ?? string.Empty,
                effectiveDateTime = inputs.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                sunrise = inputs.Sunrise.ToString(@"hh\:mm"),
                sunset = inputs.Sunset.ToString(@"hh\:mm"),
                weatherCode = inputs.Weather?.Code ?? -1,
                temperature = inputs.Weather?.Temperature ?? 0,
                weatherText = inputs.Weather?.Text ?? string.Empty,
                timeSyncEnabled = policy.CanControlTime,
                weatherSyncEnabled = policy.CanControlWeather,
                expectedBase = policy.CanControlTime
                    ? expectedBase?.ToString() ?? "unknown"
                    : "unmanaged",
                expectedScenery = policy.CanControlWeather
                    ? expectedScenery?.ToString() ?? "none"
                    : "unmanaged",
                actualActive = activeEnvironments.Count == 0
                    ? "none"
                    : string.Join(",", activeEnvironments.ToArray()),
                actualStateReadable = actualStateReadable,
                runtimeReady = runtimeReady,
                registeredControllers = GameEnvironmentRuntime.Shared.RegisteredControllerCount,
                pluginInitialized = ChillEnvPlugin.Initialized,
                inCutscene = ChillEnvPlugin.IsInCutscene(),
                scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                lastApplied = AutoEnvRunner.LastAppliedEnvironment?.ToString() ?? "none",
                uiWeatherText = ChillEnvPlugin.UIWeatherString ?? string.Empty,
                whaleOverrideActive = SceneryAutomationSystem.IsWhaleSystemTriggered,
                matchesExpected = inputs.ConfigurationValid &&
                    runtimeReady &&
                    actualStateReadable &&
                    baseMatches &&
                    sceneryMatches &&
                    !SceneryAutomationSystem.IsWhaleSystemTriggered
            };
            return JsonUtility.ToJson(snapshot);
        }

        private static void TriggerImmediateApplication()
        {
            AutoEnvRunner.TriggerDebugSync();
            SceneryAutomationSystem.RequestImmediateAutomationCheck();
        }

        private static bool IsConfigurationReady()
        {
            // UnityEngine.Object overloads ==/!= and can report a BepInEx plugin
            // component as destroyed even though its managed instance and Config
            // remain available after the scene transition.
            return !ReferenceEquals(ChillEnvPlugin.Instance, null) &&
                ChillEnvPlugin.Cfg_DebugMode != null &&
                ChillEnvPlugin.Cfg_DebugCurrentDateTime != null &&
                ChillEnvPlugin.Cfg_DebugSunriseTime != null &&
                ChillEnvPlugin.Cfg_DebugSunsetTime != null;
        }

        private static bool ReadActualState(
            EnvironmentType environmentType,
            Dictionary<EnvironmentType, bool> actualStates,
            List<string> activeEnvironments)
        {
            // Main weather/base effects are window views. Some (notably Snow)
            // have an ambient behaviour object without a readable ambient enum,
            // so the combined audio probe can report false while the visual
            // effect is correctly active. Use the same window-state truth source
            // as AutoEnvRunner's startup verification.
            if (!GameEnvironmentRuntime.Shared.TryIsWindowActive(
                    environmentType,
                    out var isActive))
            {
                actualStates[environmentType] = false;
                return false;
            }

            actualStates[environmentType] = isActive;
            if (isActive)
            {
                activeEnvironments.Add(environmentType.ToString());
            }

            return true;
        }

        private static bool IsOnlyExpectedActive(
            EnvironmentType expected,
            EnvironmentType[] candidates,
            Dictionary<EnvironmentType, bool> actualStates)
        {
            foreach (EnvironmentType candidate in candidates)
            {
                bool shouldBeActive = candidate == expected;
                if (!actualStates.TryGetValue(candidate, out var isActive) ||
                    isActive != shouldBeActive)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsOnlyExpectedSceneryActive(
            EnvironmentType? expected,
            Dictionary<EnvironmentType, bool> actualStates)
        {
            foreach (EnvironmentType candidate in SceneryWeathers)
            {
                bool shouldBeActive = expected.HasValue && candidate == expected.Value;
                if (!actualStates.TryGetValue(candidate, out var isActive) ||
                    isActive != shouldBeActive)
                {
                    return false;
                }
            }

            return true;
        }

        private static string SerializeError(string error)
        {
            return JsonUtility.ToJson(new DebugCommandError
            {
                success = false,
                error = error ?? "未知调试错误"
            });
        }

        [Serializable]
        private sealed class DebugRuntimeSnapshot
        {
            public bool success;
            public bool debugEnabled;
            public bool configurationValid;
            public string configurationError;
            public string effectiveDateTime;
            public string sunrise;
            public string sunset;
            public int weatherCode;
            public int temperature;
            public string weatherText;
            public bool timeSyncEnabled;
            public bool weatherSyncEnabled;
            public string expectedBase;
            public string expectedScenery;
            public string actualActive;
            public bool actualStateReadable;
            public bool runtimeReady;
            public int registeredControllers;
            public bool pluginInitialized;
            public bool inCutscene;
            public string scene;
            public string lastApplied;
            public string uiWeatherText;
            public bool whaleOverrideActive;
            public bool matchesExpected;
        }

        [Serializable]
        private sealed class DebugCommandError
        {
            public bool success;
            public string error;
        }
    }
}
