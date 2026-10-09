using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace TutorialBackport.Client;

internal sealed class TutorialAreaLightRenderer : MonoBehaviour
{
    private const string BufferName = "AreaLightRenderer";
    private const CameraEvent Event = CameraEvent.AfterImageEffectsOpaque;
    private const string ShaderName = "Hidden/TutorialBackport/AreaLight";

    private static readonly int TransformInvDiffuse = Shader.PropertyToID("_TransformInv_Diffuse");
    private static readonly int TransformInvSpecular = Shader.PropertyToID("_TransformInv_Specular");
    private static readonly int AmpDiffAmpSpecFresnel = Shader.PropertyToID("_AmpDiffAmpSpecFresnel");
    private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

    private static TutorialAreaLightRenderer _instance;
    private static AssetBundle _bundle;
    private static Shader _shader;
    private static readonly HashSet<AreaLight> Managed = new HashSet<AreaLight>();

    private readonly List<AreaLight> _lights = new List<AreaLight>();
    private readonly Dictionary<AreaLight, LightState> _state = new Dictionary<AreaLight, LightState>();
    private readonly LightList _outer = new LightList();
    private readonly LightList _inner = new LightList();
    private readonly Dictionary<Camera, (CommandBuffer buffer, int version)> _cameras = new Dictionary<Camera, (CommandBuffer, int)>();
    private Material _material;
    private Mesh _cube;
    private int _version = 1;

    private struct LightState
    {
        public bool Active;
        public bool Inside;
        public float Intensity;
        public Color Color;
        public Color SourceColor;
    }

    public static bool IsManaged(AreaLight light) => Managed.Contains(light);

