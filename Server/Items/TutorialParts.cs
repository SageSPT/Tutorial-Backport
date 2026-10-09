using System.Text.Json;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Items;
using SPTarkov.Server.Core.Utils;

namespace TutorialBackport.Server;

public static class TutorialParts
{
    public static void Register(TemplateTable templates, ItemConfig itemConfig, ItemFilterService itemFilter, JsonUtil jsonUtil, ISptLogger<TutorialMod> logger)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(TutorialIds.Db("templates", "tutorialParts.json")));
        JsonElement root = doc.RootElement;

        var ours = new HashSet<MongoId>();
        var others = new List<string>();
        foreach (JsonProperty entry in root.GetProperty("items").EnumerateObject())
        {
            var id = new MongoId(entry.Name);
            if (templates.Items.ContainsKey(id))
            {
                others.Add(entry.Name);
                continue;
            }

            templates.Items[id] = jsonUtil.Deserialize<TemplateItem>(entry.Value.GetRawText())!;
            ours.Add(id);
        }

        var known = templates.Handbook.Items.Select(i => i.Id).ToHashSet();
        foreach (var row in jsonUtil.Deserialize<List<HandbookItem>>(root.GetProperty("handbook").GetRawText()) ?? [])
        {
            if (ours.Contains(row.Id) && known.Add(row.Id))
            {
                templates.Handbook.Items.Add(row);
            }
        }

        foreach (JsonProperty price in root.GetProperty("prices").EnumerateObject())
        {
            var id = new MongoId(price.Name);
            if (ours.Contains(id))
            {
                templates.Prices.TryAdd(id, price.Value.GetDouble());
            }
        }

        int allowed = 0;
        foreach (JsonElement allow in root.GetProperty("slotAllow").EnumerateArray())
        {
            var item = new MongoId(allow.GetProperty("item").GetString()!);
            var parent = new MongoId(allow.GetProperty("parent").GetString()!);
            string slotName = allow.GetProperty("slot").GetString()!;
            if (!ours.Contains(item) || !templates.Items.TryGetValue(parent, out TemplateItem? parentTemplate))
            {
                continue;
            }

            Slot? slot = parentTemplate.Properties?.Slots?.FirstOrDefault(s => s.Name == slotName);
            HashSet<MongoId>? filter = slot?.Properties?.Filters?.FirstOrDefault()?.Filter;
            if (filter == null)
            {
                logger.Warning($"[TutorialBackport] tutorial MPX part {item}: slot {slotName} not found on {parent}");
                continue;
            }

            if (filter.Add(item))
            {
                allowed++;
            }
        }

        itemConfig.Blacklist.UnionWith(ours);
        itemFilter.AddItemToBlacklistCache(ours);
    }
}
