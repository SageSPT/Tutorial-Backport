using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace TutorialBackport.Client;

internal static class WakeUpPlayer
{
    public const string ClipName = "player_tutorial_intro";
    private const string ControllerAsset = "Assets/Content/Characters/Controllers/AnimationControllers/PlayerAnimController.controller";
    private const string SoundLibraryAsset = "Assets/Content/Audio/StartMission/IntroAnim/IntroAnimSoundLibrary.asset";
    public const float Length = 19.166668f;

    private static readonly (float time, string sound)[] Sounds =
    {
        (0f, "snd_look_around_01"),
        (0.2f, "snd_fists_on_the_floor_01"),
        (0.39434275f, "snd_get_up_01"),
        (0.60399336f, "snd_shake_oneself_01"),
        (0.72099447f, "snd_shake_oneself_02"),
        (0.83361065f, "snd_shake_oneself_03"),
    };

    private const float FinishRaisingTime = 0.99689925f;

    private static readonly Dictionary<string, (string clip, float volume)> Library = new Dictionary<string, (string, float)>
    {
        ["snd_look_around_01"] = ("intro_anim_look_around_01", 0.8f),
        ["snd_fists_on_the_floor_01"] = ("intro_anim_fists_on_the_floor_01", 0.6f),
        ["snd_get_up_01"] = ("intro_anim_get_up_01", 0.5f),
        ["snd_shake_oneself_01"] = ("intro_anim_shake_oneself_01", 0.6f),
        ["snd_shake_oneself_02"] = ("intro_anim_shake_oneself_02", 0.6f),
        ["snd_shake_oneself_03"] = ("intro_anim_shake_oneself_03", 0.6f),
    };

    private static AnimationClip _clip;
    private static Dictionary<string, AudioClip> _sounds;
    private static AssetBundle _soundBundle;
    private static RuntimeAnimatorController _controller;
    private static Runner _runner;

    private static AnimatorControllerPlayable _live;
    private static HashSet<int> _liveParams;
    private static Player _livePlayer;
    private static bool _curveReadLogged;

    public static bool IsPlaying { get; private set; }

