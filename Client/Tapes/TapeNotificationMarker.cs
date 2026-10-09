using EFT.UI;
using UnityEngine;

namespace TutorialBackport.Client;

internal sealed class TapeNotificationMarker : MonoBehaviour
{
    public static void Patch(HarmonyLib.Harmony harmony)
    {
        harmony.Patch(HarmonyLib.AccessTools.PropertyGetter(typeof(BaseNotificationView), nameof(BaseNotificationView.ReturnToPool)),
            postfix: new HarmonyLib.HarmonyMethod(typeof(TapeNotificationMarker), nameof(AfterReturnToPool)));
    }

    private static void AfterReturnToPool(BaseNotificationView __instance, ref bool __result)
    {
        if (__result && __instance != null && __instance.GetComponent<TapeNotificationMarker>() != null)
        {
            __result = false;
        }
    }
}
