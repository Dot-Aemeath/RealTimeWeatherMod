using UnityEngine;

namespace ChillWithYou.EnvSync.Core
{
    /// <summary>
    /// Coordinates the game's physical window with the effective weather while
    /// preserving the heroine's normal close-window animation and state flow.
    /// </summary>
    internal static class RainWindowAutomation
    {
        private const float ObservationIntervalSeconds = 0.25f;

        private static WindowController _windowController;
        private static ModEnvironment? _scheduledRain;
        private static float _closeAt;
        private static float _nextObservationAt;
        private static bool _closeActionQueued;

        internal static void Register(WindowController controller)
        {
            if (controller == null || ReferenceEquals(_windowController, controller))
            {
                return;
            }

            _windowController = controller;
            ResetSchedule();
        }

        internal static void Tick()
        {
            float now = Time.unscaledTime;
            if (now < _nextObservationAt)
            {
                return;
            }

            _nextObservationAt = now + ObservationIntervalSeconds;
            if (!IsEnabled() || _windowController == null)
            {
                ResetSchedule();
                return;
            }

            ModEnvironment? rain = CurrentRain();
            if (!rain.HasValue || !IsWindowOpen())
            {
                ResetSchedule();
                return;
            }

            if (!_scheduledRain.HasValue || IsHeavier(rain.Value, _scheduledRain.Value))
            {
                ScheduleClose(rain.Value, now);
            }

            if (_closeActionQueued || now < _closeAt || ChillEnvPlugin.IsInCutscene())
            {
                return;
            }

            HeroineAI heroine = Object.FindObjectOfType<HeroineAI>();
            if (heroine == null || !heroine.IsUse || heroine.IsCurrentGameEndDirection ||
                heroine.CurrentUpdateState != HeroineAI.UpdateStateType.Idle ||
                !IsNormalSeatedState(heroine.GetCurrentState()))
            {
                return;
            }

            _closeActionQueued = true;
            heroine.ChangeCurrentMatcheAction(true);
        }

        internal static bool ShouldBlockAutomaticOpen()
        {
            return IsEnabled() && CurrentRain().HasValue;
        }

        internal static bool TrySelectCloseAction(ref HeroineAI.ActionStateType action)
        {
            if (!IsEnabled() || _windowController == null || !IsWindowOpen())
            {
                return false;
            }

            ModEnvironment? rain = CurrentRain();
            if (!rain.HasValue || !_scheduledRain.HasValue || Time.unscaledTime < _closeAt)
            {
                return false;
            }

            action = HeroineAI.ActionStateType.WildCloseWindow;
            _closeActionQueued = true;
            return true;
        }

        internal static void NotifyWindowOpened()
        {
            if (!IsEnabled())
            {
                return;
            }

            ModEnvironment? rain = CurrentRain();
            if (rain.HasValue)
            {
                ScheduleClose(rain.Value, Time.unscaledTime);
            }
        }

        internal static void NotifyWindowClosed()
        {
            ResetSchedule();
        }

        private static bool IsEnabled()
        {
            return ChillEnvPlugin.Cfg_CloseWindowWhenRaining?.Value == true;
        }

        private static ModEnvironment? CurrentRain()
        {
            Models.WeatherInfo weather = ModEnvironmentContext.Capture().Weather;
            ModEnvironment? scenery = weather == null
                ? (ModEnvironment?)null
                : EnvironmentDecision.ResolveSceneryCode(weather.Code);

            if (scenery == ModEnvironment.LightRain ||
                scenery == ModEnvironment.HeavyRain ||
                scenery == ModEnvironment.ThunderRain)
            {
                return scenery;
            }

            return null;
        }

        private static bool IsWindowOpen()
        {
            return _windowController != null &&
                _windowController.CurrentWindowState == WindowController.WindowState.Open;
        }

        private static void ScheduleClose(ModEnvironment rain, float now)
        {
            float minSeconds;
            float maxSeconds;
            switch (rain)
            {
                case ModEnvironment.LightRain:
                    minSeconds = 30f;
                    maxSeconds = 90f;
                    break;
                case ModEnvironment.HeavyRain:
                    minSeconds = 3f;
                    maxSeconds = 8f;
                    break;
                default:
                    minSeconds = 1f;
                    maxSeconds = 4f;
                    break;
            }

            float delay = Random.Range(minSeconds, maxSeconds);
            _scheduledRain = rain;
            _closeAt = now + delay;
            _closeActionQueued = false;
            ChillEnvPlugin.Log?.LogInfo(
                $"[雨天关窗] {Describe(rain)}，将在 {delay:F1} 秒后请求角色关窗");
        }

        private static bool IsHeavier(ModEnvironment current, ModEnvironment scheduled)
        {
            return Weight(current) > Weight(scheduled);
        }

        private static int Weight(ModEnvironment rain)
        {
            switch (rain)
            {
                case ModEnvironment.ThunderRain:
                    return 3;
                case ModEnvironment.HeavyRain:
                    return 2;
                default:
                    return 1;
            }
        }

        private static string Describe(ModEnvironment rain)
        {
            switch (rain)
            {
                case ModEnvironment.ThunderRain:
                    return "雷雨";
                case ModEnvironment.HeavyRain:
                    return "雨势较大";
                default:
                    return "小雨";
            }
        }

        private static bool IsNormalSeatedState(HeroineAI.ActionStateType state)
        {
            return state >= HeroineAI.ActionStateType.WorkPC &&
                state <= HeroineAI.ActionStateType.BreakForward;
        }

        private static void ResetSchedule()
        {
            _scheduledRain = null;
            _closeAt = 0f;
            _closeActionQueued = false;
        }
    }
}
