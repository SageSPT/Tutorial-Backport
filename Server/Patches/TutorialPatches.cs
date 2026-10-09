using SPTarkov.Common.Models.Logging;
using HarmonyLib;
using SPTarkov.Server.Core.Generators.Loot;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Services.InRaid;
using SPTarkov.Server.Core.Services.Profile;

namespace TutorialBackport.Server;

public static class TutorialPatches
{
    private static TutorialProgress _progress = null!;
    private static TutorialServices _services = null!;
    private static ISptLogger<TutorialMod> _logger = null!;
    private static ItemHelper _itemHelper = null!;
    private static LocationConfig _locationConfig = null!;
    private static volatile bool _generatingTutorialLoot;

    public static void Apply(ISptLogger<TutorialMod> logger, TutorialProgress progress, TutorialServices services, ItemHelper itemHelper, LocationConfig locationConfig)
    {
        _itemHelper = itemHelper;
        _locationConfig = locationConfig;
        _services = services;
        _logger = logger;
        _progress = progress;
        var harmony = new Harmony(TutorialIds.ModGuid);
        Patch(harmony, AccessTools.Method(typeof(LocationLifecycleService), nameof(LocationLifecycleService.EndLocalRaidAsync)),
            prefix: nameof(BeforeEndLocalRaid), postfix: nameof(AfterEndLocalRaid));
        Patch(harmony, AccessTools.Method(typeof(CreateProfileService), nameof(CreateProfileService.CreateProfile)), postfix: nameof(AfterCreateProfile));
        Patch(harmony, AccessTools.Method(typeof(LocationLifecycleService), nameof(LocationLifecycleService.GenerateLocationAndLoot)),
            prefix: nameof(BeforeGenerateLocationAndLoot), postfix: nameof(AfterGenerateLocationAndLoot), finalizer: nameof(GenerateLocationAndLootFinalizer));
        Patch(harmony, AccessTools.Method(typeof(LocationLootGenerator), "CreateDynamicLootItem"), postfix: nameof(AfterCreateDynamicLootItem));
    }

    private static void BeforeGenerateLocationAndLoot(string name)
    {
        _generatingTutorialLoot = string.Equals(name, TutorialIds.LocationKey, StringComparison.OrdinalIgnoreCase);
    }

    private static Exception? GenerateLocationAndLootFinalizer(Exception? __exception)
    {
        _generatingTutorialLoot = false;
        return __exception;
    }

    private static void AfterCreateDynamicLootItem(Dictionary<string, IEnumerable<StaticAmmoDetails>> staticAmmoDist, ref ContainerItem __result)
    {
        if (!_generatingTutorialLoot || __result?.Items == null)
        {
            return;
        }

        try
        {
            var items = __result.Items.ToList();
            Item? root = items.FirstOrDefault();
            if (root == null || !_itemHelper.IsOfBaseclass(root.Template, BaseClasses.MAGAZINE)
                || items.Any(i => i.ParentId == root.Id && i.SlotId == "cartridges"))
            {
                return;
            }

            TemplateItem? template = _itemHelper.GetItem(root.Template).Value;
            if (template == null)
            {
                return;
            }

            _itemHelper.FillMagazineWithRandomCartridge(items, template, staticAmmoDist, null, _locationConfig.MinFillLooseMagazinePercent / 100.0);
            __result.Items = items;
        }
        catch (Exception error)
        {
            _logger.Error($"[TutorialBackport] tutorial magazine fill failed: {error.Message}");
        }
    }

    private static Dictionary<string, HashSet<string>>? _sides;

