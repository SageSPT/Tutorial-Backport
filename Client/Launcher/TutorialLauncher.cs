using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EFT;
using EFT.Weather;
using HarmonyLib;
using JsonType;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;

namespace TutorialBackport.Client;

internal static class TutorialLauncher
{
    private static readonly HashSet<string> LaunchedThisSession = new HashSet<string>();
    private static readonly HashSet<string> NoTutorial = new HashSet<string>();
    private static string _pendingProfile;
    private static Task<bool> _pendingCheck;
    public static bool IntroPending;

    public static bool InTutorialRaid;

    public static void Patch(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(TarkovApplication), nameof(TarkovApplication.MainMenu)),
            prefix: new HarmonyMethod(typeof(TutorialLauncher), nameof(BeforeMainMenu)));
        harmony.Patch(AccessTools.Method(typeof(TarkovApplication), nameof(TarkovApplication.OnApplicationLoaded)),
            postfix: new HarmonyMethod(typeof(TutorialLauncher), nameof(AfterApplicationLoaded)));
        LoadingCover.Patch(harmony);
    }

    private static void BeforeMainMenu(TarkovApplication __instance)
    {
        try
        {
            string profileId = __instance.Session?.Profile?.Id;
            if (!Plugin.AutoLaunch.Value || string.IsNullOrEmpty(profileId) || LaunchedThisSession.Contains(profileId)
                || NoTutorial.Contains(profileId))
            {
                return;
            }

            _pendingProfile = profileId;
            _pendingCheck = Check();
            LoadingCover.Hold();
        }
        catch (Exception error)
        {
            LoadingCover.Release();
            Log.Error("FirstLaunch", "tutorial check failed: " + error.Message);
        }
    }

    private static async Task<bool> Check()
    {
        string json = await RequestHandler.PostJsonAsync("/client/tutor-game/check", "{\"skipTutorial\":false}");
        return JObject.Parse(json)["data"]?["launchTutorGame"]?.Value<bool>() == true;
    }

    private static void AfterApplicationLoaded(TarkovApplication __instance)
    {
        if (!Plugin.AutoLaunch.Value)
        {
            return;
        }

        CheckAndLaunch(__instance).ContinueWith(t =>
        {
            if (t.Exception != null)
            {
                Log.Error("FirstLaunch", "tutorial launch failed: " + t.Exception.GetBaseException());
            }

            LoadingCover.Release();
        });
    }

    private static async Task CheckAndLaunch(TarkovApplication app)
    {
        string profileId = app.Session?.Profile?.Id;
        if (string.IsNullOrEmpty(profileId) || LaunchedThisSession.Contains(profileId) || NoTutorial.Contains(profileId))
        {
            return;
        }

        Task<bool> check = _pendingProfile == profileId && _pendingCheck != null ? _pendingCheck : Check();
        _pendingProfile = null;
        _pendingCheck = null;
        if (!await check)
        {
            NoTutorial.Add(profileId);
            return;
        }

        LaunchedThisSession.Add(profileId);
        await MainThread.Run(() => Launch(app));
    }

    private static async Task Launch(TarkovApplication app)
    {
        LocationSettings locations = app.Session.LocationSettings;
        if (locations?.locations == null || !locations.locations.TryGetValue(TutorialIdsFromServer.LocationKey(locations), out LocationSettings.Location location))
        {
            Log.Error("Raid", "Sandbox_start is not in the location list - is the TutorialBackport server mod installed?");
            return;
        }

        RaidSettings raid = app._raidSettings;
        raid.SelectedLocation = location;
        raid.Side = ESideType.Pmc;
        raid.SelectedDateTime = EDateTime.CURR;
        raid.KeyId = null;
        raid.isInTransition = false;
        raid.transitionType = ELocationTransition.None;
        raid.MetabolismDisabled = false;
        AccessTools.Property(typeof(RaidSettings), nameof(RaidSettings.RaidMode)).SetValue(raid, ERaidMode.Local);
        AccessTools.Property(typeof(RaidSettings), nameof(RaidSettings.PlayersSpawnPlace)).SetValue(raid, EPlayersSpawnPlace.SamePlace);

        TimeAndWeatherSettings weather = raid.TimeAndWeatherSettings;
        weather.IsRandomTime = false;
        weather.IsRandomWeather = false;
        weather.CloudinessType = ECloudinessType.Clear;
        weather.RainType = ERainType.NoRain;
        weather.WindType = EWindSpeed.Light;
        weather.FogType = EFogType.NoFog;
        weather.TimeFlowType = ETimeFlowType.x0;
        weather.HourOfDay = 7;

        if (!await SceneBundleHost.EnsureLoaded())
        {
            Log.Error("Raid", "tutorial scenes unavailable - not starting the tutorial raid");
            return;
        }

        await TutorialKit.Apply(app);

        ThirdPartyCompat.PrepareForTutorial();
        WakeUpPlayer.StartPreload();
        IntroPending = true;
        InTutorialRaid = true;
        if (app.Matchmaker != null)
        {
            app.Matchmaker.MatchingStartTime = DateTimeExtensions.Now;
        }

        Task matching = app.OnReadyToStartMatchingAsync();
        LoadingCover.Release();
        await matching;
    }
}
