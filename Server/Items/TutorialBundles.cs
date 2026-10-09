using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Loaders;
using SPTarkov.Server.Core.Models.Spt.Bundles;
using SPTarkov.Server.Core.Services.Server;
using SPTarkov.Server.Core.Utils;

namespace TutorialBackport.Server;

[Injectable(TypePriority = OnLoadOrder.PostLoad + 2)]
public class TutorialBundles(BundleLoader bundleLoader, BundleHashCacheService hashes, JsonUtil jsonUtil) : IOnLoad
{
    public const string ManifestFile = "lore-bundles.json";

    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        string manifestPath = Path.Combine(TutorialIds.ModDir, ManifestFile);
        if (!File.Exists(manifestPath))
        {
            return;
        }

        var manifest = jsonUtil.Deserialize<BundleManifest>(await File.ReadAllTextAsync(manifestPath, cancellationToken));
        string modPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), TutorialIds.ModDir).Replace('\\', '/');
        int added = 0, provided = 0, missing = 0;
        foreach (BundleManifestEntry entry in manifest?.Manifest ?? [])
        {
            if (bundleLoader.GetBundle(entry.Key) != null)
            {
                provided++;
                continue;
            }

            string file = Path.Join(modPath, "bundles", entry.Key).Replace('\\', '/');
            BundleHashCacheEntry? hash = File.Exists(file) ? await hashes.GetOrCalculateHashAsync(file, cancellationToken) : null;
            if (hash == null)
            {
                missing++;
                continue;
            }

            bundleLoader.AddBundle(entry.Key, new BundleInfo
            {
                ModPath = modPath,
                Bundle = entry,
                Crc = hash.Crc,
                Size = hash.Size,
                ModifiedUtcTicks = hash.ModifiedUtcTicks
            });
            added++;
        }
    }
}
