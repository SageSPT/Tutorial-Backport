using System.Threading.Tasks;
using DG.Tweening;
using EFT.Hideout;
using EFT.UI;
using HarmonyLib;
using UnityEngine;

namespace TutorialBackport.Client;

internal static class LoadingCover
{
    private const float SafetySeconds = 60f;
    private static bool _held;
    private static float _releaseAt;

    public static void Patch(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(PreloaderUI), nameof(PreloaderUI.ClosePveLoadingScreen)),
            prefix: new HarmonyMethod(typeof(LoadingCover), nameof(BeforeClose)));
    }

    private static bool BeforeClose() => !_held;

    public static void Hold()
    {
        if (!MonoBehaviourSingleton<PreloaderUI>.Instantiated)
        {
            return;
        }

        PveGameModeLoadingScreen screen = MonoBehaviourSingleton<PreloaderUI>.Instance._pveLoadingScreen;
        if (screen == null)
        {
            return;
        }

        _held = true;
        _releaseAt = Time.realtimeSinceStartup + SafetySeconds;
        if (!screen.gameObject.activeSelf || screen.CanvasGroup.alpha < 0.99f)
        {
            MonoBehaviourSingleton<PreloaderUI>.Instance.ShowPveLoadingScreen();
            Traverse.Create(screen).Field("_loadingSequence").GetValue<Sequence>()?.Complete(true);
        }
    }

    public static void Release()
    {
        MainThread.Run(() =>
        {
            ReleaseNow();
            return Task.CompletedTask;
        });
    }

    private static void ReleaseNow()
    {
        if (!_held)
        {
            return;
        }

        _held = false;
        if (MonoBehaviourSingleton<PreloaderUI>.Instantiated)
        {
            MonoBehaviourSingleton<PreloaderUI>.Instance.ClosePveLoadingScreen();
        }
    }

    public static void Tick()
    {
        if (_held && Time.realtimeSinceStartup > _releaseAt)
        {
            ReleaseNow();
        }
    }
}
