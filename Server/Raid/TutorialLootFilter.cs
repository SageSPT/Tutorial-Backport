using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace TutorialBackport.Server;

public static class TutorialLootFilter
{
    public static LooseLoot Loose(LooseLoot loot, TemplateTable templates, Action<string> report)
    {
        int removed = 0;
        loot.SpawnpointsForced = Spawnpoints(loot.SpawnpointsForced, templates, ref removed);
        loot.Spawnpoints = Spawnpoints(loot.Spawnpoints, templates, ref removed);
        if (removed > 0)
        {
            report($"loose loot: {removed} item(s) not defined in 4.1 left out");
        }

        return loot;
    }

    public static Dictionary<MongoId, StaticLootDetails> Static(Dictionary<MongoId, StaticLootDetails> loot, TemplateTable templates, Action<string> report)
    {
        int removed = 0;
        foreach (var details in loot.Values)
        {
            var kept = details.ItemDistribution.Where(d => templates.Items.ContainsKey(d.Tpl)).ToList();
            removed += details.ItemDistribution.Count() - kept.Count;
            details.ItemDistribution = kept;
        }

        if (removed > 0)
        {
            report($"static loot pools: {removed} entr(y/ies) not defined in 4.1 left out");
        }

        return loot;
    }

    public static StaticContainerDetails Containers(StaticContainerDetails containers, TemplateTable templates, Action<string> report)
    {
        var forced = containers.StaticForced?.ToList() ?? [];
        var kept = forced.Where(f => templates.Items.ContainsKey(f.ItemTpl)).ToList();
        containers.StaticForced = kept;
        if (kept.Count != forced.Count)
        {
            report($"static forced: {forced.Count - kept.Count} item(s) not defined in 4.1 left out");
        }

        return containers;
    }

    private static List<Spawnpoint>? Spawnpoints(IEnumerable<Spawnpoint>? points, TemplateTable templates, ref int removed)
    {
        if (points == null)
        {
            return null;
        }

        var result = new List<Spawnpoint>();
        foreach (var point in points)
        {
            var items = point.Template?.Items?.ToList();
            if (items == null)
            {
                result.Add(point);
                continue;
            }

            var bad = items.Where(i => !templates.Items.ContainsKey(i.Template)).Select(i => i.Id.ToString()).ToHashSet();
            if (bad.Count == 0)
            {
                result.Add(point);
                continue;
            }

            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var item in items.Where(i => i.ParentId != null && bad.Contains(i.ParentId) && !bad.Contains(i.Id.ToString())))
                {
                    bad.Add(item.Id.ToString());
                    grew = true;
                }
            }

            removed += items.Count(i => bad.Contains(i.Id.ToString()) && (i.ParentId == null || !bad.Contains(i.ParentId)));
            items.RemoveAll(i => bad.Contains(i.Id.ToString()));
            if (items.Count == 0)
            {
                continue;
            }

            point.Template!.Items = items;
            point.ItemDistribution = point.ItemDistribution?.Where(d => d.ComposedKey?.Key == null || !bad.Contains(d.ComposedKey.Key)).ToList();
            result.Add(point);
        }

        return result;
    }
}