    private static void AfterGenerateLocationAndLoot(MongoId sessionId, string name, SPTarkov.Server.Core.Models.Eft.Common.LocationBase __result)
    {
        try
        {
            if (!string.Equals(name, TutorialIds.LocationKey, StringComparison.OrdinalIgnoreCase) || __result?.Loot == null)
            {
                return;
            }

            _sides ??= LoadSides();
            string? side = _services.Profile(sessionId)?.CharacterData?.PmcData?.Info?.Side;
            if (string.IsNullOrEmpty(side))
            {
                return;
            }

            var loot = __result.Loot.ToList();
            loot.RemoveAll(spot => spot.Items != null && spot.Items.Any(i =>
                _sides.TryGetValue(i.Template.ToString(), out var allowed) && !allowed.Contains(side)));
            __result.Loot = loot;
        }
        catch (Exception error)
        {
            _logger.Error($"[TutorialBackport] faction lore filter failed: {error.Message}");
        }
    }

    private static Dictionary<string, HashSet<string>> LoadSides()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(TutorialIds.Db("templates", "loreItems.json")));
        return doc.RootElement.GetProperty("sides").EnumerateObject().ToDictionary(
            p => p.Name, p => p.Value.EnumerateArray().Select(e => e.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    private static void Patch(Harmony harmony, System.Reflection.MethodBase? target, string? prefix = null, string? postfix = null, string? finalizer = null)
    {
        if (target == null)
        {
            _logger.Error($"[TutorialBackport] patch target missing for {prefix ?? postfix}; that part of the tutorial is inactive");
            return;
        }

        try
        {
            harmony.Patch(target,
                prefix: prefix == null ? null : new HarmonyMethod(typeof(TutorialPatches), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(TutorialPatches), postfix),
                finalizer: finalizer == null ? null : new HarmonyMethod(typeof(TutorialPatches), finalizer));
        }
        catch (Exception error)
        {
            _logger.Error($"[TutorialBackport] patching {target.DeclaringType?.Name}.{target.Name} failed: {error.Message}");
        }
    }

    private static bool BeforeEndLocalRaid(MongoId sessionId, EndLocalRaidRequestData request, ref Task __result)
    {
        try
        {
            string location = (request?.ServerId ?? "").Split('.')[0].ToLowerInvariant();
            if (location != TutorialIds.LocationKey)
            {
                return true;
            }

            _progress.Set(_services.Profile(sessionId), true);
            try
            {
                TutorialLoot.RaidEnd? raidEnd = _services.Loot.Prepare(sessionId, request?.Results);
                if (raidEnd != null)
                {
                    PendingRaidEnds[sessionId] = raidEnd;
                }
            }
            catch (Exception error)
            {
                _logger.Error($"[TutorialBackport] tutorial gear handling failed: {error.Message}");
            }

            return true;
        }
        catch (Exception error)
        {
            _logger.Error($"[TutorialBackport] raid-end handling failed, falling back to normal processing: {error.Message}");
            return true;
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<MongoId, TutorialLoot.RaidEnd> PendingRaidEnds = new();

    private static void AfterEndLocalRaid(MongoId sessionId, ref Task __result)
    {
        if (PendingRaidEnds.TryRemove(sessionId, out TutorialLoot.RaidEnd? raidEnd))
        {
            __result = FinishAfter(sessionId, __result, raidEnd);
        }
    }

    private static async Task FinishAfter(MongoId sessionId, Task processing, TutorialLoot.RaidEnd raidEnd)
    {
        await processing;
        try
        {
            _services.Loot.Finish(sessionId, raidEnd);
        }
        catch (Exception error)
        {
            _logger.Error($"[TutorialBackport] tutorial gear handling failed: {error.Message}");
        }
    }

    private static void AfterCreateProfile(MongoId sessionId, ref ValueTask<string> __result)
    {
        __result = MarkPendingAfter(sessionId, __result);
    }

    private static async ValueTask<string> MarkPendingAfter(MongoId sessionId, ValueTask<string> creation)
    {
        string response = await creation;
        try
        {
            var profile = _services.Profile(sessionId);
            if (profile?.SptData == null)
            {
                _logger.Error($"[TutorialBackport] new character {sessionId}: profile not found after creation; tutorial not scheduled");
                return response;
            }

            _progress.Set(profile, false);
        }
        catch (Exception error)
        {
            _logger.Error($"[TutorialBackport] could not mark new profile: {error.Message}");
        }

        return response;
    }
}
