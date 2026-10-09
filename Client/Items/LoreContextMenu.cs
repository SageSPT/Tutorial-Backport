using System;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;

namespace TutorialBackport.Client;

internal static class LoreContextMenu
{
    public static void Patch(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(ItemUiContext), nameof(ItemUiContext.GetItemContextInteractions)),
            postfix: new HarmonyMethod(typeof(LoreContextMenu), nameof(AfterGetItemContextInteractions)));
        harmony.Patch(AccessTools.Method(typeof(ItemViewStats), nameof(ItemViewStats.SetItemIcon)),
            postfix: new HarmonyMethod(typeof(LoreContextMenu), nameof(AfterSetItemIcon)));
        harmony.Patch(AccessTools.Method(typeof(EFT.UI.DragAndDrop.QuestItemView), nameof(EFT.UI.DragAndDrop.QuestItemView.NewQuestItemView)),
            postfix: new HarmonyMethod(typeof(LoreContextMenu), nameof(AfterNewQuestItemView)));
    }

    private static readonly System.Collections.Generic.HashSet<string> Completed = new System.Collections.Generic.HashSet<string>();
    private static bool _iconLogged;

    public static void MarkCompleted(Item item) => MarkCompleted(item?.StringTemplateId);

    public static void MarkCompleted(string tpl)
    {
        if (!string.IsNullOrEmpty(tpl) && Completed.Add(tpl))
        {
            foreach (ItemViewStats stats in UnityEngine.Object.FindObjectsOfType<ItemViewStats>())
            {
                var view = stats.GetComponentInParent<EFT.UI.DragAndDrop.QuestItemView>();
                if (view != null && view.Item != null && view.Item.StringTemplateId == tpl)
                {
                    ApplyIcon(stats, view.Item);
                }
            }
        }
    }

    private static void AfterNewQuestItemView(EFT.UI.DragAndDrop.QuestItemView __result)
    {
        if (__result != null && __result.Item != null && (TapeRecorder.IsTape(__result.Item) || NoteReader.IsNote(__result.Item)))
        {
            NoteReader.Host().StartCoroutine(IconNextFrame(__result));
        }
    }

    private static System.Collections.IEnumerator IconNextFrame(EFT.UI.DragAndDrop.QuestItemView view)
    {
        yield return null;
        if (view == null || view.Item == null)
        {
            yield break;
        }

        ItemViewStats stats = view.GetComponentInChildren<ItemViewStats>(true);
        if (stats != null)
        {
            ApplyIcon(stats, view.Item);
        }

        if (!_iconLogged)
        {
            _iconLogged = true;
            UnityEngine.UI.Image icon = stats != null ? stats._specialIcon : null;
            string chain = "";
            for (UnityEngine.Transform t = icon != null ? icon.transform : null; t != null && t != view.transform.parent; t = t.parent)
            {
                chain = t.name + (t.gameObject.activeSelf ? "" : "[OFF]") + "/" + chain;
            }
        }
    }

    private static void AfterSetItemIcon(ItemViewStats __instance, Item item)
    {
        if (item == null || __instance?._specialIcon == null || !(TapeRecorder.IsTape(item) || NoteReader.IsNote(item))
            || __instance.GetComponentInParent<EFT.UI.DragAndDrop.QuestItemView>() == null)
        {
            return;
        }

        ApplyIcon(__instance, item);
    }

    private static void ApplyIcon(ItemViewStats __instance, Item item)
    {
        UnityEngine.Sprite sprite = NoteWindow.UiSprite(Completed.Contains(item.StringTemplateId) ? "CompletableIcons_0" : "CompletableIcons_1");
        if (sprite == null)
        {
            return;
        }

        UnityEngine.UI.Image icon = __instance._specialIcon;
        icon.sprite = sprite;
        icon.preserveAspect = true;
        icon.color = UnityEngine.Color.white;
        UnityEngine.UI.LayoutElement layout = icon.GetComponent<UnityEngine.UI.LayoutElement>() ?? icon.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        layout.minWidth = -1f;
        layout.minHeight = -1f;
        layout.preferredWidth = -1f;
        layout.preferredHeight = 14f;
        layout.flexibleWidth = 14f;
        layout.flexibleHeight = -1f;
        icon.gameObject.SetActive(true);
    }

    private static void AfterGetItemContextInteractions(ItemContext itemContext, ContextInteractions<EItemInfoButton> __result)
    {
        Item item = itemContext?.Item;
        if (__result == null || item == null || __result._dynamicInteractions == null)
        {
            return;
        }

        try
        {
            if (TapeRecorder.IsTape(item))
            {
                Add(__result, "PLAYTAPE", "Icon_playtape", () => TapeRecorder.PlayFromMenu(item));
                if (TapeRecorder.IsPlaying)
                {
                    Add(__result, "STOPTAPE", "Icon_stoptape", TapeRecorder.StopFromMenu);
                }
            }
            else if (NoteReader.IsNote(item))
            {
                Add(__result, "READ", "Icon_readnote", () => NoteReader.Open(item));
            }
        }
        catch (Exception error)
        {
            Log.Warning("Notes", "lore item menu entries failed: " + error.Message);
        }
    }

    private static void Add(ContextInteractions<EItemInfoButton> interactions, string key, string icon, Action action)
    {
        if (!interactions._dynamicInteractions.ContainsKey(key))
        {
            interactions.method_2(key, key, action, NoteWindow.UiSprite(icon));
        }
    }
}
