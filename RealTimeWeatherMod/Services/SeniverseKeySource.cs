using System;

namespace ChillWithYou.EnvSync.Services
{
    internal static class SeniverseKeySource
    {
        private static readonly string _encryptedDefaultKey = "7Mr4YSR87bFvE4zDgj6NbuBKgz4EiPYEnRTQ0RIaeSU=";
        public static bool HasDefaultKey => !string.IsNullOrEmpty(_encryptedDefaultKey);

        internal static string Resolve(string configuredKey)
        {
            if (!string.IsNullOrEmpty(configuredKey))
            {
                return configuredKey;
            }

            return HasDefaultKey ? KeySecurity.Decrypt(_encryptedDefaultKey) : null;
        }

    }
}
