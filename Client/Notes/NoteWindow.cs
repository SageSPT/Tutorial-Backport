using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Comfort.Common;
using EFT;
using EFT.InputSystem;
using EFT.InventoryLogic;
using EFT.UI;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TutorialBackport.Client;

internal sealed class NoteWindow : InputNode
{
    private const float FieldOfView = 22f;
    private const int MaxRenderSize = 1600;
    private static readonly Vector2 DefaultRotation = new Vector2(-20f, -5f);
    private static readonly Color PageTextColor = new Color(0.772549f, 0.7686275f, 0.7176471f, 1f);
    private static NoteWindow _open;

    private Item _item;
    private NoteReader.NoteTemplate _template;
    private float _uiScale = 1f;
    private bool _openedInRaid;
    private Camera _camera;
    private RenderTexture _texture;
    private RawImage _cameraImage;
    private AspectRatioFitter _ratio;
    private Transform _rig;
    private Transform _rotator;
    private GameObject _model;
    private CanvasGroup _localization;
    private Image _localizeImage;
    private Sprite _localizeOn, _localizeOff;
    private bool _localized;
    private TextMeshProUGUI _counter;
    private CanvasGroup _prevGroup, _nextGroup;
    private int _page, _pageCount = 1;
    private ScrollRectNoDrag _scroll;
    private readonly List<InputTree> _trees = new List<InputTree>();
    private GameWorld _world;
    private Player _player;
    private Vector2 _lastRotation, _newRotation, _dragStart;

    public static void Show(Item note, NoteReader.NoteTemplate template)
    {
        if (_open != null)
        {
            _open.Close();
        }

        Transform parent = Parent();
        var go = new GameObject("ReadNoteWindow", typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        var window = go.AddComponent<NoteWindow>();
        window.Build(note, template);
        _open = window;
    }

    private static bool InRaid()
    {
        if (!Singleton<GameWorld>.Instantiated)
        {
            return false;
        }

        Player main = Singleton<GameWorld>.Instance.MainPlayer;
        return main != null && main.HealthController != null && main.HealthController.IsAlive;
    }

    private static Transform Parent()
    {
        ItemUiContext context = ItemUiContext.Instance;
        if (!InRaid() && context != null && context._infoWindowsContainer != null
            && context._infoWindowsContainer.gameObject.activeInHierarchy)
        {
            return context._infoWindowsContainer;
        }

        var canvasGo = new GameObject("TutorialBackport.NoteCanvas", typeof(RectTransform));
        DontDestroyOnLoad(canvasGo);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;
        Canvas reference = context != null ? context.GetComponentInParent<Canvas>() : null;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        CanvasScaler refScaler = reference != null ? reference.rootCanvas.GetComponent<CanvasScaler>() : null;
        if (refScaler != null)
        {
            scaler.uiScaleMode = refScaler.uiScaleMode;
            scaler.referenceResolution = refScaler.referenceResolution;
            scaler.screenMatchMode = refScaler.screenMatchMode;
            scaler.matchWidthOrHeight = refScaler.matchWidthOrHeight;
            scaler.scaleFactor = refScaler.scaleFactor;
        }
        else
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
        }

        canvasGo.AddComponent<GraphicRaycaster>();
        return canvasGo.transform;
    }

