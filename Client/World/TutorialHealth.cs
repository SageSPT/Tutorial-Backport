using System.Collections.Generic;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using UnityEngine;
using DamageInfo = EFT.Ballistics.DamageInfo;

namespace TutorialBackport.Client;

internal static class TutorialHealth
{
    private const float MinPartHealth = 20f;
    private const float MinTotalHealth = 85f;
    private static bool _spreading;

    public static void Patch(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.ChangeHealth)),
            prefix: new HarmonyMethod(typeof(TutorialHealth), nameof(BeforeChangeHealth)));
        harmony.Patch(AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.Kill)),
            prefix: new HarmonyMethod(typeof(TutorialHealth), nameof(BeforeKill)));
        harmony.Patch(AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.ApplyDamage)),
            prefix: new HarmonyMethod(typeof(TutorialHealth), nameof(BeforeApplyDamage)),
            postfix: new HarmonyMethod(typeof(TutorialHealth), nameof(AfterApplyDamage)));
    }

    private static void BeforeApplyDamage(ActiveHealthController __instance, EBodyPart bodyPart, out float __state)
    {
        __state = Applies(__instance) ? __instance.GetBodyPartHealth(bodyPart).Current : -1f;
    }

    private static void AfterApplyDamage(ActiveHealthController __instance, EBodyPart bodyPart, float damage, DamageInfo damageInfo, float __state, float __result)
    {
        if (__state < 0f)
        {
            return;
        }
    }

    private static bool Applies(ActiveHealthController controller)
    {
        Player player = controller?.Player;
        return TutorialLauncher.InTutorialRaid && player != null && player.IsYourPlayer;
    }

    private static bool BeforeKill(ActiveHealthController __instance, EDamageType damageType)
    {
        if (!Applies(__instance))
        {
            return true;
        }

        return false;
    }

    private static bool BeforeChangeHealth(ActiveHealthController __instance, EBodyPart bodyPart, ref float value, DamageInfo damageInfo)
    {
        if (!Applies(__instance) || Mathf.Approximately(value, 0f) || bodyPart == EBodyPart.Common || value > 0f)
        {
            return true;
        }

        if (!__instance.IsAlive)
        {
            return false;
        }

        float total = __instance.GetBodyPartHealth(EBodyPart.Common).Current;
        if (MinTotalHealth > total || Mathf.Approximately(total, MinTotalHealth))
        {
            return false;
        }

        float original = value;
        if (total + value < MinTotalHealth)
        {
            value = MinTotalHealth - total;
        }

        if (bodyPart != EBodyPart.Head && bodyPart != EBodyPart.Chest)
        {
            return true;
        }

        float current = __instance.GetBodyPartHealth(bodyPart).Current;
        bool ignore = false;
        float overflow = 0f;
        if (MinPartHealth > current || Mathf.Approximately(current, MinPartHealth))
        {
            ignore = true;
            overflow = -value;
        }
        else if (current + value < MinPartHealth)
        {
            value = MinPartHealth - current;
            overflow = value - original;
        }

        float spread = Mathf.Min(total + value - MinTotalHealth, overflow);
        if (spread > 1f)
        {
            Spread(__instance, bodyPart, spread, damageInfo);
        }

        return !ignore;
    }

    private static void Spread(ActiveHealthController controller, EBodyPart from, float amount, DamageInfo damageInfo)
    {
        if (_spreading || amount <= 1f)
        {
            return;
        }

        _spreading = true;
        try
        {
            var parts = new List<EBodyPart>();
            float maxSum = 0f;
            foreach (EBodyPart part in new[] { EBodyPart.Head, EBodyPart.Chest, EBodyPart.Stomach, EBodyPart.LeftArm, EBodyPart.RightArm, EBodyPart.LeftLeg, EBodyPart.RightLeg })
            {
                if (part != from && !controller.IsBodyPartDestroyed(part))
                {
                    parts.Add(part);
                    maxSum += controller.GetBodyPartHealth(part).Maximum;
                }
            }

            foreach (EBodyPart part in parts)
            {
                controller.ChangeHealth(part, -amount * controller.GetBodyPartHealth(part).Maximum / maxSum, damageInfo);
            }
        }
        finally
        {
            _spreading = false;
        }
    }
}
