using System;
using EFT.Character.Data;
using HarmonyLib;

namespace TutorialBackport.Client;

internal static class TutorialLoudness
{
    private const string WorldVolume = "WorldVolume";
    private const float WorldOffsetDb = 4f;

    public static void Patch(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(BetterAudio), nameof(BetterAudio.ResetWorldMixerValues)),
            postfix: new HarmonyMethod(typeof(TutorialLoudness), nameof(AfterResetWorldMixerValues)));
        harmony.Patch(AccessTools.Method(typeof(FirstPersonPlayerHearingSettings), nameof(FirstPersonPlayerHearingSettings.GetVolumeDB)),
            postfix: new HarmonyMethod(typeof(TutorialLoudness), nameof(AfterGetVolumeDB)));
    }

    public static void Apply() => Set(WorldOffsetDb);

    public static void Restore() => Set(0f);

    private static void AfterResetWorldMixerValues()
    {
        if (TutorialLauncher.InTutorialRaid)
        {
            Set(WorldOffsetDb);
        }
    }

    private static void AfterGetVolumeDB(ref float __result)
    {
        if (TutorialLauncher.InTutorialRaid)
        {
            __result += WorldOffsetDb;
        }
    }

    private static void Set(float db)
    {
        try
        {
            if (MonoBehaviourSingleton<BetterAudio>.Instantiated)
            {
                MonoBehaviourSingleton<BetterAudio>.Instance.Master.SetFloat(WorldVolume, db);
            }
        }
        catch (Exception error)
        {
            Log.Warning("Audio", "world volume not set: " + error.Message);
        }
    }
}
