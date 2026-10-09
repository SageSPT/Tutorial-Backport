using System;
using EFT.Communications;
using EFT.UI;
using TMPro;
using UnityEngine;

namespace TutorialBackport.Client;

internal sealed class TapeProgressNotification : Notification
{
    private readonly string _description;
    private BaseNotificationView _view;
    private TextMeshProUGUI _timer;
    private string _lastTimer;

    public TapeProgressNotification(string tapeName, float duration)
    {
        _description = "\"" + tapeName + "\"";
        AudioClipDuration = duration;
        Duration = ENotificationDurationType.Infinite;
    }

    public float AudioClipDuration { get; }

    public override string Description => _description;

    public override BaseNotificationView CreateView(INotificationViewFactory viewFactory)
    {
        _view = TapeNotificationViews.Create(viewFactory, this);
        if (_view == null)
        {
            return base.CreateView(viewFactory);
        }

        var lines = TapeNotificationViews.AddLines(_view, new[]
        {
            (Timer(0f), Color.white),
            (string.Format(TapeNotificationViews.Localize("Tapes/StopKey{0}", "Stop: ({0})"), TapeRecorder.KeyLabel), Color.white),
        }, null);
        _timer = lines.Count > 0 ? lines[0] : null;
        TapeNotificationViews.SetBackground(_view, storyline: false);
        TapeNotificationViews.Finish(_view, this);
        TapeNotificationViews.Columns(_view, progress: true);
        _view._background.color = new Color(0f, 0f, 0f, 0.7843137f);
        return _view;
    }

    public void SetTime(float seconds)
    {
        if (_timer == null)
        {
            return;
        }

        string text = Timer(seconds);
        if (text != _lastTimer)
        {
            _lastTimer = text;
            _timer.text = text;
        }
    }

    public void Close()
    {
        if (_view != null && _view.gameObject.activeInHierarchy && !_view.IsHiding)
        {
            _view.HideNotification(false);
        }

        _view = null;
        _timer = null;
    }

    private string Timer(float seconds)
    {
        return string.Format(TapeNotificationViews.Localize("Tapes/Playback{0}{1}", "Playing: {0}/{1}"), Clock(seconds), Clock(AudioClipDuration));
    }

    private static string Clock(float seconds)
    {
        return TimeSpan.FromSeconds(Math.Max(0, Math.Floor(seconds))).ToString("mm\\:ss");
    }
}
