using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace TutorialBackport.Client;

internal static class MixerRemap
{
    private static Dictionary<string, string> _parent;
    private static readonly Dictionary<AudioMixerGroup, AudioMixerGroup> Map = new Dictionary<AudioMixerGroup, AudioMixerGroup>();
    private static readonly Dictionary<Type, FieldInfo[]> FieldsByType = new Dictionary<Type, FieldInfo[]>();
    private static Runner _runner;
    private static int _sources, _fields;

    public static void Install()
    {
        SceneManager.sceneLoaded += (scene, mode) =>
        {
            if (TutorialLauncher.InTutorialRaid && scene.name.StartsWith("Sandbox_SL", StringComparison.Ordinal))
            {
                RemapScene(scene);
                StartSweep();
                if (scene.name == "Sandbox_SL_Sound")
                {
                    AmbientFirefight.PrewarmBanks();
                }

                UiHelpers.FindFont();
            }
        };
    }

    public static void OnTutorialRaidStarted()
    {
        StartSweep();
    }

    private static AudioMixer Master => Singleton<BetterAudio>.Instantiated ? Singleton<BetterAudio>.Instance.Master : null;

    private static void RemapScene(Scene scene)
    {
        AudioMixer master = Master;
        if (master == null)
        {
            return;
        }

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (AudioSource source in root.GetComponentsInChildren<AudioSource>(true))
            {
                RemapSource(source, master);
            }

            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour != null)
                {
                    RemapObject(behaviour, master, 0);
                }
            }
        }
    }

    private static void RemapSource(AudioSource source, AudioMixer master)
    {
        AudioMixerGroup group = source != null ? source.outputAudioMixerGroup : null;
        if (group == null || group.audioMixer == master)
        {
            return;
        }

        AudioMixerGroup target = Target(group, master);
        if (target != null)
        {
            source.outputAudioMixerGroup = target;
            _sources++;
        }
    }

    private static void RemapObject(object owner, AudioMixer master, int depth)
    {
        foreach (FieldInfo field in Fields(owner.GetType()))
        {
            object value;
            try
            {
                value = field.GetValue(owner);
            }
            catch
            {
                continue;
            }

            if (value == null)
            {
                continue;
            }

            switch (value)
            {
                case AudioMixerGroup group:
                    if (group != null && group.audioMixer != master)
                    {
                        AudioMixerGroup target = Target(group, master);
                        if (target != null)
                        {
                            field.SetValue(owner, target);
                            _fields++;
                        }
                    }

                    break;
                case AudioMixer mixer:
                    if (mixer != null && mixer != master && mixer.name == master.name)
                    {
                        field.SetValue(owner, master);
                        _fields++;
                    }

                    break;
                case AudioMixerGroup[] groups:
                    for (int i = 0; i < groups.Length; i++)
                    {
                        if (groups[i] != null && groups[i].audioMixer != master)
                        {
                            AudioMixerGroup target = Target(groups[i], master);
                            if (target != null)
                            {
                                groups[i] = target;
                                _fields++;
                            }
                        }
                    }

                    break;
                case List<AudioMixerGroup> list:
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (list[i] != null && list[i].audioMixer != master)
                        {
                            AudioMixerGroup target = Target(list[i], master);
                            if (target != null)
                            {
                                list[i] = target;
                                _fields++;
                            }
                        }
                    }

                    break;
                case UnityEngine.Object _:
                    break;
                case IList items when depth < 2:
                    foreach (object item in items)
                    {
                        if (item != null && Nested(item.GetType()))
                        {
                            RemapObject(item, master, depth + 1);
                        }
                    }

                    break;
                default:
                    if (depth < 2 && Nested(value.GetType()))
                    {
                        RemapObject(value, master, depth + 1);
                    }

                    break;
            }
        }
    }

    private static bool Nested(Type type) => type.IsClass && !type.IsPrimitive && type != typeof(string) && type.IsSerializable && !typeof(UnityEngine.Object).IsAssignableFrom(type);

    private static FieldInfo[] Fields(Type type)
    {
        if (FieldsByType.TryGetValue(type, out FieldInfo[] cached))
        {
            return cached;
        }

        var list = new List<FieldInfo>();
        for (Type t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(object); t = t.BaseType)
        {
            foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                Type ft = f.FieldType;
                if (ft == typeof(AudioMixerGroup) || ft == typeof(AudioMixer) || ft == typeof(AudioMixerGroup[]) || ft == typeof(List<AudioMixerGroup>)
                    || (!ft.IsPrimitive && !ft.IsEnum && ft != typeof(string) && !typeof(UnityEngine.Object).IsAssignableFrom(ft) && !typeof(Delegate).IsAssignableFrom(ft)
                        && (ft.IsSerializable || typeof(IList).IsAssignableFrom(ft))))
                {
                    list.Add(f);
                }
            }
        }

        return FieldsByType[type] = list.ToArray();
    }

    private static AudioMixerGroup Target(AudioMixerGroup group, AudioMixer master)
    {
        if (Map.TryGetValue(group, out AudioMixerGroup cached))
        {
            return cached;
        }

        LoadTree();
        string name = group.name;
        AudioMixerGroup target = null;
        for (int guard = 0; name != null && guard < 12 && target == null; guard++)
        {
            target = master.FindMatchingGroups(name).FirstOrDefault(g => g.name == name);
            if (target == null)
            {
                name = _parent != null && _parent.TryGetValue(name, out string parent) ? parent : null;
            }
        }

        target = target ?? master.FindMatchingGroups("Master").FirstOrDefault();
        Map[group] = target;

        return target;
    }

    private static void LoadTree()
    {
        if (_parent != null)
        {
            return;
        }

        _parent = new Dictionary<string, string>();
        try
        {
            string path = Path.Combine(Path.GetDirectoryName(typeof(MixerRemap).Assembly.Location), "data", "mixer_groups.json");
            foreach (string p in JObject.Parse(File.ReadAllText(path))["groups"].ToObject<string[]>())
            {
                string[] parts = p.Split('/');
                if (parts.Length > 1 && !_parent.ContainsKey(parts[parts.Length - 1]))
                {
                    _parent[parts[parts.Length - 1]] = parts[parts.Length - 2];
                }
            }
        }
        catch (Exception error)
        {
            Log.Warning("Audio", "mixer group tree not loaded (" + error.Message + "); unknown groups go to the Master group");
        }
    }

    private static void StartSweep()
    {
        if (_runner == null)
        {
            var host = new GameObject("TutorialBackport.MixerRemap");
            UnityEngine.Object.DontDestroyOnLoad(host);
            _runner = host.AddComponent<Runner>();
        }

        if (!_runner.Running)
        {
            _runner.Running = true;
            _runner.StartCoroutine(Sweep());
        }
    }

    private static IEnumerator Sweep()
    {
        var wait = new WaitForSeconds(2f);
        while (TutorialLauncher.InTutorialRaid)
        {
            yield return wait;
            AudioMixer master = Master;
            if (master == null)
            {
                continue;
            }

            {
                int before = _sources;
                foreach (AudioSource source in UnityEngine.Object.FindObjectsOfType<AudioSource>())
                {
                    if (source.isPlaying)
                    {
                        RemapSource(source, master);
                    }
                }
            }
        }

        _runner.Running = false;
        Map.Clear();
        _sources = _fields = 0;
    }

    private sealed class Runner : MonoBehaviour
    {
        public bool Running;
    }
}
