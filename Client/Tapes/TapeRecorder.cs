using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace TutorialBackport.Client;

internal class TapeRecorder : MonoBehaviour
{
    private static TapeRecorder _instance;
    private static TapeFile _data;

    private readonly HashSet<string> _seenItems = new HashSet<string>();
    private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
    private Player _player;
    private TapeInfo _lastTape;
    private string _lastTapeName;
    private AudioSource _source;
    private TapeInfo _playing;
    private float _pollAt;
    private bool _loading;

    private RectTransform _subtitleBox;
    private TextMeshProUGUI _subtitleText;

    private static string Dir => Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "data");

    public static string KeyLabel => Plugin.TapeKey.Value.MainKey.ToString();

    public static void OnRaidStarted(GameWorld world)
    {
        if (_data == null && !LoadData())
        {
            return;
        }

        EnsureInstance();
        _instance.Begin(world.MainPlayer);
    }

    private static void EnsureInstance()
    {
        if (_instance == null)
        {
            var go = new GameObject("TutorialBackport_TapeRecorder");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<TapeRecorder>();
        }
    }

    public static void PlayFromMenu(Item item)
    {
        if (!IsTape(item))
        {
            return;
        }

        EnsureInstance();
        TapeRecorder recorder = _instance;
        if (recorder._player != null && recorder._player.HealthController.IsAlive)
        {
            if (!recorder.Items().Any(i => i.StringTemplateId == _data.Recorder))
            {
                recorder.ShowMessage(Localize("Tapes/EquipRecorderRaid", "Equip recorder first"));
                return;
            }

            if (TapeListen.Busy || TapeListen.Use(recorder._player, item))
            {
                return;
            }
        }

        PlayItem(item);
    }

    public static void StopFromMenu()
    {
        if (_instance == null || _instance._playing == null)
        {
            return;
        }

        if (_instance._player != null && !_instance._stopping)
        {
            _instance.StartCoroutine(_instance.StopLikeRecorder());
        }
        else
        {
            _instance.Stop();
        }
    }

    public static bool IsPlaying => _instance != null && _instance._playing != null;

    public static void OnRaidEnded()
    {
        _instance?.End();
    }

    private static bool LoadData()
    {
        string path = Path.Combine(Dir, "tapes.json");
        try
        {
            _data = JsonConvert.DeserializeObject<TapeFile>(File.ReadAllText(path));
            return _data?.Tapes != null;
        }
        catch (Exception error)
        {
            Log.Error("Tapes", "tape data unavailable (" + path + "): " + error.Message);
            return false;
        }
    }

    private void Begin(Player player)
    {
        StartCoroutine(LoadStopClip());
        End();
        _player = player;
        _seenItems.Clear();
        foreach (Item item in Items())
        {
            _seenItems.Add(item.Id);
            TapeInfo tape = Tape(item);
            if (tape != null)
            {
                _lastTape = tape;
                _lastTapeName = item.LocalizedName();
            }
        }
    }

    private void End()
    {
        Stop();
        _player = null;
    }

    private IEnumerable<Item> Items()
    {
        if (_player == null || _player.Inventory == null)
        {
            return Enumerable.Empty<Item>();
        }

        try
        {
            return _player.Inventory.AllRealPlayerItems.ToList();
        }
        catch (Exception)
        {
            return Enumerable.Empty<Item>();
        }
    }

    public static bool IsTape(Item item) => item != null && (_data != null || LoadData()) && Tape(item) != null;

    public static string RecorderTpl => _data != null || LoadData() ? _data.Recorder : null;

    public static void PlayItem(Item item)
    {
        TapeInfo tape = item != null && (_data != null || LoadData()) ? Tape(item) : null;
        if (_instance == null || tape == null)
        {
            return;
        }

        _instance._seenItems.Add(item.Id);
        _instance._lastTape = tape;
        _instance._lastTapeName = item.LocalizedName();
        _instance.Stop();
        if (!_instance._loading)
        {
            _instance.StartCoroutine(_instance.Play(tape));
        }
    }

    public static void StopPlaying() => _instance?.Stop();

    private static TapeInfo Tape(Item item)
    {
        string tpl = item.StringTemplateId;
        return _data.Tapes.FirstOrDefault(t => t.Tpl == tpl);
    }

    private void Update()
    {
        if (_player == null)
        {
            if (_playing != null)
            {
                UpdateUi();
            }

            return;
        }

        if (!_player.HealthController.IsAlive)
        {
            End();
            return;
        }

        if (Time.time >= _pollAt)
        {
            _pollAt = Time.time + 0.5f;
            PollInventory();
        }

        if (Plugin.TapeKey.Value.IsDown() && !IntroPlayer.IsPlaying && !WakeUpPlayer.IsPlaying)
        {
            Toggle();
        }

        UpdateUi();
    }

    private void PollInventory()
    {
        bool carryingPlayed = _playing == null;
        foreach (Item item in Items())
        {
            TapeInfo tape = Tape(item);
            if (tape != null && tape == _playing)
            {
                carryingPlayed = true;
            }

            if (!_seenItems.Add(item.Id) || tape == null)
            {
                continue;
            }

            _lastTape = tape;
            _lastTapeName = item.LocalizedName();
            ShowFound();
        }

        if (!carryingPlayed)
        {
            Stop();
        }
    }

    private void Toggle()
    {
        if (TapeListen.Busy)
        {
            return;
        }

        if (_playing != null)
        {
            if (!_stopping)
            {
                StartCoroutine(StopLikeRecorder());
            }

            return;
        }

        if (_lastTape == null)
        {
            return;
        }

        if (!Items().Any(i => i.StringTemplateId == _data.Recorder))
        {
            ShowMessage(Localize("Tapes/EquipRecorderRaid", "Equip recorder first"));
            return;
        }

        if (_loading)
        {
            return;
        }

        Item tapeItem = Items().FirstOrDefault(i => Tape(i) == _lastTape);
        if (tapeItem != null && TapeListen.Use(_player, tapeItem))
        {
            return;
        }

        StartCoroutine(Play(_lastTape));
    }

    private IEnumerator Play(TapeInfo tape)
    {
        if (!_clips.TryGetValue(tape.TapeId, out AudioClip clip))
        {
            _loading = true;
            string path = Path.Combine(Dir, tape.Audio);
            using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip("file:///" + path.Replace('\\', '/'), AudioType.OGGVORBIS))
            {
                yield return request.SendWebRequest();
                _loading = false;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Log.Error("Tapes", "could not load " + path + ": " + request.error);
                    yield break;
                }

                {
                    clip = DownloadHandlerAudioClip.GetContent(request);
                }

                clip.name = tape.TapeId;
                _clips[tape.TapeId] = clip;
            }
        }

        if (_source == null)
        {
            var go = new GameObject("TutorialBackport_TapeAudio");
            go.transform.SetParent(_player != null ? _player.Transform.Original : transform, false);
            _source = go.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            if (Singleton<BetterAudio>.Instantiated && Singleton<BetterAudio>.Instance.MasterMixerGroup != null)
            {
                _source.outputAudioMixerGroup = Singleton<BetterAudio>.Instance.MasterMixerGroup;
            }
        }

        _source.clip = clip;
        _source.volume = _data.Playback?.BaseVolume ?? 0.68f;
        _source.Play();
        _playing = tape;
        _progress?.Close();
        _progress = new TapeProgressNotification(_lastTapeName, tape.Length);
        NotificationManager.DisplayNotification(_progress);
        LoreContextMenu.MarkCompleted(tape.Tpl);
    }

    private bool _stopping;

    private IEnumerator StopLikeRecorder()
    {
        _stopping = true;
        PlayStopSound();
        bool animate = _player != null && !_player.IsInventoryOpened;
        if (animate)
        {
            _player.SetInventoryOpened(true);
        }

        yield return new WaitForSeconds(0.6f);
        if (animate && _player != null && _player.IsInventoryOpened && !EFT.UI.Screens.EftScreenManager.Instance.CheckCurrentScreen(EFT.UI.Screens.EEftScreenType.Inventory))
        {
            _player.SetInventoryOpened(false);
        }

        _stopping = false;
        Stop();
    }

    private void PlayStopSound()
    {
        if (_stopClip == null || _player == null || !Singleton<BetterAudio>.Instantiated)
        {
            return;
        }

        Vector3 head = _player.PlayerBones?.Head?.Original != null ? _player.PlayerBones.Head.Original.position : _player.Position + Vector3.up * 1.6f;
        Camera camera = Camera.main;
        float distance = camera != null ? Vector3.Distance(camera.transform.position, head) : 0f;
        Singleton<BetterAudio>.Instance.PlayAtPoint(head, _stopClip, distance, BetterAudio.AudioSourceGroupType.Character, 5);
    }

    private AudioClip _stopClip;

    private IEnumerator LoadStopClip()
    {
        if (_stopClip != null)
        {
            yield break;
        }

        string path = Path.Combine(Dir, "sounds", "recorder_button_off_with_cloth.wav");
        using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip("file:///" + path.Replace('\\', '/'), AudioType.WAV))
        {
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                Log.Warning("Tapes", "recorder stop sound not loaded: " + request.error);
                yield break;
            }

            _stopClip = DownloadHandlerAudioClip.GetContent(request);
            _stopClip.name = "recorder_button_off_with_cloth";
        }
    }

    private void Stop()
    {
        if (_source != null)
        {
            _source.Stop();
            Destroy(_source.gameObject);
            _source = null;
        }

        _playing = null;
        _progress?.Close();
        _progress = null;
        if (_subtitleBox != null)
        {
            _subtitleBox.gameObject.SetActive(false);
        }
    }

    private TapeProgressNotification _progress;

    private const float FoundNotificationDuration = 0.2f;

    private void ShowFound()
    {
        bool hasRecorder = Items().Any(i => i.StringTemplateId == _data.Recorder);
        NotificationManager.DisplayNotification(new TapeCollectedNotification(_lastTapeName, hasRecorder, FoundNotificationDuration));
    }

    private void ShowMessage(string text)
    {
        NotificationManager.DisplayWarningNotification(text);
    }

    private void UpdateUi()
    {
        bool playing = _playing != null && _source != null && _source.isPlaying;
        if (_playing != null && _source != null && !_source.isPlaying && _source.time <= 0f && !_loading)
        {
            Stop();
            return;
        }

        if (playing)
        {
            _progress?.SetTime(_source.time);
        }

        string subtitle = null;
        if (playing && Plugin.TapeSubtitles.Value)
        {
            float t = _source.time;
            TapeSubtitle line = _playing.Subtitles?.FirstOrDefault(s => t >= s.Start && t < s.End);
            if (line != null)
            {
                subtitle = Localize(line.Id, "");
            }
        }

        if (_subtitleBox == null)
        {
            if (string.IsNullOrEmpty(subtitle))
            {
                return;
            }

            BuildUi();
        }

        _subtitleBox.gameObject.SetActive(!string.IsNullOrEmpty(subtitle));
        if (!string.IsNullOrEmpty(subtitle) && _subtitleText.text != subtitle)
        {
            _subtitleText.text = subtitle;
        }

        if (_subtitleText != null && !Mathf.Approximately(_subtitleText.fontSize, SubtitleSize))
        {
            _subtitleText.enableAutoSizing = false;
            _subtitleText.fontSize = SubtitleSize;
        }
    }

    private const float SubtitleSize = 18f;

    private static string Localize(string key, string fallback)
    {
        try
        {
            string value = key.Localized(EStringCase.None);
            return string.IsNullOrEmpty(value) || value == key ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }

    private void BuildUi()
    {
        if (_subtitleBox != null)
        {
            return;
        }

        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 110;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 1f;

        _subtitleBox = UiHelpers.Child("SubtitlesView", transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 140.42f), new Vector2(800f, 80.84f));
        var box = _subtitleBox.gameObject.AddComponent<VerticalLayoutGroup>();
        box.padding = new RectOffset(0, 0, 0, 0);
        box.childAlignment = TextAnchor.UpperLeft;
        box.childControlWidth = true;
        box.childControlHeight = true;
        box.childForceExpandWidth = true;
        box.childForceExpandHeight = false;
        _subtitleBox.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        UiHelpers.Fill("Background", _subtitleBox, new Color(0f, 0f, 0f, 0.6980392f), null, 1f);
        Sprite border = TapeNotificationViews.Load("border_generic", new Vector4(2f, 2f, 2f, 2f));
        UiHelpers.Fill("Border", _subtitleBox, new Color(0.32156864f, 0.34901962f, 0.3529412f, 1f), border, 1f);
        _subtitleText = UiHelpers.Text("Text", _subtitleBox, "", SubtitleSize, TextAlignmentOptions.TopLeft);
        _subtitleText.enableAutoSizing = false;
        _subtitleText.enableWordWrapping = true;
        _subtitleText.richText = true;
        _subtitleText.color = new Color(0.84313726f, 0.8509804f, 0.8509804f, 1f);
        _subtitleText.margin = new Vector4(18f, 8f, 18f, 12f);
        _subtitleBox.gameObject.SetActive(false);
    }
}
