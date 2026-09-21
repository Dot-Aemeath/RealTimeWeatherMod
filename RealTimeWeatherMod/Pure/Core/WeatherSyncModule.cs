using System;
using System.Collections;
using System.Collections.Generic;
using ChillWithYou.EnvSync.Models;

namespace ChillWithYou.EnvSync.Core
{
    internal enum WeatherSyncMode
    {
        Regular,
        Startup
    }

    internal interface IWeatherSyncEffects
    {
        void UpdateWeatherUi(WeatherInfo weather);
        void ApplyEnvironment(
            SyncPolicySnapshot policy,
            EnvironmentInputSnapshot inputs,
            bool force);
        void RefreshSunSchedule();
        void ScheduleWeatherCheck(float delaySeconds);
        void ReportWarning(string warning, bool isDebug);
    }

    internal sealed class WeatherSyncRequest
    {
        internal WeatherSyncRequest(
            SyncPolicySnapshot policy,
            WeatherSourceSettings source,
            EnvironmentInputSnapshot inputs,
            bool forceNetwork,
            bool forceApply,
            bool rescheduleWeatherCheck,
            float refreshIntervalSeconds,
            WeatherSyncMode mode = WeatherSyncMode.Regular,
            bool acquireWeather = true)
        {
            Policy = policy ?? throw new ArgumentNullException(nameof(policy));
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
            ForceNetwork = forceNetwork;
            ForceApply = forceApply;
            RescheduleWeatherCheck = rescheduleWeatherCheck;
            RefreshIntervalSeconds = Math.Max(1f, refreshIntervalSeconds);
            Mode = mode;
            AcquireWeather = acquireWeather;
        }

        internal SyncPolicySnapshot Policy { get; }
        internal WeatherSourceSettings Source { get; }

        /// <summary>
        /// The one reading of time/weather this request is judged against. Callers
        /// capture it before submitting; nothing downstream re-reads the clock.
        /// </summary>
        internal EnvironmentInputSnapshot Inputs { get; }
        internal bool ForceNetwork { get; }
        internal bool ForceApply { get; }
        internal bool RescheduleWeatherCheck { get; }
        internal float RefreshIntervalSeconds { get; }
        internal WeatherSyncMode Mode { get; }
        internal bool AcquireWeather { get; }

        internal bool IsDebug => Inputs.IsDebug;
        internal bool DebugConfigurationValid => Inputs.ConfigurationValid;
        internal string DebugConfigurationError => Inputs.ConfigurationError;
        internal WeatherInfo DebugWeather => Inputs.Weather;

        internal WeatherSyncRequest Merge(WeatherSyncRequest newer)
        {
            if (newer == null) return this;
            return new WeatherSyncRequest(
                newer.Policy,
                newer.Source,
                newer.Inputs,
                ForceNetwork || newer.ForceNetwork,
                ForceApply || newer.ForceApply,
                RescheduleWeatherCheck || newer.RescheduleWeatherCheck,
                newer.RefreshIntervalSeconds,
                newer.Mode,
                AcquireWeather || newer.AcquireWeather);
        }
    }

    internal sealed class WeatherSyncResult
    {
        internal WeatherSyncResult(
            SyncPolicySnapshot policy,
            EnvironmentInputSnapshot inputs,
            bool forceApply,
            bool refreshSunSchedule,
            float? nextWeatherCheckSeconds,
            string warning)
        {
            Policy = policy;
            Inputs = inputs;
            ForceApply = forceApply;
            RefreshSunSchedule = refreshSunSchedule;
            NextWeatherCheckSeconds = nextWeatherCheckSeconds;
            Warning = warning;
        }

        internal SyncPolicySnapshot Policy { get; }
        internal EnvironmentInputSnapshot Inputs { get; }
        internal WeatherInfo Weather => Inputs?.Weather;
        internal bool ForceApply { get; }
        internal bool RefreshSunSchedule { get; }
        internal float? NextWeatherCheckSeconds { get; }
        internal string Warning { get; }
        internal bool UpdateWeatherUi => Policy.NeedWeatherDataForUI;
        internal bool ApplyEnvironment => Policy.CanMutateEnvironment;
    }

    /// <summary>
    /// Owns synchronization sequencing and request coalescing. Unity callers submit
    /// intent once; provider availability, acquisition, fallback and refresh timing
    /// remain behind this interface.
    /// </summary>
    internal sealed class WeatherSyncModule
    {
        private readonly IWeatherAcquisition _acquisition;
        private readonly IWeatherSyncEffects _effects;
        private bool _running;
        private PendingSync _pending;

        internal WeatherSyncModule(
            IWeatherAcquisition acquisition,
            IWeatherSyncEffects effects)
        {
            _acquisition = acquisition ?? throw new ArgumentNullException(nameof(acquisition));
            _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        }

        internal IEnumerator Synchronize(
            WeatherSyncRequest request,
            Action<WeatherSyncResult> onComplete)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (_running)
            {
                // The active sync owns effects; a cache-only caller can still
                // receive its snapshot result without reapplying stale inputs.
                if (!request.AcquireWeather)
                {
                    if (onComplete != null)
                        yield return SynchronizeOnce(request, onComplete);
                    yield break;
                }
                QueuePending(request, onComplete);
                yield break;
            }

