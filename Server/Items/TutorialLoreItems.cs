using System.Text.Json;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils;

namespace TutorialBackport.Server;

public static class TutorialLoreItems
{
    public static readonly HashSet<MongoId> QuestLoreItems =
    [
        new("68889a2f94cca0a80b070af1"), new("68889aab3fcaf13e48003ba7"), new("68889aca77aeb067290816f1"),
        new("6891bb203bbad2a6230f9be8"), new("6891bb67f674f551000a38d3"), new("6891bb83d7502d512502d160"),
        new("6891bb9f81d0bf37b10249f4")
    ];

    public static void Register(TemplateTable templates, JsonUtil jsonUtil, ISptLogger<TutorialMod> logger)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(TutorialIds.Db("templates", "loreItems.json")));
        JsonElement root = doc.RootElement;

        int added = 0;
        foreach (JsonProperty entry in root.GetProperty("items").EnumerateObject())
        {
            var id = new MongoId(entry.Name);
            if (templates.Items.ContainsKey(id))
            {
                continue;
            }

            templates.Items[id] = jsonUtil.Deserialize<TemplateItem>(entry.Value.GetRawText())!;
            added++;
        }

        int quest = 0;
        foreach (MongoId id in QuestLoreItems)
        {
            if (templates.Items.TryGetValue(id, out TemplateItem? template) && template.Properties != null)
            {
                template.Properties.QuestItem = true;
                quest++;
            }
        }

        var known = templates.Handbook.Items.Select(i => i.Id).ToHashSet();
        foreach (var row in jsonUtil.Deserialize<List<HandbookItem>>(root.GetProperty("handbook").GetRawText()) ?? [])
        {
            if (known.Add(row.Id))
            {
                templates.Handbook.Items.Add(row);
            }
        }

        foreach (JsonProperty price in root.GetProperty("prices").EnumerateObject())
        {
            templates.Prices.TryAdd(new MongoId(price.Name), price.Value.GetDouble());
        }

        var special = root.GetProperty("specialSlotItems").EnumerateArray().Select(e => new MongoId(e.GetString()!)).ToList();
        int slots = 0;
        foreach (TemplateItem item in templates.Items.Values)
        {
            foreach (Slot slot in item.Properties?.Slots ?? [])
            {
                if (slot.Name == null || !slot.Name.StartsWith("SpecialSlot"))
                {
                    continue;
                }

                HashSet<MongoId>? filter = slot.Properties?.Filters?.FirstOrDefault()?.Filter;
                if (filter == null)
                {
                    continue;
                }

                foreach (MongoId tpl in special)
                {
                    if (filter.Add(tpl))
                    {
                        slots++;
                    }
                }
            }
        }
    }
}
