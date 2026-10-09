using UnityEngine;
using UnityEngine.SceneManagement;

namespace TutorialBackport.Client;

internal static class AreaLightRevival
{
    private static Shader _proxy;
    private static Mesh _cube;

    public static void Install()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!scene.name.StartsWith("Sandbox_SL"))
        {
            return;
        }

        if (_proxy == null)
        {
            _proxy = Shader.Find("Hidden/AreaLight");
        }

        if (_cube == null)
        {
            _cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        }

        int total = 0, revived = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (AreaLight light in root.GetComponentsInChildren<AreaLight>(true))
            {
                total++;
                if (light.m_ProxyShader != null && light.m_Cube != null)
                {
                    if (TutorialAreaLightRenderer.Register(light))
                    {
                        revived++;
                    }

                    continue;
                }

                if (light.m_ProxyShader == null)
                {
                    light.m_ProxyShader = _proxy;
                }

                if (light.m_Cube == null)
                {
                    light.m_Cube = _cube;
                }

                if (light.m_ProxyShader == null || light.m_Cube == null)
                {
                    continue;
                }

                try
                {
                    light.Awake();
                    TutorialAreaLightRenderer.Register(light);
                    revived++;
                }
                catch (System.Exception error)
                {
                    Log.Warning("Scenes", $"area light '{light.name}' setup failed: {error.Message}");
                }
            }
        }
    }
}
