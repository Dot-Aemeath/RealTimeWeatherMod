using ChillWithYou.EnvSync.Core;
using HarmonyLib;

namespace ChillWithYou.EnvSync.Patches
{
    [HarmonyPatch(typeof(WindowController), nameof(WindowController.Setup))]
    internal static class RainWindowSetupPatch
    {
        private static void Postfix(WindowController __instance)
        {
            RainWindowAutomation.Register(__instance);
        }
    }

    [HarmonyPatch(typeof(WindowController), nameof(WindowController.GetNextOpenOrCloseState))]
    internal static class RainWindowSelectionPatch
    {
        private static void Postfix(ref HeroineAI.ActionStateType __result)
        {
            if (__result == HeroineAI.ActionStateType.WildOpenWindow &&
                RainWindowAutomation.ShouldBlockAutomaticOpen())
            {
                __result = HeroineAI.ActionStateType.None;
            }
        }
    }

    [HarmonyPatch(typeof(NextActionSelector), nameof(NextActionSelector.GetNextWildState))]
    internal static class RainWindowForcedClosePatch
    {
        private static void Postfix(ref HeroineAI.ActionStateType __result)
        {
            RainWindowAutomation.TrySelectCloseAction(ref __result);
        }
    }

    [HarmonyPatch(typeof(WindowController), nameof(WindowController.PlayWindowAnimationOpenWindow))]
    internal static class RainWindowOpenedPatch
    {
        private static void Postfix()
        {
            RainWindowAutomation.NotifyWindowOpened();
        }
    }

    [HarmonyPatch(typeof(WindowController), nameof(WindowController.PlayWindowAnimationCloseWindow))]
    internal static class RainWindowClosedPatch
    {
        private static void Postfix()
        {
            RainWindowAutomation.NotifyWindowClosed();
        }
    }
}
