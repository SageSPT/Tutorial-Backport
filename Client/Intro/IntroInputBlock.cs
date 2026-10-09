using System.Collections.Generic;
using EFT.InputSystem;
using HarmonyLib;

namespace TutorialBackport.Client;

internal static class IntroInputBlock
{
    public static void Patch(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(InputNodeAbstract), nameof(InputNodeAbstract.TranslateInput)),
            prefix: new HarmonyMethod(typeof(IntroInputBlock), nameof(BeforeTranslateInput)));
    }

    private static bool BeforeTranslateInput(InputNodeAbstract __instance, List<ECommand> commands, ref float[] axes, ref ECursorResult shouldLockCursor)
    {
        if (!(IntroPlayer.IsPlaying || WakeUpPlayer.IsPlaying) || !(__instance is InputTree))
        {
            return true;
        }

        commands?.Clear();
        if (axes != null)
        {
            for (int i = 0; i < axes.Length; i++)
            {
                axes[i] = 0f;
            }
        }

        shouldLockCursor = ECursorResult.LockCursor;
        return false;
    }
}
