using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TutorialBackport.Client;

internal static class DecalGuard
{
    private static readonly AccessTools.FieldRef<StaticDeferredDecalRenderer, DictionaryListHydra<string, StaticDeferredDecalRenderer.DecalsData>> Decals =
        AccessTools.FieldRefAccess<StaticDeferredDecalRenderer, DictionaryListHydra<string, StaticDeferredDecalRenderer.DecalsData>>("_decals");

    public static void Patch(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(StaticDeferredDecalRenderer), nameof(StaticDeferredDecalRenderer.UpdateInstancesBuffers)),
            prefix: new HarmonyMethod(typeof(DecalGuard), nameof(BeforeUpdateInstancesBuffers)));
    }

    private static void BeforeUpdateInstancesBuffers(StaticDeferredDecalRenderer __instance)
    {
        if (!TutorialLauncher.InTutorialRaid)
        {
            return;
        }

        var decals = Decals(__instance);
        var bad = new List<string>();
        int total = 0;
        for (int i = 0; i < decals.Count; i++)
        {
            StaticDeferredDecalRenderer.DecalsData data = decals.GetByIndex(i);
            total++;
            Material own = data.OwnMaterial;
            if (own != null && own.mainTexture is Texture2D)
            {
                continue;
            }

            bad.Add(data.Hash);
            Material parent = data.ParentMaterial;
            Shader shader = own != null ? own.shader : null;
            Texture direct = own != null && own.HasProperty("_MainTex") ? own.GetTexture("_MainTex") : null;
            Log.Warning("Scenes", $"decal group dropped: material '{(parent != null ? parent.name : "null")}' x{data.Decals.Count}, "
                + $"shader '{(shader != null ? shader.name : "null")}' supported={(shader != null && shader.isSupported)}, "
                + $"has _MainTex={(own != null && own.HasProperty("_MainTex"))}, _MainTex={(direct != null ? direct.GetType().Name + " " + direct.name : "null")}, "
                + $"parent mainTexture={(parent != null && parent.mainTexture != null ? parent.mainTexture.GetType().Name : "null")}");
        }

        foreach (string key in bad)
        {
            decals.Remove(key);
        }

        if (bad.Count > 0)
        {
            Log.Warning("Scenes", $"decals: {bad.Count} of {total} groups dropped (no usable main texture)");
        }
    }
}
