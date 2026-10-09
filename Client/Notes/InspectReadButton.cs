using System;
using EFT.InventoryLogic;
using EFT.Trading;
using EFT.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace TutorialBackport.Client;

internal static class InspectReadButton
{
    private const string ButtonName = "ReadNote";

    public static void Patch(Harmony harmony)
    {
        var show = AccessTools.Method(typeof(InfoWindow), nameof(InfoWindow.Show),
            new[] { typeof(ItemContext), typeof(IItemOwner), typeof(Trader), typeof(ItemUiContext), typeof(ContextInteractions<EItemInfoButton>) });
        harmony.Patch(show, prefix: new HarmonyMethod(typeof(InspectReadButton), nameof(BeforeShow)),
            postfix: new HarmonyMethod(typeof(InspectReadButton), nameof(AfterShow)));
    }

    private static void BeforeShow(ItemContext itemContext, ContextInteractions<EItemInfoButton> contextInteractions)
    {
        if (contextInteractions != null && NoteReader.IsNote(itemContext?.Item))
        {
            contextInteractions.RemoveDynamicInteraction("READ");
        }
    }

    private static void AfterShow(InfoWindow __instance, ItemContext itemContext)
    {
        try
        {
            Transform panel = Find(__instance.transform, "InteractionButtonsPanel");
            if (panel == null)
            {
                return;
            }

            Transform old = panel.Find(ButtonName);
            if (old != null)
            {
                UnityEngine.Object.Destroy(old.gameObject);
            }

            Item item = itemContext?.Item;
            Transform modding = panel.Find("Modding");
            if (!NoteReader.IsNote(item) || modding == null)
            {
                return;
            }

            GameObject button = UnityEngine.Object.Instantiate(modding.gameObject, panel, false);
            button.name = ButtonName;
            button.transform.SetSiblingIndex(modding.GetSiblingIndex() + 1);
            Image image = button.GetComponent<Image>();
            Sprite sprite = NoteWindow.UiSprite("ReadNoteButton");
            if (image != null && sprite != null)
            {
                image.sprite = sprite;
            }

            Button click = button.GetComponent<Button>();
            if (click != null)
            {
                click.onClick = new Button.ButtonClickedEvent();
                click.onClick.AddListener(() => NoteReader.Open(item));
                click.interactable = true;
            }

            CanvasGroup group = button.GetComponent<CanvasGroup>();
            if (group != null)
            {
                group.alpha = 1f;
                group.interactable = true;
                group.blocksRaycasts = true;
            }

            if (!HasActiveButtons(panel.Find("InteractionButtonsContainer")))
            {
                LayoutElement element = button.GetComponent<LayoutElement>() ?? button.AddComponent<LayoutElement>();
                element.ignoreLayout = true;
                var rect = (RectTransform)button.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(41f, 41f);
            }

            button.SetActive(true);
        }
        catch (Exception error)
        {
            Log.Warning("Notes", "inspect READ button failed: " + error.Message);
        }
    }

    private static bool HasActiveButtons(Transform container)
    {
        if (container == null || !container.gameObject.activeSelf)
        {
            return false;
        }

        for (int i = 0; i < container.childCount; i++)
        {
            if (container.GetChild(i).gameObject.activeSelf)
            {
                return true;
            }
        }

        return false;
    }

    private static Transform Find(Transform root, string name)
    {
        if (root.name == name)
        {
            return root;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = Find(root.GetChild(i), name);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