    private void Build(Item note, NoteReader.NoteTemplate template)
    {
        _item = note;
        _template = template;
        Vector2Int windowSize = template.WindowSize;
        _openedInRaid = InRaid();
        _world = _openedInRaid ? Singleton<GameWorld>.Instance : null;
        _player = _world != null ? _world.MainPlayer : null;
        Canvas canvas = transform.parent != null ? transform.parent.GetComponentInParent<Canvas>() : null;
        _uiScale = canvas != null ? canvas.rootCanvas.scaleFactor : 1f;
        var root = (RectTransform)transform;
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.anchoredPosition = Vector2.zero;
        var vertical = gameObject.AddComponent<VerticalLayoutGroup>();
        vertical.padding = new RectOffset(5, 4, 26, 4);
        vertical.childAlignment = TextAnchor.UpperCenter;
        vertical.childControlWidth = vertical.childControlHeight = true;
        vertical.childForceExpandWidth = vertical.childForceExpandHeight = false;
        var fitter = gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Stretch(Img("Shadow", transform, Sprite("popup_shadow"), Color.white, Image.Type.Sliced), new Vector2(50f, 50f), true);
        Image windowBack = Img("Background", transform, null, Color.black, Image.Type.Simple);
        windowBack.raycastTarget = true;
        Stretch(windowBack, Vector2.zero, true);
        Image border = Img("Border", transform, Sprite("border_shadow"), Color.white, Image.Type.Sliced);
        border.fillCenter = false;
        Stretch(border, new Vector2(10f, 10f), true);
        BuildCaption();
        BuildContent(windowSize);
        BuildPaginator();
        BuildLocalizeButton();
        BuildPreview();
        gameObject.AddComponent<CanvasGroup>();

        foreach (InputTree tree in Resources.FindObjectsOfTypeAll<InputTree>().Where(t => t != null && t.gameObject.scene.IsValid()))
        {
            if (!tree.Contains(this))
            {
                tree.Add(this);
            }

            _trees.Add(tree);
        }

        StartCoroutine(ShowContents());
    }

    private void BuildCaption()
    {
        Image panel = Img("Caption Panel", transform, null, new Color(0.32941177f, 0.34509805f, 0.35686275f, 0.3019608f), Image.Type.Simple);
        var rect = panel.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -2f);
        rect.sizeDelta = new Vector2(-4f, 20f);
        Ignore(panel.gameObject);
        var row = panel.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(2, 2, 0, 0);
        row.spacing = 3f;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = row.childForceExpandHeight = false;

        Image icon = Img("Icon", panel.transform, Sprite("ReadNote_Icon"), Color.white, Image.Type.Simple);
        Layout(icon.gameObject, minWidth: 17f, preferredWidth: 18f, preferredHeight: 18f);
        TextMeshProUGUI caption = Text("Caption", panel.transform, _item.LocalizedName(), 14f, Color.white, TextAlignmentOptions.Left);
        Layout(caption.gameObject, minWidth: 30f, flexibleWidth: 1f);

