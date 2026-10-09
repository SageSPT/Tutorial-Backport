using Path = System.IO.Path;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;

namespace TutorialBackport.Server;

[Injectable(TypePriority = OnLoadOrder.Preload + 90000)]
public class TutorialMod(
    ISptLogger<TutorialMod> logger,
    LocationTable locationTable,
    LocaleTable localeTable,
    LocationConfig locationConfig,
    ItemConfig itemConfig,
    SPTarkov.Server.Core.Services.Items.ItemFilterService itemFilter,
    TemplateTable templates,
    SPTarkov.Server.Core.Helpers.Items.ItemHelper itemHelper,
    JsonUtil jsonUtil,
    TutorialData data,
    TutorialProgress progress,
    TutorialServices services) : IOnLoad
{
    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await data.LoadAsync(cancellationToken);
            TutorialLoreItems.Register(templates, jsonUtil, logger);
            TutorialParts.Register(templates, itemConfig, itemFilter, jsonUtil, logger);
            RegisterLocation();
            RegisterLootMultipliers();
            RegisterLocales();
            TutorialPatches.Apply(logger, progress, services, itemHelper, locationConfig);
            TutorialBots.Apply(templates, jsonUtil, logger);
        }
        catch (Exception error)
        {
            logger.Error($"[TutorialBackport] disabled, load failed: {error}");
            data.Disabled = true;
        }
    }

    private void RegisterLootMultipliers()
    {
        locationConfig.LooseLootMultiplier.TryAdd(TutorialIds.LocationKey, 2.8);
        locationConfig.StaticLootMultiplier.TryAdd(TutorialIds.LocationKey, 1);
    }

    private void RegisterLocation()
    {
        var locations = locationTable.GetDictionary();
        if (locations.ContainsKey(TutorialIds.LocationKey))
        {
            logger.Warning($"[TutorialBackport] '{TutorialIds.LocationKey}' already registered by another mod; leaving it");
            return;
        }

        string folder = TutorialIds.Db("locations", TutorialIds.LocationKey);
        var location = new Location
        {
            Base = data.LocationBase,
            StaticAmmo = Read<Dictionary<string, IEnumerable<StaticAmmoDetails>>>(folder, "staticAmmo.json")!,
            AllExtracts = []
        };

        string looseJson = File.ReadAllText(Path.Combine(folder, "looseLoot.json"));
        var staticLoot = Read<Dictionary<MongoId, StaticLootDetails>>(folder, "staticLoot.json")!;
        var containers = Read<StaticContainerDetails>(folder, "staticContainers.json")!;
        Action<string> report = _ => { };
        location.LooseLoot = new LazyLoad<LooseLoot>(() => TutorialLootFilter.Loose(jsonUtil.Deserialize<LooseLoot>(looseJson)!, templates, report));
        location.StaticLoot = new LazyLoad<Dictionary<MongoId, StaticLootDetails>>(() => TutorialLootFilter.Static(staticLoot, templates, report));
        location.StaticContainers = new LazyLoad<StaticContainerDetails>(() => TutorialLootFilter.Containers(containers, templates, report));

        locations.Add(TutorialIds.LocationKey, location);
    }

    private T? Read<T>(string folder, string file) => jsonUtil.Deserialize<T>(File.ReadAllText(Path.Combine(folder, file)));

    private void RegisterLocales()
    {
        foreach (var (lang, keys) in data.Locales)
        {
            if (!localeTable.Global.TryGetValue(lang, out var table))
            {
                continue;
            }

            table.AddTransformer(dictionary =>
            {
                if (dictionary == null)
                {
                    return dictionary;
                }

                foreach (var (key, value) in keys)
                {
                    dictionary.TryAdd(key, value);
                }

                return dictionary;
            });
        }
    }
}
