using System;
using System.Collections;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.InputSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TutorialBackport.Client;

internal class TutorialHintView : MonoBehaviour
{
    private const float TweenTime = 0.6f;
    private const float QueueTime = 4f;
    private const string Font = "Jovanny Lemonad - Bender Normal SDF";

    private readonly List<HintData> _queue = new List<HintData>();
    private CanvasGroup _group;
    private RectTransform _labels;
    private Image _timerFill;
    private GameObject _timer;
    private HintData _active;
    private float _activeUntil = -1f;
    private float _activeSince;
    private float _lastShown = -100f;

    public static TutorialHintView Create()
    {
        var go = new GameObject("TutorialBackport_TutorialHintView");
        DontDestroyOnLoad(go);
        return go.AddComponent<TutorialHintView>();
    }

    private void Awake()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 120;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 1f;

        RectTransform root = UiHelpers.Child("TutorialHintView", transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(0, -160), new Vector2(514, 30));
        _group = root.gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        var column = root.gameObject.AddComponent<VerticalLayoutGroup>();
        column.childAlignment = TextAnchor.UpperLeft;
        column.childControlWidth = true;
        column.childControlHeight = false;
        column.childForceExpandWidth = true;
        column.childForceExpandHeight = true;
        var fit = root.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.MinSize;
        root.gameObject.AddComponent<LayoutElement>().preferredWidth = 514;

        UiHelpers.Fill("Background", root, new Color(0, 0, 0, 0.698f), null, 1f);
        UiHelpers.Fill("Border", root, new Color(0.3216f, 0.349f, 0.3529f, 0.7529f), UiHelpers.FindSprite("Allscreens_Borders_Default"), 1.2f);

        _labels = UiHelpers.Child("Labels", root, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(514, 30));
        var labelsColumn = _labels.gameObject.AddComponent<VerticalLayoutGroup>();
        labelsColumn.padding = new RectOffset(26, 10, 15, 15);
        labelsColumn.spacing = 2;
        labelsColumn.childAlignment = TextAnchor.MiddleCenter;
        labelsColumn.childControlWidth = true;
        labelsColumn.childControlHeight = false;
        labelsColumn.childForceExpandWidth = true;
        labelsColumn.childForceExpandHeight = true;
        _labels.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.MinSize;

