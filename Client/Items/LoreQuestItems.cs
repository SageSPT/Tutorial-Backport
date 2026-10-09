using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using HarmonyLib;

namespace TutorialBackport.Client;

internal static class LoreQuestItems
{
    public static void Patch(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(LootItem), nameof(LootItem.Init)),
            postfix: new HarmonyMethod(typeof(LoreQuestItems), nameof(AfterLootItemInit)));
    }

    private static void AfterLootItemInit(LootItem __instance, Item item)
    {
        if (item != null && item.QuestItem && (TapeRecorder.IsTape(item) || NoteReader.IsNote(item)) && !__instance.gameObject.activeSelf)
        {
            __instance.gameObject.SetActive(true);
        }
    }

    public static bool IsCarried(Player player, Item item)
    {
        ItemAddress address = item?.CurrentAddress;
        Inventory inventory = player?.InventoryController?.Inventory;
        if (address == null || inventory == null)
        {
            return false;
        }

        return address.IsChildOf(inventory.Equipment) || (inventory.QuestRaidItems != null && address.IsChildOf(inventory.QuestRaidItems));
    }
}