            _running = true;
            PendingSync current = PendingSync.Create(request, onComplete);
            try
            {
                while (current != null)
                {
                    WeatherSyncResult result = null;
                    yield return SynchronizeOnce(current.Request, value => result = value);
                    ApplyEffects(current.Request, result);
                    current.Complete(result);
                    current = _pending;
                    _pending = null;
                }
            }
            finally
            {
                _running = false;
            }
        }

        private IEnumerator SynchronizeOnce(
            WeatherSyncRequest request,
            Action<WeatherSyncResult> onComplete)
        {
            if (request.IsDebug)
            {
                onComplete(new WeatherSyncResult(
                    request.Policy,
                    request.Inputs,
                    request.ForceApply,
                    false,
                    NextDelay(request, null),
                    request.DebugConfigurationValid
                        ? null
                        : request.DebugConfigurationError));
                yield break;
            }

            bool needsWeather =
                request.Policy.CanControlWeather || request.Policy.NeedWeatherDataForUI;
            if (!needsWeather)
            {
                EnvironmentInputSnapshot inputs = request.Inputs.WithWeather(null);
                onComplete(new WeatherSyncResult(
                    request.Policy,
                    inputs,
                    request.ForceApply,
                    request.Policy.CanControlTime,
                    NextDelay(request, null),
                    null));
                yield break;
            }

            if (!request.AcquireWeather)
            {
                onComplete(new WeatherSyncResult(
                    request.Policy,
                    request.Inputs,
                    request.ForceApply,
                    request.Policy.CanControlTime || request.Policy.CanControlWeather,
                    NextDelay(request, null),
                    null));
                yield break;
            }

            WeatherFetchResult acquisitionResult = null;
            yield return _acquisition.AcquireWeather(
                request.Source,
                request.ForceNetwork,
                value => acquisitionResult = value);

            EnvironmentInputSnapshot effectiveInputs = request.Inputs.WithWeather(
                acquisitionResult?.Weather);
            onComplete(new WeatherSyncResult(
                request.Policy,
                effectiveInputs,
                request.ForceApply,
                acquisitionResult != null && acquisitionResult.Succeeded,
                NextDelay(request, acquisitionResult),
                acquisitionResult?.Error));
        }

        private static float? NextDelay(
            WeatherSyncRequest request,
            WeatherFetchResult acquisitionResult)
        {
            if (!request.RescheduleWeatherCheck)
            {
                return null;
            }

            if (acquisitionResult != null &&
                acquisitionResult.Origin == WeatherFetchOrigin.Cache &&
                acquisitionResult.CacheRemainingSeconds > 0f)
            {
                return Math.Max(
                    5f,
                    Math.Min(
                        request.RefreshIntervalSeconds,
                        acquisitionResult.CacheRemainingSeconds + 1f));
            }

            return request.RefreshIntervalSeconds;
        }

        private void ApplyEffects(
            WeatherSyncRequest request,
            WeatherSyncResult result)
        {
            if (result == null) return;

            if (!string.IsNullOrEmpty(result.Warning))
            {
                _effects.ReportWarning(result.Warning, request.IsDebug);
            }

            if (result.UpdateWeatherUi)
            {
                _effects.UpdateWeatherUi(result.Weather);
            }

            if (request.Mode == WeatherSyncMode.Regular && result.ApplyEnvironment)
            {
                _effects.ApplyEnvironment(
                    result.Policy,
                    result.Inputs,
                    result.ForceApply);
            }

            if (result.RefreshSunSchedule ||
                (request.Mode == WeatherSyncMode.Startup && result.Policy.CanControlTime))
            {
                _effects.RefreshSunSchedule();
            }

            if (result.NextWeatherCheckSeconds.HasValue)
            {
                _effects.ScheduleWeatherCheck(result.NextWeatherCheckSeconds.Value);
            }
        }

        private void QueuePending(
            WeatherSyncRequest request,
            Action<WeatherSyncResult> onComplete)
        {
            if (_pending == null)
            {
                _pending = PendingSync.Create(request, onComplete);
                return;
            }

            _pending.Request = _pending.Request.Merge(request);
            _pending.Add(onComplete);
        }

        private sealed class PendingSync
        {
            private readonly List<Action<WeatherSyncResult>> _callbacks =
                new List<Action<WeatherSyncResult>>();

            private PendingSync(WeatherSyncRequest request)
            {
                Request = request;
            }

            internal WeatherSyncRequest Request { get; set; }

            internal static PendingSync Create(
                WeatherSyncRequest request,
                Action<WeatherSyncResult> callback)
            {
                var pending = new PendingSync(request);
                pending.Add(callback);
                return pending;
            }

            internal void Add(Action<WeatherSyncResult> callback)
            {
                if (callback != null) _callbacks.Add(callback);
            }

            internal void Complete(WeatherSyncResult result)
            {
                foreach (Action<WeatherSyncResult> callback in _callbacks)
                {
                    callback(result);
                }
            }
        }
    }
}
