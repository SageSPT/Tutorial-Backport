using System.Collections;
using System.Reflection;
using HarmonyLib;

namespace TutorialBackport.Server;

public static class LotsOfLootCompat
{
    private const string GeneratorType = "LotsofLoot.Generators.LotsofLootLocationLootGenerator";

    [ThreadStatic]
    private static IDictionary? _addedTo;

    public static void Apply(Harmony harmony)
    {
        Type? generator = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(GeneratorType, false))
            .FirstOrDefault(type => type != null);
        MethodInfo? target = generator == null ? null : AccessTools.Method(generator, "GenerateDynamicLoot");
        if (target == null)
        {
            return;
        }

        harmony.Patch(target,
            prefix: new HarmonyMethod(typeof(LotsOfLootCompat), nameof(BeforeGenerateDynamicLoot)),
            finalizer: new HarmonyMethod(typeof(LotsOfLootCompat), nameof(AfterGenerateDynamicLoot)));
    }

    private static void BeforeGenerateDynamicLoot(object __instance, string locationName)
    {
        _addedTo = null;
        if (!string.Equals(locationName, TutorialIds.LocationKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            object? config = __instance.GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(field => field.FieldType.Name == "ConfigService")
                .Select(field => field.GetValue(__instance))
                .FirstOrDefault(value => value != null);
            object? preset = config?.GetType().GetProperty("LotsofLootPresetConfig")?.GetValue(config);
            if (preset?.GetType().GetProperty("Limits")?.GetValue(preset) is IDictionary limits && !limits.Contains(locationName))
            {
                limits[locationName] = int.MaxValue;
                _addedTo = limits;
            }
        }
        catch (Exception)
        {
            _addedTo = null;
        }
    }

    private static Exception? AfterGenerateDynamicLoot(Exception? __exception, string locationName)
    {
        if (_addedTo != null)
        {
            _addedTo.Remove(locationName);
            _addedTo = null;
        }

        return __exception;
    }
}
