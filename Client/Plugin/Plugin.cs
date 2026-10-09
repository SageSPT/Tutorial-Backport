using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace TutorialBackport.Client;

[BepInPlugin(Guid, "Tutorial Backport", "1.0.0")]
[BepInDependency("com.SPT.core", BepInDependency.DependencyFlags.SoftDependency)]
[BepInIncompatibility("com.fika.core")]
public class Plugin : BaseUnityPlugin
{
    public const string Guid = "com.sage.tutorialbackport";

    public static Plugin Instance;
    public static ConfigEntry<bool> AutoLaunch;
    public static ConfigEntry<bool> PlayIntro;
    public static ConfigEntry<KeyCode> SubtitleToggleKey;
    public static ConfigEntry<KeyCode> SkipKey;
    public static ConfigEntry<KeyCode> SkipKeyAlternate;
    public static ConfigEntry<KeyboardShortcut> TapeKey;
    public static ConfigEntry<bool> TapeSubtitles;

    private void Awake()
    {
        Instance = this;
        Log.Source = Logger;
        AutoLaunch = Config.Bind("Tutorial", "AutoLaunch", true, "Start the tutorial raid automatically for a new profile");
        PlayIntro = Config.Bind("Intro", "PlayIntro", true, "Play the intro video when the tutorial raid starts");
        SubtitleToggleKey = Config.Bind("Intro", "SubtitleToggleKey", KeyCode.C, "Key that toggles the intro subtitles");
        SkipKey = Config.Bind("Intro", "SkipKey", KeyCode.Space, "Hold to skip the intro");
        SkipKeyAlternate = Config.Bind("Intro", "SkipKeyAlternate", KeyCode.Escape, "Second key that also skips the intro when held (None to disable)");
        TapeKey = Config.Bind("Tapes", "PlayStopKey", new KeyboardShortcut(KeyCode.M),
            "Plays the last tape you found / stops it; needs the audio recorder in your inventory");
        TapeSubtitles = Config.Bind("Tapes", "Subtitles", true, "Show subtitles while a tape plays");

        var harmony = new Harmony(Guid);
        TryPatch(harmony, "map list filter", LocationFilter.Patch);
        TryPatch(harmony, "first-launch check", TutorialLauncher.Patch);
        TryPatch(harmony, "tutorial raid start", IntroHook.Patch);
        TryPatch(harmony, "intro input block", IntroInputBlock.Patch);
        TryPatch(harmony, "tutorial raid end", IntroHook.PatchRaidEnd);
        TryPatch(harmony, "malfunction hint", MalfunctionHook.Patch);
        TryPatch(harmony, "tutorial scene preset", SceneBundleHost.PatchPresetHost);
        TryPatch(harmony, "tutorial baked data", StreamingAssetsMirror.Patch);
        TryPatch(harmony, "tutorial decals", DecalGuard.Patch);
        TryPatch(harmony, "wake-up", WakeUpPlayer.Patch);
        TryPatch(harmony, "tutorial kit restore", TutorialKit.Patch);
        TryPatch(harmony, "intro in the raid start sequence", IntroHook.PatchStartSequence);
        TryPatch(harmony, "tutorial weather", TutorialWorld.Patch);
        TryPatch(harmony, "interaction menu guard", ThirdPartyCompat.Patch);
        TryPatch(harmony, "malfunction tutorial", MalfunctionTutorial.Patch);
        TryPatch(harmony, "area lights", TutorialAreaLightRenderer.Patch);
        TryPatch(harmony, "flickering lamps", LampFlicker.Patch);
        TryPatch(harmony, "tutorial player health", TutorialHealth.Patch);
        TryPatch(harmony, "tape notifications", TapeNotificationMarker.Patch);
        TryPatch(harmony, "note reading", NoteReader.Patch);
        TryPatch(harmony, "tape listening", TapeListen.Patch);
        TryPatch(harmony, "notes and tapes as task items", LoreQuestItems.Patch);
        TryPatch(harmony, "note and tape item buttons", LoreContextMenu.Patch);
        TryPatch(harmony, "inspect window read button", InspectReadButton.Patch);
        TryPatch(harmony, "tutorial loudness", TutorialLoudness.Patch);
        AreaLightRevival.Install();
        MixerRemap.Install();
    }

    private static void TryPatch(Harmony harmony, string name, Action<Harmony> patch)
    {
        try
        {
            patch(harmony);
        }
        catch (Exception error)
        {
            Log.Error("Compatibility", name + " could not be enabled: " + error.Message);
        }
    }

    private void Update()
    {
        MainThread.Drain();
        LoadingCover.Tick();
    }
}
