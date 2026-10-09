using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using EFT;
using EFT.GameTriggers;
using EFT.GlobalEvents;
using EFT.HealthSystem;
using Newtonsoft.Json;

namespace TutorialBackport.Client;

internal class TutorialHints : IDisposable
{
    public static TutorialHints Active;

    private const string ExaminationBotTrigger = "bot_in_examination_zone_enter";
    private readonly HintFile _data;
    private readonly Dictionary<string, int> _shown = new Dictionary<string, int>();
    private readonly Player _player;
    private readonly List<Action> _unsubscribe = new List<Action>();
    private readonly TutorialHintView _view;

    public static void Start(GameWorld world)
    {
        Active?.Dispose();
        try
        {
            string path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "data", "tutorial_hints.json");
            var data = JsonConvert.DeserializeObject<HintFile>(File.ReadAllText(path));
            Active = new TutorialHints(data, world.MainPlayer);
        }
        catch (Exception error)
        {
            Log.Error("Tutorial", "hint system not started: " + error.Message);
        }
    }

    private TutorialHints(HintFile data, Player player)
    {
        _data = data;
        _player = player;
        _view = TutorialHintView.Create();

        var handler = new Action<TriggerEvent>(OnTrigger);
        _unsubscribe.Add(GlobalEventsController.Instance.SubscribeOnEvent(handler));

        if (player?.ActiveHealthController != null)
        {
            Action<IHealthEffect> effect = OnEffectStarted;
            player.ActiveHealthController.EffectStartedEvent += effect;
            _unsubscribe.Add(() => player.ActiveHealthController.EffectStartedEvent -= effect);
        }
    }

    public void Dispose()
    {
        foreach (Action action in _unsubscribe)
        {
            try
            {
                action();
            }
            catch
            {
            }
        }

        _unsubscribe.Clear();
        _view?.Close();
        if (Active == this)
        {
            Active = null;
        }
    }

    private void OnTrigger(TriggerEvent triggerEvent)
    {
        int id = triggerEvent.TriggerId;
        bool fromPlayer = string.IsNullOrEmpty(triggerEvent.OriginProfileId) || triggerEvent.OriginProfileId == _player?.ProfileId;

        if (id == MirrorHash(ExaminationBotTrigger))
        {
            Show(Named("ExaminationHint"));
            return;
        }

        if (!fromPlayer)
        {
            return;
        }

        foreach (HintData hint in AllTriggerHints())
        {
            if (id == hint.ShowKey)
            {
                Show(hint);
            }
            else if (id == hint.CloseKey && hint.OnlyInTrigger)
            {
                _view.Close(hint);
            }
        }
    }

    private IEnumerable<HintData> AllTriggerHints()
    {
        foreach (HintData hint in _data.Triggers)
        {
            yield return hint;
        }

        foreach (string name in new[] { "HydrationHint", "FinalHint" })
        {
            HintData named = Named(name);
            if (named != null)
            {
                yield return named;
            }
        }
    }

    private void OnEffectStarted(IHealthEffect effect)
    {
        string type = effect?.GetType().Name ?? "";
        if (type.IndexOf("Bleeding", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            Show(Named("BleedHint"));
        }
        else if (type.IndexOf("Fracture", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            Show(Named("FractureHint"));
        }
    }

    public void OnPlayerMisfire()
    {
        Show(Named("MalfHint"));
    }

    public void OnMalfunctionFixed()
    {
        HintData hint = Named("MalfHint");
        if (hint != null)
        {
            _view.Close(hint);
        }
    }

    private HintData Named(string name)
    {
        return _data.Named != null && _data.Named.TryGetValue(name, out HintData hint) ? hint : null;
    }

    private void Show(HintData hint)
    {
        if (hint == null)
        {
            return;
        }

        _shown.TryGetValue(hint.Key, out int count);
        if (hint.Activations > 0 && count >= hint.Activations)
        {
            return;
        }

        _shown[hint.Key] = count + 1;
        _view.Show(hint);
    }

    public static int MirrorHash(string text)
    {
        unchecked
        {
            int num = 23;
            foreach (char c in text)
            {
                num = num * 31 + c;
            }

            return num;
        }
    }
}