    public static void Patch(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(AreaLight), nameof(AreaLight.PreCull)),
            prefix: new HarmonyMethod(typeof(TutorialAreaLightRenderer), nameof(BeforePreCull)));
        harmony.Patch(AccessTools.Method(typeof(AreaLight), nameof(AreaLight.UpdateSourceRendererParameters)),
            postfix: new HarmonyMethod(typeof(TutorialAreaLightRenderer), nameof(AfterUpdateSourceRendererParameters)));
    }

    private static bool BeforePreCull(AreaLight __instance) => !Managed.Contains(__instance);

    private static void AfterUpdateSourceRendererParameters(AreaLight __instance)
    {
        if (Managed.Contains(__instance))
        {
            ApplySourceColor(__instance);
        }
    }

    public static bool Register(AreaLight light)
    {
        if (!EnsureInstance())
        {
            return false;
        }

        if (Managed.Add(light))
        {
            _instance._lights.Add(light);
            light.Cleanup();
            ApplySourceColor(light);
        }

        return true;
    }

    public static void Shutdown()
    {
        if (_instance != null)
        {
            Destroy(_instance.gameObject);
        }

        Managed.Clear();
    }

    private static bool EnsureInstance()
    {
        if (_instance != null)
        {
            return true;
        }

        if (_shader == null)
        {
            string path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "bundles", "tutorialbackport_lightshaders.bundle");
            _bundle = _bundle ?? AssetBundle.LoadFromFile(path);
            foreach (Shader shader in _bundle != null ? _bundle.LoadAllAssets<Shader>() : Array.Empty<Shader>())
            {
                if (shader.name == ShaderName)
                {
                    _shader = shader;
                }
            }

            if (_shader == null || !_shader.isSupported)
            {
                Log.Error("Scenes", "area light shader unavailable (" + path + ") - tutorial area lights use 4.1's renderer");
                return false;
            }
        }

        var go = new GameObject("TutorialBackport_AreaLightRenderer");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<TutorialAreaLightRenderer>();
        return true;
    }

    private void Awake()
    {
        _material = new Material(_shader) { hideFlags = HideFlags.HideAndDontSave };
        _material.SetTexture(TransformInvDiffuse, AreaLightLUT.LoadLUT(AreaLightLUT.LUTType.TransformInv_DisneyDiffuse));
        _material.SetTexture(TransformInvSpecular, AreaLightLUT.LoadLUT(AreaLightLUT.LUTType.TransformInv_GGX));
        _material.SetTexture(AmpDiffAmpSpecFresnel, AreaLightLUT.LoadLUT(AreaLightLUT.LUTType.AmpDiffAmpSpecFresnel));
        _cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        Camera.onPreCull += OnCameraPreCull;
    }

    private void OnDestroy()
    {
        Camera.onPreCull -= OnCameraPreCull;
        foreach (var pair in _cameras)
        {
            if (pair.Key != null)
            {
                pair.Key.RemoveCommandBuffer(Event, pair.Value.buffer);
            }

            pair.Value.buffer.Release();
        }

        _cameras.Clear();
        _outer.Dispose();
        _inner.Dispose();
        if (_material != null)
        {
            Destroy(_material);
        }

        if (_instance == this)
        {
            _instance = null;
        }
    }

    private void LateUpdate()
    {
        Camera main = Camera.main;
        Vector3 eye = main != null ? main.transform.position : Vector3.zero;
        bool changed = false;
        for (int i = _lights.Count - 1; i >= 0; i--)
        {
            AreaLight light = _lights[i];
            if (light == null)
            {
                _lights.RemoveAt(i);
                changed = true;
                continue;
            }

            var now = new LightState
            {
                Active = light.isActiveAndEnabled && light.m_Intensity > 0f && light.Init(),
                Intensity = light.m_Intensity,
                Color = light.m_Color,
                SourceColor = light.m_SourceColor
            };
            if (now.Active)
            {
                light.CalculateDefaultValues();
                now.Inside = Aabb(AreaLightFields.ObjectToWorld(light)).Contains(eye);
            }

            if (!_state.TryGetValue(light, out LightState was) || was.Active != now.Active || was.Inside != now.Inside
                || !Mathf.Approximately(was.Intensity, now.Intensity) || was.Color != now.Color || was.SourceColor != now.SourceColor)
            {
                _state[light] = now;
                changed = true;
                if (was.Intensity != now.Intensity || was.SourceColor != now.SourceColor)
                {
                    ApplySourceColor(light);
                }
            }
        }

        if (!changed)
        {
            return;
        }

        _outer.Lights.Clear();
        _inner.Lights.Clear();
        foreach (AreaLight light in _lights)
        {
            LightState s = _state[light];
            if (s.Active)
            {
                (s.Inside ? _inner : _outer).Lights.Add(light);
            }
        }

        _outer.Upload();
        _inner.Upload();
        _version++;
    }

    private void OnCameraPreCull(Camera cam)
    {
        if (cam == null || !(cam.CompareTag("MainCamera") || cam.CompareTag("OpticCamera")))
        {
            return;
        }

        if (!_cameras.TryGetValue(cam, out var entry))
        {
            var buffer = new CommandBuffer { name = BufferName };
            cam.AddCommandBuffer(Event, buffer);
            cam.depthTextureMode |= DepthTextureMode.Depth;
            entry = (buffer, 0);
        }

        if (entry.version != _version)
        {
            Record(entry.buffer, cam);
            entry.version = _version;
        }

        _cameras[cam] = entry;
    }

    private void Record(CommandBuffer buffer, Camera cam)
    {
        buffer.Clear();
        buffer.DisableShaderKeyword("SCENEVIEWCAMERA");
        if (cam.CompareTag("OpticCamera"))
        {
            buffer.EnableShaderKeyword("OPTICCAMERA");
        }
        else
        {
            buffer.DisableShaderKeyword("OPTICCAMERA");
        }

        if (_outer.Count > 0)
        {
            buffer.DrawMeshInstancedProcedural(_cube, 0, _material, 0, _outer.Count, _outer.Props);
        }

        if (_inner.Count > 0)
        {
            buffer.DrawMeshInstancedProcedural(_cube, 0, _material, 1, _inner.Count, _inner.Props);
        }
    }

    public static Vector4 ColorInLinear(Color color, float intensity)
    {
        Color c = color * intensity;
        if (QualitySettings.activeColorSpace == ColorSpace.Gamma)
        {
            return c;
        }

        return new Vector4(c.r * c.r, c.g * c.g, c.b * c.b, 1f);
    }

    private static void ApplySourceColor(AreaLight light)
    {
        if (AreaLightFields.SourceRenderer(light) == null)
        {
            return;
        }

        if (AreaLightFields.Props(light) == null)
        {
            AreaLightFields.Props(light) = new MaterialPropertyBlock();
        }

        AreaLightFields.Props(light).SetVector(EmissionColor, ColorInLinear(light.m_SourceColor, light.m_Intensity));
        AreaLightFields.SourceRenderer(light).SetPropertyBlock(AreaLightFields.Props(light));
    }

    private static Bounds Aabb(Matrix4x4 m)
    {
        var bounds = new Bounds(m.MultiplyPoint3x4(new Vector3(-0.5f, -0.5f, -0.5f)), Vector3.zero);
        for (int i = 1; i < 8; i++)
        {
            bounds.Encapsulate(m.MultiplyPoint3x4(new Vector3((i & 1) != 0 ? 0.5f : -0.5f, (i & 2) != 0 ? 0.5f : -0.5f, (i & 4) != 0 ? 0.5f : -0.5f)));
        }

        return bounds;
    }

    private sealed class LightList : IDisposable
    {
        private static readonly int DataId = Shader.PropertyToID("_AreaLightData");
        private static readonly int ColorId = Shader.PropertyToID("_AreaLightColor");
        private static readonly int ObjectToWorldId = Shader.PropertyToID("_AreaLightObjectToWorld");

        public readonly List<AreaLight> Lights = new List<AreaLight>();
        public readonly MaterialPropertyBlock Props = new MaterialPropertyBlock();
        private GraphicsBuffer _data, _color, _objectToWorld;
        private TutorialAreaLightData[] _dataArray = new TutorialAreaLightData[0];
        private Vector4[] _colorArray = new Vector4[0];
        private Matrix4x4[] _o2wArray = new Matrix4x4[0];

        public int Count { get; private set; }

        public bool Upload()
        {
            Count = Lights.Count;
            bool realloc = false;
            int capacity = Mathf.Max(16, Mathf.NextPowerOfTwo(Count));
            if (_data == null || _data.count < Count)
            {
                Release();
                _data = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, Marshal.SizeOf<TutorialAreaLightData>());
                _color = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, 16);
                _objectToWorld = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, 64);
                _dataArray = new TutorialAreaLightData[capacity];
                _colorArray = new Vector4[capacity];
                _o2wArray = new Matrix4x4[capacity];
                realloc = true;
            }

            for (int i = 0; i < Count; i++)
            {
                AreaLight light = Lights[i];
                _dataArray[i] = TutorialAreaLightData.From(light);
                _colorArray[i] = ColorInLinear(light.m_Color, light.m_Intensity);
                _o2wArray[i] = AreaLightFields.ObjectToWorld(light);
            }

            if (Count > 0)
            {
                _data.SetData(_dataArray, 0, 0, Count);
                _color.SetData(_colorArray, 0, 0, Count);
                _objectToWorld.SetData(_o2wArray, 0, 0, Count);
            }

            Props.Clear();
            Props.SetBuffer(DataId, _data);
            Props.SetBuffer(ColorId, _color);
            Props.SetBuffer(ObjectToWorldId, _objectToWorld);
            return realloc;
        }

        public void Dispose() => Release();

        private void Release()
        {
            _data?.Release();
            _color?.Release();
            _objectToWorld?.Release();
            _data = _color = _objectToWorld = null;
        }
    }
}