        Image close = Img("Close Button", panel.transform, Sprite("close_button"), Color.white, Image.Type.Simple);
        Layout(close.gameObject, minWidth: 27f, preferredWidth: 27f, preferredHeight: 17f);
        close.raycastTarget = true;
        var button = close.gameObject.AddComponent<Button>();
        button.targetGraphic = close;
        button.onClick.AddListener(Close);
        Image x = Img("X", close.transform, Sprite("close_window_x"), Color.white, Image.Type.Simple);
        x.rectTransform.sizeDelta = new Vector2(9f, 11f);
    }

    private void BuildContent(Vector2Int windowSize)
    {
        var content = new GameObject("ContentPanel", typeof(RectTransform));
        content.transform.SetParent(transform, false);
        var row = content.AddComponent<HorizontalLayoutGroup>();
        row.childAlignment = TextAnchor.MiddleCenter;
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = row.childForceExpandHeight = false;
        Layout(content, minWidth: 550f, minHeight: 400f, preferredWidth: windowSize.x, preferredHeight: windowSize.y);
        var drag = content.AddComponent<DragArea>();
        drag.Owner = this;

        Image panelBack = Img("Background", content.transform, Sprite("info_window_back"), Color.white, Image.Type.Simple);
        panelBack.raycastTarget = true;
        Stretch(panelBack, Vector2.zero, true);
        var container = new GameObject("ImageContainer", typeof(RectTransform));
        container.transform.SetParent(content.transform, false);
        Stretch((RectTransform)container.transform, new Vector2(-80f, -80f));
        Ignore(container);
        var cameraImage = new GameObject("Camera Image", typeof(RectTransform));
        cameraImage.transform.SetParent(container.transform, false);
        _cameraImage = cameraImage.AddComponent<RawImage>();
        _cameraImage.raycastTarget = false;
        Stretch(_cameraImage.rectTransform, new Vector2(-150f, 0f));
        Ignore(cameraImage);
        _ratio = cameraImage.AddComponent<AspectRatioFitter>();
        _ratio.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        _ratio.aspectRatio = 1f;

        Image pages = Img("PagesContainer", content.transform, null, new Color(0f, 0f, 0f, 0.78431374f), Image.Type.Simple);
        pages.raycastTarget = true;
        Layout(pages.gameObject, flexibleWidth: 1f, flexibleHeight: 1f);
        var pagesRow = pages.gameObject.AddComponent<HorizontalLayoutGroup>();
        pagesRow.padding = new RectOffset(1, 1, 1, 1);
        pagesRow.childAlignment = TextAnchor.MiddleLeft;
        pagesRow.childControlWidth = pagesRow.childControlHeight = true;
        pagesRow.childForceExpandWidth = pagesRow.childForceExpandHeight = false;
        _localization = pages.gameObject.AddComponent<CanvasGroup>();

        Image viewport = Img("ViewPort", pages.transform, null, Color.white, Image.Type.Simple);
        Layout(viewport.gameObject, flexibleWidth: 1f, flexibleHeight: 1f);
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        var textHolder = new GameObject("Content", typeof(RectTransform));
        textHolder.transform.SetParent(viewport.transform, false);
        var textRect = (RectTransform)textHolder.transform;
        textRect.anchorMin = new Vector2(0f, 1f);
        textRect.anchorMax = new Vector2(1f, 1f);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.sizeDelta = Vector2.zero;
        textHolder.AddComponent<NonDrawingGraphic>().raycastTarget = true;
        Ignore(textHolder);
        var textLayout = textHolder.AddComponent<VerticalLayoutGroup>();
        textLayout.padding = new RectOffset(80, 80, 40, 40);
        textLayout.childAlignment = TextAnchor.MiddleLeft;
        textLayout.childControlWidth = textLayout.childControlHeight = true;
        textLayout.childForceExpandWidth = true;
        textLayout.childForceExpandHeight = false;
        textHolder.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        string key = _item.StringTemplateId + "_Note_Page1_Text1";
        TextMeshProUGUI page = Text("NotePageTemplate", textHolder.transform, key.Localized(), 16f, PageTextColor, TextAlignmentOptions.TopLeft);
        page.enableWordWrapping = true;
        page.richText = true;

        var scroll = pages.gameObject.AddComponent<ScrollRectNoDrag>();
        scroll.AutoZeroing = true;
        scroll.Alignment = TextAnchor.UpperCenter;
        _scroll = scroll;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = false;
        scroll.scrollSensitivity = 20f;
        scroll.viewport = viewport.rectTransform;
        scroll.content = textRect;
        Image bar = Img("Vertical Scrollbar", pages.transform, null, Color.black, Image.Type.Sliced);
        var barRect = bar.rectTransform;
        barRect.anchorMin = new Vector2(1f, 0f);
        barRect.anchorMax = new Vector2(1f, 1f);
        barRect.pivot = new Vector2(1f, 0.5f);
        barRect.anchoredPosition = new Vector2(-1f, 0f);
        barRect.sizeDelta = new Vector2(12f, -2f);
        Ignore(bar.gameObject);
        var sliding = new GameObject("Sliding Area", typeof(RectTransform));
        sliding.transform.SetParent(bar.transform, false);
        Stretch((RectTransform)sliding.transform, new Vector2(-8f, -8f));
        Image handle = Img("Handle", sliding.transform, null, new Color(0.7137255f, 0.75686283f, 0.78039223f, 1f), Image.Type.Sliced);
        Stretch(handle.rectTransform, Vector2.zero);
        handle.raycastTarget = true;
        var scrollbar = bar.gameObject.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.handleRect = handle.rectTransform;
        scrollbar.targetGraphic = handle;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
    }

    private void BuildPaginator()
    {
        var paginator = new GameObject("Paginator", typeof(RectTransform));
        paginator.transform.SetParent(transform, false);
        var column = paginator.AddComponent<VerticalLayoutGroup>();
        column.childAlignment = TextAnchor.MiddleCenter;
        column.childControlWidth = column.childControlHeight = true;
        column.childForceExpandWidth = column.childForceExpandHeight = false;
        Text("PaginatorHeader", paginator.transform, "UI/NoteReader/PageCounter".Localized(), 12f, new Color(0.29411766f, 0.29803923f, 0.27058825f, 1f), TextAlignmentOptions.Top);

        var row = new GameObject("PaginatorContent", typeof(RectTransform));
        row.transform.SetParent(paginator.transform, false);
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(20, 20, 0, 0);
        h.spacing = 20f;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        _prevGroup = PageButton(row.transform, "PrevButton", "PREVIOUS", () => ShowPage(_page - 1));
        _counter = Text("PageCounter", row.transform, "1 / 1", 24f, new Color(0.62352943f, 0.6156863f, 0.5647059f, 1f), TextAlignmentOptions.Center);
        _nextGroup = PageButton(row.transform, "NextButton", "NEXT", () => ShowPage(_page + 1));
    }

    private CanvasGroup PageButton(Transform parent, string name, string label, Action onClick)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(20, 20, 8, 8);
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        Layout(go, minWidth: 150f);
        Image background = Img("Background", go.transform, Sprite("EFT_UI_Buttons_Mask"), new Color(1f, 1f, 1f, 0f), Image.Type.Tiled);
        Stretch(background, Vector2.zero, true);
        background.raycastTarget = true;
        Image inside = Img("Background_inside", background.transform, Sprite("Button"), new Color(1f, 1f, 1f, 0f), Image.Type.Sliced);
        Stretch(inside.rectTransform, Vector2.zero);
        TextMeshProUGUI text = Text("Label", go.transform, label.Localized().ToUpperInvariant(), 24f, new Color(0.90588236f, 0.8980392f, 0.8352941f, 1f), TextAlignmentOptions.Center);
        TMP_FontAsset shadowed = FindFont("Jovanny Lemonad - Bender Shadowed SDF");
        if (shadowed != null)
        {
            text.font = shadowed;
        }

        var button = go.AddComponent<Button>();
        button.targetGraphic = inside;
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(1f, 1f, 1f, 0f);
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.15f);
        colors.pressedColor = new Color(1f, 1f, 1f, 0.3f);
        colors.disabledColor = new Color(1f, 1f, 1f, 0f);
        colors.colorMultiplier = 1f;
        button.colors = colors;
        button.onClick.AddListener(() => onClick());
        return go.AddComponent<CanvasGroup>();
    }

    private void BuildLocalizeButton()
    {
        _localizeOn = Sprite("ReadNoteLocalisations_Icon_1");
        _localizeOff = Sprite("ReadNoteLocalisations_Icon_0");
        _localizeImage = Img("LocalizeButton", transform, _localizeOn, Color.white, Image.Type.Simple);
        var rect = _localizeImage.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-10f, 10f);
        rect.sizeDelta = new Vector2(60f, 47f);
        _localizeImage.raycastTarget = true;
        Ignore(_localizeImage.gameObject);
        var button = _localizeImage.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(0.78431374f, 0.78431374f, 0.78431374f, 1f);
        colors.pressedColor = new Color(0.5686275f, 0.5686275f, 0.5686275f, 1f);
        colors.fadeDuration = 0.1f;
        button.colors = colors;
        button.onClick.AddListener(() => SetLocalized(!_localized));
    }

    private void BuildPreview()
    {
        int layer = LayersMaskController.WeaponPreview;
        var rig = new GameObject("TutorialBackport.NotePreview");
        _rig = rig.transform;
        _rig.position = new Vector3(0f, -2000f, 0f);
        _rig.localScale = Vector3.one * _uiScale;

        var content = new GameObject("PreviewContent").transform;
        content.SetParent(_rig, false);
        Quaternion lightRotation = Quaternion.Euler(10f, 329f, 5f);
        var lights = new List<Light>
        {
            AddLight(content, "MainSpot", LightType.Spot, new Vector3(0.58f, 0.3f, -0.924f), lightRotation, 3f, 3.5f, 46f, 0.7f),
            AddLight(content, "add", LightType.Point, new Vector3(-0.4f, 0.235f, -0.453f), lightRotation, 0.2f, 5f, 44f, 1f),
            AddLight(content, "add", LightType.Point, new Vector3(0.046f, 0.235f, -0.58f), lightRotation, 0.3f, 2f, 44f, 1f),
            AddLight(content, "add", LightType.Point, new Vector3(-0.124f, 0.235f, 0.2f), lightRotation, 0.3f, 5f, 44f, 1f)
        };

        Camera template = Resources.FindObjectsOfTypeAll<EFT.UI.WeaponModding.WeaponPreview>()
            .Select(preview => preview._cameraTemplate).FirstOrDefault(c => c != null);
        GameObject cameraGo;
        if (template != null)
        {
            cameraGo = Instantiate(template.gameObject, _rig, false);
            cameraGo.name = "PreviewCamera";
            _camera = cameraGo.GetComponent<Camera>();
        }
        else
        {
            cameraGo = new GameObject("PreviewCamera");
            cameraGo.transform.SetParent(_rig, false);
            _camera = cameraGo.AddComponent<Camera>();
            Log.Warning("Notes", "4.1 preview camera template not found - plain camera (no tonemapping / AO)");
        }

        PreviewFilter filter = cameraGo.GetComponent<PreviewFilter>();
        if (filter != null)
        {
            DestroyImmediate(filter);
        }

        cameraGo.transform.localPosition = new Vector3(0f, 0f, -2.572277f);
        cameraGo.transform.localRotation = Quaternion.identity;
        cameraGo.transform.localScale = Vector3.one;
        _camera.enabled = false;
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        _camera.fieldOfView = FieldOfView;
        _camera.nearClipPlane = 0.01f;
        _camera.farClipPlane = 20f;
        _camera.cullingMask = 1 << layer;
        _camera.allowHDR = true;
        _camera.allowMSAA = false;
        _camera.depth = 2f;
        CameraLightSwitcher switcher = cameraGo.GetComponent<CameraLightSwitcher>() ?? cameraGo.AddComponent<CameraLightSwitcher>();
        switcher.Lights = lights;
        cameraGo.SetActive(true);
        switcher.TurnLights(false);

        _rotator = new GameObject("Rotator").transform;
        _rotator.SetParent(content, false);
    }

    private static Light AddLight(Transform parent, string name, LightType type, Vector3 position, Quaternion rotation, float intensity, float range, float angle, float shadowStrength)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localRotation = rotation;
        var light = go.AddComponent<Light>();
        light.type = type;
        light.intensity = intensity;
        light.range = range;
        light.spotAngle = angle;
        light.color = Color.white;
        light.cullingMask = -1;
        light.renderMode = LightRenderMode.ForceVertex;
        light.shadows = LightShadows.Soft;
        light.shadowStrength = shadowStrength;
        return light;
    }

    private IEnumerator ShowContents()
    {
        yield return null;
        try
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            _uiScale = canvas != null ? canvas.rootCanvas.scaleFactor : _uiScale;
            _rig.localScale = Vector3.one * _uiScale;
            _model = Singleton<ObjectsFactory>.Instance.CreateCleanLootPrefab(_item, EFT.CameraControl.ECameraType.Default);
            _model.SetActive(true);
            var container = new GameObject("ItemContainer").transform;
            container.SetParent(_rotator, false);
            _model.transform.SetParent(container, false);
            _model.transform.localPosition = Vector3.zero;
            _model.transform.localRotation = _template.ModelPose;
            _model.transform.localScale = Vector3.one;
            SetLayer(_model.transform, LayersMaskController.WeaponPreview);

            float distance;
            Vector2 maxSize;
            if (_template.CameraDistance.HasValue && _template.FieldOfView.HasValue && _template.RenderSize.HasValue)
            {
                distance = _template.CameraDistance.Value * _uiScale / NoteReader.MeasuredScale;
                maxSize = _template.RenderSize.Value;
            }
            else
            {
                (distance, maxSize) = CameraDistance(_model);
                distance *= 1.0186f;
            }

            CreateRenderTexture(maxSize);
            AdjustCamera(distance, maxSize);
            if (_template.FieldOfView.HasValue)
            {
                _camera.fieldOfView = _template.FieldOfView.Value;
            }
        }
        catch (Exception error)
        {
            Log.Error("Notes", "note preview failed: " + error);
        }

        SetLocalized(false);
        ShowPage(0);
        _lastRotation = DefaultRotation;
        _newRotation = Vector2.zero;
        Rotate(_lastRotation);
        LoreContextMenu.MarkCompleted(_item);
    }

    private static Bounds Bounds(GameObject model)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
        if (renderers.Length == 0)
        {
            return new Bounds(model.transform.position, Vector3.one * 0.2f);
        }

        Bounds b = renderers[0].bounds;
        foreach (Renderer r in renderers.Skip(1))
        {
            b.Encapsulate(r.bounds);
        }

        return b;
    }

    private static (float, Vector2) CameraDistance(GameObject model)
    {
        Bounds b = Bounds(model);
        Vector3 pos = model.transform.position;
        Vector3 c = b.center - pos;
        Vector3 size = new Vector3(Mathf.Abs(b.extents.x) + Mathf.Abs(c.x), Mathf.Abs(b.extents.y) + Mathf.Abs(c.y), Mathf.Abs(b.extents.z) + Mathf.Abs(c.z)) * 2f;
        float diagonal = size.magnitude;
        float distance = diagonal * 0.5f / (float)Math.Tan(11.0 * Math.PI / 180.0);
        float yz = new Vector2(size.y, size.z).magnitude;
        var maxSize = new Vector2(diagonal / (float)Math.Tan(79.0 * Math.PI / 180.0), distance * yz / (distance - size.x * 0.5f));
        return (distance, maxSize);
    }

    private void CreateRenderTexture(Vector2 size)
    {
        int w = MaxRenderSize, h = MaxRenderSize;
        if (size.x < size.y)
        {
            w = Mathf.RoundToInt(size.x / size.y * MaxRenderSize);
        }
        else
        {
            h = Mathf.RoundToInt(size.y / size.x * MaxRenderSize);
        }

        _texture = new RenderTexture(Mathf.Max(1, w), Mathf.Max(1, h), 24, RenderTextureFormat.ARGB32) { name = "NoteReader RenderTexture" };
        _camera.targetTexture = _texture;
        _cameraImage.texture = _texture;
    }

    private void AdjustCamera(float distance, Vector2 maxSize)
    {
        _ratio.aspectRatio = maxSize.x / maxSize.y;
        _camera.transform.localPosition = new Vector3(0f, 0f, -distance);
        float fov = FieldOfView;
        if (maxSize.x > maxSize.y)
        {
            fov = Camera.HorizontalToVerticalFieldOfView(FieldOfView, maxSize.x / maxSize.y);
        }

        _camera.fieldOfView = fov;
        _camera.enabled = true;
    }

    private void SetLocalized(bool on)
    {
        _localized = on;
        _localization.alpha = on ? 1f : 0f;
        _localization.interactable = on;
        _localization.blocksRaycasts = on;
        if (_localizeImage != null)
        {
            _localizeImage.sprite = on ? _localizeOn : _localizeOff;
        }

        if (on)
        {
            ScrollToTop();
        }
    }

    private void ScrollToTop()
    {
        if (_scroll == null || _scroll.content == null)
        {
            return;
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(_scroll.content);
        _scroll.StopMovement();
        _scroll.verticalNormalizedPosition = 1f;
    }

    private void ShowPage(int page)
    {
        _page = Mathf.Clamp(page, 0, _pageCount - 1);
        string text = string.Format("{0}/{1}", _page + 1, _pageCount);
        _counter.text = Regex.Replace(text, @"(\D*)(\d+)(\D*)", "$1<mspace=0.6em>$2</mspace>$3");
        if (_prevGroup != null)
        {
            _prevGroup.alpha = _page > 0 ? 1f : 0.3f;
            _nextGroup.alpha = _page < _pageCount - 1 ? 1f : 0.3f;
        }
    }

    private void RemoveFromTrees()
    {
        RestoreEventSystem();
        foreach (InputTree tree in _trees)
        {
            if (tree != null)
            {
                tree.Remove(this);
            }
        }

        _trees.Clear();
    }

    private void Update()
    {
        bool raidGone = _openedInRaid && (_world == null || !Singleton<GameWorld>.Instantiated || Singleton<GameWorld>.Instance != _world
                                          || _player == null || !_player.HealthController.IsAlive);
        if (raidGone || _item == null || _item.CurrentAddress == null)
        {
            Close();
        }
    }

    internal void BeginDrag(Vector2 position)
    {
        _dragStart = position;
        _newRotation = Vector2.zero;
    }

    internal void Drag(Vector2 position)
    {
        _newRotation = (_dragStart - position) * (360f / Screen.height);
        Rotate(_lastRotation + _newRotation);
    }

    internal void EndDrag()
    {
        _lastRotation += _newRotation;
        _newRotation = Vector2.zero;
    }

    private void Rotate(Vector2 angle)
    {
        if (_rotator != null)
        {
            _rotator.localRotation = Quaternion.Euler(-angle.y, angle.x, 0f);
        }
    }

    public void Close()
    {
        if (_open == this)
        {
            _open = null;
        }

        RemoveFromTrees();
        if (_texture != null)
        {
            _texture.Release();
            Destroy(_texture);
        }

        if (_model != null)
        {
            Destroy(_model);
        }

        if (_rig != null)
        {
            Destroy(_rig.gameObject);
        }

        Transform canvas = transform.parent;
        Destroy(gameObject);
        if (canvas != null && canvas.name == "TutorialBackport.NoteCanvas")
        {
            Destroy(canvas.gameObject);
        }
    }

    private void OnDestroy()
    {
        RemoveFromTrees();
        if (_rig != null)
        {
            Destroy(_rig.gameObject);
        }
    }

    public override ETranslateResult TranslateCommand(ECommand command)
    {
        if (command == ECommand.Escape)
        {
            Close();
            return ETranslateResult.Block;
        }

        return ETranslateResult.BlockAll;
    }

    public override void TranslateAxes(ref float[] axes)
    {
        axes = null;
    }

    public override ECursorResult ShouldLockCursor()
    {
        return ECursorResult.ShowCursor;
    }

    private EventSystem _eventSystem;
    private bool _eventSystemWasEnabled = true;

    private void KeepEventSystem()
    {
        if (_eventSystem == null)
        {
            _eventSystem = Resources.FindObjectsOfTypeAll<EventSystem>().FirstOrDefault(e => e != null && e.gameObject.scene.IsValid());
            if (_eventSystem == null)
            {
                return;
            }

            _eventSystemWasEnabled = _eventSystem.enabled && _eventSystem.gameObject.activeSelf;
        }

        if (!_eventSystem.gameObject.activeSelf || !_eventSystem.enabled)
        {
            _eventSystem.gameObject.SetActive(true);
            _eventSystem.enabled = true;
        }

        if (EventSystem.current != _eventSystem)
        {
            EventSystem.current = _eventSystem;
        }
    }

    private void RestoreEventSystem()
    {
        if (_eventSystem != null && !_eventSystemWasEnabled)
        {
            _eventSystem.enabled = false;
        }

        _eventSystem = null;
    }

    private void LateUpdate()
    {
        KeepEventSystem();
        if (!Cursor.visible || Cursor.lockState == CursorLockMode.Locked)
        {
            Cursor.visible = true;
            Cursor.lockState = Screen.fullScreenMode == FullScreenMode.Windowed ? CursorLockMode.None : CursorLockMode.Confined;
        }
    }

    private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
    private static JObject _spriteMeta;

    internal static Sprite UiSprite(string name) => Sprite(name);

    private static Sprite Sprite(string name)
    {
        if (Sprites.TryGetValue(name, out Sprite cached))
        {
            return cached;
        }

        Sprite sprite = null;
        string dir = Path.Combine(Path.GetDirectoryName(typeof(NoteWindow).Assembly.Location), "data", "notes", "ui");
        string path = Path.Combine(dir, name + ".png");
        {
            if (File.Exists(path))
            {
                if (_spriteMeta == null)
                {
                    string metaPath = Path.Combine(dir, "sprites.json");
                    _spriteMeta = File.Exists(metaPath) ? JObject.Parse(File.ReadAllText(metaPath)) : new JObject();
                }

                float[] border = _spriteMeta[name]?["border"]?.ToObject<float[]>() ?? new float[4];
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = name };
                texture.LoadImage(File.ReadAllBytes(path));
                sprite = UnityEngine.Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f, 0,
                    SpriteMeshType.FullRect, new Vector4(border[0], border[1], border[2], border[3]));
                sprite.name = name;
            }
        }

        if (sprite == null)
        {
            sprite = UiHelpers.FindSprite(name);
        }

        Sprites[name] = sprite;
        return sprite;
    }

    private static TMP_FontAsset FindFont(string name) => Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f.name == name);

    private static Image Img(string name, Transform parent, Sprite sprite, Color color, Image.Type type)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.type = sprite != null ? type : Image.Type.Simple;
        image.raycastTarget = false;
        return image;
    }

    private static TextMeshProUGUI Text(string name, Transform parent, string value, float size, Color color, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.font = UiHelpers.FindFont();
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.enableWordWrapping = false;
        text.richText = true;
        text.raycastTarget = false;
        text.text = value;
        return text;
    }

    private static void Stretch(Image image, Vector2 sizeDelta, bool ignoreLayout)
    {
        Stretch(image.rectTransform, sizeDelta);
        if (ignoreLayout)
        {
            Ignore(image.gameObject);
        }
    }

    private static void Stretch(RectTransform rect, Vector2 sizeDelta)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = sizeDelta;
    }

    private static void Ignore(GameObject go)
    {
        LayoutElement e = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        e.ignoreLayout = true;
    }

    private static void Layout(GameObject go, float minWidth = -1f, float minHeight = -1f, float preferredWidth = -1f, float preferredHeight = -1f,
        float flexibleWidth = -1f, float flexibleHeight = -1f)
    {
        LayoutElement e = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        e.minWidth = minWidth;
        e.minHeight = minHeight;
        e.preferredWidth = preferredWidth;
        e.preferredHeight = preferredHeight;
        e.flexibleWidth = flexibleWidth;
        e.flexibleHeight = flexibleHeight;
    }

    private static void SetLayer(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        foreach (Transform child in t)
        {
            SetLayer(child, layer);
        }
    }

    internal sealed class DragArea : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public NoteWindow Owner;

        private void Awake()
        {
            var hit = new GameObject("RaycastTarget", typeof(RectTransform));
            hit.transform.SetParent(transform, false);
            hit.transform.SetAsFirstSibling();
            var image = hit.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
            Stretch(image.rectTransform, Vector2.zero);
            Ignore(hit);
        }

        public void OnBeginDrag(PointerEventData eventData) => Owner?.BeginDrag(eventData.position);

        public void OnDrag(PointerEventData eventData) => Owner?.Drag(eventData.position);

        public void OnEndDrag(PointerEventData eventData) => Owner?.EndDrag();
    }
}
