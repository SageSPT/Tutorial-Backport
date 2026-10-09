using EFT.Communications;
using EFT.UI;
using UnityEngine;

namespace TutorialBackport.Client;

internal sealed class TapeCollectedNotification : Notification
{
    private readonly string _description;

    public TapeCollectedNotification(string tapeName, bool hasRecorder, float animatorSpeed)
    {
        _description = "\"" + tapeName + "\"";
        HasRecorder = hasRecorder;
        AnimatorSpeed = animatorSpeed;
        Duration = ENotificationDurationType.Default;
    }

    public bool HasRecorder { get; }

    public float AnimatorSpeed { get; }

    public override string Description => _description;

    public override BaseNotificationView CreateView(INotificationViewFactory viewFactory)
    {
        BaseNotificationView view = TapeNotificationViews.Create(viewFactory, this);
        if (view == null)
        {
            return base.CreateView(viewFactory);
        }

        string title = HasRecorder
            ? string.Format(TapeNotificationViews.Localize("tapes/tapeFoundNotify{0}", "Tape found / Play ({0})"), TapeRecorder.KeyLabel)
            : TapeNotificationViews.Localize("tapes/tapeFoundNotify", "Tape found");
        TapeNotificationViews.AddLines(view, new[] { (title, TapeNotificationViews.TitleColor) }, view._text);
        TapeNotificationViews.SetBackground(view, storyline: true);
        TapeNotificationViews.Finish(view, this);
        TapeNotificationViews.Columns(view, progress: false);
        view._background.color = new Color(0f, 0f, 0f, 0.788f);
        view._animator.SetFloat(Animator.StringToHash("Speed"), AnimatorSpeed);
        return view;
    }
}