        RectTransform timer = UiHelpers.Child("Timer", root, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), Vector2.zero, new Vector2(-1.7f, 3));
        timer.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        timer.gameObject.AddComponent<Image>().color = new Color(0.1098f, 0.1176f, 0.1373f, 1f);
        _timerFill = UiHelpers.Child("TimerFill", timer, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
        _timerFill.sprite = UiHelpers.FindSprite("Pin_BG");
        _timerFill.type = Image.Type.Filled;
        _timerFill.fillMethod = Image.FillMethod.Horizontal;
        _timerFill.color = new Color(0.5333f, 0.549f, 0.5647f, 1f);
        _timer = timer.gameObject;
    }

    public void Show(HintData hint)
    {
        if (_active != null && _active.Key == hint.Key)
        {
            return;
        }

        if (_active != null && !hint.IgnoreQueue && !hint.IsImportant)
        {
            if (!_queue.Exists(h => h.Key == hint.Key))
            {
                _queue.Add(hint);
            }

            return;
        }

        StopAllCoroutines();
        StartCoroutine(SwapTo(hint));
    }

    public void Close(HintData hint)
    {
        _queue.RemoveAll(h => h.Key == hint.Key);
        if (_active != null && _active.Key == hint.Key)
        {
            StopAllCoroutines();
            StartCoroutine(HideActive());
        }
    }

    public void Close()
    {
        if (this != null)
        {
            Destroy(gameObject);
        }
    }

    private IEnumerator SwapTo(HintData hint)
    {
        if (_active != null)
        {
            yield return Fade(0f);
        }

        float wait = _lastShown + QueueTime - Time.time;
        if (_active == null && wait > 0f && !hint.IgnoreQueue)
        {
            yield return new WaitForSeconds(wait);
        }

        _active = hint;
        _activeSince = Time.time;
        _activeUntil = hint.Lifetime > 0 && hint.Lifetime < 9000 ? Time.time + hint.Lifetime : -1f;
        _timer.SetActive(_activeUntil > 0);
        Build(hint);
        _lastShown = Time.time;
        yield return Fade(1f);
    }

    private IEnumerator HideActive()
    {
        yield return Fade(0f);
        _active = null;
        if (_queue.Count > 0)
        {
            HintData next = _queue[0];
            _queue.RemoveAt(0);
            yield return SwapTo(next);
        }
    }

    private IEnumerator Fade(float target)
    {
        while (!Mathf.Approximately(_group.alpha, target))
        {
            _group.alpha = Mathf.MoveTowards(_group.alpha, target, Time.deltaTime / TweenTime);
            yield return null;
        }
    }

    private void Update()
    {
        if (_active == null || _activeUntil <= 0)
        {
            return;
        }

        float span = _activeUntil - _activeSince;
        _timerFill.fillAmount = Mathf.Clamp01((_activeUntil - Time.time) / Mathf.Max(0.01f, span));
        if (Time.time >= _activeUntil)
        {
            _activeUntil = -1f;
            StartCoroutine(HideActive());
        }
    }

    private static readonly Color TextGrey = new Color(0.78f, 0.78f, 0.78f, 1f);

    private void Build(HintData hint)
    {
        for (int i = _labels.childCount - 1; i >= 0; i--)
        {
            Destroy(_labels.GetChild(i).gameObject);
        }

        foreach (HintLine line in hint.Lines)
        {
            bool hotkey = string.Equals(line.Style, "Hotkey", StringComparison.OrdinalIgnoreCase);
            bool title = string.Equals(line.Style, "Title", StringComparison.OrdinalIgnoreCase);
            RectTransform row = UiHelpers.Child("Line", _labels, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(478, 27.8f));
            var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.padding = new RectOffset(0, 0, hotkey ? 4 : 2, hotkey ? 4 : 2);
            rowLayout.spacing = 6;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            row.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            float size = hotkey ? 19f : title ? 20f : 15f;
            TextMeshProUGUI text = UiHelpers.Text("Text (TMP)", row, Localize(line.Text), size, TextAlignmentOptions.Left);
            text.enableWordWrapping = true;
            text.color = hotkey || title ? Color.white : TextGrey;
            if (hotkey)
            {
                text.characterSpacing = 2f;
            }

            text.gameObject.AddComponent<LayoutElement>().flexibleWidth = hotkey ? 0f : 1f;

            if (line.ShowKeys && line.Keys != null)
            {
                foreach (string key in line.Keys)
                {
                    string name = KeyName(key);
                    if (!string.IsNullOrEmpty(name))
                    {
                        KeyView(row, name);
                    }
                }
            }
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_labels.parent);
    }

    private static void KeyView(RectTransform row, string label)
    {
        RectTransform key = UiHelpers.Child("KeyView", row, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22, 22));
        var layout = key.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(5, 5, 0, 0);
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        var element = key.gameObject.AddComponent<LayoutElement>();
        element.minWidth = 22f;
        element.minHeight = 22f;
        element.preferredHeight = 22f;
        element.flexibleWidth = 0f;
        element.flexibleHeight = 0f;

        UiHelpers.Fill("Fill", key, new Color(0f, 0f, 0f, 0.35f), null, 1f);
        Color edge = new Color(1f, 1f, 1f, 0.85f);
        Edge(key, "Top", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1.5f), edge);
        Edge(key, "Bottom", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1.5f), edge);
        Edge(key, "Left", new Vector2(0, 0), new Vector2(0, 1), new Vector2(1.5f, 0), edge);
        Edge(key, "Right", new Vector2(1, 0), new Vector2(1, 1), new Vector2(1.5f, 0), edge);

        TextMeshProUGUI text = UiHelpers.Text("Text", key, label, 14, TextAlignmentOptions.Center);
        text.overflowMode = TextOverflowModes.Overflow;
    }

    private static void Edge(RectTransform parent, string name, Vector2 min, Vector2 max, Vector2 size, Color color)
    {
        RectTransform rect = UiHelpers.Child(name, parent, min, max, new Vector2((min.x + max.x) / 2f, (min.y + max.y) / 2f), Vector2.zero, size);
        rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    private static string KeyName(string gameKey)
    {
        if (gameKey == "Recorder")
        {
            return TapeRecorder.KeyLabel;
        }

        try
        {
            if (Enum.TryParse(gameKey, out EGameKey key) && Singleton<EFT.Settings.SettingsManager>.Instantiated)
            {
                return Singleton<EFT.Settings.SettingsManager>.Instance.Control.Settings.GetKeyName(key);
            }
        }
        catch
        {
        }

        return null;
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
}
