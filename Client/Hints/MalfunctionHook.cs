using EFT;
using EFT.InventoryLogic;
using HarmonyLib;

namespace TutorialBackport.Client;

internal static class MalfunctionHook
{
    public static void Patch(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(Player.FirearmController), nameof(Player.FirearmController.ShotMisfired)),
            postfix: new HarmonyMethod(typeof(MalfunctionHook), nameof(AfterMisfire)));
    }

    private static void AfterMisfire(Player.FirearmController __instance, Weapon.EMalfunctionState malfunctionState)
    {
        if (TutorialHints.Active == null || malfunctionState == Weapon.EMalfunctionState.None)
        {
            return;
        }

        Player owner = Traverse.Create(__instance).Field("_player").GetValue<Player>();
        if (owner != null && owner.IsYourPlayer)
        {
            TutorialHints.Active.OnPlayerMisfire();
        }
    }
}
