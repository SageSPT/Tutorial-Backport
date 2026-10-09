using System;
using System.Collections;
using Comfort.Common;
using EFT;
using HarmonyLib;

namespace TutorialBackport.Client;

internal static class IntroHook
{
    public static void Patch(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(GameWorld), nameof(GameWorld.OnGameStarted)),
            postfix: new HarmonyMethod(typeof(IntroHook), nameof(AfterGameStarted)) { priority = Priority.First },
            finalizer: new HarmonyMethod(typeof(ThirdPartyCompat), nameof(ThirdPartyCompat.OnGameStartedFinalizer)));
    }

    private static void AfterGameStarted(GameWorld __instance)
    {
        TapeRecorder.OnRaidStarted(__instance);
        bool tutorialMap = string.Equals(__instance.LocationId, TutorialIds.LocationId, StringComparison.OrdinalIgnoreCase);
        bool launchedByUs = TutorialLauncher.IntroPending;
        TutorialLauncher.IntroPending = false;
        if (!tutorialMap)
        {
            TutorialLauncher.InTutorialRaid = false;
            return;
        }

        TutorialHints.Start(__instance);
        TutorialWorld.OnTutorialRaidStarted(__instance);
        MalfunctionTutorial.OnTutorialRaidStarted(__instance);
        if (!launchedByUs)
        {
            return;
        }

        WakeUpPlayer.Play(__instance.MainPlayer);
    }

    public static void PatchStartSequence(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(LocalGame), "vmethod_2"),
            postfix: new HarmonyMethod(typeof(IntroHook), nameof(AfterCountdownCreated)));
    }

    private static void AfterCountdownCreated(LocalGame __instance, ref IEnumerator __result)
    {
        if (TutorialLauncher.IntroPending && Plugin.PlayIntro.Value)
        {
            __result = IntroInsteadOfCountdown(__instance, __result);
        }
    }

    private static IEnumerator IntroInsteadOfCountdown(LocalGame game, IEnumerator countdown)
    {
        string location = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.LocationId : null;
        if (!string.Equals(location, TutorialIds.LocationId, StringComparison.OrdinalIgnoreCase))
        {
            yield return countdown;
            yield break;
        }

        bool setupFailed = false;
        BetterAudio audio = Singleton<BetterAudio>.Instantiated ? Singleton<BetterAudio>.Instance : null;
        try
        {
            if (MonoBehaviourSingleton<Audio.AmbientSubsystem.AmbientAudioSystem>.Instantiated)
            {
                MonoBehaviourSingleton<Audio.AmbientSubsystem.AmbientAudioSystem>.Instance.Initialize();
            }

            audio?.ForceSetCommonVolume(-80f);
            if (Singleton<EFT.UI.GUISounds>.Instantiated)
            {
                Singleton<EFT.UI.GUISounds>.Instance.method_9(isActive: false);
                Singleton<EFT.UI.GUISounds>.Instance.StopMenuBackgroundMusicWithDelay(0.5f);
            }

            var raidSettings = AccessTools.Field(typeof(LocalGame).BaseType, "_raidSettings")?.GetValue(game) as LocalRaidSettings;
            MonoBehaviourSingleton<EFT.UI.PreloaderUI>.Instance.SetSessionId(raidSettings?.mode == ELocalMode.TRAINING ? "TRAINING" : "LOCAL");
            game.GameUi.gameObject.SetActive(true);
            game.GameUi.TimerPanel.ProfileId = game.ProfileId;
        }
        catch (Exception error)
        {
            Log.Error("Intro", "raid start setup failed, using 4.1's countdown instead: " + error);
            setupFailed = true;
        }

        if (setupFailed)
        {
            yield return countdown;
            yield break;
        }

        TutorialWorld.ApplyTime();
        IntroPlayer.Play(null);
        while (IntroPlayer.IsPlaying)
        {
            yield return null;
        }

        audio?.FadeInVolumeBeforeRaid(1f);
    }

    public static void PatchRaidEnd(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(TarkovApplication), nameof(TarkovApplication.OnGameEnd)),
            prefix: new HarmonyMethod(typeof(IntroHook), nameof(BeforeGameEnd)));
    }

    private static void BeforeGameEnd()
    {
        TutorialLauncher.InTutorialRaid = false;
        TutorialHints.Active?.Dispose();
        TapeRecorder.OnRaidEnded();
        MalfunctionTutorial.Stop();
        AmbientFirefight.Stop();
        TutorialAreaLightRenderer.Shutdown();
        WakeUpPlayer.RestoreGuns();
        TutorialLoudness.Restore();
    }
}
