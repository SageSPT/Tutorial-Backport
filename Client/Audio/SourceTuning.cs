using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TutorialBackport.Client;

internal static class SourceTuning
{
    public static readonly Keyframe[] LogCurve =
    {
        K(0f, 1f, -44.94605f), K(0.00117f, 0.94737f, -38.24989f), K(0.00275f, 0.89474f, -28.33057f), K(0.00489f, 0.84211f, -20.98357f),
        K(0.00777f, 0.78947f, -15.54191f), K(0.01166f, 0.73684f, -11.51148f), K(0.01691f, 0.68421f, -8.52621f), K(0.02401f, 0.63158f, -6.31511f),
        K(0.03358f, 0.57895f, -4.67742f), K(0.04651f, 0.52632f, -3.46443f), K(0.06397f, 0.47368f, -2.566f), K(0.08753f, 0.42105f, -1.90056f),
        K(0.11935f, 0.36842f, -1.40769f), K(0.16231f, 0.31579f, -1.04264f), K(0.22031f, 0.26316f, -0.77225f), K(0.29862f, 0.21053f, -0.57198f),
        K(0.40434f, 0.15789f, -0.42365f), K(0.54708f, 0.10526f, -0.31379f), K(0.7398f, 0.05263f, -0.23241f), K(1f, 0f, -0.20228f)
    };

    public static readonly Keyframe[] IntroCurve = { K(0f, 1f, -9.58863f), K(0.15047f, 0.20543f, -0.82056f), K(1f, 0f, -0.03931f) };

    private static Keyframe K(float time, float value, float tangent) => new Keyframe(time, value, tangent, tangent);

    private sealed class Saved
    {
        public AudioSource Source;
        public AudioRolloffMode Mode;
        public AnimationCurve Curve;
        public float Spread;
        public float ReverbMix;
    }

    public static void Apply(BetterSource source, Keyframe[] keys, float spread, double restoreAtDsp)
    {
        if (source == null)
        {
            return;
        }

        AudioSource main = source.GetComponent<AudioSource>();
        var saved = new List<Saved>();
        foreach (AudioSource audio in source.GetComponentsInChildren<AudioSource>(true))
        {
            saved.Add(new Saved
            {
                Source = audio,
                Mode = audio.rolloffMode,
                Curve = audio.GetCustomCurve(AudioSourceCurveType.CustomRolloff),
                Spread = audio.spread,
                ReverbMix = audio.reverbZoneMix
            });
            audio.rolloffMode = AudioRolloffMode.Custom;
            audio.SetCustomCurve(AudioSourceCurveType.CustomRolloff, new AnimationCurve(keys));
            audio.spread = spread;
            if (audio == main)
            {
                audio.reverbZoneMix = 0f;
            }
        }

        Deadlines[source] = restoreAtDsp;
        NoteReader.Host().StartCoroutine(Restore(source, saved));
    }

    private static readonly Dictionary<BetterSource, double> Deadlines = new Dictionary<BetterSource, double>();

    public static void RestoreNow(BetterSource source)
    {
        if (source != null && Deadlines.ContainsKey(source))
        {
            Deadlines[source] = 0d;
        }
    }

    private static IEnumerator Restore(BetterSource source, List<Saved> saved)
    {
        while (source != null && Deadlines.TryGetValue(source, out double deadline) && AudioSettings.dspTime < deadline)
        {
            yield return null;
        }

        if (source != null)
        {
            Deadlines.Remove(source);
        }

        foreach (Saved s in saved)
        {
            if (s.Source == null)
            {
                continue;
            }

            s.Source.rolloffMode = s.Mode;
            if (s.Curve != null)
            {
                s.Source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, s.Curve);
            }

            s.Source.spread = s.Spread;
            s.Source.reverbZoneMix = s.ReverbMix;
        }
    }
}
