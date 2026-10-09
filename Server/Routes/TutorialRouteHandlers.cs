using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;

namespace TutorialBackport.Server;

[Injectable(InjectionType.Singleton)]
public class TutorialRouteHandlers(
    HttpResponseUtil http,
    ProfileHelper profileHelper,
    ICloner cloner,
    TutorialData data,
    TutorialProgress progress,
    SPTarkov.Server.Core.Models.Spt.Tables.TemplateTable templates,
    ISptLogger<TutorialRouteHandlers> logger)
{
    public string Check(MongoId sessionId, TutorGameCheckRequest info)
    {
        var profile = profileHelper.GetFullProfile(sessionId);
        bool launch = !data.Disabled && !info.SkipTutorial && progress.IsPending(profile);
        return http.GetBody(new Dictionary<string, object> { ["launchTutorGame"] = launch });
    }

    public string Profile(MongoId sessionId)
    {
        PmcData? pmc = profileHelper.GetPmcProfile(sessionId);
        string side = pmc?.Info?.Side?.ToLowerInvariant() ?? "";
        if (pmc?.Inventory?.Equipment == null || !data.TutorialLoadout.TryGetValue(side, out var loadout))
        {
            return http.GetBody(new Dictionary<string, object?> { ["profile"] = pmc });
        }

        PmcData profile = cloner.Clone(pmc)!;
        MongoId equipmentId = profile.Inventory!.Equipment!.Value;
        var items = profile.Inventory.Items!;
        Item? pockets = items.FirstOrDefault(i => i.ParentId == equipmentId && i.SlotId == "Pockets");

        var kept = items.Where(i => i.ParentId == equipmentId && (i.SlotId == "SecuredContainer" || i.SlotId == "Scabbard"))
            .SelectMany(i => items.GetItemWithChildren(i.Id))
            .Select(i => i.Id)
            .ToHashSet();
        kept.Add(equipmentId);
        if (pockets != null)
        {
            kept.Add(pockets.Id);
        }

        var worn = items.GetItemWithChildren(equipmentId).Select(i => i.Id).Where(id => !kept.Contains(id)).ToHashSet();
        items.RemoveAll(i => worn.Contains(i.Id));

        var kit = cloner.Clone(loadout)!.ReplaceIDs().ToList();
        var unknown = kit.Where(i => i.ParentId != null && !templates.Items.ContainsKey(i.Template)).Select(i => i.Id.ToString()).ToHashSet();
        foreach (var item in kit.Where(i => i.ParentId != null && unknown.Contains(i.ParentId)).ToList())
        {
            unknown.Add(item.Id.ToString());
        }

        if (unknown.Count > 0)
        {
            logger.Warning($"[TutorialBackport] tutorial kit: {unknown.Count} item(s) not defined in 4.1 left out");
            kit.RemoveAll(i => unknown.Contains(i.Id.ToString()));
        }

        Item root = kit.First(i => i.ParentId == null);
        foreach (Item item in kit)
        {
            if (item == root)
            {
                continue;
            }

            if (item.ParentId == root.Id.ToString())
            {
                if (item.SlotId == "Pockets")
                {
                    continue;
                }

                item.ParentId = equipmentId.ToString();
            }
            else if (pockets != null && kit.FirstOrDefault(p => p.Id.ToString() == item.ParentId)?.SlotId == "Pockets")
            {
                item.ParentId = pockets.Id.ToString();
            }

            items.Add(item);
        }

        return http.GetBody(new Dictionary<string, object?> { ["profile"] = profile });
    }

    public string SubtitleTracks() => http.GetBody(data.SubtitleTracks);

    public string Config() => http.GetBody(data.TutorialGlobals);
}
