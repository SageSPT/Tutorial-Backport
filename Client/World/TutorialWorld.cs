using System;
using System.Collections;
using Comfort.Common;
using EFT;
using EFT.GameTriggers;
using EFT.Weather;
using HarmonyLib;
using UnityEngine;

namespace TutorialBackport.Client;

internal static class TutorialWorld
{
    private const int Hour = 6, Minute = 45;
    private const float Cloudness = -0.3f, Wind = 0.2f, Turbulence = 0.2f, Rain = 0f, RainRandomness = 0f;
    private const float FogDensity = 0.008f, Temperature = 20f, Pressure = 760f, LyingWater = 0f;
    private const int WindDirection = 3;

    private static readonly (string trigger, float rotation, float position, float time)[] Shakes =
    {
        ("first_trigger_explosion_1", 0.5f, 0.5f, 1f),
        ("secondl_trigger_explosion_2", 0.3f, 0.3f, 1f),
        ("third_trigger_explosion_3", 0.5f, 0.5f, 1f),
    };

    private static Runner _runner;
    private static bool _timeApplied;

    public static void Patch(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(WeatherController), nameof(WeatherController.SetWeatherNodes)),
            prefix: new HarmonyMethod(typeof(TutorialWorld), nameof(BeforeSetWeatherNodes)));
    }

    private static void BeforeSetWeatherNodes(ref WeatherNode[] nodes)
    {
        if (!TutorialLauncher.InTutorialRaid)
        {
            return;
        }

        long now = DateTime.UtcNow.Ticks;
        nodes = new[] { Node(now - TimeSpan.TicksPerHour), Node(now + TimeSpan.TicksPerDay) };
    }

    private static WeatherNode Node(long time)
    {
        return new WeatherNode
        {
            Time = time,
            Cloudness = Cloudness,
            Wind = Wind,
            WindDirection = WindDirection,
            Turbulence = Turbulence,
            Rain = Rain,
            RainRandomness = RainRandomness,
            ScaterringFogDensity = FogDensity,
            Temperature = Temperature,
            AtmospherePressure = Pressure,
            LyingWater = LyingWater
        };
    }

    public static void ApplyTime()
    {
        if (!TutorialLauncher.InTutorialRaid || !Singleton<GameWorld>.Instantiated)
        {
            return;
        }

        GameDateTime clock = Singleton<GameWorld>.Instance.GameDateTime;
        if (clock == null)
        {
            return;
        }

        DateTime date = clock.Calculate();
        clock.Reset(DateTime.UtcNow, new DateTime(date.Year, date.Month, date.Day, Hour, Minute, 0, DateTimeKind.Utc), clock.TimeFactor, force: true);
        clock.TimeFactorMod = 0f;
        if (!_timeApplied)
        {
            _timeApplied = true;
        }

        WeatherController.Instance?.SetWeatherNodes(Array.Empty<WeatherNode>());
    }

    public static void OnTutorialRaidStarted(GameWorld world)
    {
        ApplyTime();
        _timeApplied = false;
        TriggersEmitter emitter = world.TriggersEmitter;
        if (emitter != null)
        {
            foreach (var shake in Shakes)
            {
                var s = shake;
                emitter.Subscribe(s.trigger, () => Host().StartCoroutine(Shake(s.rotation, s.position, s.time)));
            }
        }

        TutorialLoudness.Apply();
        LampFlicker.OnTutorialRaidStarted();
        AmbientFirefight.OnTutorialRaidStarted();
        MixerRemap.OnTutorialRaidStarted();
    }

    private static IEnumerator Shake(float rotation, float position, float time)
    {
        Player player = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;
        var animation = player?.ProceduralWeaponAnimation;
        ForceEffector force = animation?.ForceReact;
        if (force == null)
        {
            yield break;
        }

        animation.StartCoroutine(LinearExplosionShake(force, rotation, position, time));
    }

    private static IEnumerator LinearExplosionShake(ForceEffector force, float rotationStrength, float positionStrength, float time)
    {
        const float dt = 0.1f;
        for (float t = time; t > 0f; t -= dt)
        {
            force.GrenadeShake(UnityEngine.Random.onUnitSphere, rotationStrength + UnityEngine.Random.Range(0f, 0.15f));
            if (force.CameraPositionSpring != null)
            {
                Vector3 v = UnityEngine.Random.onUnitSphere;
                float magnitude = v.magnitude;
                v = magnitude > 1E-05f ? v / magnitude : Vector3.zero;
                force.CameraPositionSpring.AddAccelerationLimitless(v * (positionStrength * 0.01f));
            }

            yield return new WaitForSeconds(dt);
        }
    }

    private static Runner Host()
    {
        if (_runner == null)
        {
            var host = new GameObject("TutorialBackport.World");
            UnityEngine.Object.DontDestroyOnLoad(host);
            _runner = host.AddComponent<Runner>();
        }

        return _runner;
    }

    private sealed class Runner : MonoBehaviour
    {
    }
}
