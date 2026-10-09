using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Comfort.Common;
using EFT;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.Video;

namespace TutorialBackport.Client;

public class IntroPlayer : MonoBehaviour
{
    private const string Font = "Jovanny Lemonad - Bender Normal SDF";
    private const float SkipHoldSeconds = 1.5f;
    private const float ToggleStartSeconds = 8f, ToggleReopenSeconds = 4.5f, ToggleFadeSeconds = 0.5f;
    private static IntroPlayer _current;

    private Action _onComplete;
    private VideoPlayer _video;
    private AudioSource _audio;
    private RenderTexture _texture;
    private CanvasGroup _root;
    private CanvasGroup _videoGroup;
    private CanvasGroup _skipGroup;
    private CanvasGroup _toggleGroup;
    private CanvasGroup _subtitleGroup;
    private Image _skipFill;
    private TextMeshProUGUI _toggleLabel;
    private TextMeshProUGUI _subtitleText;
    private RectTransform _subtitlePanel;
    private readonly List<(double start, double end, string key)> _lines = new List<(double, double, string)>();
    private bool _subtitlesVisible = true;
    private float _toggleHideAt;
    private float _skipProgress;
    private bool _finished;
    private string _shownText = "";
    private float _baseVolume = 1f;

    private static float OverallVolume()
    {
        try
        {
            int index = Singleton<EFT.Settings.SettingsManager>.Instance.Sound.Settings.OverallVolume.Value;
            int[] steps = EFT.Settings.Sound.SoundSettingsGroup._realSoundValues;
            float db = steps[Mathf.Clamp(index, 0, steps.Length - 1)];
            return db <= -80f ? 0f : Mathf.Pow(10f, db / 20f);
        }
        catch (Exception)
        {
            return 1f;
        }
    }

    public static bool IsPlaying => _current != null;

    public static void Play(Action onComplete)
    {
        if (_current != null)
        {
            return;
        }

        var go = new GameObject("TutorialBackport_IntroVideoScreen");
        DontDestroyOnLoad(go);
        _current = go.AddComponent<IntroPlayer>();
        _current._onComplete = onComplete;
    }

    private void Start()
    {
        BuildUi();
        CloneVersionLabel();
        HideMenuTaskBar();
        StartCoroutine(Run());
    }

