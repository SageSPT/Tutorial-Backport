using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using UnityEngine;

namespace TutorialBackport.Client;

internal static class NoteReader
{
    internal sealed class NoteTemplate
    {
        public Vector2Int WindowSize;
        public Quaternion ModelPose;
        public float? CameraDistance;
        public float? FieldOfView;
        public Vector2Int? RenderSize;
    }

    internal const float MeasuredScale = 1.3333334f;

    internal static readonly Dictionary<string, NoteTemplate> Notes = new Dictionary<string, NoteTemplate>
    {
        ["6891bb203bbad2a6230f9be8"] = new NoteTemplate
        {
            WindowSize = new Vector2Int(800, 750), ModelPose = Quaternion.identity,
            CameraDistance = 0.6072f, FieldOfView = 19.435223f, RenderSize = new Vector2Int(1600, 1409)
        },
        ["6891bb67f674f551000a38d3"] = new NoteTemplate
        {
            WindowSize = new Vector2Int(1100, 950), ModelPose = Quaternion.identity,
            CameraDistance = 1.2765f, FieldOfView = 19.893f, RenderSize = new Vector2Int(1600, 1443)
        },
        ["6891bb83d7502d512502d160"] = new NoteTemplate
        {
            WindowSize = new Vector2Int(1300, 950), ModelPose = new Quaternion(0f, -0.70710677f, 0.70710677f, 0f),
            CameraDistance = 1.7448f, FieldOfView = 14.228f, RenderSize = new Vector2Int(1600, 1027)
        },
        ["6891bb9f81d0bf37b10249f4"] = new NoteTemplate
        {
            WindowSize = new Vector2Int(850, 650), ModelPose = new Quaternion(-0.5f, 0.5f, -0.5f, 0.5f),
            CameraDistance = 0.8692f, FieldOfView = 15.455f, RenderSize = new Vector2Int(1600, 1116)
        },
    };

    private static Runner _runner;

    public static bool IsNote(Item item) => item != null && Notes.ContainsKey(item.StringTemplateId);

    public static void Patch(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(InteractionContextHelper), nameof(InteractionContextHelper.GetAvailableInteractionState)),
            postfix: new HarmonyMethod(typeof(NoteReader), nameof(AfterGetAvailableInteractionState)));
    }

    private static void AfterGetAvailableInteractionState(GamePlayerOwner owner, Item rootItem, ref AvailableInteractionState __result)
    {
        if (__result?.Actions == null || !IsNote(rootItem) || owner?.Player == null)
        {
            return;
        }

        int take = __result.Actions.FindIndex(a => a.Name == "Take");
        if (take < 0 || __result.Actions.Any(a => a.Name == "READ"))
        {
            return;
        }

        InteractionAction pickup = __result.Actions[take];
        Player player = owner.Player;
        __result.Actions.Insert(take + 1, new InteractionAction
        {
            Name = "READ",
            TargetName = pickup.TargetName,
            Action = () =>
            {
                pickup.Action();
                Host().StartCoroutine(OpenAfterPickup(player, rootItem));
            }
        });
    }

    private static IEnumerator OpenAfterPickup(Player player, Item note)
    {
        for (float t = 0f; t < 5f; t += Time.unscaledDeltaTime)
        {
            if (player == null || note == null)
            {
                yield break;
            }

            if (LoreQuestItems.IsCarried(player, note))
            {
                Open(note);
                yield break;
            }

            yield return null;
        }

        Log.Warning("Notes", "READ: the note did not reach the inventory within 5 s - window not opened");
    }

    public static void Open(Item note)
    {
        try
        {
            NoteWindow.Show(note, Notes.TryGetValue(note.StringTemplateId, out NoteTemplate template) ? template : Notes["6891bb203bbad2a6230f9be8"]);
        }
        catch (Exception error)
        {
            Log.Error("Notes", "read window failed: " + error);
        }
    }

    internal static Runner Host()
    {
        if (_runner == null)
        {
            var host = new GameObject("TutorialBackport.Notes");
            UnityEngine.Object.DontDestroyOnLoad(host);
            _runner = host.AddComponent<Runner>();
        }

        return _runner;
    }

    internal sealed class Runner : MonoBehaviour
    {
    }
}
