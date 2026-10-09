using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TutorialBackport.Client;

internal static class UiHelpers
{
    private static TMP_FontAsset _font;
    private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();

    public static RectTransform Child(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
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

    public static void Fill(string name, RectTransform parent, Color color, Sprite sprite, float pixelsPerUnit)
    {
        RectTransform rect = Child(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        if (sprite != null)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = pixelsPerUnit;
        }
    }

    public static TextMeshProUGUI Text(string name, RectTransform parent, string value, float size, TextAlignmentOptions alignment)
    {
        RectTransform rect = Child(name, parent, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = FindFont();
        text.fontSize = size;
        text.color = Color.white;
        text.alignment = alignment;
        text.enableWordWrapping = false;
        text.richText = true;
        text.text = value;
        return text;
    }

    public static TMP_FontAsset FindFont()
    {
        if (_font != null)
        {
            return _font;
        }

        foreach (TMP_FontAsset font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            if (font.name == "Jovanny Lemonad - Bender Normal SDF")
            {
                return _font = font;
            }
        }

        return _font = TMP_Settings.defaultFontAsset;
    }

    public static Sprite FindSprite(string name)
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
