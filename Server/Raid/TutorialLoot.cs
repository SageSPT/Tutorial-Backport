using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Inventory;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Services.Commerce;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils.Cloners;

namespace TutorialBackport.Server;

[Injectable(InjectionType.Singleton)]
public class TutorialLoot(SaveServer saveServer, InventoryHelper inventoryHelper, MailSendService mailSendService, ICloner cloner)
{
    public sealed class RaidEnd
    {
        public required List<Item> PreRaidWorn;
        public required MongoId PreRaidEquipment;
        public required HashSet<MongoId> RaidIds;
        public required bool Dead;
    }

    public RaidEnd? Prepare(MongoId sessionId, EndRaidResult? results)
    {
        if (results?.Profile?.Inventory?.Items == null || results.Profile.Inventory.Equipment == null || !saveServer.ProfileExists(sessionId))
        {
            return null;
        }

        PmcData? pmc = saveServer.GetProfile(sessionId).CharacterData?.PmcData;
        if (pmc?.Inventory?.Items == null || pmc.Inventory.Equipment == null)
        {
            return null;
        }

        MongoId equipment = pmc.Inventory.Equipment.Value;
        var realChildren = Children(pmc.Inventory.Items);
        List<Item> preRaidWorn = Below(equipment, realChildren).Select(Copy).ToList();
        bool dead = results.Result is ExitStatus.KILLED or ExitStatus.LEFT or ExitStatus.MISSINGINACTION;
        var raidIds = results.Profile.Inventory.Items.Select(i => i.Id).ToHashSet();
        var raidEnd = new RaidEnd { PreRaidWorn = preRaidWorn, PreRaidEquipment = equipment, RaidIds = raidIds, Dead = dead };
        if (dead)
        {
            return raidEnd;
        }

        var stays = StaysOn(preRaidWorn, equipment, Children(preRaidWorn));
        var starter = preRaidWorn.Where(i => !raidIds.Contains(i.Id) && !stays.Contains(i.Id)).ToList();
        var starterIds = starter.Select(i => i.Id).ToHashSet();
        var starterChildren = Children(starter);
        var mail = new List<Item>();
        foreach (Item root in starter.Where(i => i.ParentId == null || !starterIds.Contains(new MongoId(i.ParentId))))
        {
            List<Item> group = WithChildren(root, starterChildren);
            var output = EmptyOutput(sessionId);
            inventoryHelper.AddItemToStash(sessionId, new AddItemDirectRequest
            {
                ItemWithModsToAdd = group,
                FoundInRaid = false,
                UseSortingTable = false,
                Callback = null
            }, pmc, output);
            if (output.Warnings is { Count: > 0 })
            {
                mail.AddRange(group);
            }
        }

        if (mail.Count > 0)
        {
            mailSendService.SendSystemMessageToPlayer(sessionId, "Starter gear that did not fit in your stash", mail);
        }

        return raidEnd;
    }

    public void Finish(MongoId sessionId, RaidEnd raidEnd)
    {
        PmcData? pmc = saveServer.GetProfile(sessionId).CharacterData?.PmcData;
        if (pmc?.Inventory?.Items == null || pmc.Inventory.Equipment == null)
        {
            return;
        }

        List<Item> items = pmc.Inventory.Items;
        string equipment = pmc.Inventory.Equipment.Value.ToString();
        var present = items.Select(i => i.Id).ToHashSet();
        var preChildren = Children(raidEnd.PreRaidWorn);
        string preEquipment = raidEnd.PreRaidEquipment.ToString();
        var restored = new List<Item>();

        void Restore(Item root)
        {
            foreach (Item item in WithChildren(root, preChildren))
            {
                if (!raidEnd.RaidIds.Contains(item.Id) && present.Add(item.Id))
                {
                    Item copy = Copy(item);
                    if (copy.ParentId == preEquipment)
                    {
                        copy.ParentId = equipment;
                    }

                    restored.Add(copy);
                }
            }
        }

        bool SlotFilled(string slot) => items.Any(i => i.ParentId == equipment && i.SlotId == slot);

        foreach (Item slotItem in raidEnd.PreRaidWorn.Where(i => i.ParentId == preEquipment))
        {
            if (slotItem.SlotId is "Dogtag" or "ArmBand" && !SlotFilled(slotItem.SlotId))
            {
                Restore(slotItem);
            }
        }

        if (raidEnd.Dead)
        {
            foreach (Item item in raidEnd.PreRaidWorn.Where(i => !raidEnd.RaidIds.Contains(i.Id)))
            {
                bool parentHere = item.ParentId == preEquipment ? !SlotFilled(item.SlotId ?? "") : present.Contains(new MongoId(item.ParentId!));
                bool parentRestoredLater = item.ParentId != preEquipment && raidEnd.PreRaidWorn.Any(p => p.Id.ToString() == item.ParentId && !raidEnd.RaidIds.Contains(p.Id));
                if (parentHere && !parentRestoredLater)
                {
                    Restore(item);
                }
            }
        }

        if (restored.Count > 0)
        {
            items.AddRange(restored);
        }

        _ = saveServer.SaveProfileAsync(sessionId);
    }

    private static HashSet<MongoId> StaysOn(List<Item> worn, MongoId equipment, Dictionary<string, List<Item>> children)
    {
        var stays = new HashSet<MongoId>();
        foreach (Item slotItem in worn.Where(i => i.ParentId == equipment.ToString()))
        {
            if (slotItem.SlotId == "Pockets")
            {
                stays.Add(slotItem.Id);
            }
            else if (slotItem.SlotId is "Dogtag" or "ArmBand")
            {
                stays.UnionWith(WithChildren(slotItem, children).Select(i => i.Id));
            }
        }

        return stays;
    }

    private Item Copy(Item item) => cloner.Clone(item)!;

    private static Dictionary<string, List<Item>> Children(List<Item> items) =>
        items.Where(i => i.ParentId != null).GroupBy(i => i.ParentId!).ToDictionary(g => g.Key, g => g.ToList());

    private static List<Item> Below(MongoId root, Dictionary<string, List<Item>> children)
    {
        var result = new List<Item>();
        var queue = new Queue<string>();
        var seen = new HashSet<string>();
        queue.Enqueue(root.ToString());
        while (queue.Count > 0)
        {
            string parent = queue.Dequeue();
            if (!seen.Add(parent) || !children.TryGetValue(parent, out List<Item>? kids))
            {
                continue;
            }

            foreach (Item kid in kids)
            {
                result.Add(kid);
                queue.Enqueue(kid.Id.ToString());
            }
        }

        return result;
    }

    private static List<Item> WithChildren(Item root, Dictionary<string, List<Item>> children)
    {
        var result = new List<Item> { root };
        for (int i = 0; i < result.Count; i++)
        {
            if (children.TryGetValue(result[i].Id.ToString(), out List<Item>? kids))
            {
                result.AddRange(kids);
            }
        }

        return result;
    }

    private static ItemEventRouterResponse EmptyOutput(MongoId sessionId) => new()
    {
        ProfileChanges = new Dictionary<MongoId, ProfileChange>
        {
            [sessionId] = new ProfileChange
            {
                Id = sessionId,
                Items = new ItemChanges { NewItems = [], ChangedItems = [], DeletedItems = [] }
            }
        },
        Warnings = []
    };
}
