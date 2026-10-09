using System;
using System.Linq;
using System.Threading.Tasks;
using EFT;
using HarmonyLib;

namespace TutorialBackport.Client;

internal static class TutorialKit
{
    private static Profile _original;
    private static Profile _tutorial;

    public sealed class TutorProfileResponse
    {
        public ProfileDescriptor profile;
    }

    public static void Patch(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(TarkovApplication), nameof(TarkovApplication.HandleError)),
            postfix: new HarmonyMethod(typeof(TutorialKit), nameof(AfterHandleError)));
    }

    public static async Task Apply(TarkovApplication app)
    {
        if (!(app.Session is ClientBackendSession session))
        {
            return;
        }

        try
        {
            string main = session._backendUrls?.Main;
            var response = await session.Send<TutorProfileResponse>(new SendRequest
            {
                Url = main + "/client/tutor-game/profile",
                Retries = SendRequest.DefaultRetries
            });
            if (response?.profile == null)
            {
                Log.Warning("Raid", "tutor-game/profile returned no profile - the raid keeps your own gear");
                return;
            }

            var profile = new Profile(response.profile);
            if (profile.Inventory?.DeserializationErrors?.Any() == true)
            {
                Log.Warning("Raid", $"tutorial kit: {profile.Inventory.DeserializationErrors.Count} item(s) could not be read - keeping your own gear");
                return;
            }

            _original = session.Profile;
            _tutorial = profile;
            session.Profile = profile;
        }
        catch (Exception error)
        {
            Log.Warning("Raid", "tutorial kit unavailable, the raid keeps your own gear: " + error.Message);
        }
    }

    private static void AfterHandleError(TarkovApplication __instance)
    {
        if (_original == null || !(__instance.Session is ClientBackendSession session))
        {
            return;
        }

        if (ReferenceEquals(session.Profile, _tutorial))
        {
            session.Profile = _original;
        }

        _original = null;
        _tutorial = null;
    }
}