    private static string Dir =>
        Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "bundles", "animations");

    public static IEnumerator Preload()
    {
        if (_clip != null && _sounds != null && _soundBundle != null)
        {
            yield break;
        }

        float started = Time.realtimeSinceStartup;
        AssetBundleCreateRequest animRequest = AssetBundle.LoadFromFileAsync(Path.Combine(Dir, "playeranimcontroller.bundle"));
        yield return animRequest;
        AssetBundle anim = animRequest.assetBundle;
        if (anim != null)
        {
            AssetBundleRequest ctrl = anim.LoadAssetAsync<RuntimeAnimatorController>(ControllerAsset);
            yield return ctrl;
            var controller = ctrl.asset as RuntimeAnimatorController;
            _controller = controller;
            _clip = controller?.animationClips?.FirstOrDefault(c => c != null && c.name == ClipName);
            anim.Unload(false);
        }

        AssetBundleCreateRequest soundRequest = AssetBundle.LoadFromFileAsync(Path.Combine(Dir, "introanimsoundlibrary.bundle"));
        yield return soundRequest;
        AssetBundle snd = soundRequest.assetBundle;
        if (snd != null)
        {
            AssetBundleRequest lib = snd.LoadAssetAsync(SoundLibraryAsset);
            yield return lib;
            var wanted = new HashSet<string>(Library.Values.Select(v => v.clip));
            _sounds = Resources.FindObjectsOfTypeAll<AudioClip>().Where(c => wanted.Contains(c.name))
                .GroupBy(c => c.name).ToDictionary(g => g.Key, g => g.First());
            _soundBundle = snd;
            foreach (AudioClip clip in _sounds.Values)
            {
                clip.LoadAudioData();
            }
        }

        if (_clip == null)
        {
            Log.Error("WakeUp", "player_tutorial_intro not found in " + Dir);
        }
    }

    public static void StartPreload()
    {
        Host().StartCoroutine(Preload());
    }

    public static void Play(Player player, Action onComplete = null)
    {
        if (IsPlaying || player == null)
        {
            return;
        }

        Host().StartCoroutine(Run(player, onComplete));
    }

    private static Runner Host()
    {
        if (_runner == null)
        {
            var host = new GameObject("TutorialBackport.WakeUp");
            UnityEngine.Object.DontDestroyOnLoad(host);
            _runner = host.AddComponent<Runner>();
        }

        return _runner;
    }

    private static IEnumerator Run(Player player, Action onComplete)
    {
        IsPlaying = true;
        yield return Preload();
        Animator animator = (player.BodyAnimatorCommon as AnimationSystem.UnityAnimatorWrapper)?.Animator;
        if (_clip == null || animator == null)
        {
            Log.Error("WakeUp", animator == null ? "player body is not a Unity Animator (fast animator?) - wake-up skipped" : "clip missing - wake-up skipped");
            IsPlaying = false;
            onComplete?.Invoke();
            yield break;
        }

        player.SetEmptyHands(_ => { });
        MuteGuns(true);

        PlayableGraph graph = PlayableGraph.Create("TutorialBackport.WakeUp");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var output = AnimationPlayableOutput.Create(graph, "WakeUp", animator);
        bool controllerMode = _controller != null;
        if (controllerMode)
        {
            _live = AnimatorControllerPlayable.Create(graph, _controller);
            _liveParams = new HashSet<int>();
            for (int i = 0; i < _live.GetParameterCount(); i++)
            {
                _liveParams.Add(_live.GetParameter(i).nameHash);
            }

            _livePlayer = player;
            _curveReadLogged = false;
            _detachLogged = false;
            output.SetSourcePlayable(_live);
            _live.SetTrigger("TutorialAnimation");
        }
        else
        {
            var clipPlayable = AnimationClipPlayable.Create(graph, _clip);
            clipPlayable.SetApplyFootIK(false);
            clipPlayable.SetDuration(Length);
            output.SetSourcePlayable(clipPlayable);
        }

        BeginCameraFollow(player);
        EFT.GamePlayerOwner.SetIgnoreInput(true);
        graph.Play();

        StartBreath(player);
        float start = Time.time;
        int nextSound = 0;
        while (Time.time - start < Length * FinishRaisingTime)
        {
            float t = (Time.time - start) / Length;
            while (nextSound < Sounds.Length && t >= Sounds[nextSound].time)
            {
                PlaySound(player, Sounds[nextSound].sound);
                nextSound++;
            }

            if (player == null || !player.HealthController.IsAlive)
            {
                break;
            }

            _cameraWeight = Mathf.Clamp01((Length * FinishRaisingTime - (Time.time - start)) / CameraBlendOut);
            yield return null;
        }

        _cameraPlayer = null;
        EFT.GamePlayerOwner.SetIgnoreInput(false);
        Host().StartCoroutine(StopBreath(0.5f));
        _livePlayer = null;
        _liveParams = null;
        graph.Destroy();
        if (player != null)
        {
            player.SetFirstAvailableItem(_ => { });
        }

        MuteGuns(false);
        IsPlaying = false;
        onComplete?.Invoke();
    }

    private const string GunsVolume = "GunsVolume";
    private static float? _gunsVolumeBefore;

    private static void MuteGuns(bool mute)
    {
        try
        {
            if (!Singleton<BetterAudio>.Instantiated)
            {
                return;
            }

            UnityEngine.Audio.AudioMixer master = Singleton<BetterAudio>.Instance.Master;
            if (mute)
            {
                if (_gunsVolumeBefore == null && master.GetFloat(GunsVolume, out float current))
                {
                    _gunsVolumeBefore = current;
                }

                master.SetFloat(GunsVolume, -80f);
            }
            else if (_gunsVolumeBefore != null)
            {
                master.SetFloat(GunsVolume, _gunsVolumeBefore.Value);
                _gunsVolumeBefore = null;
            }
        }
        catch (Exception error)
        {
            Log.Warning("WakeUp", "guns mixer volume not changed: " + error.Message);
        }
    }

    public static void RestoreGuns() => MuteGuns(false);

    public static bool BeforeGetCurveValue(Player __instance, int hash, ref float __result)
    {
        if (_livePlayer == null || !ReferenceEquals(__instance, _livePlayer) || _liveParams == null || !_liveParams.Contains(hash) || !_live.IsValid())
        {
            return true;
        }

        __result = Mathf.Min(_live.GetFloat(hash), 1f);
        if (!_curveReadLogged)
        {
            _curveReadLogged = true;
        }

        return false;
    }

    private static readonly int HandRightDetach = Animator.StringToHash("Hand_Right_Detach");
    private static readonly HarmonyLib.AccessTools.FieldRef<Player, RootMotion.FinalIK.LimbIK[]> Limbs =
        HarmonyLib.AccessTools.FieldRefAccess<Player, RootMotion.FinalIK.LimbIK[]>("_limbs");
    private static bool _detachLogged;

    public static void BeforeIkProcess(Player __instance)
    {
        if (_livePlayer == null || !ReferenceEquals(__instance, _livePlayer) || _liveParams == null || !_liveParams.Contains(HandRightDetach) || !_live.IsValid())
        {
            return;
        }

        float firstPerson = __instance.FirstPersonPointOfView ? Mathf.Min(_live.GetFloat(PlayerAnimator.FIRST_PERSON_CURVE_WEIGHT), 1f) : 1f;
        float rightDetach = 1f - Mathf.Min(_live.GetFloat(HandRightDetach), 1f) * firstPerson;
        RootMotion.FinalIK.LimbIK[] limbs = Limbs(__instance);
        if (rightDetach >= 1f || limbs == null || limbs.Length < 2 || limbs[1] == null)
        {
            return;
        }

        limbs[1].solver.IKPositionWeight = rightDetach;
        limbs[1].solver.IKRotationWeight = rightDetach;
        if (!_detachLogged)
        {
            _detachLogged = true;
        }
    }

    public static void Patch(HarmonyLib.Harmony harmony)
    {
        harmony.Patch(HarmonyLib.AccessTools.Method(typeof(Player), nameof(Player.GetCurveValue)),
            prefix: new HarmonyLib.HarmonyMethod(typeof(WakeUpPlayer), nameof(BeforeGetCurveValue)));
        harmony.Patch(HarmonyLib.AccessTools.Method(typeof(Player), nameof(Player.IkProcess)),
            prefix: new HarmonyLib.HarmonyMethod(typeof(WakeUpPlayer), nameof(BeforeIkProcess)));
        harmony.Patch(HarmonyLib.AccessTools.Method(typeof(EFT.CameraControl.FirstPersonCameraState), nameof(EFT.CameraControl.FirstPersonCameraState.ManualLateUpdate)),
            postfix: new HarmonyLib.HarmonyMethod(typeof(WakeUpPlayer), nameof(AfterFirstPersonCamera)));
    }

    private const float CameraBlendOut = 0.5f;
    private static Player _cameraPlayer;
    private static Quaternion _headToCamera;
    private static float _cameraWeight;

    private static float[][] _track;
    private static bool _trackLoaded;
    private static float _trackStart;

    private static readonly Quaternion RaisingRotation = Quaternion.Euler(0f, 135f, 0f);

    private static void LoadTrack()
    {
        if (_trackLoaded)
        {
            return;
        }

        _trackLoaded = true;
        string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(WakeUpPlayer).Assembly.Location), "data", "wakeup_camera.json");
        try
        {
            if (System.IO.File.Exists(path))
            {
                var data = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
                _track = data["keys"].ToObject<float[][]>();
            }
        }
        catch (Exception error)
        {
            Log.Warning("WakeUp", "wake-up camera track not loaded (" + error.Message + "); the camera follows the head instead");
        }
    }

    private static void BeginCameraFollow(Player player)
    {
        player.Transform.rotation = RaisingRotation;
        Transform head = player.PlayerBones?.Head?.Original;
        if (head == null || player.CameraPosition == null)
        {
            return;
        }

        LoadTrack();
        _headToCamera = Quaternion.Inverse(head.rotation) * player.CameraPosition.rotation;
        _cameraWeight = 1f;
        _trackStart = Time.time;
        _cameraPlayer = player;
    }

    private static void AfterFirstPersonCamera(EFT.CameraControl.FirstPersonCameraState __instance)
    {
        Player player = _cameraPlayer;
        Transform camera = __instance._cameraTransform;
        if (player == null || camera == null || !player.IsYourPlayer || _cameraWeight <= 0f)
        {
            return;
        }

        Transform root = player.Transform.Original;
        if (_track != null && _track.Length > 1 && root != null)
        {
            SampleTrack(Time.time - _trackStart, out Vector3 position, out Quaternion rotation);
            camera.rotation = Quaternion.Slerp(camera.rotation, root.rotation * rotation, _cameraWeight);
            camera.position = Vector3.Lerp(camera.position, root.TransformPoint(position), _cameraWeight);
            return;
        }

        Transform head = player.PlayerBones?.Head?.Original;
        if (head != null)
        {
            camera.rotation = Quaternion.Slerp(camera.rotation, head.rotation * _headToCamera, _cameraWeight);
        }
    }

    private static void SampleTrack(float time, out Vector3 position, out Quaternion rotation)
    {
        int hi = 1;
        while (hi < _track.Length - 1 && _track[hi][0] < time)
        {
            hi++;
        }

        float[] a = _track[hi - 1], b = _track[hi];
        float f = b[0] > a[0] ? Mathf.Clamp01((time - a[0]) / (b[0] - a[0])) : 0f;
        position = Vector3.Lerp(new Vector3(a[1], a[2], a[3]), new Vector3(b[1], b[2], b[3]), f);
        rotation = Quaternion.Slerp(new Quaternion(a[4], a[5], a[6], a[7]), new Quaternion(b[4], b[5], b[6], b[7]), f);
    }

    private static BetterSource PlayerSource(Player player, BetterAudio.AudioSourceGroupType group, Transform bone,
        Func<BetterAudio, UnityEngine.Audio.AudioMixerGroup> mixer, bool track = true)
    {
        if (!Singleton<BetterAudio>.Instantiated)
        {
            return null;
        }

        BetterAudio audio = Singleton<BetterAudio>.Instance;
        BetterSource source = audio.GetSource(group, true, true);
        if (source == null)
        {
            return null;
        }

        source.ResetFilters();
        source.ResetOcclusion();
        source.SetMixerGroup(mixer(audio) ?? audio.MasterMixerGroup);
        source.EnableStereo(true);
        if (track)
        {
            source.StartTrackingPosition(bone ?? player.Transform.Original);
        }
        else
        {
            source.StopTrackingPosition();
            source.LocalPosition = Vector3.zero;
        }

        return source;
    }

    private static bool _groupLogged;

    private static UnityEngine.Audio.AudioMixerGroup ClientPlayerGroup(BetterAudio audio)
    {
        UnityEngine.Audio.AudioMixerGroup group = null;
        try
        {
            group = audio.Master.FindMatchingGroups("ClientPlayer").FirstOrDefault(g => g != null && g.name == "ClientPlayer");
        }
        catch (Exception)
        {
        }

        if (!_groupLogged)
        {
            _groupLogged = true;
        }

        return group ?? audio.ClientPlayerMovementMixer;
    }

    private static BetterSource _breath;

    private static void StartBreath(Player player)
    {
        try
        {
            BaseSpeaker speaker = player.Speaker;
            if (speaker == null || !Singleton<Diz.Resources.IEasyAssets>.Instance.TryGetAsset<Voice>(out Voice voice, InGameBundles.TakePhrasePath(speaker.PlayerVoice)))
            {
                Log.Warning("WakeUp", "breathing: voice bank not loaded");
                return;
            }

            TagBank bank = voice.Banks.FirstOrDefault(b => b.Trigger == EPhraseTrigger.OnBreath);
            ETagStatus tags = ETagStatus.BadlyInjured | Singleton<GameWorld>.Instance.SpeakerManager.GetGroupStatus(speaker);
            TaggedClip clip = bank?.Match((int)tags);
            if (clip?.Clip == null)
            {
                Log.Warning("WakeUp", "breathing: no OnBreath / BadlyInjured clip in " + speaker.PlayerVoice);
                return;
            }

            _breath = PlayerSource(player, BetterAudio.AudioSourceGroupType.Speech, null, a => a.ClientPlayerSpeechMixer, track: false);
            if (_breath == null)
            {
                Log.Warning("WakeUp", "breathing: no free speech source");
                return;
            }

            _breath.Loop = true;
            _breath.SetRolloff(40f);
            _breath.SetPriority(128);
            SourceTuning.Apply(_breath, SourceTuning.LogCurve, 78f, double.MaxValue);
            _breath.Play(clip.Clip, null, 1f, 0.5f, forceStereo: true, oneShot: false);
        }
        catch (Exception error)
        {
            Log.Warning("WakeUp", "breathing failed: " + error.Message);
        }
    }

    private static IEnumerator StopBreath(float fade)
    {
        BetterSource source = _breath;
        _breath = null;
        if (source == null)
        {
            yield break;
        }

        source.Stop(fade);
        yield return new WaitForSeconds(fade + 0.1f);
        if (source != null)
        {
            source.Loop = false;
            SourceTuning.RestoreNow(source);
            yield return null;
            source.Release();
        }
    }

    private static void PlaySound(Player player, string key)
    {
        if (player == null || _sounds == null || !Library.TryGetValue(key, out var entry) || !_sounds.TryGetValue(entry.clip, out AudioClip clip))
        {
            Log.Warning("WakeUp", "sound not available: " + key);
            return;
        }

        BetterSource source = PlayerSource(player, BetterAudio.AudioSourceGroupType.Character, player.PlayerBones?.Ribcage?.Original, ClientPlayerGroup);
        if (source == null)
        {
            Log.Warning("WakeUp", "no free source for " + key);
            return;
        }

        double end = AudioSettings.dspTime + clip.length + 0.2;
        source.SetRolloff(70f);
        source.SetPriority(0);
        SourceTuning.Apply(source, SourceTuning.IntroCurve, 78f, end);
        source.Play(clip, null, 1f, entry.volume, forceStereo: true, oneShot: false);
        Singleton<BetterAudio>.Instance.AddToAudioSourceQueue(source, end);
    }

    private sealed class Runner : MonoBehaviour
    {
    }
}
