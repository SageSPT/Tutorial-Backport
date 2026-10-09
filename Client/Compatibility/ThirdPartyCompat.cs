using System;
using System.Collections;
using System.Reflection;
using EFT;
using HarmonyLib;

namespace TutorialBackport.Client;

internal static class ThirdPartyCompat
{
    private static bool _interactionErrorLogged;

    public static void PrepareForTutorial()
    {
        AddEmptyEntry("SkillsExtended.Skills.LockPicking.LockPickingHelpers", "LocationDoorIdLevels");

        AddAlias("LeaveItThere.ModSettings.Settings", "_allottedPointsLookup", TutorialIds.LocationId.ToLowerInvariant(), "sandbox");
        _interactionErrorLogged = false;
    }

    public static void Patch(Harmony harmony)
    {
        foreach (MethodInfo method in typeof(InteractionContextHelper).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            if (method.Name == nameof(InteractionContextHelper.GetAvailableActions))
            {
                harmony.Patch(method, finalizer: new HarmonyMethod(typeof(ThirdPartyCompat), nameof(InteractionFinalizer)));
            }
        }
    }

    private static Exception InteractionFinalizer(Exception __exception)
    {
        if (__exception == null || !TutorialLauncher.InTutorialRaid)
        {
            return __exception;
        }

        if (!_interactionErrorLogged)
        {
            _interactionErrorLogged = true;
            Log.Error("Raid", "another mod failed while building the interaction menu on the tutorial map (menu kept): " + __exception);
        }

        return null;
    }

    public static Exception OnGameStartedFinalizer(Exception __exception)
    {
        if (__exception == null || !TutorialLauncher.InTutorialRaid)
        {
            return __exception;
        }

        Log.Error("Raid", "another mod failed at raid start on the tutorial map (raid continues): " + __exception);
        return null;
    }

    private static IDictionary Table(string typeName, string fieldName, out Type type)
    {
        type = AccessTools.TypeByName(typeName);
        if (type == null)
        {
            return null;
        }

        if (!(AccessTools.Field(type, fieldName)?.GetValue(null) is IDictionary table))
        {
            Log.Warning("Raid", $"{typeName}.{fieldName} not found - that mod may fail on the tutorial map");
            return null;
        }

        return table;
    }

    private static void AddEmptyEntry(string typeName, string fieldName)
    {
        try
        {
            IDictionary table = Table(typeName, fieldName, out Type type);
            if (table == null || table.Contains(TutorialIds.LocationId))
            {
                return;
            }

            Type valueType = AccessTools.Field(type, fieldName).FieldType.GetGenericArguments()[1];
            table[TutorialIds.LocationId] = Activator.CreateInstance(valueType);
        }
        catch (Exception error)
        {
            Log.Warning("Raid", $"compat for {typeName} failed: {error.Message}");
        }
    }

    private static void AddAlias(string typeName, string fieldName, string key, string sourceKey)
    {
        try
        {
            IDictionary table = Table(typeName, fieldName, out Type type);
            if (table == null || table.Contains(key))
            {
                return;
            }

            if (!table.Contains(sourceKey))
            {
                Log.Warning("Raid", $"compat: {type.Name}.{fieldName} has no '{sourceKey}' entry to share with the tutorial map");
                return;
            }

            table[key] = table[sourceKey];
        }
        catch (Exception error)
        {
            Log.Warning("Raid", $"compat for {typeName} failed: {error.Message}");
        }
    }
}
