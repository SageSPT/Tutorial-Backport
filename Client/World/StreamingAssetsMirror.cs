using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TutorialBackport.Client;

internal static class StreamingAssetsMirror
{
    public static string Root =>
        Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "bundles");

    public static string Ours(string subPath)
    {
        if (string.IsNullOrEmpty(subPath))
        {
            return null;
        }

        subPath = subPath.Replace('\\', '/').TrimStart('/');
        if (File.Exists(Path.Combine(Application.streamingAssetsPath, subPath)))
        {
            return null;
        }

        string ours = Path.Combine(Root, subPath);
        return File.Exists(ours) ? ours : null;
    }

    public static string RelativeToStreamingAssets(string absolute)
    {
        var from = new Uri(Application.streamingAssetsPath.TrimEnd('/', '\\') + "/");
        return Uri.UnescapeDataString(from.MakeRelativeUri(new Uri(absolute)).ToString());
    }

    public static void Patch(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(Koenigz.PerfectCulling.EFT.PackedCullingGridData),
                nameof(Koenigz.PerfectCulling.EFT.PackedCullingGridData.GetPackedFilePathForGrid)),
            postfix: new HarmonyMethod(typeof(StreamingAssetsMirror), nameof(AfterAbsolutePath)));

        harmony.Patch(AccessTools.Constructor(typeof(Audio.SpatialSystem.SpatialAudioDataLoader), new[] { typeof(string), typeof(MonoBehaviour) }),
            postfix: new HarmonyMethod(typeof(StreamingAssetsMirror), nameof(AfterAudioLoaderCreated)));

        Type geometry = AccessTools.TypeByName("Meta.XR.Acoustics.MetaXRAcousticGeometry") ?? AccessTools.TypeByName("MetaXRAcousticGeometry");
        Type map = AccessTools.TypeByName("Meta.XR.Acoustics.MetaXRAcousticMap") ?? AccessTools.TypeByName("MetaXRAcousticMap");
        if (geometry != null)
        {
            harmony.Patch(AccessTools.Method(geometry, "LoadGeometryAsync"),
                prefix: new HarmonyMethod(typeof(StreamingAssetsMirror), nameof(BeforeAcousticsLoad)));
        }

        if (map != null)
        {
            harmony.Patch(AccessTools.Method(map, "LoadMapAsync"),
                prefix: new HarmonyMethod(typeof(StreamingAssetsMirror), nameof(BeforeAcousticsLoad)));
        }

        if (geometry == null || map == null)
        {
            Log.Warning("Scenes", "Meta XR acoustics types not found - tutorial acoustics data will not load");
        }
    }

    private static void AfterAbsolutePath(ref string __result)
    {
        if (string.IsNullOrEmpty(__result) || File.Exists(__result))
        {
            return;
        }

        string sub = SubPathOf(__result);
        string ours = Ours(sub);
        if (ours != null)
        {
            __result = ours;
        }
    }

    private static void AfterAudioLoaderCreated(Audio.SpatialSystem.SpatialAudioDataLoader __instance)
    {
        string path = __instance._fullPath;
        AfterAbsolutePath(ref path);
        __instance._fullPath = path;
    }

    private static void BeforeAcousticsLoad(ref string __0)
    {
        string ours = Ours(__0);
        if (ours != null)
        {
            __0 = RelativeToStreamingAssets(ours);
        }
    }

    private static string SubPathOf(string absolute)
    {
        string root = Application.streamingAssetsPath.Replace('\\', '/').TrimEnd('/') + "/";
        string a = absolute.Replace('\\', '/');
        return a.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? a.Substring(root.Length) : Path.GetFileName(a);
    }
}
