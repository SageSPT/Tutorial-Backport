using System.Collections;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace TutorialBackport.Client;

internal class MalfunctionTutorial : MonoBehaviour
{
    private const string BrokenWeaponTpl = "58948c8e86f77409493f7266";
    private static MalfunctionTutorial _instance;
    private static Weapon _armed;

    private enum State
    {
        WaitForTrigger,
        InProgress,
        Completed
    }

    private Player _player;
    private State _state;
    private Weapon _weapon;
    private bool _malfunctioned;

    public static void Patch(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(Player.FirearmController), nameof(Player.FirearmController.GetMalfunctionState)),
            postfix: new HarmonyMethod(typeof(MalfunctionTutorial), nameof(AfterGetMalfunctionState)));
    }

    private static void AfterGetMalfunctionState(Player.FirearmController __instance, ref Weapon.EMalfunctionState __result)
    {
        if (_armed == null || !ReferenceEquals(__instance.Item, _armed))
        {
            return;
        }

        _armed = null;
        __result = Weapon.EMalfunctionState.Misfire;
    }

    public static void OnTutorialRaidStarted(GameWorld world)
    {
        Stop();
        var go = new GameObject("TutorialBackport_MalfunctionTutorial");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<MalfunctionTutorial>();
        _instance._player = world.MainPlayer;
        _instance.StartCoroutine(_instance.Run());
    }

    public static void Stop()
    {
        _armed = null;
        if (_instance != null)
        {
            Destroy(_instance.gameObject);
            _instance = null;
        }
    }

    private IEnumerator Run()
    {
        var wait = new WaitForSeconds(0.25f);
        while (_state != State.Completed && _player != null && _player.HealthController.IsAlive)
        {
            if (_state == State.WaitForTrigger)
            {
                TryStart();
            }
            else
            {
                Watch();
            }

            yield return wait;
        }
    }

    private void TryStart()
    {
        if (!(_player.HandsController is Player.FirearmController hands) || hands.Item == null
            || hands.Item.StringTemplateId != BrokenWeaponTpl || !hands.Item.AllowMisfire || !(hands.CurrentOperation is Player.FirearmController.Idling))
        {
            return;
        }

        _weapon = hands.Item;
        _armed = _weapon;
        _state = State.InProgress;
        StartCoroutine(PullTrigger(hands));
    }

    private IEnumerator PullTrigger(Player.FirearmController hands)
    {
        yield return null;
        if (hands == null || !ReferenceEquals(_player.HandsController, hands) || !(hands.CurrentOperation is Player.FirearmController.Idling))
        {
            yield break;
        }

        hands.SetTriggerPressed(true);
        yield return null;
        if (ReferenceEquals(_player.HandsController, hands))
        {
            hands.SetTriggerPressed(false);
        }
    }

    private void Watch()
    {
        Weapon.EMalfunctionState malf = _weapon.MalfState.State;
        if (malf != Weapon.EMalfunctionState.None)
        {
            _malfunctioned = true;
        }
        else if (_malfunctioned)
        {
            TutorialHints.Active?.OnMalfunctionFixed();
            _state = State.Completed;
        }
    }
}
