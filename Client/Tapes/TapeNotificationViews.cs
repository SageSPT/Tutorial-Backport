using System.Collections.Generic;
using System.IO;
using System.Reflection;
using EFT;
using EFT.Communications;
using EFT.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TutorialBackport.Client;

internal static class TapeNotificationViews
{
    public static readonly Color TitleColor = new Color(1f, 0.95686275f, 0.8235294f, 1f);
    private const float FontSize = 16f;
    private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();

    private static string Dir => Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "data", "tapes", "ui");

    public static BaseNotificationView Create(INotificationViewFactory factory, Notification notification)
    {
        if (!(factory is NotifierView notifier) || notifier._defaultNotificationTemplate == null)
        {
            return null;
        }

        BaseNotificationView view = UnityEngine.Object.Instantiate(notifier._defaultNotificationTemplate, notifier._container, false);
        notifier.SetupNotificationView(view);
        view.gameObject.AddComponent<TapeNotificationMarker>();
        return view;
    }

    public static void Finish(BaseNotificationView view, Notification notification)
    {
        view._text.fontSize = FontSize;
        view.Init(notification);
        view._text.color = Color.white;
        Sprite cassette = Load("Cassetes_Icon_Big_0");
        if (cassette != null && view._icon != null)
        {
            view._icon.sprite = cassette;
            view._icon.preserveAspect = true;
            view._icon.rectTransform.sizeDelta = new Vector2(30f, 24f);
        }
    }

    private static Transform TextGroup(BaseNotificationView view)
    {
        Transform row = view._text.transform.parent;
        if (row.name == "Text group")
        {
            return row;
        }

        var go = new GameObject("Text group", typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(row, false);
        rect.SetSiblingIndex(view._text.transform.GetSiblingIndex());
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(TextGroupWidth, rect.sizeDelta.y);
        go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var group = go.AddComponent<VerticalLayoutGroup>();
        group.spacing = -5f;
        group.childAlignment = TextAnchor.MiddleLeft;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = false;
        var element = go.AddComponent<LayoutElement>();
        element.preferredWidth = TextGroupWidth;
        element.minWidth = TextGroupWidth;
        view._text.transform.SetParent(rect, false);
        Style(view._text);
        return rect;
    }

    private const float TextGroupWidth = 440f;

    public static void Columns(BaseNotificationView view, bool progress)
    {
        Transform group = view._text.transform.parent;
        var row = group != null ? group.parent?.GetComponent<HorizontalLayoutGroup>() : null;
        if (row == null)
        {
            return;
        }

        float columnLeft = progress ? 0f : 10f;
        row.padding = new RectOffset((int)(columnLeft + 60f), 0, row.padding.top, row.padding.bottom);
        row.spacing = 0f;
        if (view._icon != null)
        {
            RectTransform icon = view._icon.rectTransform;
            icon.anchorMin = icon.anchorMax = progress ? new Vector2(0f, 1f) : new Vector2(0f, 0.5f);
            icon.pivot = new Vector2(0.5f, 0.5f);
            icon.anchoredPosition = new Vector2(columnLeft + 30f, progress ? -13.3f : 0f);
        }
    }

    private static void Style(TMP_Text text)
    {
        text.fontSize = FontSize;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Overflow;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.margin = new Vector4(0f, 4f, 0f, 5f);
        var element = text.GetComponent<LayoutElement>();
        if (element != null)
        {
            element.preferredWidth = -1f;
            element.flexibleWidth = -1f;
        }
    }

    public static List<TextMeshProUGUI> AddLines(BaseNotificationView view, (string text, Color color)[] lines, TMP_Text before)
    {
        var made = new List<TextMeshProUGUI>();
        Transform parent = TextGroup(view);
        int index = before != null ? before.transform.GetSiblingIndex() : view._text.transform.GetSiblingIndex() + 1;
        foreach (var (text, color) in lines)
        {
            GameObject copy = UnityEngine.Object.Instantiate(view._text.gameObject, parent, false);
            copy.name = "TapeLine";
            copy.transform.SetSiblingIndex(index++);
            var tmp = copy.GetComponent<TextMeshProUGUI>();
            if (tmp == null)
            {
                UnityEngine.Object.Destroy(copy);
                continue;
            }

            tmp.text = text;
            tmp.color = color;
            Style(tmp);
            made.Add(tmp);
        }

        return made;
    }

    public static void SetBackground(BaseNotificationView view, bool storyline)
    {
        if (view._background == null)
        {
            return;
        }

        view._background.color = new Color(0f, 0f, 0f, 0.788f);
        if (!storyline)
        {
            return;
        }

        Sprite sprite = Load("EFT_Notification_Storylain_Background_1");
        if (sprite == null)
        {
            return;
        }

        var go = new GameObject("bg", typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(view._background.transform, false);
        rect.SetAsFirstSibling();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        go.AddComponent<LayoutElement>().ignoreLayout = true;
        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.color = new Color(1f, 1f, 1f, 0.7843137f);
        image.raycastTarget = false;
    }

    public static string Localize(string key, string fallback)
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

    public static Sprite Load(string name) => Load(name, Vector4.zero);

    public static Sprite Load(string name, Vector4 border)
    {
        if (Sprites.TryGetValue(name, out Sprite cached))
        {
            return cached;
        }

        Sprite sprite = null;
        string path = Path.Combine(Dir, name + ".png");
        if (File.Exists(path))
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.LoadImage(File.ReadAllBytes(path));
            texture.name = name;
            sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            sprite.name = name;
        }

        Sprites[name] = sprite;
        return sprite;
    }
}
