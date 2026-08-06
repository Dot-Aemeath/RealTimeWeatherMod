using System;
using System.Collections;
using UnityEngine.Networking;
using ChillWithYou.EnvSync.Core;

namespace ChillWithYou.EnvSync.Services
{
    /// <summary>
    /// The GET-then-parse protocol every weather provider adapter shares: timeout,
    /// transport failure, empty body, parse failure and parser exceptions all become a
    /// <see cref="ProviderFetchResult{T}"/>. Adapters are left with the only two things
    /// that actually differ between providers — how the URL is built and how the
    /// payload is read.
    /// </summary>
    internal static class ProviderJsonRequest
    {
        internal static IEnumerator Send<T>(
            string providerTag,
            string requestLabel,
            string url,
            int timeoutSeconds,
            Func<string, T> parse,
            Action<ProviderFetchResult<T>> onComplete)
            where T : class
        {
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = timeoutSeconds;
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success ||
                    string.IsNullOrEmpty(request.downloadHandler.text))
                {
                    string error = requestLabel + "请求失败: " + request.error;
                    ChillEnvPlugin.Log?.LogWarning(providerTag + " " + error);
                    onComplete?.Invoke(ProviderFetchResult<T>.Failure(error));
                    yield break;
                }

                ProviderFetchResult<T> result;
                try
                {
                    T value = parse(request.downloadHandler.text);
                    if (value == null)
                    {
                        ChillEnvPlugin.Log?.LogWarning(
                            providerTag + " " + requestLabel + "解析失败");
                        result = ProviderFetchResult<T>.Failure(requestLabel + "解析失败");
                    }
                    else
                    {
                        result = ProviderFetchResult<T>.Success(value);
                    }
                }
                catch (Exception ex)
                {
                    ChillEnvPlugin.Log?.LogError(
                        providerTag + " " + requestLabel + "解析异常: " + ex);
                    result = ProviderFetchResult<T>.Failure(ex.Message);
                }

                onComplete?.Invoke(result);
            }
        }
    }
}
