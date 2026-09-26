using System;
using Bulbul;
using ChillWithYou.EnvSync.Core;
using DistantLands.Cozy;
using HarmonyLib;
using MagicLightmapSwitcher;

namespace ChillWithYou.EnvSync.Patches
{
    [HarmonyPatch]
    internal static class SmoothFlareDetailPatch
    {
        private static System.Reflection.MethodBase TargetMethod() =>
            AccessTools.Method(TimeTransitionFlares.RenderPass, "FullScreenBlit");

        private static bool Prefix(UnityEngine.Rendering.CommandBuffer cmd,
            UnityEngine.Rendering.RenderTargetIdentifier source, UnityEngine.Rendering.RenderTargetIdentifier destination,
            UnityEngine.Material material, int passIndex) =>
            passIndex != 11 || !GameEnvironmentRuntime.Shared.Transitions.DrawFlares(cmd, source, destination, material);
    }

    // The click handler can turn off the game's own automatic clock before calling
    // ChangeTime. Include those reactive callbacks in the same presentation transaction.
    [HarmonyPatch(typeof(EnvironmentUI), nameof(EnvironmentUI.OnClickButtonChangeTime))]
    internal static class SmoothTimeClickPatch
    {
        private static void Prefix(EnvironmentUI __instance, EnvironmentType environmentType, out bool __state)
        {
            __state = GameEnvironmentRuntime.Shared.Transitions.BeginGameChange(__instance, environmentType);
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state) GameEnvironmentRuntime.Shared.Transitions.EndGameChange(__exception);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(EnvironmentUI), "ChangeTime")]
    internal static class SmoothTimeChangePatch
    {
        private static void Prefix(EnvironmentUI __instance, EnvironmentType environmentType, out bool __state)
        {
            __state = GameEnvironmentRuntime.Shared.Transitions.BeginGameChange(__instance, environmentType);
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state) GameEnvironmentRuntime.Shared.Transitions.EndGameChange(__exception);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(WindowViewService), nameof(WindowViewService.ChangeWeatherAndTime))]
    internal static class SmoothWindowChangePatch
    {
        private static bool Prefix(WindowViewService __instance, WindowViewType weatherAndTimeType) =>
            !GameEnvironmentRuntime.Shared.Transitions.HandleWindowChange(__instance, weatherAndTimeType);
    }

    [HarmonyPatch(typeof(WindowViewService), nameof(WindowViewService.ActivateWindow))]
    internal static class PrecipitationActivatePatch
    {
        private static void Prefix(WindowViewService __instance, WindowViewType viewType) =>
            GameEnvironmentRuntime.Shared.Precipitation.PrepareActivation(__instance, viewType);
    }

    [HarmonyPatch(typeof(RuntimeAPI), nameof(RuntimeAPI.SwitchLightmap))]
    internal static class SmoothLightmapSwitchPatch
    {
        private static bool Prefix(StoredLightingScenario scenario)
        {
            var transition = GameEnvironmentRuntime.Shared.Transitions;
            return !transition.InGameChange || !ReferenceEquals(scenario, transition.OriginalScenario);
        }
    }

    [HarmonyPatch(typeof(WindowViewService), nameof(WindowViewService.IsActiveWindow))]
    internal static class SmoothWindowStatePatch
    {
        private static void Postfix(WindowViewService __instance, WindowViewType viewType, ref bool __result)
        {
            var transition = GameEnvironmentRuntime.Shared.Transitions;
            if (transition.OwnsPresentation && ReferenceEquals(__instance, transition.WindowService) &&
                Enum.TryParse(viewType.ToString(), out ModEnvironment value) && (int)value <= 3)
                __result = value == transition.CommittedLogical;
        }
    }

    [HarmonyPatch(typeof(CozyWeather), nameof(CozyWeather.UpdateShaderVariables))]
    internal static class SmoothAtmospherePatch
    {
        private static void Postfix(CozyWeather __instance) =>
            GameEnvironmentRuntime.Shared.Transitions.CompleteAtmosphere(__instance);

        private static void Prefix(CozyWeather __instance) =>
            GameEnvironmentRuntime.Shared.Transitions.ApplyAtmosphere(__instance);

        private static Exception Finalizer(CozyWeather __instance, Exception __exception)
        {
            GameEnvironmentRuntime.Shared.Transitions.RestoreAtmosphereTime(__instance);
            return __exception;
        }
    }
}
