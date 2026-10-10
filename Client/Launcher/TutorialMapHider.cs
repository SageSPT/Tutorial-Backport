using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using HarmonyLib;
using JsonType;

namespace TutorialBackport.Client;

internal static class TutorialMapHider
{
    public static void Patch(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(TarkovApplication), nameof(TarkovApplication.ComebackToMainMenu)),
            prefix: new HarmonyMethod(typeof(TutorialMapHider), nameof(BeforeComebackToMainMenu)));
    }

    private static void BeforeComebackToMainMenu(TarkovApplication __instance)
    {
        try
        {
            if (!TutorialLauncher.InTutorialRaid)
            {
                Hide(__instance);
            }
        }
        catch (Exception error)
        {
            Log.Error("Raid", "hiding the tutorial map failed: " + error.Message);
        }
    }

    public static void Hide(TarkovApplication app)
    {
        Dictionary<string, LocationSettings.Location> locations = app?.Session?.LocationSettings?.locations;
        if (locations == null)
        {
            return;
        }

        foreach (string key in locations.Where(pair => IsTutorial(pair.Value)).Select(pair => pair.Key).ToList())
        {
            locations.Remove(key);
        }

        RaidSettings raid = app._raidSettings;
        if (raid != null && IsTutorial(raid.SelectedLocation))
        {
            raid.SelectedLocation = locations.Values.FirstOrDefault(location => location.Enabled && !location.Locked && !location.IsHideout);
        }
    }

    private static bool IsTutorial(LocationSettings.Location location) =>
        location != null && string.Equals(location.Id, TutorialIds.LocationId, StringComparison.OrdinalIgnoreCase);
}
