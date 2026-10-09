using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

namespace TutorialBackport.Client;

internal static class SceneBundleHost
{
    public const string FileName = "sandbox_start_scenes.bundle";
    private static AssetBundle _bundle;
    private static Task<bool> _loading;

    public static string BundlePath =>
        Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "bundles", FileName);

    public static bool IsLoaded => _bundle != null;

    public static Task<bool> EnsureLoaded()
    {
        if (_bundle != null)
        {
            return Task.FromResult(true);
        }

        return _loading ?? (_loading = Load());
    }

    private static async Task<bool> Load()
    {
        string path = BundlePath;
        if (!File.Exists(path))
        {
            Log.Error("Scenes", "tutorial scene bundle missing: " + path);
            _loading = null;
            return false;
        }

        if (!GameFilesMatch(path))
        {
            _loading = null;
            return false;
        }

        float started = Time.realtimeSinceStartup;
        AssetBundleCreateRequest request = AssetBundle.LoadFromFileAsync(path);
        while (!request.isDone)
        {
            await Task.Yield();
        }

        _bundle = request.assetBundle;
        _loading = null;
        if (_bundle == null)
        {
            Log.Error("Scenes", "Unity refused the tutorial scene bundle (see the Unity log above for the reason)");
            return false;
        }

        string[] scenes = _bundle.GetAllScenePaths();
        return true;
    }

    private static bool GameFilesMatch(string bundlePath)
    {
        string list = Path.ChangeExtension(bundlePath, ".files41.json");
        if (!File.Exists(list))
        {
            return true;
        }

        try
        {
            var files = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, FileSizes>>(File.ReadAllText(list));
            int bad = 0;
            string first = null;
            foreach (var pair in files)
            {
                string p = Path.Combine(Application.dataPath, pair.Key);
                bool ok = Size(p) == pair.Value.Size && (pair.Value.ResS == 0 || Size(p + ".resS") == pair.Value.ResS)
                          && (pair.Value.Resource == 0 || Size(Path.ChangeExtension(p, ".resource")) == pair.Value.Resource);
                if (!ok)
                {
                    bad++;
                    first = first ?? pair.Key;
                }
            }

            if (bad > 0)
            {
                Log.Error("Scenes", $"{bad} of {files.Count} EFT data files differ from the build this tutorial bundle was made for (first: {first}) - "
                                    + "the tutorial needs SPT 4.1 on EFT 0.16.9.5 data; not starting it");
                return false;
            }

            return true;
        }
        catch (System.Exception error)
        {
            Log.Error("Scenes", "could not check " + list + ": " + error.Message);
            return false;
        }
    }

    private static long Size(string path) => File.Exists(path) ? new FileInfo(path).Length : -1;

    private class FileSizes
    {
        [Newtonsoft.Json.JsonProperty("size")] public long Size;
        [Newtonsoft.Json.JsonProperty("resS")] public long ResS;
        [Newtonsoft.Json.JsonProperty("resource")] public long Resource;
    }

    public const string PresetKey = "maps/sandbox_start_preset.bundle";
    private static AssetBundle _preset;

    public static string PresetPath =>
        Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "bundles", "maps", "sandbox_start_preset.bundle");

    public static void PatchPresetHost(HarmonyLib.Harmony harmony)
    {
        harmony.Patch(HarmonyLib.AccessTools.Method(typeof(EFT.AssetsManager.BundlesManager), nameof(EFT.AssetsManager.BundlesManager.LoadBundleAsync)),
            prefix: new HarmonyLib.HarmonyMethod(typeof(SceneBundleHost), nameof(BeforeLoadBundle)));
    }

    private static void BeforeLoadBundle(EFT.AssetsManager.BundlesManager __instance, string bundleName)
    {
        if (bundleName == null || !string.Equals(bundleName.Replace('\\', '/'), PresetKey, System.StringComparison.OrdinalIgnoreCase)
            || __instance._bundles.ContainsKey(bundleName))
        {
            return;
        }

        if (_preset == null)
        {
            _preset = AssetBundle.LoadFromFile(PresetPath);
            if (_preset == null)
            {
                Log.Error("Scenes", "could not load the tutorial scene preset: " + PresetPath);
                return;
            }
        }

        __instance._bundles[bundleName] = new EFT.AssetsManager.BundlesManager.AssetBundleReference(_preset) { Counter = 0 };
    }

    public static void Unload()
    {
        if (_bundle == null)
        {
            return;
        }

        _bundle.Unload(true);
        _bundle = null;
    }
}