    private static string PluginIntroDir => Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "intro");

    private static string MediaPath(string file)
    {
        foreach (string dir in new[] { PluginIntroDir, Path.Combine(Application.streamingAssetsPath, "Intro") })
        {
            string path = Path.Combine(dir, file);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private IEnumerator Run()
    {
        string videoPath = MediaPath("IntroVideo.mp4");
        if (videoPath == null)
        {
            Log.Error("Intro", "IntroVideo.mp4 not found in " + PluginIntroDir + " or StreamingAssets/Intro - the intro video is not installed");
            Finish();
            yield break;
        }

        yield return LoadSubtitles();

        string language = DetectLanguage();
        string audioPath = MediaPath("IntroSound-" + language + ".ogg") ?? MediaPath("IntroSound-" + language + ".wav");
        AudioClip clip = null;
        if (audioPath != null)
        {
            AudioType type = audioPath.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ? AudioType.OGGVORBIS : AudioType.WAV;
            using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip("file:///" + audioPath.Replace('\\', '/'), type))
            {
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    clip = DownloadHandlerAudioClip.GetContent(request);
                }
                else
                {
                    Log.Warning("Intro", "audio load failed: " + request.error);
                }
            }
        }
        else
        {
            Log.Warning("Intro", "no IntroSound-" + language + ".ogg/.wav found; the video's own audio track is used");
        }

        _texture = new RenderTexture(2560, 1440, 0);
        _video.targetTexture = _texture;
        _video.url = videoPath;
        _video.audioOutputMode = clip != null ? VideoAudioOutputMode.None : VideoAudioOutputMode.AudioSource;
        if (clip == null)
        {
            _video.SetTargetAudioSource(0, _audio);
        }

        _video.Prepare();
        float timeout = Time.realtimeSinceStartup + 30f;
        while (!_video.isPrepared && Time.realtimeSinceStartup < timeout)
        {
            yield return null;
        }

        if (!_video.isPrepared)
        {
            Log.Error("Intro", "video did not prepare within 30 s; skipping intro");
            Finish();
            yield break;
        }

        _videoGroup.GetComponent<RawImage>().texture = _texture;
        PauseWorldCameras();
        _video.loopPointReached += _ => Finish();
        _video.Play();
        if (clip != null)
        {
            _audio.clip = clip;
            _audio.Play();
        }

        _toggleHideAt = Time.realtimeSinceStartup + ToggleStartSeconds;
        for (float t = 0; t < 1f; t += Time.unscaledDeltaTime * 2f)
        {
            _videoGroup.alpha = t;
            yield return null;
        }

        _videoGroup.alpha = 1f;
    }

    private IEnumerator LoadSubtitles()
    {
        var task = RequestHandler.GetJsonAsync("/client/subtitle-track/list");
        while (!task.IsCompleted)
        {
            yield return null;
        }

        try
        {
            foreach (JToken track in (JArray)JObject.Parse(task.Result)["data"])
            {
                if ((string)track["id"] != TutorialIds.IntroTrackId)
                {
                    continue;
                }

                foreach (JToken line in (JArray)track["subtitles"])
                {
                    _lines.Add(((double)line["start"], (double)line["end"], (string)line["id"]));
                }
            }
        }
        catch (Exception error)
        {
            Log.Warning("Subtitle", "subtitle track unavailable: " + error.Message);
        }
    }

    private string DetectLanguage()
    {
        if (_lines.Count == 0)
        {
            return "en";
        }

        string sample = Localize(_lines[0].key);
        foreach (char c in sample)
        {
            if (c >= 'Ѐ' && c <= 'ӿ')
            {
                return "ru";
            }
        }

        return "en";
    }

    private static string Localize(string key)
    {
        try
        {
            return key.Localized(EStringCase.None);
        }
        catch
        {
            return key;
        }
    }

    private void Update()
    {
        if (_finished || _video == null)
        {
            return;
        }

        if (Input.GetKeyDown(Plugin.SubtitleToggleKey.Value))
        {
            _subtitlesVisible = !_subtitlesVisible;
            _toggleHideAt = Time.realtimeSinceStartup + ToggleReopenSeconds;
            _toggleLabel.text = _subtitlesVisible ? "Subtitles: On" : "Subtitles: Off";
        }

        bool toggleShown = Time.realtimeSinceStartup < _toggleHideAt;
        _toggleGroup.alpha = toggleShown ? 1f : Mathf.MoveTowards(_toggleGroup.alpha, 0f, Time.unscaledDeltaTime / ToggleFadeSeconds);

        bool holding = Input.GetKey(Plugin.SkipKey.Value) || (Plugin.SkipKeyAlternate.Value != KeyCode.None && Input.GetKey(Plugin.SkipKeyAlternate.Value));
        float hold = SkipHoldSeconds;
        _skipProgress = Mathf.Clamp01(_skipProgress + (holding ? 1f : -2f) * Time.unscaledDeltaTime / hold);
        _skipFill.fillAmount = _skipProgress;
        _skipGroup.alpha = Mathf.MoveTowards(_skipGroup.alpha, holding || _skipProgress > 0 ? 1f : 0f, Time.unscaledDeltaTime * 4f);
        if (_skipProgress >= 1f)
        {
            Finish();
            return;
        }

        UpdateSubtitles(_video.time);
    }

    private void UpdateSubtitles(double time)
    {
        var sb = new StringBuilder();
        foreach (var line in _lines)
        {
            if (time >= line.start && time < line.end)
            {
                sb.AppendLine(Localize(line.key));
            }
        }

        string text = sb.ToString();
        if (text != _shownText)
        {
            _shownText = text;
            _subtitleText.text = text;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_subtitlePanel);
        }

        _subtitleGroup.alpha = _subtitlesVisible && text.Length > 0 ? 1f : 0f;
    }

    private void Finish()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        ResumeWorldCameras();
        StartCoroutine(FadeOutAndClose());
    }

    private bool _inGameScreen;

    private static Transform GameScreensParent()
    {
        try
        {
            if (!MonoBehaviourSingleton<EFT.UI.MenuUI>.Instantiated)
            {
                return null;
            }

            EFT.UI.MenuUI menu = MonoBehaviourSingleton<EFT.UI.MenuUI>.Instance;
            Transform parent = menu.MatchmakerTimeHasCome != null ? menu.MatchmakerTimeHasCome.transform.parent : null;
            if (parent == null || !parent.gameObject.activeInHierarchy)
            {
                return null;
            }

            Canvas root = parent.GetComponentInParent<Canvas>();
            if (root == null || !root.isActiveAndEnabled)
            {
                return null;
            }

            return parent;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void CloneVersionLabel()
    {
        if (_inGameScreen)
        {
            return;
        }

        try
        {
            if (!MonoBehaviourSingleton<EFT.UI.PreloaderUI>.Instantiated)
            {
                return;
            }

            EFT.UI.LocalizedText label = MonoBehaviourSingleton<EFT.UI.PreloaderUI>.Instance._alphaVersionLabel;
            if (label == null || !label.gameObject.activeInHierarchy)
            {
                return;
            }

            GameObject copy = Instantiate(label.gameObject, transform, false);
            copy.name = "VersionLabel";
            copy.transform.SetAsLastSibling();
            foreach (Graphic graphic in copy.GetComponentsInChildren<Graphic>(true))
            {
                graphic.raycastTarget = false;
            }
        }
        catch (Exception)
        {
        }
    }

    private readonly List<Camera> _pausedCameras = new List<Camera>();

    private void PauseWorldCameras()
    {
        try
        {
            foreach (Camera camera in Camera.allCameras)
            {
                if (camera != null && camera.enabled && camera.targetTexture == null)
                {
                    camera.enabled = false;
                    _pausedCameras.Add(camera);
                }
            }
        }
        catch (Exception error)
        {
            Log.Warning("Intro", "could not pause world cameras: " + error.Message);
        }
    }

    private void ResumeWorldCameras()
    {
        foreach (Camera camera in _pausedCameras)
        {
            if (camera != null)
            {
                camera.enabled = true;
            }
        }

        _pausedCameras.Clear();
    }

    private void OnDestroy()
    {
        ResumeWorldCameras();
        RestoreMenuTaskBar();
    }

    private bool _taskBarHidden;

    private void HideMenuTaskBar()
    {
        try
        {
            if (MonoBehaviourSingleton<EFT.UI.PreloaderUI>.Instantiated)
            {
                EFT.UI.PreloaderUI preloader = MonoBehaviourSingleton<EFT.UI.PreloaderUI>.Instance;
                _taskBarHidden = preloader.MenuTaskBar != null && preloader.MenuTaskBar.gameObject.activeSelf;
                preloader.SetMenuTaskBarVisibility(false);
            }
        }
        catch (Exception)
        {
        }
    }

    private void RestoreMenuTaskBar()
    {
        if (!_taskBarHidden)
        {
            return;
        }

        _taskBarHidden = false;
        try
        {
            if (!Singleton<GameWorld>.Instantiated && MonoBehaviourSingleton<EFT.UI.PreloaderUI>.Instantiated)
            {
                MonoBehaviourSingleton<EFT.UI.PreloaderUI>.Instance.SetMenuTaskBarVisibility(true);
            }
        }
        catch (Exception)
        {
        }
    }

    private IEnumerator FadeOutAndClose()
    {
        for (float t = 1f; t > 0f; t -= Time.unscaledDeltaTime * 2f)
        {
            _root.alpha = t;
            if (_audio != null)
            {
                _audio.volume = t * _baseVolume;
            }

            yield return null;
        }

        _video?.Stop();
        _audio?.Stop();
        _texture?.Release();
        _current = null;
        try
        {
            _onComplete?.Invoke();
        }
        catch (Exception error)
        {
            Log.Error("Intro", "completion callback failed: " + error.Message);
        }

        Destroy(gameObject);
    }

    private void BuildUi()
    {
        Transform screens = GameScreensParent();
        if (screens != null)
        {
            _inGameScreen = true;
            var rect = gameObject.AddComponent<RectTransform>();
            rect.SetParent(screens, false);
            rect.SetAsLastSibling();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(1920f, 0f);
            gameObject.layer = screens.gameObject.layer;
            gameObject.AddComponent<Canvas>();
            gameObject.AddComponent<GraphicRaycaster>();
        }
        else
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;
            gameObject.AddComponent<GraphicRaycaster>();
        }

        _root = gameObject.AddComponent<CanvasGroup>();
        _root.blocksRaycasts = true;
        _root.interactable = false;

        Image black = Child("BlackBackground", transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 0f)).gameObject.AddComponent<Image>();
        black.color = Color.black;
        black.raycastTarget = true;

        RectTransform videoRect = Child("Video", transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1920, 1080));
        videoRect.gameObject.AddComponent<RawImage>().color = Color.white;
        _videoGroup = videoRect.gameObject.AddComponent<CanvasGroup>();
        _videoGroup.alpha = 0f;

        _video = gameObject.AddComponent<VideoPlayer>();
        _video.playOnAwake = false;
        _video.isLooping = false;
        _video.source = VideoSource.Url;
        _video.renderMode = VideoRenderMode.RenderTexture;
        _video.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;

        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.spatialBlend = 0f;
        _audio.priority = 0;
        _audio.outputAudioMixerGroup = null;
        _baseVolume = OverallVolume();
        _audio.volume = _baseVolume;

        RectTransform skip = Child("SkipPanel", transform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(35, 40), new Vector2(148.6f, 60));
        _skipGroup = skip.gameObject.AddComponent<CanvasGroup>();
        _skipGroup.alpha = 0f;
        Panel(skip);
        RectTransform ringBack = Child("ProgressBack", skip, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(36.5f, -30), new Vector2(53, 53));
        Image back = ringBack.gameObject.AddComponent<Image>();
        back.sprite = FindSprite("progress_spinner_fill");
        back.color = new Color(0.3019f, 0.3019f, 0.3019f, 1f);
        _skipFill = Child("ProgressFill", ringBack, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
        _skipFill.sprite = back.sprite;
        _skipFill.type = Image.Type.Filled;
        _skipFill.fillMethod = Image.FillMethod.Radial360;
        _skipFill.fillAmount = 0f;
        Label(skip, "Skip", 32, new Vector2(98.8f, -30), new Vector2(59.6f, 50), TextAlignmentOptions.Right);

        RectTransform toggle = Child("SubtitlesToggle", transform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(35, 110), new Vector2(274.67f, 60));
        _toggleGroup = toggle.gameObject.AddComponent<CanvasGroup>();
        var row = toggle.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(22, 20, 0, 0);
        row.spacing = 18;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = true;
        row.childControlHeight = false;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;
        var rowFit = toggle.gameObject.AddComponent<ContentSizeFitter>();
        rowFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        rowFit.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
        Panel(toggle);
        RectTransform key = Child("KeyView", toggle, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(28, 28));
        key.gameObject.AddComponent<LayoutElement>().preferredWidth = 28;
        Image keyImage = key.gameObject.AddComponent<Image>();
        keyImage.sprite = FindSprite("Key_Small");
        keyImage.type = Image.Type.Sliced;
        keyImage.pixelsPerUnitMultiplier = 2f;
        keyImage.color = keyImage.sprite != null ? Color.white : new Color(1, 1, 1, 0.2f);
        Label(key, Plugin.SubtitleToggleKey.Value.ToString(), 20, Vector2.zero, new Vector2(28, 28), TextAlignmentOptions.Center, centred: true);
        _toggleLabel = Label(toggle, "Subtitles: On", 32, Vector2.zero, new Vector2(186.67f, 50), TextAlignmentOptions.Right);
        var labelFit = _toggleLabel.gameObject.AddComponent<ContentSizeFitter>();
        labelFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        _subtitlePanel = Child("SubtitlesView", transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 120.52f), new Vector2(800, 39.81f));
        _subtitleGroup = _subtitlePanel.gameObject.AddComponent<CanvasGroup>();
        _subtitleGroup.alpha = 0f;
        var column = _subtitlePanel.gameObject.AddComponent<VerticalLayoutGroup>();
        column.padding = new RectOffset(0, 0, 0, 0);
        column.spacing = 0;
        column.childAlignment = TextAnchor.UpperLeft;
        column.childControlWidth = true;
        column.childControlHeight = true;
        column.childForceExpandWidth = true;
        column.childForceExpandHeight = false;
        var columnFit = _subtitlePanel.gameObject.AddComponent<ContentSizeFitter>();
        columnFit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        columnFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RectTransform bgRect = Child("Background", _subtitlePanel, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        bgRect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        bgRect.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.698f);
        Sprite border = FindSprite("border_generic");
        if (border != null)
        {
            RectTransform borderRect = Child("Border", _subtitlePanel, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            borderRect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Image frame = borderRect.gameObject.AddComponent<Image>();
            frame.sprite = border;
            frame.type = Image.Type.Sliced;
            frame.pixelsPerUnitMultiplier = 1.3333334f;
            frame.color = new Color(0.3216f, 0.349f, 0.3529f, 1f);
        }

        RectTransform textRect = Child("Text", _subtitlePanel, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(800, 39.81f));
        _subtitleText = textRect.gameObject.AddComponent<TextMeshProUGUI>();
        _subtitleText.font = FindFont();
        _subtitleText.fontSize = 18;
        _subtitleText.color = new Color(0.8431f, 0.851f, 0.851f, 1f);
        _subtitleText.alignment = TextAlignmentOptions.TopLeft;
        _subtitleText.enableWordWrapping = true;
        _subtitleText.overflowMode = TextOverflowModes.Overflow;
        _subtitleText.margin = new Vector4(18, 8, 18, 12);
        _subtitleText.lineSpacing = 1;
        _subtitleText.richText = true;
    }

    private static RectTransform Child(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private static void Panel(RectTransform parent)
    {
        Child("Back", parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.6f);
    }

    private static TextMeshProUGUI Label(RectTransform parent, string text, float size, Vector2 position, Vector2 box, TextAlignmentOptions alignment, bool centred = false)
    {
        RectTransform rect = centred
            ? Child("Text", parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero)
            : Child("Label", parent, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0.5f, 0.5f), position, box);
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = FindFont();
        label.fontSize = size;
        label.color = Color.white;
        label.alignment = alignment;
        label.enableWordWrapping = false;
        label.text = text;
        return label;
    }

    private static TMP_FontAsset _font;

    private static TMP_FontAsset FindFont()
    {
        if (_font != null)
        {
            return _font;
        }

        foreach (TMP_FontAsset font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            if (font.name == Font)
            {
                return _font = font;
            }
        }

        Log.Warning("Intro", "font '" + Font + "' not loaded; using TMP default");
        return _font = TMP_Settings.defaultFontAsset;
    }

    private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();

    private static Sprite FindSprite(string name)
    {
        if (Sprites.TryGetValue(name, out Sprite cached))
        {
            return cached;
        }

        foreach (Sprite sprite in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (sprite.name == name)
            {
                Sprites[name] = sprite;
                return sprite;
            }
        }

        Sprites[name] = null;
        return null;
    }
}
