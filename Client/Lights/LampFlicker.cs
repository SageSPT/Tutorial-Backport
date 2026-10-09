using System.Collections.Generic;
using EFT.Interactive;
using HarmonyLib;

namespace TutorialBackport.Client;

internal static class LampFlicker
{
    private static readonly HashSet<int> Flickering = new HashSet<int>();
    private static readonly List<LampController> Lamps = new List<LampController>();
    private static bool _callerLogged;

    public static void Patch(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(LampController), nameof(LampController.Awake)),
            prefix: new HarmonyMethod(typeof(LampFlicker), nameof(BeforeAwake)));
        harmony.Patch(AccessTools.Method(typeof(LampController), nameof(LampController.Switch)),
            prefix: new HarmonyMethod(typeof(LampFlicker), nameof(BeforeSwitch)));
    }

    private static void BeforeAwake(LampController __instance)
    {
        if (__instance.LampState == Turnable.EState.ConstantFlickering && __instance.gameObject.scene.name.StartsWith("Sandbox_SL"))
        {
            if (Flickering.Add(__instance.GetInstanceID()))
            {
                Lamps.Add(__instance);
            }
        }
    }

    private static void BeforeSwitch(LampController __instance, ref Turnable.EState switchTo)
    {
        if (switchTo == Turnable.EState.ConstantFlickering || switchTo == Turnable.EState.Destroyed
            || !Flickering.Contains(__instance.GetInstanceID()))
        {
            return;
        }

        if (!_callerLogged)
        {
            _callerLogged = true;
        }

        switchTo = Turnable.EState.ConstantFlickering;
    }

    public static void OnTutorialRaidStarted()
    {
        int fixedCount = 0, disabled = 0;
        Lamps.RemoveAll(l => l == null);
        foreach (LampController lamp in Lamps)
        {
            if (lamp != null && Flickering.Contains(lamp.GetInstanceID()) && lamp.LampState != Turnable.EState.ConstantFlickering
                && lamp.LampState != Turnable.EState.Destroyed)
            {
                if (!lamp.Enabled)
                {
                    disabled++;
                    lamp.Enabled = true;
                }

                lamp.Switch(Turnable.EState.ConstantFlickering);
                fixedCount++;
            }
        }
    }
}
