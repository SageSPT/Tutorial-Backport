using System;
using EFT.UI.Matchmaker;
using HarmonyLib;
using JsonType;

namespace TutorialBackport.Client;

internal static class LocationFilter
{
    public static void Patch(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(MatchMakerSelectionLocationScreen), nameof(MatchMakerSelectionLocationScreen.DisplayLocation)),
            postfix: new HarmonyMethod(typeof(LocationFilter), nameof(AfterDisplayLocation)));
    }

    private static void AfterDisplayLocation(LocationSettings.Location location, ref bool __result)
    {
        if (__result && location != null && string.Equals(location.Id, TutorialIds.LocationId, StringComparison.OrdinalIgnoreCase))
        {
            __result = false;
        }
    }
}
