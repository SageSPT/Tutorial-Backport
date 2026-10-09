using System;
using System.Collections;
using System.Linq;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using UnityEngine;

namespace TutorialBackport.Client;

internal static class TapeListen
{
    private static Player _player;
    private static Item _recorder;
    private static Item _tape;
    private static Item _previous;
    private static bool _played;
    private static float _startedAt;
    private static bool _spawned;
    private static bool _stop;
    private static int _action;

    private static readonly System.Collections.Generic.Dictionary<string, string> InsertedTapes = new System.Collections.Generic.Dictionary<string, string>();

    public static bool Busy => _recorder != null;

    public static void Patch(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(InteractionContextHelper), nameof(InteractionContextHelper.GetAvailableInteractionState)),
            postfix: new HarmonyMethod(typeof(TapeListen), nameof(AfterGetAvailableInteractionState)));
        harmony.Patch(AccessTools.Method(typeof(Player.UsableItemController), nameof(Player.UsableItemController.InitializeController)),
            prefix: new HarmonyMethod(typeof(TapeListen), nameof(BeforeInitializeController)),
            postfix: new HarmonyMethod(typeof(TapeListen), nameof(AfterInitializeController)));
        Type spawn = AccessTools.Inner(typeof(Player.UsableItemController), "SpawnOperation");
        harmony.Patch(AccessTools.Method(spawn, "Start", new[] { typeof(Action) }),
            prefix: new HarmonyMethod(typeof(TapeListen), nameof(BeforeSpawn)));
        foreach (string name in new[] { "IEventsConsumerOnMagOut", "IEventsConsumerOnMagIn", "IEventsConsumerOnFiringBullet", "IEventsConsumerOnWeapOut" })
        {
            harmony.Patch(AccessTools.Method(typeof(Player.UsableItemController), name),
                postfix: new HarmonyMethod(typeof(TapeListen), nameof(AfterAnimationEvent)));
        }
    }

    private static void AfterGetAvailableInteractionState(GamePlayerOwner owner, Item rootItem, ref AvailableInteractionState __result)
    {
        if (__result?.Actions == null || owner?.Player == null || !TapeRecorder.IsTape(rootItem) || Recorder(owner.Player) == null)
        {
            return;
        }

        int take = __result.Actions.FindIndex(a => a.Name == "Take");
        if (take < 0 || __result.Actions.Any(a => a.Name == "LISTEN"))
        {
            return;
        }

        InteractionAction pickup = __result.Actions[take];
        Player player = owner.Player;
        __result.Actions.Insert(take + 1, new InteractionAction
        {
            Name = "LISTEN",
            TargetName = pickup.TargetName,
            Action = () =>
            {
                pickup.Action();
                NoteReader.Host().StartCoroutine(ListenAfterPickup(player, rootItem));
            }
        });
    }

    private static Item Recorder(Player player)
    {
        string tpl = TapeRecorder.RecorderTpl;
        return tpl == null ? null : player.InventoryController.Inventory.Equipment.GetAllItems().FirstOrDefault(i => i.StringTemplateId == tpl);
    }

    private static IEnumerator ListenAfterPickup(Player player, Item tape)
    {
        for (float t = 0f; t < 5f; t += Time.unscaledDeltaTime)
        {
            if (player == null || tape == null)
            {
                yield break;
            }

            if (LoreQuestItems.IsCarried(player, tape))
            {
                yield return new WaitForSeconds(0.3f);
                Use(player, tape);
                yield break;
            }

            yield return null;
        }

        Log.Warning("Tapes", "LISTEN: the tape did not reach the inventory within 5 s");
    }

    public static bool Use(Player player, Item tape, bool stop = false)
    {
        Item recorder = Recorder(player);
        if (recorder == null || Busy)
        {
            Log.Warning("Tapes", recorder == null ? "LISTEN: no recorder in the inventory" : "LISTEN: recorder already in use");
            return false;
        }

        _player = player;
        _recorder = recorder;
        _tape = tape;
        _previous = player.HandsController?.Item;
        _played = false;
        _spawned = false;
        _stop = stop;
        _action = stop ? 2 : RecorderAction(player, recorder, tape);
        _startedAt = Time.time;
        try
        {
            player.Proceed<Player.UsableItemController>(recorder, result =>
            {
                if (result.Failed)
                {
                    Log.Warning("Tapes", "LISTEN: recorder could not be taken in hands (" + result.Error + ") - playing without the animation");
                    Finish(false);
                }
            });
        }
        catch (Exception error)
        {
            Log.Error("Tapes", "LISTEN: " + error);
            Finish(false);
            return true;
        }

        NoteReader.Host().StartCoroutine(Watchdog());
        NoteReader.Host().StartCoroutine(WatchStates(player));
        return true;
    }

    private static int RecorderAction(Player player, Item recorder, Item tape)
    {
        string inserted = InsertedTapes.TryGetValue(recorder.Id, out string id) ? id : null;
        if (inserted != null && !player.InventoryController.Inventory.AllRealPlayerItems.Any(i => i.Id == inserted))
        {
            InsertedTapes.Remove(recorder.Id);
            inserted = null;
        }

        bool insert = inserted == null || inserted != tape.Id;
        bool eject = inserted != null && inserted != tape.Id;
        return (insert ? 3 : 1) | (eject ? 4 : 0);
    }

    private static readonly int TapeInPlay = Animator.StringToHash("TAPE IN PLAY");
    private static readonly int ButtonPlay = Animator.StringToHash("BUTTON PLAY");

    private static IEnumerator WatchStates(Player player)
    {
        int last = 0;
        while (Busy && player != null)
        {
            if (player.HandsController is Player.UsableItemController controller && controller.Item == _recorder && controller.FirearmsAnimator?.Animator != null)
            {
                var info = controller.FirearmsAnimator.Animator.GetCurrentAnimatorStateInfo(0);
                int state = info.shortNameHash;
                if (state != last)
                {
                    last = state;
                    if (state == TapeInPlay || state == ButtonPlay)
                    {
                        StartPlayback();
                    }
                }
            }

            yield return null;
        }
    }

    private static void BeforeInitializeController(Player.UsableItemController __instance, Player player, WeaponPrefab weaponPrefab)
    {
        if (weaponPrefab != null && weaponPrefab.FirearmsAnimator == null && player != null)
        {
            weaponPrefab.Init(player, true);
            __instance.AnimationEventsEmitter = weaponPrefab.AnimationEventsEmitter;
            __instance._objectInHandsAnimator = weaponPrefab.FirearmsAnimator;
        }
    }

    private static void AfterInitializeController(Player.UsableItemController __instance, Player player)
    {
        if (player == null || __instance?.Item == null || __instance.Item.StringTemplateId != TapeRecorder.RecorderTpl)
        {
            return;
        }

        BaseSoundPlayer sounds = __instance._controllerObject != null ? __instance._controllerObject.GetComponent<BaseSoundPlayer>() : null;
        if (sounds == null)
        {
            Log.Warning("Tapes", "LISTEN: recorder rig has no sound player");
            return;
        }

        sounds.Init(__instance, player.PlayerBones.WeaponRoot, player);
    }

    private static void BeforeSpawn(object __instance)
    {
        var controller = Traverse.Create(__instance).Field("Controller").GetValue<Player.UsableItemController>();
        if (!Busy || controller == null || controller.Item != _recorder)
        {
            return;
        }

        FirearmsAnimator animator = controller.FirearmsAnimator;
        _spawned = true;
        bool magIn = (_action & 3) == 3;
        bool magOut = ((_action >> 2) & 1) != 0;
        animator.SetMagInWeapon(magIn);
        animator.Animator.SetBool(AnimationControllerParametersTable.BOOL_MAGOUT, magOut);
    }

    private static void AfterAnimationEvent(object __instance, System.Reflection.MethodBase __originalMethod)
    {
        var controller = __instance as Player.UsableItemController;
        if (!Busy || controller == null || controller.Item != _recorder)
        {
            return;
        }

        string name = __originalMethod.Name.Replace("IEventsConsumerOn", "");
        switch (name)
        {
            case "WeapOut":
                Finish(true);
                NoteReader.Host().StartCoroutine(CompleteHide(controller));
                break;
        }
    }

    private static void StartPlayback()
    {
        if (_played)
        {
            return;
        }

        _played = true;
        if (_stop)
        {
            TapeRecorder.StopPlaying();
        }
        else if (_tape != null)
        {
            InsertedTapes[_recorder.Id] = _tape.Id;
            TapeRecorder.PlayItem(_tape);
        }
    }

    private static void Finish(bool restoreHands)
    {
        Player player = _player;
        Item previous = _previous;
        StartPlayback();
        _recorder = null;
        _tape = null;
        _previous = null;
        if (!restoreHands || player == null)
        {
            return;
        }

        if (previous != null && previous.CurrentAddress != null && previous.CurrentAddress.IsChildOf(player.InventoryController.Inventory.Equipment))
        {
            player.TryProceed(previous, _ => { });
        }
        else
        {
            player.SetFirstAvailableItem(_ => { });
        }
    }

    private static IEnumerator CompleteHide(Player.UsableItemController controller)
    {
        for (float t = 0f; t < 2f && controller != null; t += Time.deltaTime)
        {
            if (controller.CurrentOperation is Player.UsableItemController.Remove)
            {
                controller.FastForwardCurrentState();
                yield break;
            }

            yield return null;
        }

        if (controller != null && _player != null && _player.HandsController == controller)
        {
            Log.Warning("Tapes", "LISTEN: hands still on the recorder 2 s after WeapOut");
        }
    }

    private static IEnumerator Watchdog()
    {
        float started = Time.time;
        while (Busy && Time.time - started < 15f)
        {
            if (!_spawned && Time.time - started > 3f)
            {
                Log.Warning("Tapes", "LISTEN: the recorder did not come out within 3 s - hands back, tape plays");
                Finish(true);
                yield break;
            }

            yield return null;
        }

        if (Busy)
        {
            Log.Warning("Tapes", "LISTEN: no WeapOut event within 15 s - restoring hands");
            var controller = _player?.HandsController as Player.UsableItemController;
            Finish(true);
            if (controller != null)
            {
                NoteReader.Host().StartCoroutine(CompleteHide(controller));
            }
        }
    }
}
